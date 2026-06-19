use spacetimedb::table;

#[table(accessor = biome_region, public)]
pub struct BiomeRegion {
    #[primary_key] #[auto_inc]
    pub region_id: u64,
    pub biome_id: String,
    pub center_hex_q: i32,
    pub center_hex_r: i32,
    pub enemy_template_ids: Vec<String>,
    pub max_enemies: u32,
    pub spawn_radius: f32,
}

#[table(accessor = triangle_tile, public)]
pub struct TriangleTile {
    #[primary_key] #[auto_inc]
    pub tile_id: u64,
    pub hex_q: i32,
    pub hex_r: i32,
    pub tri_index: u8,
    #[index(btree)]
    pub chunk_index: i64,
    pub texture_id: String,
    pub rotation: u8,
    pub biome_id: String,
    pub region_id: u64,
}
