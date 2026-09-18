//! Player status effects with runtime state (pools, counters, marks) or non-stat effects
//! (reduction %, invulnerability, floors) that need consult points in the damage pipeline.
//! Plain pre-baked stat buffs stay on ActiveConsumableEffect; anything the tick or the
//! damage intercept must consult lives here. See status/methods.rs for the shared
//! apply/intercept entry points — every targeting variant (self / allies-in-radius /
//! ground zone) calls the same functions.

use spacetimedb::{table, SpacetimeType, Timestamp};

/// How a bleed mark (DamageToBleed) sheds. One purge condition per ability item.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum PurgeKind {
    /// Purge after BLEED_NO_DAMAGE_SECONDS without taking any hit (absorbed hits count).
    NoDamageTaken,
    /// Purge after accumulating BLEED_STAY_CLOSE_SECONDS within params.r1 of the marked enemy.
    StayClose,
    /// Purge after dealing params.x total damage to the marked enemy.
    DealDamage,
    /// Purge when the marked enemy dies (handled by on_enemy_killed, not the tick).
    OnKill,
}

/// How a store-damage mark (StoreDamage) settles its withheld pool at window expiry.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum WagerKind {
    /// 0 hits taken = 2x, 1..=max_hits = 1x, more = 0x. Corroborated against player_hit_flag.
    NoHits,
    /// Never leave r1 = 2x, never leave r2 = 1x, ever leave r2 (violated) = 0x.
    Radius,
    /// Stored >= x = 2x, >= z = 1x, < z = 0x.
    Damage,
}

/// What a StoreIncoming status releases its pool into at window expiry.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum ReleaseKind {
    /// AoE burst at the cursor point (paladin smite).
    Smite,
    /// Freeze the pool and re-arm; the next shield bash consumes it (tank).
    Bash,
}

/// Enemy-side debuff kinds (P2). Consult points: DoT ticks in status::methods::tick;
/// Slow/Vulnerability/ArmorBreak/Stunned are consulted by the combat sink
/// (apply_damage_to_enemy) and the enemy behavior tick (movement + fire pacing).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum EnemyStatusKind {
    /// Deals magnitude damage per second on the 1 Hz status tick.
    DoT,
    /// Movement displacement ×(1−magnitude).
    Slow,
    /// Damage taken ×(1+magnitude).
    Vulnerability,
    /// Flat defence treated as 0 (magnitude unused, always 1.0).
    ArmorBreak,
    /// Behavior per the template's stun_behavior: Pause (freeze), Slowed (acts as Slow),
    /// Enraged (movement and fire pacing ×(1+magnitude) faster).
    Stunned,
}

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum StatusEffectKind {
    /// Bleed mark: hits from the marked enemy split params.conversion into a draining pool.
    DamageToBleed,
    /// Store mark: damage dealt to the marked enemy is withheld, then released per the wager.
    StoreDamage,
    Invulnerable,
    /// Absorbs the next single hit, then consumes itself.
    DamageShield,
    DamageReductionPct,
    DamageDealtMult,
    /// Cannot drop below 1 HP. Exists only for the witch-doctor zone.
    HpFloor,
    /// Sheds/prevents enemy debuffs. Exists only for the witch-doctor zone.
    /// TODO: no consult point exists — nothing applies debuffs TO players yet (enemy
    /// debuffs land on enemies only). Design source: docs/Game Design Docs/Items/Items.md
    /// ("Warding Circle — allies inside are immune to enemy debuffs"). Implement the guard
    /// in the damage/status intercept when an enemy→player debuff path is added.
    DebuffImmune,
    /// Re-routes incoming raw hits to source_profile_id (the paladin).
    RedirectToCaster,
    Regen,
    /// Stores incoming post-mitigation damage during the window; releases as a smite burst.
    StoreIncomingSmite,
    /// Stores incoming post-mitigation damage during the window; re-arms for the next bash.
    StoreIncomingBash,
    /// Server-enforced move-speed clamp while an ability charge is held.
    SelfSlow,
}

