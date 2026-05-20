# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **bullet-hell MMO roguelike** built in **Godot 4.6.1 mono**. Combat is dodge-focused: enemies fire telegraphed parametric bullet patterns, players evade. Exported to Web/HTML5.

The codebase started as a Vampire Survivors-style template (auto-attacking, enemy waves, weapon progression) and is being rearchitected toward the above vision. Treat existing GDScript systems as a starting point, not the target design.

**Networking status:** SpacetimeDB multiplayer is partially working. Remote player sync with dead-reckoning, enemy state sync (trusted-host pattern), and an elevated headless server (privileged authority for enemy control) are all implemented. The bullet pattern system (parametric fire + client-side reconstruction + audit-log hit reporting) is next.

## Running and Developing

- **Engine:** Godot 4.6.1 mono (`Godot_v4.6.1-stable_mono_win64.exe`) — the mono build is required for C# and SpacetimeDB
- **Run game:** Press F5 in Godot editor (main scene: `VS-Like/Maps/woods_map.tscn`)
- **Export:** Web/HTML5 preset configured in `export_presets.cfg`
- **No build system** — Godot handles compilation internally
- **⚠️ NuGet warning:** Never use **Project → Tools → C# → Create C# solution** — it regenerates the `.csproj` from scratch and wipes the `SpacetimeDB.ClientSDK` package reference. The solution already exists. If the package reference is ever lost, run `dotnet add package SpacetimeDB.ClientSDK --version 2.1.0` from `g/` while Godot is closed.

## Architecture

### Collision Layers

| Layer | Purpose |
|-------|---------|
| 1 | World (static obstacles) |
| 2 | Player |
| 3 | Enemy |
| 4 | Drops |

### UI

Health bar, XP bar, weapons container (shows currently equipped items by slot), chest UI (item pick from elite drops), inventory panel (I key — equipment + inventory + stats), floating damage text.

## Scale Target

- **Player cap:** 2048–4096 players per server (powers of 2, not yet decided)
- **World design:** One large persistent map — no realm instancing. All players share the same tables. AoI via SpacetimeDB chunk subscriptions is what prevents clients from receiving every player's data.
- **Single-node is fine** at this scale — horizontal scaling escape hatch is spinning up additional server instances with shared persistence, not built-in clustering
- **Estimated server cost** at 4096 players: ~$300–500/month (financially sound at any reasonable monetization)

## SpacetimeDB Backend

SpacetimeDB is a relational database that is also a server: tables + reducers, clients subscribe to row subsets, scheduled reducers act as game ticks. Deployed on SpacetimeDB maincloud (registered via GitHub account).

**Database name:** `bullethell` on maincloud. Dashboard: `https://spacetimedb.com/@<username>/bullethell`

**Module language: Rust.** Server module lives at `server/spacetimedb/src/main/` (split across `tables.rs`, `methods.rs`, `reducers.rs`). Publish with `spacetime publish bullethell --module-path ./spacetimedb` from `server/`. After publishing, regenerate client bindings with `spacetime generate --lang csharp --out-dir ../client/SpacetimeDB/module_bindings --module-path ./spacetimedb` from `server/`.

**Key architectural decision: clients connect to SpacetimeDB only.** No separate Godot listen-server. All server authority runs inside SpacetimeDB Rust reducers.

### What is implemented (working)

**Tables (`server/spacetimedb/src/main/tables.rs`):**

