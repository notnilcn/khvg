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
                make_attack(PatternType::Straight(StraightParams { speed: 50.0, count: 5 }), EnemyTarget::NearestPlayer, 1.0, "Bullet"),
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
                make_attack(PatternType::Straight(StraightParams { speed: 100.0, count: 2 }), EnemyTarget::NearestPlayer, 0.75, "Arrow"),
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

pub fn make_attack(pattern_type: PatternType, target: EnemyTarget, fire_rate: f32, texture_id: &str) -> AttackDef {
    AttackDef { pattern_type, target, fire_rate, texture_id: texture_id.to_string() }
}
