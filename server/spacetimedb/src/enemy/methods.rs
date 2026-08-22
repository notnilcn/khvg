use spacetimedb::{ReducerContext, Identity, Table, Timestamp};
use std::time::Duration;
use crate::main::global::*;
use crate::world::prng::{hash_to_unit, next_unit, splitmix64};
use crate::world::hex::{hex_to_world, spiral_chunk_index, world_to_chunk};
use crate::world::wrap::{wrap_world_pos, wrapped_distance_sq};
use crate::world::aoi::{surrounding_chunk_indices, chunk_indices_covering};
use crate::world::tables::MapConfig;
use crate::world::instance_tables::*;
use crate::player::tables::*;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;

fn nearby_player_position_chunks(ctx: &ReducerContext, x: f32, y: f32, rings: i32) -> Vec<i64> {
    let cfg = MapConfig::load(ctx, 0, 0);
    let (cq, cr) = world_to_chunk(x, y, cfg.chunk_hex_radius, cfg.hex_outer_radius);
    surrounding_chunk_indices(cq, cr, cfg.chunk_cols, cfg.chunk_rows, Some(rings))
}

pub fn has_player_in_simulation_range(ctx: &ReducerContext, x: f32, y: f32, range: f32) -> bool {
    let range_sq = range * range;
    nearby_player_position_chunks(ctx, x, y, SIMULATION_CHUNK_RINGS).iter().any(|idx| {
        ctx.db.player_position().chunk_index().filter(idx)
            // PlayerPosition outlives a logout/disconnect (only deleted on profile delete or
            // death), so without this an enemy near a logged-out player's last known position
            // would simulate/spawn forever against a ghost.
            .filter(|pos| ctx.db.logged_in_player().profile_id().find(&pos.profile_id).is_some())
            .any(|pos| {
                let dx = pos.x - x;
                let dy = pos.y - y;
                dx * dx + dy * dy <= range_sq
            })
    })
}

pub fn has_player_near_spawn_point(ctx: &ReducerContext, x: f32, y: f32) -> bool {
    nearby_player_position_chunks(ctx, x, y, SPAWN_LOOKAHEAD_CHUNK_RINGS).iter()
        .any(|idx| ctx.db.player_position().chunk_index().filter(idx)
            .any(|pos| ctx.db.logged_in_player().profile_id().find(&pos.profile_id).is_some()))
}

