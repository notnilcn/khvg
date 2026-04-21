# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **bullet-hell MMO roguelike** built in **Godot 4.4.1**, targeting RotMG-scale multiplayer (85+ players, 850+ enemies, 2000+ bullets per realm). Combat is dodge-focused: enemies fire telegraphed parametric bullet patterns, players evade. Exported to Web/HTML5.

The codebase currently contains a Vampire Survivors-style template (auto-attacking, enemy waves, weapon progression) that is being rearchitected toward the above vision. Treat existing systems as a starting point, not the target design.

## Running and Developing

- **Engine:** Godot 4.6.1 mono (`Godot_v4.6.1-stable_mono_win64.exe`) — the mono build is required for C# and SpacetimeDB
- **Run game:** Press F5 in Godot editor (main scene: `VS-Like/Maps/woods_map.tscn`)
- **Export:** Web/HTML5 preset configured in `export_presets.cfg`
- **No build system** — Godot handles compilation internally
- **⚠️ NuGet warning:** Never use **Project → Tools → C# → Create C# solution** — it regenerates the `.csproj` from scratch and wipes the `SpacetimeDB.ClientSDK` package reference. The solution already exists. If the package reference is ever lost, run `dotnet add package SpacetimeDB.ClientSDK --version 2.1.0` from `g/` while Godot is closed.

## Architecture

### Core Systems

**Global Singleton** (`VS-Like/Game/Singleton/global.gd`)
Central manager holding the player registry (`players: Dictionary`) and the full item pool (`RES_ITEMS`). Key helpers: `pickup_item(res)` — equips directly if the relevant slot is empty, otherwise sends to inventory; `get_available_items()` — returns the full pool for chest rolls; `add_weapon()` kept as a legacy alias for `pickup_item()`.

**Player** (`VS-Like/Characters/Player/player.gd`)
Extends `CharacterBody2D`. Uses a `ResPlayer` custom resource for character data. Manages movement (WASD), equipment slots, inventory, RPG stats, and health. Death reloads the map.

- **RPG stats:** `strength`, `wisdom`, `dexterity`, `defense`, `vitality` (all `int`). Each level-up awards +1 to all stats and +5 max HP.
- **Equipment slots:** `equipped_weapon` (Weapon node), `equipped_ability` (Weapon node), `equipped_armor` (placeholder), `equipped_accessories: Array[Weapon]`. Slot count for accessories scales with Wisdom: 1 slot base, 2 at 500 WIS, 3 at 1000 WIS. `equip_item(res)` handles swapping — the displaced item goes to inventory.
- **Inventory:** `inventory: Array[ResWeapon]` (default 6 slots, max 12 via backpack). `consumables: Array[ResWeapon]` (default 2 slots, max 8).
- **Passive stat multipliers:** `extra_exp`, `extra_damage`, `extra_speed` — modified by equipped accessories in their `_ready()` / `remove_passive_effects()`.
- **Inventory UI:** press **I** to toggle. Created programmatically at runtime (`InventoryUI`); added to the HUD CanvasLayer.

**Enemy** (`VS-Like/Characters/Enemy/enemy.gd`)
Extends `Sprite2D` with physics via `PhysicsServer2D` directly (not CharacterBody2D) for performance. Auto-chases player, despawns when >600 units away. Elite enemies have 10× health, shader effect, and always drop chests.

**Spawner** (`VS-Like/Game/World/spawner.gd`)
Max 300 enemies at once. Unlocks new enemy types at 1/2/3-minute marks. Spawn interval decreases over time (1.0s → 0.75s → 0.5s → 0.35s). Supports circle-pattern spawns and elite spawns on timers.

Admin chat commands (press T; requires admin except `/stats` and `/inv`):

| Command | Description |
|---------|-------------|
| `/stats` | Print your RPG stats and HP to chat (no admin needed) |
| `/inv` | Print equipped items and inventory contents to chat (no admin needed) |
| `/chest [x y]` | Spawn an elite enemy (guaranteed chest drop) near you or at x,y |
| `/spawn [x y]` | Spawn an immortal dummy at x,y (default 0,0) |
| `/give <item> [peer]` | Give a weapon/item to yourself or another peer |
| `/remove <item> [peer]` | Remove a weapon/item from yourself or another peer |
| `/spawner on\|off` | Enable or disable the automatic enemy spawner |
| `/admin <peer>` | Grant admin (host only) |
| `/unadmin <peer>` | Revoke admin (host only) |
| `/setpass [pw]` | Set or clear the admin password (host only) |
| `/adminpass <pw>` | Claim admin via password (any player) |

**Item / Weapon System** (`VS-Like/Characters/Player/Utilities/Weapons/`)
All loot items use the single `ResItem` resource class (`res_item.gd`). Fields are grouped by category; unused fields default to 0 and are ignored at runtime. `ResWeapon` (`res_weapon.gd`) is a backward-compatibility shim that `extends ResItem`.

The `item_type` enum determines the equipment slot:

| `ItemType` | Value | Slot | Examples |
|-----------|-------|------|---------|
| `WEAPON` | 0 | Weapon | Axe, Arrow, Sine Bow |
| `ABILITY` | 1 | Ability | Bible |
| `ARMOR` | 2 | Armor | *(not yet implemented)* |
| `ACCESSORY` | 3 | Accessory (×1–3) | Boots, Parchment, Ring |

**Passive stat bonuses** for accessories/armor are stored directly in the `.tres` file (`bonus_speed_mult`, `bonus_exp_mult`, `bonus_damage_mult`, `bonus_defense`, `bonus_max_health`) and applied by the generic `PassiveItemWeapon` base class. Adding a new accessory requires only a `.tres` file — no new GDScript. Boots/Parchment/Ring each have a one-line `.gd` that just `extends PassiveItemWeapon`.

