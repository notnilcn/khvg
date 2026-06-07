use spacetimedb::{ReducerContext, Identity, Table, Timestamp};
use std::time::Duration;
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::enemy::tables::*;

pub fn has_player_in_range(ctx: &ReducerContext, x: f32, y: f32, range: f32) -> bool {
    let range_sq = range * range;
    ctx.db.logged_in_player().iter().any(|p| {
        ctx.db.player_position().profile_id().find(&p.profile_id)
            .map(|pos| {
                let dx = pos.x - x;
                let dy = pos.y - y;
                dx * dx + dy * dy <= range_sq
            })
            .unwrap_or(false)
    })
}

pub fn find_player_pos_by_id(ctx: &ReducerContext, id: Identity) -> Option<(f32, f32)> {
    ctx.db.player_position().iter().find(|p| p.player_id == id).map(|p| (p.x, p.y))
}

pub fn find_nearest_player_id(ctx: &ReducerContext, x: f32, y: f32) -> Option<Identity> {
    ctx.db.player_position().iter()
        .filter(|p| ctx.db.logged_in_player().profile_id().find(&p.profile_id).is_some())
        .min_by(|a, b| {
            let da = (a.x - x).powi(2) + (a.y - y).powi(2);
            let db = (b.x - x).powi(2) + (b.y - y).powi(2);
            da.partial_cmp(&db).unwrap_or(std::cmp::Ordering::Equal)
        })
        .map(|p| p.player_id)
}

pub fn seconds_since(ctx: &ReducerContext, since: Timestamp) -> f32 {
    let now = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    let then = since.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    now.saturating_sub(then) as f32 / 1_000_000.0
}

pub fn splitmix64(seed: u64) -> u64 {
    let h = (seed ^ (seed >> 30)).wrapping_mul(0xbf58476d1ce4e5b9);
    let h = (h ^ (h >> 27)).wrapping_mul(0x94d049bb133111eb);
    h ^ (h >> 31)
}

pub fn hash_to_unit(h: u64) -> f32 {
    (h >> 11) as f32 / (1u64 << 53) as f32
}

// Returns (is_in_active_phase, cycle_index) for a move/pause cycle measured from `since`.
pub fn cycle_phase(ctx: &ReducerContext, since: Timestamp, active_duration: f32, pause_duration: f32) -> (bool, u64) {
    let cycle = (active_duration + pause_duration).max(f32::EPSILON);
    let elapsed = seconds_since(ctx, since);
    let cycle_index = (elapsed / cycle).floor() as u64;
    (elapsed % cycle < active_duration, cycle_index)
}

pub fn recompute_aggro(ctx: &ReducerContext, x: f32, y: f32, lock_seconds: f32) -> (Option<Identity>, Timestamp) {
    let target = find_nearest_player_id(ctx, x, y);
    let locked_until = ctx.timestamp + Duration::from_secs_f32(lock_seconds.max(0.0));
    (target, locked_until)
}

pub fn build_enemy_behavior(ctx: &ReducerContext, template: &EnemyTemplate) -> EnemyBehavior {
    EnemyBehavior {
        behavior_id: 0,
        template_id: template.template_id.clone(),
        display_name: template.display_name.clone(),
        max_hp: template.max_hp,
        simulation_range: template.simulation_range,
        aggro_lock_seconds: template.aggro_lock_seconds,
        aggro_target: None,
        aggro_locked_until: ctx.timestamp,
        was_simulating: false,
        phases: template.phases.iter().map(|p| PhaseInstance {
            phase_index: p.phase_index,
            hp_threshold: p.hp_threshold,
            movement: p.movement.clone(),
            attacks: p.attacks.iter().map(|a| AttackInstance {
                attack: a.attack.clone(),
                fire_rate: a.fire_rate,
                last_fire_time: ctx.timestamp,
                seq_step_index: 0,
                seq_repeat_count: 0,
                seq_step_timer: ctx.timestamp,
                seq_active: true,
                seq_waiting: false,
            }).collect(),
        }).collect(),
        move_dir_x: 0.0,
        move_dir_y: 0.0,
        move_timer: ctx.timestamp,
    }
}

pub fn resolve_step_target(target: EnemyTarget, aggro_target: Option<Identity>) -> Option<Identity> {
    match target {
        EnemyTarget::Idle => None,
        EnemyTarget::AggroTarget => aggro_target,
    }
}

