//! Damage application: the sinks that mitigate, subtract, and handle kills. Death
//! ownership is split by domain — a player killing blow delegates to
//! `player::methods::kill_player` (teardown + lobby move live in the player module;
//! combat never touches them directly), while an enemy kill despawns through the enemy
//! archetype helper plus the status kill hook here, since both are enemy-domain.

use spacetimedb::{ReducerContext, Identity};
use std::time::Duration;
use super::{DamageRequest, IncomingDamageOptions, DEFAULT_INCOMING_OPTIONS};
use super::calc::{compute_incoming_damage, compute_player_damage};
use crate::player::tables::{player_data, PlayerData, LoggedInPlayer};
use crate::player::methods::{internal_gain_xp, kill_player};
use crate::enemy::def_tables::enemy_template;
use crate::enemy::instance_tables::{enemy, enemy_behavior, Enemy};
use crate::enemy::methods::{compute_phase, despawn_enemy_archetype, recompute_aggro};
use crate::status::tables::active_status_effect;

/// Applies damage to a player: status intercept, mitigates, subtracts, and on zero
/// delegates the full death cleanup to `player::methods::kill_player`.
/// `source_enemy_id` attributes the hit (bleed marks only convert hits from their mark).
pub fn deal_damage_to_player(ctx: &ReducerContext, player: &LoggedInPlayer, source_enemy_id: Option<u64>, base_damage: u32) -> Result<(), String> {
    deal_damage_to_player_opts(ctx, player, source_enemy_id, base_damage, DEFAULT_INCOMING_OPTIONS)
}

/// The internal flags path behind deal_damage_to_player. The status module's
/// intercept_incoming hook (Invulnerable → DamageShield → RedirectToCaster → DamageShare →
/// DamageReductionPct → DamageToBleed) runs before mitigation unless
/// `opts.skip_conversion`; the HpFloor clamp applies post-mitigation when
/// `opts.respect_floor`.
pub fn deal_damage_to_player_opts(ctx: &ReducerContext, player: &LoggedInPlayer, source_enemy_id: Option<u64>, base_damage: u32, opts: IncomingDamageOptions) -> Result<(), String> {
    let profile_id = player.profile_id;
    let base_damage = if opts.skip_conversion {
        base_damage
    } else {
        let verdict = crate::status::methods::intercept_incoming(ctx, profile_id, source_enemy_id, base_damage, opts.skip_redirect);
        if verdict.absorbed {
            return Ok(());
        }
        verdict.base_damage
    };
    let damage = compute_incoming_damage(ctx, profile_id, base_damage);
    let data = ctx.db.player_data().profile_id().find(&profile_id).ok_or("Player data not found.")?;
    let mut new_hp = data.hp.saturating_sub(damage);
    if new_hp == 0 && opts.respect_floor
        && ctx.db.active_status_effect().profile_id().filter(&profile_id).any(|r| r.kind == crate::status::tables::StatusEffectKind::HpFloor)
    {
        new_hp = 1;
    }
    if new_hp == 0 {
        kill_player(ctx, player);
        Ok(())
    } else {
        ctx.db.player_data().profile_id().update(PlayerData { hp: new_hp, ..data });
        Ok(())
    }
}

/// The weapon-path wrapper: computes the attacker's effective-weapon damage and delegates
/// to apply_damage_to_enemy with full mitigation, xp credit for the attacker, and re-aggro
/// onto the caller.
pub fn deal_damage_to_enemy(ctx: &ReducerContext, profile_id: u64, enemy: Enemy) {
    deal_damage_to_enemy_bonus(ctx, profile_id, enemy, 0)
}

/// deal_damage_to_enemy with a flat PRE-mitigation bonus riding the same hit — the armed
/// StoreIncomingBash pool on a shield bash (the pool accumulates post-mitigation, then
/// merges into the weapon hit's base before defence rather than landing as a second
/// mitigated hit).
pub fn deal_damage_to_enemy_bonus(ctx: &ReducerContext, profile_id: u64, enemy: Enemy, bonus: u32) {
    let damage = compute_player_damage(ctx, profile_id);
    apply_damage_to_enemy(ctx, enemy, damage.base + bonus, damage.post_mitigation, DamageRequest::NORMAL, Some(profile_id), Some(ctx.sender()))
}

