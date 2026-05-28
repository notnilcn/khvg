use spacetimedb::{table, Identity, ScheduleAt, SpacetimeType, Timestamp};
use super::reducers::{tick_enemy_behavior, tick_enemy_spawn};

#[derive(SpacetimeType, Clone)] pub struct StraightParams { pub speed: f32, pub count: u32 }
#[derive(SpacetimeType, Clone)] pub struct SineParams { pub speed: f32, pub count: u32, pub amplitude: f32, pub frequency: f32 }
#[derive(SpacetimeType, Clone)] pub struct HomingParams { pub speed: f32, pub count: u32, pub turn_rate: f32 }
#[derive(SpacetimeType, Clone)] pub struct RingParams { pub speed: f32, pub count: u32 }
#[derive(SpacetimeType, Clone)] pub struct SpiralParams { pub speed: f32, pub count: u32, pub arms: u32, pub rotation_speed: f32 }
#[derive(SpacetimeType, Clone)] pub struct BurstParams { pub speed: f32, pub count: u32, pub spread: f32 }

#[derive(SpacetimeType, Clone)] pub struct LinearParams { pub dir_x: f32, pub dir_y: f32, pub speed: f32 }
#[derive(SpacetimeType, Clone)] pub struct WanderParams { pub speed: f32, pub change_interval: f32 }
#[derive(SpacetimeType, Clone)] pub struct ChaseParams { pub speed: f32 }
#[derive(SpacetimeType, Clone)] pub struct FleeParams { pub speed: f32, pub min_range: f32 }

#[derive(SpacetimeType, Clone)]
pub enum MovementBehavior {
    Stationary,
    Linear(LinearParams),
    Wander(WanderParams),
    Chase(ChaseParams),
    Flee(FleeParams),
}

#[derive(SpacetimeType, Clone)]
pub enum PatternType {
    Straight(StraightParams),
    Sine(SineParams),
    Homing(HomingParams),
    Ring(RingParams),
    Spiral(SpiralParams),
    Burst(BurstParams),
}

#[derive(SpacetimeType, Clone)]
pub enum EnemyTarget {
    Idle,
    NearestPlayer,
    NearestCluster,
    SpecificPlayer(Option<Identity>),
}

#[derive(SpacetimeType, Clone)]
pub struct AttackDef {
    pub pattern_type: PatternType,
    pub target: EnemyTarget,
    pub fire_rate: f32,
    pub texture_id: String,
}

#[derive(SpacetimeType, Clone)]
pub struct PhaseDef {
    pub phase_index: u8,
    pub hp_threshold: f32,
    pub movement: MovementBehavior,
    pub attacks: Vec<AttackDef>,
}

#[derive(SpacetimeType, Clone)]
pub struct AttackInstance {
    pub pattern_type: PatternType,
    pub target: EnemyTarget,
    pub fire_rate: f32,
    pub last_fire_time: Timestamp,
    pub texture_id: String,
}

#[derive(SpacetimeType, Clone)]
pub struct PhaseInstance {
    pub phase_index: u8,
    pub hp_threshold: f32,
    pub movement: MovementBehavior,
    pub attacks: Vec<AttackInstance>,
}

#[table(accessor = enemy_template, public)]
pub struct EnemyTemplate {
    #[primary_key]
    pub template_id: String,
    pub texture_id: String,
    pub display_name: String,
    pub max_hp: u32,
    pub phases: Vec<PhaseDef>,
}

#[table(accessor = enemy_behavior, public)]
pub struct EnemyBehavior {
    #[primary_key] #[auto_inc]
    pub behavior_id: u64,
    pub template_id: String,
    pub display_name: String,
    pub max_hp: u32,
    pub phases: Vec<PhaseInstance>,
    pub move_dir_x: f32,
    pub move_dir_y: f32,
    pub move_timer: Timestamp,
}

#[table(accessor = enemy_zone, public)]
pub struct EnemyZone {
    #[primary_key] #[auto_inc]
    pub zone_id: u64,
    pub center_chunk_q: i32,
    pub center_chunk_r: i32,
    pub chunk_radius: i32,
    pub spawn_radius: f32,
    pub template_ids: Vec<String>,
    pub max_enemies: u32,
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

#[table(accessor = bullet_pattern_event, public, event)]
pub struct BulletPatternEvent {
    #[primary_key] #[auto_inc]
    pub event_id: u64,
    pub enemy_id: u64,
    pub origin_x: f32,
    pub origin_y: f32,
    pub pattern_type: PatternType,
    pub target: Option<Identity>,
    pub texture_id: String,
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