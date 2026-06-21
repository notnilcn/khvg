use spacetimedb::{table, SpacetimeType};

#[derive(SpacetimeType, Clone)]
pub struct BiomeWeight {
    pub biome_id: String,
    pub weight: f32,
}

#[table(accessor = world_def, public)]
pub struct WorldDef {
    #[primary_key]
    pub world_id: String,
    pub display_name: String,
    pub biome_weights: Vec<BiomeWeight>,
}

#[derive(SpacetimeType, Clone)]
pub struct RegionWeight {
    pub region_def_id: String,
    pub weight: f32,
}

// A texture's role (ground vs overlay), tags, and rotation rules are all per-biome, not
// intrinsic to the texture itself (TextureEntry) — the same texture_id can be ground in one
// biome and an overlay in another, with different tags/rotations/chance each time.
//
// `chance` is relative frequency among the other ground entries in this biome — not required to
// sum to 100, just weighted-random.
#[derive(SpacetimeType, Clone)]
pub struct GroundTextureConfig {
    pub texture_id: String,
    pub chance: f32,
    // Compatibility tags (e.g. "soft", "delicate"). Consumed by LayeringRule to decide which
    // overlays are allowed on this ground texture, in this biome.
    pub tags: Vec<String>,
    // Which of the 3 triangle rotations (0/1/2) this texture looks correct at, in this biome.
    // Empty = all allowed. Base and overlay each pick their rotation independently.
    pub allowed_rotations: Vec<u8>,
}

// An overlay's chance/scale/tags/rotations are per-biome (not on the texture itself), since the
// same texture can be an overlay in one biome and a ground texture in another, and look/behave
// differently in each. `chance` is a percent (0-100) of landing on a tile whose chosen ground
// texture this overlay is compatible with (LayeringRule, via `tags`) — chances among the
// compatible overlays for a given tile don't need to sum to 100; any chance left over after
// summing is "no overlay", and if they sum past 100 they're scaled down proportionally rather
// than overflowing (see weighted_pick_with_remainder in world::methods).
#[derive(SpacetimeType, Clone)]
pub struct OverlayTextureConfig {
    pub texture_id: String,
    pub chance: f32,
    pub scale: f32,
    pub tags: Vec<String>,
    pub allowed_rotations: Vec<u8>,
}

#[table(accessor = biome_def, public)]
pub struct BiomeDef {
    #[primary_key]
    pub biome_id: String,
    pub display_name: String,
    pub ground_textures: Vec<GroundTextureConfig>,
    pub overlay_textures: Vec<OverlayTextureConfig>,
    pub region_weights: Vec<RegionWeight>,
}

#[table(accessor = biome_region_def, public)]
pub struct BiomeRegionDef {
    #[primary_key]
    pub region_def_id: String,
    pub display_name: String,
    pub enemy_template_ids: Vec<String>,
    pub max_enemies: u32,
    pub spawn_radius: f32,
}

#[derive(SpacetimeType, Clone)]
pub struct ManualRegionInput {
    pub region_id: String,
    pub center_hex_q: i32,
    pub center_hex_r: i32,
    pub enemy_template_ids: Vec<String>,
    pub max_enemies: u32,
    pub spawn_radius: f32,
}

#[derive(SpacetimeType, Clone)]
pub struct ManualBiomeInput {
    pub biome_id: String,
    pub center_hex_q: i32,
    pub center_hex_r: i32,
    pub ground_textures: Vec<GroundTextureConfig>,
    pub overlay_textures: Vec<OverlayTextureConfig>,
    pub regions: Vec<ManualRegionInput>,
}
