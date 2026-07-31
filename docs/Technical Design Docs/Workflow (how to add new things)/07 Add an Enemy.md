# 07 Add an Enemy

An enemy is an `EnemyTemplate` row: a list of HP-threshold **phases**, each with a movement behavior and attack sequences made of bullet-pattern **steps** ([[06 Enemy AI & Bullet Patterns|06]]). If your enemy reuses existing `PatternType`s and `MovementBehavior`s, this is **data-only** — but the authoring happens in Rust seed code, because there's no external content tool. This guide covers the seed path (ships with the game) and the runtime path (CLI, for experimenting).

## 1. Register the sprite

Follow [[01 Add a Texture or Sprite]] — a `SpriteFrames` `.tres` under `client/Textures/Enemies/` with **`Idle` and `Walk` animations** ([[Enemy.cs##sprite.Play(moving ? "Walk" : "Idle")|Enemy.cs]] plays them based on movement), plus a `TextureEntry` seed with `TextureKind::Enemy`.

## 2. Author the template (seed path)

Add a seed function in `main/seeds.rs` using the authoring helpers ([[main/seeds.rs##pub fn make_phase|make_phase]] · [[main/seeds.rs##pub fn seq_single|seq_single]] · [[main/seeds.rs##pub fn seq_repeat|seq_repeat]] · [[main/seeds.rs##pub fn seq_multi|seq_multi]] · [[main/seeds.rs##pub fn multi_shot|multi_shot]]). The helpers insert the def rows (`MovementDef`/`SingleStepDef`/`RepeatStepDef`/`MultiStepDef`) and hand back the id-referencing values the template assembles. The Archer ([[main/seeds.rs##template_id: "Archer"|Archer]]) is the minimal worked example; adapt it:

```rust
pub fn seed_my_enemy(ctx: &ReducerContext) {
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: "Shaman".to_string(),
        texture_id: "Shaman".to_string(),     // from step 1
        display_name: "Shaman".to_string(),
        max_hp: 150,
        move_sim_factor: 1.75,                // × CAMERA_VIEW_RADIUS — simulates within this range
        attack_sim_factor: 0.9,               // × CAMERA_VIEW_RADIUS — only fires within this (smaller) range
        aggro_lock_seconds: 20.0,             // won't re-target for this long after acquiring aggro
        phases: vec![
            make_phase(ctx, 0, 0.0,           // phase 0, from 100% HP down to next threshold
                MovementBehavior::Wander(WanderParams { speed: 25.0, active_duration: 3.0, pause_duration: 2.0 }),
                0.0,                          // phase_loop_delay — pause between attack-sequence loops
                vec![
                    make_sequence(vec![       // steps run in order, then the sequence loops
                        seq_single(ctx, PatternType::Ring(RingParams { speed: 60.0, count: 6 }),
                            EnemyTarget::Idle,          // Idle = unaimed; AggroTarget = aimed at locked player
                            0.0, 0.0,                   // offset_x/y from the enemy's position
                            0.0,                        // base_angle_offset (degrees)
                            "Arrow",                    // bullet texture_id
                            2.0,                        // bullet lifetime (seconds)
                            12,                         // damage per bullet
                            1.2),                       // delay before the next step
                    ], 0.0),                  // start_delay before this sequence begins
                ]),
            make_phase(ctx, 1, 0.5,           // phase 1, at ≤50% HP — switches movement AND attacks
                MovementBehavior::Chase(ChaseParams { speed: 45.0, active_duration: 2.0, pause_duration: 1.0 }),
                0.0,
                vec![
                    make_sequence(vec![
                        seq_repeat(ctx, PatternType::Volley(VolleyParams { speed: 160.0, count: 3, speed_variance: 15.0, angle_jitter: 0.05 }),
                            EnemyTarget::AggroTarget, 0.0, 0.0, 0.0, "Arrow", 2.0, 15,
                            0.4,                        // repeat_interval — seconds between repeats
                            3,                          // repeat_target — fires this many times total
                            1.0),                       // then next_step_delay
                    ], 0.0),
                ]),
        ],
    });
}
```

Reference points while authoring:

- **Phase thresholds**: `hp_threshold` is the HP *fraction* at or below which the phase activates (`0.5` = second half). Phase 0 conventionally has `0.0`. See TestBoss ([[main/seeds.rs##template_id: "TestBoss"|TestBoss]]) for a five-phase example.
- **Step kinds**: `seq_single` (fire once), `seq_repeat` (fire N times at an interval — its damage becomes *per-instance* data, [[06 Enemy AI & Bullet Patterns|06]]), `seq_multi` (several `multi_shot`s fired simultaneously — TestBossP2/P3 nest these for fans).
- **Multiple attack sequences** in one phase (Archer) alternate, each with its own `start_delay`.
- **Sim factors** are multiples of `CAMERA_VIEW_RADIUS`, not world units — `move_sim_factor` should exceed `attack_sim_factor` so nothing shoots from off-screen ([[06 Enemy AI & Bullet Patterns|06]]).

## 3. Register the seed in `init`

Seed functions are called explicitly — add yours to the list in `lifecycle.rs` ([[lifecycle.rs##pub fn init|init]]):

```rust
seed_my_enemy(ctx);
```

(Plus the `use super::seeds::{...}` import at the top of the file.)

## 4. Make it spawn

A template that no `BiomeRegionDef` references is admin-spawn-only (like `TestBoss`). To put it in the world, add its id to region pools in `seed_region_def` ([[main/seeds.rs##fn seed_region_def|seed_region_def]]):

```rust
enemy_template_ids: vec!["Enemy".to_string(), "Archer".to_string(), "Shaman".to_string()],
```

Today every region shares this one helper — for per-region pools, give the regions their own defs (see [[11 Add a Biome or Region|11]]). `tick_enemy_spawn` (2s) then maintains up to `max_enemies` per region while a player is nearby.

## 5. Build and test

```bash
cd server && bash build.sh
# connect a client, then:
spacetime call --server local bullethell claim_admin
spacetime call --server local bullethell spawn_enemy Shaman 100 100 false false
spacetime sql --server local bullethell "SELECT enemy_id, template_id, hp FROM enemy"
spacetime call --server local bullethell despawn_enemy <enemy_id>
```

`spawn_enemy`/`despawn_enemy` route through the same archetype helpers as natural spawns ([[09 Admin, Debug & World Lifecycle|09]]), so what you test is what players get. The third argument pair `is_elite`/`immortal`: `immortal: true` makes a target dummy (`report_enemy_hit` no-ops); `is_elite` is plumbed end-to-end but **does nothing** yet.

## Runtime path (no republish)

For quick iteration you can author entirely through the CLI using the upsert reducers — pass `def_id: 0` to insert, then read the assigned id back with `spacetime sql`:

```bash
spacetime call --server local bullethell upsert_movement_def '{"def_id":0,"movement":{"wander":{"speed":25.0,"active_duration":3.0,"pause_duration":2.0}}}'
spacetime sql --server local bullethell "SELECT def_id FROM movement_def"     # -> say 42
spacetime call --server local bullethell upsert_single_step_def '{"def_id":0,"pattern_type":{"ring":{"speed":60.0,"count":6}},"target":{"idle":{}},"offset_x":0.0,"offset_y":0.0,"base_angle_offset":0.0,"texture_id":"Arrow","lifetime":2.0,"damage":12,"next_step_delay":1.2}'
spacetime sql --server local bullethell "SELECT def_id FROM single_step_def"  # -> say 43
spacetime call --server local bullethell upsert_enemy_template '{"template_id":"Shaman","texture_id":"Shaman","display_name":"Shaman","max_hp":150,"move_sim_factor":1.75,"attack_sim_factor":0.9,"aggro_lock_seconds":20.0,"phases":[{"phase_index":0,"hp_threshold":0.0,"movement_def_id":42,"phase_loop_delay":0.0,"attacks":[{"steps":[{"single":43}],"start_delay":0.0}]}]}'
spacetime call --server local bullethell spawn_enemy Shaman 100 100 false false
```

Upserts take effect live for every connected client. Remember they vanish on the next `build.sh` — bake winners into seeds.

## Gotchas

- **Template edits don't migrate live enemies.** Spawning copies the template into per-instance rows; a republish wipes them, but a runtime `upsert_enemy_template` only affects *future* spawns (with the deliberate exception that step damage is re-resolved live — [[06 Enemy AI & Bullet Patterns|06]]).
- **Angles:** `base_angle_offset` in step defs is in **degrees** (the client converts), while `PatternType` params like `spread`/`angle_span` are in **radians**. The seeds are your reference (TestBossP2 uses `100.0_f32.to_radians()` for a wide spread).
- **Bullet texture orientation** gets the global arrow-art offset — [[01 Add a Texture or Sprite|01]].
- Enemy bullets are client-rendered from fire-once `BulletPatternEvent` rows; if the enemy is outside every player's `attack_sim_factor` range it never fires, and outside `move_sim_factor` it freezes entirely. Two enemies from one template progress independently.
- Don't try to give an enemy more phases than u8 or thresholds out of order — `compute_phase` picks by fraction; keep thresholds descending in effect (phase 0 lowest... see TestBoss ordering: 0.0, 0.8, 0.6, 0.4, 0.2 — phase *index* ascending while thresholds descend).
