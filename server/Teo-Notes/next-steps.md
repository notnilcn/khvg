# Next Steps

## 7. World Generation — Tables, Structs & Pipelines

### Overview

Two generation pipelines sharing the same output tables:
- **Proc gen** (`generate_world_proc`) — reads world config tables, randomized but rule-driven, different output each run with the same world config
- **Manual gen** (`generate_world_manual`) — takes all input as struct params, no config tables read, predictable and fully controlled output

Both pipelines clear and repopulate `BiomeRegion` and `TriangleTile` before running. `init` calls one of them — swap which one when switching pipelines.

---

### Config Tables (proc gen only — seeded via admin reducers, never regenerated)

**`WorldDef`** — `world_id: String` (pk), `display_name: String`

**`BiomeDef`** — `biome_id: String` (pk), `world_id`, `display_name`, `coverage_weight: f32` (must sum to 100% across biomes in same world), `texture_ids: Vec<String>`, `max_total_enemies: u32`

**`BiomeRegionDef`** — `region_def_id: String` (pk), `biome_id`, `display_name`, `coverage_weight: f32` (must sum to 100% across region defs in same biome), `enemy_template_ids: Vec<String>`, `max_enemies: u32`, `spawn_radius: f32`

---

### Generated Output Tables (written by both pipelines)

**`BiomeRegion`** — `region_id: u64` (pk auto_inc), `biome_id: String`, `center_hex_q: i32`, `center_hex_r: i32`, `enemy_template_ids: Vec<String>`, `max_enemies: u32`, `spawn_radius: f32`
- One row per placed region seed
- Used by enemy spawning (`spawn_from_biome_regions`) — replaces `EnemyZone`

**`TriangleTile`** — `tile_id: u64` (pk auto_inc), `hex_q: i32`, `hex_r: i32`, `tri_index: u8` (0–5), `chunk_index: i64` (btree, for AoI subscriptions), `texture_id: String`, `rotation: u8` (0 / 1 / 2 = 0° / 120° / 240°), `biome_id: String`, `region_id: u64`
- 6 rows per hex, one per triangle — the only world data clients subscribe to

---

### Manual Input Structs (`#[derive(SpacetimeType)]` — never tables)

```rust
pub struct ManualRegionInput {
    pub region_id: String,
    pub center_hex_q: i32,
    pub center_hex_r: i32,
    pub texture_ids: Vec<String>,
    pub enemy_template_ids: Vec<String>,
    pub max_enemies: u32,
    pub spawn_radius: f32,
}

pub struct ManualBiomeInput {
    pub biome_id: String,
    pub center_hex_q: i32,
    pub center_hex_r: i32,
    pub texture_ids: Vec<String>,
    pub regions: Vec<ManualRegionInput>,
}
```

---

### Reducer Signatures

```rust
generate_world_proc(ctx, world_id: String, seed: u64) -> Result<(), String>
generate_world_manual(ctx, biomes: Vec<ManualBiomeInput>) -> Result<(), String>
```

---

### Generation Flow

**Proc gen:**
1. Load `BiomeDef` rows for `world_id` — validate coverage weights sum to 100%
2. Load `BiomeRegionDef` rows per biome — validate per-biome coverage weights sum to 100%
3. Place biome Voronoi seeds in memory (positions randomized, weighted by `coverage_weight`)
4. World-level Voronoi → every hex assigned a `biome_id`
5. Per biome: place region seeds in memory (weighted random positions within that biome's hexes)
6. Sub-Voronoi within biome hexes → every hex assigned a region
7. Write `BiomeRegion` rows (one per placed seed, center_hex = seed position, rules from `BiomeRegionDef`)
8. Per hex × 6 triangles: pick random texture from region's `texture_ids`, random rotation → write `TriangleTile`

**Manual gen:**
1. Take `Vec<ManualBiomeInput>` params — no table reads
2. World-level Voronoi from biome `center_hex` coords
3. Per biome: sub-Voronoi from region `center_hex` coords within that biome's hexes
4. Write `BiomeRegion` rows
5. Same triangle tile gen as proc gen step 8

---

### Enemy Spawning Migration

- Delete `EnemyZone` table
- Rename `spawn_from_zones` → `spawn_from_biome_regions`, iterate `BiomeRegion` instead
- `BiomeRegion` already carries all the fields `EnemyZone` had

---

## 8. Texture Assignment Rules (Next Step After Core Gen)

Current gen picks textures uniformly at random from the region's `texture_ids`. To produce natural-looking terrain, rules need to be layered on top. No new tables required — add rule fields to existing def tables and manual structs; the gen algorithm gains a texture-selection pass that consults them before writing each `TriangleTile`.

### Planned rule types

- **Frequency weights** — parallel `texture_weights: Vec<f32>` alongside `texture_ids` on `BiomeDef`, `BiomeRegionDef`, and the manual input structs. Controls how often each texture is chosen (e.g. grass 60%, flower 20%, rock 20%).

- **Allowed rotations per texture** — parallel `texture_allowed_rotations: Vec<Vec<u8>>` (indexed to `texture_ids`). Some textures don't look right at all three rotations — this restricts which values 0/1/2 are valid for each.

- **Noise-based distribution** — use simplex noise seeded per-region (with `seed` from `generate_world_proc`) to cluster textures spatially rather than scatter them randomly. The noise value at each triangle's world position selects a texture from the weighted list, so similar textures group together naturally.

- **Biome border textures** — triangles whose hex is adjacent to a different biome get a restricted texture set (the 1/3 and 2/3 coverage triangle shapes designed for biome edges). Interior triangles use full-coverage textures. Requires a neighbor-biome lookup pass during gen.

- **Adjacency rules / tags** — tag textures (e.g. `"ground"`, `"feature"`, `"border"`) and define which tags can appear adjacent. Prevents unnatural clustering (e.g. two feature textures side by side). Requires a second pass over already-placed triangles during gen.
