use spacetimedb::{ReducerContext, Identity, Table, Timestamp};
use std::time::Duration;
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;

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

pub fn delete_enemy_behavior(ctx: &ReducerContext, behavior_id: u64) {
    for phase in ctx.db.enemy_phases().behavior_id().filter(&behavior_id) {
        for attack in ctx.db.enemy_attack().phase_id().filter(&phase.phase_id) {
            for step in ctx.db.enemy_sequence_step().attack_id().filter(&attack.attack_id) {
                if let SequenceStepRef::Repeat(repeat_id) = step.step {
                    ctx.db.repeat_step_instance().repeat_id().delete(&repeat_id);
                }
                ctx.db.enemy_sequence_step().step_id().delete(&step.step_id);
            }
            ctx.db.enemy_attack().attack_id().delete(&attack.attack_id);
        }
        ctx.db.enemy_phases().phase_id().delete(&phase.phase_id);
    }
    ctx.db.enemy_behavior().behavior_id().delete(&behavior_id);
}

pub fn build_enemy_behavior(ctx: &ReducerContext, template: &EnemyTemplate) -> EnemyBehavior {
    let behavior = ctx.db.enemy_behavior().insert(EnemyBehavior {
        behavior_id: 0,
        aggro_target: None,
        aggro_locked_until: ctx.timestamp,
        was_simulating: false,
        active_phase_index: 0,
        velocity_x: 0.0,
        velocity_y: 0.0,
    });

    for (phase_index, phase_def) in template.phases.iter().enumerate() {
        let mut phase = ctx.db.enemy_phases().insert(EnemyPhase {
            phase_id: 0,
            behavior_id: behavior.behavior_id,
            phase_index: phase_def.phase_index,
            hp_threshold: phase_def.hp_threshold,
            movement_def_id: phase_def.movement_def_id,
            phase_loop_delay: phase_def.phase_loop_delay,
            phase_waiting: false,
            phase_loop_timer: ctx.timestamp,
            move_cycle_started_at: ctx.timestamp,
        });

        for (attack_index, attack_def) in phase_def.attacks.iter().enumerate() {
            let attack = ctx.db.enemy_attack().insert(EnemyAttack {
                attack_id: 0,
                phase_id: phase.phase_id,
                attack_index: attack_index as u32,
                step_index: 0,
                step_timer: ctx.timestamp,
                step_waiting: false,
                start_delaying: false,
            });

            for (step_index, step_def) in attack_def.steps.iter().enumerate() {
                let step_ref = match step_def {
                    SequenceStepDef::Single(def_id) => SequenceStepRef::Single(*def_id),
                    SequenceStepDef::Multi(def_id) => SequenceStepRef::Multi(MultiStepRef { def_id: *def_id, shot_index: 0 }),
                    SequenceStepDef::Repeat(def_id) => {
                        let r = match ctx.db.repeat_step_def().def_id().find(def_id) {
                            Some(r) => r,
                            None => continue,
                        };
                        let inserted = ctx.db.repeat_step_instance().insert(RepeatStepInstance {
                            repeat_id: 0,
                            pattern_type: r.pattern_type.clone(),
                            target: r.target.clone(),
                            offset_x: r.offset_x,
                            offset_y: r.offset_y,
                            base_angle_offset: r.base_angle_offset,
                            texture_id: r.texture_id.clone(),
                            lifetime: r.lifetime,
                            damage: r.damage,
                            repeat_interval: r.repeat_interval,
                            repeat_target: r.repeat_target,
                            next_step_delay: r.next_step_delay,
                            repeat_count: 0,
                        });
                        SequenceStepRef::Repeat(inserted.repeat_id)
                    }
                };
                ctx.db.enemy_sequence_step().insert(EnemySequenceStep {
                    step_id: 0,
                    attack_id: attack.attack_id,
                    step_index: step_index as u32,
                    step: step_ref,
                });
            }
        }

        if phase_index == 0 {
            begin_phase_cycle(ctx, &mut phase, phase_def);
            ctx.db.enemy_phases().phase_id().update(phase);
        }
    }

    behavior
}

pub fn resolve_step_target(target: EnemyTarget, aggro_target: Option<Identity>) -> Option<Identity> {
    match target {
        EnemyTarget::Idle => None,
        EnemyTarget::AggroTarget => aggro_target,
    }
}

pub fn is_attack_finished(attack: &EnemyAttack, attack_def: &AttackSequence) -> bool {
    attack.step_index as usize >= attack_def.steps.len()
}

fn reset_repeat_counts(ctx: &ReducerContext, attack_id: u64) {
    for step in ctx.db.enemy_sequence_step().attack_id().filter(&attack_id) {
        if let SequenceStepRef::Repeat(repeat_id) = step.step {
            if let Some(mut r) = ctx.db.repeat_step_instance().repeat_id().find(&repeat_id) {
                r.repeat_count = 0;
                ctx.db.repeat_step_instance().repeat_id().update(r);
            }
        }
    }
}

