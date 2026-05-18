# System Overview

SpacetimeDB is the database AND the server. Clients connect, subscribe to views, and call reducers. The server stores state and broadcasts changes. No separate game server process. No elevated client — the server scheduled reducer owns enemy behavior.

---

## Module Structure

```
server/spacetimedb/src/main/
  tables.rs    — all table + type definitions
  methods.rs   — pure helpers, constants, math (world_to_chunk, spiral_chunk_index, etc.)
  reducers.rs  — lifecycle + player reducers + scheduled tick
  admin.rs     — admin-only reducers (spawn, seeding, player management, map)
  seeds.rs     — Seed trait + seed_new_player + make_phase + make_attack helpers
  views.rs     — Query<T> views for client subscriptions
```

---

## Tables

### Persistent (survive disconnects, across sessions)

| Table | Struct | What it stores | Who writes |
|-------|--------|----------------|------------|
| `logged_in_player` | `Player` | Identity, username, is_admin — active players | Server on connect (swapped from logged_out) |
| `logged_out_player` | `Player` | Same struct — inactive players | Server on disconnect (swapped from logged_in) |
| `PlayerData` | `PlayerData` | Level, XP, HP, max_hp | Server (gain_xp, report_enemy_hit) |
| `PlayerStats` | `PlayerStats` | Computed STR/WIS/DEX/DEF/VIT/SPD | Server recomputes on level-up or inventory change |
| `PlayerInventory` | `PlayerInventory` | Equipment + backpack as Vec<InventorySlot> | Client via update_inventory |
| `PlayerPosition` | `PlayerPosition` | x, y, rotation, chunk_index | Client via report_movement. Persists on disconnect — player respawns where they left off. |
| `ItemStats` | `ItemStats` | Item definitions, damage/stat formulas | Admin via upsert_item_stats or init |
| `EnemyTemplate` | `EnemyTemplate` | Enemy type defs: phases → attacks (typed PatternType params, no string parsing) | Admin via upsert_enemy_template or init |
| `BuildingTile` | `BuildingTile` | Hex grid tiles: axial coords, chunk, building type, owner | Pre-populated by add_chunks; mutated by place/remove_building |
| `MapConfig` | `MapConfig` | Singleton: chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius | Admin via add_chunks |

### Session (cleared between server restarts)

| Table | What it stores | Who writes |
|-------|----------------|------------|
| `Enemy` | Live instances: template_id, res_path, spawn_x/y, x/y (live position updated by server tick), hp, phase, last_attack_time, chunk_index | Admin spawns; server tick updates position + phase; clients report hits |
| `LootDrop` | Items on the ground: item_id, x, y, chunk_index, expires_at | Server on enemy death |

### Event (ephemeral — insert + immediate delete, clients react to OnInsert)

| Table | What it stores | Who writes |
|-------|----------------|------------|
| `BulletPatternEvent` | enemy_id, origin_x/y, PatternType (with typed params), targets (Vec<Identity>), chunk_index | Server tick when enemy fires |

---

## Pattern Types

Bullet patterns are typed enums — no string parsing. Each variant carries its own params struct:

```rust
PatternType::Straight(StraightParams { speed, count })
PatternType::Sine(SineParams { speed, count, amplitude, frequency })
PatternType::Homing(HomingParams { speed, count, turn_rate })
PatternType::Ring(RingParams { speed, count })
PatternType::Spiral(SpiralParams { speed, count, arms, rotation_speed })
PatternType::Burst(BurstParams { speed, count, spread })
```

---

## Reducers

### Lifecycle (reducers.rs)

| Reducer | What it does |
|---------|-------------|
| `init` | Runs once on first publish. Starts `EnemyBehaviorSchedule` (500ms interval). |
| `client_connected` | New player: calls `seed_new_player`. Returning player: moves row from `logged_out_player` → `logged_in_player`. |
| `client_disconnected` | Moves row from `logged_in_player` → `logged_out_player`. PlayerPosition is preserved. |

### Player (reducers.rs) — all guarded by `require_logged_in`

| Reducer | What it does |
|---------|-------------|
| `set_username(username)` | Updates Player.username in logged_in_player |
| `report_movement(x, y, rotation)` | Validates caller is logged in, wraps torus coords, updates PlayerPosition |
| `gain_xp(amount)` | Adds XP, levels up, recomputes stats |
| `update_inventory(slots)` | Replaces inventory slots, recomputes stats |
| `report_enemy_hit(enemy_id)` | Computes damage from caller stats + weapon, updates enemy HP, handles death + loot + XP |
| `pickup_drop(drop_id)` | Adds item to inventory, deletes drop row |
| `place_building(hex_q, hex_r, building_type)` | Places building on empty tile |
| `remove_building(hex_q, hex_r)` | Owner or admin can remove |

### Scheduled (reducers.rs)

| Reducer | What it does |
|---------|-------------|
| `tick_enemy_behavior` | Runs every 500ms. Iterates all enemies. Skips any with no player within ENEMY_SIMULATION_RANGE (640 units). Behavior logic filled in per-template by designer. |

### Admin (admin.rs) — all require `is_admin`

| Reducer | What it does |
|---------|-------------|
| `claim_admin` | Bootstrap: caller becomes admin if none exist |
| `release_admin` | Removes admin from caller |
| `change_stats(target, ...)` | Overrides target's PlayerStats directly |
| `give_item(target, item_id)` | Adds item to target's backpack |
| `remove_item(target, item_id)` | Removes item from target's inventory |
| `upsert_item_stats(stats)` | Creates or updates item definition |
| `upsert_enemy_template(template)` | Creates or updates enemy type definition |
| `spawn_enemy(template_id, res_path, x, y, is_elite, immortal)` | Inserts Enemy row at world position |
| `despawn_enemy(enemy_id)` | Deletes Enemy row |
| `add_chunks(hex_radius, cols, rows, outer_radius)` | Pre-populates BuildingTile rows + writes MapConfig singleton |
| `clear_chunks` | Deletes all BuildingTile rows |

