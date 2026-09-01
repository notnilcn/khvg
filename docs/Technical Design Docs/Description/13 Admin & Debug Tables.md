# 13 Admin & Debug Tables

## Assumed knowledge

[[02 The Component Framework]] and [[03 Connection, Subscriptions & Views]] (reducers, views, subscription waves, binders). The admin flag lives on the identity rows covered in [[04 Lobby & Identity Tables]]; [[05 Player Data & Progression Tables]] explains the allocation/stat split `change_stats` writes into; [[07 Terrain & World Tables]], [[08 Enemy Tables]], and [[10 Items, Inventory & Loot Tables]] cover the seed/catalog/def tables the admin reducers write at runtime.

## The 30-second version

This system owns two small **server-only** tables — `PlayerPositionDebug` and its `PlayerPositionDebugSchedule` job row — plus the entire **admin tooling surface**: a single global admin slot (claimed and released by reducer, gated by `is_admin`), a battery of admin-only reducers in `main/admin.rs` that are the *only runtime writers* of the catalog/def tables (textures, enemy templates and step defs, world/biome defs, chunks), the seeds that repopulate those tables after every publish wipes the database, and the client's `DebugOverlay` — which, despite the name pairing, is a purely local performance HUD that reads nothing from the server. No first-party client code calls any admin reducer; they are driven from the `spacetime` CLI.

## Flowcharts

- [[flowcharts/main-admin.canvas]] — the composed flow for this system.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/main_subfolder.canvas]] — `main/`: admin reducers, debug tick, seeds, lifecycle.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/item_subfolder/item_subfolder.canvas]] — item catalog tables, admin catalog reducers, item/enchantment seeds.
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/game_codefile/game_codefile.canvas]] — `game.tscn`, where the DebugOverlay is declared inline.

## Table changes

```sync
![[00 Table Map#^table-player-position-debug{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-player-position-debug-schedule{seamless:true,title:false,marker:02.}]]
```

## Main body

### The admin model: one global slot

