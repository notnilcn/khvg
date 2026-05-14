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

**Database name:** `bmmo-0nzf1` on maincloud. Dashboard: `https://spacetimedb.com/@<username>/bmmo-0nzf1`

**Module language: C#.** Server module lives at `bmmo/spacetimedb/Lib.cs`. Publish with `spacetime publish` from `bmmo/`. After publishing, regenerate client bindings with `spacetime generate --lang csharp --out-dir ./module_bindings --module-path ./spacetimedb` from `bmmo/`, then copy `bmmo/module_bindings/` to `g/SpacetimeDB/module_bindings/`.

**Key architectural decision: clients connect to SpacetimeDB only.** No separate Godot listen-server. All server authority runs inside SpacetimeDB C# reducers.

### What is implemented (working)

**Tables in `bmmo/spacetimedb/Lib.cs`:**

| Table | Contents | Lifetime |
|-------|----------|----------|
| `PlayerAccount` | Identity, username, is_admin, created_at | Persistent |
| `CharacterData` | Level, XP, HP, `RpgStats` (str/wis/dex/def/vit) | Persistent |
| `PlayerInventory` | Equipment slots + backpack (12) + consumables (8) as string lists of item IDs | Persistent |
| `PlayerPosition` | x, y, move_x, move_y, speed, event_time, chunk_x, chunk_y | Session-only (deleted on disconnect) |
| `EnemyState` | enemy_id (host-assigned ulong), x, y, move_x, move_y, speed, event_time, res_path, is_elite, immortal, hp, target_id | Session-only (cleared by host on scene load) |

**Reducers:**
- `ClientConnected` — creates account/character/inventory rows for new players; inserts `PlayerPosition` for all players
- `ClientDisconnected` — deletes `PlayerPosition`
- `ReportMovement(float x, float y, float moveX, float moveY, float speed)` — client sends dead-reckoning snapshot on input change; server validates speed and updates `PlayerPosition`
- `SetUsername`, `SaveCharacterProgress`, `UpdateInventory` — persistence helpers
- `SpawnEnemy(ulong enemyId, float x, float y, string resPath, bool isElite, bool immortal, int hp)` — host calls when spawning an enemy; `enemyId` is the host's local int ID cast to ulong
- `UpdateEnemySnapshots(List<EnemySnapshot> snapshots)` — host calls every 100 ms with batch of position/direction/speed for all live enemies
- `UpdateEnemyTarget(ulong enemyId, string targetId)` — host calls immediately when an enemy switches targets; `targetId` is the STDB identity string of the chased player
- `DespawnEnemy(ulong enemyId)` — host calls when an enemy dies

**Godot client:**
- `StdbPlayerSync.cs` — autoload; spawns archer sprite for each remote player, dead-reckoning extrapolates position each frame, handles late-join catch-up via `SubscriptionReady` hook. Exposes `RemoteNodes` dict and `GetIdentityForNode()` for enemy targeting.
- `StdbEnemySync.cs` — autoload; subscribes to `EnemyState`. On insert: spawns enemy node with `IsStdbSpawned = true` and seeds `StdbTargetId`. On update: stores dead-reckoning snapshot and updates `StdbTargetId`. On delete: frees the node. `_Process` lerps/snaps enemies toward the extrapolated snapshot position when drift exceeds threshold (50 units soft, 200 hard). `Spawner._Ready` calls `OnGameSceneLoaded(spawner)` for late-join catch-up.
- `Player.cs` — sends `ReportMovement` on WASD direction change

**Constants to keep in sync with Godot client:**
- `ChunkSize = 320f` — used for AoI subscription query bounds
- `WorldSize` — not yet set; deferred with torus world (see Torus World section)

### What is NOT yet implemented

- `BulletPattern` table + `BulletHitCheck` reducer (20Hz)
- Torus world topology + hex grid overlay (deferred — see Torus World and Hex Grid sections)
- AoI chunk subscriptions (currently using `SubscribeToAllTables()`; torus-aware queries deferred with torus world)