Weapons are obtained **only from chests** (dropped by elite enemies). Level-up no longer shows a weapon selection menu — it grants stats instead. The `WeaponSelection` scene has been removed.

**Drop System** (`VS-Like/Characters/Enemy/Utilities/Drop/`)
Three drop types: Points (XP), Food (heals), Chest (from elites). Chest UI shows up to 3 random items as clickable buttons; player picks **one** to pick up (or presses Done to skip). Picked-up items equip directly if the slot is empty, otherwise go to inventory.

**Inventory UI** (`VS-Like/Characters/Player/Utilities/UI/InventoryUI/inventory_ui.gd`)
Fully programmatic (no `.tscn`). Instantiated in `player._init_player()` and added to the HUD CanvasLayer. Shows: equipment row, inventory grid (12 slots, first 6 active by default), consumable row (8 slots, first 2 active), and stats panel. Click an equipped slot to unequip back to inventory; click an inventory slot to equip. Locked slots (beyond current backpack size / wisdom level) are tinted dark. Runs with `PROCESS_MODE_WHEN_PAUSED` and handles KEY_I itself so closing works even while the game is paused.

### Data-Driven Design

Game objects use **Custom Resources** (`.tres` files) for configuration:
- Characters: `res_archer.tres`, `res_dwarf.tres`
- Enemies: `res_zombie.tres`, `res_bat.tres`, `res_cyclops.tres`, `res_goblin.tres`
- Weapons: one `.tres` per weapon type

### Collision Layers

| Layer | Purpose |
|-------|---------|
| 1 | World (static obstacles) |
| 2 | Player |
| 3 | Enemy |
| 4 | Drops |

### UI

Health bar, XP bar, weapons container (shows currently equipped items by slot), chest UI (item pick from elite drops), inventory panel (I key — equipment + inventory + stats), floating damage text.

### Display

- Internal resolution: 640×360
- Window: 1280×720 (2× scale)
- Texture filter: Nearest neighbor (pixel-perfect)
- Renderer: GL Compatibility

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
| `PlayerPosition` | x, y, move_x, move_y, chunk_x, chunk_y | Session-only (deleted on disconnect) |
| `MoveTickSchedule` | Internal — drives 20Hz movement tick | Permanent |

**Reducers:**
- `Init` — creates `MoveTickSchedule` (runs once on first publish)
- `ClientConnected` — creates account/character/inventory rows for new players; inserts `PlayerPosition` for all players
- `ClientDisconnected` — deletes `PlayerPosition`
- `SetMoveInput(float moveX, float moveY)` — client calls on keypress/release to update direction
- `PlayerMoveTick` — scheduled 20Hz; moves all players from their stored input; updates chunk coords
- `SetUsername`, `SaveCharacterProgress`, `UpdateInventory` — persistence helpers

**Constants to keep in sync with Godot client:**
- `PlayerSpeed = 100f` — must match player speed in Godot
- `ChunkSize = 320f` — used for AoI subscription query bounds

### What is NOT yet implemented

- `Enemy` table + `EnemyAI` reducer (10Hz, 2Hz writes)
- `BulletPattern` table + `BulletHitCheck` reducer (20Hz)
- `Spawner` reducer
- Godot client: WASD → `SetMoveInput` wiring
- Godot client: `PlayerPosition.OnInsert/OnUpdate/OnDelete` → spawn/move/remove sprites
- AoI chunk subscriptions (currently using `SubscribeToAllTables()`)

### Godot client setup

- **`g/SpacetimeDB/SpacetimeDBManager.cs`** — autoload singleton; manages connection + `FrameTick()`; emits `ConnectedToServer`, `DisconnectedFromServer`, `SubscriptionReady` signals
- **`g/SpacetimeDB/module_bindings/`** — generated C# bindings; do not edit manually
- **`g/VampireSurvivorsTemplate.csproj`** — has `SpacetimeDB.ClientSDK 2.1.0` package reference
- Registered in `project.godot` autoload as `SpacetimeDBManager`

### AoI (not yet wired)

Clients will subscribe to chunk-range SQL queries instead of all tables:
```sql
SELECT * FROM player_position WHERE chunk_x BETWEEN @min AND @max AND chunk_y BETWEEN @min AND @max
```
Re-subscribe when the local player crosses a chunk boundary.

### Enemy position sync strategy (not yet implemented)

`EnemyAI` reducer runs at 10Hz but writes positions to DB at ~2Hz. Clients fill gaps by running the same deterministic AI locally (snapshot interpolation). Requires all clients to agree on player positions as inputs — guaranteed since player positions come from the same SpacetimeDB subscription.

### Bullet system (not yet implemented)

`EnemyAI` writes a `BulletPattern` row (pattern_id, origin, spawn_time, params) when an enemy fires. Clients reconstruct positions locally using deterministic parametric math. `BulletHitCheck` evaluates the same function server-side for authoritative hit detection. No per-tick position sync.

### Local testing note

Multiple instances on the same machine share the same identity (same `.bmmo_token` file). The second connect attempt by the same identity will fail to insert a duplicate `PlayerPosition` row. True multiplayer testing requires different machines or manually clearing the token file between instances.

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
- **Client can fake damage:** `report_hit()` accepts client-supplied damage amount with no validation against actual weapon stats. `BulletHitCheck` reducer must validate against actual weapon stats stored server-side.
- **No position validation:** server doesn't reject teleport-speed movement updates.
- **Late-join race condition:** player respawn during another client's join sequence can result in the joining client missing the respawned player.

## Key Docs

Full customization guide: `VS-Like/README/DOCS_English.txt`
Licensing: Free for personal & commercial use; no redistribution.
