//! Emitter-composition authoring layer for enemies.
//!
//! The runtime model (`PhaseDef` → `AttackSequence` → step defs) is deliberately generic;
//! this module is the vocabulary content is actually written in. An [`Emitter`] mirrors the
//! classic bullet-hell building block (DanmakU's `DanmakuEmitter`): a pattern fired at a
//! rate, optionally spinning, optionally in bursts, optionally curving/accelerating
//! mid-flight, with additive per-difficulty-step deltas. [`Emitter::compile`] lowers one
//! emitter to a self-contained [`AttackSequence`] at seed time — no hand-unrolled steps.
//!
//! Authoring a new enemy means composing emitters into phases — see
//! `enemy/defs/gorgon_boss.rs` for a full boss. A minimal example:
//!
//! ```ignore
//! use crate::enemy::emitters::{Difficulty, DifficultySteps, Emitter};
//! use crate::enemy::defs::make_phase;
//!
//! let phases = vec![
//!     // 100–50% HP: a slow 8-way ring spinning at 30°/s.
//!     make_phase(ctx, 0, 0.0, MovementBehavior::Stationary, 0.0, vec![
//!         Emitter::ring(8, 80.0, 1.5).spin(30.0).compile(ctx, difficulty),
//!     ]),
//!     // ≤50% HP: adds an aimed 3-round burst every 2s, +1 pellet per difficulty step.
//!     make_phase(ctx, 1, 0.5, MovementBehavior::Stationary, 0.0, vec![
//!         Emitter::ring(8, 80.0, 1.5).spin(30.0).compile(ctx, difficulty),
//!         Emitter::volley(3, 160.0, 1.0)
//!             .target(EnemyTarget::AggroTarget)
//!             .burst(3, 0.15, 2.0)
//!             .difficulty(DifficultySteps { bullet_count: 1, ..Default::default() })
//!             .compile(ctx, difficulty),
//!     ]),
//! ];
//! ```
//!
//! Phases advance on HP thresholds (highest `phase_index` whose `hp_threshold` has been
//! crossed wins), exactly like the HP-gated animator states of the source material.

use spacetimedb::ReducerContext;
use super::def_tables::*;
use super::defs::{make_looping_sequence, make_sequence, seq_repeat};

/// Difficulty tiers. Content is built once per tier (seeded as separate templates), so a
/// difficulty is chosen at spawn time by template id — e.g. `GorgonBoss_Hard`.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Difficulty {
    Easy,
    Normal,
    Hard,
    Madness,
}

impl Difficulty {
    pub const ALL: [Difficulty; 4] = [Difficulty::Easy, Difficulty::Normal, Difficulty::Hard, Difficulty::Madness];

    /// Additive step index: Easy is the unmodified baseline.
    pub fn step(self) -> i32 {
        match self {
            Difficulty::Easy => 0,
            Difficulty::Normal => 1,
            Difficulty::Hard => 2,
            Difficulty::Madness => 3,
        }
    }

    /// Template-id suffix: `format!("MyBoss_{}", difficulty.suffix())`.
    pub fn suffix(self) -> &'static str {
        match self {
            Difficulty::Easy => "Easy",
            Difficulty::Normal => "Normal",
            Difficulty::Hard => "Hard",
            Difficulty::Madness => "Madness",
        }
    }
}

/// Additive per-difficulty-step deltas applied at compile time (the source material's
/// `DifficultyModifierSimple`): each field is added `difficulty.step()` times to the
/// emitter's base value. All zero = same at every difficulty.
#[derive(Clone, Copy, Default)]
pub struct DifficultySteps {
    /// Added to `fire_rate` (shots/second) per step.
    pub fire_rate: f32,
    /// Added to the pattern's bullet count (ring ways, volley pellets, …) per step.
    pub bullet_count: i32,
    /// Added to the pattern's line count (bullets per direction) per step.
    pub line_count: i32,
    /// Added to bullet speed (world units/second) per step.
    pub bullet_speed: f32,
    /// Added to bullet curving (degrees/second) per step.
    pub angular_speed: f32,
}