### Godot client setup

- **`g/SpacetimeDB/SpacetimeDBManager.cs`** — autoload singleton; manages connection + `FrameTick()`; emits `ConnectedToServer`, `DisconnectedFromServer`, `SubscriptionReady` signals
- **`g/SpacetimeDB/module_bindings/`** — generated C# bindings; do not edit manually
- **`g/VampireSurvivorsTemplate.csproj`** — has `SpacetimeDB.ClientSDK 2.1.0` package reference
- Registered in `project.godot` autoload as `SpacetimeDBManager`

### AoI (not yet wired)

Clients will subscribe to chunk-range SQL queries instead of all tables. Because the world is a torus, a player near an edge has an AoI window that wraps around — this requires splitting into up to two queries per axis (four total in the corner case):

```sql
-- non-wrapping case (most common):
SELECT * FROM player_position WHERE chunk_x BETWEEN @minX AND @maxX AND chunk_y BETWEEN @minY AND @maxY

-- wrapping case (player near x-edge): two queries unioned client-side:
SELECT * FROM player_position WHERE (chunk_x >= @wrapMin OR chunk_x <= @wrapMax) AND chunk_y BETWEEN @minY AND @maxY
```

Re-subscribe when the local player crosses a chunk boundary. `NumChunks = WorldSize / ChunkSize` — both must be powers of 2 so chunks divide evenly with no remainder.

### Enemy position sync strategy

**Implementation (elevated client / trusted-host pattern):** The elevated client is permanently responsible for calling enemy reducers — enemy spawning, position simulation, and bullet firing will not move to a server-side reducer. On each enemy spawn the elevated client calls `SpawnEnemy`; every 100 ms it calls `UpdateEnemySnapshots` with a batch of dead-reckoning snapshots for all live enemies; when an enemy switches targets it immediately calls `UpdateEnemyTarget`; on death it calls `DespawnEnemy`. All clients (including the elevated client) receive enemy state exclusively via `EnemyState` table callbacks in `StdbEnemySync.cs` — the elevated client does not take a special code path on the receive side. Clients run deterministic local AI (`Enemy._Process` STDB branch) and `StdbEnemySync._Process` applies drift correction (lerp at 50-unit drift, hard snap at 200-unit drift).

### Bullet system (not yet implemented)

The elevated client writes a `BulletPattern` row (pattern_id, origin, spawn_time, params) when an enemy fires. Clients reconstruct bullet visuals locally using deterministic parametric math. No per-tick bullet position sync.

### Local testing note

Multiple instances on the same machine share the same identity (same `.bmmo_token` file). The second connect attempt by the same identity will fail to insert a duplicate `PlayerPosition` row. True multiplayer testing requires different machines or manually clearing the token file between instances.

### Constants (choose once, never change)

- **`WorldSize`** — side length of the square torus in world units. Must be a power of 2. Recommended: `65536`. At player speed 100 u/s, one full lap takes 655 seconds (~11 min) of uninterrupted movement. View width is 640 units → ratio 102×, curvature is imperceptible.
- **`ChunkSize`** — must divide `WorldSize` exactly. `320` gives `65536 / 320 = 204` chunks per axis, `204² = 41616` total chunks.

Changing `WorldSize` after any position data exists in the DB invalidates all stored coordinates. Pick it once.

### Wrapping math

```csharp
// Wrap a coordinate into [0, WorldSize)
static float Wrap(float v) => ((v % WorldSize) + WorldSize) % WorldSize;

// Shortest signed delta between two positions on the torus (for lerp direction)
static float TorusDelta(float from, float to) {
    float d = Wrap(to - from);
    return d > WorldSize * 0.5f ? d - WorldSize : d;
}
```

`TorusDelta` is the key function: use it anywhere you compute a direction or interpolate between two positions so the path always takes the short way around.

