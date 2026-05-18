use spacetimedb::{table, SpacetimeType, Identity, Timestamp};

// ── Inventory types ──────────────────────────────────────────────────────────

#[derive(SpacetimeType, Clone)]
pub enum SlotKind {
    Weapon,
    Ability,
    Armor,
    Accessory,
    Backpack,
    Consumable,
}

#[derive(SpacetimeType, Clone)]
pub struct InventorySlot {
    pub kind: SlotKind,
    pub index: u8,       // position within kind (e.g. 0–2 for accessories, 0–11 for backpack)
    pub item_id: String, // lookup key into ItemStats
}

// ── Enemy template types ─────────────────────────────────────────────────────

#[derive(SpacetimeType, Clone)]
pub enum PatternType {
    Straight,
    Sine,
    Homing,
    Ring,
    Spiral,
    Burst,
}

#[derive(SpacetimeType, Clone)]
pub struct AttackDef {
    pub pattern_type: PatternType,
    pub target: EnemyTarget,
    pub fire_rate: f32, // seconds between shots
    pub params: String, // pattern-specific params: speed, amplitude, count, spread, etc.
}

#[derive(SpacetimeType, Clone)]
pub struct PhaseDef {
    pub phase_index: u8,
    pub hp_threshold: f32, // 0.0–1.0; enter this phase when hp% drops below value
    pub attacks: Vec<AttackDef>,
}

// ── Player tables ────────────────────────────────────────────────────────────

#[table(accessor = logged_in_player, public)]
#[table(accessor = logged_out_player)]
pub struct Player {
    #[primary_key]
    pub player_id: Identity,
    pub username: String,
    pub is_admin: bool,
}

#[table(accessor = player_data, public)]
pub struct PlayerData {
    #[primary_key]
    pub player_id: Identity,
    pub level: u32,
    pub xp: u32,
    pub hp: u32,
    pub max_hp: u32,
}

#[table(accessor = player_stats, public)]
pub struct PlayerStats {
    #[primary_key]
    pub player_id: Identity,
    pub strength: i32,
    pub wisdom: i32,
    pub dexterity: i32,
    pub defense: i32,
    pub vitality: i32,
    pub speed: i32,
}

#[table(accessor = player_inventory, public)]
pub struct PlayerInventory {
    #[primary_key]
    pub player_id: Identity,
    pub slots: Vec<InventorySlot>,
}

// ── Player session tables ────────────────────────────────────────────────────

#[table(accessor = player_position, public)]
pub struct PlayerPosition {
    #[primary_key]
    pub player_id: Identity,
    pub x: f32,
    pub y: f32,
    pub rotation: f32,
    #[index(btree)]
    pub chunk_index: i64,
}

// ── Enemy types ──────────────────────────────────────────────────────────────

#[derive(SpacetimeType, Clone)]
pub enum EnemyTarget {
    Idle,
    NearestPlayer,
    NearestCluster
}

// ── Enemy tables ─────────────────────────────────────────────────────────────

// Reference data — admin uploads once per enemy type. Clients subscribe to drive simulation.
#[table(accessor = enemy_template, public)]
pub struct EnemyTemplate {
    #[primary_key]
    pub template_id: String, // e.g. "goblin", "necromancer_boss"
    pub display_name: String,
    pub max_hp: u32,
    pub phases: Vec<PhaseDef>,
}

// Live instances — session lifetime. Insert = spawn event. Delete = death event.
// Clients simulate position/AI/bullets deterministically from spawn_x/y at spawn_time.
#[table(accessor = enemy, public)]
pub struct Enemy {
    #[primary_key] #[auto_inc] pub enemy_id: u64,
    pub template_id: String, // FK to EnemyTemplate
    pub res_path: String,    // Godot scene path; client loads visuals and AI script from here
    pub spawn_x: f32,
    pub spawn_y: f32,
    pub x: f32,
    pub y: f32,
    pub spawn_time: Timestamp,
    pub last_attack_time: Timestamp,
    pub hp: u32,
    pub phase: u8,
    pub is_elite: bool,
    pub immortal: bool,
    #[index(btree)] pub chunk_index: i64,
}

// ── Loot table ───────────────────────────────────────────────────────────────

#[table(accessor = loot_drop, public)]
pub struct LootDrop {
    #[primary_key] #[auto_inc] pub drop_id: u64,
    pub item_id: String, // FK to ItemStats
    pub x: f32,
    pub y: f32,
    #[index(btree)] pub chunk_index: i64,
    pub expires_at: Timestamp,
}

// ── Bullet pattern event table ───────────────────────────────────────────────

#[table(accessor = bullet_pattern_event, public, event)]
pub struct BulletPatternEvent {
    #[primary_key] #[auto_inc] pub event_id: u64,
    pub enemy_id: u64,
    pub origin_x: f32,
    pub origin_y: f32,
    pub pattern_type: PatternType,
    pub targets: Vec<Identity>,
    pub params: String,
    #[index(btree)] pub chunk_index: i64,
}

// ── Building tile table ──────────────────────────────────────────────────────

// Persistent hex tile grid for player-built structures.
// Pre-populated by generate_map; tiles start with building_type = "" (empty).
// chunk_index is used for AoI subscriptions: WHERE chunk_index IN (...).
#[table(accessor = building_tile, public)]
pub struct BuildingTile {
    #[primary_key]
    #[auto_inc]
    pub tile_id: u64,
    #[index(btree)]
    pub hex_q: i32,              // small hex axial q (cube x)
    pub hex_r: i32,              // small hex axial r (cube z)
    pub chunk_q: i32,            // chunk axial cq (big hex cube x)
    pub chunk_r: i32,            // chunk axial cr (big hex cube z)
    pub local_index: i32,        // hexmod value ∈ [0, area)
    #[index(btree)]
    pub chunk_index: i64,        // flat chunk index: cr * chunk_cols + cq
    pub building_type: String,   // empty = unoccupied
    pub owner_id: Option<Identity>,
}

// ── Map configuration (singleton) ────────────────────────────────────────────

// Written by add_chunks; read by report_movement and spawn_enemy so that all
// chunk and world-position computations use the same parameters the map was generated with.
#[table(accessor = map_config, public)]
pub struct MapConfig {
    #[primary_key]
    pub id: u32, // always 0
    pub chunk_hex_radius: i32,
    pub chunk_cols: i32,
    pub chunk_rows: i32,
    pub hex_outer_radius: f32,
}

// ── Item reference table ─────────────────────────────────────────────────────

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
