# Next Steps


## 9. Texture Assignment Rules

Ground texture selection per triangle wedge — server in `world/methods.rs` (`write_triangle_tiles`), `world/def_tables.rs`, `world/instance_tables.rs`; client rendering in `client/Scripts/World/TerrainManager.cs`.

### Implemented

- **Per-biome config, not per-texture** — `BiomeDef.ground_textures: Vec<GroundTextureConfig>` / `overlay_textures: Vec<OverlayTextureConfig>` carry `chance`, `tags`, `allowed_rotations` (plus `scale` for overlays) directly. Deliberately *not* on `TextureEntry` — the same texture_id can be a ground texture in one biome and an overlay in another, with different tags/rotations/chance/scale each time. `TextureEntry` only holds `texture_id`/`res_path`/`kind` now.
- **Layering compatibility** — `LayeringRule { overlay_tag, allowed_base_tag }` (global, allow-list — default deny, since most overlay/base pairs don't make sense). E.g. `HalfStones` (`light_debris`) is allowed on both `Grass` (`soft`) and `Flowers` (`delicate`); `FullStones` (`heavy_debris`) only on `Grass`.
- **Overlay chance** — `OverlayTextureConfig.chance` (0-100%, doesn't need to sum to 100 — `weighted_pick_with_remainder` treats any shortfall as "no overlay," and scales down proportionally if compatible overlays' chances sum past 100). A base with fewer compatible overlays naturally ends up with a lower overall chance of getting one — no separate per-base modifier needed.
- **Rotation rules** — `allowed_rotations: Vec<u8>` on both ground and overlay configs, empty = all 3 allowed. Base and overlay each draw their rotation independently — no cross-layer interaction to reason about.
- **Frequency** — `GroundTextureConfig.chance`, weighted-random (`weighted_pick_index`), not required to sum to 100.
- **Client rendering** — overlay batches are a parallel set to base batches (`_overlayBatches`, sharing `_meshCache` since mesh geometry never depends on layer), drawn at `ZIndex = 1` vs base's `0` (both still under the whole-node `ZIndex = -10`) so overlay reliably draws above base regardless of batch-creation order. Overlay scale is applied per-instance around the wedge's own centroid (not the mesh's local origin / hex center), so a smaller overlay stays visually centered in its triangle instead of shrinking toward one corner.
- **Per-wedge texture UV mapping** (`GetOrBuildMesh`) — each wedge fits the *whole* texture to its own bounding box, then crops to the triangle shape, instead of slicing a thin pie-wedge out of one texture shared across all 6 wedges of a hex. A triangle only has 3 vertices to a square's 4 corners, so at most ~50% of the source image is reachable this way — still far more than the old hex-wide slice (~17%). Accepted trade-off: a whole hex now shows this crop repeated/rotated 6 times instead of reading as one continuous image — source art should be composed knowing it'll be seen as independent triangular crops, not as one image split across a hex.

### Deliberately not built yet
- **Noise-based distribution** — simplex noise per-region so same textures cluster spatially instead of scattering uniformly.
- **Biome border textures** — restricted texture set (1/3, 2/3 coverage shapes) for triangles adjacent to a different biome.

### Adjacency Rules (Speced, Not Built — Next, Before Decor)

Three relationship types, kept as separate explicit mechanisms rather than one generic "any tag near any tag" pool, so a rule written for one relationship can't accidentally fire for another just because two unrelated things happen to share a tag string:

1. **Overlay-on-base, vertical, same tile** — `LayeringRule`, already built (see above).
2. **Base-to-base, lateral, neighboring tiles** — new `BaseAdjacencyRule { tag_a, tag_b }`, deny-list (default allow — most neighboring base textures are fine, only specific clashing pairs get blocked). E.g. swamp should never sit directly next to sand.
3. **Overlay-to-overlay, lateral, neighboring tiles** — new `OverlayAdjacencyRule { tag_a, tag_b }`, same deny-list shape, kept separate from (2) so the same tag string can never cross-apply between base and overlay rules.

**Why this is more than two new tables**: `write_triangle_tiles` currently rolls each of a hex's 6 wedges completely independently, with zero neighbor-awareness. Adjacency has to be checked at the **wedge level**, not the hex level — Teo's call, since art is drawn per-triangle and needs to follow triangle-level rules. A wedge's neighbors are *mostly* the other two wedges in the *same* hex (a hex can already show up to 6 different ground textures today, one per wedge); only one of a wedge's three edges crosses into a different hex. Enforcing this needs:
- A defined generation order, so "already-decided neighbors" is well-defined at the point a wedge gets rolled.
- A wedge-neighbor lookup: 2 same-hex neighbors (always), 1 cross-hex neighbor (hex-direction math + the opposite wedge index on the far side of that shared edge).
- Retry-on-conflict when a rolled candidate violates a rule against an already-decided neighbor.
- A fallback for "no candidate is fully compatible": write an empty `texture_id`/`overlay_texture_id` rather than silently accepting a violation. `TerrainManager` already renders a missing texture as a solid fallback-color triangle (existing code), so this gives free, impossible-to-miss visibility into how often/where rules conflict while tuning, instead of quietly papering over it. If blank tiles turn out to be common enough that this isn't an acceptable dev experience, revisit toward "least rule violations" instead — but that needs a way to weigh a base-adjacency violation against an overlay-adjacency violation, not worth solving until it's a real problem.

Generation only runs at publish time, never live, so the added per-publish compute cost here is a non-issue.

Sequencing: speced now, built before decor — decor's own adjacency rules (decor-to-decor, decor-to-ground) extend this same wedge/hex-neighbor machinery once it exists, rather than inventing a second neighbor-awareness system.

### Decor Generation Rules (Open — Next Design Pass, Builds On Adjacency Rules Above)

Decor (trees, rocks, etc.) needs its own version of the texture-assignment rules above, scoped to per-hex placement rather than per-triangle:
- Which decor textures are valid per biome/region, plus frequency weights (same shape as ground/overlay configs above)
- Decor-to-decor adjacency (e.g. don't place two trees in adjacent hexes) — reuses the neighbor-awareness built for adjacency rules above, not a separate mechanism
- Decor-to-ground adjacency (e.g. no trees on sand texture)

Not yet speced in detail — revisit once the adjacency-rules work above is in and proven out.

### Decor & Cliff Rendering (Client — Depth/Y-Sort)

Decor sprites are taller than their hex footprint (e.g. a tree canopy), so unlike floor triangles, their on-screen bounds can overlap a neighboring hex. Handled client-side in `TerrainManager`, not server-side:
- Same-texture decor (e.g. tree vs. tree): sort instance positions by hex row (`r`) / world-Y before writing into the `MultiMesh` instance array — later index draws on top, so nearer (larger-Y) decor correctly covers farther decor.
- Cross-texture decor (e.g. tree vs. rock): requires merging into one shared atlas + MultiMesh with per-instance UV-region selection, since separate `MultiMeshInstance2D` nodes can't interleave per-instance draw order against each other.
- Dynamic objects (player/enemies) walking near tall decor need real per-instance sort against a moving object, which a static MultiMesh sort can't do — out of scope until decor is actually implemented; revisit then.

---

## Decisions (deferred, not active tasks)

### Elevation / Cliffs — Not Pursued

Active play space stays fully flat — no elevation data anywhere; biome/region visual variety comes from texture and decor density only. Reasons:
- Real elevation collision requires enemy obstacle-avoidance AI, which doesn't exist for *any* obstacle today — `apply_movement` (`enemy/methods.rs`) is pure straight-line steering (Wander/Chase/Flee) with zero collision awareness. A cliff spanning a biome boundary would be the first case that actually requires it (sparse obstacles like rocks rarely block a direct chase path; a long boundary wall often would).
- Visual-only elevation (no enforced collision/sightline) is worse than no elevation — bullets/enemies passing through what's clearly drawn as solid terrain is a visible inconsistency, not a neutral cosmetic choice.

Future direction, if revisited: asymmetric elevation — players can cross/shoot from elevated terrain, enemies are blocked by it (movement collision only, same solid/non-solid flag every other obstacle already uses). Enemy bullets are naturally constrained by enemy *position* (no separate occlusion system needed), giving players a positioning advantage without needing full bilateral line-of-sight math. Procgen-side this would extend cleanly off the planned biome-border pass above (same neighbor lookup, also tagging the boundary as elevated) plus a new lightweight collision-edge table (same pattern as `BuildingTile`); the real new work is the enemy-avoidance AI, not procgen or rendering — both of those already extend the systems planned/built above.

### Region Spawn Trigger Uses Center Distance, Not Region Footprint — Not Yet Remedied

`has_player_near_spawn_point` and the actual spawn-cap/placement disc (`enemy/methods.rs`, `enemy/reducers.rs`) are both anchored to a region's `(center_hex_q, center_hex_r)` point + `spawn_radius`, not to the region's real (possibly irregular) Voronoi cell of `TriangleTile`s. With few/large regions this means a player can be standing on ground visually tagged as belonging to a region yet be far enough from its center to never trigger spawning there. Real fix would anchor both the trigger and placement to the region's actual tile footprint (`TriangleTile.region_id`) instead of a circle — deferred for now since more regions (smaller Voronoi cells, closer to their own centers) should shrink the practical impact; revisit if it's still a problem once real region content exists.
