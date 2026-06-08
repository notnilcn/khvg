use spacetimedb::{ReducerContext, Table};
use crate::world::tables::*;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;

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
        simulation_range: 210.0,
        aggro_lock_seconds: 15.0,
        phases: vec![
            make_phase(ctx, 0, 0.0, MovementBehavior::Wander(WanderParams { speed: 30.0, active_duration: 2.5, pause_duration: 1.5 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Ring(RingParams { speed: 70.0, count: 8 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.0, 0.0),
                ], 0.0),
            ]),
        ],
    });
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "Archer".to_string(),
        texture_id: "Archer".to_string(),
        display_name: "Archer".to_string(),
        max_hp: 100,
        simulation_range: 660.0,
        aggro_lock_seconds: 25.0,
        phases: vec![
            make_phase(ctx, 0, 0.0, MovementBehavior::Flee(FleeParams { speed: 40.0, min_range: 180.0, active_duration: 2.0, pause_duration: 1.5 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Volley(VolleyParams { speed: 200.0, count: 3, speed_variance: 20.0, angle_jitter: 0.05 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 0.0),
                ], 0.0),
                make_sequence(vec![
                    seq_single(ctx, PatternType::Shotgun(ShotgunParams { speed: 130.0, count: 5, spread: 0.4, speed_variance: 20.0, lifetime_variance: 0.4 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 0.0),
                ], 0.5),
            ]),
        ],
    });

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "TestBoss".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Test Boss".to_string(),
        max_hp: 1000,
        simulation_range: 790.0,
        aggro_lock_seconds: 30.0,
        phases: vec![
            // Phase 0 (100% HP): turret — Ring read cleanly with no movement noise
            make_phase(ctx, 0, 0.0, MovementBehavior::Stationary, 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Ring(RingParams { speed: 130.0, count: 24 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.5, 0.0),
                ], 0.0),
            ]),
            // Phase 1 (<=80% HP): patrols while sweeping an aimed fan — repositioning shifts where the gap lands
            make_phase(ctx, 1, 0.8, MovementBehavior::Wander(WanderParams { speed: 40.0, active_duration: 2.5, pause_duration: 1.5 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Curtain(CurtainParams { speed: 120.0, count: 11, angle_span: 1.6, gap_index: 5 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 3.0, 0.0),
                ], 0.0),
            ]),
            // Phase 2 (<=60% HP): rusher — closes the gap while spraying jittered aimed shots
            make_phase(ctx, 2, 0.6, MovementBehavior::Chase(ChaseParams { speed: 55.0, active_duration: 3.0, pause_duration: 1.0 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Volley(VolleyParams { speed: 180.0, count: 5, speed_variance: 25.0, angle_jitter: 0.07 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.2, 0.0),
                ], 0.0),
            ]),
            // Phase 3 (<=40% HP): kiter — keeps its distance while peppering with randomized pellets
            make_phase(ctx, 3, 0.4, MovementBehavior::Flee(FleeParams { speed: 45.0, min_range: 200.0, active_duration: 2.0, pause_duration: 1.0 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Shotgun(ShotgunParams { speed: 120.0, count: 7, spread: 0.8, speed_variance: 25.0, lifetime_variance: 0.5 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.2, 0.0),
                ], 0.0),
            ]),
            // Phase 4 (<=20% HP): plants itself and erupts in a randomized 360° nova — desperation finale
            make_phase(ctx, 4, 0.2, MovementBehavior::Stationary, 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Explosion(ExplosionParams { speed: 100.0, count: 20, speed_variance: 35.0, lifetime_variance: 0.9 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 3.0, 0.0),
                ], 0.0),
            ]),
        ],
    });
}

