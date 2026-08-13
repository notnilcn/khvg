use spacetimedb::{table, SpacetimeType};

#[derive(SpacetimeType, Clone)] pub struct RingParams { pub speed: f32, pub count: u32 }
#[derive(SpacetimeType, Clone)] pub struct VolleyParams { pub speed: f32, pub count: u32, pub speed_variance: f32, pub angle_jitter: f32 }
#[derive(SpacetimeType, Clone)] pub struct CurtainParams { pub speed: f32, pub count: u32, pub angle_span: f32, pub gap_index: u32 }
#[derive(SpacetimeType, Clone)] pub struct ShotgunParams { pub speed: f32, pub count: u32, pub spread: f32, pub speed_variance: f32, pub lifetime_variance: f32 }
#[derive(SpacetimeType, Clone)] pub struct ExplosionParams { pub speed: f32, pub count: u32, pub speed_variance: f32, pub lifetime_variance: f32 }

#[derive(SpacetimeType, Clone)] pub struct WanderParams { pub speed: f32, pub active_duration: f32, pub pause_duration: f32 }
#[derive(SpacetimeType, Clone)] pub struct ChaseParams { pub speed: f32, pub active_duration: f32, pub pause_duration: f32 }
#[derive(SpacetimeType, Clone)] pub struct FleeParams { pub speed: f32, pub min_range: f32, pub active_duration: f32, pub pause_duration: f32 }

#[derive(SpacetimeType, Clone)]
pub enum MovementBehavior {
    Stationary,
    Wander(WanderParams),
    Chase(ChaseParams),
    Flee(FleeParams),
}

#[derive(SpacetimeType, Clone)]
pub enum PatternType {
    Ring(RingParams),
    Volley(VolleyParams),
    Curtain(CurtainParams),
    Shotgun(ShotgunParams),
    Explosion(ExplosionParams),
}

#[derive(SpacetimeType, Clone)]
pub enum EnemyTarget {
    Idle,
    AggroTarget,
}

#[table(accessor = movement_def, public)]
pub struct MovementDef {
    #[primary_key] #[auto_inc] pub def_id: u64,
    pub movement: MovementBehavior,
}

#[table(accessor = single_step_def, public)]
pub struct SingleStepDef {
    #[primary_key] #[auto_inc] pub def_id: u64,
    pub pattern_type: PatternType,
    pub target: EnemyTarget,
    pub offset_x: f32,
    pub offset_y: f32,
    pub base_angle_offset: f32,
    pub texture_id: String,
    pub lifetime: f32,
    pub damage: u32,
    pub next_step_delay: f32,
}

#[table(accessor = repeat_step_def, public)]
pub struct RepeatStepDef {
    #[primary_key] #[auto_inc] pub def_id: u64,
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
}

#[derive(SpacetimeType, Clone)]
pub struct MultiShot {
    pub pattern_type: PatternType,
    pub target: EnemyTarget,
    pub offset_x: f32,
    pub offset_y: f32,
    pub base_angle_offset: f32,
    pub texture_id: String,
    pub lifetime: f32,
    pub damage: u32,
}

#[table(accessor = multi_step_def, public)]
pub struct MultiStepDef {
    #[primary_key] #[auto_inc] pub def_id: u64,
    pub shots: Vec<MultiShot>,
    pub next_step_delay: f32,
}

#[derive(SpacetimeType, Clone)]
pub enum SequenceStepDef {
    Single(u64),
    Repeat(u64),
    Multi(u64),
}

#[derive(SpacetimeType, Clone)]
pub struct AttackSequence {
    pub steps: Vec<SequenceStepDef>,
    pub start_delay: f32,
}

#[derive(SpacetimeType, Clone)]
pub struct PhaseDef {
    pub phase_index: u8,
    pub hp_threshold: f32,
    pub movement_def_id: u64,
    pub phase_loop_delay: f32,
    pub attacks: Vec<AttackSequence>,
}

#[table(accessor = enemy_template, public)]
pub struct EnemyTemplate {
    #[primary_key] pub template_id: String,
    pub texture_id: String,
    pub display_name: String,
    pub max_hp: u32,
    // Flat per-bullet damage subtraction (doc 02: minimum damage 1) — high defence is
    // what makes many-weak-bullets patterns worse than few-strong-bullets ones.
    pub defense: u32,
    // Multiples of CAMERA_VIEW_RADIUS, not absolute world units — so every template stays
    // correctly scaled if the client's camera/viewport ever changes.
    pub move_sim_factor: f32,
    pub attack_sim_factor: f32,
    pub aggro_lock_seconds: f32,
    pub phases: Vec<PhaseDef>,
}
