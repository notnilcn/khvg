# Third-Party Notices

This repository is a bullet-hell MMO roguelike built as a Godot 4.7 mono C# client over a SpacetimeDB 2.2 Rust server. It ports and/or reimplements concepts, algorithms, and rendering techniques from the three MIT-licensed reference projects below. This file records that attribution: the projects and their copyright holders, the license terms under which their code is used, and the areas of this repository that contain ports or derivatives.

The porting plan, including per-feature phases, is `PLAN.md` (see §6 and Appendix A). Statuses in the "Derivative works" section reflect that phasing.

---

## Sota

- **Copyright:** `Copyright (c) 2024 Roman Ardazishvili and Contributors.`
- **License:** MIT License
- **Source snapshot:** commit `16f3de6`
- **Repository URL:** `https://github.com/ardazishvili/Sota/blob/main/LICENSE` (link given in the source README; no other URL is stated in the license file)

### MIT License

```text
MIT License

Copyright (c) 2024 Roman Ardazishvili and Contributors.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## godot-hex-map

- **Copyright:** `Copyright 2024-present David M. Lary`; Godot engine portions: `Copyright (c) 2014-present Godot Engine contributors (see AUTHORS.md).` and `Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur.`
- **License:** MIT License (main project; Godot portions also MIT, per `LICENSE-godot.txt`)
- **Source snapshot:** commit `bab38bd`
- **Repository URL:** `https://github.com/dmlary/godot-hex-map` (clone URL given in the source README)

### MIT License (`LICENSE.txt`)

```text
Copyright 2024-present David M. Lary

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the “Software”), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### MIT License — Godot portions (`LICENSE-godot.txt`)

```text
Copyright (c) 2014-present Godot Engine contributors (see AUTHORS.md).
Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## hex_map_godot

- **Copyright:** `Copyright (c) 2024 David Pruitt`
- **License:** MIT License
- **Source snapshot:** commit `135dc75`
- **Repository URL:** none is stated in the source `LICENSE.md` or `README.md`; the README only links the catlikecoding tutorial series it reimplements. No repository URL is therefore listed here.

### MIT License

