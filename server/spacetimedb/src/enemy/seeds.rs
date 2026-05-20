use spacetimedb::{ReducerContext, Table};
use crate::enemy::tables::*;
use crate::world::seeds::Seed;

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

pub fn make_attack(pattern_type: PatternType, target: EnemyTarget, fire_rate: f32) -> AttackDef {
    AttackDef { pattern_type, target, fire_rate }
}

pub fn seed_default_enemies(ctx: &ReducerContext) {
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "default_enemy".to_string(),
        res_path: "res://Scenes/default_enemy.tscn".to_string(),
        display_name: "Default Enemy".to_string(),
        max_hp: 100,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Wander(WanderParams { speed: 25.0, change_interval: 2.0 }), vec![
                make_attack(PatternType::Straight(StraightParams { speed: 50.0, count: 5 }), EnemyTarget::NearestPlayer, 1.0),
            ]),
        ],
    });
}
