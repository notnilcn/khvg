//! Enemy definitions — the content side of the enemy module.
//!
//! Each file here authors one enemy (or one boss family) by composing the primitives from
//! `enemy/emitters.rs` (continuous/burst emitters with spin, curving, and per-difficulty
//! scaling) or, for fully custom step choreography, the raw row-inserting helpers below.
//! Definitions are seeded at publish time from `main/lifecycle.rs::init` and spawn by
//! `template_id` via the admin `spawn_enemy` reducer or a `BiomeRegion`'s template list.
//!
//! `gorgon_boss` is the reference boss: one builder producing four difficulty variants
//! (`GorgonBoss_Easy` … `GorgonBoss_Madness`) from a single emitter table.

use spacetimedb::{ReducerContext, Table};
use crate::enemy::def_tables::*;

pub mod gorgon_boss;

/// Inserts the phase's MovementDef row and returns the PhaseDef pointing at it.
pub fn make_phase(ctx: &ReducerContext, phase_index: u8, hp_threshold: f32, movement: SteeringMovement, phase_loop_delay: f32, attacks: Vec<AttackSequence>) -> PhaseDef {
    let def = ctx.db.movement_def().insert(MovementDef { def_id: 0, movement });
    PhaseDef { phase_index, hp_threshold, movement_def_id: def.def_id, phase_loop_delay, attacks }
}

impl SteeringMovement {
    /// Stand still — the old `MovementBehavior::Stationary` in the steering vocabulary.
    pub fn idle() -> SteeringMovement {
        SteeringMovement {
            speed: 0.0,
            goal: MoveGoal::Idle,
            pattern: MovePattern::Direct,
            separation_weight: 0.0,
            avoid_cliffs: false,
            active_duration: 0.0,
            pause_duration: 0.0,
        }
    }

    /// Adds a move/pause cycle (seconds) — Lunge's pause window is its telegraph.
    pub fn with_cycle(mut self, active_duration: f32, pause_duration: f32) -> SteeringMovement {
        self.active_duration = active_duration;
        self.pause_duration = pause_duration;
        self
    }
}

/// Terse SteeringMovement constructor: separation on (weight 1), cliffs on, no cycle.
pub fn steering(speed: f32, goal: MoveGoal, pattern: MovePattern) -> SteeringMovement {
    SteeringMovement {
        speed,
        goal,
        pattern,
        separation_weight: 1.0,
        avoid_cliffs: true,
        active_duration: 0.0,
        pause_duration: 0.0,
    }
}

/// A run-once sequence: the phase restarts it (after phase_loop_delay) only when every
/// sequence in the phase has finished.
pub fn make_sequence(steps: Vec<SequenceStepDef>, start_delay: f32) -> AttackSequence {
    AttackSequence { steps, start_delay, loop_delay: -1.0 }
}

/// A self-looping sequence: restarts itself `loop_delay` seconds after exhausting its steps,
/// independently of the other sequences in the phase (see AttackSequence.loop_delay).
pub fn make_looping_sequence(steps: Vec<SequenceStepDef>, start_delay: f32, loop_delay: f32) -> AttackSequence {
    AttackSequence { steps, start_delay, loop_delay }
}

#[allow(clippy::too_many_arguments)]
pub fn seq_single(ctx: &ReducerContext, pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, damage: u32, angular_speed: f32, speed_acceleration: f32, next_step_delay: f32) -> SequenceStepDef {
    let def = ctx.db.single_step_def().insert(SingleStepDef { def_id: 0, pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, damage, angular_speed, speed_acceleration, next_step_delay });
    SequenceStepDef::Single(def.def_id)
}

/// `repeat_target: 0` fires forever; `angle_step` rotates each successive shot (and orbits
/// the origin offset with it). See RepeatStepDef.
#[allow(clippy::too_many_arguments)]
pub fn seq_repeat(ctx: &ReducerContext, pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, damage: u32, angular_speed: f32, speed_acceleration: f32, repeat_interval: f32, repeat_target: u32, angle_step: f32, next_step_delay: f32) -> SequenceStepDef {
    let def = ctx.db.repeat_step_def().insert(RepeatStepDef { def_id: 0, pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, damage, angular_speed, speed_acceleration, repeat_interval, repeat_target, angle_step, next_step_delay });
    SequenceStepDef::Repeat(def.def_id)
}

// Public authoring API — the Multi step helpers are available to new content even though no
// shipped enemy uses them right now.
#[allow(dead_code)]
#[allow(clippy::too_many_arguments)]
pub fn multi_shot(pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, damage: u32, angular_speed: f32, speed_acceleration: f32) -> MultiShot {
    MultiShot { pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, damage, angular_speed, speed_acceleration }
}

#[allow(dead_code)]
pub fn seq_multi(ctx: &ReducerContext, shots: Vec<MultiShot>, next_step_delay: f32) -> SequenceStepDef {
    let def = ctx.db.multi_step_def().insert(MultiStepDef { def_id: 0, shots, next_step_delay });
    SequenceStepDef::Multi(def.def_id)
}
