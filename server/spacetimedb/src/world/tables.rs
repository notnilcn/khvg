use spacetimedb::{table, Identity, SpacetimeType};

#[derive(SpacetimeType, Clone)]
pub enum TextureKind {
    Player,
    Enemy,
    Projectile,
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
    // World-space displacement of one full torus lap in each axis — same vectors wrap_world_pos
    // uses internally. Exposed so clients can pick the correct wrapped copy of distant tiles/
    // entities when rendering near a chunk-grid seam, without re-deriving the hex-of-hexes math.
    pub lap_q_x: f32,
    pub lap_q_y: f32,
    pub lap_r_x: f32,
    pub lap_r_y: f32,
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

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum WeaponPattern {
    Single,
    Triple,
    Cluster,
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ItemType {
    Weapon,
    Artifact,
    Armor,
    Accessory,
    Bag,
    Consumable,
}

#[table(accessor = item, public)]
pub struct Item {
    #[primary_key]
    pub item_id: String,
    pub display_name: String,
    pub description: String,
    pub texture_id: String,
    pub item_type: ItemType,
}

#[table(accessor = weapon, public)]
pub struct Weapon {
    #[primary_key]
    pub weapon_id: String,
    pub damage: u32,
    pub range: f32,
    pub fire_rate: f32,
    pub projectile_speed: f32,
    pub shot_count: u32,
    pub zone_count: u32,
    pub pierce: bool,
    pub projectile_texture_id: String,
    pub pattern: WeaponPattern,
    pub spread_angle: f32,
}

#[table(accessor = armor, public)]
pub struct Armor {
    #[primary_key]
    pub armor_id: String,
    pub defense_bonus: i32,
    pub vitality_bonus: i32,
    pub hp_bonus: u32,
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ArtifactEffect {
    Strength(f32),
    Wisdom(f32),
    Dexterity(f32),
    Defense(f32),
    Vitality(f32),
    Speed(f32),
}

#[table(accessor = artifact, public)]
pub struct Artifact {
    #[primary_key]
    pub artifact_id: String,
    pub effect: ArtifactEffect,
}

#[table(accessor = accessory, public)]
pub struct Accessory {
    #[primary_key]
    pub accessory_id: String,
    pub strength_bonus: i32,
    pub wisdom_bonus: i32,
    pub dexterity_bonus: i32,
    pub speed_bonus: i32,
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ConsumableBuffEffect {
    Strength(f32),
    Wisdom(f32),
    Dexterity(f32),
    Defense(f32),
    Vitality(f32),
    Speed(f32),
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ConsumableEffect {
    Heal,
    Buff(ConsumableBuffEffect),
}

#[table(accessor = consumable, public)]
pub struct Consumable {
    #[primary_key]
    pub consumable_id: String,
    pub effect: ConsumableEffect,
    pub potency: f32,
    pub duration: f32,
}
