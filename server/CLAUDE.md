# server/CLAUDE.md

Per-module map of the SpacetimeDB module (`server/spacetimedb/`, crate `BulletHell`, `spacetimedb 2.2`), followed by the Rust/SpacetimeDB rules that apply to every edit here. See the root `CLAUDE.md` for the project-wide domain map and the publish workflow.

# Module Map

`server/spacetimedb/src/` is split into seven modules: `enemy/`, `player/`, `world/`, `item/`, `main/`, `combat/`, `anticheat/`. The baseline layout is `mod.rs` / `tables.rs` / `methods.rs` (pure helpers/math) / `reducers.rs` / `views.rs`, with three deliberate departures: `world/` split its `methods.rs` into focused files (`prng`/`noise`/`hex`/`wrap`/`aoi`) plus a composable `terrain/` pipeline; `item/` has no `methods.rs` (its only logic is the two behavior lookups, which live in `player/methods.rs` beside their callers); and `combat/` is a single `mod.rs`. `player/views.rs` hosts the shared `nearby_indices`/`nearby_indices_from_chunk` AOI helpers the other modules' views build on. Nothing here is stubbed — no `todo!`/`unimplemented!` anywhere in the module.

Composition mapping (mirrors the client's entity/component structure): one table per concern = a component, and ids like `profile_id`/`enemy_id` act as entity ids joining a logical entity's rows across tables. Archetype helpers bundle the rows of one entity: `player/methods.rs::try_scaffold_profile`/`teardown_profile` and `enemy/methods.rs::spawn_enemy_archetype`/`despawn_enemy_archetype`. An enemy's flat row (position/hp/phase on `Enemy` itself) is deliberate.

### `enemy/`
- `def_tables.rs` — static template data. Tables: `MovementDef`, `SingleStepDef`, `RepeatStepDef`, `MultiStepDef`, `EnemyTemplate` (each with an `upsert_*` admin reducer). Embedded types: `PatternType` variants (Ring, Volley, Curtain, Shotgun, Explosion, ...) + their param structs, `MovementBehavior` (+ Wander/Chase/Flee params), `EnemyTarget`, `AttackSequence`, `PhaseDef`
- `instance_tables.rs` — runtime data: `Enemy`, `EnemyBehavior`, `EnemyPhase`, `EnemyAttack`, `EnemySequenceStep`, `RepeatStepInstance`, `BulletPatternEvent` (event table), `BulletControlEvent` (event table relaying player bullet-controller casts — delete/split/attract — with `BulletControlKind`; server tracks no live bullets, clients resolve the proximity effect locally), `EnemySpawnSchedule`/`EnemyBehaviorSchedule` (scheduled tick types). No `EnemyZone` table — spawn regions are `BiomeRegion`, a `world/` table (`center_hex_q/r`, `spawn_radius`, `max_enemies`, `enemy_template_ids`, `region_id`).
- `reducers.rs` — `tick_enemy_spawn`, `tick_enemy_behavior` (scheduled), `report_enemy_hit`, `control_bullets` (validates in-world, appends a `BulletControlEvent`)
- `methods.rs` — aggro/targeting, phase/sequence ticking (`tick_phase`, `tick_sequence`), movement (`apply_movement`), biome-region spawning (`spawn_from_biome_regions`), archetype helpers `spawn_enemy_archetype`/`despawn_enemy_archetype` (wrap `build_enemy_behavior`/`delete_enemy_behavior`, so no orphan behavior-tree rows outlive the entity)
- `views.rs` — `nearby_enemies` (filters `Enemy` by the caller's AOI chunk indices via `nearby_indices`) and `enemy_templates` (unfiltered, anonymous view of the full table)

### `player/`
- `tables.rs` — `LoggedInPlayer`/`LoggedOutPlayer` (login state), `PlayerProfile`, `PlayerData` (level/xp/hp), `PlayerStats`, `PlayerInventory`, `PlayerPosition`, `PlayerChunk`, `ActiveConsumableEffect` + `ConsumableEffectSchedule`, `LootDrop`. `InventorySlot` is **not** a table — it is the struct embedded in `PlayerInventory.slots`, a fixed 24-entry `Vec` whose indices are load-bearing on both sides: `0` weapon, `1–4` hotbar consumables, `5–8` accessory, `9–12` armor, `13–14` artifact, `15–22` general (`GENERAL_SLOT_START`/`GENERAL_SLOT_COUNT`), `23` bag.
- **`PlayerChunk` exists so views don't key off `PlayerPosition`.** It is written only when the player's chunk actually changes, so AOI views recompute on real chunk crossings instead of on every 10 Hz `report_movement`. Don't "simplify" an AOI view to read `PlayerPosition`.
- `reducers.rs` — `set_username`, `create_profile`, `delete_profile`, `join_world`, `leave_world`, `report_movement`, `report_hit`, `pickup_drop`, `drop_item`, `swap_slots`, `use_item`, `apply_enchantment`, `remove_enchantment`, `tick_active_consumable_effects` (scheduled, wired to `ConsumableEffectSchedule`)
- `methods.rs` — guards (`require_logged_in`, `require_in_world`, `require_in_lobby`, `is_admin`), stat/level math (`xp_for_level`, `compute_level`, `compute_base_stats`, `compute_base_max_hp`, `recompute_stats`), the `weapon_behavior`/`consumable_behavior` item-behavior lookups, `internal_gain_xp`, `apply_consumable_effect` (heals delegate to `combat::heal_player`), archetype helpers `try_scaffold_profile`/`teardown_profile` (one union cleanup list used by BOTH combat death cleanup and `delete_profile` — the two lists previously disagreed on `player_chunk`/`player_profile`), `find_profile_by_name`
- `views.rs` — per-player subscription views (`local_player`, `local_lobby_player`, `local_player_profiles`, `local_player_active_profile`, `local_player_data`, `local_player_stats`, `local_player_inventory`, `local_player_position`, `nearby_remote_players`, `nearby_remote_players_profiles`, `nearby_loot_drops`) plus the shared `nearby_indices`/`nearby_indices_from_chunk` AOI helpers

**`recompute_stats` is the one place stats resolve.** It folds every `StatModifier` from equipped items, their socketed enchantments, and active consumable buffs into per-`StatKind` `(flat, mult)` accumulators, then resolves `(base + flat) * (1 + mult)`. Two design rules live here:
- **Mults are additive, not compounding** — two `+10%` sources give `+20%`, not `+21%`.
- **"Equipped" means `slot.allowed_slots.len() == 1`**, so *any* single-purpose slot contributes, including the weapon and bag slots.
- `StatKind::Hp` has no `PlayerStats` field; it resolves into `PlayerData.max_hp` (over `compute_base_max_hp`), with `hp` clamped down so a player can't sit above their cap after unequipping.

### `world/`
- `tables.rs` — `MapConfig`, `BuildingTile`, `TextureEntry`, `LayeringRule`, `BaseAdjacencyRule`, `OverlayAdjacencyRule`, `DecorGroundRule` (+ the `TextureKind` enum). Read config through **`MapConfig::load(ctx, fallback_chunk_cols, fallback_chunk_rows)`** rather than re-deriving defaults; the fallback differs by caller — simulation paths pass `0, 0`, `main/admin.rs` passes `1, 1`. `player/views.rs` is the one place that reads `map_config` directly (a `ViewContext` can't call the helper), defaulting both to `1`.
- `def_tables.rs` — world-gen definition inputs: `WorldDef`, `BiomeDef`, `BiomeRegionDef`, `GroundTextureConfig`/`OverlayTextureConfig`/`DecorConfig`, weight structs, `ManualBiomeInput`/`ManualRegionInput`
- `instance_tables.rs` — generated world rows: `BiomeRegion`, `TriangleTile`, `HexDecor` (the payload that populates the client's `TileComponent` scenes)
- `prng.rs` — deterministic PRNG: `splitmix64`/`hash_to_unit`/`next_unit` + weighted picks
- `noise.rs` — `value_noise2d` + seed offsets
- `hex.rs` — hex/chunk math, mostly thin wrappers over the **`hexx` crate** (`to_higher_res`/`to_lower_res`/`from_hexmod_coordinates`): `world_to_hex`, `hex_to_world`, `hex_area`/`hex_shift`, `chunk_center_hex`, `world_to_chunk`, `inv_hexmod`, `HexCell`, `enumerate_world_hexes`, neighbor deltas (`same_hex_neighbor_indices`/`cross_hex_neighbor`), plus `spiral_chunk_index` — a hand-rolled bijective hex-spiral chunk id **duplicated client-side as `HexSpiralIndex` in both `HexGridOverlayComponent.cs` and `HexGridOverlay3DComponent.cs`**; all three must change together. (The in-file comment still points at a `hex_grid_overlay.gd` that no longer exists — the client is C#-only.) Background on the coordinate schemes: [redblobgames hexagons](https://www.redblobgames.com/grids/hexagons/), [hexagon tiling of a hexagonal grid](https://observablehq.com/@sanderevers/hexagon-tiling-of-an-hexagonal-grid), [hexmod representation](https://observablehq.com/@sanderevers/hexmod-representation).
- `wrap.rs` — torus wrap (`wrap_world_pos`/`wrap_chunk_coords`) + wrapped distance. The continuous world is a torus; the hex grid is not.
- `aoi.rs` — `surrounding_chunk_indices`
- `terrain/` — terrain generation as composable passes: `rules.rs` (`LayeringRule`/adjacency/decor conflict checks, `NeighborTagMap`), `voronoi.rs` (biome/region seed distribution), `ground.rs` + `overlay.rs` (`choose_ground`/`choose_overlay`, called per hex by `write_triangle_tiles` in `mod.rs`), `decor.rs` (`place_decor`), `mod.rs` (orchestrator — `internal_generate_world_proc`/`internal_generate_world_manual` are two front-ends over one parameterized `run_generation`; deterministic draw order preserved)
- `reducers.rs` — `place_building`, `remove_building`
- `views.rs` — `all_textures`, `nearby_terrain_tiles`, `nearby_hex_decor` (AOI-filtered via `nearby_indices_from_chunk` with `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`)

### `item/`
- `tables.rs` — single unified catalog table `Item` (`item_id`, `display_name`, `description`, `texture_id`, `equip_slot`, `stat_modifiers`, `behaviors`, `max_enchantments`); `Enchantment` (`enchantment_id`, …, `stat_modifiers`, `allowed_slots`); `EquipSlot` enum (Weapon/Artifact/Armor/Accessory/Bag/Consumable); `StatKind`/`StatMode`/`StatModifier`; `ItemBehavior` enum (`Weapon(WeaponBehavior)`, `Consumable(ConsumableBehavior)`) plus `WeaponPattern`/`ConsumableEffect`/`ConsumableBuffEffect`
- `reducers.rs` — admin catalog reducers: `give_item`, `remove_item`, `upsert_item`, `upsert_enchantment`
- `views.rs` — `all_items` (the public view the client's `CatalogComponent` caches), `all_enchantments`
- `seeds.rs` — `seed_world_items`, `seed_world_enchantments`, `impl Seed for Item` (`impl Seed for Enchantment` lives in `main/seeds.rs`)

**Slot and behavior are deliberately independent axes** — `equip_slot` says only *where an item goes*, `stat_modifiers` + `behaviors` say *what it does*. That is what makes "an accessory that fires projectiles" expressible (`equip_slot: Accessory, behaviors: [Weapon(..)]`) and is the prerequisite for procedural item generation. Consequences worth knowing before changing this:
- There are **no per-type side tables** (no `Weapon`/`Armor`/`Artifact`/`Consumable` rows to join) and no `ArtifactEffect` — an artifact "effect" is just a `StatModifier` with `mode: Flat`. Look up capability with `weapon_behavior(&item)` / `consumable_behavior(&item)`, never by inspecting `equip_slot`.
- `use_item` guards on **having a `Consumable` behavior**, not on `equip_slot == Consumable` — usability is a behavior test.
- The same `Vec<enum>`-on-one-row shape is used by `EnemyTemplate.phases` and `PlayerInventory.slots`.
- Not built yet: the procedural item generator; `Bag` capacity as a behavior (`PlayerInventory.slots` is a fixed 24). Historical rationale for this schema: `docs/Technical Design Docs/(implemented) Item system - split stats from behavior.md`.

### `combat/`
- `mod.rs` — shared combat pipeline, the only place damage is computed and applied: damage formulas (`compute_player_damage` outgoing, `compute_incoming_damage` mitigation), `deal_damage_to_player` (mitigate → subtract → on zero the full death cleanup via the shared `teardown_profile` archetype helper: drops the profile's rows, moves the player to `LoggedOutPlayer`), `deal_damage_to_enemy` (kill → `despawn_enemy_archetype` + `internal_gain_xp`; survive → phase recompute + re-aggro), `heal_player`. `report_hit` and `report_enemy_hit` just resolve base damage and delegate. The `PlayerDamageOutcome`/`EnemyDamageOutcome` payloads are earmarked for the slash/bullet-despawn protocol sketched in `src/plan.md` — a proposal, not a spec; none of it is implemented.

### `anticheat/`
- `tables.rs` — `PlayerHitFlag` (server-only, not `public`): a witness flag that some client saw `hit_player` get hit by a bullet (`reported_by`, `reported_at`; no bullet identity). Used to corroborate the victim's self-reported `report_hit`.
- `reducers.rs` — `flag_player_hit` (requires in-world; self-reports silently ignored)

### `main/`
- `global.rs` — single home for tunable constants, grouped by subsystem: world gen/map grid (`DEFAULT_CHUNK_HEX_RADIUS`, `DEFAULT_CHUNK_COLS`/`ROWS`, `DEFAULT_HEX_OUTER_RADIUS`, `BIOME_VORONOI_SEED_BUDGET`), AoI/camera (`DEFAULT_AOI_CHUNK_RADIUS`, `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`, `CLIENT_VIEWPORT_HEIGHT_PX`, `CLIENT_CAMERA_ZOOM`, `CAMERA_VIEW_RADIUS`, `SIMULATION_CHUNK_RINGS`, `SPAWN_LOOKAHEAD_CHUNK_RINGS`), player stats (`BASE_MAX_HP`, `HP_PER_LEVEL`), inventory (`GENERAL_SLOT_START`, `GENERAL_SLOT_COUNT`), enemy sim (`BEHAVIOR_TICK_DT`), world timers (`LOOT_DROP_EXPIRY`, `CONSUMABLE_EFFECT_TICK_SECONDS`)
- `lifecycle.rs` — `init`, `client_connected`, `client_disconnected`
- `admin.rs` — admin-only reducers (single global admin slot — `claim_admin` refuses if any `LoggedInPlayer`/`LoggedOutPlayer` already has `is_admin`; `release_admin` is the only way to free it): `claim_admin`/`release_admin`, `change_stats`, `upsert_texture_entry`/`upsert_enemy_template`, `upsert_movement_def`/`upsert_single_step_def`/`upsert_repeat_step_def`/`upsert_multi_step_def` (runtime enemy-authoring upserts — pass `def_id: 0` to insert, an existing id to update), `spawn_enemy`/`despawn_enemy` (both route through the archetype helpers, so no orphan or double-inserted behavior-tree rows), `add_chunks`/`clear_chunks`, `upsert_world_def`/`upsert_biome_def`/`upsert_biome_region_def`, `generate_world_proc`/`generate_world_manual` (admin-gated wrappers around `world::terrain::internal_generate_world_*`, the same calls `init` uses to build the starting world), `internal_add_chunks` (the non-gated chunk-creation helper `init` calls directly), `find_player_by_username` (matches username, full identity hex, or identity-hex prefix)
- `seeds.rs` — the `Seed` trait (+ impls for `MapConfig`/`TextureEntry`/`EnemyTemplate`/`WorldDef`/`BiomeDef`/`BiomeRegionDef`/`Enchantment`), `seed_default_textures`/`seed_default_enemies`, world-gen seeds (`seed_world_defs`, `seed_region_def`, `seed_layering_rules`, `seed_adjacency_rules`, `seed_decor_ground_rules`), test boss seeds (incl. `seed_test_boss_p3_clone`), seed-building helpers (`make_phase`, `make_sequence`, `seq_single`/`seq_repeat`/`seq_multi`, `multi_shot`)
- `debug.rs` — `PlayerPositionDebug` table + its `PlayerPositionDebugSchedule` scheduled table + `tick_player_position_debug`, `toggle_debug`

---

# SpacetimeDB Rust Rules

## Core concepts

1. **Reducers are transactional** and return no data to callers — clients read data through tables/subscriptions.
2. **Reducers must be deterministic** — no filesystem, network, timers, or external RNG. Use `ctx.rng` and `ctx.timestamp`.
3. **`ctx.sender()` is the authenticated principal** — never trust an identity passed as an argument. It is a **method call**, not a field, in every context (all 50 uses in this module have the parens); `ctx.timestamp` *is* a plain field. Writing `ctx.sender` is the single most common compile error here, inherited from 1.0-era examples.
4. **Auto-increment IDs are not sequential** — gaps are normal; never use them for ordering.
5. **Server modules use the `spacetimedb` crate**, not `spacetimedb_sdk` (that is the Rust *client* SDK; this project's client is C#).
6. **Never hand-edit `client/Scripts/module_bindings/`** — regenerate via `server/build.sh`.

## Mistakes that actually happen

| Wrong | Right | Symptom |
|-------|-------|---------|
| `#[derive(SpacetimeType)]` on a `#[table]` struct | Drop it — the `#[table]` macro handles it | Conflicting derive macros |
| `#[spacetimedb::table]` + `#[primary_key]` C#-style | `#[table(accessor = user, public)]` on the struct | Wrong attribute path |
| `#[table(accessor = "my_table")]` | `#[table(accessor = my_table)]` | String literals not allowed |
| Missing `public` on a table clients subscribe to | Add the `public` flag | Clients can't subscribe |
| `ctx.db.player` / `ctx.db().player()` / `ctx.Db.player` | `ctx.db.player()` — `db` is a field, tables are methods | "no field `player` on type" |
| `ctx.db.player().find(id)` | `ctx.db.player().id().find(&id)` — always via an index | Must access via index |
| Missing `use spacetimedb::Table;` | Import the `Table` trait | "no method named `insert`" |
| `&mut ReducerContext` | `&ReducerContext` | Wrong context type |
| Reducer returning data (`-> Option<User>`) | `-> Result<(), String>` or nothing | — |
| Panicking on an expected error | Return `Err(...)` | WASM instance destroyed |
| Network/filesystem in a reducer | Use a procedure | Sandbox violation |
| `..Default::default()` in an update | Spread the existing row: `..user` | Silently nulls every other field |
| `ctx.sender` | `ctx.sender()` — a method, everywhere | "no field `sender`" |
| `scheduled(...)` written as `schedule(...)`, or spelled with `reducer =`/`column =` args | `#[table(accessor = tick_timer, scheduled(tick_enemy_spawn))]` — just the reducer name | "not a valid table type" |
| `ScheduleAt::At(t)` | `ScheduleAt::Time(t)` | No such variant |
| `let id: String = identity.to_hex();` | `identity.to_hex().to_string()` | `HexString<32>` type mismatch |
| `ctx.timestamp.to_duration_since_unix_epoch().as_micros()` | `.unwrap_or_default().as_micros()` — it returns a `Result` | Type mismatch |
| Moving a value into a struct then reading it | Compute the check before the move, or `clone()` | "value moved" |

## Canonical shapes

```rust
use spacetimedb::{table, reducer, Table, ReducerContext, Identity, Timestamp, ScheduleAt};

// Table — `public` means clients can subscribe; add `event` for append-only event tables
// (see BulletPatternEvent). Add explicit indexes for non-unique query columns.
#[table(accessor = task, public, index(name = by_owner, btree(columns = [owner_id])))]
pub struct Task {
    #[primary_key] #[auto_inc] pub id: u64,   // 0 is the auto-inc placeholder
    #[unique] pub slug: String,
    #[index(btree)] pub owner_id: Identity,
    pub title: String,
    pub created_at: Timestamp,
}

// Custom structs/enums used as FIELDS or PARAMETERS do derive SpacetimeType.
#[derive(SpacetimeType, Clone, PartialEq)]
pub enum Status { Idle, Busy(u32) }

#[reducer]
pub fn rename(ctx: &ReducerContext, id: u64, title: String) -> Result<(), String> {
    if title.is_empty() { return Err("title cannot be empty".into()); }
    let task = ctx.db.task().id().find(&id).ok_or("no such task")?;
    if task.owner_id != ctx.sender() { return Err("not yours".into()); }   // sender() — with parens
    ctx.db.task().id().update(Task { title, ..task });   // spread, never Default
    log::info!("renamed {id}");
    Ok(())
}

// Insert returns the ROW (read the assigned id off it), delete goes through an index.
let new_id = ctx.db.task().insert(Task { id: 0, /* … */ }).id;
ctx.db.task().id().delete(&new_id);

// Reads: .find() on primary key/unique (Option<Row>), .filter() on a btree index
// (iterator), .iter() for a full scan when there is no index.
for t in ctx.db.task().owner_id().filter(&ctx.sender()) { /* … */ }

// Scheduled tables: the job row is auto-deleted after the reducer runs; cancel by deleting it.
#[table(accessor = tick_timer, scheduled(tick))]
pub struct TickTimer {
    #[primary_key] #[auto_inc] scheduled_id: u64,
    scheduled_at: ScheduleAt,
}
```

`ctx` members: `ctx.sender()` (method), `ctx.timestamp`, `ctx.db`, `ctx.rng`. Lifecycle hooks are `#[reducer(init)]`, `#[reducer(client_connected)]`, `#[reducer(client_disconnected)]` — all three live in `main/lifecycle.rs`. Logging is `log::{trace,debug,info,warn,error}!`.

## Views — how the client subscribes

Every `views.rs` here uses 2.x **parameterized views**. The client never writes SQL: it subscribes to view accessors by name (see `TableSubscriber`'s `BaseTables`/`LobbyTables`/`GameTables`), so *adding a view is how you expose data*.

```rust
use spacetimedb::{ViewContext, Query, AnonymousViewContext};

// Per-caller view: ViewContext gives both ctx.db (imperative lookups) and
// ctx.from (the query builder). Note r#where — `where` is a Rust keyword.
#[spacetimedb::view(accessor = local_player_data, public)]
fn local_player_data(ctx: &ViewContext) -> impl Query<PlayerData> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);                     // sentinel ⇒ matches nothing
    ctx.from.player_data().r#where(move |d| d.profile_id.eq(profile_id)).build()
}

// Unfiltered/global view: AnonymousViewContext, no per-caller state.
#[spacetimedb::view(accessor = enemy_templates, public)]
fn enemy_templates(ctx: &AnonymousViewContext) -> impl Query<EnemyTemplate> {
    ctx.from.enemy_template().build()
}
```

Two idioms every AOI view in this module follows, because the builder has no `IN` and a view must always return a query:

- **`IN` is an OR-chain.** Seed with the first index, then fold: `let mut cond = e.chunk_index.eq(idx[0]); for &i in &idx[1..] { cond = cond.or(e.chunk_index.eq(i)); }`
- **Empty result needs a sentinel query, not an early `return`** of nothing. Build a deliberately-unmatchable query up front (`r#where(|e| e.enemy_id.eq(u64::MAX))`) and return that when the caller has no chunk yet. Same trick as the `unwrap_or(u64::MAX)` above.

Predicates are closures over *column accessors* (`d.profile_id.eq(v)`, `.or(..)`), not plain Rust comparisons, and anything captured must be `move`d in.

**Procedures** (`#[procedure] fn f(ctx: &mut ProcedureContext, …) -> T`) are the escape hatch for HTTP/filesystem side effects and *can* return values, but need an explicit `ctx.try_with_tx(|tx| …)` for database access. Still beta; nothing in this module uses them yet.

**Indexes:** primary-key and `#[unique]` columns are indexed automatically. **Index names must be unique across the whole module, not just per table.** Query code references indexes by name, so renaming or removing one means updating every call site.

## Commands

```bash
server/build.sh                 # regenerate C# bindings + publish `bullethell` to `local` (--delete-data)
spacetime logs bullethell       # module logs — first place to look
cargo check --manifest-path server/spacetimedb/Cargo.toml   # fast compile check, no publish
spacetime start                 # local server, if it isn't already running
```

Publishing wipes the database every time, so schema changes are hard cuts: no migrations, no dual-write path, and seeds re-run from `main/seeds.rs`. Debug order when something doesn't work: server running → module published → bindings regenerated → module logs → **is the client actually calling the reducer?**

## Editing behavior

- Make the smallest change necessary; don't touch unrelated files, configs, or dependencies.
- Don't invent SpacetimeDB APIs — use only what exists in the docs or already appears in this module.
- Don't add restrictions the prompt didn't ask for: if "users can do X", implement X for all users.
