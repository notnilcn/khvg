# 00 Table Map

The per-table spine of the Description docs: every table in the SpacetimeDB module (`server/spacetimedb/src/`), **who writes it** (reducer, scheduled tick, lifecycle hook, or seed) and **how the client reads it** (view accessor or raw public table, subscription wave, binder component). The system docs 04–13 transclude their "Table changes" sections from the `^table-*` anchors here — fix an entry here, not in the system docs. Link syntax is in [[01 Roadmap#Conventions]]; the module layout is [[server/AGENTS.md]]; views/waves/binders themselves are explained in [[03 Connection, Subscriptions & Views]].

Subscription waves referenced below are the static name lists in [[client/sstdbsdk/TableSubscriber.cs#BaseTables|TableSubscriber]]: `BaseTables` (catalog, subscribed on connect), `LobbyTables` (profile selection), `GameTables` (the live world). "Replay on/off" is the binder's `ReplayExistingRows` ([[client/sstdbsdk/TableBinderComponent.cs#ReplayExistingRows|TableBinderComponent]]) — replays cached rows through `RowInserted` on bind; it stays **off** on event tables (no last-seen-id tracking, so replay would re-fire the whole history) and **on** for persistent tables so late joiners see existing rows. Scheduled-job tables appear as their own entries; their only "writer" is whoever arms/disarms the job row (the runtime auto-deletes the row after each run and re-arms per its interval).

# Lobby & Identity

**LoggedInPlayer** — [[server/spacetimedb/src/player/tables.rs#LoggedInPlayer|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/reducers.rs#join_world|join_world]] (the lobby → world transition).
  - update: [[server/spacetimedb/src/player/reducers.rs#set_username|set_username]]; [[server/spacetimedb/src/main/admin.rs#claim_admin|claim_admin]] / [[server/spacetimedb/src/main/admin.rs#release_admin|release_admin]] (the single global admin slot).
  - delete: [[server/spacetimedb/src/player/reducers.rs#leave_world|leave_world]], [[server/spacetimedb/src/main/lifecycle.rs#client_disconnected|client_disconnected]], the already-in-world recovery path in [[server/spacetimedb/src/main/lifecycle.rs#client_connected|client_connected]], and the death cleanup in [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]] — every delete is paired with a ^table-logged-out-player insert.
- Client read: per-caller view `local_player` ([[server/spacetimedb/src/player/views.rs#local_player|local_player]]) in the `GameTables` wave → EntitySpawnerComponent's `LocalPlayerBinder` in [[client/Scenes/game.tscn|game.tscn]] (replay on) spawns the local-player puppet. The raw table is `public` but never subscribed directly.
- Server readers: every reducer guard — [[server/spacetimedb/src/player/methods.rs#require_in_world|require_in_world]] / [[server/spacetimedb/src/player/methods.rs#require_logged_in|require_logged_in]] / [[server/spacetimedb/src/player/methods.rs#is_admin|is_admin]] — and the left semijoins in the `nearby_remote_players` / `nearby_remote_player_rotations` views, which keep logged-out ghost rows of ^table-player-position / ^table-player-rotation from streaming.

^table-logged-in-player

**LoggedOutPlayer** — [[server/spacetimedb/src/player/tables.rs#LoggedOutPlayer|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/lifecycle.rs#client_connected|client_connected]] (first connect, plus the already-in-world recovery), [[server/spacetimedb/src/main/lifecycle.rs#client_disconnected|client_disconnected]], [[server/spacetimedb/src/player/reducers.rs#leave_world|leave_world]], the death cleanup in [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]].
  - update: [[server/spacetimedb/src/player/reducers.rs#set_username|set_username]]; [[server/spacetimedb/src/main/admin.rs#claim_admin|claim_admin]] / [[server/spacetimedb/src/main/admin.rs#release_admin|release_admin]].
  - delete: [[server/spacetimedb/src/player/reducers.rs#join_world|join_world]].
- Client read: **not `public`** — a client only ever sees its own row, through the per-caller view `local_lobby_player` ([[server/spacetimedb/src/player/views.rs#local_lobby_player|local_lobby_player]]) in the `LobbyTables` wave. [[client/sstdbsdk/DatabaseConnector.cs|DatabaseConnector]] hooks its row events internally (sets `Username`); there is no binder component.

^table-logged-out-player

**PlayerProfile** — [[server/spacetimedb/src/player/tables.rs#PlayerProfile|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/lifecycle.rs#client_connected|client_connected]] (a default "Knight" profile on first connect), [[server/spacetimedb/src/player/reducers.rs#create_profile|create_profile]].
  - update: **never updated** — no update site exists.
  - delete: [[server/spacetimedb/src/player/reducers.rs#delete_profile|delete_profile]] (which also runs the ^table-player-data teardown archetype helper).
- Client read: three views — `local_player_profiles` (in **both** the `LobbyTables` and `GameTables` waves) → LobbyComponent's `LocalPlayerProfilesBinder` ([[client/Scenes/Components/Lobby/lobby_component.tscn|lobby_component.tscn]], replay on) drives the character-slot panels; `local_player_active_profile` (`GameTables`) → LocalPlayerProfileComponent's `LocalPlayerActiveProfileBinder` ([[client/Scenes/local_player.tscn|local_player.tscn]], replay on) drives the local sprite/aim settings; `nearby_remote_players_profiles` (`GameTables`, AOI-filtered via the same chunk set as ^table-player-position's remote view) — no binder, [[client/Scripts/Players/Remote/RemotePlayer.cs|RemotePlayer]] does a one-shot `Iter()` lookup for its texture.
- Server readers: [[server/spacetimedb/src/player/methods.rs#find_profile_by_name|find_profile_by_name]] (admin give/remove flows).

^table-player-profile

# Player Data & Progression

**PlayerData** — [[server/spacetimedb/src/player/tables.rs#PlayerData|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]] (the archetype scaffold on `join_world`).
  - update: [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] (max_hp/defense/base_speed resolution + hp clamp), [[server/spacetimedb/src/player/methods.rs#internal_gain_xp|internal_gain_xp]] (level/xp/hp on level-up), and the two hp writers in the combat pipeline — [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]] (damage, death at 0) and [[server/spacetimedb/src/combat/mod.rs#heal_player|heal_player]].
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] (death cleanup and `delete_profile`).
- Client read: per-caller view `local_player_data` (`GameTables`) → LocalPlayerDataComponent's `LocalPlayerDataBinder` (local_player.tscn, replay on) → the HealthComponent mirror.
- Server readers: the combat pipeline reads `defense`/`hp` on every incoming hit; [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] clamps reported speed to `base_speed`.

^table-player-data

**PlayerStats** — [[server/spacetimedb/src/player/tables.rs#PlayerStats|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] is the **only** writer — it inserts the row when missing and updates it otherwise, resolving `(base + allocated + gear flat) × (1 + mult)` for the six allocatable stats.
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- Client read: per-caller view `local_player_stats` (`GameTables`) → LocalPlayerDataComponent's `LocalPlayerStatsBinder` (local_player.tscn, replay on) → the StatsComponent mirror.
- Server readers: [[server/spacetimedb/src/player/methods.rs#check_stat_requirements|check_stat_requirements]] (equip gating) and the outgoing damage formulas read resolved stats.

^table-player-stats

**PlayerStatAllocation** — [[server/spacetimedb/src/player/tables.rs#PlayerStatAllocation|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]].
  - update: [[server/spacetimedb/src/player/reducers.rs#allocate_stat|allocate_stat]] (player spends `unspent_points`), [[server/spacetimedb/src/player/methods.rs#internal_gain_xp|internal_gain_xp]] (+`SKILL_POINTS_PER_LEVEL` per level), [[server/spacetimedb/src/main/admin.rs#change_stats|change_stats]] (admin).
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- Client read: per-caller view `local_player_stat_allocation` (`GameTables`) → LocalPlayerDataComponent's `LocalPlayerStatAllocationBinder` (local_player.tscn, replay on); the StatsSidebar `+` buttons call `allocate_stat`.

^table-player-stat-allocation

# Movement & AOI

**PlayerPosition** — [[server/spacetimedb/src/player/tables.rs#PlayerPosition|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]] (spawn at world origin).
  - update: [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] (10 Hz, displacement-clamped against `base_speed × dt × MOVE_SLACK`; unchanged positions skip the write), [[server/spacetimedb/src/player/methods.rs#teleport_player|teleport_player]] (exempt from the clamp), `try_scaffold_profile` (repositions an existing row on rejoin).
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] **only** — `leave_world`/logout does not delete the row, so logged-out players leave ghost positions that the AOI views filter out via their ^table-logged-in-player semijoin (cleanup happens only on death or `delete_profile`).
- Client read: two views — `local_player_position` (`GameTables`) → PositionSyncComponent's `LocalPlayerPositionBinder` (local_player.tscn, replay on); `nearby_remote_players` (`GameTables`, chunk-filtered through ^table-player-chunk, own row excluded) → EntitySpawnerComponent's `NearbyRemotePlayersBinder` (game.tscn, replay on; spawn/despawn) plus each RemotePlayer's child binder in [[client/Scenes/non_local_player.tscn|non_local_player.tscn]] (`RowUpdated`, replay off) driving InterpolationComponent.
- Server readers: every AOI view's chunk scan; the `HIT_VALIDATION_RADIUS` range checks in [[server/spacetimedb/src/player/reducers.rs#report_hit|report_hit]] / [[server/spacetimedb/src/enemy/reducers.rs#report_enemy_hit|report_enemy_hit]]; [[server/spacetimedb/src/player/methods.rs#players_near_point|players_near_point]] (zone/AoE membership); a full scan by [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug|tick_player_position_debug]].

^table-player-position

**PlayerRotation** — [[server/spacetimedb/src/player/tables.rs#PlayerRotation|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]].
  - update: [[server/spacetimedb/src/player/reducers.rs#report_screen_rotation|report_screen_rotation]] (~30 Hz client cadence; sub-~0.5° changes skip the write; `chunk_index` mirrors the ^table-player-position row so AOI views filter identically).
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- Client read: view `nearby_remote_player_rotations` (`GameTables`) → RemotePlayer's second child binder in non_local_player.tscn (`RowUpdated`, replay off). The local player never reads its own rotation back — rotation is write-only for the owner.

^table-player-rotation

**PlayerChunk** — [[server/spacetimedb/src/player/tables.rs#PlayerChunk|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]].
  - update: [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] and [[server/spacetimedb/src/player/methods.rs#teleport_player|teleport_player]] — **only when the chunk actually changes**. That is why the table exists: AOI views key off it instead of ^table-player-position, so they recompute on real chunk crossings rather than on every 10 Hz movement report.
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- Client read: `public` but in no subscription wave — **the client never sees it**. It exists purely to drive server-side view recomputation.
- Server readers: [[server/spacetimedb/src/player/views.rs#nearby_indices_from_chunk|nearby_indices_from_chunk]] — the helper every AOI view (`nearby_remote_players`, `nearby_enemies`, `nearby_terrain_tiles`, `nearby_loot_drops`, `nearby_chests`, …) builds its chunk filter on.

^table-player-chunk

**PlayerMovementState** — [[server/spacetimedb/src/player/tables.rs#PlayerMovementState|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] / [[server/spacetimedb/src/player/methods.rs#teleport_player|teleport_player]] (first report).
  - update: the same two (restamp `last_report` per report; teleports restamp to stay exempt from the clamp).
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- Client read: not `public` — **server-only**. It is the bookkeeping behind `report_movement`'s displacement clamp: the per-report budget is `base_speed × dt × MOVE_SLACK`, and `dt` comes from this row.

^table-player-movement-state

**PlayerDisplacementAllowance** — [[server/spacetimedb/src/player/tables.rs#PlayerDisplacementAllowance|definition]]

- Writers:
  - insert + update: the Grapple arm of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] (stamps `max_distance` = the scaled grapple range with a short expiry; restamps an existing row).
  - delete: [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] consumes the row on the first oversized report it legitimizes (and on contact with an expired one); [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- Client read: not `public` — **server-only**. The grapple pull is client-tweened; this one-shot allowance is what lets the tween's movement reports past the displacement clamp described in ^table-player-position instead of rubber-banding.

^table-player-displacement-allowance

# Terrain & World

**MapConfig** — [[server/spacetimedb/src/world/tables.rs#MapConfig|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] upserts the singleton row (id 0) — called by [[server/spacetimedb/src/main/lifecycle.rs#init|init]] and the admin [[server/spacetimedb/src/main/admin.rs#add_chunks|add_chunks]].
  - delete: **never deleted** ([[server/spacetimedb/src/main/admin.rs#clear_chunks|clear_chunks]] deletes building tiles, not this row). Note: `impl Seed for MapConfig` exists in [[server/spacetimedb/src/main/seeds.rs#seed#1|main/seeds.rs]] but is never called — this upsert is the only writer.
- Client read: raw `public` table in the `GameTables` wave, three consumers — [[client/sstdbsdk/TableSubscriber.cs#OnMapConfigInsert|TableSubscriber]]'s own internal row hooks (the `LapQ`/`LapR` torus-lap statics every InterpolationComponent reads per frame), TerrainComponent's `MapConfigBinder` and HexGridOverlayComponent's `MapConfigBinder` in [[client/Scenes/game.tscn|game.tscn]], and HexGridOverlay3DComponent's binder in [[client/Scenes/world_3d.tscn|world_3d.tscn]] (all replay on).
- Server readers: [[server/spacetimedb/src/world/tables.rs#load|MapConfig::load]] runs in nearly every reducer that does world math (wrap, chunk, AOI); [[server/spacetimedb/src/player/views.rs|player/views.rs]] reads the row directly because a `ViewContext` can't call the helper.

^table-map-config

**BuildingTile** — [[server/spacetimedb/src/world/tables.rs#BuildingTile|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] (one row per world hex).
  - update: [[server/spacetimedb/src/world/reducers.rs#place_building|place_building]] (admin-only) / [[server/spacetimedb/src/world/reducers.rs#remove_building|remove_building]] (owner or admin) — they set/clear `building_type` + `owner_id` on the existing row.
  - delete: [[server/spacetimedb/src/main/admin.rs#clear_chunks|clear_chunks]].
- Client read: `public` but **unsubscribed** — the client never sees buildings. Placement is write-only world authoring for now; buildings aren't a designed feature yet and no client UI calls the reducers.

^table-building-tile

**TextureEntry** — [[server/spacetimedb/src/world/tables.rs#TextureEntry|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/main/seeds.rs#seed_default_textures|seed_default_textures]] (upsert by `texture_id` via `impl Seed for TextureEntry`, [[server/spacetimedb/src/main/seeds.rs#seed#2|main/seeds.rs]]), [[server/spacetimedb/src/main/admin.rs#upsert_texture_entry|upsert_texture_entry]] (admin).
  - delete: **never deleted**.
- Client read: global anonymous view `all_textures` ([[server/spacetimedb/src/world/views.rs#all_textures|world/views.rs]]) in the `BaseTables` wave → CatalogComponent's `AllTexturesBinder` (game.tscn, replay on) — the `texture_id` → `res_path` cache every visual component resolves through.

^table-texture-entry

**LayeringRule** — [[server/spacetimedb/src/world/tables.rs#LayeringRule|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/seeds.rs#seed_layering_rules|seed_layering_rules]] (clear-and-reinsert allow-list of overlay/base tag pairs).
  - update: **never updated**.
  - delete: the same seed clears all rows before reinserting.
- Client read: `public` but **unsubscribed** — terrain rules are generation inputs, not client data.
- Server readers: the conflict checks in `world/terrain/rules.rs` during generation (the overlay pass consults them per wedge).

^table-layering-rule

**BaseAdjacencyRule** — [[server/spacetimedb/src/world/tables.rs#BaseAdjacencyRule|definition]]

- Writers:
  - insert: none today — [[server/spacetimedb/src/main/seeds.rs#seed_adjacency_rules|seed_adjacency_rules]] is clear-and-reinsert with an **empty** seed list (no clashing base-texture pairs exist yet), so the table is empty in practice.
  - update: **never updated**.
  - delete: `seed_adjacency_rules` clears all rows each publish.
- Client read: `public` but **unsubscribed**.
- Server readers: `world/terrain/rules.rs` neighbor-tag checks during generation.

^table-base-adjacency-rule

**OverlayAdjacencyRule** — [[server/spacetimedb/src/world/tables.rs#OverlayAdjacencyRule|definition]]

- Writers: identical shape to ^table-base-adjacency-rule — [[server/spacetimedb/src/main/seeds.rs#seed_adjacency_rules|seed_adjacency_rules]] clears all rows and seeds none (no overlays in use). **Never updated.**
- Client read: `public` but **unsubscribed**.
- Server readers: `world/terrain/rules.rs` neighbor-tag checks during generation.

^table-overlay-adjacency-rule

**DecorGroundRule** — [[server/spacetimedb/src/world/tables.rs#DecorGroundRule|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/seeds.rs#seed_decor_ground_rules|seed_decor_ground_rules]] (clear-and-reinsert deny-list; two rules seeded — trees/rocks denied on stone).
  - update: **never updated**.
  - delete: the same seed clears all rows before reinserting.
- Client read: `public` but **unsubscribed**.
- Server readers: the decor placement checks in `world/terrain/rules.rs` / `world/terrain/decor.rs`.

^table-decor-ground-rule

**WorldDef** — [[server/spacetimedb/src/world/def_tables.rs#WorldDef|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/main/seeds.rs#seed_world_defs|seed_world_defs]] (upsert by `world_id` via `impl Seed for WorldDef`, [[server/spacetimedb/src/main/seeds.rs#seed#4|main/seeds.rs]] — seeds the single "Earth" world), [[server/spacetimedb/src/main/admin.rs#upsert_world_def|upsert_world_def]] (admin).
  - delete: **never deleted**.
- Client read: `public` but **unsubscribed**.
- Server readers: [[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_proc|internal_generate_world_proc]] (the biome-weighted generation front-end, also called by `init`).

^table-world-def

**BiomeDef** — [[server/spacetimedb/src/world/def_tables.rs#BiomeDef|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/main/seeds.rs#seed_world_defs|seed_world_defs]] (upsert by `biome_id` via `impl Seed for BiomeDef`, [[server/spacetimedb/src/main/seeds.rs#seed#5|main/seeds.rs]]), [[server/spacetimedb/src/main/admin.rs#upsert_biome_def|upsert_biome_def]] (admin).
  - delete: **never deleted**.
- Client read: `public` but **unsubscribed** — the client never joins biome definitions; the per-tile results are baked into ^table-triangle-tile / ^table-hex-decor rows at generation time.
- Server readers: the terrain pipeline's ground/overlay/decor passes (`world/terrain/`) read texture configs, noise scales, and region weights from these rows.

^table-biome-def

**BiomeRegionDef** — [[server/spacetimedb/src/world/def_tables.rs#BiomeRegionDef|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/main/seeds.rs#seed_world_defs|seed_world_defs]] via its `seed_region_def` helper (upsert by `region_def_id` via `impl Seed for BiomeRegionDef`, [[server/spacetimedb/src/main/seeds.rs#seed#6|main/seeds.rs]]), [[server/spacetimedb/src/main/admin.rs#upsert_biome_region_def|upsert_biome_region_def]] (admin).
  - delete: **never deleted**.
- Client read: `public` but **unsubscribed**.
- Server readers: [[server/spacetimedb/src/world/terrain/mod.rs#run_generation|run_generation]] resolves region seeds from these defs; the instantiated result lands in ^table-biome-region.

^table-biome-region-def

**BiomeRegion** — [[server/spacetimedb/src/world/instance_tables.rs#BiomeRegion|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/world/terrain/mod.rs#run_generation|run_generation]] (one row per distributed region seed, carrying `enemy_template_ids`/`max_enemies`/`spawn_radius` from its ^table-biome-region-def).
  - update: **never updated**.
  - delete: [[server/spacetimedb/src/world/terrain/mod.rs#clear_world_geometry|clear_world_geometry]] at the start of every generation run.
- Client read: `public` but **unsubscribed**.
- Server readers: [[server/spacetimedb/src/enemy/methods.rs#spawn_from_biome_regions|spawn_from_biome_regions]] full-scans this table on the 2 s spawn tick — it **doubles as the enemy spawn-zone table** (there is no `EnemyZone`).

^table-biome-region

**TriangleTile** — [[server/spacetimedb/src/world/instance_tables.rs#TriangleTile|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/world/terrain/mod.rs#write_triangle_tiles|write_triangle_tiles]] (the terrain pipeline — 6 wedge rows per world hex, ground + overlay baked).
  - update: **never updated**.
  - delete: [[server/spacetimedb/src/world/terrain/mod.rs#clear_world_geometry|clear_world_geometry]] at the start of every generation run.
- Client read: AOI view `nearby_terrain_tiles` ([[server/spacetimedb/src/world/views.rs#nearby_terrain_tiles|world/views.rs]]; `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS` off ^table-player-chunk) in the `GameTables` wave → TerrainComponent's `TerrainTilesBinder` (game.tscn, replay on) → the pooled TileComponent ground/overlay layers.

^table-triangle-tile

**HexDecor** — [[server/spacetimedb/src/world/instance_tables.rs#HexDecor|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/world/terrain/decor.rs#place_decor|place_decor]] (at most one decor prop per hex; a hex with no decor simply has no row).
  - update: **never updated**.
  - delete: [[server/spacetimedb/src/world/terrain/mod.rs#clear_world_geometry|clear_world_geometry]] at the start of every generation run.
- Client read: AOI view `nearby_hex_decor` ([[server/spacetimedb/src/world/views.rs#nearby_hex_decor|world/views.rs]]) in the `GameTables` wave → TerrainComponent's `HexDecorBinder` (game.tscn, replay on) → the decor/shadow MultiMesh layers.

^table-hex-decor

# Enemies & AI

**Enemy** — [[server/spacetimedb/src/enemy/instance_tables.rs#Enemy|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype|spawn_enemy_archetype]] — the only sanctioned path, called by [[server/spacetimedb/src/enemy/methods.rs#spawn_from_biome_regions|spawn_from_biome_regions]] (the 2 s spawn tick) and admin [[server/spacetimedb/src/main/admin.rs#spawn_enemy|spawn_enemy]].
  - update: [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] (x/y/chunk + the `movement_direction`/`movement_speed` dead-reckoning pair, delta-checked — the write is skipped when nothing changed), [[server/spacetimedb/src/combat/mod.rs#apply_damage_to_enemy_opts|apply_damage_to_enemy_opts]] (hp/phase on every hit), the Bash arm of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] and the 1 Hz [[server/spacetimedb/src/status/methods.rs#tick|status tick]] (the `stagger` bar).
  - delete: [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] — reached by the combat kill path in `apply_damage_to_enemy_opts` and by admin [[server/spacetimedb/src/main/admin.rs#despawn_enemy|despawn_enemy]] / [[server/spacetimedb/src/main/admin.rs#despawn_all_enemies|despawn_all_enemies]].
- Client read: AOI view `nearby_enemies` ([[server/spacetimedb/src/enemy/views.rs#nearby_enemies|enemy/views.rs]]) in the `GameTables` wave, with **three** binders — EntitySpawnerComponent's `NearbyEnemiesBinder` (game.tscn, replay on) spawns/despawns the puppets; each Enemy's own `NearbyEnemiesBinder` in [[client/Scenes/default_enemy.tscn|default_enemy.tscn]] (replay on, so late-spawned puppets still initialize) mirrors hp/stagger/position into HealthComponent + InterpolationComponent; CombatComponent's `NearbyEnemiesBinder` in local_player.tscn (replay **off** — only `RowDeleted` is wired, for nearest-enemy invalidation of aim lock).
- Server readers: [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] **full-scans** it every 100 ms; the hit reducers and combat sink read it per hit.

^table-enemy

**EnemyBehavior** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyBehavior|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]] (archetype build — root of the per-enemy behavior tree).
  - update: [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] (aggro target/lock, `was_simulating`, active phase, the unit heading memory in `velocity_x/y` — delta-checked), [[server/spacetimedb/src/combat/mod.rs#apply_damage_to_enemy_opts|apply_damage_to_enemy_opts]] (re-aggro onto the attacker on hit).
  - delete: [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior|delete_enemy_behavior]] (archetype teardown — cascades through ^table-enemy-phase / ^table-enemy-attack / ^table-enemy-sequence-step / ^table-repeat-step-instance).
- Client read: `public` but **unsubscribed** — puppets mirror ^table-enemy's flat row instead; the behavior tree is server simulation state.

^table-enemy-behavior

**EnemyPhase** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyPhase|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]] (one row per template phase).
  - update: [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]] (post-build fix-up of the inserted rows), [[server/spacetimedb/src/enemy/methods.rs#begin_phase_cycle|begin_phase_cycle]] (phase reset on HP-threshold transitions), [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] (phase timers — delta-checked).
  - delete: [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior|delete_enemy_behavior]].
- Client read: `public` but **unsubscribed**.

^table-enemy-phase

**EnemyAttack** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyAttack|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]] (one row per attack sequence in the phase).
  - update: [[server/spacetimedb/src/enemy/methods.rs#activate_all_attacks|activate_all_attacks]] / [[server/spacetimedb/src/enemy/methods.rs#begin_phase_cycle|begin_phase_cycle]] / [[server/spacetimedb/src/enemy/methods.rs#tick_phase|tick_phase]] / [[server/spacetimedb/src/enemy/methods.rs#tick_sequence|tick_sequence]] (step index, step/loop timers, wait flags).
  - delete: [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior|delete_enemy_behavior]].
- Client read: `public` but **unsubscribed**.

^table-enemy-attack

**EnemySequenceStep** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemySequenceStep|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]] (one row per step, referencing a step def).
  - update: **never updated**.
  - delete: [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior|delete_enemy_behavior]].
- Client read: `public` but **unsubscribed**.

^table-enemy-sequence-step

**RepeatStepInstance** — [[server/spacetimedb/src/enemy/instance_tables.rs#RepeatStepInstance|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]] — a per-enemy copy of the ^table-repeat-step-def so `repeat_count` can advance independently per enemy.
  - update: [[server/spacetimedb/src/enemy/methods.rs#reset_repeat_counts|reset_repeat_counts]] / [[server/spacetimedb/src/enemy/methods.rs#tick_sequence|tick_sequence]] (the `repeat_count` counter that bakes `angle_step` spin per shot).
  - delete: [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior|delete_enemy_behavior]].
- Client read: `public` but **unsubscribed**.

^table-repeat-step-instance

**EnemyTemplate** — [[server/spacetimedb/src/enemy/def_tables.rs#EnemyTemplate|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/main/seeds.rs#seed_default_enemies|seed_default_enemies]] and [[server/spacetimedb/src/enemy/defs/gorgon_boss.rs#seed|gorgon_boss::seed]] (upsert by `template_id` via `impl Seed for EnemyTemplate`, [[server/spacetimedb/src/main/seeds.rs#seed#3|main/seeds.rs]]; difficulty variants are separate rows — `Foo_Easy` … `Foo_Madness`), [[server/spacetimedb/src/main/admin.rs#upsert_enemy_template|upsert_enemy_template]] (admin).
  - delete: **never deleted**.
- Client read: global anonymous view `enemy_templates` ([[server/spacetimedb/src/enemy/views.rs#enemy_templates|enemy/views.rs]]) in the `GameTables` wave — no binder; [[client/Scripts/Players/Enemies/Enemy.cs|Enemy.cs]] reads the template directly (max_hp for its HealthComponent, display data).
- Server readers: the behavior tick (phases, sim factors, `stun_behavior`), the combat sink (flat `defense`), and `spawn_enemy` read templates constantly.

^table-enemy-template

**MovementDef** — [[server/spacetimedb/src/enemy/def_tables.rs#MovementDef|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/defs/mod.rs#make_phase|make_phase]] (every phase build inserts its MovementDef row — seed-time content authoring), [[server/spacetimedb/src/main/admin.rs#upsert_movement_def|upsert_movement_def]] (admin; `def_id: 0` = insert).
  - update: [[server/spacetimedb/src/main/admin.rs#upsert_movement_def|upsert_movement_def]] (existing `def_id`).
  - delete: **never deleted**.
- Client read: `public` but **unsubscribed**.
- Server readers: [[server/spacetimedb/src/enemy/methods.rs#apply_movement|apply_movement]] on the behavior tick.

^table-movement-def

**SingleStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#SingleStepDef|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/defs/mod.rs#seq_single|seq_single]] (called by seed code and by `Emitter::compile` when it lowers an emitter to an `AttackSequence`), [[server/spacetimedb/src/main/admin.rs#upsert_single_step_def|upsert_single_step_def]] (admin; `def_id: 0` = insert).
  - update: [[server/spacetimedb/src/main/admin.rs#upsert_single_step_def|upsert_single_step_def]] (existing `def_id`).
  - delete: **never deleted**.
- Client read: `public` but **unsubscribed** — ^table-bullet-pattern-event carries each shot's resolved damage and parameters, so clients never join step defs.
- Server readers: [[server/spacetimedb/src/enemy/methods.rs#tick_sequence|tick_sequence]] / [[server/spacetimedb/src/enemy/methods.rs#step_damage|step_damage]] on the behavior tick, and [[server/spacetimedb/src/player/reducers.rs#report_hit|report_hit]] damage resolution.

^table-single-step-def

**RepeatStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#RepeatStepDef|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/defs/mod.rs#seq_repeat|seq_repeat]] (seeds / `Emitter::compile`), [[server/spacetimedb/src/main/admin.rs#upsert_repeat_step_def|upsert_repeat_step_def]] (admin; `def_id: 0` = insert).
  - update: [[server/spacetimedb/src/main/admin.rs#upsert_repeat_step_def|upsert_repeat_step_def]] (existing `def_id`).
  - delete: **never deleted**.
- Client read: `public` but **unsubscribed** (same reasoning as ^table-single-step-def).
- Server readers: the behavior tick — copied into ^table-repeat-step-instance at spawn, then read via the instance.

^table-repeat-step-def

**MultiStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#MultiStepDef|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/defs/mod.rs#seq_multi|seq_multi]] (available to content, currently unused by shipped enemies), [[server/spacetimedb/src/main/admin.rs#upsert_multi_step_def|upsert_multi_step_def]] (admin; `def_id: 0` = insert).
  - update: [[server/spacetimedb/src/main/admin.rs#upsert_multi_step_def|upsert_multi_step_def]] (existing `def_id`).
  - delete: **never deleted**.
- Client read: `public` but **unsubscribed** (same reasoning as ^table-single-step-def).
- Server readers: the behavior tick.

^table-multi-step-def

**EnemySpawnSchedule** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemySpawnSchedule|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/admin.rs#toggle_enemy_spawning|toggle_enemy_spawning]](true) arms the 2 s interval job driving [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_spawn|tick_enemy_spawn]]. [[server/spacetimedb/src/main/lifecycle.rs#init|init]] deliberately does **not** arm it — biome spawning starts disarmed for debugging.
  - update: **never updated** (the runtime re-arms the row automatically per interval).
  - delete: `toggle_enemy_spawning`(false) cancels the job row.
- Client read: not `public` — **server-only**.

^table-enemy-spawn-schedule

**EnemyBehaviorSchedule** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyBehaviorSchedule|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/lifecycle.rs#init|init]] arms one 100 ms interval job driving [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] — the enemy simulation tick.
  - update: **never updated**; delete: **never deleted** (no disarm path exists — the behavior tick always runs, independent of spawn arming).
- Client read: not `public` — **server-only**.

^table-enemy-behavior-schedule

# Combat & Events

**BulletPatternEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#BulletPatternEvent|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] — one append per fired shot, origin stamped server-side, `damage` pre-resolved via [[server/spacetimedb/src/enemy/methods.rs#step_damage|step_damage]] so clients can compare bullet damage without subscribing step defs.
  - update: **never updated**; delete: no explicit delete site — the `event` flag makes this an append-only ephemeral broadcast table, not retained state.
- Client read: raw `public` event table in the `GameTables` wave → **one shared** `BulletPatternEventBinder` on BulletSpawnerComponent ([[client/Scenes/Components/Bullets/bullet_spawner_component.tscn|bullet_spawner_component.tscn]], replay **OFF** — replaying an event table would re-fire its whole history). The event rows are self-contained, so there is no per-enemy binder fan-out; BulletSpawnerComponent renders each event verbatim through BlastBullets2D.

^table-bullet-pattern-event

**BulletControlEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#BulletControlEvent|definition]]

- Writers:
  - insert: the bullet-control arms of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] (Delete/Split/Attract, the rect variants, Slash, Wall — with full ability validation/cooldown via `activate_ability` / the charge release). The server tracks no live bullets; it validates the caster and rebroadcasts the cast.
  - update: **never updated**; delete: no explicit delete site (append-only `event` table).
- Client read: raw `public` event table in the `GameTables` wave → BulletControllerComponent's `BulletControlEventBinder` (game.tscn, replay **OFF**); every client resolves the proximity query against its own local bullets, and the caster applies optimistically and skips its own echo via `cast_by`. There is no `control_bullets` reducer — **this table is the bullet-control protocol**.

^table-bullet-control-event

**AbilityVisualEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#AbilityVisualEvent|definition]]

- Writers:
  - insert: the Spell arm of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] — a visual-only relay of a P5 shape-spell cast; the damage is applied server-side in the same dispatch, this row only tells clients to draw the flash.
  - update: **never updated**; delete: no explicit delete site (append-only `event` table).
- Client read: raw `public` event table in the `GameTables` wave → SpellVisualComponent's `AbilityVisualEventBinder` (game.tscn, replay **OFF**), which draws the Polygon2D flash per `AbilityVisualKind` and frees it after `duration`; the caster renders optimistically and skips its own echo via `cast_by` (the ^table-bullet-control-event pattern).

^table-ability-visual-event

**PlayerHitRate** — [[server/spacetimedb/src/player/tables.rs#PlayerHitRate|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/player/methods.rs#hit_rate_allow|hit_rate_allow]] — the 1-second fixed-window token bucket behind the two self-reported hit reducers: `outgoing` caps [[server/spacetimedb/src/enemy/reducers.rs#report_enemy_hit|report_enemy_hit]] at weapon fire_rate × effective shot_count × `HIT_RATE_SLACK`, `incoming` caps [[server/spacetimedb/src/player/reducers.rs#report_hit|report_hit]] at `INCOMING_HIT_RATE_CAP`.
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- Client read: not `public` — **server-only** anti-spam bookkeeping; it never touches damage math itself.

^table-player-hit-rate

**PlayerHitFlag** — [[server/spacetimedb/src/anticheat/tables.rs#PlayerHitFlag|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]] — a witness report that *another* client saw a player get hit (self-reports silently ignored; no bullet identity carried).
  - update: **never updated**.
  - delete: the 1 Hz [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] sweep drops flags older than `HIT_FLAG_MAX_AGE_SECONDS` (60 s).
- Client read: not `public` — **server-only**; it corroborates the victim's self-reported `report_hit` against independent witnesses.
- Server readers: [[server/spacetimedb/src/status/methods.rs#settle_store_mark|settle_store_mark]] consults flags for the `NoHits` store-damage wager.

^table-player-hit-flag

# Items, Inventory & Loot

**Item** — [[server/spacetimedb/src/item/tables.rs#Item|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/item/seeds.rs#seed_world_items|seed_world_items]] (upsert by `item_id` via `impl Seed for Item`, [[server/spacetimedb/src/item/seeds.rs#seed|item/seeds.rs]] — reseeding is idempotent), [[server/spacetimedb/src/item/reducers.rs#upsert_item|upsert_item]] (admin).
  - delete: **never deleted** — the catalog is shared/static; runtime state lives on ^table-player-inventory-slot, never here.
- Client read: global anonymous view `all_items` ([[server/spacetimedb/src/item/views.rs#all_items|item/views.rs]]) in the `BaseTables` wave → CatalogComponent's `AllItemsBinder` (game.tscn, replay on) — the catalog cache behind every item display, EffectiveWeaponResolver, and catalog-fingerprint invalidation.
- Server readers: [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] / [[server/spacetimedb/src/player/methods.rs#resolve_effective_weapon|resolve_effective_weapon]] / the behavior lookups read catalog rows constantly; acquisition paths (`give_item`, chest takes, pickups) validate against them.

^table-item

**Enchantment** — [[server/spacetimedb/src/item/tables.rs#Enchantment|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/item/seeds.rs#seed_world_enchantments|seed_world_enchantments]] (upsert by `enchantment_id` via `impl Seed for Enchantment`, [[server/spacetimedb/src/main/seeds.rs#seed#7|main/seeds.rs]]), [[server/spacetimedb/src/item/reducers.rs#upsert_enchantment|upsert_enchantment]] (admin).
  - delete: **never deleted**.
- Client read: global anonymous view `all_enchantments` ([[server/spacetimedb/src/item/views.rs#all_enchantments|item/views.rs]]) in the `BaseTables` wave → CatalogComponent's `AllEnchantmentsBinder` (game.tscn, replay on).
- Server readers: [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] folds socketed + innate enchantment stat modifiers; [[server/spacetimedb/src/player/methods.rs#equipped_enchantment_behaviors|equipped_enchantment_behaviors]] folds their behaviors (deterministic slot-ascending, innate-before-socketed order the client's EffectiveWeaponResolver mirrors).

^table-enchantment

**PlayerInventorySlot** — [[server/spacetimedb/src/player/tables.rs#PlayerInventorySlot|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]] (the full fixed slot scaffold — weapon/hotbar/accessory/armor/general/bag/ability roles at load-bearing indices).
  - update: **every** slot mutation goes through [[server/spacetimedb/src/player/methods.rs#update_slot|update_slot]] — [[server/spacetimedb/src/player/reducers.rs#swap_slots|swap_slots]], [[server/spacetimedb/src/player/reducers.rs#use_item|use_item]], [[server/spacetimedb/src/player/reducers.rs#spend_ability|spend_ability]] (cooldown/charge spend), [[server/spacetimedb/src/player/reducers.rs#set_slot_toggle|set_slot_toggle]], [[server/spacetimedb/src/player/reducers.rs#apply_enchantment|apply_enchantment]] / [[server/spacetimedb/src/player/reducers.rs#remove_enchantment|remove_enchantment]], [[server/spacetimedb/src/player/methods.rs#store_item_in_inventory|store_item_in_inventory]] (shared by `pickup_drop` and `take_from_chest`), [[server/spacetimedb/src/player/methods.rs#place_span|place_span]] / [[server/spacetimedb/src/player/methods.rs#clear_span|clear_span]] (multi-slot ability spans), and admin [[server/spacetimedb/src/item/reducers.rs#give_item|give_item]] / [[server/spacetimedb/src/item/reducers.rs#remove_item|remove_item]].
  - delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] only — slots are cleared by update mid-game, never deleted.
- Client read: per-caller view `local_player_inventory` (`GameTables`) → LocalPlayerInventoryComponent's `LocalPlayerInventoryBinder` (local_player.tscn, replay on) → the 39 SlotComponent UI slots, ability activation, and the echoed-row recast guard.
- Server readers: [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] and [[server/spacetimedb/src/player/methods.rs#resolve_effective_weapon|resolve_effective_weapon]] fold the equipped heads (followers of a span never contribute).

^table-player-inventory-slot

**ActiveConsumableEffect** — [[server/spacetimedb/src/player/tables.rs#ActiveConsumableEffect|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/methods.rs#apply_consumable_effect|apply_consumable_effect]] — buff effects only; Heal effects bypass the table via [[server/spacetimedb/src/combat/mod.rs#heal_player|heal_player]]. Called by [[server/spacetimedb/src/player/reducers.rs#use_item|use_item]] and by ability Heal/Buff effects.
  - update: [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] (the 1 Hz `remaining` countdown).
  - delete: the same tick's expiry sweep (with a `recompute_stats` per affected profile), [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- Client read: `public` but in **no subscription wave** — the client never sees buff rows; a buff reaches the client only through the recomputed ^table-player-stats / ^table-player-data rows it feeds.
- Server readers: [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] folds active buffs into the stat accumulators.

^table-active-consumable-effect

**LootDrop** — [[server/spacetimedb/src/player/tables.rs#LootDrop|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/player/reducers.rs#drop_item|drop_item]] — stamps the slot's runtime state (`cooldown_until`/`charges`) onto the row so it travels with the item.
  - update: **never updated**.
  - delete: [[server/spacetimedb/src/player/reducers.rs#pickup_drop|pickup_drop]] (successful take, or an expired drop touched), the 1 Hz [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] expiry sweep (`LOOT_DROP_EXPIRY` = 10 s).
- Client read: AOI view `nearby_loot_drops` ([[server/spacetimedb/src/player/views.rs#nearby_loot_drops|player/views.rs]]) in the `GameTables` wave → EntitySpawnerComponent's `NearbyLootDropsBinder` (game.tscn, replay on) spawns [[client/Scripts/Items/Drop.cs|Drop]] puppets; the PickupComponent on each Drop calls the `pickup_drop` reducer on body-entered.
- Server readers: `pickup_drop` enforces `expires_at` and a wrap-aware `INTERACT_RADIUS` check against the caller's ^table-player-position row before the take.

^table-loot-drop

**ConsumableEffectSchedule** — [[server/spacetimedb/src/player/tables.rs#ConsumableEffectSchedule|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/lifecycle.rs#init|init]] arms one 1 Hz interval job driving [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] — **the** single 1 Hz schedule in the module: consumable expiry + stat recompute, the ^table-player-hit-flag sweep, the ^table-loot-drop expiry sweep, and all of `status::methods::tick` hang off it.
  - update: **never updated**; delete: **never deleted** (no disarm path exists).
- Client read: not `public` — **server-only**.

^table-consumable-effect-schedule

# Status Effects, Zones & Charges

**ActiveStatusEffect** — [[server/spacetimedb/src/status/tables.rs#ActiveStatusEffect|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/status/methods.rs#apply_status_effect|apply_status_effect]] — the shared entry point every targeting variant calls: the status arms of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]], the charge SelfSlow in [[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]], and the 1 Hz zone refresh.
  - update: [[server/spacetimedb/src/status/methods.rs#intercept_incoming|intercept_incoming]] (every hit stamps counter/`last_event_at`, grows bleed/store pools, consumes shields), the outgoing consults [[server/spacetimedb/src/status/methods.rs#note_damage_dealt|note_damage_dealt]] / [[server/spacetimedb/src/status/methods.rs#withhold_outgoing|withhold_outgoing]], [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] (radius-wager exit stamps), the 1 Hz [[server/spacetimedb/src/status/methods.rs#tick|tick]] (pool drains, Regen, purge progress).
  - delete: consume/replace sites in the intercept and in `apply_ability_effect` (recasting a mark replaces it; a Bash consumes an armed StoreIncomingBash pool), [[server/spacetimedb/src/status/methods.rs#settle_store_mark|settle_store_mark]], [[server/spacetimedb/src/status/methods.rs#purge_profile_statuses|purge_profile_statuses]] (death/`leave_world`/disconnect — no status may tick a logged-out player), `remove_self_slow` on charge release, the tick's expiry sweep.
- Client read: raw `public` table in the `GameTables` wave → StatusBarComponent's `ActiveStatusEffectBinder` (local_player.tscn, replay on — persistent table), filtered to the local profile in code; row insert/update/delete is the source of truth, labels count down locally between the 1 Hz updates.

^table-active-status-effect

**ActiveEnemyStatusEffect** — [[server/spacetimedb/src/status/tables.rs#ActiveEnemyStatusEffect|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/status/methods.rs#apply_enemy_status|apply_enemy_status]] — from the Curse/Bash arms of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] and the 1 Hz tick's enemy-zone pass.
  - update: `apply_enemy_status` refresh on re-apply (one row per (enemy, kind) — magnitude/remaining/source replaced); the 1 Hz [[server/spacetimedb/src/status/methods.rs#tick|tick]] (`remaining` countdown).
  - delete: [[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]] (rows never outlive their enemy — called by every kill/despawn path), the tick's expiry/orphan sweep.
- Client read: raw `public` table in the `GameTables` wave → EnemyDebuffIndicatorComponent's `ActiveEnemyStatusEffectBinder` ([[client/Scenes/default_enemy.tscn|default_enemy.tscn]], replay on), filtered per enemy instance in code — the debuff chips + stagger bar.
- Server readers: the combat sink [[server/spacetimedb/src/combat/mod.rs#apply_damage_to_enemy_opts|apply_damage_to_enemy_opts]] (ArmorBreak zeroes defence, Vulnerability multiplies), the behavior tick (Slow/Stunned via movement/pacing consults), DoT ticks in the status tick.

^table-active-enemy-status-effect

**GroundZone** — [[server/spacetimedb/src/status/tables.rs#GroundZone|definition]]

- Writers:
  - insert: the CastZone arm of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] (zone at the range-clamped cursor point, owned by the caster).
  - update: the 1 Hz [[server/spacetimedb/src/status/methods.rs#tick|status tick]] (`remaining` countdown; ally zones re-apply their status each tick to live occupants, enemy zones refresh debuffs, bursts fire once at expiry).
  - delete: the tick's expiry sweep, [[server/spacetimedb/src/status/methods.rs#purge_profile_zones|purge_profile_zones]] (a caster's zones die with them — death/`leave_world`/disconnect).
- Client read: raw `public` table in the `GameTables` wave → ZoneRendererComponent's `GroundZoneBinder` (game.tscn, replay **on** — persistent table, so late joiners still see live zones; the opposite of the event-table rule) rendering a colored Polygon2D circle per `ZoneKind`.
- Server readers: the tick's zone pass computes **live** membership each tick via [[server/spacetimedb/src/player/methods.rs#players_near_point|players_near_point]] / `enemies_near_point`; [[server/spacetimedb/src/status/methods.rs#intercept_incoming|intercept_incoming]] consults DamageShare zones live.

^table-ground-zone

**AbilityCharge** — [[server/spacetimedb/src/status/tables.rs#AbilityCharge|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]] — one held charge per player; the reducer refuses while a row exists.
  - update: **never updated**.
  - delete: [[server/spacetimedb/src/status/reducers.rs#internal_release_charge|internal_release_charge]] (the shared release path behind `release_ability_charge` **and** the 1 Hz tick's `MAX_CHARGE_SECONDS` timeout auto-release), [[server/spacetimedb/src/status/methods.rs#purge_profile_statuses|purge_profile_statuses]] (death/`leave_world`/disconnect — a vanished charge spends **no** cooldown).
- Client read: raw `public` table in the `GameTables` wave → AbilityChargeComponent's `AbilityChargeBinder` (local_player.tscn, replay on — persistent table), echo-only and filtered to the local profile; it drives CombatComponent's fire suppression and the cosmetic charge SelfSlow on PositionSyncComponent. Timeout auto-releases and lifecycle purges clear the UI state by themselves, which is why there is no optimistic flag.

^table-ability-charge

# Chests

**Chest** — [[server/spacetimedb/src/chest/mod.rs#Chest|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]] (admin-only world authoring; `chunk_index` stamped from the wrapped coordinate like enemy spawns).
  - update: **never updated**; delete: **never deleted**.
- Client read: AOI view `nearby_chests` (chest/mod.rs — the same chunk filter as `nearby_loot_drops`, off ^table-player-chunk) in the `GameTables` wave → EntitySpawnerComponent's `NearbyChestsBinder` (game.tscn, replay on) spawns [[client/Scripts/Items/Chest.cs|Chest]] puppets; the InteractComponent on each chest toggles its panel on the `interact` action.

^table-chest

**ChestItem** — [[server/spacetimedb/src/chest/mod.rs#ChestItem|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/chest/mod.rs#add_item_to_chest|add_item_to_chest]] (admin-only; the `every_single_item` special case inserts one row per catalog item).
  - update: **never updated**.
  - delete: [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] — **only for non-regenerating rows**; a `regenerating` row is never consumed, so the surviving row IS the next copy.
- Client read: AOI view `nearby_chest_items` (chest/mod.rs — an OR-chain over the nearby chest ids, since ChestItem has no `chunk_index` of its own) in the `GameTables` wave → each Chest's `NearbyChestItemsBinder` ([[client/Scenes/chest.tscn|chest.tscn]], replay on) feeds the open panel's draggable ChestSlotComponent entries; dragging one onto any player SlotComponent reports `take_from_chest`.
- Server readers: `take_from_chest` does a wrap-aware `INTERACT_RADIUS` range check against the caller's ^table-player-position row and routes placement through [[server/spacetimedb/src/player/methods.rs#store_item_in_inventory|store_item_in_inventory]].

^table-chest-item

**ChestTakeCooldown** — [[server/spacetimedb/src/chest/mod.rs#ChestTakeCooldown|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] — one row per (profile, chest) for **regenerating** chests only (those rows are never consumed, so the take rate is gated instead), restamped per take; takes inside `CHEST_TAKE_COOLDOWN_SECONDS` are rejected.
  - delete: **never deleted**.
- Client read: not `public` — **server-only**.

^table-chest-take-cooldown

# Admin & Debug

**PlayerPositionDebug** — [[server/spacetimedb/src/main/debug.rs#PlayerPositionDebug|definition]]

- Writers:
  - insert + update: [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug|tick_player_position_debug]] — while armed, upserts one row per ^table-player-position row every second (position + rotation + hex/chunk indices).
  - delete: the same tick deletes rows whose player no longer has a position row; [[server/spacetimedb/src/main/debug.rs#toggle_debug|toggle_debug]] wipes the whole table when disabling.
- Client read: not `public` — **server-only**, inspect via `spacetime sql`. The client's DebugOverlay does **not** read this table — it is a purely local perf HUD (FPS/memory/enemy count), so [[client/AGENTS.md]]'s "paired with server main/debug.rs" note is stale.

^table-player-position-debug

**PlayerPositionDebugSchedule** — [[server/spacetimedb/src/main/debug.rs#PlayerPositionDebugSchedule|definition]]

- Writers:
  - insert: [[server/spacetimedb/src/main/debug.rs#toggle_debug|toggle_debug]] (admin) arms the 1 s interval job driving [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug|tick_player_position_debug]]. Never armed by `init`.
  - update: **never updated**.
  - delete: `toggle_debug` cancels the job row (and wipes ^table-player-position-debug) when already active.
- Client read: not `public` — **server-only**.

^table-player-position-debug-schedule
