//! The Gorgon boss — a port of the main boss of "AotMK Demo Beta" (Stage_01, animator
//! `FeralGorgon.controller`), rebuilt from the decompiled/AssetRipper reference material in
//! `code_examples/AotMK_0.3.3.1b_Win64/` as the reference implementation of the
//! emitter-composition authoring layer (`enemy/emitters.rs`).
//!
//! Source structure: the fight is 9 HP-gated phases (animator states FeralGorgonP_01..09,
//! transitions on BossHealthPercentage at 0.9, 0.8, …, 0.1) plus an Intro state, mapped here
//! to 10 phases (phase 0 = Intro, phase N = P_0N). Each phase clip toggles named emitter
//! objects on/off over time windows; those become windowed Emitters below (one AttackSequence
//! per window). Rest states between phases (no emitters, 1.5–2.5s) map to each phase's
//! `PHASE_REST` loop delay. All data was extracted from
//! `ExportedProject/Assets/Scenes/Stage_01.unity` (emitter parameters) and
//! `Assets/AnimationClip/FeralGorgonP_01..09.anim` (per-phase activation windows).
//!
//! Porting rules applied (quirks of the source engine, verified in the decompiled DanmakU):
//! - AotMK "N-way 360° arc" fires N bullets with INCLUSIVE endpoints, so the first and last
//!   stack — N unique directions is (N − 1). Ring counts below are already decremented; the
//!   stacked seam bullet is dropped (one doubled-damage direction is not worth a special case).
//! - AotMK `Line.Count`/`DeltaSpeed` = a speed ladder per direction (bullet j flies at
//!   speed + Δ·(j+1)) → our `line`/`line_speed_step`.
//! - `DanmakuEmitter.FireRate` is shots/second; `EnemyGun.fireRate` is seconds/shot (inverted
//!   below). `Rotate.z` and difficulty deltas are °/s and per-step as written.
//! - AotMK speeds run 1–8 units/s; `AOTMK_SPEED_SCALE` lifts them into our 25–200 world
//!   units/s range. Per-step difficulty deltas on speed are scaled by the same factor.
//! - Bullet lifetime is derived per emitter (`reach`) so every pattern threatens ~400 world
//!   units before expiring, instead of the source's flat destroy timer.
//!
//! Deliberately not ported (no counterpart systems exist client-side yet): boss root movement
//! (intro bob, P_03 hops, P_04 orbit, P_06/08/09 lunges), the intro/death cinematics, minion
//! spawns, per-emitter bullet colors (every pattern uses the shared "Arrow" projectile
//! texture — per-color textures are an art task), music/portal. The source's one curving
//! emitter (Emitter Flower Dandelion, AngularSpeed 0.5 rad/s) is never activated by any phase
//! clip, so curving is supported by the engine (`Emitter::curve`) but unused here. P_05's
//! clip-animated −30↔+30°/s sway is approximated by two constant-spin half-windows; P_09's
//! 0.03s LookAt aim-snap is dropped (the ±180°/s spin dominates).

use spacetimedb::ReducerContext;
use crate::enemy::def_tables::*;
use crate::enemy::emitters::{Difficulty, DifficultySteps, Emitter};
use crate::main::seeds::Seed;
use super::make_phase;

/// AotMK speed units → world units. Source bullet speeds are 1–8; our existing content
/// flies at 50–200.
const AOTMK_SPEED_SCALE: f32 = 25.0;
/// Seconds of silence after a phase's last emitter window closes before the phase loops —
/// the source's Rest01/02 states (1.5–2.5s) between attack clips.
const PHASE_REST: f32 = 2.0;

/// Shorthand so the emitter tables stay readable.
const S: f32 = AOTMK_SPEED_SCALE;

fn none() -> DifficultySteps { DifficultySteps::default() }

