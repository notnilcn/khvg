use spacetimedb::{ReducerContext, Table};
use std::time::Duration;
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::methods::*;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;
use crate::enemy::methods::*;
#[spacetimedb::reducer]
pub fn tick_enemy_spawn(ctx: &ReducerContext, _job: EnemySpawnSchedule) {
    spawn_from_biome_regions(ctx);
}

#[spacetimedb::reducer]
pub fn tick_enemy_behavior(ctx: &ReducerContext, _job: EnemyBehaviorSchedule) {
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(0);
    let chunk_rows       = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(0);

    for enemy in ctx.db.enemy().iter() {
        let mut behavior = match ctx.db.enemy_behavior().behavior_id().find(&enemy.behavior_id) {
            Some(b) => b,
            None => continue,
        };
        let template = match ctx.db.enemy_template().template_id().find(&enemy.template_id) {
            Some(t) => t,
            None => continue,
        };
        if !has_player_in_simulation_range(ctx, enemy.x, enemy.y, template.simulation_range) {
            if behavior.was_simulating {
                ctx.db.enemy_behavior().behavior_id().update(EnemyBehavior { was_simulating: false, ..behavior });
            }
            continue;
        }

        // Only retarget on the tick an enemy *enters* simulation range — avoids
        // recomputing aggro every 100ms for an enemy already mid-fight.
        if !behavior.was_simulating {
            let (aggro_target, aggro_locked_until) = recompute_aggro(ctx, enemy.x, enemy.y, template.aggro_lock_seconds);
            behavior.aggro_target = aggro_target;
            behavior.aggro_locked_until = aggro_locked_until;
        }
        behavior.was_simulating = true;

        let phase_idx = enemy.phase as usize;
        let phase_def = match template.phases.get(phase_idx) {
            Some(p) => p,
            None => continue,
        };
        let mut phase = match ctx.db.enemy_phases().behavior_id().filter(&enemy.behavior_id).find(|p| p.phase_index == enemy.phase) {
            Some(p) => p,
            None => continue,
        };

        // enemy.phase is advanced by report_enemy_hit when HP crosses a threshold;
        // detect that transition here and reset the new phase's attack sequences.
        let mut active_phase_index = behavior.active_phase_index;
        if enemy.phase != active_phase_index {
            active_phase_index = enemy.phase;
            begin_phase_cycle(ctx, &mut phase, phase_def);
        }

        for (pt, step_target, ox, oy, bao, tex, lifetime, source_step) in tick_phase(ctx, &mut phase, phase_def) {
            let target = resolve_step_target(step_target, behavior.aggro_target);
            ctx.db.bullet_pattern_event().insert(BulletPatternEvent {
                event_id: 0, enemy_id: enemy.enemy_id,
                source_step,
                origin_x: enemy.x, origin_y: enemy.y,
                origin_offset_x: ox, origin_offset_y: oy,
                base_angle_offset: bao,
                pattern_type: pt,
                target, texture_id: tex, lifetime,
                chunk_index: enemy.chunk_index,
            });
        }

        let (new_x, new_y, new_dir_x, new_dir_y) = apply_movement(ctx, &enemy, &behavior, &phase);
        let (new_x, new_y) = wrap_world_pos(new_x, new_y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
        let (cq, cr) = world_to_chunk(new_x, new_y, chunk_hex_radius, hex_outer_radius);
        let chunk_index = spiral_chunk_index(cq, cr);

        ctx.db.enemy_phases().phase_id().update(phase);
        ctx.db.enemy_behavior().behavior_id().update(EnemyBehavior {
            active_phase_index, velocity_x: new_dir_x, velocity_y: new_dir_y,
            ..behavior
        });
        ctx.db.enemy().enemy_id().update(Enemy { x: new_x, y: new_y, chunk_index, ..enemy });
    }
}

#[spacetimedb::reducer]
pub fn report_enemy_hit(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let enemy = match ctx.db.enemy().enemy_id().find(&enemy_id) {
        Some(e) => e,
        None => return Ok(()),
    };
    if enemy.immortal { return Ok(()); }

    let template = ctx.db.enemy_template().template_id().find(&enemy.template_id);
    let damage = compute_player_damage(ctx, profile_id);
    let new_hp = enemy.hp.saturating_sub(damage);

    if new_hp == 0 {
        let xp_reward = template.as_ref().map(|t| t.max_hp / 10).unwrap_or(10);
        delete_enemy_behavior(ctx, enemy.behavior_id);
        ctx.db.enemy().enemy_id().delete(&enemy_id);
        internal_gain_xp(ctx, profile_id, xp_reward);
        return Ok(());
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
    Ok(())
}
