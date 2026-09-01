# 07 Terrain & World Tables

## Assumed knowledge

- [[02 The Component Framework]] — the client's entity/component model and the binder pattern every row consumer here uses.
- [[03 Connection, Subscriptions & Views]] — reducers, views, subscription waves, and `TableBinderComponent`; this doc says "the `nearby_terrain_tiles` view in the game wave" without re-teaching the mechanism.
- [[06 Movement & AOI Tables]] — `PlayerChunk` (the row the AOI views key off, ^table-player-chunk) and the torus-wrap model the terrain renderer shares.
- [[00 Table Map]] — the reference spine this doc transcludes from.

## The 30-second version

This system owns the world's geometry and everything needed to generate it: the `MapConfig` grid singleton, the per-hex `BuildingTile` scaffold, the `TextureEntry` art-path catalog, four terrain rule tables (`LayeringRule`, `BaseAdjacencyRule`, `OverlayAdjacencyRule`, `DecorGroundRule`), the generation definitions (`WorldDef`/`BiomeDef`/`BiomeRegionDef`), and the generated output rows (`BiomeRegion`, `TriangleTile`, `HexDecor`). Writes happen almost entirely at publish: the `init` lifecycle hook seeds the defs and rules, `internal_add_chunks` builds the hex scaffold and upserts `MapConfig`, and `internal_generate_world_proc` runs the deterministic terrain pipeline that inserts every region/tile/decor row; admins can re-run generation or upsert defs at runtime, and `place_building`/`remove_building` are the only gameplay-shaped reducers (admin/owner-gated world authoring). Reads split three ways: `TextureEntry` reaches the client through the global `all_textures` view in the base wave, `MapConfig` is a raw `public` table in the game wave with three consumers, and `TriangleTile`/`HexDecor` stream through the AOI views `nearby_terrain_tiles`/`nearby_hex_decor` into `TerrainComponent`. The defs, the four rule tables, `BuildingTile`, and `BiomeRegion` never leave the server.

## Flowcharts

- [[flowcharts/main-terrain.canvas]] — this system's composed flow.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Terrain_subfolder/Terrain_subfolder.canvas]] — the client terrain renderer (`TerrainComponent`/`TileComponent`/the layer components).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/terrain_subfolder/terrain_subfolder.canvas]] — the server generation pipeline passes.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/world_subfolder.canvas]] — the world module as a whole (tables, hex/wrap/aoi math, views).

## Table changes

```sync
![[00 Table Map#^table-map-config{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-building-tile{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-texture-entry{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 Table Map#^table-layering-rule{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 Table Map#^table-base-adjacency-rule{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 Table Map#^table-overlay-adjacency-rule{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 Table Map#^table-decor-ground-rule{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 Table Map#^table-world-def{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 Table Map#^table-biome-def{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 Table Map#^table-biome-region-def{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 Table Map#^table-biome-region{seamless:true,title:false,marker:11.}]]
```

```sync
![[00 Table Map#^table-triangle-tile{seamless:true,title:false,marker:12.}]]
```

```sync
![[00 Table Map#^table-hex-decor{seamless:true,title:false,marker:13.}]]
```

## The grid model: hexes, chunks, and the MapConfig singleton

