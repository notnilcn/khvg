use spacetimedb::{table, Identity, ScheduleAt, SpacetimeType, Timestamp};
use super::reducers::{tick_enemy_behavior, tick_enemy_spawn};
use super::def_tables::{PatternType, EnemyTarget};

#[derive(SpacetimeType, Clone)]
pub enum SequenceStepRef {
    Single(u64),
    Repeat(u64),
    Multi(MultiStepRef),
}

#[derive(SpacetimeType, Clone)]
pub struct MultiStepRef {
    pub def_id: u64,
    pub shot_index: u32
}

// Per-enemy copy of a RepeatStepDef so repeat_count can advance independently per enemy.
// Field semantics (angle_step, repeat_target: 0 = infinite, angular_speed) live on RepeatStepDef.
#[table(accessor = repeat_step_instance, public)]
pub struct RepeatStepInstance {
    #[primary_key] #[auto_inc] pub repeat_id: u64,
    pub pattern_type: PatternType,
    pub target: EnemyTarget,
    pub offset_x: f32,
    pub offset_y: f32,
    pub base_angle_offset: f32,
    pub texture_id: String,
    pub lifetime: f32,
    pub damage: u32,
    pub angular_speed: f32,
    pub speed_acceleration: f32,
    pub repeat_interval: f32,
    pub repeat_target: u32,
    pub angle_step: f32,
    pub next_step_delay: f32,
    pub repeat_count: u32,
}

#[table(accessor = enemy_sequence_step, public)]
pub struct EnemySequenceStep {
    #[primary_key] #[auto_inc] pub step_id: u64,
    #[index(btree)] pub attack_id: u64,
    pub step_index: u32,
    pub step: SequenceStepRef,
}

#[table(accessor = enemy_attack, public)]
pub struct EnemyAttack {
    #[primary_key] #[auto_inc] pub attack_id: u64,
    #[index(btree)] pub phase_id: u64,
    pub attack_index: u32,
    pub step_index: u32,
    pub step_timer: Timestamp,
    pub step_waiting: bool,
    pub start_delaying: bool,
    // True while a looping sequence (AttackSequence.loop_delay >= 0) is waiting out its
    // loop delay before restarting. step_timer doubles as the loop timer during the wait.
    pub loop_waiting: bool,
}

#[table(accessor = enemy_phases, public)]
pub struct EnemyPhase {
    #[primary_key] #[auto_inc] pub phase_id: u64,
    #[index(btree)] pub behavior_id: u64,
    pub phase_index: u8,
    pub hp_threshold: f32,
    pub movement_def_id: u64,
    pub phase_loop_delay: f32,
    pub phase_waiting: bool,
    pub phase_loop_timer: Timestamp,
    pub move_cycle_started_at: Timestamp,
}

#[table(accessor = enemy_behavior, public)]
pub struct EnemyBehavior {
    #[primary_key] #[auto_inc]
    pub behavior_id: u64,
    pub aggro_target: Option<Identity>,
    pub aggro_locked_until: Timestamp,
    pub was_simulating: bool,
    pub active_phase_index: u8,
    pub velocity_x: f32,
    pub velocity_y: f32,
}

#[table(accessor = enemy, public)]
pub struct Enemy {
    #[primary_key] #[auto_inc]
    pub enemy_id: u64,
    pub template_id: String,
    pub behavior_id: u64,
    pub x: f32,
    pub y: f32,
    pub spawn_time: Timestamp,
    pub hp: u32,
    pub phase: u8,
    pub is_elite: bool,
    pub immortal: bool,
    // Stagger bar (0–100, spawns at 0): bash abilities add to it, the 1 Hz status tick
    // decays it (STAGGER_DECAY_PER_SECOND), and at 100 the enemy is Stunned and it resets.
    pub stagger: f32,
    #[index(btree)]
    pub chunk_index: i64,
}

#[spacetimedb::table(accessor = enemy_spawn_schedule, scheduled(tick_enemy_spawn))]
pub struct EnemySpawnSchedule {
    #[primary_key] #[auto_inc]
    pub scheduled_id: u64,
    pub scheduled_at: ScheduleAt,
}