/// Bullet lifetime so the pattern's fastest bullet crosses ~400 world units. `speed` and
/// `line_step` are the raw emitter values (the fastest line bullet flies at speed + step·line).
fn reach(speed: f32, line_step: f32, line: u32) -> f32 {
    (400.0 / (speed + line_step * line as f32).max(1.0)).clamp(2.5, 10.0)
}

// ===== Emitter roster =====
// One fn per distinct emitter object in the source scene. Values are raw AotMK numbers
// (×S for speeds/steps); `line` counts/steps are the source's Line.Count/DeltaSpeed.
// Difficulty steps are the source's DifficultyModifierSimple deltas (speed/arc/line in
// source units, converted).

/// P_01 mainstay: slow 4-way ring spinning +36°/s.
fn tentacles_ezpz() -> Emitter {
    Emitter::ring(4, 1.0 * S, 0.5).spin(36.0).line(1, S).lifetime(reach(S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 0.5, bullet_count: 1, ..none() })
}
/// Aimed 5-way 70° fan.
fn shotgun_simple_2() -> Emitter {
    Emitter::shotgun(5, 70.0, 4.0 * S, 0.5).target(EnemyTarget::AggroTarget).line(1, 0.5 * S).lifetime(reach(4.0 * S, 0.5 * S, 1))
        .difficulty(DifficultySteps { bullet_speed: 0.5 * S, line_count: 2, ..none() })
}
/// Aimed tight 2-way fan, 3-bullet speed ladder.
fn shotgun_simple_2b() -> Emitter {
    Emitter::shotgun(2, 10.0, 4.0 * S, 0.5).target(EnemyTarget::AggroTarget).line(3, 0.5 * S).lifetime(reach(4.0 * S, 0.5 * S, 3))
        .difficulty(DifficultySteps { bullet_speed: 0.5 * S, line_count: 2, ..none() })
}
/// Aimed 2-way ring at 3/s.
fn single_follow() -> Emitter {
    Emitter::ring(2, 4.0 * S, 3.0).target(EnemyTarget::AggroTarget).line(1, S).lifetime(reach(4.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 1.0, ..none() })
}
/// 4-way ring spinning +45°/s.
fn whirlpool() -> Emitter {
    Emitter::ring(4, 4.0 * S, 3.0).spin(45.0).line(1, S).lifetime(reach(4.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 0.5, bullet_count: 1, ..none() })
}
/// 4-way ring spinning −45°/s (counter-rotates with whirlpool).
fn whirlpool_reverse() -> Emitter {
    Emitter::ring(4, 4.0 * S, 3.0).spin(-45.0).line(1, S).lifetime(reach(4.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 0.5, bullet_count: 1, ..none() })
}
/// Slow accelerating 2-way ring spinning −45°/s.
fn whirlpool_reverse_1() -> Emitter {
    Emitter::ring(2, 1.0 * S, 3.5).spin(-45.0).accel(2.0 * S).line(1, S).lifetime(reach(S, S, 1) + 2.0)
        .difficulty(DifficultySteps { bullet_speed: 0.5 * S, fire_rate: 0.5, bullet_count: 2, ..none() })
}
/// Fast single-direction stream spinning −360°/s, accelerating.
fn whirlpool_reverse_2() -> Emitter {
    Emitter::ring(1, 1.0 * S, 20.0).spin(-360.0).accel(2.0 * S).line(1, S).lifetime(reach(S, S, 1) + 2.0)
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 5.0, bullet_count: 1, ..none() })
}
/// Aimed 20-way ring.
fn shotgun_simple_3() -> Emitter {
    Emitter::ring(20, 4.0 * S, 1.5).target(EnemyTarget::AggroTarget).line(1, S).lifetime(reach(4.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 0.5, bullet_count: 1, ..none() })
}
/// 4-way narrow fan spinning +200°/s.
fn sunburst() -> Emitter {
    Emitter::shotgun(4, 8.0, 5.0 * S, 5.0).spin(200.0).line(1, 0.5 * S).lifetime(reach(5.0 * S, 0.5 * S, 1))
        .difficulty(DifficultySteps { bullet_speed: 0.3 * S, fire_rate: 1.0, bullet_count: 1, line_count: 2, ..none() })
}
/// Source EnemyGun: 3-way 240° fan sweeping at +180°/s (the transcript's "fixed" emitter —
/// it actually sweeps). EnemyGun has no Line composition.
fn enemy_gun_test() -> Emitter {
    Emitter::shotgun(3, 240.0, 5.0 * S, 1.0).spin(180.0).lifetime(reach(5.0 * S, 0.0, 1))
}
/// Slow big 6-way ring spinning +60°/s, mildly accelerating.
fn big_bullets() -> Emitter {
    Emitter::ring(6, 2.0 * S, 0.5).spin(60.0).accel(0.1 * S).line(1, S).lifetime(reach(2.0 * S, S, 1) + 2.0)
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 0.5, bullet_count: 1, ..none() })
}
/// Aimed 15-way ring.
fn shotgun_simple() -> Emitter {
    Emitter::ring(15, 3.0 * S, 0.75).target(EnemyTarget::AggroTarget).line(1, S).lifetime(reach(3.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 1.0, bullet_count: 3, ..none() })
}
/// Static 5-way ring mounted left of center, 2-bullet ladder.
fn star_line() -> Emitter {
    Emitter::ring(5, 3.0 * S, 4.0).offset(-2.0 * S, 0.0).line(2, S).lifetime(reach(3.0 * S, S, 2))
        .difficulty(DifficultySteps { bullet_speed: 0.5 * S, fire_rate: 1.0, line_count: 1, ..none() })
}
/// Static 5-way ring mounted right of center, mirrored.
fn star_line_1() -> Emitter {
    Emitter::ring(5, 2.0 * S, 3.0).offset(2.0 * S, 0.0).angle(180.0).line(2, S).lifetime(reach(2.0 * S, S, 2))
        .difficulty(DifficultySteps { bullet_speed: 0.5 * S, fire_rate: 1.0, line_count: 1, ..none() })
}
/// Aimed single stream, 3-bullet ladder.
fn line_single_follow() -> Emitter {
    Emitter::volley(1, 5.0 * S, 3.0).target(EnemyTarget::AggroTarget).line(3, S).lifetime(reach(5.0 * S, S, 3))
        .difficulty(DifficultySteps { bullet_speed: 0.3 * S, fire_rate: 1.0, line_count: 1, ..none() })
}
/// 11-way ring crawling at +5°/s, decelerating mid-flight, trailing (negative) ladder.
fn wormy_loop() -> Emitter {
    Emitter::ring(11, 5.0 * S, 1.0).spin(5.0).accel(-S).line(2, -S).lifetime(reach(5.0 * S, 0.0, 1) + 2.0)
}
/// P_05's 10-way ring; the source clip sways its spin −30↔+30°/s — callers pick a half.
fn tentacles(spin: f32) -> Emitter {
    Emitter::ring(10, 6.0 * S, 3.0).spin(spin).line(1, S).lifetime(reach(6.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: 2.0 * S, fire_rate: 1.0, bullet_count: 1, ..none() })
}
/// 9-way ring spinning +360°/s in short pulses. Source DeltaSpeed is 0 here.
fn cool_pattern() -> Emitter {
    Emitter::ring(9, 3.0 * S, 4.0).spin(360.0).lifetime(reach(3.0 * S, 0.0, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 1.0, bullet_count: 2, ..none() })
}
/// 16-way ring orbiting the boss counter-clockwise (mount left of center).
fn twin_a() -> Emitter {
    Emitter::ring(16, 2.0 * S, 0.5).offset(-S, 0.0).spin(40.0).line(1, S).lifetime(reach(2.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 0.5, bullet_count: 3, ..none() })
}
/// 16-way ring orbiting clockwise (mount right of center).
fn twin_b() -> Emitter {
    Emitter::ring(16, 1.0 * S, 0.5).offset(S, 0.0).spin(-40.0).line(1, S).lifetime(reach(S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 0.5, bullet_count: 3, ..none() })
}
/// Aimed 2-way 15° fan, 3-bullet ladder, mounted right of center.
fn twin_c() -> Emitter {
    Emitter::shotgun(2, 15.0, 2.5 * S, 0.5).target(EnemyTarget::AggroTarget).offset(S, 0.0).line(3, S).lifetime(reach(2.5 * S, S, 3))
        .difficulty(DifficultySteps { bullet_speed: 0.5 * S, fire_rate: 1.0, bullet_count: 3, line_count: 1, ..none() })
}
/// 11-way ring spinning +40°/s in 0.5s pulses (P_07).
fn tentacles_sway_cw() -> Emitter {
    Emitter::ring(11, 5.0 * S, 6.5).spin(40.0).line(1, S).lifetime(reach(5.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 1.5, bullet_count: 1, ..none() })
}
/// 11-way ring spinning −40°/s in 0.5s pulses, phase-shifted against CW.
fn tentacles_sway_ccw() -> Emitter {
    Emitter::ring(11, 5.0 * S, 6.5).spin(-40.0).line(1, S).lifetime(reach(5.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 1.5, bullet_count: 1, ..none() })
}
/// Aimed single stream, 4-bullet ladder.
fn line_single_follow_faster() -> Emitter {
    Emitter::volley(1, 3.5 * S, 4.0).target(EnemyTarget::AggroTarget).line(4, S).lifetime(reach(3.5 * S, S, 4))
        .difficulty(DifficultySteps { bullet_speed: 0.5 * S, fire_rate: 1.0, bullet_count: 1, ..none() })
}
/// Aimed 18-way ring — three of these sit at different mounts (P_08).
fn shotgun_simple_456(offset_x: f32, offset_y: f32) -> Emitter {
    Emitter::ring(18, 3.0 * S, 3.0).target(EnemyTarget::AggroTarget).offset(offset_x, offset_y).line(1, S).lifetime(reach(3.0 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 1.0, bullet_count: 2, ..none() })
}
/// Aimed 2-way 50° fan at 20/s, 2-bullet ladder (P_08).
fn line_multiple_follow_split() -> Emitter {
    Emitter::shotgun(2, 50.0, 8.0 * S, 20.0).target(EnemyTarget::AggroTarget).line(2, S).lifetime(reach(8.0 * S, S, 2))
}
/// The signature 25/s stream spinning ±180°/s (P_09). `spin`/`angle` pick the mirror.
fn shotgun_20deg_78(spin: f32, angle: f32) -> Emitter {
    Emitter::volley(1, 5.0 * S, 25.0).spin(spin).angle(angle).line(2, S).lifetime(reach(5.0 * S, S, 2))
        .difficulty(DifficultySteps { line_count: 1, ..none() })
}
/// Intro-only: slow 3-way ring spinning +45°/s.
fn shotgun_simple_1() -> Emitter {
    Emitter::ring(3, S, 1.0).spin(45.0).line(1, S).lifetime(reach(S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 1.0, bullet_count: 1, ..none() })
}
/// Intro-only: slow single stream spinning −180°/s.
fn shotgun_20deg_vsmall() -> Emitter {
    Emitter::volley(1, 1.5 * S, 6.0).spin(-180.0).line(1, S).lifetime(reach(1.5 * S, S, 1))
        .difficulty(DifficultySteps { bullet_speed: S, fire_rate: 1.0, ..none() })
}

