use spacetimedb::{table, SpacetimeType};

// `line` is the number of bullets stacked per fired direction (DanmakU's Arc.Of(Line)
// composition): bullet j flies at `speed + line_speed_step * (j + 1)` — a speed ladder that
// strings out into a line as it travels. line: 1 with line_speed_step: 0 = a single bullet
// per direction at exactly `speed`. The client renders the ladder; the server only carries
// the parameters.
#[derive(SpacetimeType, Clone)] pub struct RingParams { pub speed: f32, pub count: u32, pub line: u32, pub line_speed_step: f32 }
#[derive(SpacetimeType, Clone)] pub struct VolleyParams { pub speed: f32, pub count: u32, pub line: u32, pub line_speed_step: f32, pub speed_variance: f32, pub angle_jitter: f32 }
#[derive(SpacetimeType, Clone)] pub struct CurtainParams { pub speed: f32, pub count: u32, pub angle_span: f32, pub gap_index: u32 }
#[derive(SpacetimeType, Clone)] pub struct ShotgunParams { pub speed: f32, pub count: u32, pub line: u32, pub line_speed_step: f32, pub spread: f32, pub speed_variance: f32, pub lifetime_variance: f32 }
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

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum EnemyTarget {
    Idle,
    AggroTarget,
}

/// What a Stunned enemy status does to this template (P2 stagger). The Stunned row's
/// magnitude only matters for Slowed/Enraged; all current seeds use Pause.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum StunBehavior {
    /// Attacks and movement halt entirely for the stun's duration.
    Pause,
    /// The stun acts as a Slow of the Stunned row's magnitude (enemy keeps attacking).
    Slowed,
    /// The enemy rages: movement ×(1+magnitude), fire intervals /(1+magnitude).
    Enraged,
}

#[table(accessor = movement_def, public)]
pub struct MovementDef {
    #[primary_key] #[auto_inc] pub def_id: u64,
    pub movement: MovementBehavior,
}

// `angular_speed` (degrees/second) curves bullets mid-flight; 0 = straight.
// `speed_acceleration` (world units/second²) grows bullet speed over its lifetime
// (DanmakuAcceleration in the source material); 0 = constant speed. Both are carried
// through to BulletPatternEvent; the client renders them.
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
    pub angular_speed: f32,
    pub speed_acceleration: f32,
    pub next_step_delay: f32,
}

// `angle_step` rotates each successive shot: at fire number `repeat_count` the step fires at
// `base_angle_offset + angle_step * repeat_count`, with the origin offset rotated by the same
// angle (an emitter riding a spinning arm). This is what expresses rotating/spiralling
// emitters without unrolling steps by hand.
// `repeat_target: 0` = fire forever (never advances to the next step) — the right shape for
// continuously-firing emitters in HP-driven phases. (No legacy content uses 0.)
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
    pub angular_speed: f32,
    pub speed_acceleration: f32,
    pub repeat_interval: f32,
    pub repeat_target: u32,
    pub angle_step: f32,
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
    pub angular_speed: f32,
    pub speed_acceleration: f32,
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

// `loop_delay` < 0: run once per phase cycle (the phase restarts when every attack in the
// phase finishes — the original behavior). `loop_delay` >= 0: the sequence restarts itself
// that many seconds after exhausting its steps, independently of the other attacks — a
// self-contained emitter cadence (e.g. burst, pause, burst) that never gates or gets gated
// by sibling sequences.
#[derive(SpacetimeType, Clone)]
pub struct AttackSequence {
    pub steps: Vec<SequenceStepDef>,
    pub start_delay: f32,
    pub loop_delay: f32,
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
    // What a Stunned status does to this enemy (see StunBehavior). Pause for all current seeds.
    pub stun_behavior: StunBehavior,
    pub phases: Vec<PhaseDef>,
}
