use spacetimedb::{table, Identity, SpacetimeType};

#[derive(SpacetimeType, Clone)]
pub enum TextureKind {
    Player,
    Enemy,
    Bullet,
    Item,
    Environment,
}

#[table(accessor = map_config, public)]
pub struct MapConfig {
    #[primary_key]
    pub id: u32,
    pub chunk_hex_radius: i32,
    pub chunk_cols: i32,
    pub chunk_rows: i32,
    pub hex_outer_radius: f32,
}

#[table(accessor = building_tile, public)]
pub struct BuildingTile {
    #[primary_key] #[auto_inc]
    pub tile_id: u64,
    #[index(btree)]
    pub hex_q: i32,
    pub hex_r: i32,
    pub chunk_q: i32,
    pub chunk_r: i32,
    pub local_index: i32,
    #[index(btree)]
    pub chunk_index: i64,
    pub building_type: String,
    pub owner_id: Option<Identity>,
}

#[table(accessor = texture_entry, public)]
pub struct TextureEntry {
    #[primary_key]
    pub texture_id: String,
    pub res_path: String,
    pub kind: TextureKind,
}

#[table(accessor = item_stats, public)]
pub struct ItemStats {
    #[primary_key]
    pub item_id: String,
    pub texture_id: String,
    pub item_type: u8,
    pub fire_rate: u8,
    pub base_damage: u16,
    pub str_damage_add: i32,
    pub str_damage_mult: f32,
    pub wis_damage_add: i32,
    pub wis_damage_mult: f32,
    pub dex_damage_add: i32,
    pub dex_damage_mult: f32,
    pub def_damage_add: i32,
    pub def_damage_mult: f32,
    pub vit_damage_add: i32,
    pub vit_damage_mult: f32,
    pub spd_damage_add: i32,
    pub spd_damage_mult: f32,
    pub str_bonus_add: u16,
    pub str_bonus_mult: f32,
    pub wis_bonus_add: u16,
    pub wis_bonus_mult: f32,
    pub dex_bonus_add: u16,
    pub dex_bonus_mult: f32,
    pub def_bonus_add: u16,
    pub def_bonus_mult: f32,
    pub vit_bonus_add: u16,
    pub vit_bonus_mult: f32,
    pub spd_bonus_add: u16,
    pub spd_bonus_mult: f32,
}