Admin status is a plain `is_admin: bool` column on both identity tables — `LoggedInPlayer` and `LoggedOutPlayer` ([[server/spacetimedb/src/player/tables.rs#LoggedInPlayer|definition]]) — not a separate role table, *because* admin is a debugging convenience, not a gameplay feature. The flag survives every identity-row transition (lobby ↔ world ↔ disconnect) *because* each transition constructs the replacement row with `is_admin: p.is_admin` — see `join_world`/`leave_world` in `player/reducers.rs`, the death path in `combat/mod.rs`, and [[server/spacetimedb/src/main/lifecycle.rs#client_disconnected|client_disconnected]].

[[server/spacetimedb/src/main/admin.rs#claim_admin|claim_admin]] enforces the single-slot invariant: before granting, it scans **both** identity tables for any row already carrying `is_admin` and refuses if one exists, pointing the caller at [[server/spacetimedb/src/main/admin.rs#release_admin|release_admin]] — the only way to free the slot. Both reducers work whether the caller is in the world or in the lobby (they check `LoggedInPlayer` first, then `LoggedOutPlayer`), and both are idempotent for the current holder. The bootstrap case matters: a brand-new identity gets `is_admin: false` from `client_connected`, so the first admin claims the slot with `spacetime call bullethell claim_admin` — no seed grants it.

Every other admin reducer opens with the same guard, [[server/spacetimedb/src/player/methods.rs#is_admin|is_admin]]: look up `ctx.sender()`'s row in either identity table and read the flag, defaulting to `false` when no row exists. It trusts `ctx.sender()` (the authenticated principal) and never an identity passed as an argument — the target-picking helpers are lookup-only. [[server/spacetimedb/src/main/admin.rs#find_player_by_username|find_player_by_username]] resolves a target for those reducers by exact username (case-insensitive), full identity hex, or identity-hex prefix, scanning both identity tables; [[server/spacetimedb/src/player/methods.rs#find_profile_by_name|find_profile_by_name]] then resolves the profile within that player. This is what makes CLI invocations like `spacetime call bullethell give_item <username> <profile> "Delete Orb"` work (the full CLI recipe lives in the `playtest` skill, `.kimi-code/skills/playtest/`).

**Nothing in the client calls these reducers.** The generated bindings expose them like any other reducer, but no first-party script invokes one — they exist for `spacetime call` and admin tooling. The `is_admin` guard is what keeps that safe: any client *could* send the call, and it would bounce off the guard.

### `main/admin.rs` — the runtime writers of the static tables

Most tables in this module are written by gameplay (reducers players trigger) or by seeds at publish time. The def/catalog tables have a third writer: the admin upserts here, which let you patch content into a running world without a republish. Grouped by concern:

- **Player stats.** [[server/spacetimedb/src/main/admin.rs#change_stats|change_stats]] targets a player by username + profile name and writes the six allocatable stats into the `PlayerStatAllocation` row — deliberately the allocation *input*, not the `PlayerStats` output row, *because* `recompute_stats` rebuilds `PlayerStats` from allocation + gear on every inventory reducer and would silently wipe a direct write (see [[05 Player Data & Progression Tables]]). Negative inputs are clamped to 0 (allocation is unsigned), and it calls `recompute_stats` itself so the change is visible immediately.
- **Catalog and content upserts.** [[server/spacetimedb/src/main/admin.rs#upsert_texture_entry|upsert_texture_entry]] and [[server/spacetimedb/src/main/admin.rs#upsert_enemy_template|upsert_enemy_template]] upsert by their natural string keys; the item/enchantment catalog equivalents live in `item/reducers.rs` (below). Because these rows flow to clients through the `all_*` views in the base subscription wave, an admin upsert is how a live client sees a new texture or template without a republish.
- **Runtime enemy-authoring upserts.** [[server/spacetimedb/src/main/admin.rs#upsert_movement_def|upsert_movement_def]], [[server/spacetimedb/src/main/admin.rs#upsert_single_step_def|upsert_single_step_def]], [[server/spacetimedb/src/main/admin.rs#upsert_repeat_step_def|upsert_repeat_step_def]], and [[server/spacetimedb/src/main/admin.rs#upsert_multi_step_def|upsert_multi_step_def]] edit the behavior-tree step-def tables `EnemyTemplate` rows reference (see [[08 Enemy Tables]]). Their `def_id` is auto-increment, so the convention is: **pass `def_id: 0` to insert** (auto-inc assigns the real id — discover it afterwards with `spacetime sql`), pass an existing id to update. Passing 0 with the update branch unreachable is why each reducer spreads `MovementDef { def_id: 0, ..def }` on insert.
- **Enemy control.** [[server/spacetimedb/src/main/admin.rs#spawn_enemy|spawn_enemy]] wraps the requested position onto the torus and derives `chunk_index` (`world_to_chunk` + `spiral_chunk_index`, loading the grid via [[server/spacetimedb/src/world/tables.rs#load|MapConfig::load]] with the admin fallback `1, 1`) before delegating to [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype|spawn_enemy_archetype]] — the archetype helper *because* an enemy is several joined rows (flat row + behavior-tree rows), and hand-inserting would orphan them. [[server/spacetimedb/src/main/admin.rs#despawn_enemy|despawn_enemy]] mirrors it through [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] and then calls [[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]], so admin despawns purge statuses referencing the dead enemy and its own debuff rows. [[server/spacetimedb/src/main/admin.rs#despawn_all_enemies|despawn_all_enemies]] runs the same teardown over every `Enemy` row — identical cleanup to the combat kill path, minus the XP grant.
- **Spawn scheduling.** [[server/spacetimedb/src/main/admin.rs#toggle_enemy_spawning|toggle_enemy_spawning]] arms/disarms the `EnemySpawnSchedule` job row — the 2 s interval job that drives [[server/spacetimedb/src/enemy/methods.rs#spawn_from_biome_regions|spawn_from_biome_regions]]. Biome spawning **starts disarmed**: [[server/spacetimedb/src/main/lifecycle.rs#init|init]] never inserts that job row, so a fresh publish spawns nothing until an admin enables it. Admin-spawned enemies and the 100 ms behavior tick are unaffected either way.
- **World lifecycle.** [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] is the non-gated chunk builder: it validates the grid (`chunk_cols` must divide `chunk_rows`; radius positive), inserts one `BuildingTile` row per hex per chunk, and upserts the singleton `MapConfig` row (id 0) including the torus lap vectors. `init` calls it directly with the `DEFAULT_*` constants from `main/global.rs`; [[server/spacetimedb/src/main/admin.rs#add_chunks|add_chunks]] is just the admin gate over it, and [[server/spacetimedb/src/main/admin.rs#clear_chunks|clear_chunks]] deletes every `BuildingTile` row. `generate_world_proc`/`generate_world_manual` are likewise thin admin gates over `internal_generate_world_proc`/`internal_generate_world_manual` in [[server/spacetimedb/src/world/terrain/mod.rs|world/terrain]] — the same calls `init` uses to build the starting world — so an admin can regenerate terrain live (clients see it through the terrain views; see [[07 Terrain & World Tables]]).

### Admin reducers outside `main/`

The same `is_admin` guard fronts world-authoring reducers in three other modules:

- `item/reducers.rs` — [[server/spacetimedb/src/item/reducers.rs#give_item|give_item]] / [[server/spacetimedb/src/item/reducers.rs#remove_item|remove_item]] (target player by username + profile; give takes the first free General-role slot, remove clears every matching slot and goes through `clear_span` for ability items so multi-slot spans don't leak follower cells) and the catalog upserts [[server/spacetimedb/src/item/reducers.rs#upsert_item|upsert_item]] / [[server/spacetimedb/src/item/reducers.rs#upsert_enchantment|upsert_enchantment]] (see [[10 Items, Inventory & Loot Tables]]).
- `chest/mod.rs` — [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]] / [[server/spacetimedb/src/chest/mod.rs#add_item_to_chest|add_item_to_chest]] (chest placement and stocking as world authoring; `item_id == "every_single_item"` stocks one row per catalog item — see [[12 Chest Tables]]).
- `world/reducers.rs` — [[server/spacetimedb/src/world/reducers.rs#place_building|place_building]] is admin-only outright (buildings aren't a designed feature yet; no client UI calls it), while [[server/spacetimedb/src/world/reducers.rs#remove_building|remove_building]] lets the tile's owner remove their own building and admins remove anyone's — the one reducer where `is_admin` acts as an override rather than a gate.

### Seeds — what exists after the wipe

Every publish runs `server/build.sh` with `--delete-data`, so **the database starts empty and `init` rebuilds all static content from the seeds** — "content = seeds" (`item/AGENTS.md`); anything not seeded does not exist after a republish. [[server/spacetimedb/src/main/lifecycle.rs#init|init]] calls, in order: [[server/spacetimedb/src/main/seeds.rs#seed_default_textures|seed_default_textures]], [[server/spacetimedb/src/main/seeds.rs#seed_layering_rules|seed_layering_rules]], [[server/spacetimedb/src/main/seeds.rs#seed_adjacency_rules|seed_adjacency_rules]], [[server/spacetimedb/src/main/seeds.rs#seed_decor_ground_rules|seed_decor_ground_rules]], [[server/spacetimedb/src/main/seeds.rs#seed_default_enemies|seed_default_enemies]] (plus the Gorgon boss seed in `enemy/defs/gorgon_boss.rs`), [[server/spacetimedb/src/item/seeds.rs#seed_world_items|seed_world_items]], [[server/spacetimedb/src/item/seeds.rs#seed_world_enchantments|seed_world_enchantments]], and [[server/spacetimedb/src/main/seeds.rs#seed_world_defs|seed_world_defs]], then `internal_add_chunks` and `internal_generate_world_proc` for the starting world.

Two seed mechanics keep this idempotent across republishes and across `init` re-runs:

- **Upsert by primary key.** The [[server/spacetimedb/src/main/seeds.rs#Seed|Seed]] trait's impls (for `MapConfig`, `TextureEntry`, `EnemyTemplate`, `WorldDef`, `BiomeDef`, `BiomeRegionDef`, `Enchantment` in `main/seeds.rs`; `Item`'s impl lives beside its content in [[server/spacetimedb/src/item/seeds.rs#seed#1|item/seeds.rs]]) all do find-then-update-or-insert on the table's natural key, so reseeding over a live database refreshes rows instead of duplicating them.
- **Clear-and-reinsert where there is no natural key.** The terrain rule tables (`LayeringRule`, the two adjacency rules, `DecorGroundRule`) are keyed by auto-inc `rule_id` with no unique pair column, so their seed functions delete all rows first — otherwise every republish would accumulate duplicates (the comment on `seed_layering_rules` says exactly this).

The content itself belongs to the system docs: item/enchantment seeds (including the `ability_item` helper and the `REQ_*` class gates) are covered in [[10 Items, Inventory & Loot Tables]], enemy/boss seeds in [[08 Enemy Tables]], and world-def/texture/rule seeds in [[07 Terrain & World Tables]].

### `PlayerPositionDebug` — the server-side position mirror

`PlayerPositionDebug` is a debugging projection of the live position tables, maintained only while an admin asks for it. Its writer is a **scheduled reducer** — a SpacetimeDB job where a row in a `scheduled(...)` table (here `PlayerPositionDebugSchedule`, a 1-second `ScheduleAt::Interval`) makes the runtime invoke the named reducer on that cadence, auto-deleting the job row after each run and re-arming per the interval. [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug|tick_player_position_debug]] runs per `PlayerPosition` row: it computes the hex coordinates server-side (`world_to_hex` with `MapConfig::load(ctx, 0, 0)` — the simulation fallback), pulls `screen_rotation` off the matching `PlayerRotation` row, and upserts one `PlayerPositionDebug` row keyed by `player_id` carrying x/y, rotation, `chunk_index`, and hex indices — the precomputed hex/chunk values are the point, since computing them per `spacetime sql` query by hand is tedious. A second pass deletes debug rows whose player no longer has a position row, so the table mirrors the current position set rather than accumulating history.

The job starts **disarmed** — `init` never inserts the schedule row. [[server/spacetimedb/src/main/debug.rs#toggle_debug|toggle_debug]] (admin-gated) checks for a live schedule row: absent → it inserts the 1 s job; present → it deletes the job row(s) **and wipes every `PlayerPositionDebug` row**, so disabling leaves no stale snapshot behind. Both tables are non-`public`, so no client can subscribe; inspection is `spacetime sql bullethell "SELECT * FROM player_position_debug"` while armed.

### The client `DebugOverlay` — local only, despite the name

[[client/Scripts/Game/DebugOverlay.cs|DebugOverlay.cs]] is a `CanvasLayer` declared **inline in [[client/Scenes/game.tscn|game.tscn]]** (layer 100, `visible = false`, one `Label` child) — not instanced from a component scene. `_Input` toggles `Visible` on the `toggle_debug_overlay` input action (bound to **P** in [[client/project.godot|project.godot]]), and `_Process` — early-out while hidden — refreshes the label every 0.25 s from Godot's own `Performance` monitors (FPS, frame time, static memory, object/node counts, draw calls) plus the live enemy count via [[client/Scripts/Game/GameManager.cs#EnemyCount|GameManager.EnemyCount]] (the facade over `EntitySpawnerComponent`'s tracking dictionary). It never opens a subscription, calls a reducer, or reads a table — every number it shows is computed on the client. The pairing implied by the names (`DebugOverlay` ↔ `main/debug.rs`) does not exist in code; see Known gaps.

## Known gaps / stubs

- **`PlayerPositionDebug` has no client reader.** The table is server-only (non-`public`), inspectable only via `spacetime sql` while the job is armed. Corollary: [[client/AGENTS.md]]'s "`DebugOverlay.cs` — debug HUD; paired with server `main/debug.rs`" is **stale** — `DebugOverlay` is a purely local perf HUD (FPS/memory/enemy count) and never touches the table.
- **Stale comment in `main/seeds.rs`.** The `seed_world_defs` doc comment still lists "`TerrainManager` rendering" among the working decor machinery — `TerrainManager` is a pre-refactor class that no longer exists; the terrain renderer is `TerrainComponent` (declared inline in `game.tscn`). Comment-only drift.

## Where to go next

The admin flag's home tables are in [[04 Lobby & Identity Tables]]; the tables the admin reducers and seeds write each have their own doc — [[07 Terrain & World Tables]] (chunks, textures, world/biome defs), [[08 Enemy Tables]] (templates, step defs, spawn control), and [[10 Items, Inventory & Loot Tables]] (the item/enchantment catalog). That completes the table docs; [[00 Table Map]] remains the per-table reference spine.
