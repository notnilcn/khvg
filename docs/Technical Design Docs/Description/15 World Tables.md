# 15 World

## Assumed knowledge

- [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] (`# World` section) is the per-table spine this doc transcludes from — every table below sync-embeds its 00 entry.
- [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] for reading order and conventions.
- [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] for the machinery every read path below assumes: a *reducer* is a transactional server function that mutates tables and returns no data; a *view* is a parameterized server query the client subscribes to by name; a subscription *wave* (`BaseTables`/`LobbyTables`/`GameTables` in `TableSubscriber`) is the set of views/tables streamed together; a `TableBinderComponent` child (one per consumed table, signals wired in the `.tscn`) re-exposes row events, with `ReplayExistingRows` on for persistent tables and off for append-only event tables.
- Maintainer refs (jump-off points, not authority — code wins): [[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]].

Gloss for newcomers: the world is a torus (edges wrap) of pointy-top hexes grouped into hex-shaped *chunks*; every AOI view filters rows by a bijective *chunk index* in hex-spiral order. The server is authoritative — the client mirrors rows and never computes terrain, elevation, or placement itself.

## The 30-second version

World owns 18 tables: the `MapConfig` grid singleton, the per-hex `BuildingTile` grid (which doubles as the elevation store), the `TextureEntry`/`AnimationEntry` catalogs every sprite and model resolves through, four tag-rule tables driving procedural generation, three definition tables (`WorldDef`/`BiomeDef`/`BiomeRegionDef`) plus three generated-instance tables (`BiomeRegion`/`HexTile`/`HexDecor`), and four BitCraft-mirrored building tables. Writes are admin-only world authoring (`add_chunks` lays the hex grid, `generate_world_proc`/`generate_world_manual` fill the instance rows, `place_building`/`remove_building` stamp single hexes) plus publish-time seeds; nothing here ticks. Reads are two global catalog views in the Base wave feeding `CatalogComponent`, three AOI views in the Game wave feeding the 2D terrain pool, the elevation map, the decor layers, and the 3D water ring — while the building views exist but no wave carries them, so finished buildings and sites have no visible end yet.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How big is the world grid, and how do clients wrap near seams? | MapConfig | [[#MapConfig]] |
| Where is per-hex elevation stored? | BuildingTile | [[#BuildingTile]] |
| How do I resolve a texture id to a sprite or 3D model? | TextureEntry | [[#TextureEntry]] |
| How do character animation clips reach the client? | AnimationEntry | [[#AnimationEntry]] |
| Which overlays may sit on which ground? | LayeringRule | [[#LayeringRule]] |
| Which ground tags may never neighbor each other? | BaseAdjacencyRule | [[#BaseAdjacencyRule]] |
| Which overlay tags may never neighbor each other? | OverlayAdjacencyRule | [[#OverlayAdjacencyRule]] |
| Which decor may never sit on which ground? | DecorGroundRule | [[#DecorGroundRule]] |
| How is a whole world (biome mix) defined? | WorldDef | [[#WorldDef]] |
| How is one biome's texture/decor content defined? | BiomeDef | [[#BiomeDef]] |
| How is an enemy spawn pool defined? | BiomeRegionDef | [[#BiomeRegionDef]] |
| Where are the live spawn regions? | BiomeRegion | [[#BiomeRegion]] |
| Where are the generated ground/overlay hexes? | HexTile | [[#HexTile]] |
| Where are the generated decor props? | HexDecor | [[#HexDecor]] |
| What buildings can exist? | BuildingDesc | [[#BuildingDesc]] |
| Where are finished buildings? | BuildingState | [[#BuildingState]] |
| Which hexes does a building cover? | FootprintTileState | [[#FootprintTileState]] |
| Where is construction progress? | ProjectSiteState | [[#ProjectSiteState]] |
| How does procedural vs manual generation work? | — (cross-table) | [[#Cross-table flows]] |
| Why do views key off chunk, not position? | — (AOI rule) | [[#MapConfig]] readers + `PlayerChunk` in ^table-player-chunk |
| How does elevation gate movement and bullets? | BuildingTile | [[#BuildingTile]] readers |
| Why does the client say `TriangleTile` when the server says `HexTile`? | HexTile | [[#HexTile]] + [[#Known gaps / stubs]] |

## Flowcharts

- [[flowcharts/main-world.canvas]] — the composed world flow (the `world` flow in `flowcharts/flows.json`: client Terrain/Camera/Catalog canvases, `game_codefile`, and the server `world`, `terrain`, and `main` subfolder canvases). Resolves once the phase-2 "Regenerate all flowcharts" compose runs.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/world_subfolder.canvas]] — server world aggregate (grid math, building, views).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/terrain_subfolder/terrain_subfolder.canvas]] — the generation pipeline passes.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Terrain_subfolder/Terrain_subfolder.canvas]] — the 2D terrain/decor render path.

All three subflowchart paths verified on disk; every `world`-flow member in `flows.json` exists. **Phase 2 proposal:** if the compose leaves a stale `flowcharts/main-terrain.canvas` behind (the old topic flow was merged into the `world` module flow — see (b) in [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]]), delete it by hand; a stale `main-<name>.canvas` is never cleaned up automatically.

## Tables

### MapConfig

#### Shape

Singleton world-grid row (id 0) in [[server/spacetimedb/src/world/tables.rs#MapConfig|definition]]: `chunk_hex_radius`/`chunk_cols`/`chunk_rows`/`hex_outer_radius` describe the hex-of-hexes grid, and the `lap_*` vectors are the world-space displacement of one full torus lap along each chunk axis — precomputed so clients can pick the correct wrapped copy of distant tiles/entities near a seam without re-deriving the hex math.

#### Changers

Upsert of id 0 in [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] (via admin [[server/spacetimedb/src/main/admin.rs#add_chunks|add_chunks]] and via `init` in [[server/spacetimedb/src/main/lifecycle.rs#init|init]]); never deleted. The upsert also derives the lap vectors from `chunk_center_hex` at `(cols, 0)` / `(0, rows)` mapped through `hex_to_world`. Never re-derive defaults at call sites — read through `MapConfig::load` in [[server/spacetimedb/src/world/tables.rs#load|load]], whose fallback rule is load-bearing: when the row is missing (before `add_chunks` ever ran) radii fall back to the `DEFAULT_*` constants and lap vectors to zero, while the chunk counts are caller-chosen because call sites deliberately disagree — simulation paths pass `0, 0`, which trips `wrap_world_pos`'s `<= 0` guard and disables torus wrapping entirely, while [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|admin]] spawn paths pass `1, 1`, collapsing the grid to a single chunk so wrap/AOI math still runs, degenerately.

#### Readers

- ***Client*** — subscribed as a raw public table in the [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave. Two consumer kinds: `TableSubscriber`'s direct `OnInsert`/`OnUpdate` hooks mirror the lap vectors into its `LapQ`/`LapR` statics (reset to zero in `_ExitTree`), which `InterpolationComponent` reads per entity per frame for torus-aware dead reckoning — visible end is every remote player and enemy puppet rendering at its nearest wrapped copy instead of across the seam. And one replaying `MapConfigBinder` child on each of `TerrainComponent`, `ElevationMapComponent`, `HexGridOverlayComponent`, `HexGridOverlay3DComponent`, `TerrainComponent3D`, and `WaterComponent3D` (wired in `game.tscn` / `world_3d.tscn`) rebuilds that component's grids and meshes from the row — visible ends are the 2D tile pool re-batching, the debug overlays redrawing, the 3D chunk grid rebuilding, and the water ring re-centering. (The `MapConfig` binders on the hex-grid overlays are this doc's share of the dropped camera/presentation doc — they own no tables themselves.)
- ***Server*** — `MapConfig::load` in every wrap/AOI/spawn path (the one place `player/views.rs` reads the row directly with 1/1 defaults in [[server/spacetimedb/src/player/views.rs#nearby_indices_from_chunk|nearby_indices_from_chunk]], since a `ViewContext` can't call the helper).

```sync
![[00 Table Map#^table-map-config{seamless:true,title:false,marker:01.}]]
```

### BuildingTile

#### Shape

One row per hex in [[server/spacetimedb/src/world/tables.rs#BuildingTile|definition]]: grid position (`hex_q/r`, `chunk_q/r`, `local_index`, spiral `chunk_index`), a legacy single-hex `building_type` stamp plus `owner_id`, and the integer `elevation` level stamped by generation. It is two concerns sharing one grid: the hex-existence check ("has `add_chunks` been called?") plus the elevation store that movement, bullets, tinting, and 3D columns all read.

#### Changers

Insert per hex in `internal_add_chunks` (empty stamp, elevation 0); delete-all in [[server/spacetimedb/src/main/admin.rs#clear_chunks|clear_chunks]] (which also cascades to the footprint/building/site rows); stamp updates in [[server/spacetimedb/src/world/reducers.rs#place_building|place_building]] / [[server/spacetimedb/src/world/reducers.rs#remove_building|remove_building]]; elevation updates in the `run_generation` elevation pass; footprint completion stamps/clears the same columns for backward compatibility via `stamp_legacy_tiles` / `clear_legacy_stamp_if_free` in [[server/spacetimedb/src/world/building.rs#complete_site|building.rs]]. `place_building` is admin-only world authoring (no client UI calls it — same category as `add_chest`): it gates on `is_admin`, rejects empty types and occupied hexes (legacy stamp *and* `hex_occupied` footprints), and its inner `if !is_admin` claim-permission consult is unreachable behind the early admin return. `remove_building` requires logged-in and clears only owner-or-admin rows (delegating footprint-owned hexes to `demolish_building`).

#### Readers

- ***Client*** — via the [[server/spacetimedb/src/world/views.rs#nearby_building_tiles|nearby_building_tiles]] AOI view (terrain ring) → `GameTables` wave → `NearbyBuildingTilesBinder` on [[client/Scripts/Components/Terrain/ElevationMapComponent.cs#ElevationMapComponent|ElevationMapComponent]] (replay on, wired in `game.tscn`) → the per-hex elevation dictionary behind four visible ends: `TerrainComponent`'s per-instance ground/overlay tint (dark-to-bright lerp by `elevation / (ElevationLevels - 1)`, mirrored in `ServerConstants.cs`), `LocalPlayer`'s movement fall prediction and the bullet elevation-band rule (both via the `GameManager.ElevationMap` facade), the hex-overlay elevation labels, and `TerrainComponent3D`'s stepped columns plus cursor ground heights (`GetWorldHeightAt` = level × `WorldHeightPerLevel`). There are no building visuals — the stamp/owner columns are write-only world authoring for now.
- ***Server*** — hex-existence check; `hex_elevation` in [[server/spacetimedb/src/world/reducers.rs#hex_elevation|reducers.rs]] (default 0 before `add_chunks`) gates `report_movement`'s walk/block/fall deltas, and enemy steering probes cliffs through the same levels; `hex_occupied` consults the legacy stamp.

```sync
![[00 Table Map#^table-building-tile{seamless:true,title:false,marker:02.}]]
```

### TextureEntry

#### Shape

One row per texture id in [[server/spacetimedb/src/world/tables.rs#TextureEntry|definition]]: the 2D sprite/icon path plus the 3D model path, where empty `res_path3d` means "no 3D representation" (e.g. 2D shadow decals — 3D lighting casts real shadows), plus the `TextureKind` enum (Player/Enemy/Projectile/Item/Environment).

#### Changers

Upsert by `Seed` in [[server/spacetimedb/src/main/seeds.rs#seed_default_textures|seed_default_textures]] (players, enemies, arrow, items, the Grass/Flowers/HalfStones/FullStones ground set, Tree/Rock decor plus their shadow sprites) and by admin [[server/spacetimedb/src/main/admin.rs#upsert_texture_entry|upsert_texture_entry]] / `upsert_texture_entry_fields`; never deleted.

#### Readers

- ***Client*** — via the [[server/spacetimedb/src/world/views.rs#all_textures|all_textures]] global view → [[client/sstdbsdk/TableSubscriber.cs#BaseTables|BaseTables]] wave → `AllTexturesBinder` on [[client/Scripts/Components/Catalog/CatalogComponent.cs#CatalogComponent|CatalogComponent]] → the `texture2DCache`/`texture3DCache` behind `GetResPath2D`/`GetResPath3D` (empty 3D path normalized to null), which every sprite, icon, drop, enemy, remote-player, and character-model load resolves through the `GameManager` facade. Visible end is literally every textured thing on screen — and 3D mode loads no 2D world asset at all (`TerrainComponent` early-outs, entity sprites stay hidden), while 2D mode never touches a `res_path3d`.

```sync
![[00 Table Map#^table-texture-entry{seamless:true,title:false,marker:03.}]]
```

### AnimationEntry

#### Shape

One named animation clip in [[server/spacetimedb/src/world/tables.rs#AnimationEntry|definition]]: the source scene whose `AnimationPlayer` holds the clip (`res_path`), the clip name inside it, and the `AnimationKind` (Locomotion/Combat/Interaction/Emote). Ids are namespaced `"<lib>/<clip>"` so identically named clips across libraries never collide.

#### Changers

Upsert by `Seed` in [[server/spacetimedb/src/main/seeds.rs#seed_default_animations|seed_default_animations]] (the 43+43 Universal Animation Library clips) and by admin [[server/spacetimedb/src/main/admin.rs#upsert_animation_entry|upsert_animation_entry]]; never deleted.

#### Readers

- ***Client*** — via the [[server/spacetimedb/src/world/views.rs#all_animations|all_animations]] global view → `BaseTables` wave → `AllAnimationsBinder` on `CatalogComponent` → the animation cache behind `GetAnimation(s)`, whose clips merge into the active character model's `AnimationPlayer` and play by id — visible end is every 3D character animation (`LocalPlayer`/`Enemy`/`RemotePlayer` each mirror a `CharacterModel3D`).

```sync
![[00 Table Map#^table-animation-entry{seamless:true,title:false,marker:04.}]]
```

### LayeringRule

#### Shape

Allow-list in [[server/spacetimedb/src/world/tables.rs#LayeringRule|definition]]: an overlay tag may layer on an allowed base tag. Default is deny — compatibility is opt-in because most overlay/base pairs don't make sense.

#### Changers

Delete-all plus insert of the seed set in [[server/spacetimedb/src/main/seeds.rs#seed_layering_rules|seed_layering_rules]] (`light_debris` on `soft`/`delicate`, `heavy_debris` on `soft` only — never on Flowers); never touched by reducers.

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it, no visible end.
- ***Server*** — per-hex compatibility check in [[server/spacetimedb/src/world/terrain/rules.rs#overlay_compatible_with_base|overlay_compatible_with_base]], consulted by `choose_overlay` so only tag-compatible overlays are eligible for a hex.

```sync
![[00 Table Map#^table-layering-rule{seamless:true,title:false,marker:05.}]]
```

### BaseAdjacencyRule

#### Shape

Deny-list in [[server/spacetimedb/src/world/tables.rs#BaseAdjacencyRule|definition]] of base-tag pairs that may never sit in neighboring tiles. Default is allow; symmetric (one row blocks either direction), so one row per clashing pair.

#### Changers

Delete-all in [[server/spacetimedb/src/main/seeds.rs#seed_adjacency_rules|seed_adjacency_rules]] — seeded **empty**, because the current ground set has no clashing pairs yet; never touched by reducers.

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it, no visible end.
- ***Server*** — neighbor check in [[server/spacetimedb/src/world/terrain/rules.rs#base_tags_conflict|base_tags_conflict]], consulted by `choose_ground`'s retry-on-conflict loop against already-decided neighbors.

```sync
![[00 Table Map#^table-base-adjacency-rule{seamless:true,title:false,marker:06.}]]
```

### OverlayAdjacencyRule

#### Shape

Same shape and symmetry as ^table-base-adjacency-rule but overlay-to-overlay, kept as a separate table in [[server/spacetimedb/src/world/tables.rs#OverlayAdjacencyRule|definition]] so a shared tag string can never cross-apply between a base-to-base rule and an overlay-to-overlay rule.

#### Changers

Delete-all in `seed_adjacency_rules` — seeded **empty** (no overlays in use); never touched by reducers.

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it, no visible end.
- ***Server*** — neighbor check in [[server/spacetimedb/src/world/terrain/rules.rs#overlay_tags_conflict|overlay_tags_conflict]], consulted by `choose_overlay`'s retry loop.

```sync
![[00 Table Map#^table-overlay-adjacency-rule{seamless:true,title:false,marker:07.}]]
```

### DecorGroundRule

#### Shape

Deny-list in [[server/spacetimedb/src/world/tables.rs#DecorGroundRule|definition]]: decor with a tag may never sit on a hex carrying a denied ground/overlay tag. Not symmetric like the adjacency rules, because it relates two different tag kinds (decor vs ground/overlay).

#### Changers

Delete-all plus insert of the seed set in [[server/spacetimedb/src/main/seeds.rs#seed_decor_ground_rules|seed_decor_ground_rules]] (`tall` and `solid` both denied on `light_stone` — trees and rocks are grass/flowers-only); never touched by reducers.

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it, no visible end.
- ***Server*** — per-hex decor conflict check in [[server/spacetimedb/src/world/terrain/rules.rs#decor_ground_conflict|decor_ground_conflict]], consulted by `place_decor` against the hex's own decided ground+overlay tags.

```sync
![[00 Table Map#^table-decor-ground-rule{seamless:true,title:false,marker:08.}]]
```

### WorldDef

#### Shape

One named world in [[server/spacetimedb/src/world/def_tables.rs#WorldDef|definition]]: display name plus the biome weight list for procedural generation (weights must sum to 100 ± 0.5 — enforced by `validate_weights_sum_100`).

#### Changers

Upsert by `Seed` in [[server/spacetimedb/src/main/seeds.rs#seed_world_defs|seed_world_defs]] (`Earth`: Grassland 40 / Meadow 30 / Highlands 30) and by admin [[server/spacetimedb/src/main/admin.rs#upsert_world_def|upsert_world_def]]; never deleted.

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it, no visible end.
- ***Server*** — [[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_proc|internal_generate_world_proc]] reads the def (and its biomes) to generate; see [[#Cross-table flows]].

```sync
![[00 Table Map#^table-world-def{seamless:true,title:false,marker:09.}]]
```

### BiomeDef

#### Shape

One biome in [[server/spacetimedb/src/world/def_tables.rs#BiomeDef|definition]]: ground/overlay/decor texture configs with chances, tags, rotations, and noise scales, plus decor separation. A texture's role, tags, and rotations are per-biome, not intrinsic — the same `texture_id` can be ground in one biome and overlay in another.

#### Changers

Upsert by `Seed` in `seed_world_defs` (Grassland grass-dominant, Meadow flower-dominant, Highlands stonier — all ground-textures only) and by admin [[server/spacetimedb/src/main/admin.rs#upsert_biome_def|upsert_biome_def]]; never deleted.

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it, no visible end.
- ***Server*** — read per hex by `choose_ground` / `choose_overlay` / `place_decor` during generation. Note the seeded biomes all carry empty `overlay_textures` and `decor_configs`, so those two passes are live but currently unfed (see [[#Known gaps / stubs]]).

```sync
![[00 Table Map#^table-biome-def{seamless:true,title:false,marker:10.}]]
```

### BiomeRegionDef

#### Shape

One spawn-region definition in [[server/spacetimedb/src/world/def_tables.rs#BiomeRegionDef|definition]]: enemy template ids, max enemies, spawn radius.

#### Changers

Upsert in [[server/spacetimedb/src/main/seeds.rs#seed_region_def|seed_region_def]] (nine defs sharing one uniform pool — `Enemy`/`Archer`, 5 max, 300.0 radius) and by admin [[server/spacetimedb/src/main/admin.rs#upsert_biome_region_def|upsert_biome_region_def]]; never deleted.

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it, no visible end.
- ***Server*** — generation instantiates live ^table-biome-region rows from these defs; the enemy spawner reads the live rows, never the defs.

```sync
![[00 Table Map#^table-biome-region-def{seamless:true,title:false,marker:11.}]]
```

### BiomeRegion

#### Shape

One generated spawn region in [[server/spacetimedb/src/world/instance_tables.rs#BiomeRegion|definition]]: biome link, center hex, template ids, caps. There is deliberately no `EnemyZone` table — spawn regions *are* these rows.

#### Changers

Delete-all plus insert per region in [[server/spacetimedb/src/world/terrain/mod.rs#run_generation|run_generation]] (via `clear_world_geometry`); never touched by reducers.

#### Readers

- ***Client*** — `public` but unsubscribed: no view or wave carries it, no visible end.
- ***Server*** — `tick_enemy_spawn` picks templates and caps live enemies per region.

```sync
![[00 Table Map#^table-biome-region{seamless:true,title:false,marker:12.}]]
```

### HexTile

#### Shape

One row per generated hex in [[server/spacetimedb/src/world/instance_tables.rs#HexTile|definition]]: position, chunk, ground texture + rotation, optional overlay (with the scale copied from the biome's `OverlayTextureConfig` at generation time so clients render without joining `BiomeDef`), biome/region links. One row per *hex* — there is no `tri_index` column. The client still consumes a `TriangleTile` type with per-wedge `(TriIndex, Rotation, TextureId)` batch keys, but server grep finds no such table: the bindings are stale (predate the per-wedge → per-hex restructure) and were never regenerated. Code wins, so this doc follows `HexTile`; see the proposal in [[#Known gaps / stubs]].

#### Changers

Delete-all plus insert per hex in `run_generation` ([[server/spacetimedb/src/world/terrain/mod.rs#write_hex_tile|write_hex_tile]]; skipped when the biome resolved no ground textures); never updated or touched by reducers.

#### Readers

- ***Client*** — via the [[server/spacetimedb/src/world/views.rs#nearby_terrain_tiles|nearby_terrain_tiles]] AOI view (wider terrain ring) → `GameTables` wave → `TerrainTilesBinder` on `TerrainComponent` (replay on, wired in `game.tscn`) → pooled `TileComponent` ground/overlay MultiMesh batches — visible end is the 2D ground itself — plus `WaterComponent3D`'s `NearbyTerrainTilesBinder` (wired in `world_3d.tscn`), which ref-counts land rows per hex and floats water tiles exactly where rows are *missing* — visible end is the ocean ring with coastal bands. Caveat: both components cast rows to the stale `TriangleTile` binding type, so these two paths are broken until the bindings regenerate (see [[#Known gaps / stubs]]).
- ***Server*** — no reducer reads these rows; they are a pure client payload (movement reads elevation off ^table-building-tile instead).

```sync
![[00 Table Map#^table-hex-tile{seamless:true,title:false,marker:13.}]]
```

### HexDecor

#### Shape

At most one decor prop per hex in [[server/spacetimedb/src/world/instance_tables.rs#HexDecor|definition]]: texture plus paired shadow sprite (rotating as one unit), rotation, biome/region. Hexes without decor simply have no row — decor is optional content, not required fill.

#### Changers

Delete-all plus insert in `run_generation` ([[server/spacetimedb/src/world/terrain/decor.rs#place_decor|place_decor]], separation rules enforced); never updated or touched by reducers. Effectively zero rows today: every seeded biome carries an empty `decor_configs`, so `place_decor` early-returns.

#### Readers

- ***Client*** — via the [[server/spacetimedb/src/world/views.rs#nearby_hex_decor|nearby_hex_decor]] AOI view → `GameTables` wave → `HexDecorBinder` on `TerrainComponent` → the decor/shadow MultiMesh layers of each pooled tile — visible end is trees/rocks and their shadows once any biome seeds decor configs. Live but unfed.

```sync
![[00 Table Map#^table-hex-decor{seamless:true,title:false,marker:14.}]]
```

### BuildingDesc

#### Shape

Static building catalog in [[server/spacetimedb/src/world/building.rs#BuildingDesc|definition]] (the BitCraft `building_desc` + construction-recipe mirror, collapsed): footprint as rotated axial deltas with per-tile roles (`Hitbox`/`Walkable`/`Perimeter`), max health, build cost in work actions.

#### Changers

Upsert in [[server/spacetimedb/src/world/building.rs#seed_default_buildings|seed_default_buildings]] (`hut` 1-hex/3-actions, `hall` 7-hex/10-actions, `paved` walkable/1-action) and by admin [[server/spacetimedb/src/world/building.rs#upsert_building_desc|upsert_building_desc]]; never deleted.

#### Readers

- ***Client*** — `public` but unsubscribed (no wave carries `all_building_desc`), no visible end.
- ***Server*** — footprint math ([[server/spacetimedb/src/world/building.rs#footprint_cells|footprint_cells]], rotated 60° steps per `direction`) for placement, occupancy, and claim checks; completion reads costs.

```sync
![[00 Table Map#^table-building-desc{seamless:true,title:false,marker:15.}]]
```

### BuildingState

#### Shape

One finished building anchor in [[server/spacetimedb/src/world/building.rs#BuildingState|definition]] (the BitCraft `BuildingState` mirror): anchor hex, chunk, direction, def link, territory (`None` = wilderness), owner. Position lives on this row instead of a separate `LocationState` table — one less join.

#### Changers

Insert on site completion in `complete_site` (instant for zero-action descs); update of `territory_id` to wilderness on claim dissolve; delete in [[server/spacetimedb/src/world/building.rs#demolish_building|demolish_building]] and `clear_chunks`; never otherwise updated.

#### Readers

- ***Client*** — `public` but unsubscribed (no wave carries `nearby_buildings`), no visible end.
- ***Server*** — occupancy checks; claim-dissolve releases these rows.

```sync
![[00 Table Map#^table-building-state{seamless:true,title:false,marker:16.}]]
```

### FootprintTileState

#### Shape

One row per footprint hex of a finished building in [[server/spacetimedb/src/world/building.rs#FootprintTileState|definition]] (the BitCraft `FootprintTileState` mirror): owner building, hex, chunk, per-tile role.

#### Changers

Insert per cell in `complete_site`; delete in `demolish_building` and `clear_chunks`; never updated.

#### Readers

- ***Client*** — `public` but unsubscribed (no wave carries `nearby_footprint_tiles`), no visible end.
- ***Server*** — `hex_occupied`, the first occupancy check consulted (before site footprints and the legacy stamp).

```sync
![[00 Table Map#^table-footprint-tile{seamless:true,title:false,marker:17.}]]
```

### ProjectSiteState

#### Shape

One construction site in [[server/spacetimedb/src/world/building.rs#ProjectSiteState|definition]] (the BitCraft `ProjectSiteState` mirror, scoped to work progress): anchor, def, direction, work progress, owner, territory. The footprint stays implicit from the desc so site and building ids never collide in one owner column.

#### Changers

Insert in [[server/spacetimedb/src/world/building.rs#place_project_site|place_project_site]] (footprint + claim-permission validated, territory resolved to the single covering claim or wilderness); progress update in [[server/spacetimedb/src/world/building.rs#advance_project_site|advance_project_site]] until completion converts it via `complete_site`; delete in `complete_site`, [[server/spacetimedb/src/world/building.rs#cancel_project_site|cancel_project_site]], and `clear_chunks`.

#### Readers

- ***Client*** — `public` but unsubscribed (no wave carries `nearby_project_sites`), no visible end.
- ***Server*** — occupancy re-checks against other sites at advance time (excluding the site's own implicit footprint); claim-dissolve releases the territory link.

```sync
![[00 Table Map#^table-project-site{seamless:true,title:false,marker:18.}]]
```

## Files — pointer deep dive

In [[server/spacetimedb/src/world/pointers.md|pointers]] order. The pointers say *what* each file uses; this section says *how*.

### aoi.rs

Reducer-side chunk selection. [[server/spacetimedb/src/world/aoi.rs#surrounding_chunk_indices|surrounding_chunk_indices]] hex-ranges around a chunk, wraps each coordinate, and dedupes by spiral id — called by `nearby_indices_from_chunk` (every view's AOI set) and the enemy sim. [[server/spacetimedb/src/world/aoi.rs#chunk_indices_covering|chunk_indices_covering]] converts a world-space circle into a chunk-index superset for `enemies_near_point`/`players_near_point` by deliberately over-estimating rings, so callers must still apply an exact wrapped-distance filter per row.

### building.rs

The full building system: tables and views above, plus footprint math (`normalize_direction`, `rotate_offset` stepping 60° clockwise in `HEX_NEIGHBOR_DELTAS` order, `footprint_cells`), occupancy (`hex_occupied` checks finished footprints, then live site footprints computed on the fly, then the legacy stamp), claim consults (`territory_under_footprint` requires every cell in one territory; `check_build_perm_for_cells` converts cells to world pos via `MapConfig::load(ctx, 0, 0)` + `hex_to_world`), legacy-stamp sync (`stamp_legacy_tiles` on completion, `clear_legacy_stamp_if_free` on demolition), and the five reducers/seed/views named in the table sections. `claims/mod.rs` writes `BuildingState`/`ProjectSiteState` on unclaim; `main/admin.rs` deletes all four tables' rows in `clear_chunks`.

### def_tables.rs

Pure schema plus the generation-input structs: weight structs (`BiomeWeight`, `RegionWeight`), per-biome texture configs (with the role-is-per-biome and chance-model comments that `choose_ground`/`choose_overlay` implement), and the manual-front-end inputs (`ManualBiomeInput`/`ManualRegionInput`). Written by `main/seeds.rs` and the three `upsert_*_def` admin reducers; read only inside `terrain/`.

### hex.rs

Hex/chunk math over the `hexx` crate: `world_to_hex`/`hex_to_world` (pointy-top layout), `chunk_center_hex`/`inv_hexmod` (chunk ↔ hex resolution), `world_to_chunk`, `hex_neighbors` over `HEX_NEIGHBOR_DELTAS`, and `enumerate_world_hexes` (every cell with its spiral chunk id and world pos — the cell list both generation fronts feed to `run_generation`). [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index|spiral_chunk_index]] is the hand-rolled bijective hex-spiral whose lockstep client sibling is `HexMath.SpiralIndex` in [[client/Scripts/World/HexMath.cs#SpiralIndex|HexMath.cs]] (same arm/step math, consumed by both hex-grid overlays) — both copies must change together, as must the `ServerConstants.cs` mirrors of `global.rs`.

### instance_tables.rs

Pure schema for the three generated tables. No outside-module writers — only `terrain/` inserts/deletes; the enemy spawner reads `BiomeRegion`.

### mod.rs

Module docs plus the `pub mod` list. No tables, no logic — the composition note (one table per concern, ids as entity joins) mirrors the client entity/component structure.

### noise.rs

Deterministic value noise: `noise_lattice_hash` keyed by integer lattice coords (same hash family as the PRNG, so a `(seed, x, y)` always reproduces), `smoothstep` interpolation in [[server/spacetimedb/src/world/noise.rs#value_noise2d|value_noise2d]]. Three decorrelation offsets — `OVERLAY_NOISE_SEED_OFFSET`, `DECOR_NOISE_SEED_OFFSET`, `ELEVATION_NOISE_SEED_OFFSET` — give ground, overlay, decor, and elevation independent fields from one world seed, so debris patches don't just trace ground-texture boundaries. Non-positive scales opt each pass back to plain per-hex randomness.

### prng.rs

Deterministic SplitMix64 (`splitmix64`/`hash_to_unit`/`next_unit` over a shared `draw` counter — same `(world_id, seed)` always yields the same layout) plus weighted picks: `pick_index`, `weighted_pick_index` (relative weights, zero-total falls back to 0), and `weighted_pick_with_remainder` (0–100 chances where leftover is real "pick nothing" and overshoot scales down instead of overflowing). The `_from_unit` variants drive picks from an already-computed noise value instead of a fresh draw, which is what makes texture picks spatially coherent. The enemy module reuses `splitmix64`/`hash_to_unit`/`next_unit` for wander and spawn jitter.

### reducers.rs

`hex_elevation` (hex_q-btree + hex_r-find, default 0 — the same lookup `place_building` uses) plus the two admin-authoring reducers `place_building`/`remove_building` detailed under [[#BuildingTile]].

### tables.rs

Schema for the eight grid/catalog/rule tables plus `TextureKind`/`AnimationKind` and the `MapConfig::load` fallback helper detailed under [[#MapConfig]]. Read across the whole server (movement, combat, zones, chests, enemies all load `MapConfig`); written only by `internal_add_chunks`, the rule/texture/animation seeds, and the catalog upserts.

### terrain/decor.rs

[[server/spacetimedb/src/world/terrain/decor.rs#place_decor|place_decor]] runs per cell right after that cell's ground+overlay passes and reads the tags they just wrote: filter configs by `decor_ground_conflict`, enforce type-agnostic `decor_min_separation` against already-decided decor (checked first, with no retry loop — unlike ground/overlay), roll once on the decor noise field, insert at most one `HexDecor` row. Rotation is always 0.0 — decor art has its shadow baked into a fixed relative position, so rotating would break that relationship.

### terrain/ground.rs

[[server/spacetimedb/src/world/terrain/ground.rs#choose_ground|choose_ground]] picks one ground texture per hex: first pick follows the biome noise field (clustering), then retry-on-conflict re-rolls plain-random among the rest until one clears every already-decided neighbor's `base_tags_conflict` — or the hex is left blank (empty texture id). Rotation comes from `pick_rotation` (explicit 0–5 allow-list wins, else uniform 0–5).

### terrain/mod.rs

Orchestrator. [[server/spacetimedb/src/world/terrain/mod.rs#run_generation|run_generation]] executes the fixed passes in deterministic draw order sharing one `draw` counter: `clear_world_geometry`, then the elevation pass (quantizes its own noise field into `ELEVATION_LEVELS` levels and stamps `BuildingTile` rows — consuming no draws so layout determinism is untouched), then per biome: region plans → `BiomeRegion` inserts → per hex `write_hex_tile` (ground → overlay → record decided tags → insert `HexTile`) + `place_decor`. Missing biome defs become `skipped()` inputs whose seeds still claim cells but emit nothing; region entries with missing defs are skipped without a draw. The two fronts differ only in input resolution — see [[#Cross-table flows]].

### terrain/overlay.rs

[[server/spacetimedb/src/world/terrain/overlay.rs#choose_overlay|choose_overlay]] picks at most one overlay per hex: eligible = tag-compatible with the ground texture just chosen (`overlay_compatible_with_base`), first pick on the decorrelated overlay noise field via `weighted_pick_with_remainder` (leftover chance is genuinely "no overlay", overshoot scales down), retry-on-conflict against decided neighbors' `overlay_tags_conflict`. The chosen scale is copied onto the `HexTile` row so clients never join `BiomeDef`.

### terrain/rules.rs

The four table-driven checks (`overlay_compatible_with_base`, `base_tags_conflict`, `overlay_tags_conflict`, `decor_ground_conflict`) plus the transient `DecidedHexTags`/`NeighborTagMap` — tags of already-written hexes kept just long enough for not-yet-written neighbors to check adjacency against, never persisted to `HexTile`. `decided_neighbor_tags` reads the six lateral neighbors via `hex_neighbors`.

### terrain/voronoi.rs

Seed distribution: `validate_weights_sum_100` gates world and region weights; `combine_biome_weights` sums duplicate biome entries preserving first-seen order (a `HashMap`'s order isn't stable across publishes, which would silently break same-seed determinism); `distribute_biome_seeds` deals each biome a proportional share of `BIOME_VORONOI_SEED_BUDGET` (16) seed points with at least one per positive weight; `group_cells_by_seeds` buckets every hex under its wrap-aware nearest seed (`nearest_seed_index` over `wrapped_distance_sq`, so seam-adjacent seeds aren't cheated).

### views.rs

Two global catalog views (`all_textures`, `all_animations`) and three AOI views (`nearby_terrain_tiles`, `nearby_building_tiles`, `nearby_hex_decor`) — all three AOI views build their chunk set from `nearby_indices_from_chunk` with `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS` (2) and OR-chain the `chunk_index` equality (the builder has no `IN`), returning a sentinel unmatchable query when the caller has no chunk yet. Subscribed in `BaseTables` (catalogs) and `GameTables` (the three AOI views); the building views in `building.rs` (`all_building_desc`, `nearby_buildings`, `nearby_footprint_tiles`, `nearby_project_sites`) follow the same AOI shape but no wave subscribes to them.

### wrap.rs

Torus math. `wrap_chunk_coords` (`rem_euclid`), [[server/spacetimedb/src/world/wrap.rs#wrap_world_pos|wrap_world_pos]] (shifts x/y by lap-period vectors when the chunk coord wrapped; `cols/rows <= 0` disables wrapping — the `MapConfig::load(ctx, 0, 0)` degenerate case), and `wrapped_distance_sq` (checks all 9 lap-shift combinations — the server mirror of the client's `TorusMath.NearestCandidate`, needed whenever a stored wrapped position is compared against an unwrapped region/chunk-center point). Consumed by movement, teleports, chests, enemies, zones, and hit validation.

## Cross-table flows

Both generation fronts funnel into one parameterized `run_generation` and differ only in how seeds and content resolve. `internal_generate_world_proc(world_id, seed)` (called by `init` with `"Earth", 0` after `internal_add_chunks(2, 6, 6, 48.0)`, and by admin `generate_world_proc`) validates the `WorldDef` weights, enumerates cells from the stored `MapConfig` (erroring when there are no hexes — `add_chunks` first), deals Voronoi seeds proportional to combined biome weights, and resolves each biome's content from its `BiomeDef` with per-biome region plans drawn from `region_weights`. `internal_generate_world_manual(biomes)` (admin `generate_world_manual` only) instead takes explicit `ManualBiomeInput`s — one seed point per biome from its center hex, fixed region plans — and seeds its PRNG from `ctx.timestamp`, so manual runs are not reproducible. Either way `run_generation` clears `BiomeRegion`/`HexTile`/`HexDecor`, stamps `BuildingTile.elevation`, and writes the new rows in one deterministic pass — which is also why every publish wipes the world and rebuilds it from seeds.

## Known gaps / stubs

- **Stale `TriangleTile` binding (verified).** The server defines only `HexTile`, but the generated C# bindings still expose a `TriangleTile` table (per-wedge rows with `tri_index`) and `TerrainComponent` / `WaterComponent3D` cast `nearby_terrain_tiles` rows to it, keying every MultiMesh batch and land ref-count on `(TriIndex, Rotation, TextureId)`; the `TriangleTile` mentions in `TileComponent.cs` / `TerrainComponent3D.cs` comments are the same staleness. Until `server/build.sh` regenerates the bindings and those two components migrate to `HexTile`, the 2D terrain pool and the 3D water ring read the wrong row shape. (00's `HexTile` entry now carries the one-line staleness note; the binding regen + component migration is still pending.)
- **`MapConfig::seed` is dead (verified).** The 00 `MapConfig` entry no longer cites it as a changer (only `internal_add_chunks` writes the row) — nothing constructs a `MapConfig` except `internal_add_chunks`; the impl has zero callers (kept or removed in code).
- **`BuildingTile` write-only nuance (verified).** The pre-existing gap note ("`BuildingTile` is `public` but the client never subscribes") is stale for the elevation column — `nearby_building_tiles` *is* in the Game wave feeding `ElevationMapComponent`. What remains true is the write-only-authoring half: the `building_type`/owner stamp has no readers anywhere (no building visuals, admin-only `place_building`/`remove_building`, no client UI calls them), and all four building views go unread.
- **Decor and overlay pipelines are live but unfed (verified).** Every seeded biome carries empty `overlay_textures` and `decor_configs`, and both adjacency tables seed empty — so `choose_overlay` always yields `None`, `place_decor` early-returns, and `HexTile.overlay_texture_id` is always null in practice. The 2D decor MultiMesh layers, the `HexDecorBinder` path, and the rule tables' retry loops all work but have no content to act on (per the seed comment: decor collision/walkability and art readability are undecided).
- **`place_building`'s claim consult is unreachable (verified).** The `if !is_admin` territory-permission branch sits behind the early `is_admin` return, so only admins ever reach it — placement inside claims currently bypasses `check_build` by construction, not by policy.
- **3D terrain ignores per-hex textures (code-documented).** `TerrainComponent3D` renders the MarchingSquaresTerrain default ground texture because the 2D per-wedge sprite system has no mapping onto MST texture slots; distant chunks also render flat at level 0 until the elevation AOI reaches them, and seam-edge chunks read void neighbors (cliff walls at the world edge) since only the canonical lap is built.

(The old `hex_grid_overlay.gd` stale comment in `world/hex.rs` is gone and is not listed here.)

## Where to go next

Read [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03]] for the subscription machinery this doc leans on; then the Enemy doc (the biome spawner reads ^table-biome-region and the sim probes elevation), the Claims doc (footprint/claim consults gate every building write), and the Player doc (`report_movement`'s elevation gates and the `PlayerChunk`-not-`Position` AOI rule).

## Phase 2 proposals

- **TriangleTile → HexTile (code work still pending):** regenerate the C# bindings via `server/build.sh`; migrate `TerrainComponent.cs` / `WaterComponent3D.cs` (casts, `(TriIndex, …)` batch keys, land ref-counts) and the `TileComponent.cs` / `TerrainComponent3D.cs` comments to `HexTile`. The 00 `HexTile` staleness note is applied.
- **Flowcharts:** this doc links the NEW `flowcharts/main-world.canvas` (the `world` flow); if the compose leaves a stale `flowcharts/main-terrain.canvas` behind, hand-delete it per the stale-main rule.