pub fn tick_sequence(ctx: &ReducerContext, attack: &mut AttackInstance, seq: &AttackSequence) -> Option<(PatternType, EnemyTarget, f32, f32, f32, String, f32)> {
    if seq.steps.is_empty() { return None; }

    if !attack.seq_active {
        if attack.fire_rate <= 0.0 { return None; }
        let cooldown = Duration::from_secs_f32(1.0 / attack.fire_rate);
        if ctx.timestamp < attack.last_fire_time + cooldown { return None; }
        attack.seq_active = true;
        attack.seq_step_index = 0;
        attack.seq_repeat_count = 0;
        attack.seq_waiting = false;
        attack.seq_step_timer = ctx.timestamp;
    }

    for _ in 0..=seq.steps.len() {
        let idx = attack.seq_step_index as usize;
        if idx >= seq.steps.len() {
            if seq.loop_sequence {
                attack.seq_step_index = 0;
                attack.seq_repeat_count = 0;
                attack.seq_waiting = false;
                continue;
            } else {
                attack.seq_active = false;
                attack.last_fire_time = ctx.timestamp;
                return None;
            }
        }
        match seq.steps[idx].clone() {
            SequenceStep::Single(s) => {
                if attack.seq_waiting {
                    if s.wait <= 0.0 || ctx.timestamp >= attack.seq_step_timer + Duration::from_secs_f32(s.wait) {
                        attack.seq_step_index += 1;
                        attack.seq_waiting = false;
                        continue;
                    }
                    return None;
                }
                attack.seq_step_timer = ctx.timestamp;
                if s.wait > 0.0 {
                    attack.seq_waiting = true;
                } else {
                    attack.seq_step_index += 1;
                }
                return Some((s.pattern_type, s.target, s.origin_offset_x, s.origin_offset_y, s.base_angle_offset, s.texture_id, s.lifetime));
            }
            SequenceStep::Repeat(r) => {
                if attack.seq_waiting {
                    if r.wait <= 0.0 || ctx.timestamp >= attack.seq_step_timer + Duration::from_secs_f32(r.wait) {
                        attack.seq_step_index += 1;
                        attack.seq_repeat_count = 0;
                        attack.seq_waiting = false;
                        continue;
                    }
                    return None;
                }
                if r.count == 0 || attack.seq_repeat_count >= r.count {
                    if r.wait > 0.0 {
                        if r.count == 0 { attack.seq_step_timer = ctx.timestamp; }
                        attack.seq_waiting = true;
                        return None;
                    }
                    attack.seq_step_index += 1;
                    attack.seq_repeat_count = 0;
                    continue;
                }
                let should_fire = attack.seq_repeat_count == 0
                    || ctx.timestamp >= attack.seq_step_timer + Duration::from_secs_f32(r.interval);
                if should_fire {
                    attack.seq_repeat_count += 1;
                    attack.seq_step_timer = ctx.timestamp;
                    return Some((r.pattern_type, r.target, r.origin_offset_x, r.origin_offset_y, r.base_angle_offset, r.texture_id, r.lifetime));
                }
                return None;
            }
        }
    }
    None
}

pub fn compute_phase(hp: u32, max_hp: u32, phases: &[PhaseDef]) -> u8 {
    let hp_pct = hp as f32 / max_hp as f32;
    let mut phase = 0u8;
    for def in phases {
        if hp_pct <= def.hp_threshold && def.phase_index > phase {
            phase = def.phase_index;
        }
    }
    phase
}

