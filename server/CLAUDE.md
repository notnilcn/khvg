# Server Module Map

`server/spacetimedb/src/` is split into six modules: `enemy/`, `player/`, `world/`, `item/`, `main/`, `combat/`. Gameplay modules follow `mod.rs` / `methods.rs` (pure helpers/math) / `reducers.rs` / `views.rs` — **`world/` is the exception**: its old `methods.rs` was split into focused files (`prng`/`noise`/`hex`/`wrap`/`aoi`) plus a composable `terrain/` generation pipeline, and star imports were replaced with explicit `use` lists (structure-only refactor — no schema changes, client bindings verified byte-identical). Every `views.rs` that exists is implemented; `player/views.rs` hosts the shared `nearby_indices`/`nearby_indices_from_chunk` AOI helpers the other modules' views build on.

Composition mapping (mirrors the client's entity/component structure): one table per concern = a component, and ids like `profile_id`/`enemy_id` act as entity ids joining a logical entity's rows across tables. Archetype helpers bundle the rows of one entity: `player/methods.rs::try_scaffold_profile`/`teardown_profile` and `enemy/methods.rs::spawn_enemy_archetype`/`despawn_enemy_archetype`. An enemy's flat row (position/hp/phase on `Enemy` itself) is deliberate.

### `enemy/`
- `def_tables.rs` — static template data: `PatternType` variants (Ring, Volley, Curtain, Shotgun, Explosion, ...) + their param structs, `MovementBehavior` (+ Wander/Chase/Flee params), `EnemyTarget`, attack sequence step defs (`SingleStepDef`, `RepeatStepDef`, `MultiStepDef`, `AttackSequence`, `PhaseDef`), `EnemyTemplate` table
- `instance_tables.rs` — runtime data: `Enemy`, `EnemyBehavior`, `EnemyPhase`, `EnemyAttack`, `EnemySequenceStep`, `RepeatStepInstance`, `BulletPatternEvent` (event table), `EnemySpawnSchedule`/`EnemyBehaviorSchedule` (scheduled tick types). No `EnemyZone` table — spawn regions are `BiomeRegion`, a `world/` module table (`center_hex_q/r`, `spawn_radius`, `max_enemies`, `enemy_template_ids`, `region_id`).
- `reducers.rs` — `tick_enemy_spawn`, `tick_enemy_behavior` (scheduled), `report_enemy_hit`
- `methods.rs` — aggro/targeting, phase/sequence ticking (`tick_phase`, `tick_sequence`), movement (`apply_movement`), biome-region spawning (`spawn_from_biome_regions`), archetype helpers `spawn_enemy_archetype`/`despawn_enemy_archetype` (wrap `build_enemy_behavior`/`delete_enemy_behavior`, so no orphan behavior-tree rows outlive the entity)
- `views.rs` — `nearby_enemies` (filters `Enemy` by the caller's AOI chunk indices via the shared `nearby_indices` helper from `player/views.rs`) and `enemy_templates` (unfiltered, anonymous view of the full `EnemyTemplate` table)

### `player/`
- `tables.rs` — `LoggedInPlayer`/`LoggedOutPlayer` (login state), `PlayerProfile`, `PlayerData` (level/xp/hp), `PlayerStats`, `PlayerInventory`, `PlayerPosition`, `LootDrop`, `InventorySlot`
- `reducers.rs` — `set_username`, `create_profile`, `delete_profile`, `join_world`, `leave_world`, `report_movement`, `report_hit`, `pickup_drop`, `drop_item`, `swap_slots`, `use_item`, `apply_enchantment`, `remove_enchantment`
- `methods.rs` — guards (`require_logged_in`, `require_in_world`, `require_in_lobby`, `is_admin`), stat/level math (`xp_for_level`, `compute_level`, `compute_base_stats`, `recompute_stats`), `internal_gain_xp`, `apply_consumable_effect` (heals delegate to `combat::heal_player`), archetype helpers `try_scaffold_profile`/`teardown_profile` (a single union cleanup list used by BOTH combat death cleanup and `delete_profile` — the two lists previously disagreed on `player_chunk`/`player_profile`), `find_profile_by_name`
- `views.rs` — per-player subscription views (`local_player`, `local_lobby_player`, `local_player_profiles`, `local_player_active_profile`, `local_player_data`, `local_player_stats`, `local_player_inventory`, …) plus the shared `nearby_indices`/`nearby_indices_from_chunk` AOI helpers

### `world/`
- `tables.rs` — `MapConfig` (with `MapConfig::load(ctx, fallback_chunk_cols, fallback_chunk_rows)`, the single fallback helper that replaced ≥6 verbatim fallback blocks — fallbacks differ per call site: sim paths 0/0, admin/views 1/1), `BuildingTile`, `TextureEntry`, `TextureKind`, layering/adjacency/decor-ground rule tables
- `def_tables.rs` — world-gen definition inputs: `WorldDef`, `BiomeDef`, `BiomeRegionDef`, `GroundTextureConfig`/`OverlayTextureConfig`/`DecorConfig`, weight structs, `ManualBiomeInput`/`ManualRegionInput`
- `instance_tables.rs` — generated world rows: `BiomeRegion`, `TriangleTile`, `HexDecor` (the payload that populates the client's `TileComponent` scenes)
- `prng.rs` — deterministic PRNG: `splitmix64`/`hash_to_unit`/`next_unit` + weighted picks
- `noise.rs` — `value_noise2d` + seed offsets
- `hex.rs` — hex/chunk math: `world_to_hex`, `hex_to_world`, `hex_area`/`hex_shift`, `chunk_center_hex`, `world_to_chunk`, `inv_hexmod`, `spiral_chunk_index`, `HexCell`, `enumerate_world_hexes`, neighbor deltas (`same_hex_neighbor_indices`/`cross_hex_neighbor`)
- `wrap.rs` — torus wrap (`wrap_world_pos`/`wrap_chunk_coords`) + wrapped distance
- `aoi.rs` — `surrounding_chunk_indices`
- `terrain/` — terrain generation as composable passes: `rules.rs` (`LayeringRule`/adjacency/decor conflict checks, `NeighborTagMap`), `voronoi.rs` (biome/region seed distribution), `ground.rs` + `overlay.rs` (the two halves of the old `write_triangle_tiles`), `decor.rs` (`place_decor`), `mod.rs` (orchestrator — `internal_generate_world_proc`/`internal_generate_world_manual` are two front-ends over one parameterized `run_generation`; deterministic draw order preserved)
- `reducers.rs` — `place_building`, `remove_building`
- `views.rs` — `all_textures`, `nearby_terrain_tiles`, `nearby_hex_decor` (AOI-filtered via `nearby_indices_from_chunk` with `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`)

### `item/`
- `tables.rs` — single unified item catalog table `Item` (`item_id`, `display_name`, `description`, `texture_id`, `equip_slot`, `stat_modifiers`, `behaviors`, `max_enchantments`); `Enchantment` table (`enchantment_id`, `display_name`, `description`, `texture_id`, `stat_modifiers`, `allowed_slots`); `EquipSlot` enum (Weapon/Artifact/Armor/Accessory/Bag/Consumable); `StatKind`/`StatMode`/`StatModifier`; `ItemBehavior` enum (`Weapon(WeaponBehavior)`, `Consumable(ConsumableBehavior)`) plus `WeaponPattern`/`ConsumableEffect`/`ConsumableBuffEffect`
- `reducers.rs` — admin catalog reducers: `give_item`, `remove_item`, `upsert_item`, `upsert_enchantment`
- `views.rs` — `all_items` (public subscription view clients use for the item cache; see `client/CLAUDE.md` `GameManager.cs`), `all_enchantments`
- `seeds.rs` — `seed_world_items`, `seed_world_enchantments`, `impl Seed for Item` (`impl Seed for Enchantment` lives in `main/seeds.rs`)

### `combat/`
- `mod.rs` — shared combat pipeline, the only place damage is computed and applied: damage formulas (`compute_player_damage` outgoing, `compute_incoming_damage` mitigation), `deal_damage_to_player` (mitigate → subtract → on zero the full death cleanup via the shared `teardown_profile` archetype helper: drops the profile's rows, moves the player to `LoggedOutPlayer`), `deal_damage_to_enemy` (kill → `despawn_enemy_archetype` + `internal_gain_xp`; survive → phase recompute + re-aggro), `heal_player`. `report_hit` and `report_enemy_hit` just resolve base damage and delegate.

### `main/`
- `global.rs` — single home for tunable constants, grouped by subsystem: world gen/map grid (`DEFAULT_CHUNK_HEX_RADIUS`, `DEFAULT_CHUNK_COLS`/`ROWS`, `DEFAULT_HEX_OUTER_RADIUS`, `BIOME_VORONOI_SEED_BUDGET`), AoI/camera (`DEFAULT_AOI_CHUNK_RADIUS`, `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`, `CLIENT_VIEWPORT_HEIGHT_PX`, `CLIENT_CAMERA_ZOOM`, `CAMERA_VIEW_RADIUS`, `SIMULATION_CHUNK_RINGS`, `SPAWN_LOOKAHEAD_CHUNK_RINGS`), player stats (`BASE_MAX_HP`, `HP_PER_LEVEL`), inventory (`GENERAL_SLOT_START`, `GENERAL_SLOT_COUNT`), enemy sim (`BEHAVIOR_TICK_DT`), world timers (`LOOT_DROP_EXPIRY`, `CONSUMABLE_EFFECT_TICK_SECONDS`)
- `lifecycle.rs` — `init`, `client_connected`, `client_disconnected`
- `admin.rs` — admin-only reducers (single global admin slot — `claim_admin` refuses if any `LoggedInPlayer`/`LoggedOutPlayer` already has `is_admin`; `release_admin` is the only way to free it): `claim_admin`/`release_admin`, `change_stats`, `upsert_texture_entry`/`upsert_enemy_template`, `upsert_movement_def`/`upsert_single_step_def`/`upsert_repeat_step_def`/`upsert_multi_step_def` (runtime enemy-authoring upserts — pass `def_id: 0` to insert, an existing id to update), `spawn_enemy`/`despawn_enemy` (both route through `spawn_enemy_archetype`/`despawn_enemy_archetype`, so no orphan or double-inserted behavior-tree rows), `add_chunks`/`clear_chunks`, `upsert_world_def`/`upsert_biome_def`/`upsert_biome_region_def`, `generate_world_proc`/`generate_world_manual` (admin-gated wrappers around `world::terrain::internal_generate_world_proc`/`internal_generate_world_manual`, the same calls `main/lifecycle.rs`'s `init` uses to build the starting world), `find_player_by_username` (matches username, full identity hex, or identity-hex prefix).
- `seeds.rs` — `seed_default_textures/enemies`, test boss seeds, seed-building helpers (`make_phase`, `make_sequence`, `seq_single`/`seq_repeat`/`seq_multi`, `multi_shot`)
- `debug.rs` — `PlayerPositionDebug` table + `tick_player_position_debug`, `toggle_debug`

---

# SpacetimeDB Rules (All Languages)

## Migrating from 1.0 to 2.0?

**If you are migrating existing SpacetimeDB 1.0 code to 2.0, apply `spacetimedb-migration-2.0.mdc` first.** It documents breaking changes (reducer callbacks → event tables, `name`→`accessor`, `sender()` method, etc.) and should be considered before other rules.

---

## Language-Specific Rules

| Language | Rule File |
|----------|-----------|
| **TypeScript/React** | `spacetimedb-typescript.mdc` (MANDATORY) |
| **Rust** | `spacetimedb-rust.mdc` (MANDATORY) |
| **C#** | `spacetimedb-csharp.mdc` (MANDATORY) |
| **Migrating 1.0 → 2.0** | `spacetimedb-migration-2.0.mdc` |

---

## Core Concepts

1. **Reducers are transactional** — they do not return data to callers
2. **Reducers must be deterministic** — no filesystem, network, timers, or random
3. **Read data via tables/subscriptions** — not reducer return values
4. **Auto-increment IDs are not sequential** — gaps are normal, don't use for ordering
5. **`ctx.sender` is the authenticated principal** — never trust identity args

---

## Feature Implementation Checklist

When implementing a feature that spans backend and client:

1. **Backend:** Define table(s) to store the data
2. **Backend:** Define reducer(s) to mutate the data
3. **Client:** Subscribe to the table(s)
4. **Client:** Call the reducer(s) from UI — **don't forget this step!**
5. **Client:** Render the data from the table(s)

**Common mistake:** Building backend tables/reducers but forgetting to wire up the client to call them.

---

## Index System

SpacetimeDB automatically creates indexes for:
- Primary key columns
- Columns marked as unique

You can add explicit indexes on non-unique columns for query performance.

**Index names must be unique across your entire module (all tables).** If two tables have indexes with the same declared name → conflict error.

**Schema ↔ Code coupling:**
- Your query code references indexes by name
- If you add/remove/rename an index in the schema, update all code that uses it
- Removing an index without updating queries causes runtime errors

---

## Commands

```bash
# Login to allow remote database deployment e.g. to maincloud
spacetime login

# Start local SpacetimeDB
spacetime start

# Publish module
spacetime publish <db-name> --module-path <module-path>

# Clear and republish
spacetime publish <db-name> --clear-database -y --module-path <module-path>

# Generate client bindings
spacetime generate --lang <lang> --out-dir <out> --module-path <module-path>

# View logs
spacetime logs <db-name>
```

---

## Deployment

- Maincloud is the spacetimedb hosted cloud and the default location for module publishing
- The default server marked by *** in `spacetime server list` should be used when publishing
- If the default server is maincloud you should publish to maincloud
- Publishing to maincloud is free of charge
- When publishing to maincloud the database dashboard will be at the url: https://spacetimedb.com/@<username>/<database-name>
- The database owner can view utilization and performance metrics on the dashboard

---

## Debugging Checklist

1. Is SpacetimeDB server running? (`spacetime start`)
2. Is the module published? (`spacetime publish`)
3. Are client bindings generated? (`spacetime generate`)
4. Check server logs for errors (`spacetime logs <db-name>`)
5. **Is the reducer actually being called from the client?**

---

## Editing Behavior

- Make the smallest change necessary
- Do NOT touch unrelated files, configs, or dependencies
- Do NOT invent new SpacetimeDB APIs — use only what exists in docs or this repo
- Do NOT add restrictions the prompt didn't ask for — if "users can do X", implement X for all users


# SpacetimeDB Rust SDK

## ⛔ COMMON MISTAKES — LLM HALLUCINATIONS

These are **actual errors** observed when LLMs generate SpacetimeDB Rust code:

### 1. Wrong Crate for Server vs Client

```rust
// ❌ WRONG — using client crate for server module
use spacetimedb_sdk::*;  // This is for CLIENTS only!

// ✅ CORRECT — use spacetimedb for server modules
use spacetimedb::{table, reducer, Table, ReducerContext, Identity, Timestamp};
```

### 2. Wrong Table Macro Syntax

```rust
// ❌ WRONG — using attribute-style like C#
#[spacetimedb::table]
#[primary_key]
pub struct User { ... }

// ❌ WRONG — SpacetimeType on tables (causes conflicts!)
#[derive(SpacetimeType)]
#[table(accessor = my_table)]
pub struct MyTable { ... }

// ✅ CORRECT — use #[table(...)] macro with options, NO SpacetimeType
#[table(accessor = user, public)]
pub struct User {
    #[primary_key]
    identity: Identity,
    name: Option<String>,
}
```

### 3. Wrong Table Access Pattern

```rust
// ❌ WRONG — using ctx.Db or ctx.db() method or field access
ctx.Db.user.Insert(...);
ctx.db().user().insert(...);
ctx.db.player;  // Field access

// ✅ CORRECT — ctx.db is a field, table names are methods with parentheses
ctx.db.user().insert(User { ... });
ctx.db.user().identity().find(ctx.sender);
ctx.db.player().id().find(&player_id);
```

### 4. Wrong Update Pattern

```rust
// ❌ WRONG — partial update or using .update() directly on table
ctx.db.user().update(User { name: Some("new".into()), ..Default::default() });

// ✅ CORRECT — find existing, spread it, update via primary key accessor
if let Some(user) = ctx.db.user().identity().find(ctx.sender) {
    ctx.db.user().identity().update(User { name: Some("new".into()), ..user });
}
```

### 5. Wrong Reducer Return Type

```rust
// ❌ WRONG — returning data from reducer
#[reducer]
pub fn get_user(ctx: &ReducerContext, id: Identity) -> Option<User> { ... }

// ❌ WRONG — mutable context
pub fn my_reducer(ctx: &mut ReducerContext, ...) { }

// ✅ CORRECT — reducers return Result<(), String> or nothing, immutable context
#[reducer]
pub fn do_something(ctx: &ReducerContext, value: String) -> Result<(), String> {
    if value.is_empty() {
        return Err("Value cannot be empty".to_string());
    }
    Ok(())
}
```

### 6. Wrong Client Connection Pattern

```rust
// ❌ WRONG — subscribing before connected
let conn = DbConnection::builder().build()?;
conn.subscription_builder().subscribe_to_all_tables();  // NOT CONNECTED YET!

// ✅ CORRECT — subscribe in on_connect callback
DbConnection::builder()
    .on_connect(|conn, identity, token| {
        conn.subscription_builder()
            .on_applied(|ctx| println!("Ready!"))
            .subscribe_to_all_tables();
    })
    .build()?;
```

### 7. Forgetting to Advance the Connection

```rust
// ❌ WRONG — connection never processes messages
let conn = DbConnection::builder().build()?;
// ... callbacks never fire ...

// ✅ CORRECT — must call one of these to process messages
conn.run_threaded();           // Spawn background thread
// OR
conn.run_async().await;        // Async task
// OR (in game loop)
conn.frame_tick()?;            // Manual polling
```

### 8. Missing Table Trait Import

```rust
// ❌ WRONG — "no method named `insert` found"
use spacetimedb::{table, reducer, ReducerContext};
ctx.db.user().insert(...);  // ERROR!

// ✅ CORRECT — import Table trait for table methods
use spacetimedb::{table, reducer, Table, ReducerContext};
ctx.db.user().insert(...);  // Works!
```

### 9. Wrong ScheduleAt Variant

```rust
// ❌ WRONG — At variant doesn't exist
scheduled_at: ScheduleAt::At(future_time),

// ✅ CORRECT — use Time variant
scheduled_at: ScheduleAt::Time(future_time),
```

### 10. Identity to String Conversion

```rust
// ❌ WRONG — to_hex() returns HexString<32>, not String
let id: String = identity.to_hex();  // Type mismatch!

// ✅ CORRECT — chain .to_string()
let id: String = identity.to_hex().to_string();
```

### 11. Timestamp Duration Extraction

```rust
// ❌ WRONG — returns Result, not Duration directly
let micros = ctx.timestamp.to_duration_since_unix_epoch().as_micros();

// ✅ CORRECT — unwrap the Result
let micros = ctx.timestamp.to_duration_since_unix_epoch()
    .unwrap_or_default()
    .as_micros();
```

### 12. Borrow After Move

```rust
// ❌ WRONG — `tool` moved into struct, then borrowed
ctx.db.stroke().insert(Stroke { tool, color, ... });
if tool == "eraser" { ... }  // ERROR: value moved!

// ✅ CORRECT — check before move, or use clone
let is_eraser = tool == "eraser";
ctx.db.stroke().insert(Stroke { tool, color, ... });
if is_eraser { ... }
```

### 13. Client SDK Uses Blocking I/O

The SpacetimeDB Rust client SDK uses blocking I/O. If mixing with async runtimes (Tokio, async-std), use `spawn_blocking` or run the SDK on a dedicated thread to avoid blocking the async executor.

### 14. Wrong Schedule Syntax
```rust
// ❌ WRONG — `schedule` is not a valid table type
#[table(name = tick_timer, schedule(reducer = tick, column = scheduled_at))]

// ✅ CORRECT — `scheduled` is a valid table type
#[table(name = tick_timer, scheduled(reducer = tick, column = scheduled_at))]
```
---

## 1) Common Mistakes Table

### Server-side errors

| Wrong | Right | Error |
|-------|-------|-------|
| `#[derive(SpacetimeType)]` on `#[table]` | Remove it — macro handles this | Conflicting derive macros |
| `ctx.db.player` (field access) | `ctx.db.player()` (method) | "no field `player` on type" |
| `ctx.db.player().find(id)` | `ctx.db.player().id().find(&id)` | Must access via index |
| `&mut ReducerContext` | `&ReducerContext` | Wrong context type |
| Missing `use spacetimedb::Table;` | Add import | "no method named `insert`" |
| `#[table(accessor = "my_table")]` | `#[table(accessor = my_table)]` | String literals not allowed |
| Missing `public` on table | Add `public` flag | Clients can't subscribe |
| `#[spacetimedb::reducer]` | `#[reducer]` after import | Wrong attribute path |
| Network/filesystem in reducer | Use procedures instead | Sandbox violation |
| Panic for expected errors | Return `Result<(), String>` | WASM instance destroyed |

---

## 2) Table Definition (CRITICAL)

**Tables use `#[table(...)]` macro on `pub struct`. DO NOT derive `SpacetimeType` on tables!**

> ⚠️ **CRITICAL:** Always import `Table` trait — required for `.insert()`, `.iter()`, `.find()`, etc.

```rust
use spacetimedb::{table, reducer, Table, ReducerContext, Identity, Timestamp};

// ❌ WRONG — DO NOT derive SpacetimeType on tables!
#[derive(SpacetimeType)]  // REMOVE THIS!
#[table(accessor = task)]
pub struct Task { ... }

// ✅ CORRECT — just the #[table] attribute
#[table(accessor = user, public)]
pub struct User {
    #[primary_key]
    identity: Identity,
    
    #[unique]
    username: Option<String>,
    
    online: bool,
}

#[table(accessor = message, public)]
pub struct Message {
    #[primary_key]
    #[auto_inc]
    id: u64,
    
    sender: Identity,
    text: String,
    sent: Timestamp,
}

// With multi-column index
#[table(accessor = task, public, index(name = by_owner, btree(columns = [owner_id])))]
pub struct Task {
    #[primary_key]
    #[auto_inc]
    pub id: u64,
    pub owner_id: Identity,
    pub title: String,
}
```

### Table Options

```rust
#[table(accessor = my_table)]           // Private table (default)
#[table(accessor = my_table, public)]   // Public table - clients can subscribe
```

### Column Attributes

```rust
#[primary_key]           // Primary key (auto-indexed, enables .find())
#[auto_inc]              // Auto-increment (use with #[primary_key])
#[unique]                // Unique constraint (auto-indexed)
#[index(btree)]          // B-Tree index for queries
```

### Insert returns ROW, not ID

```rust
let row = ctx.db.task().insert(Task {
    id: 0,  // auto-inc placeholder
    owner_id: ctx.sender,
    title: "New task".to_string(),
    created_at: ctx.timestamp,
});
let new_id = row.id;  // Get the actual ID
```

---

## 3) Reducers

### Definition Syntax

```rust
use spacetimedb::{reducer, ReducerContext, Table};

#[reducer]
pub fn send_message(ctx: &ReducerContext, text: String) -> Result<(), String> {
    // Validate input
    if text.is_empty() {
        return Err("Message cannot be empty".to_string());
    }
    
    // Insert returns the inserted row
    let row = ctx.db.message().insert(Message {
        id: 0,  // auto-inc placeholder
        sender: ctx.sender,
        text,
        sent: ctx.timestamp,
    });
    
    log::info!("Message {} sent by {:?}", row.id, ctx.sender);
    Ok(())
}
```

### Update Pattern (CRITICAL)

```rust
#[reducer]
pub fn set_name(ctx: &ReducerContext, name: String) -> Result<(), String> {
    // Find existing row
    let user = ctx.db.user().identity().find(ctx.sender)
        .ok_or("User not found")?;
    
    // ✅ CORRECT — spread existing row, override specific fields
    ctx.db.user().identity().update(User {
        name: Some(name),
        ..user  // Preserves identity, online, etc.
    });
    
    Ok(())
}

// ❌ WRONG — partial update nulls out other fields!
// ctx.db.user().identity().update(User { identity: ctx.sender, name: Some(name), ..Default::default() });
```

### Delete Pattern

```rust
#[reducer]
pub fn delete_message(ctx: &ReducerContext, message_id: u64) -> Result<(), String> {
    ctx.db.message().id().delete(&message_id);
    Ok(())
}
```

### Lifecycle Hooks

```rust
#[reducer(init)]
pub fn init(ctx: &ReducerContext) {
    // Called when module is first published
}

#[reducer(client_connected)]
pub fn client_connected(ctx: &ReducerContext) {
    // ctx.sender is the connecting identity
    if let Some(user) = ctx.db.user().identity().find(ctx.sender) {
        ctx.db.user().identity().update(User { online: true, ..user });
    } else {
        ctx.db.user().insert(User {
            identity: ctx.sender,
            username: None,
            online: true,
        });
    }
}

#[reducer(client_disconnected)]
pub fn client_disconnected(ctx: &ReducerContext) {
    if let Some(user) = ctx.db.user().identity().find(ctx.sender) {
        ctx.db.user().identity().update(User { online: false, ..user });
    }
}
```

### ReducerContext fields

```rust
ctx.sender          // Identity of the caller
ctx.timestamp       // Current timestamp
ctx.db              // Database access
ctx.rng             // Deterministic RNG (use instead of rand)
```

---

## 4) Index Access

### Primary Key / Unique — `.find()` returns `Option<Row>`

```rust
// Primary key lookup
let user = ctx.db.user().identity().find(ctx.sender);

// Unique column lookup  
let user = ctx.db.user().username().find(&"alice".to_string());

if let Some(user) = user {
    // Found
}
```

### BTree Index — `.filter()` returns iterator

```rust
#[table(accessor = message, public)]
pub struct Message {
    #[primary_key]
    #[auto_inc]
    id: u64,
    
    #[index(btree)]
    room_id: u64,
    
    text: String,
}

// Filter by indexed column
for msg in ctx.db.message().room_id().filter(&room_id) {
    // Process each message in room
}
```

### No Index — `.iter()` + manual filter

```rust
// Full table scan
for user in ctx.db.user().iter() {
    if user.online {
        // Process online users
    }
}
```

---

## 5) Custom Types

**Use `#[derive(SpacetimeType)]` ONLY for custom structs/enums used as fields or parameters.**

```rust
use spacetimedb::SpacetimeType;

// Custom struct for table fields
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub struct Position {
    pub x: i32,
    pub y: i32,
}

// Custom enum
#[derive(SpacetimeType, Clone, Debug, PartialEq)]
pub enum PlayerStatus {
    Idle,
    Walking(Position),
    Fighting(Identity),
}

// Use in table (DO NOT derive SpacetimeType on the table!)
#[table(accessor = player, public)]
pub struct Player {
    #[primary_key]
    pub id: Identity,
    pub position: Position,
    pub status: PlayerStatus,
}
```

---

## 6) Scheduled Tables

```rust
use spacetimedb::{table, reducer, ReducerContext, ScheduleAt, Timestamp};

#[table(accessor = cleanup_job, scheduled(cleanup_expired))]
pub struct CleanupJob {
    #[primary_key]
    #[auto_inc]
    scheduled_id: u64,
    
    scheduled_at: ScheduleAt,
    target_id: u64,
}

#[reducer]
pub fn cleanup_expired(ctx: &ReducerContext, job: CleanupJob) {
    // Job row is auto-deleted after reducer completes
    log::info!("Cleaning up: {}", job.target_id);
}

// Schedule a job
#[reducer]
pub fn schedule_cleanup(ctx: &ReducerContext, target_id: u64, delay_ms: u64) {
    let future_time = ctx.timestamp + std::time::Duration::from_millis(delay_ms);
    ctx.db.cleanup_job().insert(CleanupJob {
        scheduled_id: 0,  // auto-inc placeholder
        scheduled_at: ScheduleAt::Time(future_time),
        target_id,
    });
}

// Cancel by deleting the row
#[reducer]
pub fn cancel_cleanup(ctx: &ReducerContext, job_id: u64) {
    ctx.db.cleanup_job().scheduled_id().delete(&job_id);
}
```

---

## 7) Client SDK

```rust
// Connection pattern
let conn = DbConnection::builder()
    .with_uri("http://localhost:3000")
    .with_module_name("my-module")
    .with_token(load_saved_token())  // None for first connection
    .on_connect(on_connected)
    .build()
    .expect("Failed to connect");

// Subscribe in on_connect callback, NOT before!
fn on_connected(conn: &DbConnection, identity: Identity, token: &str) {
    conn.subscription_builder()
        .on_applied(|ctx| println!("Ready!"))
        .subscribe_to_all_tables();
}
```

### ⚠️ CRITICAL: Advance the Connection

**You MUST call one of these** — without it, no callbacks fire:

```rust
conn.run_threaded();           // Background thread (simplest)
conn.run_async().await;        // Async task
conn.frame_tick()?;            // Manual polling (game loops)
```

### Table Access & Callbacks

```rust
// Iterate
for user in ctx.db.user().iter() { ... }

// Find by primary key
if let Some(user) = ctx.db.user().identity().find(&identity) { ... }

// Row callbacks
ctx.db.user().on_insert(|ctx, user| { ... });
ctx.db.user().on_update(|ctx, old, new| { ... });
ctx.db.user().on_delete(|ctx, user| { ... });

// Call reducers
ctx.reducers.set_name("Alice".to_string()).unwrap();
```

---

## 8) Procedures (Beta)

**Procedures are for side effects (HTTP, filesystem) that reducers can't do.**

⚠️ Procedures are currently in beta. API may change.

```rust
use spacetimedb::{procedure, ProcedureContext};

// Simple procedure
#[procedure]
fn add_numbers(_ctx: &mut ProcedureContext, a: u32, b: u32) -> u64 {
    a as u64 + b as u64
}

// Procedure with database access
#[procedure]
fn save_external_data(ctx: &mut ProcedureContext, url: String) -> Result<(), String> {
    // HTTP request (allowed in procedures, not reducers)
    let data = fetch_from_url(&url)?;

    // Database access requires explicit transaction
    ctx.try_with_tx(|tx| {
        tx.db.external_data().insert(ExternalData {
            id: 0,
            content: data,
        });
        Ok(())
    })?;

    Ok(())
}
```

### Key differences from reducers

| Reducers | Procedures |
|----------|------------|
| `&ReducerContext` (immutable) | `&mut ProcedureContext` (mutable) |
| Direct `ctx.db` access | Must use `ctx.with_tx()` |
| No HTTP/network | HTTP allowed |
| No return values | Can return data |

---

## 9) Logging

```rust
use spacetimedb::log;

log::trace!("Detailed trace");
log::debug!("Debug info");
log::info!("Information");
log::warn!("Warning");
log::error!("Error occurred");
```

---

## 10) Commands

```bash
# Start local server
spacetime start

# Publish module
spacetime publish <module-name> --module-path <backend-dir>

# Clear database and republish
spacetime publish <module-name> --clear-database -y --module-path <backend-dir>

# Generate bindings
spacetime generate --lang rust --out-dir <client>/src/module_bindings --module-path <backend-dir>

# View logs
spacetime logs <module-name>
```

---

## 11) Hard Requirements

**Rust-specific:**

1. **DO NOT derive `SpacetimeType` on `#[table]` structs** — the macro handles this
2. **Import `Table` trait** — `use spacetimedb::Table;` required for `.insert()`, `.iter()`, etc.
3. **Use `&ReducerContext`** — not `&mut ReducerContext`
4. **Tables are methods** — `ctx.db.table()` not `ctx.db.table`
5. **Server modules use `spacetimedb` crate** — clients use `spacetimedb-sdk`
6. **Reducers must be deterministic** — no filesystem, network, timers, or external RNG
7. **Use `ctx.rng`** — not `rand` crate for random numbers
8. **Use `ctx.timestamp`** — never `std::time::SystemTime::now()` in reducers
9. **Client MUST advance connection** — call `run_threaded()`, `run_async()`, or `frame_tick()`
10. **Subscribe in `on_connect` callback** — not before connection is established
11. **Update requires full row** — spread existing row with `..existing`
12. **DO NOT edit generated bindings** — regenerate with `spacetime generate`
13. **Identity to String needs `.to_string()`** — `identity.to_hex().to_string()`
14. **Client SDK is blocking** — use `spawn_blocking` or dedicated thread if mixing with async runtimes
