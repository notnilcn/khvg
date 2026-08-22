//! Ability charge reducers (P4): begin/release a hold-to-charge ability. One charge per
//! player (the AbilityCharge row, keyed by profile_id) — death/logout/disconnect vanish
//! it with NO cooldown spent (see purge_profile_statuses), and the 1 Hz tick in
//! status/methods.rs auto-releases at MAX_CHARGE_SECONDS so a dropped client can't hold
//! the SelfSlow forever. Slot validation, effect dispatch and the cooldown/charge spend
//! are shared with activate_ability (player/reducers.rs).

use spacetimedb::{reducer, ReducerContext, Table};
use crate::main::global::*;
use crate::enemy::methods::seconds_since;
use crate::player::methods::{require_in_world, ability_is_charge_capable};
use crate::player::reducers::{resolve_ability_slot, apply_ability_effect, spend_ability};
use super::methods::apply_status_effect;
use super::tables::*;

#[reducer]
pub fn begin_ability_charge(ctx: &ReducerContext, slot_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    // Same slot/item/cooldown/charges validation as activate_ability — but nothing is
    // spent yet; the spend happens on release.
    let (_slot, ability) = resolve_ability_slot(ctx, profile_id, slot_index)?;
    if !ability_is_charge_capable(&ability) {
        return Err("Ability cannot be charged.".to_string());
    }
    if ctx.db.ability_charge().profile_id().find(&profile_id).is_some() {
        return Err("Already charging an ability.".to_string());
    }

    ctx.db.ability_charge().insert(AbilityCharge { profile_id, slot_index, started_at: ctx.timestamp });
    // SelfSlow while the charge is held (enforced by report_movement's speed clamp).
    // Duration is only a safety net — release removes the row, and the tick auto-releases
    // at MAX_CHARGE_SECONDS; the +2 covers tick drift.
    let params = StatusParams { slow_factor: CHARGE_SELF_SLOW_FACTOR, ..StatusParams::none() };
    apply_status_effect(ctx, profile_id, StatusEffectKind::SelfSlow, params, MAX_CHARGE_SECONDS + 2.0, 0, profile_id);
    Ok(())
}

#[reducer]
pub fn release_ability_charge(ctx: &ReducerContext, slot_index: u32, target_x: f32, target_y: f32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let charge = ctx.db.ability_charge().profile_id().find(&profile_id)
        .ok_or("No ability charge is held.")?;
    if charge.slot_index != slot_index {
        return Err("Charged slot does not match.".to_string());
    }
    internal_release_charge(ctx, profile_id, target_x, target_y)
}

/// The shared release path — release_ability_charge (cursor target) and the 1 Hz tick's
/// timeout auto-release (target = the player's own position, the only cursor-less choice;
/// a Teleport fizzles in place but still spends its cooldown). Deletes the charge row,
/// removes the SelfSlow, dispatches the effect scaled by the charge fraction, THEN spends
/// cooldown/charges exactly like activate_ability.
pub fn internal_release_charge(ctx: &ReducerContext, profile_id: u64, target_x: f32, target_y: f32) -> Result<(), String> {
    let charge = ctx.db.ability_charge().profile_id().find(&profile_id)
        .ok_or("No ability charge is held.")?;
    let charge_seconds = seconds_since(ctx, charge.started_at);
    ctx.db.ability_charge().profile_id().delete(&profile_id);
    remove_self_slow(ctx, profile_id);

    let (slot, ability) = resolve_ability_slot(ctx, profile_id, charge.slot_index)?;
    let charge_fraction = (charge_seconds / MAX_CHARGE_SECONDS).min(1.0);
    apply_ability_effect(ctx, profile_id, &slot, &ability, target_x, target_y, charge_fraction)?;
    spend_ability(ctx, profile_id, slot, &ability);
    Ok(())
}

/// Removes the charge's SelfSlow row(s) — one per player in practice, but sweep them all.
fn remove_self_slow(ctx: &ReducerContext, profile_id: u64) {
    for row in ctx.db.active_status_effect().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        if row.kind == StatusEffectKind::SelfSlow {
            ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
        }
    }
}
