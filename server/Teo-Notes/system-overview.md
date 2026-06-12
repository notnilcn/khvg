# System Overview

Current-state reference for the SpacetimeDB module and its client integration. Reflects the
post-redesign profile-based player model, the split item schema, and the typed enemy
behavior/pattern system. For the architectural rules (authority model, hex grid math, anti-cheat
design) see the root `CLAUDE.md` — this doc is the "what exists right now" companion to that.

## Module Layout

```
server/spacetimedb/src/
├── player/   tables.rs, methods.rs, reducers.rs, views.rs
├── world/    tables.rs, methods.rs, reducers.rs, views.rs
├── enemy/    def_tables.rs, instance_tables.rs, methods.rs, reducers.rs, views.rs
└── main/     lifecycle.rs, admin.rs, seeds.rs, debug.rs
```

- `player/` — identity, profiles, stats/inventory/position, movement, taking damage, loot pickup
- `world/` — hex/chunk math, map config, buildings, item definitions (Item + per-type tables)
- `enemy/` — `def_tables.rs` is static template data (authored once via seeds), `instance_tables.rs`
  is per-enemy runtime state. `methods.rs` is the behavior tick state machine.
- `main/` — lifecycle hooks (`init`/`client_connected`/`client_disconnected`), admin tools, seed
  data, and the position-debug overlay

---

## Tables

### `player/tables.rs`

```rust
InventorySlot (embedded, not a table) { allowed_types: Vec<ItemType>, item_id: Option<String> }

LoggedInPlayer (public)  { #[pk] player_id: Identity, username: String, is_admin: bool, #[unique] profile_id: u64 }
LoggedOutPlayer (private) { #[pk] player_id: Identity, username: String, is_admin: bool }

PlayerProfile (public, index by_player_profile_name on [player_id, profile_name]) {
    #[pk #[auto_inc]] profile_id: u64,
    #[index] player_id: Identity,
    profile_name: String, texture_id: String, aim_assist: bool, lock_on: bool
}

PlayerData (public)     { #[pk] profile_id: u64, level: u32, xp: u32, hp: u32, max_hp: u32 }
PlayerStats (public)    { #[pk] profile_id: u64, strength/wisdom/dexterity/defense/vitality/speed: i32 }
PlayerInventory (public){ #[pk] profile_id: u64, slots: Vec<InventorySlot>  // 24 slots, see layout below }
PlayerPosition (public) { #[pk] profile_id: u64, player_id: Identity, x/y/rotation: f32, #[index] chunk_index: i64 }

LootDrop (public) { #[pk #[auto_inc]] drop_id: u64, item_id: String, x/y: f32, #[index] chunk_index: i64, expires_at: Timestamp }
```

**Identity split:** a connected player (`Identity`) is either a row in `LoggedInPlayer` (in-world)
or `LoggedOutPlayer` (lobby), never both. Each player can own up to 3 `PlayerProfile` rows
(characters); `LoggedInPlayer.profile_id` is the *active* one. `PlayerData`/`PlayerStats`/
`PlayerInventory`/`PlayerPosition` are keyed by `profile_id`, not `player_id`.

**Current inventory layout (24 slots, built by `try_scaffold_profile`):**

| Indices | Zone | Allowed types |
|---|---|---|
| 0 | Weapon | `Weapon` |
| 1–5 | Hotbar | `Ability`, `Consumable` |
| 6–9 | Accessory | `Accessory` |
| 10–13 | Armor | `Armor` |
| 14–22 | General (9 slots) | everything except `Bag` |
| 23 | Bag | `Bag` |

This layout is changing in next-steps.md Task 2 — see Known Gaps.

### `world/tables.rs`