```text
MIT License

Copyright (c) 2024 David Pruitt

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Derivative works in this repository

This section lists the files and areas that are ports or reimplementations of the projects above, grouped by source and annotated with the implementation phase from `PLAN.md` §7 / Appendix A and its current status. **Present** means the derived code is in the tree; **Partial** / **Not ported** entries state what is missing or what replaced the planned approach. The pre-existing wrapping/hex code that predates the plan is listed only where the port extended it.

### From `hex_map_godot` (`135dc75`)

| Target in this repository | What is derived | Phase | Status |
|---|---|---|---|
| `server/spacetimedb/src/world/wrap.rs` — `wrap_hex_coords` | Canonical hex wrap helper; reimplementation of the source's wrapped hex coords/distance/neighbors | 1 (W1) | Present — implemented together with `wrapped_hex_neighbors`; `#[cfg(test)]` covers idempotence, neighborhood preservation and lap displacement |
| `client/Scripts/World/HexMath.cs` — `WrapHex` | Client mirror of `wrap_hex_coords` (lockstep copy) | 1 (W1) | Present — lockstep copy; consumed by Water/GridOverlay/TerrainRoads/AbilityPreview components |
| `client/Scripts/World/TorusMath.cs` — `WrapPosition` | Client mirror of server `wrap_world_pos` for position → canonical before hex lookups | 1 (W1/W4) | Present — lockstep copy; `ElevationMapComponent` is the main consumer |
| `client/addons/MarchingSquaresTerrain/algorithm/terrain/mst_hex_math.gd` — `MSTHexMath.wrap_hex` | MST fork mirror of `wrap_hex_coords` | 1 (W1/W3) | Present — plus `wrap_chunk`, consumed by the fork's `get_hex_global`/`_regenerate_neighbor_chunks` |
| `server/spacetimedb/src/world/terrain/` seam-aware generation (e.g. `rules.rs` wrap-aware neighbor lookup) | Wrapped-neighbor walk used by the source's grid neighbor logic | 1 (W2) | Present — `decided_neighbor_tags` canonicalizes through `wrapped_hex_neighbors` |
| `client/Scripts/Components/Terrain/TerrainComponent3D.cs` chunk apron | Chunk seam mesh shifts and camera column recycling are the reference for the 3D chunk apron (mirrors, not whole-node recentering) | 1 (W3, reference only) | Present — visual-only `ChunkAprons` mirror pool sharing canonical chunk meshes/shells; no source code copied |
| `server/spacetimedb/src/world/terrain/` river/road/wall generation; `HexTile` per-edge masks | Ports `_create_rivers` (incoming-direction/estuary bookkeeping), road placement, and wall placement from the source generator/cell code | 3 | Partial — `world/masks.rs` + `terrain/rivers.rs`/`roads.rs` are in; **wall generation is not implemented** (`wall_mask` is reserved and always 0) |
| `client/Assets/Shaders/{river,road,wall,water,water_shore,estuaries}_shader.gdshader` | Shaders ported nearly verbatim from the source's corresponding shaders | 3 | Not ported — `TerrainRoadsComponent3D` renders the masks as flat unshaded ArrayMesh strips instead; the pre-existing `Assets/Shaders/water.gdshader` is unrelated to this plan |
| `server/spacetimedb/src/world/pathing.rs` | Wrap-aware A*/Dijkstra and movement costs; reference implementation is the source's grid pathfinding and unit costs | 4 | Present — deterministic Dijkstra + `terrain_step_cost`; used by road generation and enemy AggroTarget routing |
| Per-profile explored map (`PlayerExploredChunk`) and client fog/map layer | Reveal/explored shader data model is the rendering reference | 4 | Present — `player/explored.rs` + view + `ExploredMapComponent3D` (flat memory-tile mesh, not the source's shader-channel tint) |
| `server/spacetimedb/src/world/terrain/decor.rs` (feature placement) | Feature generator thresholds/clustering | Backlog | Not scheduled |

### From `godot-hex-map` (`bab38bd`)

| Target in this repository | What is derived | Phase | Status |
|---|---|---|---|
| `client/Scripts/World/HexRegion.cs` | `(q, r, y)` cell id, axial rounding, distance, rotation, and plane-bounded iterators ported from the source core cell-id and iterator/plane code | 2 (2A) | Present — wrap-aware variants use `HexMath.WrapHex`; consumed by `AbilityPreview3DComponent`'s conservative AoE preview, no gameplay code reads it |
| `server/spacetimedb/src/world/hex_region.rs` | Same cell-id/iterator/plane logic mirrored for the server, built on `wrap_hex_coords`; the source's `tests/` assertions are ported into its `#[cfg(test)]` module | 2 (2A) | Present — mirrored; the assertions typecheck via `cargo check --tests` (a SpacetimeDB module cannot link a test binary) and were validated from a standalone temp crate. Not yet adopted by server gameplay code |
| `client/Scripts/World/HexAutoTile.cs` | Deterministic client auto-tile variant resolver (cosmetic variants from server-owned types/masks); rule data model ported statement-for-statement from the source auto-tiling node: 35-cell pattern, cell states, rotation permutation tables, ring masks, empty-origin search padding, ordered first-match | 3 | Present — ported, with the ported GDScript-oracle tests (all parameterized rule tests + the exhaustive per-cell rotation test) in a temp console harness; not yet wired into a renderer |
| MST authoring reference (quad selection, edit plane/cursor) | Editor tooling reference only; runtime-irrelevant | Backlog | Not scheduled |

### From `Sota` (`16f3de6`)

| Target in this repository | What is derived | Phase | Status |
|---|---|---|---|
| Server terrain heights + client mesh path (ridge height field) | Ridge polylines per connected same-biome group, distance-to-border blending, per-biome final-height curves | 5 | Present — `world/heights.rs` produces the field (`BuildingTile.heights` + derived `elevation`); the client renders smooth tops via `draw_hex_heights` + `MSTHexMath.smooth_hex_height` and interpolates them in `ElevationMapComponent.TryGetSurfaceHeightAt` |
| Rust world/region helpers (DSU) | Disjoint-set grouping used by the ridge/region code | 5 (and Phase 3 if useful) | Present (narrow) — a private DSU inside `heights.rs` groups same-class hexes; not factored into a shared `world/` helper |
| MST mesh builder (recursive tessellation + smooth normals) | Recursive tessellation path and discretized shared-vertex normals; the iterative tessellation path is not to be reused | 5 | Not ported as such — the fork's smooth top is a barycentric subdivision of the server payload (`SMOOTH_SUBDIV`) with averaged per-triangle normals; Sota's recursive tessellation and discretized shared vertices are not used |
| Resource-node table (honeycomb levels) | Honeycomb resource-level model | Backlog | Not scheduled |

---

## Credit-header convention

Every file that contains ported or derived code gets a one-line credit header at the top:

- Rust, C#, and GDShader files:

  ```text
  // Portions derived from <project> (<commit>) - MIT. See THIRD_PARTY_NOTICES.md.
  ```

- GDScript files:

  ```text
  # Portions derived from <project> (<commit>) - MIT. See THIRD_PARTY_NOTICES.md.
  ```

`<project>` is the source project name (`Sota`, `godot-hex-map`, or `hex_map_godot`); `<commit>` is the snapshot commit listed in that project's section above. Use this "Portions derived from…" wording when a file contains only a small derived portion alongside original code.

Shaders ported near-verbatim are credited with the ported wording instead:

```text
// Ported from <project> (<commit>) - MIT. See THIRD_PARTY_NOTICES.md.
```
