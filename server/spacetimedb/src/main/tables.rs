use spacetimedb::{table, SpacetimeType, Identity, Timestamp};

#[derive(SpacetimeType, Clone)]
pub struct RpgStats {
    pub strength: i32,
    pub wisdom: i32,
    pub dexterity: i32,
    pub defense: i32,
    pub vitality: i32,
    pub speed: i32,
}

#[derive(SpacetimeType, Clone)]
pub struct EnemySnapshot {
    pub enemy_id: u64,
    pub x: f32,
    pub y: f32,
    pub move_x: i8,
    pub move_y: i8,
    pub speed: u16,
}

#[table(accessor = player_account, public)]
pub struct PlayerAccount {
    #[primary_key]
    pub player_id: Identity,
    pub username: String,
    pub is_admin: bool,
    pub created_at: Timestamp,
}

#[table(accessor = character_data, public)]
pub struct CharacterData {
    #[primary_key]
    pub player_id: Identity,
    pub level: u32,
    pub xp: u32,
    pub max_hp: u32,
    pub current_hp: u32,
    pub stats: RpgStats,
}

#[table(accessor = player_inventory, public)]
pub struct PlayerInventory {
    #[primary_key]
    pub player_id: Identity,
    pub equipped_weapon: Option<String>,
    pub equipped_ability: Option<String>,
    pub equipped_armor: Option<String>,
    pub equipped_accessories: Vec<String>,
    pub backpack_slots: Vec<String>,
    pub consumable_slots: Vec<String>,
}

#[table(accessor = player_position, public)]
pub struct PlayerPosition {
    #[primary_key]
    pub player_id: Identity,
    pub x: f32,
    pub y: f32,
    pub move_x: i8,
    pub move_y: i8,
    pub speed: u16,
    pub event_time: Timestamp,
    pub chunk_x: i32,
    pub chunk_y: i32,
}

#[table(accessor = elevated_client, public)]
pub struct ElevatedClient {
    #[primary_key]
    pub identity: Identity,
    pub claimed_at: Timestamp,
}

#[table(accessor = bullet_pattern, public)]
pub struct BulletPattern {
    #[primary_key]
    pub slot_id: u16,
    pub pattern_type: u8,
    pub origin_x: f32,
    pub origin_y: f32,
    pub target_x: f32,
    pub target_y: f32,
    pub spawn_time: Timestamp,
    pub despawn_time: Timestamp,
}

#[table(accessor = enemy_state, public)]
pub struct EnemyState {
    #[primary_key]
    pub enemy_id: u64,
    pub x: f32,
    pub y: f32,
    pub move_x: i8,
    pub move_y: i8,
    pub speed: u16,
    pub event_time: Timestamp,
    pub res_path: String,
    pub is_elite: bool,
    pub immortal: bool,
    pub hp: u32,
    pub target_id: Option<String>,
    pub bullet_pattern_type: u8,
}

#[table(accessor = item_stats, public)]
pub struct ItemStats {
    #[primary_key]
    pub item_id: String,
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
