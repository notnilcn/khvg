use spacetimedb::{table, SpacetimeType, Identity, Timestamp, ScheduleAt};
use crate::item::tables::StatModifier;
use super::reducers::tick_status_effects;

// What a slot is for. Replaces the old "equipped iff allowed_slots.len() == 1" inference —
// the acceptance set for a slot is derived from its role (see slot_allowed in methods.rs).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum SlotRole { Weapon, Hotbar, Accessory, Armor, General, Bag, Ability }

// One row per inventory slot (was: one Vec<InventorySlot> inside PlayerInventory, cloned and
// rewritten wholesale by eight reducers). Per-slot rows give ability slots a home for runtime
// state — cooldowns, charges, toggle index, multi-slot span occupancy — that has no business
// being replicated as part of a value type on every swap.
#[table(accessor = player_inventory_slot, public, index(accessor = by_profile_slot, btree(columns = [profile_id, slot_index])))]
#[derive(Clone)]
pub struct PlayerInventorySlot {
    #[primary_key] #[auto_inc]
    pub slot_id: u64,
    #[index(btree)]
    pub profile_id: u64,
    pub slot_index: u32,
    pub role: SlotRole,
    pub item_id: Option<String>,
    pub enchantment_ids: Vec<String>,
    pub cooldown_until: Option<Timestamp>,
    // Remaining activations for charge-based abilities; u32::MAX = unlimited.
    pub charges: u32,
    // Weapon/ability mode index (weapon shot-pattern toggles); 0 = default.
    pub active_toggle: u32,
    // Some(head_slot_index) marks a follower cell of a multi-slot span; None on heads and
    // ordinary slots. Followers never hold item_id themselves.
    pub occupied_by: Option<u32>,
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
    // Resolved gear/buff defence (flat per-bullet subtraction, doc 02) — lives here
    // rather than in PlayerStats for the same reason max_hp does: it's modifier-only,
    // not one of the six allocatable stats.
    pub defense: i32,
    // Resolved move speed cap (world units/sec) over BASE_SPEED — modifier-only like
    // defense: items raise it via StatKind::BaseSpeed, it is never allocatable.
    // report_movement clamps reported velocity to this.
    pub base_speed: f32,
}

// The six allocatable stats (doc 03): three attributes + three temperaments, resolved
// by recompute_stats into (base + allocated + gear_flat) * (1 + mult). Hp/Defense
// modifiers resolve into PlayerData instead and have no field here.
#[table(accessor = player_stats, public)]
pub struct PlayerStats {
    #[primary_key]
    pub profile_id: u64,
    pub strength: i32,
    pub wisdom: i32,
    pub dexterity: i32,
    pub damage_dealer: i32,
    pub supporter: i32,
    pub artisan: i32,
}

// The allocation *input* behind PlayerStats: SKILL_POINTS_PER_LEVEL unspent points per
// level, distributed by the allocate_stat reducer. recompute_stats folds these on top
// of the flat base stats.
#[table(accessor = player_stat_allocation, public)]
pub struct PlayerStatAllocation {
    #[primary_key]
    pub profile_id: u64,
    pub strength: u32,
    pub wisdom: u32,
    pub dexterity: u32,
    pub damage_dealer: u32,
    pub supporter: u32,
    pub artisan: u32,
    pub unspent_points: u32,
}

#[table(accessor = player_position, public)]
pub struct PlayerPosition {
    #[primary_key]
    pub profile_id: u64,
    pub player_id: Identity,
    pub x: f32,
    pub y: f32,
    // Movement as an angle + scalar speed instead of a velocity vector — clients
    // reconstruct velocity = direction * speed for dead reckoning. movement_speed is the
    // player's current speed (0 when idle), clamped server-side to the resolved
    // base_speed; movement_direction keeps its last value while idle.
    pub movement_direction: f32,
    pub movement_speed: f32,
    #[index(btree)]
    pub chunk_index: i64,
}

// Screen (camera/facing) rotation, split out of PlayerPosition so it can be reported on
// its own (faster) cadence — camera rotation needs more frequent updates than the 10Hz
// movement report. chunk_index mirrors the position row so AOI views filter identically.
#[table(accessor = player_rotation, public)]
pub struct PlayerRotation {
    #[primary_key]
    pub profile_id: u64,
    pub player_id: Identity,
    pub screen_rotation: f32,
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
    pub modifier: StatModifier,
    pub remaining: f32,
}

#[table(accessor = consumable_effect_schedule, scheduled(tick_status_effects))]
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
    pub enchantment_ids: Vec<String>,
}
