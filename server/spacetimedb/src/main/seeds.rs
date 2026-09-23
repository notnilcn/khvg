use spacetimedb::{ReducerContext, Table};
use crate::world::tables::*;
use crate::world::def_tables::*;
use crate::enemy::def_tables::*;
use crate::enemy::defs::*;
use crate::item::tables::*;

fn texture(texture_id: &str, res_path2d: &str, res_path3d: &str, kind: TextureKind) -> TextureEntry {
    TextureEntry { texture_id: texture_id.to_string(), res_path2d: res_path2d.to_string(), res_path3d: res_path3d.to_string(), kind }
}

pub fn seed_default_textures(ctx: &ReducerContext) {
    TextureEntry::seed(ctx, texture("Enemy", "res://Textures/Enemies/Knight.tres", "res://Assets/3D/skeletons/Skeleton_Warrior.fbx", TextureKind::Enemy));
    TextureEntry::seed(ctx, texture("Archer", "res://Textures/Enemies/Archer.tres", "res://Assets/3D/skeletons/Skeleton_Rogue.fbx", TextureKind::Enemy));
    TextureEntry::seed(ctx, texture("Knight", "res://Textures/Players/Knight.tres", "res://Assets/3D/Universal Base Characters[Standard]/Base Characters/Godot - UE/Superhero_Female_FullBody.gltf", TextureKind::Player));
    TextureEntry::seed(ctx, texture("Arrow", "res://Textures/Attacks/Arrow.tres", "res://Assets/3D/accesiories/arrow_bow.fbx", TextureKind::Projectile));
    TextureEntry::seed(ctx, texture("Helmet", "res://Textures/Items/Armor/Helmet.tres", "", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Hat", "res://Textures/Items/Accesories/Hat.tres", "", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Skull", "res://Textures/Items/Artifacts/Skull.tres", "", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Orb", "res://Textures/Items/Artifacts/Orb.tres", "", TextureKind::Item));
	TextureEntry::seed(ctx, texture("Tome", "res://Textures/Items/Artifacts/Tome.tres", "", TextureKind::Item));
	TextureEntry::seed(ctx, texture("Scripture", "res://Textures/Items/Artifacts/Scripture.tres", "", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Bag", "res://Textures/Items/Bags/Bag.tres", "res://Assets/3D/accesiories/quiver.fbx", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Bread", "res://Textures/Items/Consumables/Bread.tres", "res://Assets/3D/accesiories/mug_full.fbx", TextureKind::Item));
    TextureEntry::seed(ctx, texture("Bow", "res://Textures/Items/Weapons/Bow.tres", "res://Assets/3D/accesiories/bow_withString.fbx", TextureKind::Item));

    TextureEntry::seed(ctx, texture("Grass", "res://Textures/World/Ground/Grass.tres", "res://Assets/3D/KayKit_Medieval_Hexagon_Pack/tiles/base/hex_grass.fbx", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("Flowers", "res://Textures/World/Ground/Flowers.tres", "res://Assets/3D/KayKit_Medieval_Hexagon_Pack/tiles/base/hex_grass.fbx", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("HalfStones", "res://Textures/World/Ground/HalfStones.tres", "res://Assets/3D/KayKit_Medieval_Hexagon_Pack/tiles/roads/hex_road_A.fbx", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("FullStones", "res://Textures/World/Ground/FullStones.tres", "res://Assets/3D/KayKit_Medieval_Hexagon_Pack/tiles/roads/hex_road_B.fbx", TextureKind::Environment));

    TextureEntry::seed(ctx, texture("Tree", "res://Textures/World/Decor/Props/Tree.tres", "res://Assets/3D/KayKit_Medieval_Hexagon_Pack/decoration/nature/trees_A_medium.fbx", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("TreeShadow", "res://Textures/World/Decor/Shadows/Tree.tres", "", TextureKind::Environment));

    TextureEntry::seed(ctx, texture("Rock", "res://Textures/World/Decor/Props/Rock.tres", "res://Assets/3D/KayKit_Medieval_Hexagon_Pack/decoration/nature/rock_single_A.fbx", TextureKind::Environment));
    TextureEntry::seed(ctx, texture("RockShadow", "res://Textures/World/Decor/Shadows/Rock.tres", "", TextureKind::Environment));
}

fn animation(animation_id: &str, res_path: &str, clip_name: &str, kind: AnimationKind) -> AnimationEntry {
    AnimationEntry { animation_id: animation_id.to_string(), res_path: res_path.to_string(), clip_name: clip_name.to_string(), kind }
}

// Universal Animation Library 1+2 clips (Quaternius "Universal" rig — shared by the Universal
// Base Characters and the Modular Fantasy outfits). animation_id is "<lib>/<clip>" so identically
// named clips across libraries (A_TPose) never collide; the client merges clips under this id.
// clip_name is the IMPORTED clip name: Godot's glTF importer strips the "_Loop" suffix (and
// bakes loop_mode into the clip instead), so e.g. "Walk_Loop" in the glb is "Walk" here.
pub fn seed_default_animations(ctx: &ReducerContext) {
    const UAL1: &str = "res://Assets/3D/Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard.glb";
    const UAL2: &str = "res://Assets/3D/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb";
    use AnimationKind::*;
    let ual1: [(&str, AnimationKind); 43] = [
        ("A_TPose", Emote), ("Crouch_Fwd", Locomotion), ("Crouch_Idle", Locomotion),
        ("Dance", Emote), ("Death01", Emote), ("Driving", Locomotion),
        ("Fixing_Kneeling", Interaction), ("Hit_Chest", Combat), ("Hit_Head", Combat),
        ("Idle", Emote), ("Idle_Talking", Emote), ("Idle_Torch", Emote),
        ("Interact", Interaction), ("Jog_Fwd", Locomotion), ("Jump_Land", Locomotion),
        ("Jump", Locomotion), ("Jump_Start", Locomotion), ("PickUp_Table", Interaction),
        ("Pistol_Aim_Down", Combat), ("Pistol_Aim_Neutral", Combat), ("Pistol_Aim_Up", Combat),
        ("Pistol_Idle", Combat), ("Pistol_Reload", Combat), ("Pistol_Shoot", Combat),
        ("Punch_Cross", Combat), ("Punch_Jab", Combat), ("Push", Locomotion),
        ("Roll", Locomotion), ("Sitting_Enter", Emote), ("Sitting_Exit", Emote),
        ("Sitting_Idle", Emote), ("Sitting_Talking", Emote),
        ("Spell_Simple_Enter", Combat), ("Spell_Simple_Exit", Combat),
        ("Spell_Simple_Idle", Combat), ("Spell_Simple_Shoot", Combat),
        ("Sprint", Locomotion), ("Swim_Fwd", Locomotion), ("Swim_Idle", Locomotion),
        ("Sword_Attack", Combat), ("Sword_Idle", Combat), ("Walk_Formal", Locomotion),
        ("Walk", Locomotion),
    ];
    let ual2: [(&str, AnimationKind); 43] = [
        ("A_TPose", Emote), ("Chest_Open", Interaction), ("ClimbUp_1m", Locomotion),
        ("Consume", Interaction), ("Farm_Harvest", Interaction), ("Farm_PlantSeed", Interaction),
        ("Farm_Watering", Interaction), ("Hit_Knockback", Combat), ("Idle_FoldArms", Emote),
        ("Idle_Lantern", Emote), ("Idle_No", Emote), ("Idle_Rail_Call", Interaction),
        ("Idle_Rail", Emote), ("Idle_Shield_Break", Emote), ("Idle_Shield", Emote),
        ("Idle_TalkingPhone", Emote), ("LayToIdle", Emote), ("Melee_Hook", Combat),
        ("Melee_Hook_Rec", Combat), ("NinjaJump_Idle", Locomotion),
        ("NinjaJump_Land", Locomotion), ("NinjaJump_Start", Locomotion),
        ("OverhandThrow", Combat), ("Shield_Dash", Locomotion), ("Shield_OneShot", Combat),
        ("Slide_Exit", Locomotion), ("Slide", Locomotion), ("Slide_Start", Locomotion),
        ("Sword_Block", Combat), ("Sword_Dash", Locomotion), ("Sword_Heavy_Combo", Combat),
        ("Sword_Regular_A", Combat), ("Sword_Regular_A_Rec", Combat),
        ("Sword_Regular_B", Combat), ("Sword_Regular_B_Rec", Combat),
        ("Sword_Regular_C", Combat), ("Sword_Regular_Combo", Combat),
        ("TreeChopping", Interaction), ("Walk_Carry", Locomotion), ("Yes", Emote),
        ("Zombie_Idle", Emote), ("Zombie_Scratch", Combat), ("Zombie_Walk_Fwd", Locomotion),
    ];
    for (clip, kind) in ual1 {
        AnimationEntry::seed(ctx, animation(&format!("ual1/{clip}"), UAL1, clip, kind));
    }
    for (clip, kind) in ual2 {
        AnimationEntry::seed(ctx, animation(&format!("ual2/{clip}"), UAL2, clip, kind));
    }
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
        defense: 0,
        move_sim_factor: 1.5,
        attack_sim_factor: 0.85,
        aggro_lock_seconds: 15.0,
        stun_behavior: StunBehavior::Pause,
        phases: vec![
            make_phase(ctx, 0, 0.0, steering(30.0, MoveGoal::SpawnPoint, MovePattern::Meander(MeanderParams { sway_radians: 0.8, leash_radius: 180.0 })).with_cycle(2.5, 1.5), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Ring(RingParams { speed: 70.0, count: 8, line: 1, line_speed_step: 0.0 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 0.0, 0.0, 0.9),
                ], 0.0),
            ]),
        ],
    });
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "Archer".to_string(),
        texture_id: "Archer".to_string(),
        display_name: "Archer".to_string(),
        max_hp: 100,
        defense: 2,
        move_sim_factor: 2.0,
        attack_sim_factor: 1.05,
        aggro_lock_seconds: 25.0,
        stun_behavior: StunBehavior::Pause,
        phases: vec![
            make_phase(ctx, 0, 0.0, steering(40.0, MoveGoal::AggroTarget, MovePattern::Flee(FleeParams { min_range: 180.0 })).with_cycle(2.0, 1.5), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Volley(VolleyParams { speed: 200.0, count: 3, line: 1, line_speed_step: 0.0, speed_variance: 20.0, angle_jitter: 0.05 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 0.0, 0.0, 0.0),
                ], 0.0),
                make_sequence(vec![
                    seq_single(ctx, PatternType::Shotgun(ShotgunParams { speed: 130.0, count: 5, line: 1, line_speed_step: 0.0, spread: 0.4, speed_variance: 20.0, lifetime_variance: 0.4 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 10, 0.0, 0.0, 0.0),
                ], 0.5),
            ]),
        ],
    });

    // Reference example, not content: one phase per steering pattern × pattern family, so
    // contributors (and manual testing) can see every primitive in a single template. For
    // emitter-style authoring with spin/burst/difficulty composition, see enemy/emitters.rs
    // and the boss in enemy/defs/gorgon_boss.rs.
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "TestBoss".to_string(),
        texture_id: "Enemy".to_string(),
        display_name: "Test Boss".to_string(),
        max_hp: 1000,
        defense: 8,
        move_sim_factor: 2.75,
        attack_sim_factor: 1.35,
        aggro_lock_seconds: 30.0,
        stun_behavior: StunBehavior::Pause,
        phases: vec![
            // Phase 0 (100% HP): meanders around its spawn point — Ring read cleanly against the wandering gait
            make_phase(ctx, 0, 0.0, steering(40.0, MoveGoal::SpawnPoint, MovePattern::Meander(MeanderParams { sway_radians: 0.8, leash_radius: 220.0 })).with_cycle(2.5, 1.5), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Ring(RingParams { speed: 130.0, count: 24, line: 1, line_speed_step: 0.0 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 2.5, 10, 0.0, 0.0, 0.0),
                ], 0.0),
            ]),
            // Phase 1 (<=80% HP): orbits the aggro target at range (per-enemy range/sign, no concentric stacking) while sweeping an aimed fan
            make_phase(ctx, 1, 0.8, steering(55.0, MoveGoal::AggroTarget, MovePattern::Orbit(OrbitParams { range: 200.0 })).with_cycle(2.5, 1.5), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Curtain(CurtainParams { speed: 120.0, count: 11, angle_span: 1.6, gap_index: 5 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 3.0, 10, 0.0, 0.0, 0.0),
                ], 0.0),
            ]),
            // Phase 2 (<=60% HP): zigzag approach — closes the gap on a sine while spraying jittered aimed shots
            make_phase(ctx, 2, 0.6, steering(55.0, MoveGoal::AggroTarget, MovePattern::Zigzag(ZigzagParams { amplitude_radians: 0.6, period_seconds: 1.2 })).with_cycle(3.0, 1.0), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Volley(VolleyParams { speed: 180.0, count: 5, line: 1, line_speed_step: 0.0, speed_variance: 25.0, angle_jitter: 0.07 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.2, 10, 0.0, 0.0, 0.0),
                ], 0.0),
            ]),
            // Phase 3 (<=40% HP): lunges — short 3.5x bursts at the target with the pause window reading as the telegraph
            make_phase(ctx, 3, 0.4, steering(45.0, MoveGoal::AggroTarget, MovePattern::Lunge(LungeParams { speed_mult: 3.5 })).with_cycle(0.6, 1.4), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Shotgun(ShotgunParams { speed: 120.0, count: 7, line: 1, line_speed_step: 0.0, spread: 0.8, speed_variance: 25.0, lifetime_variance: 0.5 }), EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.2, 10, 0.0, 0.0, 0.0),
                ], 0.0),
            ]),
            // Phase 4 (<=20% HP): flees — keeps its distance while erupting in a randomized 360° nova
            make_phase(ctx, 4, 0.2, steering(45.0, MoveGoal::AggroTarget, MovePattern::Flee(FleeParams { min_range: 200.0 })).with_cycle(2.0, 1.0), 0.0, vec![
                make_sequence(vec![
                    seq_single(ctx, PatternType::Explosion(ExplosionParams { speed: 100.0, count: 20, speed_variance: 35.0, lifetime_variance: 0.9 }), EnemyTarget::Idle, 0.0, 0.0, 0.0, "Arrow", 3.0, 10, 0.0, 0.0, 0.0),
                ], 0.0),
            ]),
        ],
    });
}

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

impl Seed for AnimationEntry {
    fn seed(ctx: &ReducerContext, data: AnimationEntry) {
        if ctx.db.animation_entry().animation_id().find(&data.animation_id).is_some() {
            ctx.db.animation_entry().animation_id().update(data);
        } else {
            ctx.db.animation_entry().insert(data);
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


