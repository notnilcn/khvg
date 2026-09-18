//! Shared combat pipeline: the single place where damage is computed, mitigated, and
//! applied. Reducers resolve base damage and delegate here, so the player and enemy paths
//! share one implementation — the server-side counterpart of the client's
//! HealthComponent/DamageComponent composition.
//!
//! Layout: `calc.rs` is pure damage math (no row writes), `apply.rs` is the sinks that
//! mitigate, subtract, and handle kills. This file keeps the shared policy vocabulary
//! (mitigation modes/options) and re-exports everything so callers keep their
//! `crate::combat::` paths.
//!
//! Composition mapping: each per-entity table is a component (one concern per table) and
//! `profile_id`/`enemy_id` is the entity id joining its rows. Death ownership is split by
//! domain: a player killing blow delegates to `player::methods::kill_player` (teardown +
//! lobby move live in the player module — combat never touches them), while an enemy kill
//! despawns through the shared `despawn_enemy_archetype` helper, so there is exactly one
//! table list per entity type.
//!
//! Damage model (Game Design Doc 02): defence is a FLAT per-bullet subtraction with
//! minimum damage 1, which is what makes many-weak-bullets patterns trade off against
//! few-strong-bullets ones. Weapon damage is per trigger pull, divided among the
//! pattern's bullets. True-damage enchantment bonuses apply after defence.

mod apply;
mod calc;

pub use apply::{
    apply_damage_to_enemy, deal_damage_to_enemy, deal_damage_to_enemy_bonus,
    deal_damage_to_player, deal_damage_to_player_opts, heal_player,
};
pub use calc::compute_incoming_damage;

/// Single damage policy for the enemy damage path: what mitigation applies and which
/// outgoing consults run. One struct, one apply path, named constructors for the three
/// call patterns — no mode × options boolean pairs.
pub struct DamageRequest {
    /// Subtract flat defence (minimum 1) and consult ArmorBreak/Vulnerability.
    pub mitigated: bool,
    /// Run the StoreDamage withhold consult.
    pub consult_withhold: bool,
    /// Run the DamageToBleed conversion consult.
    pub consult_bleed: bool,
}

impl DamageRequest {
    /// Weapon hits, spells, zones, wagers: full mitigation + both outgoing consults.
    pub const NORMAL: DamageRequest = DamageRequest { mitigated: true, consult_withhold: true, consult_bleed: true };
    /// Enemy DoT ticks: mitigated, but neither outgoing consult runs — a Bloodbath DoT
    /// can't re-convert its own tick into a fresh (decaying) DoT forever instead of ever
    /// landing, and a caster's own DoT ticks aren't withheld into their own store pool.
    pub const DOT_TICK: DamageRequest = DamageRequest { mitigated: true, consult_withhold: false, consult_bleed: false };
    /// Store-damage releases: already mitigated at capture (no double-dip), consults run.
    pub const FLAT_RELEASE: DamageRequest = DamageRequest { mitigated: false, consult_withhold: true, consult_bleed: true };
}

/// Options for the internal damage path. Bleed-pool drains call with BLEED_DRAIN_OPTIONS:
/// no re-conversion of their own ticks, but HpFloor still applies and death cleanup runs.
/// No direct HP writes anywhere — death cleanup lives only in the apply path.
pub struct IncomingDamageOptions {
    /// Skip the whole status intercept (invulnerable/shield/redirect/reduction/bleed
    /// conversion) for this hit — used by the bleed pool's own drain ticks.
    pub skip_conversion: bool,
    /// Whether the HpFloor post-mitigation clamp applies.
    pub respect_floor: bool,
    /// Whether RedirectToCaster consults run — redirected hits pass true so a redirect
    /// chain can never loop.
    pub skip_redirect: bool,
}

pub const DEFAULT_INCOMING_OPTIONS: IncomingDamageOptions = IncomingDamageOptions { skip_conversion: false, respect_floor: true, skip_redirect: false };
/// Bleed-pool drains: bypass the status intercept and redirect, still respect the floor.
pub const BLEED_DRAIN_OPTIONS: IncomingDamageOptions = IncomingDamageOptions { skip_conversion: true, respect_floor: true, skip_redirect: true };
/// Zone damage-share portions: already intercepted at the source hit — shared portions
/// never re-intercept (loop guard) — but still respect the floor.
pub const SHARED_PORTION_OPTIONS: IncomingDamageOptions = IncomingDamageOptions { skip_conversion: true, respect_floor: true, skip_redirect: true };
/// Redirected hits: the intercept runs on the new victim (only redirect itself is
/// skipped so a chain can never loop), floor still applies.
pub const REDIRECTED_OPTIONS: IncomingDamageOptions = IncomingDamageOptions { skip_conversion: false, respect_floor: true, skip_redirect: true };