pub fn activate_all_attacks(ctx: &ReducerContext, phase_id: u64, phase_def: &PhaseDef) {
    for mut attack in ctx.db.enemy_attack().phase_id().filter(&phase_id) {
        let attack_def = &phase_def.attacks[attack.attack_index as usize];
        attack.step_index = 0;
        attack.step_waiting = false;
        attack.step_timer = ctx.timestamp;
        attack.start_delaying = attack_def.start_delay > 0.0;
        reset_repeat_counts(ctx, attack.attack_id);
        ctx.db.enemy_attack().attack_id().update(attack);
    }
}

pub fn begin_phase_cycle(ctx: &ReducerContext, phase: &mut EnemyPhase, phase_def: &PhaseDef) {
    phase.move_cycle_started_at = ctx.timestamp;
    if phase.phase_loop_delay > 0.0 {
        phase.phase_waiting = true;
        phase.phase_loop_timer = ctx.timestamp;
        for mut attack in ctx.db.enemy_attack().phase_id().filter(&phase.phase_id) {
            attack.step_index = 0;
            attack.step_waiting = false;
            attack.start_delaying = false;
            reset_repeat_counts(ctx, attack.attack_id);
            ctx.db.enemy_attack().attack_id().update(attack);
        }
    } else {
        phase.phase_waiting = false;
        activate_all_attacks(ctx, phase.phase_id, phase_def);
    }
}

pub type BulletStepEvent = (PatternType, EnemyTarget, f32, f32, f32, String, f32, SequenceStepRef);

pub fn tick_phase(ctx: &ReducerContext, phase: &mut EnemyPhase, phase_def: &PhaseDef) -> Vec<BulletStepEvent> {
    if phase.phase_waiting {
        if ctx.timestamp < phase.phase_loop_timer + Duration::from_secs_f32(phase.phase_loop_delay) {
            return vec![];
        }
        phase.phase_waiting = false;
        activate_all_attacks(ctx, phase.phase_id, phase_def);
    }

    let mut events = Vec::new();
    let mut all_finished = true;
    for mut attack in ctx.db.enemy_attack().phase_id().filter(&phase.phase_id) {
        let attack_def = &phase_def.attacks[attack.attack_index as usize];
        if is_attack_finished(&attack, attack_def) { continue; }
        events.extend(tick_sequence(ctx, &mut attack, attack_def));
        if !is_attack_finished(&attack, attack_def) { all_finished = false; }
        ctx.db.enemy_attack().attack_id().update(attack);
    }

    if all_finished {
        begin_phase_cycle(ctx, phase, phase_def);
    }

    events
}