pub fn find_player_pos_by_id(ctx: &ReducerContext, id: Identity) -> Option<(f32, f32)> {
    ctx.db.logged_in_player().player_id().find(id)?;
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

/// Reducer-side AOI query: every enemy whose (wrap-aware) distance to (x, y) is within
/// `radius`, resolved through the Enemy.chunk_index btree. The view-side nearby_indices
/// helpers are ViewContext-only and cannot serve reducers. Used by ability targeting
/// (cursor-pick) and spell/zone shape queries.
pub fn enemies_near_point(ctx: &ReducerContext, x: f32, y: f32, radius: f32) -> Vec<Enemy> {
    let cfg = MapConfig::load(ctx, 0, 0);
    let lap_q = (cfg.lap_q_x, cfg.lap_q_y);
    let lap_r = (cfg.lap_r_x, cfg.lap_r_y);
    let radius_sq = radius * radius;
    chunk_indices_covering(x, y, radius, cfg.chunk_hex_radius, cfg.hex_outer_radius, cfg.chunk_cols, cfg.chunk_rows)
        .iter()
        .flat_map(|idx| ctx.db.enemy().chunk_index().filter(idx))
        .filter(|e| wrapped_distance_sq(x, y, e.x, e.y, lap_q, lap_r) <= radius_sq)
        .collect()
}

pub fn seconds_since(ctx: &ReducerContext, since: Timestamp) -> f32 {
    let now = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    let then = since.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    now.saturating_sub(then) as f32 / 1_000_000.0
}

/// A step's per-bullet damage, resolved from a SequenceStepRef. Shared by report_hit (hit
/// attribution) and the behavior tick (stamps BulletPatternEvent.damage for damage-aware
/// bullet controllers).
pub fn step_damage(ctx: &ReducerContext, step: &SequenceStepRef) -> Option<u32> {
    match step {
        SequenceStepRef::Single(def_id) => ctx.db.single_step_def().def_id().find(def_id).map(|s| s.damage),
        SequenceStepRef::Repeat(repeat_id) => ctx.db.repeat_step_instance().repeat_id().find(repeat_id).map(|r| r.damage),
        SequenceStepRef::Multi(multi_ref) => ctx.db.multi_step_def().def_id().find(&multi_ref.def_id).and_then(|m| m.shots.get(multi_ref.shot_index as usize).map(|s| s.damage)),
    }
}

/// Fire-pacing consult (Stunned on an Enraged template): every interval runs faster —
/// `base / (1 + magnitude)`. Anything else (no stun, Pause/Slowed template) returns base.
/// Applied at every wall-clock fire-timing comparison in tick_phase/tick_sequence.
pub fn effective_interval(ctx: &ReducerContext, base: f32, enemy_id: u64, stun_behavior: &StunBehavior) -> f32 {
    if *stun_behavior == StunBehavior::Enraged {
        if let Some(magnitude) = crate::status::methods::stunned_magnitude(ctx, enemy_id) {
            return base / (1.0 + magnitude);
        }
    }
    base
}

// Rotates a step's origin offset by `angle_deg` (degrees). Repeat steps apply this with
// their accumulated spin (angle_step * repeat_count), so an emitter mounted on a rotating
// arm orbits with the same angle its shots rotate by. Zero angle or zero offset is a no-op.
fn rotate_offset(x: f32, y: f32, angle_deg: f32) -> (f32, f32) {
    if angle_deg == 0.0 || (x == 0.0 && y == 0.0) { return (x, y); }
    let r = angle_deg.to_radians();
    (x * r.cos() - y * r.sin(), x * r.sin() + y * r.cos())
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
    // Collect before deleting: removing rows while iterating a btree index filter skips
    // entries (the same reason teardown_profile collects first).
    let phases = ctx.db.enemy_phases().behavior_id().filter(&behavior_id).collect::<Vec<_>>();
    for phase in phases {
        for attack in ctx.db.enemy_attack().phase_id().filter(&phase.phase_id).collect::<Vec<_>>() {
            for step in ctx.db.enemy_sequence_step().attack_id().filter(&attack.attack_id).collect::<Vec<_>>() {
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
                loop_waiting: false,
            });

            for (step_index, step_def) in attack_def.steps.iter().enumerate() {
                let step_ref = match step_def {
                    SequenceStepDef::Single(def_id) => SequenceStepRef::Single(*def_id),
                    SequenceStepDef::Multi(def_id) => SequenceStepRef::Multi(MultiStepRef { def_id: *def_id, shot_index: 0 }),
                    // RepeatStepDef is shared template data; each enemy gets its own
                    // RepeatStepInstance so repeat_count can advance independently.
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
                            angular_speed: r.angular_speed,
                            speed_acceleration: r.speed_acceleration,
                            repeat_interval: r.repeat_interval,
                            repeat_target: r.repeat_target,
                            angle_step: r.angle_step,
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

/// Spawn one enemy from a template: build its behavior tree (build_enemy_behavior) and insert
/// the flat Enemy row pointing at it. The behavior tree (EnemyBehavior plus its EnemyPhase/
/// EnemyAttack/EnemySequenceStep/RepeatStepInstance rows) is the enemy's component tree, with
/// behavior_id/enemy_id as the entity id joining it — the Enemy row stays flat by design
/// (structure-only refactor: no schema changes).
pub fn spawn_enemy_archetype(ctx: &ReducerContext, template: &EnemyTemplate, x: f32, y: f32, is_elite: bool, immortal: bool, chunk_index: i64) -> Enemy {
    let behavior = build_enemy_behavior(ctx, template);
    ctx.db.enemy().insert(Enemy {
        enemy_id: 0, template_id: template.template_id.clone(), behavior_id: behavior.behavior_id,
        x, y, spawn_time: ctx.timestamp,
        hp: template.max_hp, phase: 0, is_elite, immortal, stagger: 0.0, chunk_index,
    })
}

/// Despawn one enemy: delete the flat Enemy row together with its whole behavior tree
/// (delete_enemy_behavior), so no orphan component rows outlive the entity.
pub fn despawn_enemy_archetype(ctx: &ReducerContext, enemy: &Enemy) {
    delete_enemy_behavior(ctx, enemy.behavior_id);
    ctx.db.enemy().enemy_id().delete(enemy.enemy_id);
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
        attack.loop_waiting = false;
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
            attack.loop_waiting = false;
            reset_repeat_counts(ctx, attack.attack_id);
            ctx.db.enemy_attack().attack_id().update(attack);
        }
    } else {
        phase.phase_waiting = false;
        activate_all_attacks(ctx, phase.phase_id, phase_def);
    }
}

/// One fired shot, collected by tick_phase/tick_sequence and inserted as a BulletPatternEvent
/// by the behavior reducer. offset/base_angle_offset are already baked (angle_step applied);
/// the client renders them verbatim.
pub struct BulletStepEvent {
    pub pattern_type: PatternType,
    pub target: EnemyTarget,
    pub offset_x: f32,
    pub offset_y: f32,
    pub base_angle_offset: f32,
    pub texture_id: String,
    pub lifetime: f32,
    pub angular_speed: f32,
    pub speed_acceleration: f32,
    pub source_step: SequenceStepRef,
}

// Per-tick entry point for one phase. Advances every attack sequence in the phase by at
// most one step each, collecting any bullet events fired this tick. Once all attacks have
// exhausted their steps, the phase loop restarts via begin_phase_cycle — except sequences
// with loop_delay >= 0, which restart themselves after their loop delay and never count
// toward the phase-level restart (a self-paced emitter must not gate or be gated by its
// siblings). enemy_id/stun_behavior feed effective_interval: a Stunned Enraged enemy runs
// every pacing interval faster.
pub fn tick_phase(ctx: &ReducerContext, enemy_id: u64, stun_behavior: &StunBehavior, phase: &mut EnemyPhase, phase_def: &PhaseDef) -> Vec<BulletStepEvent> {
    if phase.phase_waiting {
        if ctx.timestamp < phase.phase_loop_timer + Duration::from_secs_f32(effective_interval(ctx, phase.phase_loop_delay, enemy_id, stun_behavior)) {
            return vec![];
        }
        phase.phase_waiting = false;
        activate_all_attacks(ctx, phase.phase_id, phase_def);
    }

    let mut events = Vec::new();
    let mut all_finished = true;
    for mut attack in ctx.db.enemy_attack().phase_id().filter(&phase.phase_id) {
        let attack_def = &phase_def.attacks[attack.attack_index as usize];
        if attack_def.loop_delay >= 0.0 {
            all_finished = false;
            if is_attack_finished(&attack, attack_def) {
                if !attack.loop_waiting {
                    attack.loop_waiting = true;
                    attack.step_timer = ctx.timestamp;
                }
                if ctx.timestamp >= attack.step_timer + Duration::from_secs_f32(effective_interval(ctx, attack_def.loop_delay, enemy_id, stun_behavior)) {
                    attack.step_index = 0;
                    attack.step_waiting = false;
                    attack.loop_waiting = false;
                    attack.step_timer = ctx.timestamp;
                    reset_repeat_counts(ctx, attack.attack_id);
                } else {
                    ctx.db.enemy_attack().attack_id().update(attack);
                    continue;
                }
            }
            if !is_attack_finished(&attack, attack_def) {
                events.extend(tick_sequence(ctx, enemy_id, stun_behavior, &mut attack, attack_def));
            }
            ctx.db.enemy_attack().attack_id().update(attack);
            continue;
        }
        if is_attack_finished(&attack, attack_def) { continue; }
        events.extend(tick_sequence(ctx, enemy_id, stun_behavior, &mut attack, attack_def));
        if !is_attack_finished(&attack, attack_def) { all_finished = false; }
        ctx.db.enemy_attack().attack_id().update(attack);
    }

    if all_finished {
        begin_phase_cycle(ctx, phase, phase_def);
    }

    events
}

// Advances one attack by at most one step transition per call. Step progress
// (step_index/step_waiting/step_timer) is persisted on the EnemyAttack row between ticks.
// Single/Multi: fire once, then optionally wait `next_step_delay` before moving to the next
// step. Repeat: re-fire on `repeat_interval` until `repeat_count` reaches `repeat_target`,
// then behaves like the others. Every wall-clock comparison goes through effective_interval
// so a Stunned Enraged enemy fires proportionally faster.
pub fn tick_sequence(ctx: &ReducerContext, enemy_id: u64, stun_behavior: &StunBehavior, attack: &mut EnemyAttack, attack_def: &AttackSequence) -> Vec<BulletStepEvent> {
    if attack_def.steps.is_empty() {
        return vec![];
    }

    if attack.start_delaying {
        if attack_def.start_delay > 0.0 && ctx.timestamp < attack.step_timer + Duration::from_secs_f32(effective_interval(ctx, attack_def.start_delay, enemy_id, stun_behavior)) {
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
                    if s.next_step_delay <= 0.0 || ctx.timestamp >= attack.step_timer + Duration::from_secs_f32(effective_interval(ctx, s.next_step_delay, enemy_id, stun_behavior)) {
                        attack.step_index += 1;
                        attack.step_waiting = false;
                        continue;
                    }
                    return vec![];
                }
                attack.step_timer = ctx.timestamp;
                let event = BulletStepEvent {
                    pattern_type: s.pattern_type.clone(), target: s.target.clone(),
                    offset_x: s.offset_x, offset_y: s.offset_y,
                    base_angle_offset: s.base_angle_offset,
                    texture_id: s.texture_id.clone(), lifetime: s.lifetime,
                    angular_speed: s.angular_speed,
                    speed_acceleration: s.speed_acceleration,
                    source_step: SequenceStepRef::Single(def_id),
                };
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
                    if r.next_step_delay <= 0.0 || ctx.timestamp >= attack.step_timer + Duration::from_secs_f32(effective_interval(ctx, r.next_step_delay, enemy_id, stun_behavior)) {
                        attack.step_index += 1;
                        r.repeat_count = 0;
                        attack.step_waiting = false;
                        ctx.db.repeat_step_instance().repeat_id().update(r);
                        continue;
                    }
                    return vec![];
                }
                if r.repeat_target > 0 && r.repeat_count >= r.repeat_target {
                    if r.next_step_delay > 0.0 {
                        attack.step_waiting = true;
                        return vec![];
                    }
                    attack.step_index += 1;
                    r.repeat_count = 0;
                    ctx.db.repeat_step_instance().repeat_id().update(r);
                    continue;
                }
                // Fire with catch-up: the behavior tick is 100 ms (BEHAVIOR_TICK_DT), so a
                // repeat_interval below that can only reach its true rate by firing multiple
                // times per tick. step_timer advances by whole intervals — no drift. The cap
                // bounds the burst an enemy dumps after a stall (e.g. re-entering sim range).
                // angle_step bakes the per-shot rotation (and matching origin-offset orbit)
                // in here, so the client renders spinning emitters verbatim.
                let interval = Duration::from_secs_f32(effective_interval(ctx, r.repeat_interval.max(0.001), enemy_id, stun_behavior));
                let mut events = Vec::new();
                while (r.repeat_target == 0 || r.repeat_count < r.repeat_target) && events.len() < MAX_CATCHUP_SHOTS {
                    if r.repeat_count > 0 && ctx.timestamp < attack.step_timer + interval { break; }
                    let spin = r.angle_step * r.repeat_count as f32;
                    let (offset_x, offset_y) = rotate_offset(r.offset_x, r.offset_y, spin);
                    events.push(BulletStepEvent {
                        pattern_type: r.pattern_type.clone(), target: r.target.clone(),
                        offset_x, offset_y,
                        base_angle_offset: r.base_angle_offset + spin,
                        texture_id: r.texture_id.clone(), lifetime: r.lifetime,
                        angular_speed: r.angular_speed,
                        speed_acceleration: r.speed_acceleration,
                        source_step: SequenceStepRef::Repeat(repeat_id),
                    });
                    r.repeat_count += 1;
                    attack.step_timer = if r.repeat_count > 1 { attack.step_timer + interval } else { ctx.timestamp };
                }
                ctx.db.repeat_step_instance().repeat_id().update(r);
                return events;
            }
            SequenceStepRef::Multi(multi_ref) => {
                let m = match ctx.db.multi_step_def().def_id().find(&multi_ref.def_id) {
                    Some(m) => m,
                    None => return vec![],
                };
                if attack.step_waiting {
                    if m.next_step_delay <= 0.0 || ctx.timestamp >= attack.step_timer + Duration::from_secs_f32(effective_interval(ctx, m.next_step_delay, enemy_id, stun_behavior)) {
                        attack.step_index += 1;
                        attack.step_waiting = false;
                        continue;
                    }
                    return vec![];
                }
                attack.step_timer = ctx.timestamp;
                let events = m.shots.iter().enumerate().map(|(i, s)| BulletStepEvent {
                    pattern_type: s.pattern_type.clone(), target: s.target.clone(),
                    offset_x: s.offset_x, offset_y: s.offset_y,
                    base_angle_offset: s.base_angle_offset,
                    texture_id: s.texture_id.clone(), lifetime: s.lifetime,
                    angular_speed: s.angular_speed,
                    speed_acceleration: s.speed_acceleration,
                    source_step: SequenceStepRef::Multi(MultiStepRef { def_id: multi_ref.def_id, shot_index: i as u32 }),
                }).collect();
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

// Picks the highest phase_index whose hp_threshold has been crossed (hp_pct <= threshold).
// Phases are authored in descending-threshold order, but we take the max defensively in
// case that ordering assumption is ever violated.
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

pub fn apply_movement(ctx: &ReducerContext, enemy: &Enemy, behavior: &EnemyBehavior, phase: &EnemyPhase, stun_behavior: &StunBehavior) -> (f32, f32, f32, f32) {
    let dt = BEHAVIOR_TICK_DT;
    let idle = (enemy.x, enemy.y, behavior.velocity_x, behavior.velocity_y);
    // Enemy-status consult: Slow scales displacement ×(1−magnitude); a Stunned row counts as
    // a Slow of its magnitude for Slowed templates and speeds the enemy up for Enraged ones
    // (Pause templates never reach here — the behavior tick skips them wholesale).
    let move_mult = crate::status::methods::enemy_movement_mult(ctx, enemy.enemy_id, stun_behavior);
    let movement = match ctx.db.movement_def().def_id().find(&phase.movement_def_id) {
        Some(m) => m.movement,
        None => return idle,
    };
    match movement {
        MovementBehavior::Stationary => idle,
        MovementBehavior::Wander(p) => {
            let (active, cycle_index) = cycle_phase(ctx, phase.move_cycle_started_at, p.active_duration, p.pause_duration);
            if !active { return idle; }
            // New deterministic direction each active cycle, derived from enemy_id + cycle_index
            // so the path is reproducible (for audits) without storing any extra state.
            let angle = hash_to_unit(splitmix64(enemy.enemy_id.wrapping_add(cycle_index))) * std::f32::consts::TAU;
            let (dir_x, dir_y) = (angle.cos(), angle.sin());
            (enemy.x + dir_x * p.speed * move_mult * dt, enemy.y + dir_y * p.speed * move_mult * dt, dir_x, dir_y)
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
                    (enemy.x + nx * p.speed * move_mult * dt, enemy.y + ny * p.speed * move_mult * dt, nx, ny)
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
                        (enemy.x + nx * p.speed * move_mult * dt, enemy.y + ny * p.speed * move_mult * dt, nx, ny)
                    } else {
                        idle
                    }
                }
                None => idle,
            }
        }
    }
}

pub fn spawn_from_biome_regions(ctx: &ReducerContext) {
    let cfg = MapConfig::load(ctx, 0, 0);
    let (chunk_hex_radius, hex_outer_radius, chunk_cols, chunk_rows) =
        (cfg.chunk_hex_radius, cfg.hex_outer_radius, cfg.chunk_cols, cfg.chunk_rows);
    let lap_q = (cfg.lap_q_x, cfg.lap_q_y);
    let lap_r = (cfg.lap_r_x, cfg.lap_r_y);

    for region in ctx.db.biome_region().iter() {
        let (center_x, center_y) = hex_to_world(region.center_hex_q, region.center_hex_r, hex_outer_radius);

        if !has_player_near_spawn_point(ctx, center_x, center_y) { continue; }

        let spawn_radius_sq = region.spawn_radius * region.spawn_radius;
        // wrapped_distance_sq, not a plain Euclidean check: a spawn offset can cross a lap seam
        // and get wrapped to the far side of the map even when the region's own center doesn't,
        // and a plain distance check would then never find its own children, defeating the cap.
        let live_count = ctx.db.enemy().iter()
            .filter(|e| region.enemy_template_ids.contains(&e.template_id) && wrapped_distance_sq(e.x, e.y, center_x, center_y, lap_q, lap_r) <= spawn_radius_sq)
            .count() as u32;

        if live_count >= region.max_enemies || region.enemy_template_ids.is_empty() { continue; }

        let us = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros() as u64;
        let template_idx = (region.region_id ^ (us / 1_000_000)) as usize % region.enemy_template_ids.len();
        let template = match ctx.db.enemy_template().template_id().find(&region.enemy_template_ids[template_idx]) {
            Some(t) => t,
            None => continue,
        };

        // next_unit draws (splitmix64 + hash_to_unit), not a raw cast — region_id.wrapping_add(us)
        // produces a value near u64::MAX, and `us` only advances it by ~2e6 per tick; casting
        // straight to f32 has a rounding granularity far coarser than that at this magnitude, so
        // a single-draw angle was silently freezing for long stretches instead of varying tick to
        // tick. sqrt(u) on the radius draw, not a raw scale, so points are uniform over the disc's
        // area instead of bunching toward the center.
        let seed = region.region_id.wrapping_add(us);
        let mut draw = 0u64;
        let radius = region.spawn_radius * next_unit(seed, &mut draw).sqrt();
        let angle = next_unit(seed, &mut draw) * std::f32::consts::TAU;
        let spawn_x = center_x + angle.cos() * radius;
        let spawn_y = center_y + angle.sin() * radius;
        let (spawn_x, spawn_y) = wrap_world_pos(spawn_x, spawn_y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);

        let (cq, cr) = world_to_chunk(spawn_x, spawn_y, chunk_hex_radius, hex_outer_radius);
        let chunk_index = spiral_chunk_index(cq, cr);
        spawn_enemy_archetype(ctx, &template, spawn_x, spawn_y, false, false, chunk_index);
    }
}
