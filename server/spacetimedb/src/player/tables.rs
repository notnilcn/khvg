use spacetimedb::{table, SpacetimeType, Identity, Timestamp, ScheduleAt};
use crate::world::tables::{ItemType, ConsumableBuffEffect};
use super::reducers::tick_active_consumable_effects;

#[derive(SpacetimeType, Clone)]
pub struct InventorySlot {
    pub allowed_types: Vec<ItemType>,
    pub item_id: Option<String>,
}

#[table(accessor = logged_in_player, public)]
pub struct LoggedInPlayer {
    #[primary_key]
    pub player_id: Identity,
    pub username: String,
    pub is_admin: bool,
    #[unique]
    pub profile_id: u64,
}

#[table(accessor = logged_out_player)]
pub struct LoggedOutPlayer {
    #[primary_key]
    pub player_id: Identity,
    pub username: String,
    pub is_admin: bool,
}

#[table(accessor = player_profile, public, index(accessor = by_player_profile_name, btree(columns = [player_id, profile_name])))]
pub struct PlayerProfile {
    #[primary_key] #[auto_inc]
    pub profile_id: u64,
    #[index(btree)]
    pub player_id: Identity,
    pub profile_name: String,
    pub texture_id: String,
    pub aim_assist: bool,
    pub lock_on: bool,
}

#[table(accessor = player_data, public)]
pub struct PlayerData {
    #[primary_key]
    pub profile_id: u64,
    pub level: u32,
    pub xp: u32,
    pub hp: u32,
    pub max_hp: u32,
}

#[table(accessor = player_stats, public)]
pub struct PlayerStats {
    #[primary_key]
    pub profile_id: u64,
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
    pub profile_id: u64,
    pub slots: Vec<InventorySlot>,
}

#[table(accessor = player_position, public)]
pub struct PlayerPosition {
    #[primary_key]
    pub profile_id: u64,
    pub player_id: Identity,
    pub x: f32,
    pub y: f32,
    pub rotation: f32,
    #[index(btree)]
    pub chunk_index: i64,
}

// Written only when the player's chunk actually changes (unlike PlayerPosition, which updates
// every report_movement call at 10Hz) — views that key off this instead of PlayerPosition only
// recompute on real chunk crossings, not on every movement tick.
#[table(accessor = player_chunk, public)]
pub struct PlayerChunk {
    #[primary_key]
    pub profile_id: u64,
    pub chunk_q: i32,
    pub chunk_r: i32,
}

#[table(accessor = active_consumable_effect, public)]
pub struct ActiveConsumableEffect {
    #[primary_key] #[auto_inc]
    pub effect_id: u64,
    #[index(btree)]
    pub profile_id: u64,
    pub effect: ConsumableBuffEffect,
    pub remaining: f32,
}

#[table(accessor = consumable_effect_schedule, scheduled(tick_active_consumable_effects))]
pub struct ConsumableEffectSchedule {
    #[primary_key] #[auto_inc]
    pub scheduled_id: u64,
    pub scheduled_at: ScheduleAt,
}

#[table(accessor = loot_drop, public)]
pub struct LootDrop {
    #[primary_key] #[auto_inc]
    pub drop_id: u64,
    pub item_id: String,
    pub x: f32,
    pub y: f32,
    #[index(btree)]
    pub chunk_index: i64,
    pub expires_at: Timestamp,
    pub dropped_by: Option<Identity>,
}
