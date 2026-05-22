use spacetimedb::{ReducerContext, Table, ScheduleAt, TimeDuration};
use std::time::Duration;
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::player::methods::*;
use crate::enemy::tables::*;
use crate::enemy::methods::*;
use crate::enemy::seeds::seed_default_enemies;
#[spacetimedb::reducer(init)]
pub fn init(ctx: &ReducerContext) {
    ctx.db.enemy_behavior_schedule().insert(EnemyBehaviorSchedule {
        scheduled_id: 0,
        scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_millis(100))),
    });
    ctx.db.enemy_spawn_schedule().insert(EnemySpawnSchedule {
        scheduled_id: 0,
        scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_secs(2))),
    });
    seed_default_enemies(ctx);
}

#[spacetimedb::reducer]
pub fn tick_enemy_spawn(ctx: &ReducerContext, _job: EnemySpawnSchedule) {
    if ctx.db.enemy().iter().next().is_some() { return; } // temporary: one enemy at a time

    let template = match ctx.db.enemy_template().template_id().find(&"default_enemy".to_string()) {
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
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(0);
    let chunk_rows       = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(0);

    for enemy in ctx.db.enemy().iter() {
        if !has_player_in_range(ctx, enemy.x, enemy.y, ENEMY_SIMULATION_RANGE) { continue; }

        let behavior = match ctx.db.enemy_behavior().behavior_id().find(&enemy.behavior_id) {
            Some(b) => b,
            None => continue,
        };

        let phase_idx = enemy.phase as usize;
        if phase_idx >= behavior.phases.len() { continue; }

        let mut phases = behavior.phases.clone();

        for attack in &mut phases[phase_idx].attacks {
            let fire_interval = Duration::from_secs_f32(1.0 / attack.fire_rate);
            if ctx.timestamp >= attack.last_fire_time + fire_interval {
                attack.last_fire_time = ctx.timestamp;
                let target = resolve_attack_targets(ctx, &mut attack.target, enemy.x, enemy.y);
                ctx.db.bullet_pattern_event().insert(BulletPatternEvent {
                    event_id: 0, enemy_id: enemy.enemy_id,
                    origin_x: enemy.x, origin_y: enemy.y,
                    pattern_type: attack.pattern_type.clone(),
                    target, chunk_index: enemy.chunk_index,
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
}

#[spacetimedb::reducer]
pub fn report_enemy_hit(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    require_logged_in(ctx)?;
    let enemy = match ctx.db.enemy().enemy_id().find(&enemy_id) {
        Some(e) => e,
        None => return Ok(()),
    };
    if enemy.immortal { return Ok(()); }

    let damage = compute_player_damage(ctx, ctx.sender());
    let new_hp = enemy.hp.saturating_sub(damage);

    if new_hp == 0 {
        let xp_reward = ctx.db.enemy_template().template_id().find(&enemy.template_id)
            .map(|t| t.max_hp / 10)
            .unwrap_or(10);
        ctx.db.loot_drop().insert(LootDrop {
            drop_id: 0, item_id: String::new(),
            x: enemy.x, y: enemy.y, chunk_index: enemy.chunk_index,
            expires_at: ctx.timestamp + Duration::from_secs(60),
        });
        ctx.db.enemy_behavior().behavior_id().delete(&enemy.behavior_id);
        ctx.db.enemy().enemy_id().delete(&enemy_id);
        internal_gain_xp(ctx, ctx.sender(), xp_reward);
        return Ok(());
    }

    let new_phase = ctx.db.enemy_template().template_id().find(&enemy.template_id)
        .map(|t| compute_phase(new_hp, t.max_hp, &t.phases))
        .unwrap_or(enemy.phase);

    ctx.db.enemy().enemy_id().update(Enemy { hp: new_hp, phase: new_phase, ..enemy });
    Ok(())
}
