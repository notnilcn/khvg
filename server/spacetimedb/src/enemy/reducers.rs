use spacetimedb::{ReducerContext, Table, ScheduleAt, TimeDuration, log_stopwatch};
use std::time::Duration;
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::player::methods::*;
use crate::enemy::tables::*;
use crate::enemy::methods::*;
#[spacetimedb::reducer]
pub fn tick_enemy_spawn(ctx: &ReducerContext, _job: EnemySpawnSchedule) {
    if ctx.db.enemy().iter().next().is_some() { return; } // temporary: one enemy at a time

    let template = match ctx.db.enemy_template().template_id().find(&"TestBoss".to_string()) {
        Some(t) => t,
        None => return,
    };

    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let (cq, cr) = world_to_chunk(0.0, 0.0, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    let inserted_behavior = ctx.db.enemy_behavior().insert(build_enemy_behavior(ctx, &template));

    ctx.db.enemy().insert(Enemy {
        enemy_id: 0, template_id: template.template_id, behavior_id: inserted_behavior.behavior_id,
        x: 0.0, y: 0.0, spawn_time: ctx.timestamp,
        hp: template.max_hp, phase: 0, is_elite: false, immortal: false, chunk_index,
    });
}

#[spacetimedb::reducer]
pub fn tick_enemy_behavior(ctx: &ReducerContext, _job: EnemyBehaviorSchedule) {
    // let reducer_timer = log_stopwatch::LogStopwatch::new("Enemy Behavior Reducer Completed");

    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(0);
    let chunk_rows       = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(0);

    // let mut total = 0u32;
    // let mut simulated = 0u32;
    // let mut bullets_fired = 0u32;

    for enemy in ctx.db.enemy().iter() {
        //total += 1;
        let mut behavior = match ctx.db.enemy_behavior().behavior_id().find(&enemy.behavior_id) {
            Some(b) => b,
            None => continue,
        };
        if !has_player_in_range(ctx, enemy.x, enemy.y, behavior.simulation_range) {
            if behavior.was_simulating {
                ctx.db.enemy_behavior().behavior_id().update(EnemyBehavior { was_simulating: false, ..behavior });
            }
            continue;
        }
        // simulated += 1;

        if !behavior.was_simulating {
            let (aggro_target, aggro_locked_until) = recompute_aggro(ctx, enemy.x, enemy.y, behavior.aggro_lock_seconds);
            behavior.aggro_target = aggro_target;
            behavior.aggro_locked_until = aggro_locked_until;
        }
        behavior.was_simulating = true;

        let phase_idx = enemy.phase as usize;
        if phase_idx >= behavior.phases.len() { continue; }

        let mut phases = behavior.phases.clone();

        for attack in &mut phases[phase_idx].attacks {
            let seq = attack.attack.clone();
            if let Some((pt, step_target, ox, oy, bao, tex, lifetime)) = tick_sequence(ctx, attack, &seq) {
                // bullets_fired += 1;
                let target = resolve_step_target(step_target, behavior.aggro_target);
                ctx.db.bullet_pattern_event().insert(BulletPatternEvent {
                    event_id: 0, enemy_id: enemy.enemy_id,
                    origin_x: enemy.x, origin_y: enemy.y,
                    origin_offset_x: ox, origin_offset_y: oy,
                    base_angle_offset: bao,
                    pattern_type: pt,
                    target, texture_id: tex, lifetime,
                    chunk_index: enemy.chunk_index,
                });
            }
        }

        let movement = phases[phase_idx].movement.clone();
        let (new_x, new_y, new_dir_x, new_dir_y, new_move_timer) = apply_movement(ctx, &enemy, &behavior, &movement);
        let (new_x, new_y) = wrap_world_pos(new_x, new_y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
        let (cq, cr) = world_to_chunk(new_x, new_y, chunk_hex_radius, hex_outer_radius);
        let chunk_index = spiral_chunk_index(cq, cr);

        ctx.db.enemy_behavior().behavior_id().update(EnemyBehavior {
            phases, move_dir_x: new_dir_x, move_dir_y: new_dir_y, move_timer: new_move_timer,
            ..behavior
        });
        ctx.db.enemy().enemy_id().update(Enemy { x: new_x, y: new_y, chunk_index, ..enemy });
    }

    // if total > 0 {
    //     log::info!("tick_enemy_behavior: {}/{} enemies simulated, {} bullets fired this tick", simulated, total, bullets_fired);
    // }

    // reducer_timer.end();
}

#[spacetimedb::reducer]
pub fn report_enemy_hit(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    let profile_id = require_in_world(ctx)?;
    let enemy = match ctx.db.enemy().enemy_id().find(&enemy_id) {
        Some(e) => e,
        None => return Ok(()),
    };
    if enemy.immortal { return Ok(()); }

    let damage = compute_player_damage(ctx, profile_id);
    let new_hp = enemy.hp.saturating_sub(damage);

    if new_hp == 0 {
        let xp_reward = ctx.db.enemy_template().template_id().find(&enemy.template_id).map(|t| t.max_hp / 10).unwrap_or(10);
        ctx.db.enemy_behavior().behavior_id().delete(&enemy.behavior_id);
        ctx.db.enemy().enemy_id().delete(&enemy_id);
        internal_gain_xp(ctx, profile_id, xp_reward);
        return Ok(());
    }

    let new_phase = ctx.db.enemy_template().template_id().find(&enemy.template_id)
        .map(|t| compute_phase(new_hp, t.max_hp, &t.phases))
        .unwrap_or(enemy.phase);

    if let Some(mut behavior) = ctx.db.enemy_behavior().behavior_id().find(&enemy.behavior_id) {
        let mut changed = false;
        if new_phase != enemy.phase {
            let (aggro_target, aggro_locked_until) = recompute_aggro(ctx, enemy.x, enemy.y, behavior.aggro_lock_seconds);
            behavior.aggro_target = aggro_target;
            behavior.aggro_locked_until = aggro_locked_until;
            changed = true;
        } else if behavior.aggro_target != Some(ctx.sender()) && ctx.timestamp >= behavior.aggro_locked_until {
            behavior.aggro_target = Some(ctx.sender());
            behavior.aggro_locked_until = ctx.timestamp + Duration::from_secs_f32(behavior.aggro_lock_seconds.max(0.0));
            changed = true;
        }
        if changed {
            ctx.db.enemy_behavior().behavior_id().update(behavior);
        }
    }

    ctx.db.enemy().enemy_id().update(Enemy { hp: new_hp, phase: new_phase, ..enemy });
    Ok(())
}
