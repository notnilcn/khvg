# 02 World & Hex Grid

## Assumed knowledge

- [[00 Roadmap]] for how these docs are structured and the conventions used below.
- [[01 Architecture & Sync Model]] for the tables/reducers/views vocabulary and the chunk-based area-of-interest (AOI) idea in general — this doc reuses that mechanism rather than re-explaining it, and covers server bootstrap (step 2) at the point where it first touches this system's code.
- [[01 Executive Summary|Game Design Docs]] if you want the *what/why* — the torus-shaped world and its lore are covered there (and in `04 Lore.md`'s waypoint lore); this doc is only concerned with the *how*.

## The 30-second version

The world is one giant torus (a rectangle that wraps top-to-bottom and left-to-right) tiled with hexagons, using the [hexx](https://docs.rs/hexx) crate's axial coordinate math on both server and client. Hexes are grouped into **chunks** — hexagons made of hexagons, via the same "hex of hexes" trick `hexx` calls resolution conversion — and every chunk gets a single bijective `i64` index via a hex-spiral numbering, which is what tables key on instead of raw `(q, r)` pairs. The whole map is procedurally generated once, at server boot (`init`, step 2): a wrapped Voronoi diagram assigns each hex a biome and then a region within that biome, and each hex's 6 triangular wedges independently roll a ground texture, an optional overlay, and (per-hex) an optional decor prop, all from data-driven weight tables with tag-based adjacency rules to keep the result from looking like static. None of this is sent to every client — clients only ever see the ring of chunks around their own current chunk, an AOI subscription that recomputes on chunk crossings, not on every movement tick. The client renders that stream as batched wedge meshes (`TerrainManager`), reconciling the torus wrap so tiles near a seam still look continuous.

## System flowchart

Ordering below is a scoped excerpt of [[99 End-to-End Timeline Flowchart]] — the world-generation step from server bootstrap, then the world-facing half of "joining the world" and "movement and AOI" (the player-input and profile/login halves of those same steps belong to the future player-system doc and aren't embedded here).

```sync
![[99 End-to-End Timeline Flowchart#^boot-2{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-1{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-2{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-3{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-3{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-4{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-5{seamless:true,title:false,marker:08.}]]
```

## Hex coordinates and the torus wrap

Both server and client use **pointy-top axial coordinates** `(q, r)` — `hexx::Hex` on the server, a hand-ported equivalent on the client (`HexGridOverlay2D`/`HexGridOverlay3D`'s own `WorldToHex`/`HexToWorld`, kept in sync with the server by hand rather than shared code, since one side is Rust/WASM and the other is C#). `world_to_hex`/`hex_to_world` convert between that and continuous world-space (x, y) via a `HexLayout` scaled by `hex_outer_radius`. See [[methods.rs##pub fn world_to_hex|world_to_hex]] / [[methods.rs##pub fn hex_to_world|hex_to_world]].

A **chunk** is itself a hexagon of hexes, `chunk_hex_radius` rings wide — `hexx`'s `to_lower_res`/`to_higher_res` (wrapped as `world_to_chunk`/`chunk_center_hex`) convert between a fine hex and the chunk that contains it and back. Chunks are then given a single bijective `i64` index via `spiral_chunk_index`, a closed-form hex-spiral numbering (no ring-walking loop) that both `world/methods.rs` and the client's `HexGridOverlay*.cs` implement independently — tables key on this index (`chunk_index` columns on `BuildingTile`, `TriangleTile`, `HexDecor`, `PlayerPosition`) rather than on `(chunk_q, chunk_r)` pairs. See [[methods.rs##pub fn spiral_chunk_index|spiral_chunk_index]].

The map is finite (`chunk_cols` × `chunk_rows` chunks) but wraps on both axes — walking off the east edge reappears on the west edge, same for north/south. `wrap_chunk_coords` folds a chunk coordinate back into `[0, chunk_cols) × [0, chunk_rows)` with `rem_euclid`; `wrap_world_pos` does the continuous-space equivalent, adding whole "lap" offsets (the world-space displacement of one full traversal of the grid) whenever a position's chunk wrapped. Those lap vectors are precomputed once and exposed on `MapConfig` (`lap_q_x/y`, `lap_r_x/y`) specifically so clients don't have to re-derive the hex-of-hexes math just to pick the right wrapped copy of a distant tile. See [[methods.rs##pub fn wrap_world_pos|wrap_world_pos]] and [[tables.rs##pub struct MapConfig|MapConfig]].

Anything that compares a *stored* (already-wrapped) position against one computed fresh from a center that hasn't been wrapped — e.g. a region's spawn radius, or a decor prop near a seam — has to check all 9 lap-shift combinations to find the true nearest copy, since a plain Euclidean distance can silently never match across a seam. `wrapped_distance_sq` does this server-side; the client's equivalent is `TorusMath.NearestCandidate`, used by `TerrainManager` to decide which copy of a hex to actually draw. See [[methods.rs##pub fn wrapped_distance_sq|wrapped_distance_sq]] and [[TorusMath.cs##public static Vector2 NearestCandidate|NearestCandidate]].

## Chunks and the starting grid

`add_chunks` (admin-only, called once by `init` at boot as `internal_add_chunks` — see step 2) lays out the chunk grid: for every `(chunk_q, chunk_r)` pair it inserts one `BuildingTile` row per hex in that chunk, initially unoccupied (`building_type: ""`). This is what `place_building`/`remove_building` later look up and claim — a building is just a `BuildingTile` row whose `building_type`/`owner_id` got filled in, gated on the caller owning the tile or being admin. See [[admin.rs##pub fn add_chunks|add_chunks]] and [[reducers.rs##pub fn place_building|place_building]]. `clear_chunks` deletes every `BuildingTile` (also admin-only), for resetting the grid without a full database wipe.

Default values, set in `main/global.rs` and used unless an admin calls `add_chunks` with different arguments: `chunk_hex_radius = 2` (19 hexes per chunk), a `6×6` chunk grid (`chunk_cols`/`chunk_rows`), and `hex_outer_radius = 48.0` world units per hex.

## Procedural world generation

`internal_generate_world_proc(world_id, seed)` — called by `init` at boot with `("Earth", 0)`, and re-runnable by an admin via `generate_world_proc` — is what actually paints the grid `add_chunks` laid out. It's deterministic: every random-looking decision derives from `(seed, a monotonic draw counter)` through `splitmix64`, so the same `(world_id, seed)` pair always reproduces the same map. See [[methods.rs##pub fn internal_generate_world_proc|internal_generate_world_proc]].

1. **Biome assignment.** A `WorldDef` (e.g. `"Earth"`, seeded with `Grassland 40% / Meadow 30% / Highlands 30%`) lists biomes by weight. Each biome gets a share of a fixed seed-point budget (`BIOME_VORONOI_SEED_BUDGET = 16`) proportional to its weight — every hex in the world is then assigned to whichever biome's seed point is *nearest*, torus-wrap-aware (`nearest_seed_index`, using `wrapped_distance_sq` so a seed near a grid seam doesn't get shortchanged). This is a Voronoi diagram, just computed by brute-force nearest-seed rather than an explicit diagram structure.
2. **Region assignment.** The same nearest-seed trick runs again *within* each biome's cells, this time against that `BiomeDef`'s own `region_weights` — each region gets a `BiomeRegion` row (center hex, spawn radius, `enemy_template_ids`, `max_enemies` — the enemy-spawning system's hook into world geography, out of scope here beyond noting the table lives in `world/`, not `enemy/`).
3. **Per-wedge ground + overlay.** Every hex has 6 triangular wedges (`TriangleTile.tri_index` 0–5); each independently weighted-picks a ground texture from its biome's `ground_textures`, then — if any `overlay_textures` are tag-compatible with that ground pick via `LayeringRule` — maybe an overlay on top. Picks are noise-driven when `ground_noise_scale`/`overlay_noise_scale` > 0 (`value_noise2d`, a bilinear-interpolated lattice hash) so textures cluster into patches instead of scattering per-wedge; a conflicting pick (checked against `BaseAdjacencyRule`/`OverlayAdjacencyRule` for the wedge's same-hex and cross-hex neighbors) gets re-rolled plain-random among the remaining candidates until one clears, or the wedge is left blank. See [[methods.rs##pub fn value_noise2d|value_noise2d]] and [[instance_tables.rs##pub struct TriangleTile|TriangleTile]].
4. **Decor.** Once a hex's own 6 wedges are decided, `place_decor` optionally adds one `HexDecor` row (tree, rock, ...) — gated on `DecorGroundRule` tag conflicts against that hex's own ground/overlay tags, a minimum-separation check against already-placed decor within `decor_min_separation` hexes, and its own noise-clustered weighted pick. At most one decor per hex; separation rules make "no row" the normal case, not a placeholder. See [[instance_tables.rs##pub struct HexDecor|HexDecor]].

`internal_generate_world_manual` is the admin-authoring counterpart: instead of `WorldDef`/`BiomeDef` weight tables, it takes an explicit list of biome/region centers and configs directly as reducer arguments, for hand-placing a specific layout rather than rolling one. Both entry points call the same `write_triangle_tiles`/`place_decor` helpers underneath. See [[admin.rs##pub fn generate_world_manual|generate_world_manual]].

## Streaming world data to clients (AOI)

`TriangleTile` and `HexDecor` are `public` tables, but no client subscribes to them directly — both are read through **views** (`nearby_terrain_tiles`, `nearby_hex_decor`) that filter down to the caller's own area of interest, the same pattern [[01 Architecture & Sync Model]] describes generally for `nearby_enemies`/`nearby_remote_players`/`nearby_loot_drops`. The filter is a `chunk_index` membership test against `nearby_indices_from_chunk`, which resolves the caller's `PlayerChunk` row and returns the ring of chunk indices within `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS` (2) rings of it. See [[views.rs##fn nearby_terrain_tiles|nearby_terrain_tiles]].

`PlayerChunk` — not `PlayerPosition` — is what these views key off, and it's written only on an actual chunk crossing (steps 13–14), not on every 10Hz `report_movement` call. That's the whole point of splitting it out from `PlayerPosition`: a naive AOI view keyed on raw position would recompute its chunk-ring on every movement tick; keying on `PlayerChunk` means the expensive part (`surrounding_chunk_indices`) only reruns when the player actually moves into a new chunk (step 15). See [[methods.rs##pub fn surrounding_chunk_indices|surrounding_chunk_indices]].

## Client rendering

`TerrainManager` (2D-only — there is no 3D terrain renderer; see [[07 Client Rendering & Camera]] once it exists for why 3D stays limited to the player's own model) subscribes to `NearbyTerrainTiles`/`NearbyHexDecor`/`MapConfig` and renders the stream as batched geometry rather than one node per tile:

- Each wedge is its own tiny triangle mesh, UV-cropped to *that wedge's own* bounding box (not the whole hex) so more of the source texture is visible per wedge, at the cost of the same crop repeating 6× rotated around a hex instead of one continuous image — a deliberate trade-off, not a bug. `rotation`/`overlay_rotation` (0/1/2 = 0°/120°/240°) pick which third of the texture lands in a given wedge, so a handful of source textures still read as varied. See [[TerrainManager.cs##private ArrayMesh GetOrBuildMesh|GetOrBuildMesh]].
- All wedges sharing a `(tri_index, rotation, texture_id)` key are drawn as one `MultiMeshInstance2D` batch — insert/update/delete callbacks only flip a `_dirty` flag, and the actual regroup-and-rebuild happens at most once per frame regardless of how many rows one chunk crossing touched at once (step 16).
- Decor is a separate, simpler quad (anchored at its bottom-center "feet" so rotation swings a tall prop around its ground-contact point) with its own texture-keyed batches and a paired shadow sprite baked at a fixed relative offset rather than derived by naming convention.
- Since a stored hex position is its one canonical (possibly far-from-camera) representative on the torus, every draw call resolves `TorusMath.NearestCandidate` against `GameManager`'s `LapQ`/`LapR` (sourced from `MapConfig`) before culling against the camera's view rect — otherwise tiles near a seam would vanish or double up depending on which side of the seam the camera happened to be on.

`HexGridOverlay2D` is a separate, currently-hidden (`visible = false` in `main.tscn`) debug overlay: it subscribes to `MapConfig` independently of `TerrainManager` and draws the chunk grid as flat-colored polygons (3-coloring by `(chunk_q - chunk_r) mod 3`, so no two adjacent chunks share a color) with hex/chunk/spiral-index labels, plus an optional HUD showing the local player's own hex/chunk under the cursor. See [[HexGridOverlay2D.cs##public override void _Draw|_Draw]].

## Known gaps and stubs

- **`HexGridOverlay3D` doesn't render real terrain.** It's wired into `world_3d.tscn` (unlike `HexGridOverlay2D`, it isn't hidden) and reads `MapConfig` for scale, but it populates its hex ring with fixed placeholder `PackedScene`s (`PurpleTileScene`/`PinkTileScene`/`GreyTileScene`, colored by `(chunk_q*2 + chunk_r) mod 3`) rather than subscribing to `NearbyTerrainTiles`/`NearbyHexDecor` — it's a 3D grid-visualization prototype, not a second renderer for the actual biome/texture data `TerrainManager` draws in 2D. See [[HexGridOverlay3D.cs##private void RefreshTiles|RefreshTiles]].
- **Buildings have no client consumer.** `BuildingTile`, `place_building`, and `remove_building` all exist and work server-side (bindings are generated), but no client script subscribes to `BuildingTile` or calls either reducer — there's no UI path to place a building today. This is a stub, not a design decision to build around.

## Where to go next

[[03 Player System]] once it exists picks up the other half of "joining the world" and "movement and AOI" — profile scaffolding's non-world fields, `report_movement`'s input/feel side, and the `PlayerChunk`/AOI mechanism's other consumers (`nearby_enemies`, `nearby_remote_players`, `nearby_loot_drops`). [[05 Enemy AI & Bullet Patterns]] is the other immediate branch off this doc's `BiomeRegion` table, which is where enemy spawning gets its geography.
