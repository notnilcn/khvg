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
    // None when no overlay was rolled for this triangle. overlay_rotation/overlay_scale are
    // unused when None. overlay_scale is copied from the biome's OverlayTextureConfig at
    // generation time so the client can render it without joining back through BiomeDef.
    pub overlay_texture_id: Option<String>,
    pub overlay_rotation: u8,
    pub overlay_scale: f32,
    pub biome_id: String,
    pub region_id: u64,
}

// One decor prop per hex, at most — separation rules mean two decor can never be close enough
// for multiple-per-hex to come up. Unlike TriangleTile, a hex with no decor simply has no row
// here rather than a blank placeholder, since decor is optional content, not a required fill.
#[table(accessor = hex_decor, public)]
pub struct HexDecor {
    #[primary_key] #[auto_inc]
    pub decor_id: u64,
    pub hex_q: i32,
    pub hex_r: i32,
    #[index(btree)]
    pub chunk_index: i64,
    pub texture_id: String,
    pub shadow_texture_id: String,
    pub rotation_degrees: f32,
    pub biome_id: String,
    pub region_id: u64,
}
