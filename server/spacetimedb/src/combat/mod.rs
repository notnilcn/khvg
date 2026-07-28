//! Shared combat pipeline: the single place where damage is computed, mitigated, and
//! applied, and where death side effects live. Reducers resolve base damage and delegate
//! here, so the player and enemy paths share one implementation — the server-side
//! counterpart of the client's HealthComponent/DamageComponent composition.
//!
//! Composition mapping: each per-entity table is a component (one concern per table) and
//! `profile_id`/`enemy_id` is the entity id joining its rows. Death/despawn teardown goes
//! through the shared archetype helpers — teardown_profile for players, despawn_enemy_archetype
//! for enemies — so there is exactly one table list per entity type.

use spacetimedb::{ReducerContext, Table};
use std::time::Duration;
use crate::player::tables::{logged_in_player, logged_out_player, player_data, player_inventory, player_stats, LoggedInPlayer, LoggedOutPlayer, PlayerData};
use crate::player::methods::{internal_gain_xp, teardown_profile, weapon_behavior};
use crate::enemy::def_tables::enemy_template;
use crate::enemy::instance_tables::{enemy, enemy_behavior, Enemy};
use crate::enemy::methods::{compute_phase, despawn_enemy_archetype, recompute_aggro};
use crate::item::tables::item;

// Payloads are part of the pipeline API for upcoming callers (e.g. the slash/bullet-despawn
// reducers in src/plan.md); today's reducers only branch on the variant.
#[allow(dead_code)]
pub enum PlayerDamageOutcome {
    Alive { hp: u32 },
    Died,
}

#[allow(dead_code)]
pub enum EnemyDamageOutcome {
    Alive { hp: u32, phase: u8 },
    Died { xp_reward: u32 },
}

/// Outgoing player damage: weapon base damage scaled by strength.
pub fn compute_player_damage(ctx: &ReducerContext, profile_id: u64) -> u32 {
    let stats = match ctx.db.player_stats().profile_id().find(&profile_id) { Some(s) => s, None => return 1 };
    let inv = match ctx.db.player_inventory().profile_id().find(&profile_id) { Some(i) => i, None => return 1 };
    let weapon_item = inv.slots.get(0)
        .and_then(|s| s.item_id.as_ref())
        .and_then(|id| ctx.db.item().item_id().find(id));
    let weapon = weapon_item.as_ref().and_then(|i| weapon_behavior(i));
    match weapon {
        Some(w) => (w.damage as f32 * (1.0 + stats.strength as f32 * 0.002)).max(1.0) as u32,
        None => 0,
    }
}

/// Incoming player damage after defense mitigation (minimum 1).
pub fn compute_incoming_damage(ctx: &ReducerContext, profile_id: u64, base_damage: u32) -> u32 {
    let stats = match ctx.db.player_stats().profile_id().find(&profile_id) { Some(s) => s, None => return base_damage.max(1) };
    (base_damage as f32 * (1.0 - stats.defense as f32 * 0.002)).max(1.0) as u32
}

/// Applies damage to a player: mitigates, subtracts, and on zero performs the full death
/// cleanup (drops the profile's rows and moves the player back to the lobby).
pub fn deal_damage_to_player(ctx: &ReducerContext, player: &LoggedInPlayer, base_damage: u32) -> Result<PlayerDamageOutcome, String> {
    let profile_id = player.profile_id;
    let damage = compute_incoming_damage(ctx, profile_id, base_damage);
    let data = ctx.db.player_data().profile_id().find(&profile_id).ok_or("Player data not found.")?;
    let new_hp = data.hp.saturating_sub(damage);
    if new_hp == 0 {
        teardown_profile(ctx, profile_id);
        ctx.db.logged_in_player().player_id().delete(ctx.sender());
        ctx.db.logged_out_player().insert(LoggedOutPlayer { player_id: ctx.sender(), username: player.username.clone(), is_admin: player.is_admin });
        Ok(PlayerDamageOutcome::Died)
    } else {
        ctx.db.player_data().profile_id().update(PlayerData { hp: new_hp, ..data });
        Ok(PlayerDamageOutcome::Alive { hp: new_hp })
    }
}

/// Applies damage to an enemy: subtracts, and on zero deletes the enemy (and its behavior
/// rows) and awards xp. On survival, recomputes the phase from hp thresholds and re-aggros
/// onto the attacker (ctx.sender()).
pub fn deal_damage_to_enemy(ctx: &ReducerContext, profile_id: u64, enemy: Enemy) -> EnemyDamageOutcome {
    let template = ctx.db.enemy_template().template_id().find(&enemy.template_id);
    let damage = compute_player_damage(ctx, profile_id);
    let new_hp = enemy.hp.saturating_sub(damage);

    if new_hp == 0 {
        let xp_reward = template.as_ref().map(|t| t.max_hp / 10).unwrap_or(10);
        despawn_enemy_archetype(ctx, &enemy);
        internal_gain_xp(ctx, profile_id, xp_reward);
        return EnemyDamageOutcome::Died { xp_reward };
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
        } else if behavior.aggro_target != Some(ctx.sender()) && ctx.timestamp >= behavior.aggro_locked_until {
            behavior.aggro_target = Some(ctx.sender());
            behavior.aggro_locked_until = ctx.timestamp + Duration::from_secs_f32(aggro_lock_seconds.max(0.0));
            changed = true;
        }
        if changed {
            ctx.db.enemy_behavior().behavior_id().update(behavior);
        }
    }

    ctx.db.enemy().enemy_id().update(Enemy { hp: new_hp, phase: new_phase, ..enemy });
    EnemyDamageOutcome::Alive { hp: new_hp, phase: new_phase }
}

/// Heals a player, clamped to max_hp. No-op if the player has no data row.
pub fn heal_player(ctx: &ReducerContext, profile_id: u64, amount: u32) {
    if let Some(data) = ctx.db.player_data().profile_id().find(&profile_id) {
        let new_hp = (data.hp + amount).min(data.max_hp);
        ctx.db.player_data().profile_id().update(PlayerData { hp: new_hp, ..data });
    }
}
