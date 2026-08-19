# 08 Enemies & AI

## Assumed knowledge

- [[02 The Component Framework]] — the entity/component model on both sides (one table per concern on the server, `*Component` child nodes on the client); the enemy is its cleanest example.
- [[03 Boot & Connection]] — what `init` seeds and why every publish rebuilds the database from scratch.
- [[05 Joining the World]] — subscription waves and `EntitySpawnerComponent`, the thing that instantiates enemy puppets.
- [[06 Movement & Position Sync]] — AOI (`PlayerChunk`, `nearby_indices`), the torus, and `InterpolationComponent`, which enemy puppets share with remote players.
- [[07 Terrain & World Streaming]] — hex/chunk math and where `BiomeRegion` rows come from.

## The 30-second version

Enemies are **data, not code**. A static `EnemyTemplate` row (seeded at publish) describes an enemy declaratively: hp, defense, two simulation-range factors, an aggro lock, and a list of phases — each phase an hp threshold, a movement behavior, and attack sequences built from parameterized bullet-pattern steps. Two scheduled reducers drive everything: a 2-second spawn tick walks `BiomeRegion` rows and tops up regions that have players near them, and a 100 ms behavior tick moves each enemy, advances its current phase's attack sequences, and appends one `BulletPatternEvent` row per fired step. Spawning an enemy inserts a small tree of rows (behavior root → phases → attacks → sequence steps) — the server-side mirror of the client's component tree — and deleting an enemy deletes the whole tree, so no orphan rows ever outlive it. The client does no AI at all: `EntitySpawnerComponent` instantiates a `default_enemy.tscn` puppet per `NearbyEnemies` row, the puppet lerps toward row positions, and every `BulletPatternEvent` row addressed to its enemy id is rendered locally as BlastBullets2D bullets with deterministic, event-seeded jitter so all clients draw identical trajectories.

## Flowcharts