/// One emitter: a pattern fired on a cadence, from an optional offset mount, with optional
/// spin, curving, and acceleration. Construct with `Emitter::ring`/`volley`/`shotgun`/`new`,
/// refine with the builder methods, then `compile` per difficulty.
#[derive(Clone)]
pub struct Emitter {
    pub pattern: PatternType,
    pub target: EnemyTarget,
    /// Activations per second. In burst mode this only converts `active_duration` to a shot
    /// count — the cadence comes from `burst_count`/`burst_pause`/`burst_delay`.
    pub fire_rate: f32,
    /// Emitter rotation in degrees/second; each shot fires (and orbits its offset mount)
    /// at the emitter's current angle. 0 = fixed direction.
    pub spin_deg_per_sec: f32,
    /// Shots per burst; 0 or 1 = continuous fire at `fire_rate`.
    pub burst_count: u32,
    /// Seconds between shots inside a burst.
    pub burst_pause: f32,
    /// Seconds of silence between bursts.
    pub burst_delay: f32,
    /// Mid-flight bullet curving in degrees/second. 0 = straight.
    pub angular_speed: f32,
    /// Bullet speed growth in world units/second². 0 = constant speed.
    pub speed_acceleration: f32,
    /// Mount point relative to the enemy's center; orbits with `spin_deg_per_sec`.
    pub offset: (f32, f32),
    /// Rest firing angle in degrees (0 = toward positive X, or toward the target for aimed
    /// patterns).
    pub base_angle: f32,
    /// Delay after the phase starts before this emitter's first activation.
    pub start_delay: f32,
    /// How long the emitter stays active after `start_delay`, in seconds; 0 = until the
    /// phase ends. Windowed activation is what per-phase emitter on/off schedules compile to.
    pub active_duration: f32,
    pub texture_id: String,
    pub lifetime: f32,
    pub damage: u32,
    pub difficulty: DifficultySteps,
}

// Public authoring API — not every builder is exercised by shipped content yet (burst mode,
// curving, …); they're part of the vocabulary contributors write enemies in.
#[allow(dead_code)]
impl Emitter {
    /// A full 360° ring of `count` bullets.
    pub fn ring(count: u32, speed: f32, fire_rate: f32) -> Self {
        Self::new(PatternType::Ring(RingParams { speed, count, line: 1, line_speed_step: 0.0 }), fire_rate)
    }

    /// `count` aimed bullets with per-pellet jitter.
    pub fn volley(count: u32, speed: f32, fire_rate: f32) -> Self {
        Self::new(PatternType::Volley(VolleyParams { speed, count, line: 1, line_speed_step: 0.0, speed_variance: 0.0, angle_jitter: 0.0 }), fire_rate)
    }

    /// `count` bullets evenly fanning `spread` degrees around the firing direction.
    pub fn shotgun(count: u32, spread_deg: f32, speed: f32, fire_rate: f32) -> Self {
        Self::new(PatternType::Shotgun(ShotgunParams { speed, count, line: 1, line_speed_step: 0.0, spread: spread_deg.to_radians(), speed_variance: 0.0, lifetime_variance: 0.0 }), fire_rate)
    }

    pub fn new(pattern: PatternType, fire_rate: f32) -> Self {
        Emitter {
            pattern,
            target: EnemyTarget::Idle,
            fire_rate,
            spin_deg_per_sec: 0.0,
            burst_count: 0,
            burst_pause: 0.0,
            burst_delay: 0.0,
            angular_speed: 0.0,
            speed_acceleration: 0.0,
            offset: (0.0, 0.0),
            base_angle: 0.0,
            start_delay: 0.0,
            active_duration: 0.0,
            texture_id: "Arrow".to_string(),
            lifetime: 3.0,
            damage: 10,
            difficulty: DifficultySteps::default(),
        }
    }

    pub fn target(mut self, target: EnemyTarget) -> Self { self.target = target; self }
    pub fn spin(mut self, deg_per_sec: f32) -> Self { self.spin_deg_per_sec = deg_per_sec; self }
    /// Fire `count` shots `pause` seconds apart, then stay silent for `delay` seconds.
    /// Note: spin accumulates per shot within a burst and restarts from the base angle each
    /// burst (loop restart resets the shot counter) — a small, usually negligible snap.
    pub fn burst(mut self, count: u32, pause: f32, delay: f32) -> Self {
        self.burst_count = count;
        self.burst_pause = pause;
        self.burst_delay = delay;
        self
    }
    pub fn curve(mut self, deg_per_sec: f32) -> Self { self.angular_speed = deg_per_sec; self }
    pub fn accel(mut self, units_per_sec2: f32) -> Self { self.speed_acceleration = units_per_sec2; self }
    /// Stack `count` bullets per fired direction, each `speed_step` faster than the last
    /// (DanmakU's Line composition — the ladder strings out into a line as it travels).
    pub fn line(mut self, count: u32, speed_step: f32) -> Self {
        match &mut self.pattern {
            PatternType::Ring(p) => { p.line = count; p.line_speed_step = speed_step; }
            PatternType::Volley(p) => { p.line = count; p.line_speed_step = speed_step; }
            PatternType::Shotgun(p) => { p.line = count; p.line_speed_step = speed_step; }
            _ => {}
        }
        self
    }
    pub fn offset(mut self, x: f32, y: f32) -> Self { self.offset = (x, y); self }
    pub fn angle(mut self, deg: f32) -> Self { self.base_angle = deg; self }
    pub fn start_delay(mut self, seconds: f32) -> Self { self.start_delay = seconds; self }
    /// Stay active for `seconds` after `start_delay`, then stop until the phase restarts.
    pub fn active_duration(mut self, seconds: f32) -> Self { self.active_duration = seconds; self }
    /// Activate only inside the window [`start`, `start` + `duration`] of the phase.
    pub fn window(mut self, start: f32, duration: f32) -> Self {
        self.start_delay = start;
        self.active_duration = duration;
        self
    }
    pub fn texture(mut self, texture_id: &str) -> Self { self.texture_id = texture_id.to_string(); self }
    pub fn lifetime(mut self, seconds: f32) -> Self { self.lifetime = seconds; self }
    pub fn damage(mut self, damage: u32) -> Self { self.damage = damage; self }
    pub fn difficulty(mut self, steps: DifficultySteps) -> Self { self.difficulty = steps; self }