```rust
enum TextureKind { Player, Enemy, Bullet, Item, Environment }

MapConfig (public)     { #[pk] id: u32 /* always 0, singleton */, chunk_hex_radius/chunk_cols/chunk_rows: i32, hex_outer_radius: f32 }

BuildingTile (public) {
    #[pk #[auto_inc]] tile_id: u64,
    #[index] hex_q: i32, hex_r: i32, chunk_q/chunk_r: i32, local_index: i32,
    #[index] chunk_index: i64,
    building_type: String, // "" = unoccupied
    owner_id: Option<Identity>
}

TextureEntry (public) { #[pk] texture_id: String, res_path: String, kind: TextureKind }

enum WeaponPattern { Single, Triple, Cluster }
enum ItemType { Weapon, Ability, Armor, Accessory, Bag, Consumable }

Item (public) { #[pk] item_id: String, display_name: String, texture_id: String, item_type: ItemType }

Weapon (public)    { #[pk] weapon_id: String, damage: u32, range: f32, fire_rate: f32, bullet_speed: f32, shot_count: u32, zone_count: u32, pierce: bool, projectile_texture_id: String, pattern: WeaponPattern, spread_angle: f32 }
Armor (public)     { #[pk] armor_id: String, defense_bonus/vitality_bonus: i32, hp_bonus: u32 }
Artifact (public)  { #[pk] artifact_id: String, cooldown/duration/potency: f32 }
Accessory (public) { #[pk] accessory_id: String, strength_bonus/wisdom_bonus/dexterity_bonus/speed_bonus: i32 }
Consumable (public){ #[pk] consumable_id: String, effect: String, potency/duration: f32 }
```

**Item schema:** `Item` is the shared row (name/icon/type); each `ItemType` has a matching
per-type table keyed by the *same* `item_id` string. One quirk worth knowing: `ItemType::Ability`
items store their stats in the **`Artifact`** table (not an "Ability" table) — `seed_ability`
inserts into both `Item{item_type: Ability}` and `Artifact`.

### `enemy/def_tables.rs` (static template data, authored via seeds)

```rust
// Pattern param structs (used inside PatternType)
RingParams      { speed: f32, count: u32 }
VolleyParams    { speed: f32, count: u32, speed_variance: f32, angle_jitter: f32 }
CurtainParams   { speed: f32, count: u32, angle_span: f32, gap_index: u32 }
ShotgunParams   { speed: f32, count: u32, spread: f32, speed_variance: f32, lifetime_variance: f32 }
ExplosionParams { speed: f32, count: u32, speed_variance: f32, lifetime_variance: f32 }

// Movement param structs (used inside MovementBehavior)
WanderParams { speed: f32, active_duration: f32, pause_duration: f32 }
ChaseParams  { speed: f32, active_duration: f32, pause_duration: f32 }
FleeParams   { speed: f32, min_range: f32, active_duration: f32, pause_duration: f32 }

enum MovementBehavior { Stationary, Wander(WanderParams), Chase(ChaseParams), Flee(FleeParams) }
enum PatternType { Ring(RingParams), Volley(VolleyParams), Curtain(CurtainParams), Shotgun(ShotgunParams), Explosion(ExplosionParams) }
enum EnemyTarget { Idle, AggroTarget }

MovementDef (public) { #[pk #[auto_inc]] def_id: u64, movement: MovementBehavior }

SingleStepDef (public) {
    #[pk #[auto_inc]] def_id: u64,
    pattern_type: PatternType, target: EnemyTarget,
    offset_x/offset_y: f32, base_angle_offset: f32,
    texture_id: String, lifetime: f32, damage: u32, next_step_delay: f32
}

RepeatStepDef (public) { // same as SingleStepDef plus:
    ...SingleStepDef fields, repeat_interval: f32, repeat_target: u32
}

MultiShot (embedded) { pattern_type, target, offset_x/offset_y, base_angle_offset, texture_id, lifetime, damage } // one "shot" within a Multi step
MultiStepDef (public) { #[pk #[auto_inc]] def_id: u64, shots: Vec<MultiShot>, next_step_delay: f32 }

enum SequenceStepDef { Single(def_id), Repeat(def_id), Multi(def_id) } // refs into the three tables above

AttackSequence (embedded) { steps: Vec<SequenceStepDef>, start_delay: f32 }
PhaseDef (embedded) { phase_index: u8, hp_threshold: f32, movement_def_id: u64, phase_loop_delay: f32, attacks: Vec<AttackSequence> }

EnemyTemplate (public) {
    #[pk] template_id: String, texture_id: String, display_name: String,
    max_hp: u32, simulation_range: f32, aggro_lock_seconds: f32,
    phases: Vec<PhaseDef>
}
```

### `enemy/instance_tables.rs` (per-enemy runtime state)