/// Cast-time values the tick/intercept need later. One flat struct with a field per
/// concept (unused fields stay zero) — a per-kind enum payload would make the tick match
/// on every row twice. Mirrors the kind:
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct StatusParams {
    pub purge: PurgeKind,           // DamageToBleed
    pub wager: WagerKind,           // StoreDamage
    pub release: ReleaseKind,       // StoreIncoming*
    pub conversion: f32,            // DamageToBleed: fraction of a marked-enemy hit routed to the pool
    pub pool_seconds: f32,          // DamageToBleed: approximate pool drain time
    pub per_second: f32,            // Regen
    pub pct: f32,                   // DamageReductionPct (0..1)
    pub mult: f32,                  // DamageDealtMult (1 = no change)
    pub r1: f32,                    // StayClose purge radius / radius-wager inner radius
    pub r2: f32,                    // radius-wager outer radius
    pub x: f32,                     // DealDamage purge threshold / damage-wager 2x threshold
    pub z: f32,                     // damage-wager 1x threshold
    pub max_hits: u32,              // NoHits wager: hits above this forfeit (0x)
    pub release_radius: f32,        // Smite release AoE radius
    pub slow_factor: f32,           // SelfSlow: speed multiplier while held
    pub outgoing: bool,             // DamageToBleed: convert outgoing hits into an enemy DoT
}

impl StatusParams {
    pub fn none() -> Self {
        StatusParams {
            purge: PurgeKind::NoDamageTaken,
            wager: WagerKind::NoHits,
            release: ReleaseKind::Smite,
            conversion: 0.0, pool_seconds: 0.0, per_second: 0.0, pct: 0.0, mult: 1.0,
            r1: 0.0, r2: 0.0, x: 0.0, z: 0.0, max_hits: 0, release_radius: 0.0,
            slow_factor: 1.0, outgoing: false,
        }
    }
}

/// Ground-zone kinds (P3 — the witch doctor's circles and the wizard's spire). Ally zones
/// re-apply their status/heal every 1 Hz tick to whoever is inside (ZONE_STATUS_REFRESH_SECONDS,
/// so leaving the zone sheds the effect within ~1.5s); enemy zones do the same through
/// apply_enemy_status. Burst kinds do nothing per tick and fire once at expiry. DamageShare
/// has no per-tick effect at all — it is consulted live by intercept_incoming.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum ZoneKind {
    /// Applies DebuffImmune to players inside (magnitude unused — the kind has no consult
    /// point yet; see the TODO on StatusEffectKind::DebuffImmune).
    DebuffImmunity,
    /// Applies DamageReductionPct (pct = magnitude) to players inside.
    DamageReduction,
    /// Applies HpFloor to players inside (magnitude unused).
    HpFloor,
    /// Heals players inside for magnitude HP per tick.
    HealOverTime,
    /// At expiry: heals players inside for magnitude, once, then the zone dies.
    BurstHeal,
    /// Applies DamageDealtMult (mult = magnitude) to players inside.
    DamageBuff,
    /// Applies a DoT (magnitude damage/s) to enemies inside.
    EnemyDot,
    /// At expiry: deals magnitude damage (Normal mitigation) to enemies inside, once.
    EnemyBurst,
    /// Applies Slow (magnitude fraction) to enemies inside.
    EnemySlow,
    /// Applies Vulnerability (magnitude fraction) to enemies inside.
    EnemyVulnerability,
    /// Splits incoming hits across the zone's live occupants (magnitude unused).
    DamageShare,
}

/// A ground zone (P3). Placed by the CastZone ability at the range-clamped cursor point,
/// owned by the caster, and ticked at 1 Hz by status::methods::tick — membership is LIVE
/// each tick (players_near_point / enemies_near_point on the zone center), never carried.
/// Purged with the owner (death / leave_world / disconnect via purge_profile_zones).
/// `magnitude` means per-kind (see ZoneKind); `remaining` is the zone's own lifetime.
#[table(accessor = ground_zone, public)]
pub struct GroundZone {
    #[primary_key] #[auto_inc]
    pub zone_id: u64,
    #[index(btree)]
    pub owner_profile_id: u64,
    pub kind: ZoneKind,
    pub x: f32,
    pub y: f32,
    pub radius: f32,
    pub magnitude: f32,
    pub remaining: f32,
}