- [[flowcharts/main-enemies.canvas]] — this doc's composed flow (server `enemy/` module, biome spawn regions, the client `Enemy` puppet, and the bullet-spawn path).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/enemy_subfolder/methods_codefile/methods_codefile.canvas]] — deep dive into `enemy/methods.rs`: the spawn walk, the archetype helpers, the phase/sequence ticking state machine, and movement.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/enemy_subfolder/instance_tables_codefile/instance_tables_codefile.canvas]] — the runtime behavior-tree rows (`Enemy`/`EnemyBehavior`/`EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance`) and the two event tables.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Bullets_subfolder/BulletSpawnerComponent_codefile/BulletSpawnerComponent_codefile.canvas]] — the client-side `PatternType` dispatch, per-pattern spawn geometry, and the deterministic jitter.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^enemy-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^enemy-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^enemy-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^enemy-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^enemy-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^enemy-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^enemy-7{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^enemy-8{seamless:true,title:false,marker:08.}]]
```

## Main body

### Templates: enemies are content, not code

```sync
![[00 End-to-End Timeline Flowchart#^enemy-1{seamless:true,title:false}]]
```

The whole enemy *design space* lives in `server/spacetimedb/src/enemy/def_tables.rs`, and it is deliberately split into two layers. The top layer is [[server/spacetimedb/src/enemy/def_tables.rs#EnemyTemplate#1|EnemyTemplate]], one row per enemy type, keyed by a string `template_id`. Its scalar fields are the combat/simulation contract: `max_hp`, a **flat** `defense` (per-bullet subtraction, minimum damage 1 — the comment on the field spells out why: flat defense is what makes many-weak-bullets patterns strictly worse against armored enemies than few-strong-bullets ones), and `aggro_lock_seconds`. The two `*_sim_factor` fields are **multiples of `CAMERA_VIEW_RADIUS`, not world units** — again the field comment says why: templates stay correctly scaled if the client's camera or viewport ever changes, because ranges are defined relative to what the player can see rather than to absolute distances.

The nested layer is `phases: Vec<PhaseDef>`, embedded right on the template row (the same `Vec<enum>`-on-one-row shape the item catalog uses for behaviors). Each [[server/spacetimedb/src/enemy/def_tables.rs#PhaseDef#1|PhaseDef]] is a self-contained stage of the fight: an `hp_threshold` (the phase activates once `hp/max_hp <= threshold`), a `movement_def_id` pointing at a shared [[server/spacetimedb/src/enemy/def_tables.rs#MovementDef#1|MovementDef]] row, a `phase_loop_delay` (a breather between attack cycles — boss choreography), and `attacks: Vec<AttackSequence>`. An [[server/spacetimedb/src/enemy/def_tables.rs#AttackSequence#1|AttackSequence]] is an ordered `steps` list plus a `start_delay`; each step is a [[server/spacetimedb/src/enemy/def_tables.rs#SequenceStepDef#1|SequenceStepDef]] — an enum that holds only a `def_id` referencing one of the three shared step tables: [[server/spacetimedb/src/enemy/def_tables.rs#SingleStepDef#1|SingleStepDef]] (fire once), [[server/spacetimedb/src/enemy/def_tables.rs#RepeatStepDef#1|RepeatStepDef]] (re-fire on an interval up to a count), [[server/spacetimedb/src/enemy/def_tables.rs#MultiStepDef#1|MultiStepDef]] (fire several shots simultaneously). Steps are shared rows referenced by id so two templates can reuse the same step definition, while phases/attacks stay embedded because they are never shared.

The actual bullet geometry is the [[server/spacetimedb/src/enemy/def_tables.rs#PatternType#1|PatternType]] enum carried by every step: `Ring` (full circle), `Volley` (aimed, with speed/angle jitter), `Curtain` (an aimed fan with a designated `gap_index` to dodge through), `Shotgun` (aimed spread with speed/lifetime variance), `Explosion` (full circle with variance — the "nova"). Each variant carries its own parameter struct, so a pattern is a small closed set of numbers the client can reproduce exactly. Movement is the parallel [[server/spacetimedb/src/enemy/def_tables.rs#MovementBehavior#1|MovementBehavior]] enum: `Stationary`, `Wander` (walk/pause cycle), `Chase` (toward the aggro target), `Flee` (away from it while inside `min_range`). Finally [[server/spacetimedb/src/enemy/def_tables.rs#EnemyTarget#1|EnemyTarget]] decides what a step aims at: `Idle` (nobody — radial patterns ignore it) or `AggroTarget` (the identity the behavior row is currently locked onto).

**Authoring.** Because templates are rows, content is *seeded and upserted*, never compiled in. [[server/spacetimedb/src/main/seeds.rs#seed_default_enemies#1|seed_default_enemies]] runs at publish (see [[03 Boot & Connection]]) and inserts three templates — a wandering "Enemy" with a slow ring, a fleeing "Archer" with aimed volley + shotgun sequences, and a five-phase "TestBoss" (turret ring → wandering curtain → chasing volley → kiting shotgun → stationary explosion) that exists to exercise every pattern and every movement behavior across phase transitions. [[server/spacetimedb/src/main/seeds.rs#seed_test_boss_p2#1|seed_test_boss_p2]] and its p3–p6 siblings add pattern-gauntlet bosses (P3's rotating aimed/idle hexagon spirals are the densest authored sequences). The seed-building helpers [[server/spacetimedb/src/main/seeds.rs#make_phase#1|make_phase]], `make_sequence`, [[server/spacetimedb/src/main/seeds.rs#seq_single#1|seq_single]], [[server/spacetimedb/src/main/seeds.rs#seq_repeat#1|seq_repeat]], [[server/spacetimedb/src/main/seeds.rs#seq_multi#1|seq_multi]], and `multi_shot` do the unglamorous work of inserting the shared def rows and returning the id-carrying enum refs. Seeding is idempotent across republishes because `EnemyTemplate`'s `Seed` impl upserts by its natural string key, and the same rows can be edited live by an admin through [[server/spacetimedb/src/main/admin.rs#upsert_enemy_template#1|upsert_enemy_template]] and the `upsert_*_def` reducers (see [[12 Admin & Debug]]) — no republish needed to tune a fight.

### Two clocks

All server-side enemy time comes from two scheduled tables inserted by `init` in [[server/spacetimedb/src/main/lifecycle.rs#init#1|lifecycle.rs]]: `EnemyBehaviorSchedule` fires [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior#1|tick_enemy_behavior]] every 100 ms, and `EnemySpawnSchedule` fires [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_spawn#1|tick_enemy_spawn]] every 2 s. (A scheduled table row is a timer: SpacetimeDB re-invokes the named reducer on the row's interval — see the boot doc.) Because every publish wipes the database, both schedules are rebuilt from scratch each publish along with the seeds. The 100 ms tick rate is load-bearing beyond scheduling: [[server/spacetimedb/src/enemy/methods.rs#apply_movement#1|apply_movement]] integrates positions with `dt = BEHAVIOR_TICK_DT` (0.1, from `main/global.rs`), so changing the schedule interval without changing the constant would silently change movement speeds.

### The spawn tick: regions as spawn pools

```sync
![[00 End-to-End Timeline Flowchart#^enemy-2{seamless:true,title:false}]]
```

There is no dedicated spawn-zone table: spawn regions **are** the world's [[server/spacetimedb/src/world/instance_tables.rs#BiomeRegion#1|BiomeRegion]] rows (center hex, `spawn_radius`, `max_enemies`, `enemy_template_ids`), generated with the terrain at publish — nine region defs, all seeded with the same uniform Enemy/Archer pool by `seed_region_def` (its own comment says there's no per-region density variation yet). Note this is the full extent of biome-enemy coupling today: regions are spawn pools with a template list, nothing more — the "biomes as gameplay" design (biome-specific mechanics) is aspirational and stays out of these docs.

[[server/spacetimedb/src/enemy/methods.rs#spawn_from_biome_regions#1|spawn_from_biome_regions]] walks every region each tick with four gates, in order:

1. **Player proximity with lookahead** — [[server/spacetimedb/src/enemy/methods.rs#has_player_near_spawn_point#1|has_player_near_spawn_point]] checks the chunks within `SPAWN_LOOKAHEAD_CHUNK_RINGS` (2) of the region center for any `PlayerPosition` row, semijoined against `logged_in_player` so a logged-out player's leftover position row (a "ghost" — see [[13 Disconnect & Teardown]]) can't hold a region's spawner open. The lookahead means enemies already exist by the time a player walks into view; nothing pops into existence on screen.
2. **Population cap** — the live count filters `Enemy` rows to the region's template list within `spawn_radius` of its center, measured with `wrapped_distance_sq` rather than plain Euclidean distance. The inline comment explains why that matters: the world is a torus and a spawn offset can cross a lap seam, so an unwrapped check could fail to count a region's own children and the cap would never trip.
3. **Template pick** — `(region_id ^ (timestamp_seconds)) % template_count`, a cheap time-varying rotation through the pool.
4. **Position draw** — a SplitMix64 stream seeded by `region_id.wrapping_add(micros)` produces two unit draws via `next_unit`; the radius draw is square-rooted so points are uniform over the disc's *area* (raw scaling bunches spawns at the center). A nearby comment records the bug that forced the proper PRNG draw: casting a huge `u64` straight to `f32` has rounding granularity coarser than the per-timestamp advance, which froze the angle draw for long stretches. The point is then wrapped onto the torus and assigned a spiral chunk index before [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype#1|spawn_enemy_archetype]] runs with `is_elite: false, immortal: false`.

### The archetype: a behavior tree made of rows

```sync
![[00 End-to-End Timeline Flowchart#^enemy-3{seamless:true,title:false}]]
```

This is the server-side mirror of the client's entity/component model (see [[02 The Component Framework]]): one table per concern, `enemy_id`/`behavior_id` as the joins, and a pair of archetype helpers that always create and destroy the bundle as a unit. [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior#1|build_enemy_behavior]] instantiates the template into runtime rows in `enemy/instance_tables.rs`:

- **`EnemyBehavior`** — the root: current `aggro_target`, `aggro_locked_until`, `was_simulating` (the entry-edge flag), `active_phase_index`, and the last movement direction (`velocity_x/y`).
- **`EnemyPhase`** (one per phase) — runtime copies of the threshold/movement/loop-delay fields plus timers: `phase_waiting`/`phase_loop_timer` for the inter-cycle breather and `move_cycle_started_at`, the anchor the walk/pause movement cycles are measured from.
- **`EnemyAttack`** (one per sequence) — the sequence cursor: `step_index`, `step_timer`, `step_waiting` (waiting out a `next_step_delay`), `start_delaying` (waiting out the sequence's `start_delay`).
- **`EnemySequenceStep`** (one per step) — an ordered `SequenceStepRef` pointing at the shared def... except for `Repeat` steps. A `RepeatStepDef` is shared template data, but its `repeat_count` must advance per enemy, so `build_enemy_behavior` copies each one into a per-enemy **[[server/spacetimedb/src/enemy/instance_tables.rs#RepeatStepInstance#1|RepeatStepInstance]]** row and the ref points at the copy. This is the one place instantiation is not just foreign keys.

The flat [[server/spacetimedb/src/enemy/instance_tables.rs#Enemy#1|Enemy]] row (position, hp, current phase, `is_elite`/`immortal`, btree-indexed `chunk_index`) then points at the tree by `behavior_id`; keeping the hot per-tick fields flat on one row is deliberate, so the 10 Hz tick updates one row instead of re-joining the tree.

Teardown is symmetric: [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior#1|delete_enemy_behavior]] walks the tree bottom-up (repeat instances, steps, attacks, phases, root) and deletes every row, and [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype#1|despawn_enemy_archetype]] calls it before deleting the `Enemy` row itself — no orphan component rows outlive the entity. One subtlety the code comments on: each level is **collected into a `Vec` before deleting**, because deleting rows while iterating a btree index filter silently skips entries (the same reason `teardown_profile` collects first). Every spawn/despawn path routes through these two helpers — the spawn tick, combat kills (`deal_damage_to_enemy`), and the admin [[server/spacetimedb/src/main/admin.rs#spawn_enemy#1|spawn_enemy]]/`despawn_enemy` reducers — so the invariant holds no matter who pulls the trigger.

### The behavior tick: two ranges, one state machine

```sync
![[00 End-to-End Timeline Flowchart#^enemy-4{seamless:true,title:false}]]
```

Each tick, [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior#1|tick_enemy_behavior]] iterates all enemies and first asks [[server/spacetimedb/src/enemy/methods.rs#has_player_in_simulation_range#1|has_player_in_simulation_range]] whether any *logged-in* player is within `move_sim_factor × CAMERA_VIEW_RADIUS`. The check is chunk-pruned (`SIMULATION_CHUNK_RINGS` = 2 rings of candidate chunks via `surrounding_chunk_indices`, then exact distance) and ghost-guarded by the same `logged_in_player` semijoin as the spawn tick — an enemy standing next to a logged-out player's stale position row goes to sleep instead of fighting a ghost (`was_simulating` flips back to false). Enemies outside the range cost exactly one cheap check per tick.

Aggro is an **entry-edge** decision: `recompute_aggro` runs only on the tick an enemy *enters* simulation range (or on phase change / hit, below), picking the nearest logged-in player via [[server/spacetimedb/src/enemy/methods.rs#find_nearest_player_id#1|find_nearest_player_id]] and stamping `aggro_locked_until = now + aggro_lock_seconds`. Without the edge trigger, a 10 Hz retarget would thrash targets mid-fight; the lock additionally keeps a damaged enemy from instantly bouncing between attackers.

Phase transitions are **detected, not driven**, here: `report_enemy_hit` is what advances `enemy.phase` (via `compute_phase`, below); the tick just notices `enemy.phase != behavior.active_phase_index` and calls [[server/spacetimedb/src/enemy/methods.rs#begin_phase_cycle#1|begin_phase_cycle]], which restarts the movement cycle anchor and either starts the inter-phase breather (`phase_loop_delay > 0` → park all attacks, reset their cursors and repeat counts) or immediately re-arms every sequence through [[server/spacetimedb/src/enemy/methods.rs#activate_all_attacks#1|activate_all_attacks]]. This split means damage code never touches timers, and tick code never computes damage.

```sync
![[00 End-to-End Timeline Flowchart#^enemy-5{seamless:true,title:false}]]
```

Inside the tighter `attack_sim_factor` ring, [[server/spacetimedb/src/enemy/methods.rs#tick_phase#1|tick_phase]] advances each of the phase's sequences by at most one step transition per tick (all sequences run *in parallel* — that is how P3 overlays its rotating spirals), and when every sequence is exhausted the phase loop restarts through `begin_phase_cycle`. The per-sequence cursor lives on the `EnemyAttack` row between ticks, and [[server/spacetimedb/src/enemy/methods.rs#tick_sequence#1|tick_sequence]] is a small state machine over it:

- **`start_delaying`** burns down the sequence's `start_delay` first (P3's six rotating `seq_multi` bursts are one sequence; the Archer's second sequence starts 0.5 s late).
- **Single** fires once, then either advances immediately or waits out `next_step_delay` (`step_waiting`).
- **Repeat** re-fires every `repeat_interval` until its per-enemy `RepeatStepInstance.repeat_count` hits `repeat_target`, then drains `next_step_delay` and resets the count for the next cycle.
- **Multi** fires all its shots in one tick, each tagged `SequenceStepRef::Multi { def_id, shot_index }` — the shot index is load-bearing later, because it's how `report_hit` resolves which row of the def a given bullet came from.

Every fired step becomes one [[server/spacetimedb/src/enemy/instance_tables.rs#BulletPatternEvent#1|BulletPatternEvent]] row — an append-only `event` table (SpacetimeDB event tables are ephemeral: rows are pushed to subscribers, not persisted as state). The row carries everything the client needs to render without further lookups: pattern + params, origin (the enemy's position) plus the step's `origin_offset`, `base_angle_offset`, the resolved target identity ([[server/spacetimedb/src/enemy/methods.rs#resolve_step_target#1|resolve_step_target]] maps `Idle` → `None`, `AggroTarget` → the locked identity), texture, lifetime, the enemy's `chunk_index`, and the `source_step` — the `SequenceStepRef` that will later identify this bullet's damage on hit (combat-5). The server itself never simulates a bullet; the event row *is* the bullet, and every client derives the trajectory locally.

### Movement and the network protocol

```sync
![[00 End-to-End Timeline Flowchart#^enemy-6{seamless:true,title:false}]]
```

[[server/spacetimedb/src/enemy/methods.rs#apply_movement#1|apply_movement]] runs every simulating tick with `dt = BEHAVIOR_TICK_DT`. Wander/Chase/Flee all share [[server/spacetimedb/src/enemy/methods.rs#cycle_phase#1|cycle_phase]], which derives (active?, cycle index) from wall-clock time since `move_cycle_started_at` — no countdown timers are stored; the cycle is a pure function of time. Wander's direction is `hash_to_unit(splitmix64(enemy_id + cycle_index)) × τ`, so the path is fully reproducible from data already on the row (enemy id + time) with zero extra state — the comment says this is for audits. Chase and Flee resolve the aggro target's current position through [[server/spacetimedb/src/enemy/methods.rs#find_player_pos_by_id#1|find_player_pos_by_id]] (again ghost-guarded); Flee only runs while inside `min_range`, otherwise it idles — that is what makes the Archer a kiter. A missing `MovementDef` row degrades gracefully to "stand still" rather than erroring.

The tick then wraps the new position onto the torus (`wrap_world_pos`), recomputes the chunk (`world_to_chunk` + `spiral_chunk_index`), and updates the `Enemy` row. That row update **is** the entire network protocol: the [[server/spacetimedb/src/enemy/views.rs#nearby_enemies#1|nearby_enemies]] view filters `Enemy` to the subscriber's AOI chunks with the standard OR-chain over `chunk_index` (plus the sentinel unmatchable query when the caller has no chunk yet — see the view idioms in [[06 Movement & Position Sync]]), so position/hp/phase changes stream to interested clients ~10×/s for free. The companion [[server/spacetimedb/src/enemy/views.rs#enemy_templates#1|enemy_templates]] view is an unfiltered anonymous view of the template table — templates are tiny and static, so every client just caches all of them.

### The client puppet

```sync
![[00 End-to-End Timeline Flowchart#^enemy-7{seamless:true,title:false}]]
```

The spawn/despawn mechanics are the generic spawner's (join-7): [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnEnemyInsert#1|OnEnemyInsert]] instantiates its exported `EnemyScene` (`default_enemy.tscn`), keys it by `EnemyId` in a dictionary, and `OnEnemyDelete` frees it when the row leaves the AOI or the enemy dies. What makes the enemy special is what the puppet does with rows.

`default_enemy.tscn` declares the full component set inline (live wiring — there is no separate enemy component scene): `AnimatedSprite2D` + `CollisionShape2D`, `StatsComponent`/`HealthComponent` mirrors, a `FactionComponent` set to `Enemies` (what `HitZone`s test opposition against), a `DamageReceivingComponent` hurtbox on the enemy collision layer (what player hit zones detect — the physics body itself is not the combat surface), an `InterpolationComponent`, and two `TableBinderComponent` children with their signals connected in the scene file: `NearbyEnemiesBinder` (`ReplayExistingRows = true`, so a row already cached when the puppet spawns arrives through the same insert path) and `BulletPatternEventBinder`. [[client/Scripts/Players/Enemies/Enemy.cs|Enemy.cs]] is `CharacterBody2D, IEntity` — like `LocalPlayer` it implements the entity interface directly with its own `EntityRegistry` because C#'s single inheritance is already spent on the physics base class.

- [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowInserted#1|OnEnemyRowInserted]] ignores rows for other enemies (every puppet binds the same view), snaps the `InterpolationComponent` to the row position, loads the template's `SpriteFrames` by resolving `template.TextureId` through the catalog (`GameManager.GetResPath`), and seeds the mirrors: `maxHp` from the cached `EnemyTemplate`, hp from the live row, both through `HealthComponent.SetFromServer`, with the shared `Stat` registered under `StatKind.Hp` in `StatsComponent` (the comedot shared-Stat pattern). The server defines the values; the component never computes them.
- [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowUpdated#1|OnEnemyRowUpdated]] only retargets the interpolation lerp and refreshes hp/phase — 10 updates a second become smooth motion because `InterpolationComponent` does the tweening (see [[06 Movement & Position Sync]]).
- [[client/Scripts/Players/Enemies/Enemy.cs#OnBulletPatternRowInserted#1|OnBulletPatternRowInserted]] filters pattern events to this puppet's `EnemyId` and forwards matches to `BulletManager.Instance.SpawnEnemyBullet`. One caveat worth knowing: `GameTables` subscribes the `BulletPatternEvent` event table **unfiltered** — the server-side `chunk_index` btree exists, but no view narrows events to the AOI, so every client receives every fired pattern worldwide and each puppet discards the ones that aren't its own. At current enemy counts this is negligible; it is the first place to look if bullet-event bandwidth ever matters.
- `_Process` plays Walk/Idle off `interpolation.Moving` and mirrors the enemy into the 3D backdrop viewport through a `CharacterModel3D` built from the exported `SkeletonScene` (`Skeleton_Warrior.fbx`, wired in the `.tscn`) — the same pattern as the local player (see [[11 Camera & Presentation]]).

### Rendering the patterns

```sync
![[00 End-to-End Timeline Flowchart#^enemy-8{seamless:true,title:false}]]
```

[[client/Scripts/Game/BulletManager.cs|BulletManager]] is a thin facade: a `Node2D, IEntity` root instanced in `game.tscn` whose only jobs are to own the BlastBullets2D factory child (typed as a plain `[Export] Node` because the GDExtension exposes no C# types) and to forward spawn requests into [[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs|BulletSpawnerComponent]]. The static `Instance` property exists because external entities like the enemy puppet have no registry path to the bullet entity.

[[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#SpawnEnemyBullet#1|SpawnEnemyBullet]] is a five-way dispatch on `PatternType`. The shared pre-work: compute the origin (event origin + step offset), resolve the base angle from the target identity — [[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#ResolveTargetAngle#1|ResolveTargetAngle]] reads the local player's `LocalPlayerPosition` row for self-targets and `NearbyRemotePlayers` for others — then stamp `bullets_custom_data` with a [[client/Scripts/Game/BulletData.cs|BulletData]] carrying the event's `SourceStep`, set the lifetime, and apply the cached texture. That `BulletData` is the thread back to combat: when a bullet later overlaps a player, the router reports the `SequenceStepRef` and the server resolves damage by looking the ref up in the step tables (combat-5/6) — clients never name a damage number.

Geometry per pattern, all expressed as arrays of `Transform2D`s fed to the factory:

- **Ring/Curtain** spawn as *one* multi-bullet `DirectionalBullets2D` instance (Ring = `τ/count` steps around the circle; Curtain = an `angle_span`-wide fan centered on the base angle, skipping `gap_index` — the authored dodge gap).
- **Volley/Shotgun/Explosion** spawn as *per-pellet* instances because each pellet needs its own speed/lifetime. Their variance is not random per client: [[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#Jitter#1|Jitter]] is a SplitMix64 hash of `(event_id, pellet_index, stream)` mapped to `[-magnitude, +magnitude]`, so every client (and a future audit script) derives bit-identical trajectories from the event row alone — the same deterministic-hash philosophy as the server's Wander directions. Volley jitters speed and angle; Shotgun and Explosion additionally jitter per-pellet lifetime, which is what makes their clouds feel organic instead of a clean wall.

Two operational details matter more than they look:

- **Everything crossing into BlastBullets2D is an untyped `Call(…)`** — `spawn_controllable_directional_bullets`, `set_collision_layer_from_array`, `generate_random_data`. GDExtension methods have no compile-time checking in C#, so a typo in any of these strings is a runtime-only failure; treat renames in the plugin as breaking changes here. (The `ProjectileTextureAngleOffset = π/4` constant exists because the Arrow art faces up-right; it pre-rotates so BlastBullets2D's own trajectory rotation lands correctly.)
- **The factory cannot enumerate its active instances**, so every `spawn_controllable_directional_bullets` result is tracked in the `LiveEnemyBullets` HashSet, pruned once a second and before each spawn by [[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#PruneLiveEnemyBullets#1|PruneLiveEnemyBullets]]. Liveness is checked per bullet index (`is_bullet_status_enabled`) because the bulk `get_all_bullets_status` array only reports index 0 — a plugin quirk the code comments flag. This registry exists for the bullet-control abilities (`BulletControllerComponent`, combat-9), which must find live bullets later; `SpawnEnemyBulletFan` is the entry point those abilities use to spawn split pellets that keep the original `SourceStep`.

### Where combat touches the enemy

The enemy side of damage is deliberately thin and fully covered in [[09 Combat & Damage]]; the beats that matter here are: [[server/spacetimedb/src/enemy/reducers.rs#report_enemy_hit#1|report_enemy_hit]] silently ignores missing and `immortal` enemies, then delegates to [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_enemy#1|deal_damage_to_enemy]]. A kill runs `despawn_enemy_archetype` (the tree teardown above) and awards `max_hp / 10` XP. A survival recomputes the phase with [[server/spacetimedb/src/enemy/methods.rs#compute_phase#1|compute_phase]] — the highest `phase_index` whose threshold the new hp% has crossed, taking the max defensively in case phases are ever authored out of descending order — and re-aggros: a phase change triggers a full `recompute_aggro`, otherwise the attacker becomes the target once the current `aggro_lock_seconds` lock has expired. The tick then notices the phase change on its next pass (above). The bullet-control protocol (`BulletControlEvent`/`control_bullets`) also lives in this module's files but is a player-ability concern — see combat-9 and [[10 Inventory, Items & Enchantments]] for the ability side.

## Known gaps / stubs

- **`is_elite` is dead data.** `Enemy.is_elite` is stored server-side, mirrored into `Enemy.IsElite` on the client, and settable only through the admin `spawn_enemy` reducer (biome spawning always passes `false`) — but no behavior, spawn rule, or visual anywhere consumes it. Elites are a schema placeholder today.
- **`immortal` is admin-only by construction.** Same situation: only `spawn_enemy` can set it, and the only consumer is `report_enemy_hit`'s early-out. Fine for test bosses, but there is no gameplay path to it.
- **`BulletPatternEvent` is not AOI-filtered.** The client subscribes to the whole event table (`GameTables`) and every puppet filters by `enemy_id` locally; the server-side `chunk_index` btree is unused by any view. Correct at current scale, but it is unfiltered per-event traffic to every client.
- **No spawn variety per region.** `seed_region_def` gives all nine regions the same Enemy/Archer pool, radius, and cap (its comment says so); biome-flavored enemy mixes are unimplemented, and "biomes as gameplay" beyond spawn pools is an aspirational system that stays out of these docs by design.

## Where to go next

Continue to [[09 Combat & Damage]] for what happens when those bullets connect — the `SequenceStepRef` damage resolution, the death/teardown path, and the bullet-control abilities that consume `LiveEnemyBullets`. [[11 Camera & Presentation]] covers the 3D backdrop the enemy puppet mirrors into, and [[12 Admin & Debug]] covers the `spawn_enemy`/`upsert_enemy_template` reducers for authoring and testing fights live.