### Lerp on a torus

Standard `Lerp(a, b, t)` breaks near the wrap boundary (e.g. lerping from x=65530 to x=6 goes the wrong way). Always lerp as:

```csharp
float newX = Wrap(from.X + TorusDelta(from.X, to.X) * t);
float newY = Wrap(from.Y + TorusDelta(from.Y, to.Y) * t);
```

Apply this in `StdbPlayerSync._Process` when interpolating remote player nodes.

### Position reporting and validation

Clients send a movement snapshot to the server via `ReportMovement(float x, float y, float moveX, float moveY, float speed)` **only when their input direction changes** — not every frame. The server currently:
1. Validates `speed` arg against `MaxSpeed = 400f` — rejects if exceeded
2. Normalises the direction vector server-side
3. Updates `PlayerPosition` with the new snapshot and recomputes chunk coords

Other clients receive the snapshot via subscription and extrapolate position forward using `pos = snapshot.Pos + snapshot.MoveDir * snapshot.Speed * elapsed`. When a new snapshot arrives, they smoothly lerp the visual node to the corrected position to absorb drift.

**When torus world is added:** step 1 should also wrap incoming coords with `Wrap()`, and step 2 should add a plausibility check: extrapolate the old snapshot to `ctx.Timestamp`, then reject if `TorusDist(extrapolated, incoming) > MaxSpeed * tolerance`.

No server-side movement tick for players — the server is a trusted relay, not a physics simulator. Enemy movement is handled by the elevated client (trusted-host pattern).

### Chunk indexing on torus

```csharp
int ChunkIndex(float v) => (int)(Wrap(v) / ChunkSize);
```

Chunk indices are always in `[0, NumChunks)`. AoI queries use modular range checks — see AoI section above.

## Hex Grid (future — not yet implemented)

Planned as a BitCraft-style dual coordinate system. Two grids coexist independently:

**Continuous grid** — player/enemy/bullet positions as `Vector2`. Unchanged from current. All physics, movement, and bullet hit detection operate here.

**Hex grid** — buildings and terrain as axial coordinates `(q, r)` integers. Pointy-top hex math:
```
x = R * sqrt(3) * (q + r/2)
y = R * 1.5 * r
```
Storage: `Dictionary<(int q, int r), CellData>` — O(1) lookup by cell. Hex size `R` must be chosen once and never changed (changing it invalidates all stored coordinates).

**Architectural rules to follow when implementing:**
- The two grids never mix in hit detection. Bullets stay in `Vector2`. Immunity zones (shelter mechanic) convert once at zone-enter/exit to set `player.IsInImmunityZone = true` — not checked per-bullet per-tick.
- The hex grid and the spatial hash grid (for AoI/hit detection) are different things at different granularities. Don't conflate them.
- If SpacetimeDB is ever added, clients subscribe to hex chunks for building data separately from entity AoI. Size hex chunks so one chunk ≈ one AoI radius.
- Bullets pass through all objects including player-built structures. Shelter is implemented as **immunity zones** (damage immunity flag), not physical collision — this preserves parametric bullet correctness.

**Scope note:** The hex math itself is trivial (two conversion functions, one afternoon). The systems on top — crafting, resource nodes, persistence, griefing prevention, destruction — are months of work. To future-proof without committing: pick hex size `R`, write the two conversion functions, stop there.

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

**Stats:** Server authority always. `RpgStats` (STR/WIS/DEX/DEF/VIT/SPD) are written only by the elevated client or admin reducers — never accepted from regular clients. Since damage is computed from stats, client-controlled stats are a direct cheat vector regardless of PvE/PvP mode.

**Combat:** Client authority by default. Clients self-report both taking damage from bullets and landing hits on enemies. Because this is PvE, combat cheating only harms the cheater — delayed detection and banning is acceptable. This is intentional; it means zero server overhead for honest players and gives a lever to tighten enforcement if abuse becomes a real problem.