pub fn tick_sequence(ctx: &ReducerContext, attack: &mut EnemyAttack, attack_def: &AttackSequence) -> Vec<BulletStepEvent> {
    if attack_def.steps.is_empty() {
        return vec![];
    }

    if attack.start_delaying {
        if attack_def.start_delay > 0.0 && ctx.timestamp < attack.step_timer + Duration::from_secs_f32(attack_def.start_delay) {
            return vec![];
        }
        attack.start_delaying = false;
        attack.step_timer = ctx.timestamp;
    }

    for _ in 0..=attack_def.steps.len() {
        let idx = attack.step_index;
        if idx as usize >= attack_def.steps.len() {
            return vec![];
        }
        let step_row = match ctx.db.enemy_sequence_step().attack_id().filter(&attack.attack_id).find(|s| s.step_index == idx) {
            Some(s) => s,
            None => return vec![],
        };
        match step_row.step {
            SequenceStepRef::Single(def_id) => {
                let s = match ctx.db.single_step_def().def_id().find(&def_id) {
                    Some(s) => s,
                    None => return vec![],
                };
                if attack.step_waiting {
                    if s.next_step_delay <= 0.0 || ctx.timestamp >= attack.step_timer + Duration::from_secs_f32(s.next_step_delay) {
                        attack.step_index += 1;
                        attack.step_waiting = false;
                        continue;
                    }
                    return vec![];
                }
                attack.step_timer = ctx.timestamp;
                let event = (s.pattern_type.clone(), s.target.clone(), s.offset_x, s.offset_y, s.base_angle_offset, s.texture_id.clone(), s.lifetime, SequenceStepRef::Single(def_id));
                if s.next_step_delay > 0.0 {
                    attack.step_waiting = true;
                } else {
                    attack.step_index += 1;
                }
                return vec![event];
            }
            SequenceStepRef::Repeat(repeat_id) => {
                let mut r = match ctx.db.repeat_step_instance().repeat_id().find(&repeat_id) {
                    Some(r) => r,
                    None => return vec![],
                };
                if attack.step_waiting {
                    if r.next_step_delay <= 0.0 || ctx.timestamp >= attack.step_timer + Duration::from_secs_f32(r.next_step_delay) {
                        attack.step_index += 1;
                        r.repeat_count = 0;
                        attack.step_waiting = false;
                        ctx.db.repeat_step_instance().repeat_id().update(r);
                        continue;
                    }
                    return vec![];
                }
                if r.repeat_target == 0 || r.repeat_count >= r.repeat_target {
                    if r.next_step_delay > 0.0 {
                        if r.repeat_target == 0 { attack.step_timer = ctx.timestamp; }
                        attack.step_waiting = true;
                        return vec![];
                    }
                    attack.step_index += 1;
                    r.repeat_count = 0;
                    ctx.db.repeat_step_instance().repeat_id().update(r);
                    continue;
                }
                let should_fire = r.repeat_count == 0
                    || ctx.timestamp >= attack.step_timer + Duration::from_secs_f32(r.repeat_interval);
                if should_fire {
                    r.repeat_count += 1;
                    attack.step_timer = ctx.timestamp;
                    let event = (r.pattern_type.clone(), r.target.clone(), r.offset_x, r.offset_y, r.base_angle_offset, r.texture_id.clone(), r.lifetime, SequenceStepRef::Repeat(repeat_id));
                    ctx.db.repeat_step_instance().repeat_id().update(r);
                    return vec![event];
                }
                return vec![];
            }
            SequenceStepRef::Multi(multi_ref) => {
                let m = match ctx.db.multi_step_def().def_id().find(&multi_ref.def_id) {
                    Some(m) => m,
                    None => return vec![],
                };
                if attack.step_waiting {
                    if m.next_step_delay <= 0.0 || ctx.timestamp >= attack.step_timer + Duration::from_secs_f32(m.next_step_delay) {
                        attack.step_index += 1;
                        attack.step_waiting = false;
                        continue;
                    }
                    return vec![];
                }
                attack.step_timer = ctx.timestamp;
                let events = m.shots.iter().enumerate().map(|(i, s)| (s.pattern_type.clone(), s.target.clone(), s.offset_x, s.offset_y, s.base_angle_offset, s.texture_id.clone(), s.lifetime, SequenceStepRef::Multi(MultiStepRef { def_id: multi_ref.def_id, shot_index: i as u32 }))).collect();
                if m.next_step_delay > 0.0 {
                    attack.step_waiting = true;
                } else {
                    attack.step_index += 1;
                }
                return events;
            }
        }
    }
    vec![]
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

pub fn apply_movement(ctx: &ReducerContext, enemy: &Enemy, behavior: &EnemyBehavior, phase: &EnemyPhase) -> (f32, f32, f32, f32) {
    let dt = BEHAVIOR_TICK_DT;
    let idle = (enemy.x, enemy.y, behavior.velocity_x, behavior.velocity_y);
    let movement = match ctx.db.movement_def().def_id().find(&phase.movement_def_id) {
        Some(m) => m.movement,
        None => return idle,
    };
    match movement {
        MovementBehavior::Stationary => idle,
        MovementBehavior::Wander(p) => {
            let (active, cycle_index) = cycle_phase(ctx, phase.move_cycle_started_at, p.active_duration, p.pause_duration);
            if !active { return idle; }
            let angle = hash_to_unit(splitmix64(enemy.enemy_id.wrapping_add(cycle_index))) * std::f32::consts::TAU;
            let (dir_x, dir_y) = (angle.cos(), angle.sin());
            (enemy.x + dir_x * p.speed * dt, enemy.y + dir_y * p.speed * dt, dir_x, dir_y)
        }
        MovementBehavior::Chase(p) => {
            let (active, _) = cycle_phase(ctx, phase.move_cycle_started_at, p.active_duration, p.pause_duration);
            if !active { return idle; }
            match behavior.aggro_target.and_then(|id| find_player_pos_by_id(ctx, id)) {
                Some((px, py)) => {
                    let dx = px - enemy.x;
                    let dy = py - enemy.y;
                    let len = (dx * dx + dy * dy).sqrt().max(f32::EPSILON);
                    let (nx, ny) = (dx / len, dy / len);
                    (enemy.x + nx * p.speed * dt, enemy.y + ny * p.speed * dt, nx, ny)
                }
                None => idle,
            }
        }
        MovementBehavior::Flee(p) => {
            let (active, _) = cycle_phase(ctx, phase.move_cycle_started_at, p.active_duration, p.pause_duration);
            if !active { return idle; }
            match behavior.aggro_target.and_then(|id| find_player_pos_by_id(ctx, id)) {
                Some((px, py)) => {
                    let dx = enemy.x - px;
                    let dy = enemy.y - py;
                    let dist = (dx * dx + dy * dy).sqrt();
                    if dist < p.min_range {
                        let (nx, ny) = (dx / dist.max(f32::EPSILON), dy / dist.max(f32::EPSILON));
                        (enemy.x + nx * p.speed * dt, enemy.y + ny * p.speed * dt, nx, ny)
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
        let inserted_behavior = build_enemy_behavior(ctx, &template);

        ctx.db.enemy().insert(Enemy {
            enemy_id: 0, template_id: template.template_id, behavior_id: inserted_behavior.behavior_id,
            x: spawn_x, y: spawn_y, spawn_time: ctx.timestamp,
            hp: template.max_hp, phase: 0, is_elite: false, immortal: false, chunk_index,
        });
    }
}
