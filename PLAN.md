# PLAN — Porting the hex/world tech from `code_examples/` into the game

Status: **decisions locked 2026-09-21** (§0) · Sources: `code_examples/Sota`, `code_examples/godot-hex-map`, `code_examples/hex_map_godot`

This plan covers everything worth taking from the three reference repos and how each piece lands in this project (Godot 4.7 mono C# client + SpacetimeDB 2.2 Rust server, server-authoritative). **Priority 1 is the wrapping work**; everything else is the roadmap in §7. Each source folder has its own `AGENTS.md` with a repo map, so future passes can start there instead of re-reading the trees.

---

## 0. Decisions (locked 2026-09-21)

| # | Question | Decision | Plan impact |
|---|---|---|---|
| 1 | Wrap topology | **Close the remaining seams** in the existing canonical-lap + nearest-copy torus (not grid-level rewiring) | §3 W1–W5 unchanged; no hex-graph restructuring |
| 2 | 3D MST seam | **Fix in Phase 1** | W3 is active work, not conditional |
| 3 | Rivers / roads / walls | **Server-authoritative** — new per-hex masks on `HexTile` + generation in `world/terrain/` | New work package §4.1 (Phase 3) |
| 4 | Fog-of-war / explored | **Per-profile explored map; reveal granularity = the AOI chunk disk** | New work package §4.2 (Phase 4) |
| 5 | Enemy pathfinding | **Add wrap-aware hex A*/Dijkstra** | New work package §4.3 (Phase 4) |
| 6 | Attribution | **`THIRD_PARTY_NOTICES.md` + per-file credit headers** | §6 cross-cutting; Phase 0 task |
| 7 | Sota terrain quality | **Ridge-based height field wanted eventually** | §4.6 (Phase 5), not dropped |
| 8 | Auto-tile rules | **Client-side visual variants** from server-owned types; server owns gameplay-relevant masks only | §4.5 (Phase 3 support) |
| 9 | Backlog order | **Order confirmed** (wrapping → hex library → content → optional systems → Sota) | §7 phasing table |

---

## 1. What the sources are (and aren't)

| Repo | Language / form | License | Snapshot | What it is | What it is NOT |
|---|---|---|---|---|---|
| `Sota` | C++ GDExtension (Godot 4.2+) | MIT (Roman Ardazishvili) | `16f3de6`, 2025-06-29 | Hex/pent meshes, tessellation, ridge-based terrain, honeycomb, polyhedra, DSU, cube coords | No wrapping, no chunking, no networking, no tests |
| `godot-hex-map` | C++ GDExtension (Godot 4.2+) | MIT (David M. Lary; Godot portions also MIT) | `bab38bd`, 2026-03-01 | GridMap redesign for hexes: cell ids, plane-bounded iterators, octant chunks, auto-tile rule engine, editor tooling; doctest + GUT tests | **No wrapping/toroidal code at all**; rendering is Godot-internals-bound |
| `hex_map_godot` | Pure GDScript (Godot 4) | MIT (David Pruitt) | `135dc75`, 2025-01-01 | Catlikecoding's 27-part hex-map tutorials in Godot: terrain/elevation/terraces, rivers, roads, walls, water/estuaries, features, pathfinding, fog/explored, **wrapping**, save/load | No networking; some tutorial bugs |

Porting is **reimplementation in C#/Rust** (all three are C++/GDScript), not code transfer. Only `hex_map_godot`'s GDScript maps mechanically to C#, and shaders can be ported nearly verbatim (with credits per §0.6).

---

## 2. Where main is today (the baseline we're diffing against)

### 2.1 The wrapping model that already exists

Server (`server/spacetimedb/src/world/`):
- `wrap.rs` — `wrap_chunk_coords` (chunk indices via `rem_euclid`), `wrap_world_pos` (continuous-space torus: shifts by integer combinations of the lap vectors), `wrapped_distance_sq` (9 lap-candidate minimum). Note the comment: *"The continuous world is a torus; the hex grid is not."*
- `hex.rs` — `world_to_hex` / `hex_to_world` / `chunk_center_hex` / `world_to_chunk` / `hex_neighbors` / `enumerate_world_hexes` / `spiral_chunk_index`, all **unwrapped** except where callers pre-wrap.
- `terrain/mod.rs` — generation enumerates canonical chunks only (`0..cols × 0..rows`) and is torus-aware only through the Voronoi biome pass (`nearest_seed_index(..., lap_q, lap_r)`, `group_cells_by_seeds`).
- `aoi.rs` — AOI chunk indices wrap correctly.
- `main/admin.rs:282-284` computes `lap_q_*` / `lap_r_*` into `MapConfig`. World size today: **chunk radius 2 (19 hexes/chunk), 6×6 chunks = 36 chunks / 684 hexes, hex outer radius 48** (`main/global.rs:6-14`).

Client (`client/Scripts/`):
- `World/TorusMath.cs` — `NearestCandidate(canonical, reference, lapQ, lapR)` picks the nearest of 9 lap copies (render-time only; never mutates canonical data).
- `World/HexMath.cs` — `HexToWorld`, `WorldToHex`, `ToLowerRes`, `ChunkCenterHex`, `HexToLocal`, `SpiralIndex`; lockstep-commented mirrors of server `hex.rs`.
- `Components/Movement/InterpolationComponent.cs` + `PositionSyncComponent.cs` — torus-aware lerp/dead-reckoning for remote players and local reports.
- `Components/Terrain/TerrainComponent.cs:290-334` — the 2D renderer iterates all cached rows, computes each hex's nearest lap copy to the camera, culls, and batches. **2D terrain is already wrap-correct.**
- `Components/Terrain/TerrainComponent3D.cs:23-27` — **explicit known limitation:** only the canonical lap is built; seam edge chunks read void neighbors (cliff walls at the world edge).
- `Camera/HexGridOverlay3DComponent.cs` — same unwrapped-edge limitation (cosmetic/debug).

### 2.2 Gaps this plan closes (the wrapping work)

- **G1. 3D terrain seam.** The MST built by `TerrainComponent3D` ends at the canonical lap: false cliff walls into void, wrong ledge/transition blending on border hexes, and no terrain visible on the far side of the seam. The 2D renderer solved this with per-hex nearest-copy; 3D has no equivalent.
- **G2. Terrain-generation adjacency at the seam.** `rules.rs:39-44` (`decided_neighbor_tags`) looks up raw `(q,r)` neighbors in `neighbor_tags`, which only ever contains canonical-lap cells. Boundary hexes never see their wrapped neighbors, so shorelines/overlay/decor transitions can cut off at the seam. Voronoi biome assignment is fine (already uses lap vectors); the per-hex passes are not.
- **G3. No canonical hex wrap helper.** Nothing maps an arbitrary hex `(q,r)` back into the canonical lap. Needed for G1/G2 and for any gameplay code that gets a position/hex from the far side of a lap.
- **G4. Node-space vs canonical-space audit.** Several components read *node* positions (which may drift up to a lap from canonical) and then do hex/elevation lookups on them. Candidates to audit: `BulletCliffComponent` (samples `ElevationMap` with bullet global positions), bullet band stamping at spawn, `WaterComponent3D` ring placement, `HexGridOverlay3DComponent` labels, cursor ground intersection. The fix pattern: wrap the position with a client mirror of `wrap_world_pos` before `WorldToHex`/elevation lookups.

### 2.3 Other systems in main that a port would touch

- 2D terrain: `TerrainComponent` + `TileComponent` + 4 layer components, `MultiMeshInstance2D` batches per chunk, per-hex rows from `NearbyTerrainTiles` / `NearbyHexDecor`.
- 3D terrain: tracked fork `client/addons/MarchingSquaresTerrain/` (MST, hex stepped terrain, own `AGENTS.md`); data model = per-chunk `hex_data` (elevation, ground/wall slots, grass, per-edge transition kinds), mesh rebuilt from records, `get_hex_global` resolves neighbor records.
- Water: `WaterComponent3D` (3D ring of ocean tiles around the player) + `BackingOcean`.
- Elevation: server `BuildingTile.elevation` from noise; client `ElevationMapComponent` exposes `GetElevation`/`GetWorldHeightAt`.
- Ability shapes (circle/line/cone/rect) are bespoke code in `player/reducers.rs` + `BulletControllerComponent`.

---

## 3. Priority 1 — the wrapping port

### W1. Canonical hex wrap helper (server + client mirrors)

Add to `server/spacetimedb/src/world/wrap.rs` (or `hex.rs`):

```rust
// Map an arbitrary hex into the canonical lap (chunk coords wrap; local offset preserved).
pub fn wrap_hex_coords(q: i32, r: i32, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32) -> (i32, i32) {
    if chunk_cols <= 0 || chunk_rows <= 0 { return (q, r); }
    let (cq, cr) = to_lower_res(q, r, chunk_hex_radius);
    let (cqw, crw) = wrap_chunk_coords(cq, cr, chunk_cols, chunk_rows);
    if (cq, cr) == (cqw, crw) { return (q, r); }
    let (lcq, lcr) = to_local_in_chunk(q, r, chunk_hex_radius);   // q,r − chunk center
    let (ccq, ccr) = chunk_center_hex(cqw, crw, chunk_hex_radius);
    (ccq + lcq, ccr + lcr)
}
```

Mirror it as `HexMath.WrapHex(q, r, radius, cols, rows)` in `client/Scripts/World/HexMath.cs` and `MSTHexMath.wrap_hex(...)` in the MST fork, following the existing lockstep-comment convention used by `SpiralIndex`/`spiral_chunk_index` (all copies must change together). Also add a `TorusMath.WrapPosition(Vector2, lapQ, lapR)` client mirror of `wrap_world_pos` for G4 (position → canonical before hex lookups).

Properties to assert in review/tests: idempotence; `wrap_hex_coords` preserves the 6-neighborhood (each canonical neighbor maps to the canonical rep of that neighbor); world position of the wrapped hex differs from the original by an integer combination of the lap vectors.

### W2. Seam-aware terrain generation

- In `rules.rs::decided_neighbor_tags`, canonicalize each `hex_neighbors` result with `wrap_hex_coords` before the `neighbor_tags` lookup (add a wrap-aware neighbor helper rather than rewriting `hex_neighbors`; raw-coordinate callers stay valid).
- Semantics note: the per-hex passes are inherently order-dependent (a rule can only consult *already-decided* neighbors). Wrapping makes the seam behave like the interior (the neighbor is found if already written; skipped otherwise) rather than worse than the interior. If order-dependence itself becomes a problem, the upgrade is a two-pass decision (pass 1: ground for all hexes, pass 2: overlay/decor with all neighbors known) — that change would make the whole pipeline order-independent, not just the seam.
- Audit other `hex_neighbors` consumers for the same issue: building placement adjacency, spawn region checks, anything that treats hex adjacency as a graph (e.g. `BiomeRegion` membership), and the Phase 4 pathfinder (§4.3 builds on the wrapped helper).

### W3. Fix the 3D MST seam (two independent changes)

**(a) Wrap-aware neighbor resolution in the fork.** Add storage-only `wrap_chunk_cols` / `wrap_chunk_rows` (0 = off) to `MarchingSquaresTerrain` (set from `MapConfig` by `TerrainComponent3D`), and canonicalize in `get_hex_global(q, r)` (`algorithm/terrain/marching_squares_terrain.gd:788-802`) before the chunk lookup. Everything downstream already funnels through it — `MarchingSquaresTerrainChunk.get_neighbor_hex` (`marching_squares_terrain_chunk.gd:256-264`), border wall/ledge logic, `_regenerate_neighbor_chunks` (`:769-773`), `get_elevation_at_world` — so border hexes read their true wrapped neighbors and the false void cliffs disappear. Follow the fork's own AGENTS.md for style.

**(b) Chunk "apron" copies around the camera.** Canonical MST only covers the canonical lap, while the local player's *node* can drift up to a lap past it (server wraps the stored position; `TorusMath` keeps rendering continuous). Lift the 2D renderer's nearest-copy trick to chunk granularity:
- Maintain a small pool of `MeshInstance3D` mirrors (start with ~8-16). Each frame (or on elevation change), for each canonical chunk within camera range, place mirrors at `chunk.Position + a*lapQ3 + b*lapR3` for `(a,b) in {-1,0,1}² \ {(0,0)}`, nearest-first, until the pool is exhausted.
- Mirrors **share the canonical chunk's `Mesh`** (same trick as `UnderShell` in `TerrainComponent3D.RefreshShells`), so `regenerate_mesh` is free to mirror — just re-assign `mirror.Mesh = chunk.Mesh` in the same pass.
- `lapQ3`/`lapR3` derive from `HexMath.HexToWorld3D(chunk_center_hex(cols,0))` or from `TableSubscriber.LapQ/LapR` converted to `Vector3(x, 0, y)`.
- Collision on mirrors: gameplay does not use MST collision (player is 2D logic, camera `SpringArm3D` mask is pinned to 0), so mirrors can be visual-only initially; if collision is ever needed, mirror the `StaticBody3D` shapes too.
- Do **not** recycle/translate the whole terrain node (`hex_map_godot`'s `center_map` style): player/camera/raycasts and `get_elevation_at_world` are all in global space, so moving the terrain would break them. Mirrors are the safe equivalent.

Rejected hex_map_godot ideas here, with reasons: **noise seam blending** (`HexMetrics.sample_noise`) is unnecessary and wrong for main because terrain is generated once in canonical space and repeated — the pattern is inherently periodic; **camera position wrapping** is unnecessary because the client camera works in unwrapped/drifted space; **grid-level neighbor wiring at creation** is replaced by W1's canonicalization plus the server's already-wrapped gameplay queries.

### W4. Canonical-space audit (G4)

- Add `TorusMath.WrapPosition` (mirror of server `wrap_world_pos`) and use it anywhere a node/entity position is converted to a hex or fed to `ElevationMapComponent`/MST lookups.
- Touch list to verify in playtest: `BulletCliffComponent`, `BulletSpawnerComponent` band stamping, `WaterComponent3D` ring, `LocalPlayer` cursor ground intersection, `HexGridOverlay3DComponent` labels, `CombatComponent` hit-zone elevation stamping.
- Consider a small debug command/button ("teleport to seam") to make this testable without walking a whole lap.

### W5. Verification of the wrapping phase

- `cargo check --manifest-path server/spacetimedb/Cargo.toml`, `dotnet build client/khvg.csproj`.
- Playtest skill: stand at chunk (0,0) and walk across -q / -r seams; screenshot 2D and 3D; confirm no void walls, no texture/overlay cutoff, and that `spacetime sql` shows seam-adjacent hexes with consistent elevations. Also verify the 2D renderer still parks/reuses chunk tiles.
- If we add a Rust unit test, keep it to pure helpers (`wrap_hex_coords` round-trip/neighborhood) — the module has no test harness today.

---

## 4. Confirmed work packages (post-wrapping)

### 4.1 Rivers / roads / walls — Phase 3 (decision §0.3)

**Server (authoritative):**
- Extend `HexTile` with per-edge masks: `river_mask: u8`, `road_mask: u8`, `wall_mask: u8`, one bit per edge in `HEX_NEIGHBOR_DELTAS` order (both hexes on an edge carry the bit — generation side is the invariant). Add `map_config`/seed knobs only if needed.
- Generation passes in `world/terrain/` **after** elevation and ground/overlay (rivers follow elevation downhill; roads connect regions/POIs; walls wrap settlements), each updating rows the way the elevation pass updates `BuildingTile` (`terrain/mod.rs:140-146`). Keep the shared `draw` counter ordering deterministic.
- Port `hex_map_godot`'s generators: `_create_rivers` (with incoming-direction/estuary bookkeeping), road placement, wall placement (`hex_map_generator.gd`, `hex_cell.gd` river/road fields).
- Seam correctness comes free from W1/W2 (masks are per-hex data; adjacency canonicalization decides river continuity across the seam).

**Client (rendering + cosmetics):**
- New rendering component (e.g. `TerrainRoadsComponent3D`) that reads `HexTile` rows and builds per-hex edge quads/strips — either extending MST's transition system or as an independent mesh/MultiMesh layer. Shaders ported nearly verbatim from `hex_map_godot/shaders/{river,road,wall,water,water_shore,estuaries}_shader.gdshader` (credit headers per §0.6).
- Decorative variants (junction caps, bridge look, wall corners) resolved by the client rule resolver in §4.5.
- Gameplay hooks come later: movement cost in the §4.3 pathfinder, and possibly `report_movement` checks (climb cost over roads/rivers) once they exist.

### 4.2 Per-profile explored map — Phase 4 (decision §0.4; reveal granularity = the AOI disk)

**Reveal unit = the terrain AOI disk** (`DEFAULT_TERRAIN_AOI_CHUNK_RADIUS` chunks, computed by `world/aoi.rs::surrounding_chunk_indices` and already used for `nearby_terrain_tiles`). The player reveals a *group of chunks* at a time, never a single hex or a single chunk. Trigger: on every AOI change — the player's chunk crossing, which `report_movement` already detects and writes via `PlayerChunk` — plus once at `join_world`.

**Storage: one row per revealed chunk with a denormalized snapshot.** The reveal operation covers the AOI group; rows stay per chunk so overlapping AOI circles don't duplicate payloads (moving one chunk reveals only the new outer ring).
- `PlayerExploredChunk { profile_id, chunk_index, revealed_at, hex_payload }` where `hex_payload` is the compact per-hex render snapshot for the chunk's 19 hexes (ground id + rotation, overlay id + rotation + scale, elevation), copied from the generated `HexTile`/`BuildingTile` rows when revealed. World generation is static after `internal_generate_world`, so snapshots can never go stale — this is what lets distant explored areas render without widening the `nearby_terrain_tiles` subscription.
- Insert the new AOI's chunks, skipping already-revealed ones (bfind/set-diff over the profile's ≤36 rows); no per-hex tracking, no partial-chunk reveal.
- Views: `local_player_explored` filtered to the caller's profile via the `ctx.from` idiom, subscribed with the game wave.
- Client: a dedicated fog/map layer renders dimmed tiles from the snapshot for explored chunks outside the live AOI; live AOI tiles render normally. `hex_map_godot`'s `hex_cell_shader_data.gd` (per-cell visible/explored texture channels) is the rendering reference if we tint live terrain instead of using a separate map screen.

### 4.3 Wrap-aware enemy pathfinding — Phase 4 (decision §0.5)

- New Rust helper (e.g. `world/pathing.rs`): A*/Dijkstra over wrapped hex neighbors (uses W1's `wrap_hex_coords`), heuristic = wrapped hex distance (min over lap candidates), binary heap.
- Cost function: elevation delta (walkable up/down thresholds already exist in `main/global.rs`), + `road_mask` cheaper, `river_mask`/cliffs blocked, `wall_mask` blocked when walled state differs between hexes, urban/farm surcharges later — ports `hex_unit.gd:130-152`.
- Enemy integration: a `PathGoal`/waypoint behavior layered on the existing steering (`enemy/methods.rs`) — path recompute on goal/target chunk change or every N ticks, steering handles the short-range movement. Keep the tick budget in mind (world is 684 hexes; A* is cheap, but recompute cadence is a knob).
- Read `hex_map_godot`'s `hex_grid.gd:593-665` for the reference implementation and its wrap-aware neighbor walk.

### 4.4 Shared hex region library — Phase 2 (decisions §0.8, §0.9)

- `godot-hex-map`'s `src/core/cell_id.*`, `iter_axial/radial/cube.*`, `planes.*` (tests in `tests/` are the spec) ported to:
  - C# `client/Scripts/World/HexRegion.cs`: `(q, r, y)` id, `s=-q-r`, half-away-from-zero axial rounding, `distance = hex_dist + |dy|`, rotation, plane-bounded iterators (plane = enabled axes expand by radius, disabled pin), and a wrap-aware variant using W1.
  - Rust mirror for server shape resolution.
- Adoption: replace bespoke circle/line/cone/rect math in `player/reducers.rs` + `BulletControllerComponent` with region queries, and use the same regions for client AoE previews. Add a ring-ordered iterator (iterators in the source are not distance-sorted) if ring-sequential ground effects are wanted.

### 4.5 Client-side auto-tile variant resolver — Phase 3 support (decision §0.8)

- Port the `godot-hex-map` rule data model (`auto_tiled_node.h/.cpp`: 35-cell pattern, `DISABLED/EMPTY/NOT_EMPTY/TYPE/NOT_TYPE`, 6 rotation permutation tables, ring masks, empty-origin search padding, ordered first-match) into a deterministic C# resolver.
- Input: server-owned types/masks (`HexTile` ground/overlay ids, river/road/wall masks, elevation). Output: purely cosmetic variants (wall caps at mask junctions, road/river junction sprites/meshes, MST transition kinds, decor variants). Server never stores these.
- Rule: any variant that gameplay depends on stays server-side (masks, costs, blockings); everything cosmetic is derived identically on every client from subscribed rows.

### 4.6 Sota ridge-based terrain — Phase 5 (decision §0.7)

- Goal: replace flat stepped hex tops with smooth, seamless hills/mountains/water (Sota's look) while keeping MST's stepped cliffs if desired, or moving to vertex heights.
- Reference: `ridge_set.cpp` (ridge polylines per connected same-biome group), `general_utility.cpp:98-156` (distance-to-border blending, corner wavefront distances, noise term), `plain/hill/mountain/water_mesh.cpp` (per-biome final-height curves), DSU grouping (`algo/dsu.h`, `ridge_hex_grid.cpp:624-654`), and `Terraformer`/`ridge_group.cpp:68-134` for incremental edits.
- Server-side for determinism: heights (or ridge data) generated in Rust from the world seed and streamed; client renders. Expect a new `HexTile`/`BuildingTile` height representation and a client mesh path that consumes it.
- Watch the latent bugs recorded in `Sota/AGENTS.md` (stale min/max state, `numeric_limits<float>::min()` init, half-hex iterative tessellation) if any code is reused — port the recursive tessellation path, never the iterative one.

---

## 5. Remaining backlog (not scheduled)

- `hex_map_godot` feature placement (`hex_feature_collection.gd` / `hex_feature_manager.gd`): enrich `world/terrain/decor.rs` with feature-level thresholds / hash clustering. Low priority — the existing decor rules cover most cases.
- `hex_map_godot` terrain mesh polish: vertex perturbation (`HexMetrics.perturb`), terraces/slopes, waterfalls — fold into §4.6 or MST independently.
- `hex_map_godot` land/climate generator (`_create_regions` / `_create_land` / `_erode_land` / `_create_climate` / `_set_terrain_type`): optional alternative biome seeding.
- Sota smooth normals via discretized shared vertices (`general_utility.cpp:22-43`, `discretizer.*`): ~30 lines C#; lets hex tops share averaged normals (fits §4.6).
- Sota honeycomb resource-level model (`honeycomb_honey.cpp:61-123`): model for mining/gathering nodes.
- Sota utility ports: `cosrp`, point-to-line distance for "distance to tile edge", recursive tessellation, icosahedral hex mapping only if a spherical map is ever wanted.
- `godot-hex-map` editor tooling (edit plane, cursor, quad selection, manual UndoRedo batching): reference for improving MST authoring; runtime-irrelevant.
- **Skip outright:** save/load and file versioning (SpacetimeDB owns persistence), UI/display modes, `Node3D`-per-cell architectures, octant chunking/MultiMesh rendering, lightmap/navmesh baking hooks, polyhedra/prisms/pent meshes, TAL shim, node-per-tile collision.

---

## 6. Cross-cutting concerns

- **Server authority / determinism.** Anything that changes world state (rivers, roads, walls, explored snapshots, pathfinding costs, ridge heights, DSU grouping) is computed in Rust from the world seed; the client mirrors and renders. Client-only ports must not feed gameplay decisions.
- **Lockstep copies.** `spiral_chunk_index` ↔ `HexMath.SpiralIndex` is the existing precedent; extend the convention and comments to `wrap_hex_coords`, `MSTHexMath.wrap_hex`, and the region library. Every pair needs a comment naming the other file(s).
- **Attribution (approved).** Phase 0: create `THIRD_PARTY_NOTICES.md` at the repo root (the three projects, their commits, full MIT texts, and a list of ported files/derivatives) and a one-line credit header in each ported file — including ported shaders, which are near-verbatim copies.
- **Verification loop.** `cargo check` + `dotnet build` for compile; playtest skill for runtime (MCP for gameplay, `spacetime` CLI for setup/inspection only). MST fork changes get a headless `--import` parse check per that folder's AGENTS.md.

---

## 7. Phasing

| Phase | Content | Depends on |
|---|---|---|
| 0 | `THIRD_PARTY_NOTICES.md` + credit-header convention | — |
| 1 | **Wrapping:** W1 wrap helper + mirrors, W2 seam-aware generation, **W3 MST wrap reads + chunk apron (confirmed)**, W4 audit, W5 verification | — |
| 2 | Hex region library (cell id + plane iterators + wrapped variants) in C#/Rust; adopt in ability shapes/AoE preview | Phase 1 |
| 3 | Rivers/roads/walls: `HexTile` masks + generation, client variant resolver, rendering + shaders | Phase 1, Phase 2 (resolver) |
| 4 | Per-profile explored map; wrap-aware enemy A*; feature placement; terrain polish | Phase 1, Phase 3 (costs/masks) |
| 5 | Sota ridge height field + smooth normals + DSU/Terraformer edit pattern | Phase 4 (3D terrain design freeze) |

---

## Appendix A — source feature → main target map

| Source | Feature | Main target | Status |
|---|---|---|---|
| hex_map_godot | Wrapped hex coords/distance/neighbors | `world/wrap.rs`, `HexMath.cs`, `TorusMath.cs`, MST `MSTHexMath` | Phase 1 |
| hex_map_godot | Chunk seam mesh shifts, camera column recycling | reference for `TerrainComponent3D` chunk apron | Phase 1 (reference) |
| hex_map_godot | Noise seam blending | not ported (periodic generation) | rejected |
| hex_map_godot | Rivers/roads/walls/water/estuaries + shaders | `HexTile` masks, `world/terrain/`, client mesh layer, `Assets/Shaders` | Phase 3 |
| hex_map_godot | Dijkstra + movement costs | Rust `world/pathing.rs`, enemy path-follow | Phase 4 |
| hex_map_godot | Explored/visible shader data | `PlayerExploredChunk` rows revealed per AOI disk + client fog/map layer | Phase 4 |
| hex_map_godot | Feature generator | `world/terrain/decor.rs` | backlog |
| godot-hex-map | Cell id + plane iterators | `World/HexRegion.cs` + Rust mirror | Phase 2 |
| godot-hex-map | Auto-tile rule engine | deterministic C# client variant resolver | Phase 3 |
| godot-hex-map | Quad selection, edit plane/cursor | MST authoring reference | backlog |
| Sota | DSU | Rust world/region helpers | Phase 5 (and Phase 3 if useful) |
| Sota | Recursive tessellation + smooth normals | MST mesh builder | Phase 5 |
| Sota | Ridge height field | server terrain heights + client mesh path | Phase 5 |
| Sota | Honeycomb levels | resource-node table | backlog |

## Appendix B — AGENTS.md files added with this plan

- `code_examples/Sota/AGENTS.md`
- `code_examples/godot-hex-map/AGENTS.md`
- `code_examples/hex_map_godot/AGENTS.md`

Each has the repo map, build/test commands, key files, gotchas, and a pointer back to this plan.
