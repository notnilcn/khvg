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

#[table(accessor = biome_def, public)]
pub struct BiomeDef {
    #[primary_key]
    pub biome_id: String,
    pub display_name: String,
    pub texture_ids: Vec<String>,
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
    pub texture_ids: Vec<String>,
    pub regions: Vec<ManualRegionInput>,
}