#[spacetimedb::table(accessor = enemy_behavior_schedule, scheduled(tick_enemy_behavior))]
pub struct EnemyBehaviorSchedule {
    #[primary_key] #[auto_inc]
    pub scheduled_id: u64,
    pub scheduled_at: ScheduleAt,
}

#[derive(SpacetimeType, Clone)]
pub enum BulletControlKind {
    Delete,
    Split,
    Attract,
    DeleteRect,
    SplitRect,
    SlashRect,
    WallRect,
}

/// Relay of a player's bullet-controller ability cast (delete/split/blackhole/rectangle).
/// The server tracks no live bullets — it validates the caller and rebroadcasts the cast;
/// every client resolves the proximity query against its own local bullets. Ephemeral
/// event table, same shape as BulletPatternEvent.
/// Field encoding: Delete/Split use (x, y) as the center and `radius` as the radius; Attract
/// adds (target_x, target_y) as the homing target and `duration` as the black hole's linger
/// time in seconds (0 = one-shot pull). DeleteRect/SplitRect are the rectangle variants:
/// (x, y) is the cast origin, (target_x, target_y) the far end, and `radius` the rectangle
/// width. SlashRect is the DeleteRect encoding with `duration` repurposed to carry the
/// max-bullet-damage threshold as the f32 BITS of the u32 (f32::from_bits — clients decode
/// with BitConverter.SingleToUInt32Bits). WallRect is the DeleteRect encoding with
/// `duration` as the wall's linger time in seconds (the Attract pattern).
#[table(accessor = bullet_control_event, public, event)]
pub struct BulletControlEvent {
    #[primary_key] #[auto_inc]
    pub event_id: u64,
    pub kind: BulletControlKind,
    pub x: f32,
    pub y: f32,
    pub radius: f32,
    pub target_x: f32,
    pub target_y: f32,
    pub duration: f32,
    pub cast_by: Identity,
}

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum AbilityVisualKind {
    Circle,
    Line,
    Cone,
    Meteor,
    Converge,
}

/// Visual-only relay of a Spell ability cast (P5): damage is applied server-side in the
/// same dispatch — this row only tells every client to draw the flash. Ephemeral event
/// table, same shape as BulletControlEvent. The caster renders optimistically and skips
/// its own echo via cast_by (the BulletControlEvent pattern).
/// Field encoding per kind: (x, y) is always the cast origin (the player's position row).
/// Circle/Meteor/Converge use (target_x, target_y) as the effect center and `radius` as the
/// circle radius; Line uses (target_x, target_y) as the far end and `radius` as the line
/// width; Cone uses the Line encoding with `radius` as the spread in RADIANS. `duration` is
/// the visual linger in seconds (SPELL_VISUAL_SECONDS).
#[table(accessor = ability_visual_event, public, event)]
pub struct AbilityVisualEvent {
    #[primary_key] #[auto_inc]
    pub event_id: u64,
    pub kind: AbilityVisualKind,
    pub x: f32,
    pub y: f32,
    pub target_x: f32,
    pub target_y: f32,
    pub radius: f32,
    pub duration: f32,
    pub cast_by: Identity,
}

#[table(accessor = bullet_pattern_event, public, event)]
pub struct BulletPatternEvent {
    #[primary_key] #[auto_inc]
    pub event_id: u64,
    pub enemy_id: u64,
    pub source_step: SequenceStepRef,
    /// damage-aware bullet controllers (SlashRect) can compare without subscribing step defs.
    pub damage: u32,
    pub origin_x: f32,
    pub origin_y: f32,
    pub origin_offset_x: f32,
    pub origin_offset_y: f32,
    pub base_angle_offset: f32,
    pub pattern_type: PatternType,
    pub target: Option<Identity>,
    pub texture_id: String,
    pub lifetime: f32,
    // Degrees/second of mid-flight bullet curving (0 = straight). Rendered client-side.
    pub angular_speed: f32,
    // World units/second² of bullet speed growth over its lifetime (0 = constant speed).
    pub speed_acceleration: f32,
    #[index(btree)]
    pub chunk_index: i64,
}