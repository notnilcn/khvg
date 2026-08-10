# 07 Terrain & World Streaming

## Assumed knowledge

- [[06 Movement & Position Sync]] — the torus world and its lap vectors (`MapConfig` → `LapQ`/`LapR`, conn-4), `TorusMath.NearestCandidate`, `spiral_chunk_index`, and the two-clock `PlayerPosition`/`PlayerChunk` split that makes AOI views cheap (move-3, move-5).
- [[05 Joining the World]] — the game subscription wave (join-3) that carries `NearbyTerrainTiles`/`NearbyHexDecor`/`MapConfig` to the client.
- [[03 Boot & Connection]] — publish-time `init`: `internal_add_chunks` lays down the chunk grid and `MapConfig` (boot-5), then `internal_generate_world_proc("Earth", 0)` generates the terrain (boot-6).
- [[02 The Component Framework]] — how `TableBinderComponent` re-emits subscribed rows as Godot signals (`LastRow`, `ReplayExistingRows`), and what an entity/component pair is (`TileComponent` implements `IEntity` directly, like `LocalPlayer`).
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `terrain` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/` (the server one has the hex-grid background reading list).

## The 30-second version

The world is a 6×6 grid of hex chunks (each chunk 19 hexes, each hex 6 triangle wedges) generated **once at server publish time** by a deterministic pipeline: biome seeds are scattered proportionally to the seeded "Earth" `WorldDef`'s weights, every hex joins its nearest seed (a wrap-aware Voronoi partition), regions are drawn per biome, and then each wedge gets a ground texture and maybe an overlay, and each hex maybe a decor prop — all driven by one shared SplitMix64 random stream, one spatial noise field per layer, and four tag-rule tables that veto incompatible neighbors. The output is plain rows: `TriangleTile` (6 per hex) and `HexDecor` (at most 1 per hex). Clients never see the whole world: the AOI views `NearbyTerrainTiles`/`NearbyHexDecor` filter those rows to the 2 chunk rings around the caller's `PlayerChunk`, so terrain streams in and out as you cross chunk boundaries. On the client, `TerrainComponent` pools one `TileComponent` scene instance per visible chunk and rebuilds once per frame, batching wedges into `MultiMeshInstance2D` leaves keyed by `(TriIndex, Rotation, TextureId)` — so an entire chunk of ground renders as a handful of draw calls.

## Flowcharts

- [[flowcharts/main-terrain.canvas]] — the composed terrain flow (client terrain components and scenes, the server's `world` module and its `terrain` pipeline).
![[flowcharts/main-terrain.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Terrain_subfolder/Terrain_subfolder.canvas]] — deep dive: all eight terrain component scripts.
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/Components_subfolder/Terrain_subfolder/Terrain_subfolder.canvas]] — deep dive: `tile_component.tscn`, the per-chunk scene the pool instantiates.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/terrain_subfolder/terrain_subfolder.canvas]] — deep dive: the generation pipeline (`mod`, `voronoi`, `ground`, `overlay`, `decor`, `rules`).

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

```sync
![[00 End-to-End Timeline Flowchart#^terrain-9{seamless:true,title:false,marker:09.}]]
```

## Main body

### Generation is publish-time, deterministic, and replace-in-place

```sync
![[00 End-to-End Timeline Flowchart#^terrain-1{seamless:true,title:false,marker:01.}]]
```

Terrain is **content, not simulation**: nothing regenerates, grows, or erodes at runtime. The only in-game mutations of world geometry after boot-6 are an admin re-running [[server/spacetimedb/src/main/admin.rs##pub fn generate_world_proc|generate_world_proc]]/[[server/spacetimedb/src/main/admin.rs##pub fn generate_world_manual|generate_world_manual]] (both just gate the `internal_*` functions behind `is_admin`) and building placement (terrain-9). That is why the pipeline can afford to be a brute-force full-world rewrite — [[server/spacetimedb/src/world/terrain/mod.rs##fn clear_world_geometry|clear_world_geometry]] deletes all ~4104 `TriangleTile` rows and every region/decor row, then the passes reinsert them — and why subscribers see a regen as an ordinary storm of row deletes and inserts.

Determinism is a deliberate, load-bearing property, and it rests on three mechanisms. First, the PRNG is [[server/spacetimedb/src/world/prng.rs##pub fn splitmix64|splitmix64]] — a tiny hash-based generator where randomness comes from hashing `(seed + draw)`, with a single `draw` counter threaded through the whole pipeline ([[server/spacetimedb/src/world/prng.rs##pub fn next_unit|next_unit]] increments it per roll). Same `(world_id, seed)` ⇒ same draw sequence ⇒ same world, with no RNG state to serialize. Second, iteration order is pinned: [[server/spacetimedb/src/world/terrain/voronoi.rs##pub fn combine_biome_weights|combine_biome_weights]] deliberately uses an order-preserving Vec rather than a HashMap, because a different biome order would feed the shared draw counter in a different order and silently reshuffle the world for the same seed. Third, skipped content must not consume draws — a `RegionWeight` whose `BiomeRegionDef` is missing is skipped *without* a draw (terrain-3), so deleting a def doesn't cascade into a different layout for everything after it. The manual front-end opts out of reproducibility on purpose: [[server/spacetimedb/src/world/terrain/mod.rs##pub fn internal_generate_world_manual|internal_generate_world_manual]] seeds from `ctx.timestamp`, since hand-authored inputs are the reproducibility.

Both front-ends hard-require the boot-5 `MapConfig` row ("call add_chunks first"), because [[server/spacetimedb/src/world/hex.rs##pub fn enumerate_world_hexes|enumerate_world_hexes]] derives the entire cell list from it — for each chunk it computes the center hex via `chunk_center_hex` and walks the chunk's 19 hexes in "hexmod" order via [[server/spacetimedb/src/world/hex.rs##pub fn inv_hexmod|inv_hexmod]], precomputing each hex's world position and spiral `chunk_index` so the later passes never redo hex math.

### Weighted Voronoi on a torus

```sync
![[00 End-to-End Timeline Flowchart#^terrain-2{seamless:true,title:false,marker:02.}]]
```

A Voronoi partition is the simplest organic-looking way to carve a map: scatter seed points, give every location to its nearest seed, and you get contiguous blobs with irregular borders — no two runs alike, no hand-painting. The weighted twist is that "40% Grassland" can't be enforced by area directly, so [[server/spacetimedb/src/world/terrain/voronoi.rs##pub fn distribute_biome_seeds|distribute_biome_seeds]] buys area *statistically*: 40% of the 16-seed budget ≈ 6–7 seeds, and randomly placed seeds each claim roughly equal area, so the biome ends up with roughly 40% of the map. The `.max(1.0)` floor means a 1% biome still gets one seed — a rounding artifact can't erase a biome entirely.

The wrap-awareness of [[server/spacetimedb/src/world/terrain/voronoi.rs##pub fn nearest_seed_index|nearest_seed_index]] is not optional polish. On a torus (doc 06), a plain Euclidean nearest-seed check measures a seed near the east edge as far from cells just across the west seam, so it would cede those cells to a competitor and distort every biome's share near seams. [[server/spacetimedb/src/world/wrap.rs##pub fn wrapped_distance_sq|wrapped_distance_sq]] checks all 9 lap-shifted copies, exactly the server's mirror of the client's `NearestCandidate`. Note the same 9-copy trick reappears client-side in terrain-8's culling — every position comparison in this game pays the torus tax somewhere.

```sync
![[00 End-to-End Timeline Flowchart#^terrain-3{seamless:true,title:false,marker:03.}]]
```

Regions exist for the enemy sim, not for rendering — no client subscription carries `BiomeRegion` rows; the server reads them when spawning enemies ([[08 Enemies & AI]]). Their `TriangleTile.biome_id`/`region_id` fields are thus write-only provenance today: the client renderer never reads them. The seeded content ([[server/spacetimedb/src/main/seeds.rs##pub fn seed_world_defs|seed_world_defs]]) is three biomes differing only in ground-texture frequencies (Grassland 75/20/5 Grass/Flowers/HalfStones, Meadow flower-dominant, Highlands stone-heavy) over nine regions from [[server/spacetimedb/src/main/seeds.rs##fn seed_region_def|seed_region_def]], all sharing the same Enemy/Archer spawn pool — variety is a texture-frequency knob, not new content types.

### One wedge, three passes

```sync
![[00 End-to-End Timeline Flowchart#^terrain-4{seamless:true,title:false,marker:04.}]]
```

The rules tables encode two opposite philosophies, both stated in their struct comments. [[server/spacetimedb/src/world/tables.rs##pub struct LayeringRule|LayeringRule]] is an **allow-list**: an overlay may sit on a ground texture only if a row pairs one of the overlay's tags with one of the ground's tags — most overlay/base combinations are nonsense, so compatibility is opt-in. [[server/spacetimedb/src/world/tables.rs##pub struct BaseAdjacencyRule|BaseAdjacencyRule]], [[server/spacetimedb/src/world/tables.rs##pub struct OverlayAdjacencyRule|OverlayAdjacencyRule]], and [[server/spacetimedb/src/world/tables.rs##pub struct DecorGroundRule|DecorGroundRule]] are **deny-lists**: only specifically clashing pairs get rows. The two adjacency tables are separate tables rather than one generic one so a tag shared by a ground texture and an overlay can never cross-apply between layers. All checks run in [[server/spacetimedb/src/world/terrain/rules.rs##pub fn decided_neighbor_tags|rules.rs]] against the in-memory `NeighborTagMap` — tags of wedges already written *this run* — because a wedge has three lateral neighbors (two same-hex, one cross-hex via [[server/spacetimedb/src/world/hex.rs##pub fn cross_hex_neighbor|cross_hex_neighbor]]) and only the ones written earlier in the cell order can be consulted. Adjacency is therefore order-dependent by construction: the first wedge picks freely, later wedges route around it.

The clustering trick is worth understanding because it's the whole "noise" story. [[server/spacetimedb/src/world/noise.rs##pub fn value_noise2d|value_noise2d]] is value noise: hash the four lattice corners around the scaled position (the hash is `splitmix64` keyed by integer coordinates, so it's as deterministic as the PRNG), smoothstep-interpolate between them. The output is a smooth [0,1) field — nearby positions get nearby values. Feeding that value into [[server/spacetimedb/src/world/prng.rs##pub fn weighted_pick_index_from_unit|weighted_pick_index_from_unit]] instead of a fresh draw means adjacent hexes fall in the same band of the cumulative weight distribution and pick the *same* texture: patches emerge. Each layer xor's the world seed with its own offset constant ([[server/spacetimedb/src/world/noise.rs##pub const OVERLAY_NOISE_SEED_OFFSET|OVERLAY_NOISE_SEED_OFFSET]], [[server/spacetimedb/src/world/noise.rs##pub const DECOR_NOISE_SEED_OFFSET|DECOR_NOISE_SEED_OFFSET]]) so the three fields are decorrelated — otherwise debris density would visibly trace the ground-texture boundaries. A `noise_scale <= 0` in the biome def disables clustering and falls back to plain per-wedge draws.

```sync
![[00 End-to-End Timeline Flowchart#^terrain-5{seamless:true,title:false,marker:05.}]]
```

Two asymmetries between the passes are easy to miss. Overlay chances are **percentages with a remainder** ([[server/spacetimedb/src/world/prng.rs##pub fn weighted_pick_with_remainder_from_unit|weighted_pick_with_remainder_from_unit]]): 30+20 across the eligible overlays means 50% no-overlay, and because eligibility is filtered by the ground pick first, a ground texture with few compatible overlays is naturally sparser — no per-base tuning knob needed. Ground chances, by contrast, are relative weights that always pick something unless every candidate conflicts (then the wedge gets an empty `texture_id` and renders as the client's fallback color). And decor separation is checked *before* rolling, type-agnostically, against the `decided_decor` set — the comment in [[server/spacetimedb/src/world/terrain/decor.rs##pub(super) fn place_decor|place_decor]] explains why there's no retry loop here: if any prop already sits within `decor_min_separation` hexes, the hex is skipped outright rather than shopping for a different prop that fits.

### Streaming: terrain is just two more AOI views

```sync
![[00 End-to-End Timeline Flowchart#^terrain-6{seamless:true,title:false,marker:06.}]]
```

Everything doc 06 established about AOI applies unchanged — the views even reuse [[server/spacetimedb/src/player/views.rs##pub(crate) fn nearby_indices_from_chunk|nearby_indices_from_chunk]], passing [[server/spacetimedb/src/main/global.rs##pub const DEFAULT_TERRAIN_AOI_CHUNK_RADIUS|DEFAULT_TERRAIN_AOI_CHUNK_RADIUS]] explicitly (a separate constant from `DEFAULT_AOI_CHUNK_RADIUS` so terrain range can be tuned independently of entity range; both are 2 today). The `chunk_index` btree index on [[server/spacetimedb/src/world/instance_tables.rs##pub struct TriangleTile|TriangleTile]]/[[server/spacetimedb/src/world/instance_tables.rs##pub struct HexDecor|HexDecor]] is what the OR-chain matches against — the same spiral id `report_movement` writes into `PlayerChunk`, so "which chunks are near the player" and "which tiles are in those chunks" speak one language. There is no `BiomeRegion` view and no overlay/decor filtering beyond chunk membership: what the wave delivers is exactly what the renderer draws.

Two rows in the wave are not terrain at all but terrain *metadata*. `MapConfig` (a public table, not a view) carries `hex_outer_radius` and the lap vectors — the client can't position a single wedge without them. And `AllTextures` (the anonymous view [[server/spacetimedb/src/world/views.rs##fn all_textures|all_textures]] over [[server/spacetimedb/src/world/tables.rs##pub struct TextureEntry|TextureEntry]], subscribed in the conn-4 base wave) is the id→`res://` path catalog that `TerrainComponent.GetTexture` consults through [[GameManager.cs##public static string? GetResPath(string textureId)|GameManager.GetResPath]] → `CatalogComponent` (its caching is [[10 Inventory, Items & Enchantments]]). A tile row stores only texture *ids*; resolving them to Godot `Texture2D`s is entirely a client-side, catalog-driven affair.

### The client: dirty flags, a tile pool, one rebuild per frame

```sync
![[00 End-to-End Timeline Flowchart#^terrain-7{seamless:true,title:false,marker:07.}]]
```

The wiring lives inline in `main.tscn`: the [[main.tscn##[node name="TerrainComponent" type="Node2D" parent="."|TerrainComponent node]] (z_index −10, under everything) with `TileScene` pointed at `tile_component.tscn`, three child binders, and eight `[connection]` entries at the bottom of the scene wiring `RowInserted`/`RowUpdated`/`RowDeleted` from each binder. (The standalone `terrain_component.tscn` is one of the nine unreferenced duplicate scenes — [[02 The Component Framework]] → Known gaps — and `TerrainComponent`'s own docstring still claims the binders are declared there; see Known gaps.)

The design center is that **row events are cheap and rebuilds are expensive**, so the two are decoupled by the `_dirty` flag. A chunk crossing rewrites the AOI views' membership wholesale — hundreds of insert/delete callbacks arrive within a frame or two — and each one only mutates a dictionary entry. The camera check in `_Process` is the other trigger: panning changes which wrapped copy of which hex is visible without any row events at all, so camera movement alone must schedule a rebuild. The pool pre-warm of `RingChunkCount(2)` = 19 tiles matches the server ring count on purpose — the comment on [[TerrainComponent.cs##public partial class TerrainComponent : Node2DComponent|AoiChunkRadius]] says the pool grows on demand if that's ever wrong, so a mismatch costs a hitch, not a bug. `OnMapConfigRow` is the odd callback out: it changes `_outerRadius`, which invalidates every cached mesh, so it flushes both mesh caches and every pooled leaf via `InvalidateMeshes` and rebuilds immediately rather than waiting for the dirty path.

### Wrap-aware culling and MultiMesh batching

```sync
![[00 End-to-End Timeline Flowchart#^terrain-8{seamless:true,title:false,marker:08.}]]
```

The culling subtlety: `RebuildVisibleInstances` iterates **every cached hex**, not a camera-window lookup. On a torus the camera window means nothing until you know *which copy* of the world you're looking at — a hex stored at canonical (5000, 0) may have a wrapped copy 50 units from the camera. So each hex center is first pulled through `NearestCandidate` with the `MapConfig` lap vectors, and only then tested against the viewport rect (inflated by a two-hex-radius margin so partially visible hexes at the screen edge don't pop). This is move-4/move-6's candidate-picking applied to static geometry.

Batching is where the rendering cost actually lives. Godot's `MultiMeshInstance2D` draws many copies of one mesh in a single draw call, so the renderer's job is to minimize distinct meshes. [[TerrainComponent.cs##private ArrayMesh GetOrBuildMesh|GetOrBuildMesh]] builds one wedge `ArrayMesh` per `(TriIndex, Rotation, TextureId)` key — a triangle from the hex center to the two rim vertices at angles 30°+60°·k (pointy-top axial layout, matching the server's `hexx`-based [[server/spacetimedb/src/world/hex.rs##pub fn hex_to_world|hex_to_world]], which the client's own `HexToWorld` mirrors formula-for-formula). Two non-obvious details in that mesh builder: the texture is fit to *the wedge's own bounding box* and spun by 120°·Rotation, so one texture yields visually varied wedges (the code comment records this as a deliberate art call — more texture per wedge at the cost of hex-wide continuity); and `AtlasTexture` region crops are **not honored** by `MultiMeshInstance2D`'s raw UV path, so [[TerrainComponent.cs##private static (Texture2D? Base, Rect2 UvRegion) ResolveTexture|ResolveTexture]] unwraps the atlas and the crop is baked into the mesh UVs by hand. Decor meshes are plain quads keyed by texture alone, anchored **bottom-center** so a rotated prop pivots at its ground-contact point (its "feet") instead of its middle.

[[TerrainLayerComponent.cs##public abstract partial class TerrainLayerComponent : Node2DComponent|TerrainLayerComponent]] owns the leaf lifecycle for all four layers: `GetOrCreateLeaf` makes one `MultiMeshInstance2D` per batch key, `FinishPass` zeroes (`InstanceCount = 0`) leaves that had no instances this pass rather than freeing them — chunk crossings reuse the leaves, so steady-state streaming allocates nothing. The layers differ only in transforms: [[GroundComponent.cs##public partial class GroundComponent : TerrainLayerComponent|GroundComponent]] stamps wedges at hex centers; [[OverlayComponent.cs##public partial class OverlayComponent : TerrainLayerComponent|OverlayComponent]] scales around the wedge's own centroid ([[TerrainComponent.cs##public Vector2 GetWedgeCentroid|GetWedgeCentroid]]) because the mesh origin is the hex center and scaling about it would slide the overlay into a corner; [[DecorLayerComponent.cs##public abstract partial class DecorLayerComponent : TerrainLayerComponent|DecorLayerComponent]] sorts each texture's instances by hex row (`HexR`) so nearer props draw over farther ones within a batch — cross-texture ordering would require a shared atlas, and its docstring's pointer to `next-steps.md` is dangling (Known gaps). Per-chunk batching does duplicate a leaf when two visible chunks share a batch key; the `TerrainComponent` docstring accepts that (biome coherence keeps per-chunk key counts small) and records the escape hatch — one AOI-wide `TileComponent` — should it regress.

### Buildings: the reducer-only plot layer

```sync
![[00 End-to-End Timeline Flowchart#^terrain-9{seamless:true,title:false,marker:09.}]]
```

`BuildingTile` is deliberately boring: one row per world hex inserted by [[server/spacetimedb/src/main/admin.rs##pub fn internal_add_chunks|internal_add_chunks]] with an empty `building_type`, and occupancy *is* the empty/non-empty string. [[server/spacetimedb/src/world/reducers.rs##pub fn place_building|place_building]] finds the row by the `hex_q` btree index plus an `hex_r` scan, rejects occupied tiles, and stamps the caller's `Identity` as `owner_id`; [[server/spacetimedb/src/world/reducers.rs##pub fn remove_building|remove_building]] enforces owner-or-admin. There is no validation of `building_type` against any catalog — any non-empty string is a building — and no geometry interaction: a building doesn't change the hex's tiles, decor, or walkability. Per the roadmap's scope rule, base-building beyond these two reducers is aspirational and stays out of these docs.

## Known gaps / stubs

- **Stale `.gd` reference in `hex.rs`.** The comment above [[server/spacetimedb/src/world/hex.rs##pub fn spiral_chunk_index|spiral_chunk_index]] says it "Mirrors `_hex_spiral_index` in `hex_grid_overlay.gd`" — that GDScript file no longer exists. The real client duplicates are the C# [[HexGridOverlayComponent.cs##private static long HexSpiralIndex|HexSpiralIndex]] in `HexGridOverlayComponent.cs` and `HexGridOverlay3DComponent.cs`; all three implementations must change together (as server `CLAUDE.md` notes).
- **Stale `TerrainManager.cs` references.** `TerrainManager` was a pre-refactor client class that no longer exists; its successor is `TerrainComponent`. It is still cited by two wedge-geometry comments in `hex.rs` (above [[server/spacetimedb/src/world/hex.rs##pub fn same_hex_neighbor_indices|same_hex_neighbor_indices]] and [[server/spacetimedb/src/world/hex.rs##pub fn cross_hex_neighbor|cross_hex_neighbor]] — the math they describe now lives in `TerrainComponent.GetWedgeVertices`) and by the "TerrainManager rendering" remark in the [[server/spacetimedb/src/main/seeds.rs##pub fn seed_world_defs|seed_world_defs]] comment.
- **Stale `TerrainComponent` docstring.** The [[TerrainComponent.cs##public partial class TerrainComponent : Node2DComponent|TerrainComponent]] summary says its binders are "declared in terrain_component.tscn, signals wired in the editor" — but `terrain_component.tscn` is one of the nine unreferenced duplicate scenes; the live declaration and all eight signal connections are inline in `main.tscn` (terrain-7).
- **Leftover debug prints.** `TerrainComponent._Ready` prints a `DEBUG binders:` line on startup, and [[TerrainComponent.cs##private void OnTileRowInserted|OnTileRowInserted]] prints `DEBUG first tile row received` once per session — dev scaffolding that shipped.
- **Buildings are server-only in practice.** No client script calls `place_building`/`remove_building` (only the generated bindings know the names), and `BuildingTile` appears in no subscription wave ([[TableSubscriber.cs##public static readonly string[] GameTables|GameTables]]), so a building placed via the CLI is stored and ownership-enforced but never rendered anywhere.
- **Overlay and decor machinery is live but generates nothing.** Every seeded biome has empty `overlay_textures` and `decor_configs` ([[server/spacetimedb/src/main/seeds.rs##pub fn seed_world_defs|seed_world_defs]] — its comment parks decor pending 2D collision/walkability decisions and better art), and [[server/spacetimedb/src/main/seeds.rs##pub fn seed_adjacency_rules|seed_adjacency_rules]] seeds zero rows. The layering rules from [[server/spacetimedb/src/main/seeds.rs##pub fn seed_layering_rules|seed_layering_rules]] and decor-ground rules from [[server/spacetimedb/src/main/seeds.rs##pub fn seed_decor_ground_rules|seed_decor_ground_rules]] are seeded but currently have no content to act on. In-game effect: no overlays, no props, no shadows — ground wedges only.
- **Dangling `next-steps.md` reference.** [[DecorLayerComponent.cs##public abstract partial class DecorLayerComponent : TerrainLayerComponent|DecorLayerComponent]]'s docstring defers cross-texture decor sorting to "a shared atlas per next-steps.md", but no such file exists in the repo.
- **Mismatched fallback hex radius.** The client's [[TerrainComponent.cs##private const float DefaultHexOuterRadius|DefaultHexOuterRadius]] fallback is 96 while the server's [[server/spacetimedb/src/main/global.rs##pub const DEFAULT_HEX_OUTER_RADIUS|DEFAULT_HEX_OUTER_RADIUS]] is 48. Harmless today (no tile rows exist before the `MapConfig` row arrives and overwrites it), but the fallback would render a double-scale world if it ever mattered.
- **Dead `hex_shift` plumbing.** [[server/spacetimedb/src/world/hex.rs##pub fn inv_hexmod|inv_hexmod]] takes a `_shift` parameter it ignores, and both `enumerate_world_hexes` and `internal_add_chunks` compute [[server/spacetimedb/src/world/hex.rs##pub fn hex_shift|hex_shift]] solely to feed it — leftover from an earlier hexmod formulation.
- **Region weights don't buy area.** Per the [[server/spacetimedb/src/main/global.rs##pub const BIOME_VORONOI_SEED_BUDGET|BIOME_VORONOI_SEED_BUDGET]] comment, `RegionWeight` values gate only `validate_weights_sum_100` — every resolvable region in a biome gets exactly one seed, so a 25/25/25/25 split and a 90/5/3/2 split produce statistically identical region layouts (terrain-3). Listed here because the def shape implies otherwise.

## Where to go next

The `BiomeRegion` rows this pipeline plants are the enemy sim's spawn districts — [[08 Enemies & AI]] picks up from there. The debug hex-grid overlay that shares the spiral-index math, and the camera whose position drives terrain-8's culling, are [[11 Camera & Presentation]]; texture-id resolution through `CatalogComponent` is [[10 Inventory, Items & Enchantments]].
