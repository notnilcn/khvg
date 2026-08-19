# 07 Terrain & World Streaming

## Assumed knowledge

- [[02 The Component Framework]] — what a `*Component` node is, how it registers with its `IEntity` root, and what a `TableBinderComponent` does (re-exposes subscribed table row events as Godot signals).
- [[03 Boot & Connection]] — how the client connects, what a subscription wave is, and how `init` seeds the database on every publish.
- [[05 Joining the World]] — how the game subscription wave opens at join and why every AOI view keys off `PlayerChunk`.
- [[06 Movement & Position Sync]] — the torus lap vectors, `TorusMath.NearestCandidate`, and the write-`PlayerChunk`-only-on-crossing rule that drives all streaming.
- [[00 End-to-End Timeline Flowchart]] — the whole-timeline view this doc's steps are transcluded from.

## The 30-second version

The world is a finite 6×6 grid of hexagonal **chunks** (each chunk a hexagon of hexes) whose continuous space wraps like a torus. At publish time the server's `init` reducer builds the grid (one `BuildingTile` row per hex plus a singleton `MapConfig` row carrying the grid dimensions and the two torus **lap vectors**), then runs a **deterministic generation pipeline**: weighted Voronoi seeds split the map into biomes, each biome gets region seeds (the enemy spawn regions), and every hex is filled wedge-by-wedge — six triangles per hex, each getting a ground texture, optionally an overlay, and at most one decor prop per hex — governed by table-driven tag rules (allow-listed layering, deny-listed adjacency, deny-listed decor-on-ground). The output is plain table rows: `TriangleTile`, `HexDecor`, `BiomeRegion`. Clients never download the whole map: two AOI **views** filter those rows to the chunk ring around the viewer's `PlayerChunk`, so walking across a chunk boundary silently inserts the new ring's rows and deletes the old. Client-side, one `TerrainComponent` mirrors the rows into dictionaries, and once per frame rebuilds only what's visible — wrapping every cached hex to its camera-nearest torus copy, culling to the camera window, and batching wedges by `(TriIndex, Rotation, TextureId)` into pooled `MultiMeshInstance2D` leaves held by one pooled `TileComponent` per visible chunk. The only player-facing world mutation is `place_building`/`remove_building`, which today has no client UI.

## Flowcharts