    /// Lowers the emitter to one self-pacing AttackSequence with the difficulty deltas
    /// applied. Continuous emitters become an infinite (or `active_duration`-bounded)
    /// Repeat; burst emitters become a finite Repeat inside a self-looping sequence.
    pub fn compile(&self, ctx: &ReducerContext, difficulty: Difficulty) -> AttackSequence {
        let step = difficulty.step();
        let mut pattern = self.pattern.clone();
        apply_difficulty(&mut pattern, &self.difficulty, step);
        let angular_speed = self.angular_speed + self.difficulty.angular_speed * step as f32;
        let fire_rate = (self.fire_rate + self.difficulty.fire_rate * step as f32).max(0.05);

        if self.burst_count > 1 {
            let step_def = seq_repeat(
                ctx, pattern, self.target.clone(),
                self.offset.0, self.offset.1, self.base_angle,
                &self.texture_id, self.lifetime, self.damage, angular_speed, self.speed_acceleration,
                self.burst_pause.max(0.01), self.burst_count,
                self.spin_deg_per_sec * self.burst_pause, 0.0,
            );
            make_looping_sequence(vec![step_def], self.start_delay, self.burst_delay.max(0.0))
        } else {
            let interval = 1.0 / fire_rate;
            // A finite shot count for windowed emitters; 0 = fire until the phase ends.
            let target = if self.active_duration > 0.0 {
                (self.active_duration * fire_rate).round().max(1.0) as u32
            } else {
                0
            };
            let step_def = seq_repeat(
                ctx, pattern, self.target.clone(),
                self.offset.0, self.offset.1, self.base_angle,
                &self.texture_id, self.lifetime, self.damage, angular_speed, self.speed_acceleration,
                interval, target,
                self.spin_deg_per_sec * interval, 0.0,
            );
            make_sequence(vec![step_def], self.start_delay)
        }
    }
}

fn scale_count(base: u32, delta: i32, step: i32) -> u32 {
    (base as i32 + delta * step).max(1) as u32
}

fn scale_speed(base: f32, delta: f32, step: i32) -> f32 {
    (base + delta * step as f32).max(1.0)
}

fn apply_difficulty(pattern: &mut PatternType, d: &DifficultySteps, step: i32) {
    match pattern {
        PatternType::Ring(p) => {
            p.count = scale_count(p.count, d.bullet_count, step);
            p.line = scale_count(p.line, d.line_count, step);
            p.speed = scale_speed(p.speed, d.bullet_speed, step);
        }
        PatternType::Volley(p) => {
            p.count = scale_count(p.count, d.bullet_count, step);
            p.line = scale_count(p.line, d.line_count, step);
            p.speed = scale_speed(p.speed, d.bullet_speed, step);
        }
        PatternType::Shotgun(p) => {
            p.count = scale_count(p.count, d.bullet_count, step);
            p.line = scale_count(p.line, d.line_count, step);
            p.speed = scale_speed(p.speed, d.bullet_speed, step);
        }
        // Curtain/Explosion carry no `line`; bullet_count scales their count.
        PatternType::Curtain(p) => {
            p.count = scale_count(p.count, d.bullet_count, step);
            p.speed = scale_speed(p.speed, d.bullet_speed, step);
        }
        PatternType::Explosion(p) => {
            p.count = scale_count(p.count, d.bullet_count, step);
            p.speed = scale_speed(p.speed, d.bullet_speed, step);
        }
    }
}
