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
    pub repeat_interval: f32,
    pub repeat_target: u32,
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
}

/// Relay of a player's bullet-controller ability cast (delete/split/blackhole). The server
/// tracks no live bullets — it validates the caller and rebroadcasts the cast; every client
/// resolves the proximity query against its own local bullets. Ephemeral event table, same
/// shape as BulletPatternEvent.
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
    pub cast_by: Identity,
}

#[table(accessor = bullet_pattern_event, public, event)]
pub struct BulletPatternEvent {
    #[primary_key] #[auto_inc]
    pub event_id: u64,
    pub enemy_id: u64,
    pub source_step: SequenceStepRef,
    pub origin_x: f32,
    pub origin_y: f32,
    pub origin_offset_x: f32,
    pub origin_offset_y: f32,
    pub base_angle_offset: f32,
    pub pattern_type: PatternType,
    pub target: Option<Identity>,
    pub texture_id: String,
    pub lifetime: f32,
    #[index(btree)]
    pub chunk_index: i64,
}