- [[flowcharts/main-terrain.canvas]] — the composed terrain flow (client `Terrain/` components + scenes, server `world/` + `world/terrain/` + `main/`).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/terrain_subfolder/terrain_subfolder.canvas]] — the generation pipeline deep dive (`run_generation`, voronoi, ground/overlay/decor/rules passes).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/world_subfolder.canvas]] — the world tables, hex/wrap/aoi/prng/noise math, reducers, and views.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Terrain_subfolder/Terrain_subfolder.canvas]] — `TerrainComponent`/`TileComponent`/the layer components, symbol level.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^terrain-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^terrain-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^terrain-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^terrain-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^terrain-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^terrain-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^terrain-7{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^terrain-8{seamless:true,title:false,marker:08.}]]
```

## Main body

### The data model: content in, generated rows out

Terrain is a table pipeline with three generations of tables, and reading them in that order is the fastest way to understand the system.

**Generation inputs (static content, seeded at boot, admin-editable).** The structural tables live in [[server/spacetimedb/src/world/tables.rs#TextureEntry#1|world/tables.rs]] and [[server/spacetimedb/src/world/def_tables.rs#WorldDef#1|world/def_tables.rs]]:

- `TextureEntry` maps a `texture_id` string to a `res://` path plus a `TextureKind` — the indirection every client texture lookup goes through (the client's `CatalogComponent` caches it from the base subscription wave; doc 03). Terrain uses the `Environment` entries (Grass/Flowers/HalfStones/FullStones, Tree/Rock + their shadows) seeded by [[server/spacetimedb/src/main/seeds.rs#seed_default_textures#1|seed_default_textures]].
- The four **rule tables**: `LayeringRule` (an **allow-list** — overlay tag `X` may sit on ground tag `Y`; default deny), `BaseAdjacencyRule` and `OverlayAdjacencyRule` (symmetric **deny-lists** — two tags may never be lateral neighbors; default allow), and `DecorGroundRule` (a non-symmetric deny-list — decor tag `X` may never be placed in a hex containing ground/overlay tag `Y`). All four are content, not code: seeds insert them ([[server/spacetimedb/src/main/seeds.rs#seed_layering_rules#1|seed_layering_rules]] / [[server/spacetimedb/src/main/seeds.rs#seed_adjacency_rules#1|seed_adjacency_rules]] / [[server/spacetimedb/src/main/seeds.rs#seed_decor_ground_rules#1|seed_decor_ground_rules]]), and generation reads them live from the database, so an admin can retune the world without republishing code.
- `WorldDef` ("Earth") is a weighted menu of biomes; `BiomeDef` is where nearly all the art direction lives: per-biome `GroundTextureConfig`s (texture, relative `chance`, compatibility `tags`, `allowed_rotations`), `OverlayTextureConfig`s (plus a per-overlay `scale`), `DecorConfig`s (a texture paired with an explicit `shadow_texture_id`), three independent noise frequencies (`ground_noise_scale`/`overlay_noise_scale`/`decor_noise_scale`), `decor_min_separation`, and weighted `region_weights` into `BiomeRegionDef`s (the enemy spawn recipes — template ids, `max_enemies`, `spawn_radius`). Texture roles are deliberately **per-biome, not per-texture** — the same `texture_id` can be ground in one biome and an overlay in another, *because* a texture's compatibility tags only make sense in the context of what else that biome uses. [[server/spacetimedb/src/main/seeds.rs#seed_world_defs#1|seed_world_defs]] seeds three biomes (Grassland 40%, Meadow 30%, Highlands 30%) and nine region defs through the `Seed` trait's upsert, so a republish overwrites rather than duplicates.

**Generated rows (rebuilt from scratch every publish).** [[server/spacetimedb/src/world/instance_tables.rs#TriangleTile#1|world/instance_tables.rs]] holds the pipeline's output: `BiomeRegion` (one row per region, the enemy spawn regions of doc 08), `TriangleTile` (six rows per hex — `tri_index` 0–5 — each with ground `texture_id` + `rotation`, optional `overlay_texture_id`/`overlay_rotation`/`overlay_scale`, and a btree-indexed `chunk_index`), and `HexDecor` (at most one row per hex; a hex with no decor simply has no row, *because* decor is optional content rather than a required fill). `overlay_scale` is copied onto the row at generation time specifically so the client never has to join back through `BiomeDef` to render.

**The grid itself.** The singleton [[server/spacetimedb/src/world/tables.rs#MapConfig#1|MapConfig]] row (id 0) carries `chunk_hex_radius` (2), `chunk_cols`/`chunk_rows` (6×6), `hex_outer_radius` (48 world units), and the four lap-vector components — the world-space displacement of one full grid traversal in each hex axis, precomputed precisely so clients can do torus math without re-deriving the hex-of-hexes geometry. `MapConfig::load` exists *because* the row can be absent (before `add_chunks` ever runs): callers pass their own fallback grid — simulation paths pass `0, 0` (which trips `wrap_world_pos`'s guard and disables wrapping entirely) while `main/admin.rs` passes `1, 1` (a degenerate one-chunk torus so the math still runs). And one `BuildingTile` row per hex pre-exists for the building system (below) — created empty, mutated later.

### The grid: hexes of hexes, chunks, and the spiral index

The map is a **hexagon tiling of a hexagonal grid**: small hexes (radius `hex_outer_radius` = 48) group into chunk-hexes at `chunk_hex_radius` = 2 resolution (19 small hexes per chunk, `hex_area` = `Hex::range_count(2)`), and 6×6 chunk-hexes form the world. [[server/spacetimedb/src/world/hex.rs#world_to_hex#1|world/hex.rs]] is mostly thin wrappers over the `hexx` crate: `to_higher_res` converts a chunk coordinate to its center hex ([[server/spacetimedb/src/world/hex.rs#chunk_center_hex#1|chunk_center_hex]]), `to_lower_res` converts a hex to its parent chunk ([[server/spacetimedb/src/world/hex.rs#world_to_chunk#1|world_to_chunk]]), and `from_hexmod_coordinates` enumerates a chunk's local hexes ([[server/spacetimedb/src/world/hex.rs#inv_hexmod#1|inv_hexmod]]). [[server/spacetimedb/src/world/hex.rs#enumerate_world_hexes#1|enumerate_world_hexes]] walks chunk-by-chunk, hexmod-by-hexmod, producing every `HexCell` (hex coords + chunk index + world position) — this enumeration order *is* the generation order, which is part of why generation is reproducible.

Every row that streams by AOI needs a single scalar to filter on, so the 2D chunk coordinate is flattened by [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index#1|spiral_chunk_index]] — a hand-rolled **bijective** hex-spiral numbering (ring radius from cube coordinates, then arm × steps within the ring). Bijective matters: a btree index on `chunk_index` can filter a chunk ring with simple equality OR-chains, and no two chunks ever collide.

```sync
![[00 End-to-End Timeline Flowchart#^terrain-7{seamless:true,title:false,marker:07.}]]
```

The wrap layer sits on top in [[server/spacetimedb/src/world/wrap.rs#wrap_world_pos#1|world/wrap.rs]]: chunk *coordinates* wrap with `rem_euclid` ([[server/spacetimedb/src/world/wrap.rs#wrap_chunk_coords#1|wrap_chunk_coords]]), while continuous *positions* wrap by whole lap vectors — the lap vector for axis q is defined as the world position of the hex at `chunk_center_hex(chunk_cols, 0)` (and likewise for r), i.e. how far one full grid traversal moves you in continuous space. Those exact vectors are what `internal_add_chunks` stores on the `MapConfig` row, and [[server/spacetimedb/src/world/wrap.rs#wrapped_distance_sq#1|wrapped_distance_sq]] is the server-side 9-candidate nearest-copy distance check mirroring the client's `TorusMath.NearestCandidate` (doc 06). Generation itself is wrap-aware wherever it compares distances, *because* Voronoi seeds near a seam would otherwise cede area to farther seeds across the seam.

### Generation at boot: one deterministic pipeline

Publish wipes the database (`--delete-data`), so `init` rebuilds the world every time: [[server/spacetimedb/src/main/admin.rs#internal_add_chunks#1|internal_add_chunks]] inserts the `BuildingTile` grid + `MapConfig` (boot-3), then [[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_proc#1|internal_generate_world_proc]] runs for `"Earth"` with seed `0` (boot-4). Two admin reducers — [[server/spacetimedb/src/main/admin.rs#generate_world_proc#1|generate_world_proc]] and [[server/spacetimedb/src/main/admin.rs#generate_world_manual#1|generate_world_manual]] — are gated front-ends over the same calls for CLI-driven regen, and `add_chunks`/`clear_chunks` manage the grid itself.

```sync
![[00 End-to-End Timeline Flowchart#^terrain-1{seamless:true,title:false,marker:01.}]]
```

`internal_generate_world_proc` validates (WorldDef exists, biome weights sum to 100 via [[server/spacetimedb/src/world/terrain/voronoi.rs#validate_weights_sum_100#1|validate_weights_sum_100]], `MapConfig` present — "call add_chunks first" otherwise), then resolves each weighted biome entry into a `BiomeGen` (its content plus how its regions get seeds) and hands everything to the shared orchestrator [[server/spacetimedb/src/world/terrain/mod.rs#run_generation#1|run_generation]]. The manual front-end ([[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_manual#1|internal_generate_world_manual]]) differs only in inputs: biomes/regions are placed at explicit hex coordinates, and the seed is drawn from `ctx.timestamp` — manual runs are intentionally not reproducible. Every run executes the same passes in the same order, sharing one PRNG **draw counter**, which is what makes "same `(world_id, seed)` ⇒ same map" hold:

1. **Clear.** [[server/spacetimedb/src/world/terrain/mod.rs#clear_world_geometry#1|clear_world_geometry]] deletes all previous `BiomeRegion`/`TriangleTile`/`HexDecor` rows, so regen is replace, not append.
2. **Biome Voronoi.** [[server/spacetimedb/src/world/terrain/voronoi.rs#combine_biome_weights#1|combine_biome_weights]] sums duplicate weight entries preserving first-seen order — a `HashMap`'s iteration order isn't stable across publishes, which would silently reshuffle the draw order and break reproducibility. [[server/spacetimedb/src/world/terrain/voronoi.rs#distribute_biome_seeds#1|distribute_biome_seeds]] then gives each biome a share of the `BIOME_VORONOI_SEED_BUDGET` (16) seed points proportional to its weight (every weight > 0 gets at least 1, so a small biome can't round away to nothing), each seed a random world hex. [[server/spacetimedb/src/world/terrain/voronoi.rs#group_cells_by_seeds#1|group_cells_by_seeds]] buckets every world hex under its *nearest* seed — all biomes' seeds competing in one pool — using the wrap-aware [[server/spacetimedb/src/world/terrain/voronoi.rs#nearest_seed_index#1|nearest_seed_index]] (`wrapped_distance_sq` under the hood, because on a torus a plain Euclidean nearest check is wrong across a seam).
3. **Regions.** Per biome, one seed per `RegionWeight` entry whose `BiomeRegionDef` resolves (missing defs are skipped without spending a draw — preserving the counter), each inserted as a `BiomeRegion` row; every hex then joins its nearest region seed, which is how each `TriangleTile`/`HexDecor` row gets its `region_id`.
4. **Per-hex content.** For each cell, [[server/spacetimedb/src/world/terrain/mod.rs#write_triangle_tiles#1|write_triangle_tiles]] runs the ground pass then the overlay pass for all six wedges (recording each wedge's decided tags in a transient `NeighborTagMap` for not-yet-written neighbors to check against — the tags are *not* persisted onto `TriangleTile`), and [[server/spacetimedb/src/world/terrain/decor.rs#place_decor#1|place_decor]] rolls the hex's decor immediately after, *because* its ground check reads that hex's own six wedges, which the wedge passes just finished deciding.

A biome whose `BiomeDef` is missing still claims its Voronoi cells via `BiomeGen::skipped` — those hexes simply get no content, matching the pre-refactor behavior. The randomness itself is a SplitMix64 counter: [[server/spacetimedb/src/world/prng.rs#next_unit#1|next_unit]] hashes `(seed + draw++)` to `[0,1)`, so the sequence depends only on the seed and the *order* of calls — which is why every pass above is so careful about when it does and doesn't consume a draw.

### The three passes: ground, overlay, decor

All three passes share one shape — *first pick follows a spatially-coherent noise field so neighbors cluster into patches; on a rule conflict with an already-written neighbor, retry plain-random among the remaining candidates* — and differ in their gating rule set. The noise is [[server/spacetimedb/src/world/noise.rs#value_noise2d#1|value_noise2d]]: deterministic 2D value noise (a SplitMix64-keyed lattice hash, smoothstep-bilinear-interpolated), where `scale` is spatial frequency — lower scale, larger patches. Feeding the noise *unit* into the weighted pick ([[server/spacetimedb/src/world/prng.rs#weighted_pick_index_from_unit#1|weighted_pick_index_from_unit]]) instead of drawing fresh randomness is the whole trick: the pick is still weighted-random across textures, but spatially coherent across the map. A non-positive biome noise scale opts back into a plain per-wedge draw.

**Ground** ([[server/spacetimedb/src/world/terrain/ground.rs#choose_ground#1|choose_ground]]): weighted-pick a ground texture by the biome's `chance`s, then check it against every already-decided neighbor's ground tags via [[server/spacetimedb/src/world/terrain/rules.rs#base_tags_conflict#1|base_tags_conflict]]. On conflict, re-roll among the *remaining* candidates until one fits — clustering is a nice-to-have, a guaranteed-compatible neighbor is not. If every candidate conflicts, the wedge is left **blank** (empty `texture_id`; the client renders nothing there). The wedge's rotation comes from [[server/spacetimedb/src/world/terrain/mod.rs#pick_rotation#1|pick_rotation]], restricted to the config's `allowed_rotations` (empty = all three).

**Overlay** ([[server/spacetimedb/src/world/terrain/overlay.rs#choose_overlay#1|choose_overlay]]): first filter the biome's overlays to those tag-compatible with the ground texture *just chosen* ([[server/spacetimedb/src/world/terrain/rules.rs#overlay_compatible_with_base#1|overlay_compatible_with_base]], the `LayeringRule` allow-list) — a side effect worth noticing: a ground texture with few compatible overlays naturally has a lower overall overlay chance, with no separate tuning knob. Then [[server/spacetimedb/src/world/prng.rs#weighted_pick_with_remainder_from_unit#1|weighted_pick_with_remainder_from_unit]] treats the chances as percentages that needn't sum to 100 — the leftover under 100 is a real "no overlay" outcome, and sums over 100 are scaled down rather than overflowing. Conflicts check the neighbor's *overlay* tags ([[server/spacetimedb/src/world/terrain/rules.rs#overlay_tags_conflict#1|overlay_tags_conflict]]), and only a conflict triggers the retry loop — landing in the "leftover" zone isn't retried. The overlay field is decorrelated from the ground field by XORing the seed with `OVERLAY_NOISE_SEED_OFFSET` ([[server/spacetimedb/src/world/noise.rs#OVERLAY_NOISE_SEED_OFFSET#1|noise.rs]]), so debris patches don't just trace the ground-texture boundary.

**Decor** ([[server/spacetimedb/src/world/terrain/decor.rs#place_decor#1|place_decor]]): at most one prop per hex, on its own decorrelated field (`DECOR_NOISE_SEED_OFFSET`). Eligibility is the `DecorGroundRule` deny-list ([[server/spacetimedb/src/world/terrain/rules.rs#decor_ground_conflict#1|decor_ground_conflict]]) checked against the union of ground+overlay tags across the hex's six wedges. Separation is **type-agnostic and checked up front**: if any decor was already decided within `decor_min_separation` hexes, nothing goes here at all — deliberately *instead of* a retry loop, because a sparse forest shouldn't densify just because one type was blocked. Placed decor is always upright (`rotation_degrees = 0.0`) *because* the art bakes its shadow at a fixed relative offset — rotating the prop would divorce it from its shadow.

The neighbor geometry behind the adjacency checks lives in [[server/spacetimedb/src/world/terrain/rules.rs#decided_neighbor_tags#1|decided_neighbor_tags]]: a wedge's neighbors are the two adjacent `tri_index` values in the same hex ([[server/spacetimedb/src/world/hex.rs#same_hex_neighbor_indices#1|same_hex_neighbor_indices]]) plus exactly one wedge across the hex border ([[server/spacetimedb/src/world/hex.rs#cross_hex_neighbor#1|cross_hex_neighbor]] — the wedge's outer edge points in hex-direction `(tri_index+1)%6`, and the wedge sharing that edge from the other side is `(tri_index+3)%6` of the neighbor hex, since directions 180° apart differ by 3 in a 6-direction scheme). Only *already-written* wedges are consulted, which is why generation order (hex enumeration order, wedge 0→5) is part of the deterministic contract.

### Streaming: terrain is just more AOI rows

```sync
![[00 End-to-End Timeline Flowchart#^terrain-3{seamless:true,title:false,marker:03.}]]
```

Terrain reuses the movement system's streaming machinery verbatim — that is the design point. The two views in [[server/spacetimedb/src/world/views.rs#nearby_terrain_tiles#1|world/views.rs]] follow the same two SpacetimeDB 2.x idioms doc 06 spells out (an `IN` is an OR-chain over `chunk_index` equality; the empty case returns a sentinel query matching `u64::MAX` rather than returning early), because both key off the shared helper [[server/spacetimedb/src/player/views.rs#nearby_indices_from_chunk#1|nearby_indices_from_chunk]] with `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS` = 2 — the caller's `PlayerChunk` expanded to its surrounding chunk ring by [[server/spacetimedb/src/world/aoi.rs#surrounding_chunk_indices#1|surrounding_chunk_indices]] (chunk coords wrapped, spiral-indexed, deduplicated). The consequence of the write-on-crossing rule: standing still costs zero terrain traffic, and one chunk crossing re-filters both views, arriving on the client as a burst of inserts for the gained ring and deletes for the lost one. `nearby_indices_from_chunk` reads `map_config` directly with a `1, 1` fallback (a `ViewContext` can't call `MapConfig::load`), so the views degrade to a degenerate one-chunk grid rather than failing if the row is missing. The third view, [[server/spacetimedb/src/world/views.rs#all_textures#1|all_textures]], is an unfiltered anonymous view — the full texture catalog, subscribed in the base wave for `CatalogComponent` (doc 03).

### The client renderer: TerrainComponent

```sync
![[00 End-to-End Timeline Flowchart#^terrain-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^terrain-4{seamless:true,title:false,marker:04.}]]
```

[[client/Scripts/Components/Terrain/TerrainComponent.cs|TerrainComponent]] is declared **inline in `game.tscn`** (a `Node2D` at `z_index = -10`, so terrain always draws under entities) with three child binders — `MapConfigBinder` (`MapConfig`), `TerrainTilesBinder` (`NearbyTerrainTiles`), `HexDecorBinder` (`NearbyHexDecor`), all `ReplayExistingRows = true`, signals wired to its `On*` handlers in the same scene file. (Its doc comment still says the binders are "declared in terrain_component.tscn" — stale; see Known gaps.) Insert/update/delete handlers only maintain two dictionaries — `_tilesById` + `_tilesByHex` (both needed: deletes arrive by id, rendering groups by hex) and `_decorById` — and set `_dirty`. Nothing rebuilds on the callback itself.

The rebuild runs in [[client/Scripts/Components/Terrain/TerrainComponent.cs#_Process#1|_Process]] when the camera moved/zoomed **or** the dirty flag is set — one [[client/Scripts/Components/Terrain/TerrainComponent.cs#RebuildVisibleInstances#1|RebuildVisibleInstances]] per frame at most, *because* a chunk crossing delivers hundreds of row events in one burst and rebuilding per event would redo the same work hundreds of times. The pass itself:

1. **Wrap.** Every cached hex center is canonical (stored at its raw, possibly far-away position), so it's mapped through `TorusMath.NearestCandidate` against the camera position using the lap vectors `TableSubscriber` mirrored from `MapConfig` (doc 06) — the same hex can need drawing at a lap-shifted copy when the camera sits near a seam.
2. **Cull.** Camera window (viewport size ÷ zoom) plus a two-hex-radius margin; anything outside contributes nothing this pass. The loop deliberately iterates the whole cache rather than doing a spatial lookup, *because* you can't cull by canonical position on a torus — the nearest copy must be computed first.
3. **Batch.** Surviving wedges group per chunk per `(TriIndex, Rotation, TextureId)` key (overlays separately, carrying their per-instance `OverlayScale`); decor groups per chunk per texture, split into shadow and prop lists.
4. **Hand off.** Each present chunk's four batch dictionaries go to `AcquireTile(chunk).Populate(...)`; active tiles whose chunk vanished are zeroed via `Clear()` and parked back in `_tilePool`.

The pool is pre-warmed in [[client/Scripts/Components/Terrain/TerrainComponent.cs#OnEntityReady#1|OnEntityReady]] to `RingChunkCount(AoiChunkRadius)` = `1 + 3R(R+1)` = 19 tiles at the exported radius 2 (mirroring the server's `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`), so the first subscription burst — the largest single payload of rows the client ever receives — doesn't hitch on 19 scene instantiations at once; [[client/Scripts/Components/Terrain/TerrainComponent.cs#AcquireTile#1|AcquireTile]] still grows the pool on demand if more chunks are ever visible. Batching *per chunk* rather than globally duplicates a MultiMesh leaf when two chunks share a batch key — accepted deliberately (biome coherence keeps per-chunk key counts small), and the component's own comment records the fallback: `Populate`/`Clear` take whole grouped dictionaries with no per-chunk assumptions, so one AOI-wide `TileComponent` would only change the grouping loop.

### TileComponent and the MultiMesh layers

```sync
![[00 End-to-End Timeline Flowchart#^terrain-6{seamless:true,title:false,marker:06.}]]
```

Each pooled [[client/Scripts/Components/Terrain/TileComponent.cs|TileComponent]] (instantiated in code from `Scenes/Components/Terrain/tile_component.tscn` — data-driven count, so code-instantiated by the project's own convention) is a tiny `IEntity` whose scene declares exactly four layer children: `GroundComponent` (z=0), `OverlayComponent` (z=1), `DecorShadowComponent` (z=2), `DecorComponent` (z=3). `Populate` just forwards each batch dictionary to its layer's `SetInstances`; the layers are where the actual rendering design lives.

The rendering primitive is the `MultiMeshInstance2D` — Godot's "draw the same mesh N times with different transforms in one draw call" node, which is how ~100 wedges per chunk cost a handful of draw calls instead of hundreds. [[client/Scripts/Components/Terrain/TerrainLayerComponent.cs|TerrainLayerComponent]] (the abstract base) pools one MultiMesh **leaf per batch key** (`GetOrCreateLeaf`), and `FinishPass` zeroes leaves that had no instances this pass instead of freeing them — chunk crossings then *reuse* leaves rather than reallocating every pass. `ClearLeaves` is the full teardown, called only from `ClearMeshes` when the hex radius changes. A leaf with no resolved texture modulates a fallback green instead of crashing — a missing texture id renders as obvious green wedges.

The four layers differ only in transform math:

- [[client/Scripts/Components/Terrain/GroundComponent.cs|GroundComponent]] — plain hex-center transforms, one leaf per wedge batch key.
- [[client/Scripts/Components/Terrain/OverlayComponent.cs|OverlayComponent]] — same batch keys, but each instance is scaled about its wedge's own **centroid** (`position − scale × centroid`) rather than the hex-center local origin the mesh is built around — scaling about the hex center would shrink a small overlay toward a corner instead of keeping it centered in its triangle.
- [[client/Scripts/Components/Terrain/DecorLayerComponent.cs|DecorLayerComponent]] (shared base of the two decor layers, which are themselves empty subclasses differing only in declared z-index) — one leaf per texture, instances sorted by hex row (`HexR`) so nearer (larger world-Y) decor draws after and covers farther decor *of the same texture*. Cross-texture decor sorting (tree vs. rock) is explicitly out of scope in the code — it needs a shared atlas.

Meshes and textures are shared across all chunks from `TerrainComponent`'s caches, *because* wedge geometry depends only on the batch key, never on which chunk draws it. The wedge mesh builder ([[client/Scripts/Components/Terrain/TerrainComponent.cs#GetOrBuildMesh#1|GetOrBuildMesh]]) does two non-obvious things. First, it fits the texture to the **wedge's own bounding box** rather than the hex's — a triangle only has three UV points to a square image's four corners, so at most half the image is addressable per wedge, and each hex shows that crop repeated/rotated six times; the code comment records this as an intentional art call (more texture per wedge over hex-wide continuity). Second, the row's `rotation` (0/1/2) spins the UV crop by 120° steps, so six wedges sharing one texture still look varied. And *because* `AtlasTexture`'s region crop isn't honored by raw mesh UVs on the MultiMesh path, [[client/Scripts/Components/Terrain/TerrainComponent.cs#ResolveTexture#1|ResolveTexture]] unwraps atlases and bakes the region into the UVs itself, binding the underlying sheet. The decor mesh ([[client/Scripts/Components/Terrain/TerrainComponent.cs#GetOrBuildDecorMesh#1|GetOrBuildDecorMesh]]) is a plain rectangle anchored **bottom-center** — the prop's feet — so a tall tree doesn't appear to float above the hex it's planted at. Wedge vertex geometry is [[client/Scripts/Components/Terrain/TerrainComponent.cs#GetWedgeVertices#1|GetWedgeVertices]]: the two rim vertices at angles `30° + 60°·triIndex` and `30° + 60°·(triIndex+1)`, with the hex center as the implicit third vertex — the same `30+60·N` convention the server's `cross_hex_neighbor` comment cites.

`OnMapConfigRow` → [[client/Scripts/Components/Terrain/TerrainComponent.cs#InvalidateMeshes#1|InvalidateMeshes]] exists for the dev path where chunks are re-added at a new radius: it wipes both mesh caches and every pooled tile's leaves, then rebuilds — cheaper than patching stale MultiMesh state. Texture lookup goes through `GameManager.GetResPath(textureId)` — the `CatalogComponent` cache of the `TextureEntry` table — which is the only join between terrain rows and actual image files.

### Buildings: the only writable world rows

```sync
![[00 End-to-End Timeline Flowchart#^terrain-8{seamless:true,title:false,marker:08.}]]
```

The `BuildingTile` grid from `internal_add_chunks` is the one part of the world players can mutate: [[server/spacetimedb/src/world/reducers.rs#place_building#1|place_building]] finds the hex's pre-existing row, refuses an occupied tile, and stamps `building_type` + `owner_id`; [[server/spacetimedb/src/world/reducers.rs#remove_building#1|remove_building]] clears them, gated on ownership-or-admin (a no-op on an already-empty tile). Note `place_building` guards only `require_logged_in`, not `require_in_world` — a lobby-seated player could technically call it from the CLI. Both reducers work *because* the rows always exist — placement is an update, never an insert, so there is no orphan/cleanup path.

**Aspirational boundary:** base-building beyond this reducer pair (construction costs, building behaviors, territory) and biomes-as-gameplay (biome effects on combat/movement — `BiomeRegion` today feeds only enemy spawning) are design-doc systems with no code; they stay out of scope here deliberately.

## Known gaps / stubs

- **Stale comments in `world/hex.rs`.** The `spiral_chunk_index` comment points at a `hex_grid_overlay.gd`, and the `same_hex_neighbor_indices`/`cross_hex_neighbor` comments cite a `TerrainManager.cs` — both are pre-refactor names that no longer exist. The client is C#-only; the live counterparts are `HexGridOverlayComponent.cs`/`HexGridOverlay3DComponent.cs` and `TerrainComponent.cs` (the old `TerrainManager` class was replaced by `TerrainComponent`).
- **Stale doc comment in `TerrainComponent.cs`.** The class summary says its binders are "declared in terrain_component.tscn" — but `Scenes/Components/Terrain/terrain_component.tscn` is one of the seven unreferenced duplicate scenes; the live wiring is inline in `game.tscn` (cited above). Same stale-reference pattern in the binder field comments.
- **Leftover debug prints.** `GD.Print` DEBUG lines in `TerrainComponent._Ready` (binder dump) and `OnTileRowInserted` ("first tile row received") — development noise left in the shipped path.
- **The seeded world uses none of the overlay/decor machinery.** All three seeded `BiomeDef`s ship `overlay_textures: vec![]` and `decor_configs: vec![]` ([[server/spacetimedb/src/main/seeds.rs#seed_world_defs#1|seed_world_defs]]), and [[server/spacetimedb/src/main/seeds.rs#seed_adjacency_rules#1|seed_adjacency_rules]] inserts zero rows — so the live "Earth" generates ground wedges only: no overlay is ever rolled, no `HexDecor` row ever exists, and the Tree/Rock/FullStones textures, the layering rules, and the decor-ground rules are seeded content with nothing driving them. The machinery is exercised only if an admin upserts a richer `BiomeDef` ([[server/spacetimedb/src/main/admin.rs#upsert_biome_def#1|upsert_biome_def]]) and re-runs `generate_world_proc`. Relatedly, `BIOME_VORONOI_SEED_BUDGET` applies to biome seeds only — `region_weights` currently gate validation, not area share (the constant's own comment says so).
- **`place_building`/`remove_building` have no client caller, and nothing renders `BuildingTile`.** No UI calls the reducers (CLI only), and no client component subscribes to or draws the building rows — the building system is server-complete but client-absent.
- **Minor comment/check mismatch.** `main/global.rs` says `DEFAULT_CHUNK_ROWS` "must evenly divide DEFAULT_CHUNK_COLS", but `internal_add_chunks` checks the converse (`chunk_rows % chunk_cols == 0`, message: "chunk_cols must cleanly divide into chunk_rows"). Both hold at the shipped 6×6, so it's cosmetic.

## Where to go next

The `BiomeRegion` rows generated here are the spawn regions the 2-second enemy tick walks — continue to [[08 Enemies & AI]] for how regions become enemies. The camera-window culling and the debug hex-grid overlays that re-derive this doc's hex math are in [[11 Camera & Presentation]]. And the chunk-crossing mechanics that trigger every terrain re-stream are owned by [[06 Movement & Position Sync]].
