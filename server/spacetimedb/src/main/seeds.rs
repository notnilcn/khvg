use spacetimedb::{ReducerContext, Table};
use crate::world::tables::*;
use crate::enemy::tables::*;

pub fn seed_default_textures(ctx: &ReducerContext) {
    TextureEntry::seed(ctx, TextureEntry {
        texture_id: "Enemy".to_string(),
        res_path: "res://Textures/Enemies/Knight.tres".to_string(),
        kind: TextureKind::Enemy,
    });
    TextureEntry::seed(ctx, TextureEntry {
        texture_id: "Archer".to_string(),
        res_path: "res://Textures/Enemies/Archer.tres".to_string(),
        kind: TextureKind::Enemy,
    });
    TextureEntry::seed(ctx, TextureEntry {
        texture_id: "Knight".to_string(),
        res_path: "res://Textures/Players/Knight.tres".to_string(),
        kind: TextureKind::Player,
    });
    TextureEntry::seed(ctx, TextureEntry {
        texture_id: "Bullet".to_string(),
        res_path: "res://Textures/Attacks/Bullet.png".to_string(),
        kind: TextureKind::Bullet,
    });
    TextureEntry::seed(ctx, TextureEntry {
        texture_id: "Arrow".to_string(),
        res_path: "res://Textures/Attacks/Arrow.png".to_string(),
        kind: TextureKind::Bullet,
    });
}

pub fn seed_default_enemies(ctx: &ReducerContext) {
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "Enemy".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Enemy".to_string(),
        max_hp: 100,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Wander(WanderParams { speed: 25.0, change_interval: 2.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 0.0, "Bullet", 0.0),
                ], true), 1.0),
            ]),
        ],
    });
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "Archer".to_string(),
        texture_id: "Archer".to_string(),
        display_name: "Archer".to_string(),
        max_hp: 100,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Wander(WanderParams { speed: 30.0, change_interval: 1.5 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Straight(StraightParams { speed: 100.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 0.0, "Arrow", 0.0),
                ], true), 0.75),
            ]),
        ],
    });

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "TestBoss".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Test Boss".to_string(),
        max_hp: 1000,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Stationary, vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 20.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 40.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 60.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 80.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 100.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 120.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 140.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 160.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 180.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 200.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 220.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 240.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 260.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 280.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 300.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 320.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 50.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 340.0, "Bullet", 0.0),
                ], true), 1.0),
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 90.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 180.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 270.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 10.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 100.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 190.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 280.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 20.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 110.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 200.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 290.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 30.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 120.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 210.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 300.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 40.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 130.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 220.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 310.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 50.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 140.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 230.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 320.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 60.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 150.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 240.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 330.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 70.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 160.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 250.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 340.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 80.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 170.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 260.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 350.0, "Arrow", 0.1),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 90.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 180.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 270.0, "Arrow", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 35.0, count: 1 }), EnemyTarget::Idle, 0.0, 0.0, 360.0, "Arrow", 0.1),
                ], true), 1.0),
            ]),
        ],
    });
}

pub fn seed_stress_enemies(ctx: &ReducerContext) {
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "LowStress".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Low Stress".to_string(),
        max_hp: 100,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Wander(WanderParams { speed: 20.0, change_interval: 3.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Ring(RingParams { speed: 60.0, count: 8 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 0.0),
                ], true), 0.3),
            ]),
        ],
    });

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "MedStress".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Med Stress".to_string(),
        max_hp: 150,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Wander(WanderParams { speed: 35.0, change_interval: 2.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Ring(RingParams { speed: 80.0, count: 16 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 0.0),
                ], true), 0.8),
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Straight(StraightParams { speed: 120.0, count: 3 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 0.0, "Arrow", 0.0),
                ], true), 1.5),
            ]),
        ],
    });

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "HighStress".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "High Stress".to_string(),
        max_hp: 200,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Chase(ChaseParams { speed: 50.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Ring(RingParams { speed: 60.0, count: 24 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 0.0),
                ], true), 1.5),
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Burst(BurstParams { speed: 100.0, count: 8, spread: 1.05 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 0.0, "Arrow", 0.0),
                ], true), 2.0),
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Straight(StraightParams { speed: 80.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 0.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 80.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 45.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 80.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 90.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 80.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 135.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 80.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 180.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 80.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 225.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 80.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 270.0, "Bullet", 0.0),
                    seq_single(PatternType::Straight(StraightParams { speed: 80.0, count: 1 }), EnemyTarget::NearestPlayer, 0.0, 0.0, 315.0, "Bullet", 0.0),
                ], true), 2.5),
            ]),
        ],
    });
}

pub trait Seed: Sized {
    fn seed(ctx: &ReducerContext, data: Self);
}

impl Seed for MapConfig {
    fn seed(ctx: &ReducerContext, data: MapConfig) {
        if ctx.db.map_config().id().find(&0u32).is_some() {
            ctx.db.map_config().id().update(data);
        } else {
            ctx.db.map_config().insert(data);
        }
    }
}

impl Seed for TextureEntry {
    fn seed(ctx: &ReducerContext, data: TextureEntry) {
        if ctx.db.texture_entry().texture_id().find(&data.texture_id).is_some() {
            ctx.db.texture_entry().texture_id().update(data);
        } else {
            ctx.db.texture_entry().insert(data);
        }
    }
}

impl Seed for ItemStats {
    fn seed(ctx: &ReducerContext, data: ItemStats) {
        if ctx.db.item_stats().item_id().find(&data.item_id).is_some() {
            ctx.db.item_stats().item_id().update(data);
        } else {
            ctx.db.item_stats().insert(data);
        }
    }
}

impl Seed for EnemyTemplate {
    fn seed(ctx: &ReducerContext, data: EnemyTemplate) {
        if ctx.db.enemy_template().template_id().find(&data.template_id).is_some() {
            ctx.db.enemy_template().template_id().update(data);
        } else {
            ctx.db.enemy_template().insert(data);
        }
    }
}

pub fn make_phase(phase_index: u8, hp_threshold: f32, movement: MovementBehavior, attacks: Vec<AttackDef>) -> PhaseDef {
    PhaseDef { phase_index, hp_threshold, movement, attacks }
}

pub fn make_attack(attack: AttackSequence, fire_rate: f32) -> AttackDef {
    AttackDef { attack, fire_rate }
}

pub fn make_sequence(steps: Vec<SequenceStep>, loop_sequence: bool) -> AttackSequence {
    AttackSequence { steps, loop_sequence }
}

pub fn seq_single(pattern_type: PatternType, target: EnemyTarget, origin_offset_x: f32, origin_offset_y: f32, base_angle_offset: f32, texture_id: &str, wait: f32) -> SequenceStep {
    SequenceStep::Single(SequenceSingle { pattern_type, target, origin_offset_x, origin_offset_y, base_angle_offset, texture_id: texture_id.to_string(), wait })
}

pub fn seq_repeat(pattern_type: PatternType, target: EnemyTarget, origin_offset_x: f32, origin_offset_y: f32, base_angle_offset: f32, texture_id: &str, interval: f32, count: u32, wait: f32) -> SequenceStep {
    SequenceStep::Repeat(SequenceRepeat { pattern_type, target, origin_offset_x, origin_offset_y, base_angle_offset, texture_id: texture_id.to_string(), interval, count, wait })
}
