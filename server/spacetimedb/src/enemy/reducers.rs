use spacetimedb::{ReducerContext, Table};
use crate::main::global::*;
use crate::world::hex::{spiral_chunk_index, world_to_chunk};
use crate::world::wrap::wrap_world_pos;
use crate::world::tables::MapConfig;
use crate::player::methods::*;
use crate::combat::deal_damage_to_enemy;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;
use crate::enemy::methods::*;
#[spacetimedb::reducer]
pub fn tick_enemy_spawn(ctx: &ReducerContext, _job: EnemySpawnSchedule) {
    spawn_from_biome_regions(ctx);
}

#[spacetimedb::reducer]
pub fn tick_enemy_behavior(ctx: &ReducerContext, _job: EnemyBehaviorSchedule) {
    let cfg = MapConfig::load(ctx, 0, 0);
    let (chunk_hex_radius, hex_outer_radius, chunk_cols, chunk_rows) =
        (cfg.chunk_hex_radius, cfg.hex_outer_radius, cfg.chunk_cols, cfg.chunk_rows);

    for enemy in ctx.db.enemy().iter() {
        let mut behavior = match ctx.db.enemy_behavior().behavior_id().find(&enemy.behavior_id) {
            Some(b) => b,
            None => continue,
        };
        let template = match ctx.db.enemy_template().template_id().find(&enemy.template_id) {
            Some(t) => t,
            None => continue,
        };
        if !has_player_in_simulation_range(ctx, enemy.x, enemy.y, template.move_sim_factor * CAMERA_VIEW_RADIUS) {
            if behavior.was_simulating {
                ctx.db.enemy_behavior().behavior_id().update(EnemyBehavior { was_simulating: false, ..behavior });
            }
            continue;
        }

        // Only retarget on the tick an enemy *enters* move range — avoids
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

        // Movement runs from move_sim_factor so the enemy is already approaching by the time
        // it's close — but it only opens fire once a player is within attack_sim_factor, so it
        // never shoots from off-screen.
        if has_player_in_simulation_range(ctx, enemy.x, enemy.y, template.attack_sim_factor * CAMERA_VIEW_RADIUS) {
            for ev in tick_phase(ctx, &mut phase, phase_def) {
                let target = resolve_step_target(ev.target, behavior.aggro_target);
                ctx.db.bullet_pattern_event().insert(BulletPatternEvent {
                    event_id: 0, enemy_id: enemy.enemy_id,
                    source_step: ev.source_step,
                    origin_x: enemy.x, origin_y: enemy.y,
                    origin_offset_x: ev.offset_x, origin_offset_y: ev.offset_y,
                    base_angle_offset: ev.base_angle_offset,
                    pattern_type: ev.pattern_type,
                    target, texture_id: ev.texture_id, lifetime: ev.lifetime,
                    angular_speed: ev.angular_speed,
                    speed_acceleration: ev.speed_acceleration,
                    chunk_index: enemy.chunk_index,
                });
            }
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
pub fn control_bullets(
    ctx: &ReducerContext,
    kind: BulletControlKind,
    x: f32,
    y: f32,
    radius: f32,
    target_x: f32,
    target_y: f32,
) -> Result<(), String> {
    require_in_world(ctx)?;
    ctx.db.bullet_control_event().insert(BulletControlEvent {
        event_id: 0,
        kind,
        x,
        y,
        radius,
        target_x,
        target_y,
        duration: 0.0,
        cast_by: ctx.sender(),
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_enemy_hit(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;

    let enemy = match ctx.db.enemy().enemy_id().find(&enemy_id) {
        Some(e) => e,
        None => return Ok(()),
    };
    if enemy.immortal { return Ok(()); }

    deal_damage_to_enemy(ctx, player.profile_id, enemy);
    Ok(())
}