/// Seeds the four difficulty variants: `GorgonBoss_Easy` … `GorgonBoss_Madness`.
pub fn seed(ctx: &ReducerContext) {
    for difficulty in Difficulty::ALL {
        build(ctx, difficulty);
    }
}

fn build(ctx: &ReducerContext, difficulty: Difficulty) {
    let phases = vec![
        // Phase 0 — Intro (100–90% HP). Source clip: 8.8s.
        make_phase(ctx, 0, 0.0, SteeringMovement::idle(), PHASE_REST, vec![
            shotgun_simple_1().window(0.55, 6.52).compile(ctx, difficulty),
            shotgun_20deg_vsmall().window(3.92, 3.15).compile(ctx, difficulty),
            shotgun_simple_3().window(8.4, 0.4).compile(ctx, difficulty),
        ]),
        // Phase 1 — P_01 (≤90%). Source clip: 8.55s.
        make_phase(ctx, 1, 0.9, SteeringMovement::idle(), PHASE_REST, vec![
            tentacles_ezpz().window(0.0, 8.55).compile(ctx, difficulty),
            shotgun_simple_2().window(0.0, 6.2).compile(ctx, difficulty),
            shotgun_simple_2b().window(0.0, 7.5).compile(ctx, difficulty),
            single_follow().window(6.2, 2.35).compile(ctx, difficulty),
        ]),
        // Phase 2 — P_02 (≤80%). Source clip: 8.65s. (Root bob not ported.)
        make_phase(ctx, 2, 0.8, SteeringMovement::idle(), PHASE_REST, vec![
            whirlpool().window(0.0, 6.45).compile(ctx, difficulty),
            whirlpool_reverse().window(0.0, 6.47).compile(ctx, difficulty),
            whirlpool_reverse_1().window(0.0, 6.47).compile(ctx, difficulty),
            whirlpool_reverse_2().window(7.18, 1.17).compile(ctx, difficulty),
        ]),
        // Phase 3 — P_03 (≤70%). Source clip: 7.02s. (Root hops not ported.)
        make_phase(ctx, 3, 0.7, SteeringMovement::idle(), PHASE_REST, vec![
            enemy_gun_test().window(0.0, 1.7).compile(ctx, difficulty),
            shotgun_simple_3().window(1.77, 5.25).compile(ctx, difficulty),
            sunburst().window(2.47, 4.55).compile(ctx, difficulty),
            whirlpool().window(4.6, 2.42).compile(ctx, difficulty),
        ]),
        // Phase 4 — P_04 (≤60%). Source clip: 10.33s. (Root orbit not ported.)
        make_phase(ctx, 4, 0.6, SteeringMovement::idle(), PHASE_REST, vec![
            big_bullets().window(0.0, 10.33).compile(ctx, difficulty),
            shotgun_simple().window(0.0, 1.7).compile(ctx, difficulty),
            shotgun_simple().window(4.37, 2.21).compile(ctx, difficulty),
            shotgun_simple().window(9.98, 0.35).compile(ctx, difficulty),
            star_line().window(1.53, 1.49).compile(ctx, difficulty),
            star_line().window(6.7, 2.1).compile(ctx, difficulty),
            star_line_1().window(2.43, 1.22).compile(ctx, difficulty),
            star_line_1().window(7.6, 1.83).compile(ctx, difficulty),
            line_single_follow().window(4.33, 2.0).compile(ctx, difficulty),
        ]),
        // Phase 5 — P_05 (≤50%). Source clip: 10.5s. Sway ≈ two constant-spin half-windows.
        make_phase(ctx, 5, 0.5, SteeringMovement::idle(), PHASE_REST, vec![
            wormy_loop().window(0.0, 5.0).compile(ctx, difficulty),
            tentacles(-30.0).window(3.0, 3.75).compile(ctx, difficulty),
            tentacles(30.0).window(6.75, 3.75).compile(ctx, difficulty),
            big_bullets().window(5.78, 4.72).compile(ctx, difficulty),
            cool_pattern().window(1.95, 0.22).compile(ctx, difficulty),
            cool_pattern().window(6.42, 0.28).compile(ctx, difficulty),
            cool_pattern().window(7.82, 0.4).compile(ctx, difficulty),
            cool_pattern().window(9.02, 0.43).compile(ctx, difficulty),
        ]),
        // Phase 6 — P_06 (≤40%). Source clip: 1s. (Root lunge not ported.)
        make_phase(ctx, 6, 0.4, SteeringMovement::idle(), PHASE_REST, vec![
            twin_a().window(0.0, 1.0).compile(ctx, difficulty),
            twin_b().window(0.0, 1.0).compile(ctx, difficulty),
            twin_c().window(0.0, 0.38).compile(ctx, difficulty),
            twin_c().window(0.78, 0.22).compile(ctx, difficulty),
        ]),
        // Phase 7 — P_07 (≤30%). Source clip: 11.5s. CW pulses at 1.5,2.5,…,10.5; CCW at
        // 2.0,3.0,…,11.0 (interleaved 0.5s on/off) — generated, not unrolled.
        make_phase(ctx, 7, 0.3, SteeringMovement::idle(), PHASE_REST, {
            let mut attacks: Vec<AttackSequence> = (0..10)
                .flat_map(|i| [
                    tentacles_sway_cw().window(1.5 + i as f32, 0.5).compile(ctx, difficulty),
                    tentacles_sway_ccw().window(2.0 + i as f32, 0.5).compile(ctx, difficulty),
                ])
                .collect();
            attacks.extend([
                line_single_follow_faster().window(4.5, 1.52).compile(ctx, difficulty),
                line_single_follow_faster().window(8.98, 1.52).compile(ctx, difficulty),
                big_bullets().window(6.0, 0.92).compile(ctx, difficulty),
                big_bullets().window(10.48, 0.92).compile(ctx, difficulty),
            ]);
            attacks
        }),
        // Phase 8 — P_08 (≤20%). Source clip: 1s. (Root dash not ported.)
        make_phase(ctx, 8, 0.2, SteeringMovement::idle(), PHASE_REST, vec![
            shotgun_simple_456(-1.81 * S, -1.47 * S).window(0.08, 0.25).compile(ctx, difficulty),
            shotgun_simple_456(-1.81 * S, -1.47 * S).window(0.58, 0.25).compile(ctx, difficulty),
            shotgun_simple_456(0.0, 1.53 * S).window(0.17, 0.25).compile(ctx, difficulty),
            shotgun_simple_456(0.0, 1.53 * S).window(0.67, 0.25).compile(ctx, difficulty),
            shotgun_simple_456(2.15 * S, -1.5 * S).window(0.25, 0.25).compile(ctx, difficulty),
            shotgun_simple_456(2.15 * S, -1.5 * S).window(0.75, 0.25).compile(ctx, difficulty),
            line_multiple_follow_split().window(0.27, 0.73).compile(ctx, difficulty),
        ]),
        // Phase 9 — P_09 (≤10%): the signature crossed 25/s spiral streams. Source clip: 1s.
        // (Root dash and the 0.03s aim-snap not ported.)
        make_phase(ctx, 9, 0.1, SteeringMovement::idle(), PHASE_REST, vec![
            shotgun_20deg_78(180.0, 0.0).window(0.167, 0.716).compile(ctx, difficulty),
            shotgun_20deg_78(-180.0, 180.0).window(0.167, 0.716).compile(ctx, difficulty),
            big_bullets().window(0.367, 0.633).compile(ctx, difficulty),
        ]),
    ];

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: format!("GorgonBoss_{}", difficulty.suffix()),
        texture_id: "Enemy".to_string(), // placeholder — boss sprite is an art task
        display_name: format!("Gorgon ({})", difficulty.suffix()),
        // Source EnemyHealth: maxHealth 750 + 250 per difficulty step.
        max_hp: 750 + 250 * difficulty.step() as u32,
        defense: 8,
        move_sim_factor: 2.75,
        attack_sim_factor: 1.35,
        aggro_lock_seconds: 30.0,
        stun_behavior: StunBehavior::Pause,
        phases,
    });
}
