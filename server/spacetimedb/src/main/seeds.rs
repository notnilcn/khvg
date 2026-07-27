use spacetimedb::{ReducerContext, Table};
use crate::world::tables::*;
use crate::world::def_tables::*;
use crate::enemy::def_tables::*;
use crate::item::tables::*;

fn texture(texture_id: &str, res_path: &str, kind: TextureKind) -> TextureEntry {
    TextureEntry { texture_id: texture_id.to_string(), res_path: res_path.to_string(), kind }
}

pub fn seed_default_textures(ctx: &ReducerContext) {
    TextureEntry::seed(ctx, texture("Enemy", "res://Textures/Enemies/Knight.tres", TextureKind::Enemy));
    TextureEntry::seed(ctx, texture("Archer", "res://Textures/Enemies/Archer.tres", TextureKind::Enemy));
    TextureEntry::seed(ctx, texture("Knight", "res://Textures/Players/Knight.tres", TextureKind::Player));
    TextureEntry::seed(ctx, texture("Arrow", "res://Textures/Attacks/Arrow.tres", TextureKind::Projectile));
    TextureEntry::seed(ctx, texture("Helmet", "res://Textures/Items/Armor/Helmet.tres", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Hat", "res://Textures/Items/Accesories/Hat.tres", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Skull", "res://Textures/Items/Artifacts/Skull.tres", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Bag", "res://Textures/Items/Bags/Bag.tres", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Bread", "res://Textures/Items/Consumables/Bread.tres", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Bow", "res://Textures/Items/Weapons/Bow.tres", TextureKind::Item));

    TextureEntry::seed(ctx, texture("Grass", "res://Textures/World/Ground/Grass.tres", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("Flowers", "res://Textures/World/Ground/Flowers.tres", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("HalfStones", "res://Textures/World/Ground/HalfStones.tres", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("FullStones", "res://Textures/World/Ground/FullStones.tres", TextureKind::Environment));

    TextureEntry::seed(ctx, texture("Tree", "res://Textures/World/Decor/Props/Tree.tres", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("TreeShadow", "res://Textures/World/Decor/Shadows/Tree.tres", TextureKind::Environment));

    TextureEntry::seed(ctx, texture("Rock", "res://Textures/World/Decor/Props/Rock.tres", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("RockShadow", "res://Textures/World/Decor/Shadows/Rock.tres", TextureKind::Environment));
}   

// Allow-list of overlay/base tag pairs — absence of a rule means that overlay never layers on
// that base. Clear-and-reinsert (no natural pair key to upsert against) so this stays correct
// across republishes.
pub fn seed_layering_rules(ctx: &ReducerContext) {
    for rule in ctx.db.layering_rule().iter() {
        ctx.db.layering_rule().rule_id().delete(&rule.rule_id);
    }
    let rules = [
        ("light_debris", "soft"),     // HalfStones on Grass
        ("light_debris", "delicate"), // HalfStones on Flowers
        ("heavy_debris", "soft"),     // FullStones on Grass (not Flowers — no rule for that pair)
    ];
    for (overlay_tag, allowed_base_tag) in rules {
        ctx.db.layering_rule().insert(LayeringRule { rule_id: 0, overlay_tag: overlay_tag.to_string(), allowed_base_tag: allowed_base_tag.to_string() });
    }
}

// Deny-lists for tiles that may never sit laterally next to each other — see BaseAdjacencyRule/
// OverlayAdjacencyRule. Empty for now: the current ground texture set (Grass/Flowers/FullStones/
// HalfStones) has no clashing pairs yet; there are no overlays in use. Clear-and-reinsert like
// seed_layering_rules, so this stays correct across republishes once pairs are added.
pub fn seed_adjacency_rules(ctx: &ReducerContext) {
    for rule in ctx.db.base_adjacency_rule().iter() {
        ctx.db.base_adjacency_rule().rule_id().delete(&rule.rule_id);
    }
    for rule in ctx.db.overlay_adjacency_rule().iter() {
        ctx.db.overlay_adjacency_rule().rule_id().delete(&rule.rule_id);
    }
}

// Deny-list for which decor may sit in a hex carrying a given ground/overlay tag — see
// DecorGroundRule. Trees and rocks are grass/flowers-only — neither spawns on a HalfStones wedge.
pub fn seed_decor_ground_rules(ctx: &ReducerContext) {
    for rule in ctx.db.decor_ground_rule().iter() {
        ctx.db.decor_ground_rule().rule_id().delete(&rule.rule_id);
    }
    let rules = [
        ("tall", "light_stone"),  // Tree denied on HalfStones
        ("solid", "light_stone"), // Rock denied on HalfStones
    ];
    for (decor_tag, denied_base_tag) in rules {
        ctx.db.decor_ground_rule().insert(DecorGroundRule { rule_id: 0, decor_tag: decor_tag.to_string(), denied_base_tag: denied_base_tag.to_string() });
    }
}

pub fn seed_default_enemies(ctx: &ReducerContext) {
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "Enemy".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Enemy".to_string(),
        max_hp: 100,
        move_sim_factor: 1.5,
        attack_sim_factor: 0.85,
        aggro_lock_seconds: 15.0,
        phases: vec![
            make_phase(ctx, 0, 0.0, MovementBehavior::Wander(WanderParams { speed: 30.0, active_duration: 2.5, pause_duration: 1.5 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Ring(RingParams { speed: 70.0, count: 8 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 0.9),
                ], 0.0),
            ]),
        ],
    });
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "Archer".to_string(),
        texture_id: "Archer".to_string(),
        display_name: "Archer".to_string(),
        max_hp: 100,
        move_sim_factor: 2.0,
        attack_sim_factor: 1.05,
        aggro_lock_seconds: 25.0,
        phases: vec![
            make_phase(ctx, 0, 0.0, MovementBehavior::Flee(FleeParams { speed: 40.0, min_range: 180.0, active_duration: 2.0, pause_duration: 1.5 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Volley(VolleyParams { speed: 200.0, count: 3, speed_variance: 20.0, angle_jitter: 0.05 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 0.0),
                ], 0.0),
                make_sequence(vec![
                    seq_single(ctx, PatternType::Shotgun(ShotgunParams { speed: 130.0, count: 5, spread: 0.4, speed_variance: 20.0, lifetime_variance: 0.4 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 0.0),
                ], 0.5),
            ]),
        ],
    });

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "TestBoss".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Test Boss".to_string(),
        max_hp: 1000,
        move_sim_factor: 2.75,
        attack_sim_factor: 1.35,
        aggro_lock_seconds: 30.0,
        phases: vec![
            // Phase 0 (100% HP): turret — Ring read cleanly with no movement noise
            make_phase(ctx, 0, 0.0, MovementBehavior::Stationary, 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Ring(RingParams { speed: 130.0, count: 24 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 2.5, 10, 0.0),
                ], 0.0),
            ]),
            // Phase 1 (<=80% HP): patrols while sweeping an aimed fan — repositioning shifts where the gap lands
            make_phase(ctx, 1, 0.8, MovementBehavior::Wander(WanderParams { speed: 40.0, active_duration: 2.5, pause_duration: 1.5 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Curtain(CurtainParams { speed: 120.0, count: 11, angle_span: 1.6, gap_index: 5 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 3.0, 10, 0.0),
                ], 0.0),
            ]),
            // Phase 2 (<=60% HP): rusher — closes the gap while spraying jittered aimed shots
            make_phase(ctx, 2, 0.6, MovementBehavior::Chase(ChaseParams { speed: 55.0, active_duration: 3.0, pause_duration: 1.0 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Volley(VolleyParams { speed: 180.0, count: 5, speed_variance: 25.0, angle_jitter: 0.07 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.2, 10, 0.0),
                ], 0.0),
            ]),
            // Phase 3 (<=40% HP): kiter — keeps its distance while peppering with randomized pellets
            make_phase(ctx, 3, 0.4, MovementBehavior::Flee(FleeParams { speed: 45.0, min_range: 200.0, active_duration: 2.0, pause_duration: 1.0 }), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Shotgun(ShotgunParams { speed: 120.0, count: 7, spread: 0.8, speed_variance: 25.0, lifetime_variance: 0.5 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.2, 10, 0.0),
                ], 0.0),
            ]),
            // Phase 4 (<=20% HP): plants itself and erupts in a randomized 360° nova — desperation finale
            make_phase(ctx, 4, 0.2, MovementBehavior::Stationary, 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Explosion(ExplosionParams { speed: 100.0, count: 20, speed_variance: 35.0, lifetime_variance: 0.9 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 3.0, 10, 0.0),
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
        move_sim_factor: 2.75,
        attack_sim_factor: 1.35,
        aggro_lock_seconds: 30.0,
        phases: vec![
            make_phase(ctx, 0, 0.0, MovementBehavior::Stationary, 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Ring(RingParams { speed: 50.0, count: 6 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 0.5),
                    seq_multi(ctx, vec![
                        multi_shot(PatternType::Shotgun(ShotgunParams { speed: 50.0, count: 25, spread: 100.0_f32.to_radians(), speed_variance: 0.0, lifetime_variance: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 10),
                        multi_shot(PatternType::Shotgun(ShotgunParams { speed: 50.0, count: 14, spread: 12.5_f32.to_radians(), speed_variance: 0.0, lifetime_variance: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 10),
                    ], 0.5),
                    seq_repeat(ctx, PatternType::Ring(RingParams { speed: 50.0, count: 6 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 0.5, 2, 0.5),
                ], 0.0),
            ]),
        ],
    });
}

pub fn seed_test_boss_p3(ctx: &ReducerContext) {
    let bullet = |angle: f32| multi_shot(PatternType::Volley(VolleyParams { speed: 65.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, angle, "Arrow", 2.0, 10);
    let bullet_many = |offset: f32, wait: f32| seq_multi(ctx, vec![bullet(offset), bullet(offset + 60.0), bullet(offset + 120.0), bullet(offset + 180.0), bullet(offset + 240.0), bullet(offset + 300.0)], wait);
    let arrow = |angle: f32| multi_shot(PatternType::Volley(VolleyParams { speed: 50.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::Idle, 0.0, 0.0, angle, "Arrow", 2.0, 10);
    let arrow_many = |offset: f32, wait: f32| seq_multi(ctx, vec![arrow(offset), arrow(offset + 60.0), arrow(offset + 120.0), arrow(offset + 180.0), arrow(offset + 240.0), arrow(offset + 300.0)], wait);

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "TestBossP3".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Test Boss P3".to_string(),
        max_hp: 100,
        move_sim_factor: 2.75,
        attack_sim_factor: 1.35,
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
                    seq_single(ctx, PatternType::Volley(VolleyParams { speed: 100.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 1.0),
                ], 0.0),
            ]),
        ],
    });
}

fn seed_test_boss_p3_clone(ctx: &ReducerContext, template_id: &str, display_name: &str) {
    let bullet = |angle: f32| multi_shot(PatternType::Volley(VolleyParams { speed: 50.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::AggroTarget, 0.0, 0.0, angle, "Arrow", 2.0, 10);
    let bullet_many = |offset: f32| seq_multi(ctx, vec![bullet(offset), bullet(offset + 60.0), bullet(offset + 120.0), bullet(offset + 180.0), bullet(offset + 240.0), bullet(offset + 300.0)], 0.1);
    let arrow = |angle: f32| multi_shot(PatternType::Volley(VolleyParams { speed: 50.0, count: 1, speed_variance: 0.0, angle_jitter: 0.0 }), EnemyTarget::Idle, 0.0, 0.0, angle, "Arrow", 2.0, 10);
    let arrow_many = |offset: f32| seq_multi(ctx, vec![arrow(offset), arrow(offset + 60.0), arrow(offset + 120.0), arrow(offset + 180.0), arrow(offset + 240.0), arrow(offset + 300.0)], 0.1);

    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: template_id.to_string(),
        texture_id: "Enemy".to_string(),
        display_name: display_name.to_string(),
        max_hp: 100,
        move_sim_factor: 2.75,
        attack_sim_factor: 1.35,
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

// Mixed melee/ranged spawn pool, uniform across every region for now — no info yet suggesting
// density or radius should vary per region.
fn seed_region_def(ctx: &ReducerContext, region_def_id: &str, display_name: &str) {
    BiomeRegionDef::seed(ctx, BiomeRegionDef {
        region_def_id: region_def_id.to_string(),
        display_name: display_name.to_string(),
        enemy_template_ids: vec!["Enemy".to_string(), "Archer".to_string()],
        max_enemies: 5,
        spawn_radius: 300.0,
    });
}

// Demo content for now: just the 3 floor textures (Grass/Flowers/HalfStones — FullStones is
// parked, see below) at different frequencies per biome, no decor (mechanism stays built and
// working — DecorConfig/HexDecor/DecorGroundRule, TerrainManager rendering — just nothing
// seeded into the world right now: 2D top-down decor collision/walkability isn't decided yet,
// and the current decor art doesn't read well enough to be worth it yet either).
pub fn seed_world_defs(ctx: &ReducerContext) {
    seed_region_def(ctx, "GrasslandRegion1", "Grassland I");
    seed_region_def(ctx, "GrasslandRegion2", "Grassland II");
    seed_region_def(ctx, "GrasslandRegion3", "Grassland III");
    seed_region_def(ctx, "MeadowRegion1", "Meadow I");
    seed_region_def(ctx, "MeadowRegion2", "Meadow II");
    seed_region_def(ctx, "HighlandsRegion1", "Highlands I");
    seed_region_def(ctx, "HighlandsRegion2", "Highlands II");
    seed_region_def(ctx, "HighlandsRegion3", "Highlands III");
    seed_region_def(ctx, "HighlandsRegion4", "Highlands IV");

    // Grass-dominant, a bit of flowers, rare stone.
    BiomeDef::seed(ctx, BiomeDef {
        biome_id: "Grassland".to_string(),
        display_name: "Grassland".to_string(),
        ground_textures: vec![
            GroundTextureConfig { texture_id: "Grass".to_string(), chance: 75.0, tags: vec!["soft".to_string()], allowed_rotations: vec![] },
            GroundTextureConfig { texture_id: "Flowers".to_string(), chance: 20.0, tags: vec!["delicate".to_string()], allowed_rotations: vec![] },
            GroundTextureConfig { texture_id: "HalfStones".to_string(), chance: 5.0, tags: vec!["light_stone".to_string()], allowed_rotations: vec![] },
        ],
        overlay_textures: vec![],
        region_weights: vec![
            RegionWeight { region_def_id: "GrasslandRegion1".to_string(), weight: 34.0 },
            RegionWeight { region_def_id: "GrasslandRegion2".to_string(), weight: 33.0 },
            RegionWeight { region_def_id: "GrasslandRegion3".to_string(), weight: 33.0 },
        ],
        ground_noise_scale: 0.006,
        overlay_noise_scale: 0.004,
        decor_configs: vec![],
        decor_noise_scale: 0.004,
        decor_min_separation: 1,
    });
    // Flower-dominant, grass fills the gaps, rare stone.
    BiomeDef::seed(ctx, BiomeDef {
        biome_id: "Meadow".to_string(),
        display_name: "Meadow".to_string(),
        ground_textures: vec![
            GroundTextureConfig { texture_id: "Flowers".to_string(), chance: 55.0, tags: vec!["delicate".to_string()], allowed_rotations: vec![] },
            GroundTextureConfig { texture_id: "Grass".to_string(), chance: 40.0, tags: vec!["soft".to_string()], allowed_rotations: vec![] },
            GroundTextureConfig { texture_id: "HalfStones".to_string(), chance: 5.0, tags: vec!["light_stone".to_string()], allowed_rotations: vec![] },
        ],
        overlay_textures: vec![],
        region_weights: vec![
            RegionWeight { region_def_id: "MeadowRegion1".to_string(), weight: 50.0 },
            RegionWeight { region_def_id: "MeadowRegion2".to_string(), weight: 50.0 },
        ],
        ground_noise_scale: 0.006,
        overlay_noise_scale: 0.004,
        decor_configs: vec![],
        decor_noise_scale: 0.004,
        decor_min_separation: 1,
    });
    // Still grass-dominant, but noticeably more stone presence than the other two biomes.
    BiomeDef::seed(ctx, BiomeDef {
        biome_id: "Highlands".to_string(),
        display_name: "Highlands".to_string(),
        ground_textures: vec![
            GroundTextureConfig { texture_id: "Grass".to_string(), chance: 55.0, tags: vec!["soft".to_string()], allowed_rotations: vec![] },
            GroundTextureConfig { texture_id: "Flowers".to_string(), chance: 25.0, tags: vec!["delicate".to_string()], allowed_rotations: vec![] },
            GroundTextureConfig { texture_id: "HalfStones".to_string(), chance: 20.0, tags: vec!["light_stone".to_string()], allowed_rotations: vec![] },
        ],
        overlay_textures: vec![],
        region_weights: vec![
            RegionWeight { region_def_id: "HighlandsRegion1".to_string(), weight: 25.0 },
            RegionWeight { region_def_id: "HighlandsRegion2".to_string(), weight: 25.0 },
            RegionWeight { region_def_id: "HighlandsRegion3".to_string(), weight: 25.0 },
            RegionWeight { region_def_id: "HighlandsRegion4".to_string(), weight: 25.0 },
        ],
        ground_noise_scale: 0.006,
        overlay_noise_scale: 0.004,
        decor_configs: vec![],
        decor_noise_scale: 0.004,
        decor_min_separation: 1,
    });
    WorldDef::seed(ctx, WorldDef {
        world_id: "Earth".to_string(),
        display_name: "Earth".to_string(),
        biome_weights: vec![
            BiomeWeight { biome_id: "Grassland".to_string(), weight: 40.0 },
            BiomeWeight { biome_id: "Meadow".to_string(), weight: 30.0 },
            BiomeWeight { biome_id: "Highlands".to_string(), weight: 30.0 },
        ],
    });
}

pub fn make_phase(ctx: &ReducerContext, phase_index: u8, hp_threshold: f32, movement: MovementBehavior, phase_loop_delay: f32, attacks: Vec<AttackSequence>) -> PhaseDef {
    let def = ctx.db.movement_def().insert(MovementDef { def_id: 0, movement });
    PhaseDef { phase_index, hp_threshold, movement_def_id: def.def_id, phase_loop_delay, attacks }
}

pub fn make_sequence(steps: Vec<SequenceStepDef>, start_delay: f32) -> AttackSequence {
    AttackSequence { steps, start_delay }
}

pub fn seq_single(ctx: &ReducerContext, pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, damage: u32, next_step_delay: f32) -> SequenceStepDef {
    let def = ctx.db.single_step_def().insert(SingleStepDef { def_id: 0, pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, damage, next_step_delay });
    SequenceStepDef::Single(def.def_id)
}

pub fn seq_repeat(ctx: &ReducerContext, pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, damage: u32, repeat_interval: f32, repeat_target: u32, next_step_delay: f32) -> SequenceStepDef {
    let def = ctx.db.repeat_step_def().insert(RepeatStepDef { def_id: 0, pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, damage, repeat_interval, repeat_target, next_step_delay });
    SequenceStepDef::Repeat(def.def_id)
}

pub fn multi_shot(pattern_type: PatternType, target: EnemyTarget, offset_x: f32, offset_y: f32, base_angle_offset: f32, texture_id: &str, lifetime: f32, damage: u32) -> MultiShot {
    MultiShot { pattern_type, target, offset_x, offset_y, base_angle_offset, texture_id: texture_id.to_string(), lifetime, damage }
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

impl Seed for EnemyTemplate {
    fn seed(ctx: &ReducerContext, data: EnemyTemplate) {
        if ctx.db.enemy_template().template_id().find(&data.template_id).is_some() {
            ctx.db.enemy_template().template_id().update(data);
        } else {
            ctx.db.enemy_template().insert(data);
        }
    }
}

impl Seed for WorldDef {
    fn seed(ctx: &ReducerContext, data: WorldDef) {
        if ctx.db.world_def().world_id().find(&data.world_id).is_some() {
            ctx.db.world_def().world_id().update(data);
        } else {
            ctx.db.world_def().insert(data);
        }
    }
}

impl Seed for BiomeDef {
    fn seed(ctx: &ReducerContext, data: BiomeDef) {
        if ctx.db.biome_def().biome_id().find(&data.biome_id).is_some() {
            ctx.db.biome_def().biome_id().update(data);
        } else {
            ctx.db.biome_def().insert(data);
        }
    }
}

impl Seed for BiomeRegionDef {
    fn seed(ctx: &ReducerContext, data: BiomeRegionDef) {
        if ctx.db.biome_region_def().region_def_id().find(&data.region_def_id).is_some() {
            ctx.db.biome_region_def().region_def_id().update(data);
        } else {
            ctx.db.biome_region_def().insert(data);
        }
    }
}

impl Seed for Enchantment {
    fn seed(ctx: &ReducerContext, data: Enchantment) {
        if ctx.db.enchantment().enchantment_id().find(&data.enchantment_id).is_some() {
            ctx.db.enchantment().enchantment_id().update(data);
        } else {
            ctx.db.enchantment().insert(data);
        }
    }
}


