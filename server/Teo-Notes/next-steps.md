# Next Steps

## Recently Completed (reference only)

- **World generation pipeline** — `BiomeDef`/`BiomeRegionDef` config tables, `BiomeRegion`/`TriangleTile` output tables, `generate_world_proc`/`generate_world_manual` reducers. Fully implemented in `world/tables.rs`, `world/def_tables.rs`, `world/instance_tables.rs`, `world/methods.rs`. Read the source for exact fields/flow — this file no longer carries a parallel schema dump.
- **Enemy spawning migrated off `EnemyZone`** — deleted that table; `spawn_from_biome_regions` (renamed from dead-code `spawn_from_zones`) iterates `BiomeRegion` and is now actually wired into `tick_enemy_spawn`.
- **Enemy simulation split into move/attack ranges** — `EnemyTemplate.move_sim_factor`/`attack_sim_factor`, multiples of `CAMERA_VIEW_RADIUS` (derived from the client's actual viewport/zoom in `world/methods.rs`) rather than hardcoded world units. Movement starts from farther away; attacks only fire once a player is within camera view (+ small lead-in), so nothing shoots from off-screen.
- **Fixed: ghost-position spawning** — `has_player_in_simulation_range`/`has_player_near_spawn_point`/`find_player_pos_by_id` were reading `PlayerPosition` without checking `LoggedInPlayer`. Since logout/disconnect preserves `PlayerPosition` (by design, for next login), a departed player's last position kept triggering spawns/simulation forever. Fixed by filtering to currently-logged-in profiles, same pattern `find_nearest_player_id` already used.
- **Fixed: region spawn cap wasn't torus-wrap-aware** — `live_count` compared enemy positions to a region's center with plain Euclidean distance, but the actual spawn position goes through `wrap_world_pos`. A region sitting near a lap seam could have its spawn offset wrap to the far side of the map, permanently invisible to its own cap check → unbounded spawning. Fixed with `wrapped_distance_sq` (checks all 9 lap-shift combinations, mirrors the client's `TorusMath.NearestCandidate`).
- **Fixed: spawn angle was silently frozen** — `angle_hash as f32 / u64::MAX as f32` lost so much precision that the angle didn't visibly change for long stretches (f32's rounding granularity at that magnitude is far coarser than how much the per-tick hash actually moved). Switched to `hash_to_unit(splitmix64(...))`, the same deterministic-PRNG pattern already used for Wander movement and world-gen.
- **AOI tightened** — `DEFAULT_AOI_CHUNK_RADIUS` 3→2 (now matches `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`, since there's no point subscribing players/enemies/loot further out than terrain itself renders). `SPAWN_LOOKAHEAD_CHUNK_RINGS` 2 (matches AOI — still a real lead-time buffer since AOI subscription range is itself well past actual camera view distance). `SIMULATION_CHUNK_RINGS` 1 (tight, exact-distance gate for movement/attack checks).

---

## 8. Enemy Spawning & Chunk Granularity (Open)

Investigating the bugs above surfaced a structural mismatch, not just code defects:

- **Region spawn placement should be pure-random within the region**, not a fixed-radius ring. Current formula (`center + cos/sin(angle) * spawn_radius * 0.5`) only varies angle — every enemy from a region lands on the exact same circle. Should pick a random point inside the disc (or some other natural distribution), not just a random direction at a fixed distance.

- **`chunk_hex_radius` is too coarse relative to `CAMERA_VIEW_RADIUS`.** A chunk is currently ~915 world units across; the camera only sees ~240-427. That means any chunk-bucket-based proximity check (AOI, spawn lookahead, simulation range) is checking an area 2-4x bigger than what's actually relevant, so "N rings of chunks" doesn't track real "can I see/reach this" distance. Fix: shrink `chunk_hex_radius` (not `hex_outer_radius` — that's tile visual size, leave it alone) so one chunk is closer to one screen-width, paired with growing `chunk_cols`/`chunk_rows` proportionally to keep the total world size the same.

- **Region count/density needs to scale with the (finer) chunk grid.** With only 5 regions on a 10×10 chunk grid, a 2-ring lookahead already covers ~19% of the entire map — so "in lookahead range" ends up true almost regardless of where a player stands, even though the actual region might be thousands of world-units away (only "close" because of torus wraparound at the chunk-bucket level). Shrinking chunk size alone doesn't fix this unless region count/spread also scales up — this is really a map-design-pass concern, not a constant to tune in isolation.

These three are coupled — solve them together when doing the real map-layout pass, not as one-off constant tweaks.

---

## 9. Texture Assignment Rules (Next Step After Core Gen)

Current gen picks textures uniformly at random from the region's `texture_ids`. To produce natural-looking terrain, rules need to be layered on top. No new tables required — add rule fields to existing def tables and manual structs; the gen algorithm gains a texture-selection pass that consults them before writing each `TriangleTile`.

### Planned rule types

- **Frequency weights** — parallel `texture_weights: Vec<f32>` alongside `texture_ids` on `BiomeDef`, `BiomeRegionDef`, and the manual input structs. Controls how often each texture is chosen (e.g. grass 60%, flower 20%, rock 20%).

- **Allowed rotations per texture** — parallel `texture_allowed_rotations: Vec<Vec<u8>>` (indexed to `texture_ids`). Some textures don't look right at all three rotations — this restricts which values 0/1/2 are valid for each.

- **Noise-based distribution** — use simplex noise seeded per-region (with `seed` from `generate_world_proc`) to cluster textures spatially rather than scatter them randomly. The noise value at each triangle's world position selects a texture from the weighted list, so similar textures group together naturally.

- **Biome border textures** — triangles whose hex is adjacent to a different biome get a restricted texture set (the 1/3 and 2/3 coverage triangle shapes designed for biome edges). Interior triangles use full-coverage textures. Requires a neighbor-biome lookup pass during gen.

- **Adjacency rules / tags** — tag textures (e.g. `"ground"`, `"feature"`, `"border"`) and define which tags can appear adjacent. Prevents unnatural clustering (e.g. two feature textures side by side). Requires a second pass over already-placed triangles during gen.

### Ground Z-Layers (Overlays)

A dirt path painted over grass on the same triangle is a second texture layer with the same footprint as the base layer, not a spatial feature. No positional sort needed — the overlay's footprint is always 100% coincident with the base layer, so draw order is static (overlay batch always renders after the base batch, same node-order/z_index approach as decor-after-floor). Needs an `overlay_texture_id` (or small `Vec<String>` for stacked overlays) per region/biome def, rendered via its own MMI batch keyed the same way as the base layer (`TriIndex`, `Rotation`, `TextureId`).

### Decor Generation Rules (Open — Next Design Pass)

Decor (trees, rocks, etc.) needs its own version of the texture-assignment rules above, scoped to per-hex placement rather than per-triangle:
- Which decor textures are valid per biome/region, plus frequency weights (same shape as `texture_weights` above)
- Decor-to-decor adjacency (e.g. don't place two trees in adjacent hexes)
- Decor-to-ground adjacency (e.g. no trees on sand texture)

Not yet speced — revisit once core texture rules (above) are in and proven out.

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