/// The one enemy damage sink — weapon hits, spells, DoT ticks, zone damage, and store-damage
/// releases all come through here with a DamageRequest policy. Mitigated requests subtract
/// defence and consult the target's enemy statuses (ArmorBreak → defence 0, Vulnerability →
/// final damage ×(1+magnitude)); unmitigated ones were already mitigated at capture.
/// On kill despawns the enemy (and its behavior rows) and awards xp to `xp_credit`.
/// On survival, recomputes the phase from hp thresholds and re-aggros onto `aggro_from`
/// (skipped when None — scheduled-reducer ticks have no meaningful sender).
pub fn apply_damage_to_enemy(
    ctx: &ReducerContext,
    enemy: Enemy,
    amount: u32,
    post_mitigation_bonus: u32,
    request: DamageRequest,
    xp_credit: Option<u64>,
    aggro_from: Option<Identity>,
) {
    let template = ctx.db.enemy_template().template_id().find(&enemy.template_id);
    let base_defense = template.as_ref().map(|t| t.defense).unwrap_or(0);
    // Target-side enemy-status consults (mitigated requests only — a release was already
    // mitigated at capture and must not double-dip): ArmorBreak zeroes defence,
    // Vulnerability multiplies the final damage.
    let (defense, vulnerability_mult) = if request.mitigated {
        (
            crate::status::methods::enemy_defense(ctx, enemy.enemy_id, base_defense),
            crate::status::methods::enemy_vulnerability_mult(ctx, enemy.enemy_id),
        )
    } else {
        (base_defense as i32, 1.0)
    };
    // Attacker-side status consult: DamageDealtMult applies at deal-time, pre-mitigation.
    let amount = (amount as f32 * crate::status::methods::outgoing_damage_mult(ctx, xp_credit)) as u32;
    // amount == 0 stays 0 (the no-weapon case deals no damage rather than the min-1 floor).
    let base = if request.mitigated && amount > 0 { (amount as i32 - defense).max(1) as u32 } else { amount };
    let damage = ((base + post_mitigation_bonus) as f32 * vulnerability_mult) as u32;
    // Attacker-side status consults on the final damage: StoreDamage withholds it into the
    // pool (released flat at settlement — already mitigated here); an outgoing bleed mark
    // converts it into an enemy DoT instead; otherwise record it for the DamageToBleed
    // DealDamage purge progress.
    if request.consult_withhold
        && crate::status::methods::withhold_outgoing(ctx, xp_credit, enemy.enemy_id, damage)
    {
        return;
    }
    if request.consult_bleed
        && crate::status::methods::convert_outgoing_to_bleed(ctx, xp_credit, enemy.enemy_id, damage)
    {
        return;
    }
    crate::status::methods::note_damage_dealt(ctx, xp_credit, enemy.enemy_id, damage);
    let new_hp = enemy.hp.saturating_sub(damage);

    if new_hp == 0 {
        let xp_reward = template.as_ref().map(|t| t.max_hp / 10).unwrap_or(10);
        despawn_enemy_archetype(ctx, &enemy);
        crate::status::methods::on_enemy_killed(ctx, enemy.enemy_id);
        if let Some(profile_id) = xp_credit {
            internal_gain_xp(ctx, profile_id, xp_reward);
        }
        return;
    }

    let new_phase = template.as_ref().map(|t| compute_phase(new_hp, t.max_hp, &t.phases)).unwrap_or(enemy.phase);
    let aggro_lock_seconds = template.as_ref().map(|t| t.aggro_lock_seconds).unwrap_or(0.0);

    if let Some(mut behavior) = ctx.db.enemy_behavior().behavior_id().find(&enemy.behavior_id) {
        let mut changed = false;
        if new_phase != enemy.phase {
            let (aggro_target, aggro_locked_until) = recompute_aggro(ctx, enemy.x, enemy.y, aggro_lock_seconds);
            behavior.aggro_target = aggro_target;
            behavior.aggro_locked_until = aggro_locked_until;
            changed = true;
        } else if let Some(from) = aggro_from {
            if behavior.aggro_target != Some(from) && ctx.timestamp >= behavior.aggro_locked_until {
                behavior.aggro_target = Some(from);
                behavior.aggro_locked_until = ctx.timestamp + Duration::from_secs_f32(aggro_lock_seconds.max(0.0));
                changed = true;
            }
        }
        if changed {
            ctx.db.enemy_behavior().behavior_id().update(behavior);
        }
    }

    ctx.db.enemy().enemy_id().update(Enemy { hp: new_hp, phase: new_phase, ..enemy });
}

/// Heals a player, clamped to max_hp. No-op if the player has no data row.
pub fn heal_player(ctx: &ReducerContext, profile_id: u64, amount: u32) {
    if let Some(data) = ctx.db.player_data().profile_id().find(&profile_id) {
        let new_hp = (data.hp + amount).min(data.max_hp);
        ctx.db.player_data().profile_id().update(PlayerData { hp: new_hp, ..data });
    }
}