pub fn apply_movement(ctx: &ReducerContext, enemy: &Enemy, behavior: &EnemyBehavior, movement: &MovementBehavior) -> (f32, f32, f32, f32, Timestamp) {
    let dt = BEHAVIOR_TICK_DT;
    let idle = (enemy.x, enemy.y, behavior.move_dir_x, behavior.move_dir_y, behavior.move_timer);
    match movement {
        MovementBehavior::Stationary => idle,
        MovementBehavior::Wander(p) => {
            let (active, cycle_index) = cycle_phase(ctx, enemy.spawn_time, p.active_duration, p.pause_duration);
            if !active { return idle; }
            let angle = hash_to_unit(splitmix64(enemy.enemy_id.wrapping_add(cycle_index))) * std::f32::consts::TAU;
            let (dir_x, dir_y) = (angle.cos(), angle.sin());
            (enemy.x + dir_x * p.speed * dt, enemy.y + dir_y * p.speed * dt, dir_x, dir_y, behavior.move_timer)
        }
        MovementBehavior::Chase(p) => {
            let (active, _) = cycle_phase(ctx, enemy.spawn_time, p.active_duration, p.pause_duration);
            if !active { return idle; }
            match behavior.aggro_target.and_then(|id| find_player_pos_by_id(ctx, id)) {
                Some((px, py)) => {
                    let dx = px - enemy.x;
                    let dy = py - enemy.y;
                    let len = (dx * dx + dy * dy).sqrt().max(f32::EPSILON);
                    let (nx, ny) = (dx / len, dy / len);
                    (enemy.x + nx * p.speed * dt, enemy.y + ny * p.speed * dt, nx, ny, behavior.move_timer)
                }
                None => idle,
            }
        }
        MovementBehavior::Flee(p) => {
            let (active, _) = cycle_phase(ctx, enemy.spawn_time, p.active_duration, p.pause_duration);
            if !active { return idle; }
            match behavior.aggro_target.and_then(|id| find_player_pos_by_id(ctx, id)) {
                Some((px, py)) => {
                    let dx = enemy.x - px;
                    let dy = enemy.y - py;
                    let dist = (dx * dx + dy * dy).sqrt();
                    if dist < p.min_range {
                        let (nx, ny) = (dx / dist.max(f32::EPSILON), dy / dist.max(f32::EPSILON));
                        (enemy.x + nx * p.speed * dt, enemy.y + ny * p.speed * dt, nx, ny, behavior.move_timer)
                    } else {
                        idle
                    }
                }
                None => idle,
            }
        }
    }
}

#[allow(dead_code)]
pub fn spawn_from_zones(ctx: &ReducerContext) {
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(0);
    let chunk_rows       = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(0);

    for zone in ctx.db.enemy_zone().iter() {
        let (center_hq, center_hr) = chunk_center_hex(zone.center_chunk_q, zone.center_chunk_r, chunk_hex_radius);
        let (center_x, center_y) = hex_to_world(center_hq, center_hr, hex_outer_radius);

        if !has_player_in_range(ctx, center_x, center_y, zone.spawn_radius) { continue; }

        let spawn_radius_sq = zone.spawn_radius * zone.spawn_radius;
        let live_count = ctx.db.enemy().iter()
            .filter(|e| zone.template_ids.contains(&e.template_id) && (e.x - center_x).powi(2) + (e.y - center_y).powi(2) <= spawn_radius_sq)
            .count() as u32;

        if live_count >= zone.max_enemies || zone.template_ids.is_empty() { continue; }

        let us = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros() as u64;
        let template_idx = (zone.zone_id ^ (us / 1_000_000)) as usize % zone.template_ids.len();
        let template = match ctx.db.enemy_template().template_id().find(&zone.template_ids[template_idx]) {
            Some(t) => t,
            None => continue,
        };

        let angle_hash = zone.zone_id.wrapping_mul(0x9e3779b97f4a7c15).wrapping_add(us);
        let angle = (angle_hash as f32 / u64::MAX as f32) * std::f32::consts::TAU;
        let spawn_x = center_x + angle.cos() * zone.spawn_radius * 0.5;
        let spawn_y = center_y + angle.sin() * zone.spawn_radius * 0.5;
        let (spawn_x, spawn_y) = wrap_world_pos(spawn_x, spawn_y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);

        let (cq, cr) = world_to_chunk(spawn_x, spawn_y, chunk_hex_radius, hex_outer_radius);
        let chunk_index = spiral_chunk_index(cq, cr);
        let inserted_behavior = ctx.db.enemy_behavior().insert(build_enemy_behavior(ctx, &template));

        ctx.db.enemy().insert(Enemy {
            enemy_id: 0, template_id: template.template_id, behavior_id: inserted_behavior.behavior_id,
            x: spawn_x, y: spawn_y, spawn_time: ctx.timestamp,
            hp: template.max_hp, phase: 0, is_elite: false, immortal: false, chunk_index,
        });
    }
}
