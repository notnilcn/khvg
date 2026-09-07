# 12 World Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for the table-per-concern mirror, [[03 Connection, Subscriptions & Views]] for AOI views and global catalog views. Tunables referenced below live in `09 Main Tables` (`global.rs`); the elevation rules that consume them in `10 Player Tables` (`report_movement`); the hex-spiral lockstep sibling (`HexMath.SpiralIndex`) in the client `World/` helpers.

## The 30-second version

The world is a torus of hex chunks: `MapConfig` (singleton grid + lap vectors) plus one `BuildingTile` row per hex (carrying the integer elevation level) form the simulation grid, built by `internal_add_chunks` and stamped by generation. Content comes from definition inputs (`WorldDef` → `BiomeDef` → `BiomeRegionDef` plus texture/rule tables) compiled by the deterministic `terrain/` pipeline — Voronoi biome/region seeds, then per-hex ground → overlay → decor passes with adjacency/layering checks sharing one PRNG draw counter — into generated rows (`BiomeRegion` spawn regions, `TriangleTile` wedges, `HexDecor` props). Clients render the generated rows through AOI views into the pooled `TileComponent` chunk ring (ocean wherever rows are absent) and read elevation through the `nearby_building_tiles` view; spawn regions and generation inputs never leave the server.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How big is the world / how does wrapping work? | `MapConfig` | [[#MapConfig]] — chunk grid, lap vectors, torus math |
| What is the elevation of this hex? | `BuildingTile` | [[#BuildingTile]] — per-hex levels, walk/fall rules |
| Where do sprites and models resolve from? | `TextureEntry` | [[#TextureEntry]] — 2D/3D path pair, empty = no 3D |
| How do 3D character animations attach? | `AnimationEntry` | [[#AnimationEntry]] — clip merge by id |
| Which overlays / neighbors / decor are legal? | rules | [[#LayeringRule]], [[#BaseAdjacencyRule]], [[#OverlayAdjacencyRule]], [[#DecorGroundRule]] |
| How is the map generated? | `WorldDef` / `BiomeDef` | [[#WorldDef]], [[#BiomeDef]] — Voronoi front-ends over one pipeline |
| Where do enemies spawn? | `BiomeRegion` | [[#BiomeRegion]] — regions are server-side (no `EnemyZone` table) |
| How is the ground drawn? | `TriangleTile` | [[#TriangleTile]] — wedges into the pooled chunk ring |
| How are trees/rocks placed? | `HexDecor` | [[#HexDecor]] — at most one prop per hex |
| How do chunk indices work? | — | [[#hex.rs]] — hex-spiral ids, lockstep client copy |

## Flowcharts

- [[flowcharts/main-world.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/world_subfolder.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/terrain_subfolder/terrain_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Terrain_subfolder/Terrain_subfolder.canvas]]

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
![[00 Table Map#^table-animation-entry{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 Table Map#^table-layering-rule{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 Table Map#^table-base-adjacency-rule{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 Table Map#^table-overlay-adjacency-rule{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 Table Map#^table-decor-ground-rule{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 Table Map#^table-world-def{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 Table Map#^table-biome-def{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 Table Map#^table-biome-region-def{seamless:true,title:false,marker:11.}]]
```

```sync
![[00 Table Map#^table-biome-region{seamless:true,title:false,marker:12.}]]
```

```sync
![[00 Table Map#^table-triangle-tile{seamless:true,title:false,marker:13.}]]
```

```sync
![[00 Table Map#^table-hex-decor{seamless:true,title:false,marker:14.}]]
```

(The entries above are the complete changer/reader reference. The sections below add Shape plus the generation/spatial mechanics the entries summarize.)

## MapConfig

### Shape

Singleton row (id 0) in [[server/spacetimedb/src/world/tables.rs#MapConfig|tables.rs]]: chunk hex radius (2), grid size (6×6 chunks, ~2500 world units wide — shrunk from 22 so ~45 biome enemies stay encounterable on foot), hex outer radius (48), and the two torus lap vectors (world-space displacement of one full lap per axis, derived from the grid at chunk setup).

### Changers

`internal_add_chunks` inserts-or-updates the singleton (validating cols-divide-rows and positive radius); `Seed` impl covers the pre-chunks fallback. Reads go through `MapConfig::load(ctx, …)` with caller-chosen degenerate fallbacks (simulation `0, 0` disables wrapping via the `<= 0` guard; admin `1, 1` collapses to one chunk) — except `player/views.rs`, which reads the row directly (a `ViewContext` can't call the helper) defaulting to `1, 1` (see entries above).

### Readers

- ***Client*** — raw `public` table, Game wave → `TableSubscriber`'s internal hooks mirror `LapQ`/`LapR` statics (per-entity-per-frame reads by `InterpolationComponent`, reset in `_ExitTree`) → grid geometry in `TerrainComponent`, debug grids in both hex-grid overlays, elevation radius in `ElevationMapComponent`; `DebugOverlay` also reads the row. Visible end: correct wrapping near every torus seam.
- ***Server*** — every wrap/AOI/chunk computation takes its radii from here; `hex_elevation` defaults to 0 before chunks exist.

## BuildingTile

### Shape

One row per hex: hex coords (btree `hex_q` + `hex_r` find), chunk coords + `local_index`, btree `chunk_index`, `building_type` (empty = none) + optional `owner_id`, and the integer `elevation` level (0–7 over `ELEVATION_LEVELS`).

### Changers

Inserted only by `internal_add_chunks` (empty type, elevation 0); `run_generation`'s elevation pass stamps quantized noise levels onto existing rows without consuming a PRNG draw (deterministic order untouched; `clear_world_geometry` never touches this table); `place_building` / `remove_building` stamp/clear type + owner (placement requires the generated row — "has add_chunks been called?"); `clear_chunks` wipes all tiles (see entries above).

### Readers

- ***Client*** — via the `nearby_building_tiles` AOI view (terrain-radius ring) in the Game wave, replay ON → `ElevationMapComponent`'s binders → the `(q, r) → level` dictionary (`GetElevation` default 0, `GetWorldHeightAt`, `ElevationChanged`) → `LocalPlayer` prediction + fall tween, bullet band rule, 2D terrain tint, 3D mesh heights. Visible end: cliffs that block, falls that tween, bullets clearing low ground.
- ***Server*** — `hex_elevation` feeds `report_movement`'s walk/block/fall rules and enemy steering cliff probes (enemies have no fall mechanic — steep down blocks them too).

## TextureEntry

### Shape

One row per texture id: string PK, `res_path2d` (sprite/icon) + `res_path3d` (model — empty = no 3D representation, e.g. 2D shadow decals where lighting casts real shadows), and `TextureKind`. A texture's *role* (ground vs overlay, tags, rotations, chance) is per-biome in ^table-biome-def, not here — the same id can be ground in one biome and overlay in another.

### Changers

`seed_default_textures` (every referenced id needs a row — missing = blank icon, no error); `upsert_texture_entry` (+ `_fields` variant) at runtime (see entries above).

### Readers

- ***Client*** — via the `all_textures` view in the Base wave → `CatalogComponent` (`GetResPath2D` / `GetResPath3D` normalized to null) → every sprite/icon/model load (entities, drops, tiles, UI icons, 3D mirrors).
- ***Server*** — none (rendering-only catalog).

## AnimationEntry

### Shape

One row per named animation clip: string PK, the source scene whose `AnimationPlayer` holds the clip, the clip name inside it, and `AnimationKind` (Locomotion / Combat / Interaction / Emote).

### Changers

`seed_default_animations` only — static seed content, no update/delete paths (see entries above).

### Readers

- ***Client*** — via the `all_animations` view in the Base wave → `CatalogComponent`'s binder → `LocalPlayer` merges every clip into the 3D character model's `AnimationPlayer` under its id (e.g. `ual1/Walk`); `DebugOverlay` lists the rows. Visible end: animated 3D models.
- ***Server*** — none (rendering-only catalog).

## LayeringRule

### Shape

Allow-list: overlay tag × allowed base tag (auto-inc `rule_id`). Default is deny — compatibility is opt-in per pair.

### Changers

`seed_layering_rules` only (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — generation input).
- ***Server*** — `overlay_compatible_with_base` gates `choose_overlay` per wedge: leftover chance after compatible overlays is "no overlay"; overshoot scales down proportionally (never overflows).

## BaseAdjacencyRule

### Shape

Deny-list: symmetric tag_a × tag_b pairs that may never sit in laterally neighboring tiles (same-hex or cross-hex — one row blocks both directions). Default is allow; only clashing pairs (swamp/sand) are listed.

### Changers

`seed_adjacency_rules` only (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — generation input).
- ***Server*** — `base_tags_conflict` gates `choose_ground` against already-decided neighbor wedges (`NeighborTagMap`).

## OverlayAdjacencyRule

### Shape

Same shape and symmetry as ^table-base-adjacency-rule, kept separate so a shared tag string can never cross-apply between a base rule and an overlay rule.

### Changers

`seed_adjacency_rules` (same seeder — see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — generation input).
- ***Server*** — `overlay_tags_conflict` gates `choose_overlay`.

## DecorGroundRule

### Shape

Deny-list decor_tag × denied_base_tag — not symmetric (it relates two tag kinds, not two occurrences of one).

### Changers

`seed_decor_ground_rules` only (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — generation input).
- ***Server*** — `decor_ground_conflict` gates `place_decor` per hex (ground OR overlay tags on any of the 6 wedges).

## WorldDef

### Shape

Generation front-end: string PK, display name, `biome_weights` (share of the `BIOME_VORONOI_SEED_BUDGET` = 16 seed points, hence of map area on average).

### Changers

`seed_world_defs`; `upsert_world_def` at runtime (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — generation input).
- ***Server*** — `internal_generate_world_proc` resolves biome seeds from weights (`combine_biome_weights` / `distribute_biome_seeds`, one seed per entry) and buckets every world hex under its nearest seed.

## BiomeDef

### Shape

One biome's content: ground/overlay texture configs (per-biome tags/chances/rotations/scales — chance is relative frequency for ground, percent-of-compatible for overlay), `region_weights`, noise scales for ground/overlay/decor clustering (≤ 0 disables clustering to plain picks, each on its own decorrelated field), and `decor_min_separation` (hex distance floor between any two decor).

### Changers

Seeded with the world defs; `upsert_biome_def` at runtime (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — generation input).
- ***Server*** — `choose_ground` / `choose_overlay` (noise-clustered weighted picks per wedge, rotations from `allowed_rotations`) and `place_decor` per hex.

## BiomeRegionDef

### Shape

One region template: string PK, enemy roster, `max_enemies`, `spawn_radius`. No position — positions resolve at generation.

### Changers

`seed_region_def` (private, called per region from `seed_world_defs`); `upsert_biome_region_def` at runtime (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — generation input).
- ***Server*** — region seeding: one seed per `RegionWeight` entry with a resolvable def (missing defs skip without consuming a draw), each resolving to a `RegionPlan` stamped into a ^table-biome-region row.

## BiomeRegion

### Shape

One live spawn region: auto-inc `region_id`, `biome_id`, center hex, roster, `max_enemies`, `spawn_radius`. There is deliberately no `EnemyZone` table — this is it.

### Changers

Inserted per plan by `run_generation`; wiped by `clear_world_geometry` on every regeneration (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — simulation input).
- ***Server*** — `spawn_from_biome_regions` (the 2 s tick) spawns inside regions with players in `SPAWN_LOOKAHEAD_CHUNK_RINGS`, capped by `MAX_LIVE_ENEMIES` / `MAX_SPAWNS_PER_TICK` / per-region `max_enemies`.

## TriangleTile

### Shape

One row per triangle wedge (6 per hex): hex coords + `tri_index`, btree `chunk_index`, ground texture + rotation, optional overlay (+ rotation + biome-copied scale, so the client renders without joining `BiomeDef`), and biome/region ids.

### Changers

`write_triangle_tiles` emits 6 rows per hex in deterministic draw order (ground pass → overlay pass → tag record → insert); regeneration wipes and rewrites the set (see entries above).

### Readers

- ***Client*** — via the `nearby_terrain_tiles` AOI view (terrain-radius ring) in the Game wave → `TerrainComponent`'s binder → the pooled `TileComponent` ring (`1+3R(R+1)` pre-warmed at `AoiChunkRadius` 2, one rebuild per frame max, camera path throttled to chunk crossings, per-texture MultiMesh batch leaves, per-hex torus wrap + cull, elevation tint from ^table-building-tile); `WaterComponent3D` floats ocean tiles wherever rows are ABSENT (beyond-AOI reads as missing, so the ring edge is open ocean); `world_3d.tscn` binds the same view for the 3D terrain. Visible end: the ground itself.
- ***Server*** — none post-generation (rendering payload).

## HexDecor

### Shape

At most one prop per hex (separation rules make more impossible): hex coords, btree `chunk_index`, texture + paired shadow sprite (rotating as one unit, data-driven not by naming), rotation degrees, biome/region ids. Hexes without decor simply have no row — unlike wedges, decor is optional content, not required fill.

### Changers

`place_decor` per hex; regeneration rewrites the set (see entries above).

### Readers

- ***Client*** — via the `nearby_hex_decor` AOI view in the Game wave → `TerrainComponent`'s decor layers (per-texture batches sorted by hex row + shadow layer). Visible end: trees, rocks, props.
- ***Server*** — none post-generation (rendering payload).

## Files — pointer deep dive

Entry points: [[server/spacetimedb/src/world/pointers.md|world pointers]].

### aoi.rs

Reducer-side AOI: `surrounding_chunk_indices` (chunk rings around a chunk, torus-wrapped) and `chunk_indices_covering` (chunk superset of a world-space circle). Used by player views (subscription AOI), enemy sim + spawn (simulation/spawn rings), and the reducer-side circle queries (`players_near_point`, `enemies_near_point` — views can't run inside reducers).

### def_tables.rs

Generation inputs (`WorldDef`, `BiomeDef`, `BiomeRegionDef` + weight/config structs + manual inputs). Written by seeds and the three def upserts in admin; read by the terrain front-ends.

### hex.rs

Hex/chunk math over the `hexx` crate (`to_higher_res` / `to_lower_res` / `from_hexmod_coordinates`): `world_to_hex` / `hex_to_world` / `world_to_chunk` / `chunk_center_hex` / `hex_area` / `inv_hexmod` / `HexCell` / `enumerate_world_hexes` + neighbor deltas, and the hand-rolled bijective `spiral_chunk_index` — whose lockstep client sibling is `HexMath.SpiralIndex` (consumed by both hex-grid overlays): both copies must change together. Used by chest, enemy, admin, debug, and player code. Background: redblobgames hexagons, hexagon-tiling-of-a-hexagonal-grid, hexmod representation (see `server/AGENTS.md`).

### instance_tables.rs

Generated world rows (`BiomeRegion`, `TriangleTile`, `HexDecor`). Read server-side only by `spawn_from_biome_regions` (regions); tiles/decor are write-then-render payloads.

### mod.rs

Module declarations only.

### noise.rs

`value_noise2d` + seed offsets (elevation/overlay/decor/meander each on its own decorrelated field). No vault consumers outside the folder — generation-internal.

### prng.rs

Deterministic PRNG (`splitmix64` / `hash_to_unit` / `next_unit` + `pick_index` / weighted picks). Deterministic because reducers must be — `ctx.rng` never enters worldgen; the shared `draw` counter is the randomness. Also used by enemy methods.

### reducers.rs

`hex_elevation` (hex_q-btree + hex_r-find, default 0 — the same lookup placement uses; consumed by `report_movement`) plus `place_building` / `remove_building`: placement stamps type + owner onto the generated row (admin-only authoring, buildings aren't a designed feature yet), removal clears them (owner or admin, any logged-in caller). Buildings are write-only world authoring for now (see [[#Known gaps / stubs]]).

### tables.rs

Config tables (`MapConfig`, `BuildingTile`, `TextureEntry`, `AnimationEntry`, the four rule tables). `MapConfig` is used by every module (chest, enemy, player, status, debug, admin, seeds); `BuildingTile` is read by admin/debug and written by chunks/generation/buildings; the catalog/rule rows are written by seeds + admin upserts.

### terrain/decor.rs

`place_decor`: per-hex prop rolls (chance, tag conflicts, separation set) — folder-internal, called once per cell by `run_generation`.

### terrain/ground.rs

`choose_ground`: per-wedge ground pick (noise-clustered weighted chance + adjacency conflicts + rotation) — folder-internal.

### terrain/mod.rs

The orchestrator: `internal_generate_world_proc` / `internal_generate_world_manual` are two front-ends over parameterized `run_generation` (passes: clear geometry → elevation stamp → Voronoi bucket → per-biome region plans → per-hex wedge/decor writes, one shared `draw` counter, deterministic order preserved; missing defs claim cells but emit no content). Called by `init` (starting world) and the admin generate wrappers.

### terrain/overlay.rs

`choose_overlay`: per-wedge overlay roll on top of the ground pick (compatibility + percent chance + scale) — folder-internal.

### terrain/rules.rs

Rule checks + `NeighborTagMap` (`overlay_compatible_with_base`, `base_tags_conflict`, `overlay_tags_conflict`, `decor_ground_conflict`, `decided_neighbor_tags`) — folder-internal, consulted against already-written neighbors.

### terrain/voronoi.rs

Seed distribution (`validate_weights_sum_100`, `combine_biome_weights`, `distribute_biome_seeds`, `group_cells_by_seeds`, `nearest_seed_index`) — folder-internal.

### views.rs

`all_textures` / `all_animations` (global catalog views) + the three terrain-radius AOI views (`nearby_terrain_tiles`, `nearby_building_tiles`, `nearby_hex_decor` — all over `nearby_indices_from_chunk`, sentinel-empty pattern). Subscribed in Base (catalog) and Game (terrain) waves; bound in `game.tscn` (`TerrainComponent`, `ElevationMapComponent`, `CatalogComponent`) and `world_3d.tscn` (`MapConfig`, terrain tiles).

### wrap.rs

Torus math: `wrap_world_pos` / `wrap_chunk_coords` + wrapped distance. The continuous world is a torus; the hex grid is not. Used by chest, enemy, player, status, and admin code — every cross-seam comparison in the module goes through here (and the client's `TorusMath` mirrors it).

## Cross-table flows

Generation is the module's spanning flow: defs + rules (inputs) → `run_generation` (pipeline) → regions/tiles/decor (outputs) + elevation stamped back onto the grid. Gameplay then reads three different projections: sim/grid (`MapConfig` + `BuildingTile`), spawn (`BiomeRegion`), render (`TriangleTile` + `HexDecor` + `TextureEntry`). Each link lives on its own entry above.

## Known gaps / stubs

- **`BuildingTile` is `public` but the client never subscribes to the raw table** — buildings aren't a designed feature yet, so the table is write-only world authoring for now. (Clients read elevation through the `nearby_building_tiles` view, not the raw table.)
- **Region weights only gate validation, not area share.** `BIOME_VORONOI_SEED_BUDGET` splits biome seeds proportionally by weight, but per-biome `region_weights` are validated to sum to 100 without driving area — a region entry with a missing def is skipped draw-free.

## Where to go next

This is the last module doc. From here: `00 Table Map.md` for any table's changer/reader reference, `03 Connection, Subscriptions & Views` for the read path, or the flowchart aggregate [[flowcharts/main.canvas]] for the visual overview.
