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
    // Runtime state carried from the dropped slot (ability cooldown/charges travel with
    // the item — drop_item stamps them, pickup_drop restores them). None/u32::MAX = none.
    pub cooldown_until: Option<Timestamp>,
    pub charges: u32,
}

// Server-only movement-report bookkeeping behind report_movement's displacement clamp:
// when the last report arrived, so the per-report budget is base_speed × dt × MOVE_SLACK.
// falling_until locks movement while a downhill-fall tween plays out (see report_movement).
// Not public — clients never read it. teleport_player restamps it (teleports are exempt
// from the clamp); teardown_profile deletes it.
#[table(accessor = player_movement_state)]
pub struct PlayerMovementState {
    #[primary_key]
    pub profile_id: u64,
    pub last_report: Timestamp,
    // While in the future, report_movement ignores position claims — the client is
    // tweening a downhill fall and movement is locked. None = not falling.
    pub falling_until: Option<Timestamp>,
}

/// Server-only Grapple coordination (P4): Grapple's pull is client-tweened, so a cast
/// stamps one allowance row here (max distance = the scaled grapple range, short expiry).
/// report_movement's displacement clamp accepts ONE oversized report up to max_distance
/// while the row lives and consumes it — without this the clamp would rubber-band every
/// legit grapple. Deleted on use, on expiry contact, and by teardown_profile.
#[table(accessor = player_displacement_allowance)]
pub struct PlayerDisplacementAllowance {
    #[primary_key]
    pub profile_id: u64,
    pub max_distance: f32,
    pub expires_at: Timestamp,
}

/// Server-only token bucket behind the two self-reported hit reducers (one 1-second
/// fixed window per profile): `outgoing` caps report_enemy_hit at weapon fire_rate ×
/// effective shot_count × HIT_RATE_SLACK, `incoming` caps report_hit at
/// INCOMING_HIT_RATE_CAP. Bounds hit-spam and StoreIncoming farming without touching
/// damage math. See hit_rate_allow in player/methods.rs.
#[table(accessor = player_hit_rate)]
pub struct PlayerHitRate {
    #[primary_key]
    pub profile_id: u64,
    pub window_start: Timestamp,
    pub outgoing: u32,
    pub incoming: u32,
}

/// Discovery / knowledge gating (Phase 1 — BitCraft `knowledge_secondary_state`
/// pattern, translated). One row per profile: `discovered` fills automatically on
/// pickup/observe, `acquired` fills via the explicit unlock reducer. Unknown items
/// render as ??? client-side until discovered.
#[table(accessor = player_knowledge, public)]
#[derive(Clone)]
pub struct PlayerKnowledge {
    #[primary_key]
    pub profile_id: u64,
    pub discovered: Vec<String>,
    pub acquired: Vec<String>,
}

/// Survival sim state (Phase 2 — BitCraft `player_regen`/`starving` pattern).
/// One row per profile, stamped on every hit via `intercept_incoming` and read by
/// the regen pass: regen only applies after REGEN_DELAY_SECONDS without damage;
/// `attrition_ticks` counts consecutive ticks without relief — past
/// ATTRITION_GRACE_TICKS the pressure row deals ATTRITION_DAMAGE_PER_TICK via the
/// shared damage sink (never direct HP writes). Skipped when logged out (rows are
/// only ticked for `LoggedInPlayer` profiles).
#[table(accessor = player_survival)]
#[derive(Clone)]
pub struct PlayerSurvival {
    #[primary_key]
    pub profile_id: u64,
    pub last_damage_at: Option<Timestamp>,
    pub attrition_ticks: u32,
    pub last_regen_at: Option<Timestamp>,
}

/// One hex of a revealed chunk's render snapshot. Mirrors the `HexTile` fields the client's
/// fog/map layer needs, plus `height` and `smooth` from the hex's `BuildingTile` row (height 0
/// / smooth false when absent).
#[derive(SpacetimeType, Clone)]
pub struct ExploredHex {
    pub hex_q: i32,
    pub hex_r: i32,
    pub texture_id: String,
    pub rotation: u8,
    pub overlay_texture_id: Option<String>,
    pub overlay_rotation: u8,
    pub overlay_scale: f32,
    pub height: i16,
    pub smooth: bool,
    pub river_mask: u8,
    pub road_mask: u8,
    pub wall_mask: u8,
}

/// Per-profile explored map (PLAN §4.2). Reveal granularity is the terrain AOI disk: the
/// player's chunk crossing reveals `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS` chunk rings around the
/// destination chunk at once, never a single hex or single chunk. Rows stay per chunk, so
/// overlapping AOI disks do not duplicate payloads — a one-chunk move inserts only the newly
/// entered outer ring (set-diffed against the profile's own rows, ≤ 19 today — a 2-ring disk).
///
/// `hexes` is a denormalized snapshot of the chunk's `HexTile` rows joined with
/// `BuildingTile.height`, copied once at reveal time. World generation is static after
/// `internal_generate_world`, so snapshots can never go stale — that is what lets distant
/// explored chunks render without widening the `nearby_terrain_tiles` subscription. (An
/// admin regeneration is the one exception: it rewrites the live rows but leaves the explored
/// snapshots as the record of what that player saw.)
#[table(accessor = player_explored_chunk, public)]
#[derive(Clone)]
pub struct PlayerExploredChunk {
    #[primary_key] #[auto_inc]
    pub explored_id: u64,
    #[index(btree)]
    pub profile_id: u64,
    pub chunk_q: i32,
    pub chunk_r: i32,
    pub chunk_index: i64,
    pub revealed_at: Timestamp,
    pub hexes: Vec<ExploredHex>,
}