**Economy:** Server authority always. Drops, trades, purchases, and inventory mutations are validated server-side. Economy cheating has cross-player impact so it is never deferred to auditing.

### Suspicion score

Each player has a `suspicion_score` stored in `PlayerAccount`. It drifts upward when audits find discrepancies and decays back toward zero over time. The score determines how likely a player is to be selected for an audit in the next audit batch, and whether their position history is being logged.

### Movement history logging

SpacetimeDB advises against high-frequency logging for all players. Position history is therefore only written to a `PlayerPositionHistory` table for players whose `suspicion_score` is above a threshold. Below the threshold, no extra rows are written — `PlayerPosition` alone is sufficient for live gameplay.

`PlayerPositionHistory` stores timestamped `ReportMovement` snapshots with a rolling TTL (e.g. 48 hours). `BulletPattern` rows (Task 6) are retained for the same window and are sufficient to reconstruct all bullet positions deterministically.

### Batch audits

Audits run non-live, once per day, via an external script (not a STDB reducer). The script:
1. Reads the SpacetimeDB transaction log / `PlayerPositionHistory` table for sampled players
2. Reconstructs player positions over the audit window using dead-reckoning from stored snapshots
3. Reconstructs bullet positions using the parametric math from `BulletPattern` rows
4. Checks for statistically implausible bullet evasion (player position inside bullet radius with no damage taken)
5. Checks for statistically implausible hit reports on enemies (damage claimed exceeds weapon stats, or player was not in range)
6. Adjusts `suspicion_score` up or down based on findings

Audits cover both directions: **bullet hit evasion** (invincibility cheats) and **outgoing damage inflation** (damage/hit-rate cheats against enemies).

### Wave banning

Bans from audit findings are not applied immediately. They are queued and released in periodic waves. This prevents cheaters from correlating the timing of a ban to a specific action, which would let them reverse-engineer the detection method.

## Implementation Roadmap

### Task 6 — Bullet pattern system

**Goal:** Enemies fire parametric bullet patterns; clients reconstruct bullet visuals locally; hit detection is client-reported with batch audit validation.

**Authority note:** Per the anti-cheat design, clients have authority over combat. Clients self-report bullet hits (damage taken) and hits on enemies. The server does not validate hits in real time. Instead, `BulletPattern` rows are retained as the audit record — the batch audit script uses them to reconstruct bullet positions offline and cross-check client reports. There is no live `BulletHitCheck` reducer.

**Files to edit:** `bmmo/spacetimedb/Lib.cs` + new client file

**Read first:**
- `g/VS-Like/Bullets/BulletManager.cs` and `g/VS-Like/Bullets/BulletVisual/BulletVisual.cs` — the existing local bullet system. The visual reconstruction logic replaces this.

**What to do:**
- Add `BulletPattern` table: `PatternId` (PK, AutoInc), `OriginX`, `OriginY`, `SpawnTime` (Timestamp), `PatternType` (string), `Params` (string — serialise as JSON or delimited values), `ExpiresAt` (Timestamp — set to SpawnTime + pattern lifetime + audit retention window)
- In the elevated client's fire-rate logic (`Spawner` or equivalent), write a `BulletPattern` row when an enemy fires — no per-tick bullet position sync
- Clients reconstruct bullet positions each frame using deterministic parametric math: `position = f(PatternType, Params, currentTime - SpawnTime)`
- Add `ReportHit(patternId, damage)` reducer (client → server): client calls this when a bullet visually hits them; server updates `CharacterData` HP. No server-side validation at call time — audit happens offline.
- Add `ReportEnemyHit(enemyId, damage, weaponId)` reducer (client → server): client calls this when their weapon hits an enemy; server updates enemy HP. Economy-relevant (XP, drops) so outgoing damage is a target for audit too.
- Scheduled cleanup: delete `BulletPattern` rows past `ExpiresAt` to avoid unbounded table growth