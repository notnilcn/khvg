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
        simulation_range: 210.0,
        aggro_lock_seconds: 15.0,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Wander(WanderParams { speed: 30.0, active_duration: 2.5, pause_duration: 1.5 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Ring(RingParams { speed: 70.0, count: 8 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.0, 0.0),
                ], true), 1.8),
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
            make_phase(0, 0.0, MovementBehavior::Flee(FleeParams { speed: 40.0, min_range: 180.0, active_duration: 2.0, pause_duration: 1.5 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Volley(VolleyParams { speed: 200.0, count: 3, speed_variance: 20.0, angle_jitter: 0.05 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 0.0),
                ], true), 1.2),
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Shotgun(ShotgunParams { speed: 130.0, count: 5, spread: 0.4, speed_variance: 20.0, lifetime_variance: 0.4 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 0.0),
                ], true), 2.2),
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
            make_phase(0, 0.0, MovementBehavior::Stationary, vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Ring(RingParams { speed: 130.0, count: 24 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.5, 0.0),
                ], true), 1.0),
            ]),
            // Phase 1 (<=80% HP): patrols while sweeping an aimed fan — repositioning shifts where the gap lands
            make_phase(1, 0.8, MovementBehavior::Wander(WanderParams { speed: 40.0, active_duration: 2.5, pause_duration: 1.5 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Curtain(CurtainParams { speed: 120.0, count: 11, angle_span: 1.6, gap_index: 5 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 3.0, 0.0),
                ], true), 0.6),
            ]),
            // Phase 2 (<=60% HP): rusher — closes the gap while spraying jittered aimed shots
            make_phase(2, 0.6, MovementBehavior::Chase(ChaseParams { speed: 55.0, active_duration: 3.0, pause_duration: 1.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Volley(VolleyParams { speed: 180.0, count: 5, speed_variance: 25.0, angle_jitter: 0.07 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.2, 0.0),
                ], true), 2.0),
            ]),
            // Phase 3 (<=40% HP): kiter — keeps its distance while peppering with randomized pellets
            make_phase(3, 0.4, MovementBehavior::Flee(FleeParams { speed: 45.0, min_range: 200.0, active_duration: 2.0, pause_duration: 1.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Shotgun(ShotgunParams { speed: 120.0, count: 7, spread: 0.8, speed_variance: 25.0, lifetime_variance: 0.5 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.2, 0.0),
                ], true), 0.8),
            ]),
            // Phase 4 (<=20% HP): plants itself and erupts in a randomized 360° nova — desperation finale
            make_phase(4, 0.2, MovementBehavior::Stationary, vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Explosion(ExplosionParams { speed: 100.0, count: 20, speed_variance: 35.0, lifetime_variance: 0.9 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 3.0, 0.0),
                ], true), 0.5),
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
        simulation_range: 180.0,
        aggro_lock_seconds: 20.0,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Wander(WanderParams { speed: 20.0, active_duration: 3.0, pause_duration: 1.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Ring(RingParams { speed: 60.0, count: 8 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.0, 0.0),
                ], true), 0.3),
            ]),
        ],
    });

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "MedStress".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Med Stress".to_string(),
        max_hp: 150,
        simulation_range: 450.0,
        aggro_lock_seconds: 20.0,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Wander(WanderParams { speed: 35.0, active_duration: 2.0, pause_duration: 1.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Ring(RingParams { speed: 80.0, count: 16 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.0, 0.0),
                ], true), 0.8),
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Volley(VolleyParams { speed: 120.0, count: 3, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.5, 0.0),
                ], true), 1.5),
            ]),
        ],
    });

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "HighStress".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "High Stress".to_string(),
        max_hp: 200,
        simulation_range: 375.0,
        aggro_lock_seconds: 20.0,
        phases: vec![
            make_phase(0, 0.0, MovementBehavior::Chase(ChaseParams { speed: 50.0, active_duration: 4.0, pause_duration: 1.0 }), vec![
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Ring(RingParams { speed: 60.0, count: 24 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Bullet", 2.0, 0.0),
                ], true), 1.5),
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Shotgun(ShotgunParams { speed: 100.0, count: 8, spread: 1.05, speed_variance: 0.0, lifetime_variance: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.5, 0.0),
                ], true), 2.0),
                make_attack(make_sequence(vec![
                    seq_single(PatternType::Volley(VolleyParams { speed: 80.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Bullet", 3.0, 0.0),
                    seq_single(PatternType::Volley(VolleyParams { speed: 80.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 45.0, "Bullet", 3.0, 0.0),
                    seq_single(PatternType::Volley(VolleyParams { speed: 80.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 90.0, "Bullet", 3.0, 0.0),
                    seq_single(PatternType::Volley(VolleyParams { speed: 80.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 135.0, "Bullet", 3.0, 0.0),
                    seq_single(PatternType::Volley(VolleyParams { speed: 80.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 180.0, "Bullet", 3.0, 0.0),
                    seq_single(PatternType::Volley(VolleyParams { speed: 80.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 225.0, "Bullet", 3.0, 0.0),
                    seq_single(PatternType::Volley(VolleyParams { speed: 80.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 270.0, "Bullet", 3.0, 0.0),
                    seq_single(PatternType::Volley(VolleyParams { speed: 80.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 315.0, "Bullet", 3.0, 0.0),
                ], true), 2.5),
            ]),
        ],
    });
}

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

pub fn make_phase(phase_index: u8, hp_threshold: f32, movement: MovementBehavior, attacks: Vec<AttackDef>) -> PhaseDef {
    PhaseDef { phase_index, hp_threshold, movement, attacks }
}

pub fn make_attack(attack: AttackSequence, fire_rate: f32) -> AttackDef {
    AttackDef { attack, fire_rate }
}

pub fn make_sequence(steps: Vec<SequenceStep>, loop_sequence: bool) -> AttackSequence {
    AttackSequence { steps, loop_sequence }
}

pub fn seq_single(pattern_type: PatternType, target: EnemyTarget, origin_offset_x: f32, origin_offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, wait: f32) -> SequenceStep {
    SequenceStep::Single(SequenceSingle { pattern_type, target, origin_offset_x, origin_offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, wait })
}

pub fn seq_repeat(pattern_type: PatternType, target: EnemyTarget, origin_offset_x: f32, origin_offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, interval: f32, count: u32, wait: f32) -> SequenceStep {
    SequenceStep::Repeat(SequenceRepeat { pattern_type, target, origin_offset_x, origin_offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, interval, count, wait })
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