```rust
enum SequenceStepRef { Single(def_id), Repeat(repeat_id), Multi(MultiStepRef) } // runtime-side equivalent of SequenceStepDef
MultiStepRef (embedded) { def_id: u64, shot_index: u32 }

RepeatStepInstance (public) { // per-enemy clone of a RepeatStepDef, so repeat_count is independent per enemy
    #[pk #[auto_inc]] repeat_id: u64,
    ...same fields as RepeatStepDef, repeat_count: u32
}

EnemySequenceStep (public) { #[pk #[auto_inc]] step_id: u64, #[index] attack_id: u64, step_index: u32, step: SequenceStepRef }

EnemyAttack (public) { // one per AttackSequence, tracks step progress
    #[pk #[auto_inc]] attack_id: u64, #[index] phase_id: u64,
    attack_index: u32, step_index: u32, step_timer: Timestamp, step_waiting: bool, start_delaying: bool
}

EnemyPhase (public) { // one per PhaseDef
    #[pk #[auto_inc]] phase_id: u64, #[index] behavior_id: u64,
    phase_index: u8, hp_threshold: f32, movement_def_id: u64, phase_loop_delay: f32,
    phase_waiting: bool, phase_loop_timer: Timestamp, move_cycle_started_at: Timestamp
}

EnemyBehavior (public) { // one per Enemy
    #[pk #[auto_inc]] behavior_id: u64,
    aggro_target: Option<Identity>, aggro_locked_until: Timestamp,
    was_simulating: bool, active_phase_index: u8, velocity_x/velocity_y: f32
}

Enemy (public) {
    #[pk #[auto_inc]] enemy_id: u64, template_id: String, behavior_id: u64,
    x/y: f32, spawn_time: Timestamp, hp: u32, phase: u8, is_elite: bool, immortal: bool,
    #[index] chunk_index: i64
}

EnemySpawnSchedule (scheduled → tick_enemy_spawn, every 2s)        { #[pk #[auto_inc]] scheduled_id: u64, scheduled_at: ScheduleAt }
EnemyBehaviorSchedule (scheduled → tick_enemy_behavior, every 100ms){ #[pk #[auto_inc]] scheduled_id: u64, scheduled_at: ScheduleAt }

EnemyZone (public) { // not yet populated/wired — see Known Gaps (Task 5)
    #[pk #[auto_inc]] zone_id: u64,
    center_chunk_q/center_chunk_r: i32, chunk_radius: i32, spawn_radius: f32,
    template_ids: Vec<String>, max_enemies: u32
}

BulletPatternEvent (public, event table) {
    #[pk #[auto_inc]] event_id: u64, enemy_id: u64, source_step: SequenceStepRef,
    origin_x/origin_y: f32, origin_offset_x/origin_offset_y: f32, base_angle_offset: f32,
    pattern_type: PatternType, target: Option<Identity>, texture_id: String, lifetime: f32,
    #[index] chunk_index: i64
}
```

### `main/debug.rs`

```rust
PlayerPositionDebug (private) { #[pk] player_id: Identity, x/y/rotation: f32, chunk_index: i64, hex_q/hex_r: i32 }
PlayerPositionDebugSchedule (scheduled → tick_player_position_debug, 1s when active) { #[pk #[auto_inc]] scheduled_id: u64, scheduled_at: ScheduleAt }
```

---

## Reducers

### `main/lifecycle.rs`