| Table | Contents | Lifetime |
|-------|----------|----------|
| `Player` | Identity, username, is_admin | Persistent |
| `PlayerData` | level, xp, hp, max_hp | Persistent |
| `PlayerStats` | str/wis/dex/def/vit/spd — recomputed from level + gear | Persistent |
| `PlayerInventory` | `Vec<InventorySlot>` (kind + index + item_id) | Persistent |
| `ItemStats` | item_id (PK), all stat bonus/mult fields per stat | Persistent |
| `EnemyTemplate` | template_id (PK), display_name, max_hp, phases | Persistent |
| `PlayerPosition` | x, y, rotation, chunk_index (btree) | Session-only (deleted on disconnect) |
| `Enemy` | enemy_id (auto_inc PK), template_id, res_path, spawn_x/y/time, hp, phase, target (EnemyTarget enum), is_elite, immortal, chunk_index (btree) | Session-only |
| `LootDrop` | drop_id (auto_inc PK), item_id, x, y, chunk_index (btree), expires_at | Session-only |
| `BuildingTile` | tile_id (auto_inc PK), hex_q (btree), hex_r, chunk_q, chunk_r, local_index, chunk_index (btree), building_type, owner_id, hp, destroyed_at (nullable Timestamp) | Persistent |
| `MapConfig` | id=0 (PK singleton), chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius | Persistent |

**Reducers (`server/spacetimedb/src/main/reducers.rs`):**
- `client_connected` — creates Player/PlayerData/PlayerStats/PlayerInventory rows for new players; inserts PlayerPosition for all
- `client_disconnected` — deletes PlayerPosition
- `report_movement(x, y, rotation)` — validates speed, updates PlayerPosition with new coords and recomputed chunk_index
- `change_stats(...)` — updates PlayerStats (admin only)
- `update_inventory(slots)` — replaces PlayerInventory slots, calls recompute_stats
- `spawn_enemy(template_id, res_path, x, y, is_elite, immortal)` — inserts Enemy row (host calls)
- `report_enemy_hit(enemy_id, damage, weapon_id)` — reduces enemy HP; on death: awards XP, drops loot, deletes row
- `add_chunks(chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius?)` — generates all BuildingTile rows and writes MapConfig; admin only. CLI: `spacetime call bullethell add_chunks 7 10 10` (omit last arg for default outer radius)
- `claim_admin` — grants is_admin to caller; for CLI bootstrapping. Run once: `spacetime call bullethell claim_admin`
- `release_admin` — grants is_admin to caller; for CLI bootstrapping. Run once: `spacetime call bullethell release_admin`