The world is a flat grid of pointy-top hexes; the hex math is thin wrappers over the `hexx` crate in [[server/spacetimedb/src/world/hex.rs#world_to_hex|world/hex.rs]] (`world_to_hex`/`hex_to_world`). Hexes are grouped into **chunks** — a hex-of-hexes at resolution `chunk_hex_radius` (`to_lower_res`/`to_higher_res`) — and chunks are the unit of every AOI query, because a view can filter on one indexed integer instead of doing geometry. That integer is the `chunk_index` column on `TriangleTile`/`HexDecor` (and on player/enemy rows): a bijective hex-spiral numbering computed by [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index|spiral_chunk_index]]. The client has a lockstep sibling, [[client/Scripts/World/HexMath.cs#SpiralIndex|HexMath.SpiralIndex]], consumed by both hex-grid overlays — the two copies must change together or every chunk id disagrees across the wire.

The grid's parameters live in one singleton row, `MapConfig` (id 0): chunk radius, grid dimensions in chunks (`chunk_cols` × `chunk_rows`), the world-unit hex size (`hex_outer_radius`), and four **lap vectors**. The continuous world is a torus but the hex grid is not: [[server/spacetimedb/src/world/wrap.rs#wrap_world_pos|wrap_world_pos]] wraps a position by shifting it whole *laps* — the world-space displacement of one full grid traversal in the q or r axis — and those displacement vectors are exactly what `lap_q_*`/`lap_r_*` store, computed once by [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] when it upserts the row. Exposing them on the row (rather than letting clients re-derive the hex-of-hexes math) is what lets every client pick the correct wrapped copy of a tile or entity near a grid seam; [[client/sstdbsdk/TableSubscriber.cs#SetLap|TableSubscriber]] mirrors them into the static `LapQ`/`LapR` that `InterpolationComponent` and `TerrainComponent` read every frame.

Server code never re-derives defaults: it reads the row through [[server/spacetimedb/src/world/tables.rs#load|MapConfig::load]], whose fallback is deliberately caller-chosen — simulation paths pass `0, 0` (trips `wrap_world_pos`'s `<= 0` guard, disabling torus wrapping entirely) while admin paths pass `1, 1` (collapses the grid to one degenerate chunk so wrap/AOI math still runs). The AOI helper in `player/views.rs` is the one place that reads `map_config` directly instead, because a `ViewContext` can't call the reducer-context helper.

With the seeded defaults ([[server/spacetimedb/src/main/global.rs|main/global.rs]]: `DEFAULT_CHUNK_HEX_RADIUS` = 2, 6×6 chunks, `DEFAULT_HEX_OUTER_RADIUS` = 48) the world is 36 chunks × 19 hexes = 684 hexes, i.e. 4,104 `TriangleTile` rows (6 wedges per hex) on every generation run.

## Who builds the world, and when

Every publish wipes the database, so world construction is part of the boot sequence in [[server/spacetimedb/src/main/lifecycle.rs#init|init]]:

1. `seed_default_textures` / `seed_layering_rules` / `seed_adjacency_rules` / `seed_decor_ground_rules` / `seed_world_defs` (all in [[server/spacetimedb/src/main/seeds.rs|main/seeds.rs]]) populate the catalog, rule, and definition tables. The def seeds upsert by primary key through the `Seed` trait impls (`impl Seed for WorldDef` is [[server/spacetimedb/src/main/seeds.rs#seed#4|the fourth]]), so republishes converge instead of duplicating; the rule seeds are clear-and-reinsert because rule rows have no natural key to upsert against.
2. [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] inserts one `BuildingTile` row per world hex (the per-hex scaffold buildings later hang off) and upserts the `MapConfig` singleton with freshly computed lap vectors. (The uncalled `impl Seed for MapConfig` in `main/seeds.rs` is dead weight — this upsert is the only writer.)
3. [[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_proc|internal_generate_world_proc]]("Earth", seed 0) runs the terrain pipeline, replacing all generated rows.

The same three steps are reachable at runtime through admin reducers: `add_chunks`/`clear_chunks`, `upsert_world_def`/`upsert_biome_def`/`upsert_biome_region_def`, and `generate_world_proc`/`generate_world_manual` — thin `is_admin` gates over the same internals (`main/admin.rs`). `generate_world_manual` front-ends [[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_manual|internal_generate_world_manual]], which takes explicit `ManualBiomeInput` placements instead of a `WorldDef` and seeds its PRNG from `ctx.timestamp` — the procedural path is the deterministic one (same `(world_id, seed)` ⇒ same layout), the manual path is for one-off crafted maps.

## The generation pipeline

Both front-ends converge on [[server/spacetimedb/src/world/terrain/mod.rs#run_generation|run_generation]], which always executes the same passes in the same order, sharing one PRNG `draw` counter ([[server/spacetimedb/src/world/prng.rs#splitmix64|splitmix64]] — the same hash family the client mirrors for bullet jitter, so world gen needs no shared RNG state):

1. **Biome Voronoi** ([[server/spacetimedb/src/world/terrain/voronoi.rs|voronoi.rs]]): `combine_biome_weights` sums duplicate `biome_weights` entries preserving first-seen order — the returned Vec *is* the generation order, and a HashMap's unstable iteration would silently break "same seed ⇒ same layout". `distribute_biome_seeds` then gives each biome a weight-proportional share of `BIOME_VORONOI_SEED_BUDGET` (16) seed points placed at random world hexes, and `group_cells_by_seeds` buckets every hex under its nearest seed — wrap-aware via [[server/spacetimedb/src/world/wrap.rs#wrapped_distance_sq|wrapped_distance_sq]], because a plain Euclidean nearest check is wrong across a torus seam (a seed near the seam would cede its rightful area to the other seeds).
2. **Region seeds**: per biome, one `RegionPlan` per `RegionWeight` entry whose `BiomeRegionDef` resolves (missing defs are skipped without consuming a draw, keeping the counter deterministic). Each plan becomes one `BiomeRegion` row carrying `enemy_template_ids`/`max_enemies`/`spawn_radius` copied from its def.
3. **Per-hex content**: each cell is assigned to its nearest region seed, then [[server/spacetimedb/src/world/terrain/mod.rs#write_triangle_tiles|write_triangle_tiles]] emits its 6 wedge rows and [[server/spacetimedb/src/world/terrain/decor.rs#place_decor|place_decor]] rolls its (at most one) decor prop. Every run starts by deleting all previous `BiomeRegion`/`TriangleTile`/`HexDecor` rows ([[server/spacetimedb/src/world/terrain/mod.rs#clear_world_geometry|clear_world_geometry]]) — generation is replace-all, never incremental.

The three content passes are where the four rule tables and the noise fields come in:

- **Ground** ([[server/spacetimedb/src/world/terrain/ground.rs#choose_ground|choose_ground]]): the first pick is driven by `value_noise2d` at the biome's `ground_noise_scale` fed through `weighted_pick_index_from_unit`, so nearby wedges land in the same noise band and textures cluster into patches instead of scattering per-wedge (a non-positive scale opts back into pure per-wedge randomness). If the pick conflicts with an already-decided neighbor under `BaseAdjacencyRule`, it re-rolls plain-random among the remaining candidates; if every candidate conflicts, the wedge's `texture_id` is left **empty** — a legal row the client renders as its fallback color.
- **Overlay** ([[server/spacetimedb/src/world/terrain/overlay.rs#choose_overlay|choose_overlay]]): first filtered to overlays tag-compatible with the ground texture *just chosen* — `LayeringRule` is an allow-list precisely so compatibility is opt-in per biome (the same texture can be ground in one biome and overlay in another; the role/tags/rotations live on `BiomeDef`'s config structs, not on `TextureEntry`). The pick uses [[server/spacetimedb/src/world/prng.rs#weighted_pick_with_remainder|weighted_pick_with_remainder]]: chances are percentages that needn't sum to 100, the leftover is a real "no overlay" outcome, and sums past 100 are scaled down. It rolls on its own noise field decorrelated by `OVERLAY_NOISE_SEED_OFFSET`, so debris patches don't just trace the ground-texture boundaries.
- **Decor** ([[server/spacetimedb/src/world/terrain/decor.rs#place_decor|place_decor]]): rolled once per hex right after that hex's wedges are decided, because the `DecorGroundRule` check reads the union of ground+overlay tags across the hex's own 6 wedges. `decor_min_separation` is type-agnostic and checked *before* rolling — if anything was already placed within that hex distance, no decor goes here at all. `rotation_degrees` is always 0 because each prop's shadow is a separate paired sprite baked at a fixed offset.

Adjacency is checked only against **already-written** neighbors: decided tags live in a transient `NeighborTagMap` ([[server/spacetimedb/src/world/terrain/rules.rs#decided_neighbor_tags|rules.rs]]) keyed by `(hex_q, hex_r, tri_index)`, never persisted. A wedge has two same-hex neighbors (`tri_index ± 1` — the wedges tile the hex edge-to-edge) and one cross-hex neighbor across its outer edge ([[server/spacetimedb/src/world/hex.rs#cross_hex_neighbor|cross_hex_neighbor]]), so generation order is load-bearing and deliberately fixed.

Two output-row shapes are worth knowing. `TriangleTile` bakes the overlay *result* (`overlay_texture_id`/`overlay_rotation`/`overlay_scale` — the scale copied from the biome config at generation time) so the client renders without ever joining back through `BiomeDef`; that's why the def tables can stay server-only. And a hex with no decor simply has no `HexDecor` row — decor is optional content, not a fill, so absence is the representation.

`BiomeRegion` has a second job: it **is** the enemy spawn-zone table (there is no `EnemyZone`). [[server/spacetimedb/src/enemy/methods.rs#spawn_from_biome_regions|spawn_from_biome_regions]] full-scans it on the 2 s spawn tick, spawning up to `max_enemies` of the region's `enemy_template_ids` within `spawn_radius` of its center. That tick starts disarmed — `init` never inserts the `EnemySpawnSchedule` job row; an admin arms it with [[server/spacetimedb/src/main/admin.rs#toggle_enemy_spawning|toggle_enemy_spawning]] (doc 08 covers the spawn loop itself).

## The client's three read paths

**Textures (base wave).** `TextureEntry` is the indirection layer between every `texture_id` string in the database and a Godot resource path: the global anonymous view `all_textures` rides the `BaseTables` wave into CatalogComponent's `AllTexturesBinder` (declared inline in [[client/Scenes/game.tscn|game.tscn]], replay on), which fills a plain `texture_id → res_path` dictionary in [[client/Scripts/Components/Catalog/CatalogComponent.cs#OnTextureRow|OnTextureRow]]. Everything visual — terrain included — resolves art through [[client/Scripts/Components/Catalog/CatalogComponent.cs#GetResPath|GetResPath]] (exposed as the `GameManager.GetResPath` facade); `TerrainComponent.GetTexture` `GD.Load`s and caches the `Texture2D` on first use. Because `BaseTables` is subscribed on connect, ahead of the game wave, the texture catalog is already populated by the time the client joins the world.

**MapConfig (game wave, three consumers).** The row is a raw `public` table in `GameTables`, and all three consumers are wired in live scenes: TableSubscriber's own internal `OnInsert`/`OnUpdate` hooks (the `LapQ`/`LapR` statics, reset in `_ExitTree` — one of the two sanctioned infrastructure exceptions to the binder rule, alongside DatabaseConnector's lobby hook), TerrainComponent's `MapConfigBinder` in game.tscn (→ `OnMapConfigRow`: adopts the radii and calls `InvalidateMeshes`, since cached wedge geometry is stale at a new scale), and the two debug overlays' binders — HexGridOverlayComponent's in game.tscn, HexGridOverlay3DComponent's in [[client/Scenes/world_3d.tscn|world_3d.tscn]] (all replay on, so the singleton row is delivered immediately on bind).

**Tiles and decor (game wave, AOI views).** `nearby_terrain_tiles` and `nearby_hex_decor` ([[server/spacetimedb/src/world/views.rs|world/views.rs]]) are per-caller views: they read the caller's `PlayerChunk` row through the shared `nearby_indices_from_chunk` helper with `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS` (2 rings), then filter on an OR-chain of those chunk indices against the `chunk_index` btree (the builder has no `IN`, and a caller with no chunk yet gets a deliberately unmatchable sentinel query). Because they key off `PlayerChunk` — rewritten only on real chunk crossings — the views recompute per crossing, not per 10 Hz movement report. On the client, TerrainComponent's `TerrainTilesBinder`/`HexDecorBinder` (game.tscn, replay on) feed `OnTileRowInserted/Updated/Deleted` and `OnDecorRow*`, which only update the row dictionaries (`_tilesById`/`_tilesByHex`/`_decorById`) and set a dirty flag.

## TerrainComponent: from rows to MultiMeshes

[[client/Scripts/Components/Terrain/TerrainComponent.cs|TerrainComponent.cs]] (a `Node2DComponent` declared inline in game.tscn, `z_index = -10` so terrain sits under every entity) turns those row events into drawn geometry with three deliberate frugalities:

- **One rebuild per frame.** `_Process` rebuilds only when the camera crossed a chunk boundary, the zoom changed (either alters the cull window), or the dirty flag is set — so the burst of insert/delete events from one subscription refresh or chunk crossing collapses into a single [[client/Scripts/Components/Terrain/TerrainComponent.cs#RebuildVisibleInstances|RebuildVisibleInstances]] instead of one rebuild per row event.
- **Wrap-aware culling over the cache, not a spatial query.** The rebuild iterates *every* cached hex, because rows near a torus seam are stored at their canonical (far-away) coordinates; [[client/Scripts/World/TorusMath.cs#NearestCandidate|TorusMath.NearestCandidate]] picks whichever lap-shifted copy of each hex is actually nearest the camera before the AABB cull (with a 2-hex margin) applies. Survivors are grouped per chunk into batch keys — `(TriIndex, Rotation, TextureId)` for ground/overlay, per-texture for decor/shadow — in clear-and-reused grouping buffers, so a steady-state rebuild allocates nothing.
- **A pooled scaffolding per chunk.** `OnEntityReady` pre-warms `RingChunkCount(AoiChunkRadius)` = 1 + 3R(R+1) = 19 `TileComponent` instances (the `[Export]` mirrors the server's terrain AOI radius; the pool grows on demand if more chunks are ever visible). `AcquireTile`/`Populate` hand each visible chunk's batches to one instance; chunks that left the AOI are `Clear()`ed and parked, their layers zeroed rather than freed.

The pooled instance is [[client/Scenes/Components/Terrain/tile_component.tscn|tile_component.tscn]]: a `TileComponent` root (`Node2D, IEntity` — it implements the interface directly with its own `EntityRegistry`, the `LocalPlayer` pattern) with four declared layer children — `GroundComponent` (z=0), `OverlayComponent` (z=1), `DecorShadowComponent` (z=2), `DecorComponent` (z=3). [[client/Scripts/Components/Terrain/TileComponent.cs#Populate|Populate]] fans the chunk's four batch dictionaries out to the layers; the layers themselves are [[client/Scripts/Components/Terrain/TerrainLayerComponent.cs|TerrainLayerComponent]] subclasses owning pooled `MultiMeshInstance2D` **batch leaves** — one MultiMesh per batch key, code-created because the key set is data-driven. Leaves that go inactive are zeroed (`InstanceCount = 0` in `FinishPass`), not freed, so chunk crossings reuse them.

Mesh geometry is shared across all chunks because it only depends on the batch key: `GetOrBuildMesh` builds a wedge triangle from `GetWedgeVertices` (vertices at 30°+60°·triIndex around the hex center, the local origin) and bakes the UVs to fit the *wedge's own bounding box*, spun by 120°·rotation so one texture yields three looks — a whole hex therefore shows the same crop rotated 6 times rather than one continuous image (an intentional art call, documented on the method). `AtlasTexture` region crops aren't honored by raw MultiMesh UVs, so `ResolveTexture` unwraps the atlas and bakes the crop into the UVs itself. The overlay layer scales each instance around its wedge's own centroid (`GetWedgeCentroid`) — scaling around the hex-center origin would shrink overlays toward a corner. Decor is a plain bottom-center-anchored quad per texture (`GetOrBuildDecorMesh`), so tall props rotate around their ground-contact point; the two decor layers share [[client/Scripts/Components/Terrain/DecorLayerComponent.cs|DecorLayerComponent]], which sorts instances by hex row so nearer props overdraw farther ones of the same texture.

## The hex-grid overlays

Both overlays are debug visualizations whose only server input is the `MapConfig` row. [[client/Scripts/Components/Camera/HexGridOverlayComponent.cs|HexGridOverlayComponent.cs]] (game.tscn, `visible = false` by default) redraws on camera motion, painting chunk-colored hexes (a `(cq − cr) mod 3` 3-coloring so adjacent chunks never share a color), out-of-bounds shading past the grid edge, and per-hex coordinate/chunk-index labels computed with the same `HexMath` the terrain renderer uses. [[client/Scripts/Components/Camera/HexGridOverlay3DComponent.cs|HexGridOverlay3DComponent.cs]] is its sibling inside the 3D backdrop viewport (world_3d.tscn), instancing colored tile scenes in a ring around the local player. Both adopt the real grid parameters in their `OnMapConfigRow` handlers; until the row arrives they draw with placeholder defaults.

## Buildings

`BuildingTile` is the one table here with gameplay-shaped reducers and no client at all. [[server/spacetimedb/src/world/reducers.rs#place_building|place_building]] (admin-only) stamps `building_type` + `owner_id` onto the existing scaffold row for a hex, rejecting occupied tiles; [[server/spacetimedb/src/world/reducers.rs#remove_building|remove_building]] (owner or admin) clears them. Nothing on the client subscribes to the table and no UI calls the reducers — placement is write-only world authoring, exactly like `add_chest`, because buildings aren't a designed feature yet.

## Known gaps / stubs

- **`BuildingTile` is `public` but unsubscribed** (carried from the plan's verified gap list): the client never sees buildings; the table is write-only world authoring until the feature is designed.
- **The decor and overlay pipelines ship with no content.** Every seeded `BiomeDef` has empty `overlay_textures` and `decor_configs` ([[server/spacetimedb/src/main/seeds.rs#seed_world_defs|seed_world_defs]] — decor is parked pending a 2D top-down collision/walkability decision and better art, per the seed comment). Consequences: the overlay pass and the client's two decor layers render nothing; the two seeded `DecorGroundRule`s never fire (no decor to deny); `BaseAdjacencyRule`/`OverlayAdjacencyRule` are seeded empty (`seed_adjacency_rules` clears and inserts nothing — no clashing pairs exist yet); and the `FullStones` overlay texture is seeded in the catalog but unused.
- **Region weights gate validation, not area share.** `validate_weights_sum_100` enforces the sum, but `run_generation` places exactly one region seed per `RegionWeight` entry regardless of the weight value — the `BIOME_VORONOI_SEED_BUDGET` comment in `main/global.rs` calls this out ("not yet wired up to a budget"). Region area share is effectively uniform-per-entry today.
- **Stale pre-refactor comments.** `world/hex.rs`'s `same_hex_neighbor_indices`/`cross_hex_neighbor` comments cite `TerrainManager.cs`, and the `seed_world_defs` comment cites "TerrainManager rendering" — `TerrainManager` was the pre-refactor renderer, replaced by `TerrainComponent` + `TileComponent`. The geometry facts the comments state are still accurate; only the class name is dead.
- **Client fallback constants disagree with the server defaults** (cosmetic, dev-only): `TerrainComponent` assumes a 96-unit hex and `HexGridOverlayComponent` a 32-unit hex/5-radius chunk until the `MapConfig` row arrives and overrides them — harmless because the row always lands before terrain is visible, but the numbers are not mirrored from `DEFAULT_HEX_OUTER_RADIUS` (48) / `DEFAULT_CHUNK_HEX_RADIUS` (2).

## Where to go next

`BiomeRegion`'s second life as the spawn-zone table leads straight into [[08 Enemy Tables]] — the 2 s spawn tick, the 100 ms behavior schedule, and the behavior-tree instance tables. The torus math and the `PlayerChunk` row that drives every AOI view here are the subject of [[06 Movement & AOI Tables]], and the admin reducers that re-generate the world at runtime are catalogued in [[13 Admin & Debug Tables]].