| Reducer | Notes |
|---|---|
| `init` | Inserts the two enemy schedules (`EnemyBehaviorSchedule` @ 100ms, `EnemySpawnSchedule` @ 2s), runs all seed functions, calls `internal_add_chunks` with the default map dimensions (radius 5, 30×30 chunks, `hex_outer_radius` 32.0) |
| `client_connected` | If the identity is already `LoggedInPlayer` (shouldn't happen — logged as an error and force-moved back to lobby). If brand new (not in either table), inserts a `LoggedOutPlayer` row and a default `PlayerProfile` ("Knight"/"Knight" texture) |
| `client_disconnected` | If currently `LoggedInPlayer`, moves the row to `LoggedOutPlayer` (same as `leave_world`) |

### `player/reducers.rs`

| Reducer | Guard | Notes |
|---|---|---|
| `set_username(username)` | connected | Updates whichever of `LoggedInPlayer`/`LoggedOutPlayer` exists |
| `create_profile(profile_name, texture_id)` | lobby | Max 3 profiles per player; names unique case-insensitively per player |
| `delete_profile(profile_id)` | lobby, owner | Deletes `PlayerData`/`PlayerStats`/`PlayerInventory`/`PlayerPosition`/`PlayerProfile` |
| `join_world(profile_id)` | lobby, owner | `LoggedOutPlayer` → `LoggedInPlayer{profile_id}`, then `try_scaffold_profile` |
| `leave_world()` | in world | `LoggedInPlayer` → `LoggedOutPlayer`; profile data preserved |
| `report_movement(x, y, rotation)` | in world | Wraps position via `wrap_world_pos`, recomputes `chunk_index`, updates `PlayerPosition` |
| `update_inventory(slots)` | in world | Validates same length + unchanged `allowed_types` + item-type compatibility, then `recompute_stats`. **Slated for removal** — Task 2 |
| `report_hit(attack: SequenceStepRef)` | in world | Self-reported "I got hit". Resolves `base_damage` from the step def/instance, applies `compute_incoming_damage`. On HP 0: deletes `PlayerData`/`PlayerInventory`/`PlayerStats`/`PlayerPosition` and moves the player back to `LoggedOutPlayer` (full reset — profile row survives, next `join_world` re-scaffolds starter gear) |
| `pickup_drop(drop_id)` | in world | Places item in the first empty general slot (14–22), deletes the `LootDrop` row; errors if none free |

### `world/reducers.rs`

| Reducer | Guard | Notes |
|---|---|---|
| `place_building(hex_q, hex_r, building_type)` | connected | Errors if the tile's `building_type` is already non-empty |
| `remove_building(hex_q, hex_r)` | connected, owner or admin | Clears `building_type`/`owner_id` |

### `enemy/reducers.rs`

| Reducer | Notes |
|---|---|
| `tick_enemy_spawn` (scheduled, 2s) | **Temporary**: only spawns if zero enemies exist; hardcodes the `"Enemy"` template at world origin. Will be replaced by `spawn_from_zones` (Task 5) |
| `tick_enemy_behavior` (scheduled, 100ms) | Main enemy tick — see Enemy Behavior Lifecycle below |
| `report_enemy_hit(enemy_id)` | Self-reported "I hit the enemy". No-op if `immortal`. Damage via `compute_player_damage`. On HP 0: `delete_enemy_behavior` + delete `Enemy`, award XP (`max_hp / 10`). Else: `compute_phase` may advance `enemy.phase`, and aggro is re-rolled or re-locked onto the reporter |

### `main/admin.rs`

All gated by `is_admin(ctx)` except `claim_admin`.

| Reducer | Notes |
|---|---|
| `claim_admin()` / `release_admin()` | Single-admin model — errors if another admin already exists |
| `change_stats(target_username, profile_name, str, wis, dex, def, vit, spd)` | Direct `PlayerStats` overwrite — bypasses `recompute_stats`, so item bonuses are reapplied only on the next `recompute_stats` call |
| `give_item` / `remove_item(target_username, profile_name, item_id)` | `give_item` places into the first empty general slot **without** checking `ItemType` against `allowed_types` |
| `upsert_texture_entry` / `upsert_item` / `upsert_enemy_template` | Generic upsert-by-PK |
| `spawn_enemy(template_id, x, y, is_elite, immortal)` / `despawn_enemy(enemy_id)` | Manual enemy spawn/despawn |
| `add_chunks(...)` / `clear_chunks()` | `add_chunks` wraps `internal_add_chunks` (also used by `init`) — (re)generates the full `BuildingTile` grid and `MapConfig` |
| `find_player_by_username(lookup)` | Helper (not a reducer) — matches by username, full identity hex, or identity hex prefix |

### `main/debug.rs`

| Reducer | Notes |
|---|---|
| `tick_player_position_debug` (scheduled, 1s when active) | Mirrors all `PlayerPosition` rows into `PlayerPositionDebug` (adds `hex_q`/`hex_r`), removes entries for disconnected players |
| `toggle_debug()` | Admin only. Toggles the schedule above; clears `PlayerPositionDebug` rows on disable |

---

## Views

Views are how clients subscribe to filtered/joined data without per-row server logic. `ViewContext`
views are per-caller (filtered by `ctx.sender()`); `AnonymousViewContext` views are global
reference data available to anyone.

### `player/views.rs`

`nearby_indices(ctx)` is a shared helper (not a view): resolves the caller's `PlayerPosition` via
their `LoggedInPlayer.profile_id`, then returns `surrounding_chunk_indices(...)` for AoI.

| View | Returns |
|---|---|
| `local_player` | Caller's `LoggedInPlayer` row |
| `local_lobby_player` | Caller's `LoggedOutPlayer` row |
| `local_player_profiles` | All `PlayerProfile` rows owned by caller (≤3) |
| `local_player_active_profile` | `PlayerProfile` matching caller's active `profile_id` |
| `local_player_data` / `local_player_stats` / `local_player_inventory` / `local_player_position` | Caller's row in the respective table |
| `nearby_remote_players` | `PlayerPosition` rows in AoI chunks, `left_semijoin` with `LoggedInPlayer` on `profile_id` |
| `nearby_remote_players_profiles` | All `PlayerProfile` rows, `left_semijoin` with `LoggedInPlayer` on `profile_id` — lets clients resolve texture/name for any nearby remote player |

### `world/views.rs` (all `AnonymousViewContext`, public reference data)

`all_textures`, `all_items`, `all_weapons`, `all_armor`, `all_artifacts`, `all_accessories`, `all_consumables` — each returns every row of the corresponding table.

### `enemy/views.rs`

| View | Returns |
|---|---|
| `nearby_enemies` | `Enemy` rows in AoI chunks (same chunk-index pattern as `nearby_remote_players`) |
| `enemy_templates` | All `EnemyTemplate` rows (anonymous, public reference data) |

---

## Hex Grid & Chunk System

Full math rules live in the root `CLAUDE.md`; the implementation is in `world/methods.rs`. Key
constants:

| Constant | Value | Meaning |
|---|---|---|
| `BASE_MAX_HP` | 100 | HP at level 1 |
| `HP_PER_LEVEL` | 5 | Max HP gained per level |
| `DEFAULT_CHUNK_HEX_RADIUS` | 5 | Hexes per chunk (radius) |
| `DEFAULT_CHUNK_COLS` / `_ROWS` | 30 / 30 | World size in chunks (torus) |
| `DEFAULT_HEX_OUTER_RADIUS` | 32.0 | World-space size of one hex |
| `DEFAULT_AOI_CHUNK_RADIUS` | 3 | Chunk rings included in AoI subscriptions |
| `BEHAVIOR_TICK_DT` | 0.1 | Enemy behavior tick interval (seconds) |

`spiral_chunk_index` gives every chunk a unique `i64`; `wrap_world_pos`/`wrap_chunk_coords` wrap
both continuous and chunk coordinates across the torus; `surrounding_chunk_indices` returns the
AoI ring used by `nearby_indices`.

---

## Login / Lobby / Profile / World Flow

1. **First connect** (`client_connected`): inserts `LoggedOutPlayer` + a default "Knight" profile.
2. **Lobby** (`LoggedOutPlayer`): `set_username`, `create_profile` (≤3), `delete_profile`,
   `join_world(profile_id)`.
3. **Join**: `LoggedOutPlayer` → `LoggedInPlayer{profile_id}`. `try_scaffold_profile` creates
   `PlayerData`/`PlayerInventory`/`PlayerStats`/`PlayerPosition` if this profile has never been
   played before (starter inventory includes `StarterShotgun` in slot 0; `PlayerStats` derived via
   `recompute_stats`; spawn position is world origin).
4. **In world** (`LoggedInPlayer`): `report_movement`, `report_hit`, `report_enemy_hit`,
   `pickup_drop`, `place_building`/`remove_building`, `update_inventory`.
5. **Leave** (`leave_world` or disconnect): `LoggedInPlayer` → `LoggedOutPlayer`. Profile data
   (`PlayerData`/`Stats`/`Inventory`/`Position`) is preserved for next time.
6. **Death** (`report_hit` drives HP to 0): full reset — `PlayerData`/`Inventory`/`Stats`/`Position`
   rows are **deleted** and the player is moved to `LoggedOutPlayer`. The `PlayerProfile` row
   itself survives; the next `join_world` re-scaffolds fresh starter gear via
   `try_scaffold_profile`.

---

## Combat & Damage Flow

Both directions are client-authoritative for collision detection (per the anti-cheat model in the
root `CLAUDE.md`) — the server only computes the resulting damage numbers from self-reported hits.

- **Player → Enemy**: client detects its bullet hit an enemy and calls
  `report_enemy_hit(enemy_id)`. Server computes `compute_player_damage` =
  `weapon.damage * (1 + STR * 0.02)`, minimum 1, or `0` if no weapon is equipped (slot 0 empty).
- **Enemy → Player**: `BulletManager` spawns bullets client-side from `BulletPatternEvent` rows;
  on collision it calls `report_hit(source_step)`. Server resolves `base_damage` from the
  referenced `SingleStepDef` / `RepeatStepInstance` / `MultiStepDef[shot_index]`, then computes
  `compute_incoming_damage` = `base_damage * (1 - DEF * 0.02)`, minimum 1.

---

## Enemy Behavior Lifecycle

**Definitions → instances:** `EnemyTemplate.phases: Vec<PhaseDef>` is static template data. When
an enemy spawns, `build_enemy_behavior` instantiates the runtime tree: one `EnemyBehavior`, one
`EnemyPhase` per `PhaseDef`, one `EnemyAttack` per `AttackSequence`, one `EnemySequenceStep` per
step. `Repeat` steps get their own `RepeatStepInstance` clone (cloned from the shared
`RepeatStepDef`) so `repeat_count` advances independently per enemy.

**Per-tick (`tick_enemy_behavior`, every 100ms):**

1. Skip enemies with no logged-in player within `template.simulation_range` (and clear
   `was_simulating`).
2. On the tick an enemy *enters* range, `recompute_aggro` picks the nearest player and sets
   `aggro_locked_until`.
3. If `enemy.phase` (set externally by `report_enemy_hit`) differs from
   `behavior.active_phase_index`, the new phase's cycle is reset via `begin_phase_cycle`.
4. `tick_phase` advances every `AttackSequence` in the active phase by at most one step via
   `tick_sequence`, collecting bullet events. Each event becomes a `BulletPatternEvent` row
   (`target` resolved via `resolve_step_target` + `behavior.aggro_target`).
5. `apply_movement` computes the new position from the phase's `MovementDef`
   (`Stationary`/`Wander`/`Chase`/`Flee`), then `wrap_world_pos` + chunk reindex.

**`tick_sequence` step state machine** (persisted on `EnemyAttack.step_index`/`step_waiting`/`step_timer`):
- `Single`/`Multi`: fire once, optionally wait `next_step_delay`, then advance.
- `Repeat`: re-fire every `repeat_interval` until `repeat_count` reaches `repeat_target`, then
  behaves like the others.

**On `report_enemy_hit` death:** `delete_enemy_behavior` tears down every `EnemyPhase`/
`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance` row plus the `Enemy` row, and awards
`max_hp / 10` XP via `internal_gain_xp`.

**On non-lethal hit:** `compute_phase` picks the highest-index phase whose `hp_threshold` has been
crossed. If the phase changed, aggro is re-rolled (nearest player). Otherwise, once
`aggro_locked_until` expires, aggro re-locks onto whoever just landed the hit.

---

## Seeding (`main/seeds.rs`, run from `init`)

- `seed_default_textures` — 5 `TextureEntry` rows (Enemy/Archer/Knight/Bullet/Arrow)
- `seed_default_enemies` — `"Enemy"` (Wander + Ring turret), `"Archer"` (Flee + Volley/Shotgun kiter)
- `"TestBoss"` + `seed_test_boss_p2`..`p6` — 6 multi-phase boss templates used to exercise pattern
  variety (turret/curtain-sweep/rusher/kiter/nova phases, plus P2–P6 variants exercising
  `Repeat`/`Multi` step types)
- `seed_default_items` — 3 starter weapons (`StarterBow`/`StarterRifle`/`StarterShotgun`) via
  `seed_weapon`, each inserting both an `Item` and a `Weapon` row
- `seed_armor` / `seed_ability` / `seed_accessory` / `seed_consumable` — `#[allow(dead_code)]`
  scaffolding, not yet called from `init`; ready for future content authoring
- `Seed` trait — generic upsert-by-primary-key, implemented for `MapConfig`/`TextureEntry`/`Item`/
  `Weapon`/`Armor`/`Artifact`/`Accessory`/`Consumable`/`EnemyTemplate`. Re-running `init` (e.g.
  after a fresh publish) updates existing rows instead of erroring on duplicate keys.
- `make_phase`/`make_sequence`/`seq_single`/`seq_repeat`/`seq_multi`/`multi_shot` — builder helpers
  that insert def rows (`MovementDef`/`SingleStepDef`/`RepeatStepDef`/`MultiStepDef`) and return the
  lightweight `*Ref` enums stored on `PhaseDef`/`AttackSequence`

---

## Client Overview

Full per-file breakdown is in `client/CLAUDE.md`. High-level data flow:

- **`GameManager.cs`** — connection lifecycle, table subscriptions, entity registries
  (`remotePlayers`/`enemies`/`drops`), and a minimal lobby UI (`ShowLobby` builds a single "Join
  World" button programmatically — see Known Gaps re: `LobbyGui.cs`)
- **`BulletManager.cs`** — dispatches `BulletPatternEvent` rows to `BlastBullets2D` spawners for
  enemy bullets, and fires player bullets via `SpawnPlayerBullet`. Its `SplitMix64`/`HashToUnit`/
  `Jitter` mirror `enemy/methods.rs` exactly, so `Volley`/`Shotgun`/`Explosion` pellet variance is
  reproducible from `event_id` alone. `OnEnemyBulletAreaEntered` → `report_hit`.
- **`LocalPlayer`/`LocalPlayerCombat`** — input, movement (`report_movement`), aiming, firing,
  `report_enemy_hit` on landed hits
- **`RemotePlayer.cs`** — interpolates `nearby_remote_players` rows
- **`Enemy.cs`** — interpolates `nearby_enemies` rows
- **`Drop.cs`** — renders `LootDrop` pickups, calls `pickup_drop` on interact
- **`HexGridOverlay2D.cs`** — debug grid overlay
- **`DebugOverlay.cs`** — FPS/debug HUD, wires `toggle_debug`

---

## Known Gaps / Next Steps

See `server/Teo-Notes/next-steps.md` for full task descriptions.

- **Task 2 — Inventory Server Logic**: new 24-slot layout (0 weapon / 1–5 consumable-only hotbar /
  6–9 accessory / 10–13 armor / 14–15 artifact / 16–22 general / 23 bag — note this differs from
  the *current* layout documented above, which has no dedicated artifact zone and a 1–5 hotbar
  that also allows `Ability`). Removes `update_inventory`; adds `drop_item`/`swap_slots`/
  `use_item`; rewrites `pickup_drop` to scan general slots first; `Consumable.effect: String` →
  typed `ConsumableEffect` enum; new `LOOT_DROP_EXPIRY` constant.
- **Task 3 — Client Inventory/Hotbar UI**: `LocalPlayerInventory.cs` is currently an empty
  placeholder for this.
- **Task 4 — Loot/Reward Drops**: depends on Task 2. Per-template drop tables, `LootDrop`
  insertion on enemy death.
- **Task 5 — World Zones + Dynamic Spawning**: `EnemyZone` table and `spawn_from_zones` (marked
  `#[allow(dead_code)]`) already exist, but `tick_enemy_spawn` still hardcodes a single `"Enemy"`
  spawn at the origin capped at 1 live enemy total. Needs `EnemyZone` rows authored and
  `tick_enemy_spawn` rewired to call `spawn_from_zones`.

**Minor observations** (not yet actioned — flagged for awareness, not necessarily bugs):

- `give_item` (admin) doesn't validate `ItemType` against the target slot's `allowed_types`, unlike
  `update_inventory`/`pickup_drop`. Left as-is for now — admin tooling will get a pass once the
  inventory rework (Task 2) lands.
- `Scenes/lobby_gui.tscn` / `Scripts/Game/LobbyGui.cs` are leftover/temporary lobby scaffolding —
  no action needed.