pub fn seed_test_boss_p2(ctx: &ReducerContext) {
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "TestBossP2".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Test Boss P2".to_string(),
        max_hp: 100,
        simulation_range: 790.0,
        aggro_lock_seconds: 30.0,
        phases: vec![
            make_phase(ctx, 0, 0.0, MovementBehavior::Stationary, 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Ring(RingParams { speed: 50.0, count: 6 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.0, 0.5),
                    seq_multi(ctx, vec![
                        multi_shot(PatternType::Shotgun(ShotgunParams { speed: 50.0, count: 25, spread: 100.0_f32.to_radians(), speed_variance: 0.0, lifetime_variance: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0),
                        multi_shot(PatternType::Shotgun(ShotgunParams { speed: 50.0, count: 14, spread: 12.5_f32.to_radians(), speed_variance: 0.0, lifetime_variance: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0),
                    ], 0.5),
                    seq_repeat(ctx, PatternType::Ring(RingParams { speed: 50.0, count: 6 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.0, 0.5, 2, 0.5),
                ], 0.0),
            ]),
        ],
    });
}

pub fn seed_test_boss_p3(ctx: &ReducerContext) {
    let bullet = |angle: f32| multi_shot(PatternType::Volley(VolleyParams { speed: 65.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, angle, "Bullet", 2.0);
    let bullet_many = |offset: f32, wait: f32| seq_multi(ctx, vec![bullet(offset), bullet(offset + 60.0), bullet(offset + 120.0), bullet(offset + 180.0), bullet(offset + 240.0), bullet(offset + 300.0)], wait);
    let arrow = |angle: f32| multi_shot(PatternType::Volley(VolleyParams { speed: 50.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::Idle, 0.0, 0.0, angle, "Arrow", 2.0);
    let arrow_many = |offset: f32, wait: f32| seq_multi(ctx, vec![arrow(offset), arrow(offset + 60.0), arrow(offset + 120.0), arrow(offset + 180.0), arrow(offset + 240.0), arrow(offset + 300.0)], wait);

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "TestBossP3".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Test Boss P3".to_string(),
        max_hp: 100,
        simulation_range: 790.0,
        aggro_lock_seconds: 30.0,
        phases: vec![
            make_phase(ctx, 0, 0.0, MovementBehavior::Stationary, 0.0, vec![
                make_sequence(vec![
                    bullet_many(30.0, 0.15), bullet_many(40.0, 0.15), bullet_many(50.0, 0.15),
                    bullet_many(0.0, 0.15), bullet_many(10.0, 0.15), bullet_many(20.0, 0.15),
                ], 0.0),
                make_sequence(vec![
                    arrow_many(0.0, 0.1), arrow_many(10.0, 0.1), arrow_many(20.0, 0.1),
                    arrow_many(30.0, 0.1), arrow_many(40.0, 0.1), arrow_many(50.0, 0.1),
                ], 0.0),
                make_sequence(vec![
                    seq_single(ctx, PatternType::Volley(VolleyParams { speed: 100.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 2.0, 1.0),
                ], 0.0),
            ]),
        ],
    });
}

fn seed_test_boss_p3_clone(ctx: &ReducerContext, template_id: &str, display_name: &str) {
    let bullet = |angle: f32| multi_shot(PatternType::Volley(VolleyParams { speed: 50.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, angle, "Bullet", 2.0);
    let bullet_many = |offset: f32| seq_multi(ctx, vec![bullet(offset), bullet(offset + 60.0), bullet(offset + 120.0), bullet(offset + 180.0), bullet(offset + 240.0), bullet(offset + 300.0)], 0.1);
    let arrow = |angle: f32| multi_shot(PatternType::Volley(VolleyParams { speed: 50.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::Idle, 0.0, 0.0, angle, "Arrow", 2.0);
    let arrow_many = |offset: f32| seq_multi(ctx, vec![arrow(offset), arrow(offset + 60.0), arrow(offset + 120.0), arrow(offset + 180.0), arrow(offset + 240.0), arrow(offset + 300.0)], 0.1);

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: template_id.to_string(),
        texture_id: "Enemy".to_string(),
        display_name: display_name.to_string(),
        max_hp: 100,
        simulation_range: 790.0,
        aggro_lock_seconds: 30.0,
        phases: vec![
            make_phase(ctx, 0, 0.0, MovementBehavior::Stationary, 0.0, vec![
                make_sequence(vec![
                    bullet_many(30.0), bullet_many(40.0), bullet_many(50.0),
                    bullet_many(0.0), bullet_many(10.0), bullet_many(20.0),
                ], 0.0),
                make_sequence(vec![
                    arrow_many(0.0), arrow_many(10.0), arrow_many(20.0),
                    arrow_many(30.0), arrow_many(40.0), arrow_many(50.0),
                ], 0.0),
            ]),
        ],
    });
}

pub fn seed_test_boss_p4(ctx: &ReducerContext) { seed_test_boss_p3_clone(ctx, "TestBossP4", "Test Boss P4"); }
pub fn seed_test_boss_p5(ctx: &ReducerContext) { seed_test_boss_p3_clone(ctx, "TestBossP5", "Test Boss P5"); }
pub fn seed_test_boss_p6(ctx: &ReducerContext) { seed_test_boss_p3_clone(ctx, "TestBossP6", "Test Boss P6"); }

pub fn seed_default_items(ctx: &ReducerContext) {
    seed_weapon(ctx, "StarterBow",     "Starter Bow",     "Arrow",  "Arrow",  10, 150.0, 4.0, 300.0,  3, 25, false, WeaponPattern::Triple,  0.35);
    seed_weapon(ctx, "StarterRifle",   "Starter Rifle",   "Bullet", "Bullet", 60, 500.0, 0.8, 700.0,  1, 50, true, WeaponPattern::Single,  0.0);
    seed_weapon(ctx, "StarterShotgun", "Starter Shotgun", "Arrow", "Arrow", 30, 100.0, 1.5, 250.0, 8, 25, false, WeaponPattern::Cluster, 1.05);
}

pub fn seed_weapon(ctx: &ReducerContext, item_id: &str, display_name: &str, texture_id: &str, projectile_texture_id: &str, damage: u32, range: f32, fire_rate: f32, bullet_speed: f32, shot_count: u32, zone_count: u32, pierce: bool, pattern: WeaponPattern, spread_angle: f32) {
    Weapon::seed(ctx, Weapon { weapon_id: item_id.to_string(), damage, range, fire_rate, bullet_speed, shot_count, zone_count, pierce, projectile_texture_id: projectile_texture_id.to_string(), pattern, spread_angle });
    Item::seed(ctx, Item { item_id: item_id.to_string(), display_name: display_name.to_string(), texture_id: texture_id.to_string(), item_type: ItemType::Weapon });
}

pub fn seed_armor(ctx: &ReducerContext, item_id: &str, display_name: &str, texture_id: &str, defense_bonus: i32, vitality_bonus: i32, hp_bonus: u32) {
    Armor::seed(ctx, Armor { armor_id: item_id.to_string(), defense_bonus, vitality_bonus, hp_bonus });
    Item::seed(ctx, Item { item_id: item_id.to_string(), display_name: display_name.to_string(), texture_id: texture_id.to_string(), item_type: ItemType::Armor });
}

pub fn seed_ability(ctx: &ReducerContext, item_id: &str, display_name: &str, texture_id: &str, cooldown: f32, duration: f32, potency: f32) {
    Artifact::seed(ctx, Artifact { artifact_id: item_id.to_string(), cooldown, duration, potency });
    Item::seed(ctx, Item { item_id: item_id.to_string(), display_name: display_name.to_string(), texture_id: texture_id.to_string(), item_type: ItemType::Ability });
}

pub fn seed_accessory(ctx: &ReducerContext, item_id: &str, display_name: &str, texture_id: &str, strength_bonus: i32, wisdom_bonus: i32, dexterity_bonus: i32, speed_bonus: i32) {
    Accessory::seed(ctx, Accessory { accessory_id: item_id.to_string(), strength_bonus, wisdom_bonus, dexterity_bonus, speed_bonus });
    Item::seed(ctx, Item { item_id: item_id.to_string(), display_name: display_name.to_string(), texture_id: texture_id.to_string(), item_type: ItemType::Accessory });
}

pub fn seed_consumable(ctx: &ReducerContext, item_id: &str, display_name: &str, texture_id: &str, effect: &str, potency: f32, duration: f32) {
    Consumable::seed(ctx, Consumable { consumable_id: item_id.to_string(), effect: effect.to_string(), potency, duration });
    Item::seed(ctx, Item { item_id: item_id.to_string(), display_name: display_name.to_string(), texture_id: texture_id.to_string(), item_type: ItemType::Consumable });
}

pub fn make_phase(ctx: &ReducerContext, phase_index: u8, hp_threshold: f32, movement: MovementBehavior, phase_loop_delay: f32, attacks: Vec<AttackSequence>) -> PhaseDef {
    let def = ctx.db.movement_def().insert(MovementDef { def_id: 0, movement });
    PhaseDef { phase_index, hp_threshold, movement_def_id: def.def_id, phase_loop_delay, attacks }
}

pub fn make_sequence(steps: Vec<SequenceStepDef>, start_delay: f32) -> AttackSequence {
    AttackSequence { steps, start_delay }
}

pub fn seq_single(ctx: &ReducerContext, pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, next_step_delay: f32) -> SequenceStepDef {
    let def = ctx.db.single_step_def().insert(SingleStepDef { def_id: 0, pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, next_step_delay });
    SequenceStepDef::Single(def.def_id)
}

pub fn seq_repeat(ctx: &ReducerContext, pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, repeat_interval: f32, repeat_target: u32, next_step_delay: f32) -> SequenceStepDef {
    let def = ctx.db.repeat_step_def().insert(RepeatStepDef { def_id: 0, pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, repeat_interval, repeat_target, next_step_delay });
    SequenceStepDef::Repeat(def.def_id)
}

pub fn multi_shot(pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32) -> MultiShot {
    MultiShot { pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime }
}

pub fn seq_multi(ctx: &ReducerContext, shots: Vec<MultiShot>, next_step_delay: f32) -> SequenceStepDef {
    let def = ctx.db.multi_step_def().insert(MultiStepDef { def_id: 0, shots, next_step_delay });
    SequenceStepDef::Multi(def.def_id)
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

impl Seed for Item {
    fn seed(ctx: &ReducerContext, data: Item) {
        if ctx.db.item().item_id().find(&data.item_id).is_some() {
            ctx.db.item().item_id().update(data);
        } else {
            ctx.db.item().insert(data);
        }
    }
}

impl Seed for Weapon {
    fn seed(ctx: &ReducerContext, data: Weapon) {
        if ctx.db.weapon().weapon_id().find(&data.weapon_id).is_some() {
            ctx.db.weapon().weapon_id().update(data);
        } else {
            ctx.db.weapon().insert(data);
        }
    }
}

impl Seed for Armor {
    fn seed(ctx: &ReducerContext, data: Armor) {
        if ctx.db.armor().armor_id().find(&data.armor_id).is_some() {
            ctx.db.armor().armor_id().update(data);
        } else {
            ctx.db.armor().insert(data);
        }
    }
}

impl Seed for Artifact {
    fn seed(ctx: &ReducerContext, data: Artifact) {
        if ctx.db.artifact().artifact_id().find(&data.artifact_id).is_some() {
            ctx.db.artifact().artifact_id().update(data);
        } else {
            ctx.db.artifact().insert(data);
        }
    }
}

impl Seed for Accessory {
    fn seed(ctx: &ReducerContext, data: Accessory) {
        if ctx.db.accessory().accessory_id().find(&data.accessory_id).is_some() {
            ctx.db.accessory().accessory_id().update(data);
        } else {
            ctx.db.accessory().insert(data);
        }
    }
}

impl Seed for Consumable {
    fn seed(ctx: &ReducerContext, data: Consumable) {
        if ctx.db.consumable().consumable_id().find(&data.consumable_id).is_some() {
            ctx.db.consumable().consumable_id().update(data);
        } else {
            ctx.db.consumable().insert(data);
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