---

## Views

Clients subscribe to views rather than raw tables or SQL. Views use `Query<T>` (incremental — read set updated without full recompute). Each table has its own view so queries stay efficient.

### Local player (subscribe once on connect)

| View | Returns | Filter |
|------|---------|--------|
| `local_player` | `Player` | player_id == sender |
| `local_player_data` | `PlayerData` | player_id == sender |
| `local_player_stats` | `PlayerStats` | player_id == sender |
| `local_player_inventory` | `PlayerInventory` | player_id == sender |
| `local_player_position` | `PlayerPosition` | player_id == sender |

### Other players (subscribe once on connect)

| View | Returns | Filter |
|------|---------|--------|
| `remote_players` | `Player` | All logged_in_player except sender (metadata — username etc.) |
| `nearby_player_positions` | `PlayerPosition` | chunk_index IN surrounding_chunk_indices(sender position) |

### Enemies (subscribe once on connect)

| View | Returns | Filter |
|------|---------|--------|
| `nearby_enemies` | `Enemy` | chunk_index IN surrounding_chunk_indices(sender position) |
| `enemy_templates` | `EnemyTemplate` | All (static reference data) |

### Bullet events (direct table subscription — event tables don't support views yet)

Clients subscribe directly to `BulletPatternEvent`. On `OnInsert`, unpack `PatternType` variant and feed to bullet factory. Row is already deleted server-side by the time the client reacts — purely a broadcast mechanism.

---

## Login / Logout Flow

`Player` is a single struct used by two tables. On connect/disconnect the row is moved between them, carrying all player data including username and is_admin.

```
New player connects
  → seed_new_player: creates PlayerData, PlayerStats, PlayerInventory, PlayerPosition (0,0), inserts into logged_in_player

Returning player connects
  → logged_out_player row deleted, same row inserted into logged_in_player
  → PlayerPosition unchanged — player loads in at last known position

Player disconnects
  → logged_in_player row deleted, same row inserted into logged_out_player
  → PlayerPosition row stays — position persists for next session
```

Gameplay reducers only operate on logged_in_player (enforced by `require_logged_in` guard). Admin reducers can target any player by username, searching both tables.

---

## Enemy Lifecycle

```
Admin calls spawn_enemy() (or future spawn zone logic)
  → Enemy row inserted with spawn_x/y, x/y, hp=max_hp, phase=0
  → Clients in AoI receive OnInsert from nearby_enemies view
  → Game manager spawns enemy node

tick_enemy_behavior fires every 500ms
  → Skips enemies with no player within 640 units
  → Updates Enemy.x/y (live position), Enemy.phase, Enemy.last_attack_time
  → If firing: inserts BulletPatternEvent (ephemeral — deleted same transaction)
  → Clients receive Enemy OnUpdate → enemy node updates itself
  → Clients receive BulletPatternEvent OnInsert → bullet factory spawns pattern

Client calls report_enemy_hit(enemy_id)
  → Server computes damage from caller stats + equipped weapon
  → Updates Enemy.hp + Enemy.phase
  → On death: inserts LootDrop, deletes Enemy row, awards XP
  → Clients receive OnDelete → game manager despawns node
```

---

## Client Responsibility Split (target architecture)

| Node | Subscribes to | Owns |
|------|--------------|------|
| SpacetimeDBManager | — | Connection, view subscriptions |
| Game manager | `nearby_enemies`, `loot` (future view) | OnInsert → spawn node; OnDelete → despawn node |
| Local player | 5 local_player_* views | Input, movement, combat calls to server |
| Remote player node | `remote_players`, `nearby_player_positions` | OnUpdate for own player_id → lerp position |
| Enemy node | `nearby_enemies` | OnUpdate for own enemy_id → apply server state |
| Bullet factory | `BulletPatternEvent` (direct table sub) | OnInsert → unpack PatternType → run spawner |
| Drop node | Loot view (TBD) | Pickup collision, visual |

---

## Seeding

New enemy templates and items are defined in code via the `Seed` trait and helper functions. Safe to re-run on republish (upsert pattern).

```rust
EnemyTemplate::seed(ctx, EnemyTemplate {
    template_id: "goblin".into(),
    display_name: "Goblin".into(),
    max_hp: 100,
    phases: vec![
        make_phase(0, 1.0, vec![
            make_attack(PatternType::Straight(StraightParams { speed: 150.0, count: 1 }), EnemyTarget::NearestPlayer, 2.0),
        ]),
        make_phase(1, 0.5, vec![
            make_attack(PatternType::Ring(RingParams { speed: 150.0, count: 8 }), EnemyTarget::NearestPlayer, 1.5),
        ]),
    ],
});
```

---

## Known Gaps

| Gap | Status |
|-----|--------|
| Client refactor | Next step — see next-steps.md |
| Bullet factory node integration | After client refactor |
| Enemy behavior logic in tick | Skeleton only — behavior per template designed by designer |
| Spawn zones (proximity-based spawning) | Not yet implemented |
| AoI resubscription on chunk boundary crossing | Not yet wired on client |
| Player taking damage reducer | Not yet defined |
| View params for world events | Blocked on SpacetimeDB — event tables and views with params not yet supported |
| Torus world wrapping in chunk queries | Deferred until world size finalized |
| Loot drop item content | drop_id placeholder only — needs drop table per template |
| `report_player_hit` reducer | Needed for bullet → player damage flow |
