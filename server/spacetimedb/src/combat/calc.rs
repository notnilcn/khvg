//! Damage math: pure resolution, no row writes. Reducers resolve base damage and the
//! apply step (`apply.rs`) mitigates and writes, so every damage path shares one
//! mitigation site — the server-side counterpart of the client's
//! HealthComponent/DamageComponent composition.
//!
//! Damage model (Game Design Doc 02): defence is a FLAT per-bullet subtraction with
//! minimum damage 1, which is what makes many-weak-bullets patterns trade off against
//! few-strong-bullets ones. Weapon damage is per trigger pull, divided among the
//! pattern's bullets. True-damage enchantment bonuses apply after defence.

use spacetimedb::{ReducerContext, Table};
use crate::player::tables::{player_data, player_stats};
use crate::item::methods::resolve_effective_weapon;

/// Outgoing weapon damage split at the mitigation seam: `base` is pre-defence (defence is
/// subtracted by `apply_damage_to_enemy` so all damage paths share one mitigation site),
/// `post_mitigation` is the true-damage bonus defence must not touch (added after).
pub struct ComputedDamage {
    pub base: u32,
    pub post_mitigation: u32,
}

/// Outgoing player damage per reported bullet hit: the effective weapon's per-trigger
/// damage divided among its bullets (toggle + enchantment composition already applied by
/// resolve_effective_weapon), scaled by strength. Defence is NOT subtracted here — that
/// happens in apply_damage_to_enemy so weapon, spell, DoT, and zone damage all share the
/// same mitigation (and future enemy-status) consult points.
pub fn compute_player_damage(ctx: &ReducerContext, profile_id: u64) -> ComputedDamage {
    let stats = match ctx.db.player_stats().profile_id().find(&profile_id) { Some(s) => s, None => return ComputedDamage { base: 1, post_mitigation: 0 } };
    let weapon = match resolve_effective_weapon(ctx, profile_id) { Some(w) => w, None => return ComputedDamage { base: 0, post_mitigation: 0 } };
    let raw = weapon.behavior.damage as f32
        * (1.0 + stats.strength as f32 * 0.002)
        / weapon.behavior.shot_count.max(1) as f32;
    ComputedDamage {
        base: (raw + weapon.flat_bullet_mod).max(0.0) as u32,
        post_mitigation: (weapon.true_flat + raw * weapon.true_pct) as u32,
    }
}

/// Incoming player damage after the player's flat defence (minimum 1).
pub fn compute_incoming_damage(ctx: &ReducerContext, profile_id: u64, base_damage: u32) -> u32 {
    let data = match ctx.db.player_data().profile_id().find(&profile_id) { Some(d) => d, None => return base_damage.max(1) };
    (base_damage as i32 - data.defense).max(1) as u32
}