**Not yet ported to Rust (existed in old C# module):**
- `UpdateEnemySnapshots` / `UpdateEnemyTarget` / `DespawnEnemy` — enemy dead-reckoning sync reducers; `StdbEnemySync.cs` still references these but they don't exist in the Rust module yet

**Godot client:**
- `StdbPlayerSync.cs` — autoload; spawns archer sprite for each remote player, dead-reckoning extrapolates position each frame, handles late-join catch-up via `SubscriptionReady` hook. Exposes `RemoteNodes` dict and `GetIdentityForNode()` for enemy targeting.
- `StdbEnemySync.cs` — autoload; updated for Rust Enemy table. On insert: spawns enemy node. On update/delete: drift correction and despawn.
- `SpacetimeDBManager.cs` — autoload singleton; manages connection + `FrameTick()`; emits `ConnectedToServer`, `DisconnectedFromServer`, `SubscriptionReady` signals. Exposes `GetMapConfig()` returning a GDScript-compatible Dictionary with `chunk_hex_radius`, `chunk_cols`, `chunk_rows`, `hex_outer_radius`.
- `Player.cs` — sends `ReportMovement` on WASD direction change

### What is NOT yet implemented

- `BulletPattern` table + bullet pattern system (Task 6)
- AoI chunk subscriptions (currently using `SubscribeToAllTables()`; torus-aware queries deferred with torus world)
- Enemy dead-reckoning reducers (`UpdateEnemySnapshots`, `UpdateEnemyTarget`, `DespawnEnemy`) not yet ported to Rust

### Godot client setup

- **`client/SpacetimeDB/SpacetimeDBManager.cs`** — autoload singleton; manages connection + `FrameTick()`; emits `ConnectedToServer`, `DisconnectedFromServer`, `SubscriptionReady` signals
- **`client/SpacetimeDB/module_bindings/`** — generated C# bindings; do not edit manually
- **`VampireSurvivorsTemplate.csproj`** (project root) — has `SpacetimeDB.ClientSDK 2.1.0` package reference
- Registered in `project.godot` autoload as `SpacetimeDBManager`

### AoI (not yet wired)

Session tables (`PlayerPosition`, `Enemy`, `LootDrop`) each carry a single `chunk_index: i64` column (btree indexed) computed from the entity's chunk axial coords `(cq, cr)` using the hex spiral formula (see below). AoI queries filter by this column:

```sql
SELECT * FROM player_position WHERE chunk_index IN (0, 1, 2, 3, 4, 5, 6, 7, ...)
```

The list is the 37 spiral indices of the chunks around the player's current chunk — computed by `surrounding_chunk_indices(cq, cr, chunk_cols, chunk_rows)` in `methods.rs`.

Re-subscribe when the local player crosses a chunk boundary. Torus wrapping means a player near an edge has an AoI window that may include chunks on the opposite side; `surrounding_chunk_indices` already clamps to map bounds, but the torus wrap logic is deferred with torus world.

**Chunk index formula (hex spiral) — must match on server and all clients:**

1. Convert axial (cq, cr) → cube: `x = cq`, `z = cr`, `y = −cq − cr`
2. Ring radius: `ρ = max(|x|, |y|, |z|)`
3. If ρ = 0: index = 0
4. Otherwise: `index = 3ρ(ρ−1)+1 + arm×ρ + step`, where arm/step are determined by which face of the ring cube (cq, cr) lies on (see `spiral_chunk_index` in `methods.rs` and `_hex_spiral_index` in `hex_grid_overlay.gd`)

This bijection is unique for all integer (cq, cr) including negative values, unlike the old linear formula `cr * chunk_cols + cq` which collides for coordinates summing to the same value.

### Enemy position sync strategy

**Implementation (elevated client / trusted-host pattern):** The elevated client is permanently responsible for calling enemy reducers — enemy spawning, position simulation, and bullet firing will not move to a server-side reducer. On each enemy spawn the elevated client calls `SpawnEnemy`; periodically it calls `UpdateEnemySnapshots` with a batch of dead-reckoning snapshots for all live enemies; when an enemy switches targets it immediately calls `UpdateEnemyTarget`; on death it calls `DespawnEnemy`. All clients (including the elevated client) receive enemy state exclusively via `EnemyState` table callbacks in `StdbEnemySync.cs` — the elevated client does not take a special code path on the receive side. Clients run deterministic local AI (`Enemy._Process` STDB branch) and `StdbEnemySync._Process` applies drift correction (lerp at 50-unit drift, hard snap at 200-unit drift).

**Snapshot interval:** Intended to be as long as possible. Because all clients simulate enemy AI deterministically from the `target` field, snapshots are only needed for drift correction — not to convey position on every tick. Behavior transitions (enemy switches target mode or acquires a specific player) trigger an immediate snapshot; periodic snapshots handle accumulated drift only. The exact interval is not yet decided.

**Enemy targeting — `EnemyTarget` enum (discriminated union / sentinel value pattern):**

```
Idle              — no target; enemy does not move
NearestPlayer     — client computes nearest player independently each frame
NearestCluster    — client computes centroid of nearest player cluster independently
SpecificPlayer(Identity) — all clients chase the same named player; no divergence
```

Clients `match` on this field to determine simulation behavior. The elevated client writes the variant when spawning or when a target transition occurs. `NearestPlayer` and `NearestCluster` are computed independently per client, which introduces potential divergence — this is bounded by the AoI targeting constraint below.

**AoI targeting constraint:** Enemies only target players/clusters within 1 chunk-rings of the enemy's current position. Clients do not simulate enemy behavior for enemies in the outer 3-ring buffer of their AoI window. This guarantees that any enemy a client is simulating has its target within that client's AoI subscription, keeping independent centroid/nearest-player computations consistent across clients.

### Bullet system (not yet implemented)

The elevated client writes a `BulletPattern` row (pattern_id, origin, spawn_time, params) when an enemy fires. Clients reconstruct bullet visuals locally using deterministic parametric math. No per-tick bullet position sync.

### Position reporting and validation

Clients send a movement snapshot to the server via `ReportMovement(float x, float y, float moveX, float moveY, float speed)` **only when their input direction changes** — not every frame. The server currently:
1. Validates `speed` arg against `MaxSpeed = 400f` — rejects if exceeded
2. Normalises the direction vector server-side
3. Updates `PlayerPosition` with the new snapshot and recomputes chunk coords

Other clients receive the snapshot via subscription and extrapolate position forward using `pos = snapshot.Pos + snapshot.MoveDir * snapshot.Speed * elapsed`. When a new snapshot arrives, they smoothly lerp the visual node to the corrected position to absorb drift.

**When torus world is added:** step 1 should also wrap incoming coords with `Wrap()`, and step 2 should add a plausibility check: extrapolate the old snapshot to `ctx.Timestamp`, then reject if `TorusDist(extrapolated, incoming) > MaxSpeed * tolerance`.

## Hex Grid

Planned as a BitCraft-style dual coordinate system. Two grids coexist independently:

**Continuous grid** — player/enemy/bullet positions as `Vector2`. Unchanged from current. All physics, movement, and bullet hit detection operate here.

**Hex grid** — buildings and terrain as axial coordinates `(q, r)` integers. Pointy-top hex math:
```
x = R * sqrt(3) * (q + r/2)
y = R * 1.5 * r
```
Hex size `R` (`hex_outer_radius` in MapConfig, default 32.0) must be chosen once and never changed — it invalidates all stored coordinates.

### What is implemented

- **`BuildingTile` table** — persists all hex tiles generated by `add_chunks`. Stores small hex axial `(hex_q, hex_r)`, chunk axial `(chunk_q, chunk_r)`, `local_index` (hexmod offset within chunk), `chunk_index` (spiral, for AoI), `building_type` (empty = unoccupied), `owner_id`.
- **`MapConfig` singleton** — written by `add_chunks`, read by all reducers. Stores `chunk_hex_radius`, `chunk_cols`, `chunk_rows`, `hex_outer_radius`. Fallback constants `DEFAULT_CHUNK_HEX_RADIUS = 7` and `DEFAULT_HEX_OUTER_RADIUS = 32.0` used before the table is populated.
- **Hex grid overlay** — GDScript debug overlay at `client/VS-Like/Maps/HexGrid/hex_grid_overlay.gd`, added to `woods_map.tscn`. Draws hex outlines and three labels per hex: small hex `(q,r)`, chunk `(cq,cr)`, and chunk spiral index. Reads MapConfig at runtime via `SpacetimeDBManager.GetMapConfig()`. Redraws on camera move.
- **Server hex math** — `methods.rs` wraps the `hexx` crate: `world_to_hex`, `hex_to_world`, `small_to_big`, `hexmod`, `inv_hexmod`, `chunk_center_hex`, `world_to_chunk`, `spiral_chunk_index`, `surrounding_chunk_indices`.

### What is NOT yet implemented

- Building placement / mutation (building_type is always empty after generation)
- Tile claiming system (players must claim tiles before building; claimed zones are where enemies can raid)
- Enemy-vs-structure combat (elevated client calls `DamageBuilding` reducer; building HP tracked in `BuildingTile`)
- Client-side hex grid rendering (tiles, structures)
- Crafting, resource nodes, ownership, destruction
- AoI subscriptions for BuildingTile (hex chunks for building data separate from entity AoI)

### Architectural rules

- All collision math is in continuous `Vector2` space — never in hex coordinate space. Building positions are converted to world space via `hex_to_world` before any collision check. The hex grid and the spatial hash grid (for AoI/hit detection) are different things at different granularities. Don't conflate them.
- **Bullets can hit buildings.** The immunity zone / bullet-pass-through approach was abandoned. Enemy bullets interact with player structures; building damage is handled by the elevated client (which already owns enemy bullet trajectories) calling a `DamageBuilding` reducer. Other clients react to the building's HP changing via subscription — no client-side determinism required for building damage.
- **Building placement requires a claimed zone.** Players can only place buildings in tiles they have claimed. Enemies can enter and attack structures in claimed zones (raids are intentional). This sidesteps the mid-flight obstruction problem: bullet patterns are always fired in unclaimed territory, so buildings cannot appear mid-pattern in the area the audit reconstructs for player-evasion checks.
- `BuildingTile` needs a `destroyed_at` timestamp (nullable) so the batch audit can reconstruct which buildings existed during a given audit window.

## Target Architecture (not yet implemented)

The networking model is SpacetimeDB maincloud — see SpacetimeDB Backend section above for the full design. Notes below cover client-side concerns that apply regardless of backend.

### Entity Pooling
- Pre-allocate max enemy visual nodes and max bullet visual nodes on clients at scene load
- Avoids per-frame `Node.new()` / `queue_free()` and GC hitches at 2000+ bullets

### Client-Side Simulation
- Clients run deterministic enemy AI locally between 2Hz snapshots from SpacetimeDB
- Clients reconstruct bullet positions locally from `BulletPattern` spawn events using parametric math
- Both require the client and server to use identical algorithms and share the same inputs (player positions via subscription)

### Known Issues in Current Codebase (fix before shipping)
- **`SaveCharacterProgress` accepts arbitrary stats:** Clients can write any `RpgStats` values (STR/WIS/DEX/DEF/VIT/SPD) to `CharacterData` with no server validation. Since damage is computed from stats, this is a direct cheat vector. Fix: replace client-side level-up stat grants with a server-side `LevelUp` reducer called by the elevated client (or triggered by validated XP thresholds), so the server computes and writes stats directly rather than trusting the client report.
- **Client combat authority is intentional:** Clients self-report both incoming bullet hits and outgoing damage to enemies with no live server validation. This is by design — see Anti-Cheat System section. Validation happens in the offline batch audit, not at call time.
- **Position validation:** `ReportMovement` currently only validates speed. Full plausibility check (extrapolate old snapshot, reject if incoming position is implausibly far) is deferred until torus world is implemented.
- **Late-join race condition:** ✅ Fixed — `StdbPlayerSync` now hooks `SubscriptionReady` to catch players who were online before the late-joiner's subscription was applied.
- **Enemy out-of-range despawn leaves stale STDB rows:** `Enemy._Process` STDB branch calls `QueueFree()` directly when `_GetStdbNearestDistance() >= 600`. This removes the local node but does not call `DespawnEnemy` reducer, leaving the row in the DB. Non-host clients will re-spawn the enemy on next `OnInsert` (won't fire) — the row just sits there until the next session reset. Fix: emit `RequestDespawn` from `_Process` on the same condition, or add a host-only out-of-range check in `Spawner._OnStdbSnapshotTimer`.

## Anti-Cheat System

### Authority model

**Stats:** Server authority always. `RpgStats` (STR/WIS/DEX/DEF/VIT/SPD) are written only by the elevated client or admin reducers — never accepted from regular clients.

**Combat:** Client authority by default. Clients self-report both getting hit bullets and landing hits on enemies. The server calculates the damage from those hit reports, but the server relies on the clients to report those hits in the first place. Because this is PvE, combat cheating only harms the cheater — delayed detection and banning is acceptable. This is intentional; it means zero server overhead for honest players and gives a lever to tighten enforcement if abuse becomes a real problem.

**Economy:** Server authority always. Drops, trades, purchases, and inventory mutations are validated server-side. Economy cheating has cross-player impact so it is never deferred to auditing.

### Swarm anti-cheat

Honest clients will be able to tell the server when another player is cheating. For example, for bullet collisions, players will be performing those bullet collisions for all other players within their AoI, and those collisions will be reported to the server. If a player cheats by not reporting that they were hit, other players will still tell the server that that player was hit by a bullet and the cheater will be suspected of cheating and the cheating player's `suspicion_score` will increase.

### Movement history logging

SpacetimeDB advises against high-frequency logging for all players. Position history is therefore only written to a `PlayerPositionHistory` table for players whose `suspicion_score` is above a threshold. Below the threshold, no extra rows are written — `PlayerPosition` alone is sufficient for live gameplay.

`PlayerPositionHistory` stores timestamped `ReportMovement` snapshots with a rolling TTL (e.g. 48 hours). `BulletPattern` rows (Task 6) are retained for the same window and are sufficient to reconstruct all bullet positions deterministically.

### Batch audits

Audits run non-live, once per day, via an external script (not a STDB reducer). The script:
1. Reads the SpacetimeDB transaction log / `PlayerPositionHistory` table for sampled players
2. Reconstructs player positions over the audit window using dead-reckoning from stored snapshots
3. Reconstructs bullet positions using the parametric math from `BulletPattern` rows
4. Checks for statistically implausible bullet evasion (player position inside bullet radius with no damage taken) — bullet patterns are fired in unclaimed territory, so building state is not needed for this check
5. Checks for statistically implausible hit reports on enemies (damage claimed exceeds weapon stats, or player was not in range)
6. Adjusts `suspicion_score` up or down based on findings

Note: buildings will need to be in the transaction to correctly simulate the bullet positions

Audits cover both directions: **bullet hit evasion** (invincibility cheats) and **outgoing damage inflation** (damage/hit-rate cheats against enemies).

### Wave banning

Bans from audit findings are not applied immediately. They are queued and released in periodic waves. This prevents cheaters from correlating the timing of a ban to a specific action, which would let them reverse-engineer the detection method.

## Implementation Roadmap

### Task 6 — Bullet pattern system

**Goal:** Enemies fire parametric bullet patterns; clients reconstruct bullet visuals locally; hit detection is client-reported with batch audit validation.

**Authority note:** Per the anti-cheat design, clients have authority over combat. Clients self-report bullet hits (damage taken) and hits on enemies. The server does not validate hits in real time. Instead, `BulletPattern` rows are retained as the audit record — the batch audit script uses them to reconstruct bullet positions offline and cross-check client reports. There is no live `BulletHitCheck` reducer.

**Files to edit:** `server/spacetimedb/src/main/tables.rs`, `server/spacetimedb/src/main/reducers.rs` + new client file

**Read first:**
- `client/VS-Like/Bullets/BulletManager.cs` and `client/VS-Like/Bullets/BulletVisual/BulletVisual.cs` — the existing local bullet system. The visual reconstruction logic replaces this.

**What to do:**
- Add `BulletPattern` table: `PatternId` (PK, AutoInc), `OriginX`, `OriginY`, `SpawnTime` (Timestamp), `PatternType` (string), `Params` (string — serialise as JSON or delimited values), `ExpiresAt` (Timestamp — set to SpawnTime + pattern lifetime + audit retention window)
- In the elevated client's fire-rate logic (`Spawner` or equivalent), write a `BulletPattern` row when an enemy fires — no per-tick bullet position sync
- Clients reconstruct bullet positions each frame using deterministic parametric math: `position = f(PatternType, Params, currentTime - SpawnTime)`
- Add `ReportHit(patternId, damage)` reducer (client → server): client calls this when a bullet visually hits them; server updates `CharacterData` HP. No server-side validation at call time — audit happens offline.
- Add `ReportEnemyHit(enemyId, damage, weaponId)` reducer (client → server): client calls this when their weapon hits an enemy; server updates enemy HP. Economy-relevant (XP, drops) so outgoing damage is a target for audit too.
- Scheduled cleanup: delete `BulletPattern` rows past `ExpiresAt` to avoid unbounded table growth