/// One active status row. Uniqueness: one row per (profile, kind), except the mark kinds
/// (DamageToBleed/StoreDamage) which share a single slot — one active mark per player.
/// Field roles dual-purpose by kind (each row has exactly one purge/wager), documented here:
/// - `pool`: bleed pool (DamageToBleed) / withheld damage pool (StoreDamage, StoreIncoming*)
/// - `counter`: hits taken during the window (StoreDamage NoHits wager) / total damage
///   dealt to the marked enemy (DamageToBleed DealDamage purge)
/// - `progress`: accumulated seconds inside r1 (DamageToBleed StayClose purge) / 1.0 once
///   r1 was exited (StoreDamage Radius wager — "never leave r1" check)
/// - `violated`: radius wager — r2 was exited (stamped from report_movement, never cleared) /
///   StoreIncomingBash armed flag (pool frozen, waiting for the next bash)
/// - `marked_enemy_id`: 0 = none
/// - `source_profile_id`: redirect target (RedirectToCaster) / casting paladin, etc.
/// - `last_event_at`: last hit taken / last radius-exit
#[table(accessor = active_status_effect, public)]
pub struct ActiveStatusEffect {
    #[primary_key] #[auto_inc]
    pub effect_id: u64,
    #[index(btree)]
    pub profile_id: u64,
    pub kind: StatusEffectKind,
    pub params: StatusParams,
    pub pool: f32,
    pub counter: u32,
    pub progress: f32,
    pub violated: bool,
    pub remaining: f32,
    #[index(btree)]
    pub marked_enemy_id: u64,
    pub source_profile_id: u64,
    pub last_event_at: Timestamp,
}

/// One held ability charge (P4). One charge per player — begin_ability_charge refuses
/// while a row exists. Deleted by release_ability_charge, by the 1 Hz tick's timeout
/// auto-release (MAX_CHARGE_SECONDS), and by the lifecycle purge (death / leave_world /
/// disconnect — a vanished charge spends NO cooldown). The charge's SelfSlow status row
/// is a separate ActiveStatusEffect row removed on release.
#[table(accessor = ability_charge, public)]
pub struct AbilityCharge {
    #[primary_key]
    pub profile_id: u64,
    pub slot_index: u32,
    pub started_at: Timestamp,
}

/// Channeled action kind (Phase 1 — BitCraft `progressive_action_state` pattern).
/// Revive / Capture / ChannelCast share one lock/progress/expiry shape; completion
/// dispatches into the existing effect helpers (`heal_player`, `apply_ability_effect`).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum ChanneledActionKind {
    Revive,
    Capture,
    ChannelCast,
}

/// A progressive / channeled action (Phase 1 — BitCraft `ProgressiveActionState`,
/// translated to one-table-per-concern). Max 1 active per player + max N per target
/// (mirror BitCraft `validate_slots`). `lock_expires_at` auto-releases on the 1 Hz
/// tick (mirror the charge MAX_CHARGE_SECONDS path). `is_public` mirrors BitCraft's
/// public-vs-owned craft split.
#[table(accessor = channeled_action, public, index(accessor = by_channel_target, btree(columns = [target_id])))]
pub struct ChanneledAction {
    #[primary_key] #[auto_inc]
    pub action_id: u64,
    #[index(btree)]
    pub profile_id: u64,
    pub kind: ChanneledActionKind,
    pub target_id: u64,
    pub target_x: f32,
    pub target_y: f32,
    pub progress: f32,
    pub required: f32,
    pub lock_expires_at: Timestamp,
    pub is_public: bool,
}

/// One active enemy debuff row. Uniqueness: one row per (enemy_id, kind) — re-applying
/// refreshes (replaces magnitude/remaining/source). Lifetime is managed by the 1 Hz status
/// tick (expiry sweep + DoT damage) and by on_enemy_killed (rows never outlive their enemy).
/// `magnitude` means per-kind (see EnemyStatusKind); `source_profile_id` credits DoT kills
/// and aggro (0 = none).
#[table(accessor = active_enemy_status_effect, public)]
pub struct ActiveEnemyStatusEffect {
    #[primary_key] #[auto_inc]
    pub effect_id: u64,
    #[index(btree)]
    pub enemy_id: u64,
    pub kind: EnemyStatusKind,
    pub magnitude: f32,
    pub remaining: f32,
    pub source_profile_id: u64,
}
