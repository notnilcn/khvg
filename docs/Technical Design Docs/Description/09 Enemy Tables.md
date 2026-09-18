# 09 Enemy

Template = data. This doc covers the 17 tables in `server/spacetimedb/src/enemy/`: five static definition tables (movement, three step kinds, template), five runtime behavior-tree tables plus the `Enemy` row itself, two schedule tables, three append-only event tables, and one server-only attestation table. A *reducer* is a server function a client calls (transactional — it returns nothing; clients learn results only through table subscriptions). A *scheduled tick* is a reducer the database fires on a timer via a job row. A *view* is a parameterized server-side query the client subscribes to by name instead of writing SQL. A *subscription wave* is one of the `BaseTables`/`LobbyTables`/`GameTables` name lists in `TableSubscriber.cs` — everything the live world needs arrives in the game wave. A *binder* is a `TableBinderComponent` node that re-exposes one subscribed table's row events as editor-wireable Godot signals (`RowInserted`/`RowUpdated`/`RowDeleted`); handlers read the row off `LastRow` because signals carry no args. A *scene* (`.tscn` file) is Godot's prefab format — node trees declared in text, with signal wiring inline.

Maintainer refs (jump-off points, not authority — code wins): [[server/spacetimedb/src/enemy/AGENTS.md|enemy AGENTS.md]] (canonical module doc: tick/phase/sequence semantics, field encodings, add-an-enemy recipe), [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]].

## Assumed knowledge

[[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] (the `^table-*` spine this doc transcludes), [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] (conventions, reading order), [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] (views, waves, binders, the event-table replay rule), [[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]] (the shared damage pipeline — the only place damage is computed).

## The 30-second version

Enemies are authored as `EnemyTemplate` rows (HP-gated phases of concurrent attack sequences built from `MovementDef`/`SingleStepDef`/`RepeatStepDef`/`MultiStepDef` rows) and simulated by a 100 ms scheduled tick that walks only the chunk-gated enemies near logged-in players, appending one `BulletPatternEvent` per shot (plus a `BulletFireAttestation` proof row) while steering bodies with context steering; clients render events verbatim through a single shared binder and puppet `Enemy` rows with dead reckoning, and report hits back through the rate-limited `report_enemy_hit` reducer while victims attest incoming fire against the attestation rows.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do I add a new enemy or boss? | EnemyTemplate | [[#EnemyTemplate]] + Files (`defs/`, `emitters.rs`) |
| What movement can a phase use? | MovementDef | [[#MovementDef]] |
| What is a single-shot / repeating / volley bullet step? | SingleStepDef / RepeatStepDef / MultiStepDef | [[#SingleStepDef]], [[#RepeatStepDef]], [[#MultiStepDef]] |
| How does an enemy appear on screen? | Enemy | [[#Enemy]] |
| How does an enemy move between server updates? | Enemy | [[#Enemy]] (dead reckoning) |
| What runs the 100 ms simulation? | EnemyBehaviorSchedule | [[#EnemyBehaviorSchedule]] |
| Why does an enemy freeze / speed up when stunned? | EnemyBehavior | [[#EnemyBehavior]] (stun consults) |
| What is the per-enemy repeat counter? | RepeatStepInstance | [[#RepeatStepInstance]] |
| What is a step slot / attack cursor / phase row? | EnemySequenceStep / EnemyAttack / EnemyPhase | [[#EnemySequenceStep]], [[#EnemyAttack]], [[#EnemyPhase]] |
| How do bullets get from server to screen? | BulletPatternEvent | [[#BulletPatternEvent]] |
| How do player bullet-delete/split/black-hole casts work? (there is no `control_bullets` reducer) | BulletControlEvent | [[#BulletControlEvent]] |
| How are Spell flashes drawn if damage is server-side? | AbilityVisualEvent | [[#AbilityVisualEvent]] |
| How does the server know a bullet really fired? | BulletFireAttestation | [[#BulletFireAttestation]] |
| How do I report hitting an enemy? | Enemy | [[#Enemy]] (`report_enemy_hit`) |
| Where is damage actually computed? | — (other doc) | [[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]] (`apply_damage_to_enemy`); kill flow in [[#Cross-table flows]] |
| Where is the stagger bar / enemy debuff chips? | Enemy + `ActiveEnemyStatusEffect` | [[#Enemy]]; debuff rows live in the Status doc (^table-active-enemy-status-effect) |
| Where do enemies spawn from? (there is no `EnemyZone` table) | EnemySpawnSchedule + `BiomeRegion` | [[#EnemySpawnSchedule]]; regions are a World-doc table (^table-biome-region) |
| Which difficulty is running? | EnemyTemplate | [[#EnemyTemplate]] (compile-time variants) |
| Why do bullets curve / accelerate mid-flight? | SingleStepDef etc. → BulletPatternEvent | [[#BulletPatternEvent]] (`angular_speed`/`speed_acceleration`) |
| Degrees or radians? | — (convention) | Bullet angles are degrees (`spread` and curtain `angle_span` are radians); the whole movement layer is radians — see [[#RepeatStepDef]] and [[#Enemy]] |

## Flowcharts

[[flowcharts/main-enemy.canvas]] is this system's composed flowchart (the `enemy` flow in `flowcharts/flows.json`: the enemy subfolder aggregate plus the nearest client canvases for puppets, spawning, world, and main). Deep dives:

- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/enemy_subfolder/enemy_subfolder.canvas]] — the module aggregate (tick, sequences, movement, spawning).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Players_subfolder/Enemies_subfolder/Enemies_subfolder.canvas]] — the `Enemy.cs` puppet read path.
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/default_enemy_codefile/default_enemy_codefile.canvas]] — the live puppet scene wiring.

**Phase 2 proposal:** no composed `flowcharts/main-enemy.canvas` exists yet (verified — the flowcharts root holds only `flows.json` plus `Subflowcharts/`; the old-plural `main-enemies.canvas` was not found either), so the links above stay unresolved until the phase-2 recompose, the same expected state [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] describes for the global canvas. Compose the `enemy` flow to `flowcharts/main-enemy.canvas` and delete any stale `main-enemies.canvas` if one is present at compose time.

## Tables

### MovementDef

```sync
![[00 Table Map#^table-movement-def{seamless:true,title:false,marker:01.}]]
```

#### Shape

One named context-steering movement per phase: a `SteeringMovement` = Goal × Pattern × Avoidance (`MoveGoal` Idle/AggroTarget/SpawnPoint/FixedPoint; `MovePattern` Direct/Flee/Orbit/Meander/Zigzag/Lunge with per-pattern params; `separation_weight` and `avoid_cliffs` toggles; an optional move/pause cycle via `active_duration`/`pause_duration`). `speed` is world units/second. All angles in this layer are radians — bullet angles stay degrees. The row exists because `PhaseDef` (embedded in `EnemyTemplate`) only stores a `movement_def_id`, so content helpers insert the movement row first and point at it.

#### Changers

- **Insert** by the content helper [[server/spacetimedb/src/enemy/defs/mod.rs#make_phase|make_phase]] at seed time (every `make_phase` call inserts one row), and by admin [[server/spacetimedb/src/main/admin.rs#upsert_movement_def|upsert_movement_def]] (`def_id` 0 inserts, an existing id updates).
- **Never updated or deleted** by gameplay; there is no delete path.

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it, no visible end. Clients never see movement defs; they only see the resulting angle+speed on each `Enemy` row.
- ***Server*** — [[server/spacetimedb/src/enemy/methods.rs#apply_movement|apply_movement]] resolves the phase's id every behavior tick and steers from it (missing def = stand still, heading preserved).

### SingleStepDef

```sync
![[00 Table Map#^table-single-step-def{seamless:true,title:false,marker:02.}]]
```

#### Shape

One single-shot bullet pattern: pattern params (`PatternType` Ring/Volley/Curtain/Shotgun/Explosion), target (`EnemyTarget` Idle/AggroTarget), origin offsets, `base_angle_offset` (degrees), texture, lifetime, per-bullet `damage`, mid-flight `angular_speed` (degrees/second of curve) and `speed_acceleration`, and `next_step_delay` before the sequence advances. `line`/`line_speed_step` on Ring/Volley/Shotgun params stack bullets per direction as a speed ladder (bullet j flies at `speed + step × (j+1)`), not spatial copies.

#### Changers

- **Insert** by [[server/spacetimedb/src/enemy/defs/mod.rs#seq_single|seq_single]] at seed time and by admin [[server/spacetimedb/src/main/admin.rs#upsert_single_step_def|upsert_single_step_def]].
- **Never updated or deleted.**

#### Readers

- ***Client*** — `public` but unsubscribed: no visible end. Clients render the fired `BulletPatternEvent`, never the def.
- ***Server*** — [[server/spacetimedb/src/enemy/methods.rs#tick_sequence|tick_sequence]] resolves the def per shot, and [[server/spacetimedb/src/enemy/methods.rs#step_damage|step_damage]] reads its `damage` so the behavior tick stamps per-bullet damage onto the event at insert time.

### RepeatStepDef

```sync
![[00 Table Map#^table-repeat-step-def{seamless:true,title:false,marker:03.}]]
```

#### Shape

One repeating emitter: the single-shot fields plus `repeat_interval`, `repeat_target` (**0 = fire forever**, never advances — the right shape for continuously-firing emitters in HP-driven phases), `angle_step` (per-shot spin: shot N fires at `base + step × N` with the origin offset orbited by the same angle), and `next_step_delay`. `spread` (and `CurtainParams.angle_span`) are radians in the raw structs even though every other bullet angle is degrees; the authoring layer hides this because [[server/spacetimedb/src/enemy/emitters.rs#shotgun|Emitter::shotgun]] takes degrees and converts.

#### Changers

- **Insert** by [[server/spacetimedb/src/enemy/defs/mod.rs#seq_repeat|seq_repeat]] at seed time (directly, or via [[server/spacetimedb/src/enemy/emitters.rs#compile|Emitter::compile]]) and by admin [[server/spacetimedb/src/main/admin.rs#upsert_repeat_step_def|upsert_repeat_step_def]].
- **Never updated or deleted.** Per-enemy progress lives on the copied ^table-repeat-step-instance row, never here, so one def serves every live enemy.

#### Readers

- ***Client*** — `public` but unsubscribed: no visible end.
- ***Server*** — [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]] copies each referenced def into a per-enemy `RepeatStepInstance` at spawn; `tick_sequence` reads it every behavior tick through the instance (catch-up loop capped at `MAX_CATCHUP_SHOTS` = 8, backlog clamped to `BACKLOG_CAP` = 2 intervals so sim re-entry fires at most a ~2-shot burst).

### MultiStepDef

```sync
![[00 Table Map#^table-multi-step-def{seamless:true,title:false,marker:04.}]]
```

#### Shape

One multi-shot volley: a `shots` vec of `MultiShot` entries (each a full single-shot parameter set) fired together, plus `next_step_delay`. It exists for one-tick bursts whose pellets differ per shot in ways a Repeat's uniform spin cannot express.

#### Changers

- **Insert** by [[server/spacetimedb/src/enemy/defs/mod.rs#seq_multi|seq_multi]] and by admin [[server/spacetimedb/src/main/admin.rs#upsert_multi_step_def|upsert_multi_step_def]].
- **Never updated or deleted.** No shipped content uses it yet (`multi_shot`/`seq_multi` are `#[allow(dead_code)]` — available vocabulary, zero callers), so every Multi row in the wild today comes from the admin upsert path.

#### Readers

- ***Client*** — `public` but unsubscribed: no visible end.
- ***Server*** — `tick_sequence` reads the def per visit and emits the whole `shots` vec as events in one tick, truncated at `MAX_MULTI_SHOTS` = 24 with a `log::warn` (a content-sized per-tick row-insert storm otherwise).

### EnemyTemplate

```sync
![[00 Table Map#^table-enemy-template{seamless:true,title:false,marker:05.}]]
```

#### Shape

One enemy kind, keyed by string `template_id`: sprite `texture_id`, `display_name`, `max_hp`, flat per-bullet `defense` (subtracted per hit, minimum 1 — high defence punishes many-weak-bullets patterns), `move_sim_factor`/`attack_sim_factor` (multiples of `CAMERA_VIEW_RADIUS`, so ranges rescale if the camera ever changes), `aggro_lock_seconds`, `stun_behavior` (Pause/Slowed/Enraged — all current seeds use Pause), and the `phases` vec of `PhaseDef` (index, hp threshold, movement ref, loop delay, concurrent `AttackSequence`s). Difficulty is compile-time: variants are separate rows seeded per tier (`GorgonBoss_Easy` … `GorgonBoss_Madness` via [[server/spacetimedb/src/enemy/emitters.rs#Difficulty|Difficulty]]); spawning picks a template id, and there is no runtime difficulty flag.

#### Changers

- **Upsert** by `EnemyTemplate::seed` ([[server/spacetimedb/src/main/seeds.rs#EnemyTemplate|EnemyTemplate::seed]] — update when the id exists, insert otherwise) at publish from [[server/spacetimedb/src/main/seeds.rs#seed_default_enemies|seed_default_enemies]] (`Enemy`, `Archer`, `TestBoss` — the every-movement/every-pattern showcase) and [[server/spacetimedb/src/enemy/defs/gorgon_boss.rs#seed|gorgon_boss::seed]] (four difficulty variants), both called from [[server/spacetimedb/src/main/lifecycle.rs#init|init]]; and by admin [[server/spacetimedb/src/main/admin.rs#upsert_enemy_template|upsert_enemy_template]].
- **Never deleted.**

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/enemy/views.rs#enemy_templates|enemy_templates]] global view → `GameTables` wave → no binder; instead [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowInserted|Enemy.OnEnemyRowInserted]] reads `EnemyTemplates.Iter()` directly per spawned puppet for `max_hp`, the 2D sprite frames (via `GameManager.GetResPath2D`), and the 3D model (via `GameManager.GetResPath3D`, loaded in code so 2D mode never touches the FBX). Visible end: each `Enemy` puppet's health bar (`HealthComponent.SetFromServer(row.Hp, maxHp)`), sprite, and `CharacterModel3D` mirror (hidden sprite + no frames in 3D mode).
- ***Server*** — Spawn paths (`spawn_enemy`, `spawn_from_biome_regions`) resolve the template by id; the combat sink reads `defense`; the behavior tick reads the sim factors and `stun_behavior`.

### RepeatStepInstance

```sync
![[00 Table Map#^table-repeat-step-instance{seamless:true,title:false,marker:06.}]]
```

#### Shape

Per-enemy copy of a `RepeatStepDef` (all fire fields cloned) plus the mutable `repeat_count`, so the spin/interval progress of one enemy never leaks into another firing the same def. Referenced from `EnemySequenceStep` via `SequenceStepRef::Repeat(repeat_id)`.

#### Changers

- **Insert** in `build_enemy_behavior`, one row per Repeat step of the template.
- **Update** (`repeat_count` increment, `step_timer` re-arm by whole intervals so pacing never drifts) in `tick_sequence`; reset to 0 on sequence/phase restarts via `reset_repeat_counts`.
- **Delete** in [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior|delete_enemy_behavior]] alongside the rest of the tree.

#### Readers

- ***Client*** — `public` but unsubscribed: no visible end.
- ***Server*** — `tick_sequence`'s catch-up loop is the only reader. Unremarkable beyond that.

### EnemySequenceStep

```sync
![[00 Table Map#^table-enemy-sequence-step{seamless:true,title:false,marker:07.}]]
```

#### Shape

One step slot: (`attack_id`, `step_index`) ordering plus a `SequenceStepRef` (Single def id / Repeat instance id / Multi def id + shot index). The slot is stable storage; the mutable cursor lives on `EnemyAttack`.

#### Changers

- **Insert** in `build_enemy_behavior`, mirroring each template step (Repeat defs become fresh instance ids here).
- **Never updated** — steps are positional; the cursor moves, the slots don't.
- **Delete** in `delete_enemy_behavior`.

#### Readers

- ***Client*** — `public` but unsubscribed: no visible end.
- ***Server*** — `tick_sequence` looks the current `step_index` up per attack per tick (missing row = sequence stalls silently via early return, never a panic — the scheduled tick must not destroy the WASM instance).

### EnemyAttack

```sync
![[00 Table Map#^table-enemy-attack{seamless:true,title:false,marker:08.}]]
```

#### Shape

Runtime cursor for one attack sequence inside a phase: `step_index`, `step_timer` (doubles as the loop-delay timer), and the `step_waiting` / `start_delaying` / `loop_waiting` flags. One row per `AttackSequence` in the phase def, linked by `attack_index` (bounds-checked everywhere — a stale tree vs an edited template deactivates instead of indexing).

#### Changers

- **Insert** in `build_enemy_behavior` (cursors start at step 0, timer stamped at build so fresh spawns carry no backlog).
- **Update** every behavior tick in [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] via [[server/spacetimedb/src/enemy/methods.rs#tick_sequence|tick_sequence]] / [[server/spacetimedb/src/enemy/methods.rs#tick_phase|tick_phase]] (delta-checked: unchanged rows skip the write).
- **Delete** in `delete_enemy_behavior`.

#### Readers

- ***Client*** — `public` but unsubscribed: no visible end.
- ***Server*** — `tick_phase` walks every attack in the phase each tick (concurrent sequences), honoring `start_delay`, `next_step_delay` waits, and self-looping `loop_delay >= 0` sequences that restart independently and never count toward the phase-level restart.

### EnemyPhase

```sync
![[00 Table Map#^table-enemy-phase{seamless:true,title:false,marker:09.}]]
```

#### Shape

Runtime phase row per behavior: `phase_index`, `hp_threshold`, `movement_def_id`, `phase_loop_delay` (silence between loops — and the rest after an HP-threshold phase change), `phase_waiting` + `phase_loop_timer`, and `move_cycle_started_at` anchoring the movement move/pause cycle. One row per `PhaseDef`; only the matching-`phase_index` row drives the tick.

#### Changers

- **Insert** in `build_enemy_behavior`; phase 0 activates immediately via `begin_phase_cycle`.
- **Update** in `tick_phase` (loop waits/timers) and on HP-threshold transitions detected in `tick_enemy_behavior` (`enemy.phase != active_phase_index` → `begin_phase_cycle`, which restamps the move cycle and either parks the phase in `phase_waiting` or re-arms all attacks).
- **Delete** in `delete_enemy_behavior`.

#### Readers

- ***Client*** — `public` but unsubscribed: no visible end (clients see the resulting `Enemy.phase` byte, not this row).
- ***Server*** — `tick_phase` gates the whole phase on `phase_waiting`; `apply_movement` reads the row's movement def and cycle anchor. Unremarkable beyond that.

### EnemyBehavior

```sync
![[00 Table Map#^table-enemy-behavior{seamless:true,title:false,marker:10.}]]
```

#### Shape

One behavior root per enemy: `aggro_target` + `aggro_locked_until`, `was_simulating` (re-entry edge detector), `active_phase_index` (transition detector), and `velocity_x`/`velocity_y` unit heading memory — Meander's persistent noise-swayed heading, every other pattern's last chosen direction. Server-only in effect: no view exposes it, because the client-facing movement (angle + speed scalar) lives on the `Enemy` row.

#### Changers

- **Insert** in `build_enemy_behavior` (no target, unlocked, not simulating, phase 0, zero heading).
- **Update** in `tick_enemy_behavior` (aggro recompute on sim-range entry and whenever the target goes stale — logged out, died, or left move range — plus the `was_simulating`/`active_phase_index`/heading writes, all delta-checked) and by the combat sink, which re-aggros survivors onto their attacker.
- **Delete** in `delete_enemy_behavior`.

#### Readers

- ***Client*** — `public` but unsubscribed: no visible end.
- ***Server*** — `apply_movement` reads aggro target (as goal) and heading memory; the Stunned-on-Pause check in `tick_enemy_behavior` consults [[server/spacetimedb/src/status/methods.rs#stunned_magnitude|stunned_magnitude]] before any of this runs, freezing the enemy wholesale for the tick.

### Enemy

```sync
![[00 Table Map#^table-enemy{seamless:true,title:false,marker:11.}]]
```

#### Shape

One live enemy: `template_id` + `behavior_id` links, `x`/`y` plus `spawn_x`/`spawn_y` leash anchor, movement as `movement_direction` (radians) + `movement_speed` scalar (0 when standing — the same angle+scalar convention as `PlayerPosition`), `hp`/`phase`/`is_elite`/`immortal`, the 0–100 `stagger` bar, and btree-indexed `chunk_index` for AOI. The row stays flat by design while the behavior tree hangs off `behavior_id` — position/hp/phase are the hot fields every system needs without a join.

#### Changers

- **Insert** in [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype|spawn_enemy_archetype]] (builds the behavior tree, then the flat row at full template HP, stagger 0), via admin [[server/spacetimedb/src/main/admin.rs#spawn_enemy|spawn_enemy]] (wraps + chunk-stamps the position) and the biome spawner in [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_spawn|tick_enemy_spawn]].
- **Update** of position/movement/chunk in `tick_enemy_behavior` (delta-checked, torus-wrapped via `wrap_world_pos`), of hp/phase in the combat sink, of stagger in the 1 Hz status tick (decay; full bar applies Stunned and resets).
- **Delete** in [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] (tree + row together, so no orphans) via admin [[server/spacetimedb/src/main/admin.rs#despawn_enemy|despawn_enemy]] / `despawn_all_enemies` and via combat kills with XP credit.

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/enemy/views.rs#nearby_enemies|nearby_enemies]] AOI view → `GameTables` wave → `NearbyEnemiesBinder` on `EntitySpawnerComponent` (replay on, wired in `game.tscn`) → spawns one `Enemy` puppet (`default_enemy.tscn`, deferred adds so a delete arriving before the spawn cancels instead of freeing a not-yet-parented node; tracked for `GetEnemy`/`EnemyCount`). Each puppet's own `NearbyEnemiesBinder` (replay on, wired in `default_enemy.tscn`) feeds [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowUpdated|OnEnemyRowUpdated]], which reconstructs `Vector2.FromAngle(direction) * speed` and calls `InterpolationComponent.SetTarget` — velocity-driven dead reckoning between the 10 Hz row updates, torus-aware via `TorusMath.NearestCandidate` — plus `HealthComponent.SetFromServer`, phase/stagger mirroring, and (on insert) `SnapTo` + template skinning. [[client/Scripts/Components/Weapon/CombatComponent.cs#ScanNearestEnemy|CombatComponent.ScanNearestEnemy]] also reads `NearbyEnemies.Iter()` for aim, and its child `NearbyEnemiesBinder` (wired in `local_player.tscn`) invalidates the cached nearest enemy in `OnNearbyEnemyDeletedRow`.
- ***Server*** — The behavior tick's chunk-gated simulation set reads `chunk_index`; `report_enemy_hit`'s victim lookup reads by `enemy_id`; `enemies_near_point` serves ability targeting and spell/zone shape queries through the same index.

### EnemySpawnSchedule

```sync
![[00 Table Map#^table-enemy-spawn-schedule{seamless:true,title:false,marker:12.}]]
```

#### Shape

The scheduled-job row arming the biome spawner tick: interval rows persist (the job re-arms itself each firing; cancelling = deleting the row).

#### Changers

- **Insert** by admin [[server/spacetimedb/src/main/admin.rs#toggle_enemy_spawning|toggle_enemy_spawning]]`(true)` (2 s interval). Spawning starts **disarmed** — `init` deliberately inserts no row (debugging default).
- **Delete** by `toggle_enemy_spawning(false)`.
- **Never updated.**

#### Readers

- ***Client*** — Server-only schedule table (not `public`): never subscribed, no visible end.
- ***Server*** — Firing the row runs `tick_enemy_spawn` → `spawn_from_biome_regions`: global `MAX_LIVE_ENEMIES` (64) + per-tick `MAX_SPAWNS_PER_TICK` (3) caps, per-region `max_enemies` counted through the wrap-aware `enemies_near_point` (a plain distance check would lose children wrapped across a lap seam and defeat the cap), spawn gating on `has_player_near_spawn_point` (`SPAWN_LOOKAHEAD_CHUNK_RINGS` = 2, logged-in players only so ghosts can't anchor spawns), uniform-disc placement (`sqrt` radius draw, timestamp-seeded `next_unit` — a raw `u64`-to-`f32` cast silently froze the angle at this magnitude), then torus wrap + chunk stamp + `spawn_enemy_archetype`.

### EnemyBehaviorSchedule

```sync
![[00 Table Map#^table-enemy-behavior-schedule{seamless:true,title:false,marker:13.}]]
```

#### Shape

The scheduled-job row arming the 100 ms behavior tick (`BEHAVIOR_TICK_DT` = 0.1).

#### Changers

- **Insert** in `init` (100 ms interval).
- **Never updated or deleted** in normal operation.

#### Readers

- ***Client*** — Server-only schedule table (not `public`): never subscribed, no visible end.
- ***Server*** — Each firing runs `tick_enemy_behavior`: builds the sim set from `PlayerChunk` rows (written only on real chunk crossings, not every movement report) expanded by `SIMULATION_CHUNK_RINGS` (2) with **no full enemy-table scan**, truncates to `BEHAVIOR_TICK_ENEMY_BUDGET` (96, lowest ids win, `log::warn` not silent); per enemy, exact wrap-distance checks split movement (`move_sim_factor`) from firing (`attack_sim_factor`, so enemies approach before they ever shoot from off-screen), Pause-stun freeze, aggro entry/stale retarget, HP-transition phase restart, concurrent `tick_phase` firing with server-baked damage/origin, then `apply_movement` — all writes delta-checked.

### BulletControlEvent

```sync
![[00 Table Map#^table-bullet-control-event{seamless:true,title:false,marker:14.}]]
```

#### Shape

Append-only relay of one player's bullet-controller cast. There is **no `control_bullets` reducer** (verified absent from the code) — the server tracks no live bullets, so it validates the caller and rebroadcasts; every client resolves the proximity query against its own local bullets, and edge-of-radius results can differ slightly. Geometry is encoded in the fields because the event must be self-contained: Delete/Split use (`x`,`y`) + `radius`; Attract adds (`target_x`,`target_y`) homing target with `duration` = linger seconds (0 = one-shot pull); DeleteRect/SplitRect use cast origin (`x`,`y`) + far end (`target_x`,`target_y`) + width (`radius`); SlashRect is the DeleteRect encoding with `duration` repurposed to the max-bullet-damage threshold as f32 bits (clients decode with `BitConverter`); WallRect is the DeleteRect encoding with `duration` = wall linger seconds.

#### Changers

- **Insert** in [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] for bullet-control ability effects (reached via `activate_ability` and the charge-release path, with full ability validation — cooldown, charges, slot resolution).
- **Append-only: never updated or deleted.**

#### Readers

- ***Client*** — Subscribed as a raw event table → `GameTables` wave → `BulletControlEventBinder` (**replay off** — no `ReplayExistingRows` line in `game.tscn`, so the bool default `false` holds; replay would re-fire history) wired under `BulletManager/BulletControllerComponent` in `game.tscn` → [[client/Scripts/Components/Bullets/BulletControllerComponent.cs#BulletControllerComponent|BulletControllerComponent]] deletes/splits/attracts live enemy bullets near the cast (plus lingering attract/wall zones and the slash flash polygon drawn under `BulletManager`). Own echoes are skipped via `cast_by`, and the caster applies the same cast optimistically from `LocalPlayerInventoryComponent.TryActivateAbility` (guarded against double-cast until the echo lands).
- ***Server*** — No reads; fire-and-rebroadcast by design.

### AbilityVisualEvent

```sync
![[00 Table Map#^table-ability-visual-event{seamless:true,title:false,marker:15.}]]
```

#### Shape

Append-only visual-only relay of a Spell cast — damage is applied server-side in the same `apply_ability_effect` dispatch, so this row only tells every client to draw the flash. Per-kind field encoding: (`x`,`y`) is always the cast origin (the caster's position row); Circle/Meteor/Converge use (`target_x`,`target_y`) as effect center + `radius`; Line uses far end + width; Cone uses the Line encoding with `radius` as spread in radians; `duration` is the visual linger (`SPELL_VISUAL_SECONDS`).

#### Changers

- **Insert** in `apply_ability_effect` on the Spell path (one row per cast, alongside the shape-damage application).
- **Append-only: never updated or deleted.**

#### Readers

- ***Client*** — Subscribed as a raw event table → `GameTables` wave → `AbilityVisualEventBinder` (**replay off**, wired on `SpellVisualComponent` in `game.tscn`) → `SpellVisualComponent` draws the per-kind `Polygon2D` flash and frees it after the duration. The caster renders optimistically through the same `Render` path and skips its own `cast_by` echo — the `BulletControllerComponent` pattern.
- ***Server*** — No reads.

### BulletFireAttestation

```sync
![[00 Table Map#^table-bullet-fire-attestation{seamless:true,title:false,marker:16.}]]
```

#### Shape

Server-only proof that an enemy actually fired a `BulletPatternEvent`: `remaining` starts at the event's pellet count ([[server/spacetimedb/src/enemy/methods.rs#pattern_bullet_count|pattern_bullet_count]] — Ring/Volley batch `count × line`, Shotgun the same, Curtain skips its gap index, Explosion is per-pellet) and each accepted victim report decrements it, so replaying one `event_id` past its pellets is rejected. The count function must match `BulletSpawnerComponent`'s spawn code pellet-for-pellet; a zero-pellet event attests nothing (no row). Keyed by `event_id` (not auto-inc) so reports join directly to the claimed event.

#### Changers

- **Insert** in [[server/spacetimedb/src/enemy/methods.rs#attest_bullet_fire|attest_bullet_fire]] per fired event on the behavior tick's fire path (called with the insert's returned row, which carries the assigned `event_id`).
- **Update** (pool decrement) or **Delete** (pool exhausted) in `report_hit`; **Delete** of rows older than `BULLET_ATTESTATION_TTL_SECONDS` (30 s) in the 1 Hz `tick_status_effects` sweep.

#### Readers

- ***Client*** — Server-only (not `public`): clients only send the `event_id` back, no visible end. Trust model: a client-chosen (step, enemy) pair is never accepted — the step must have actually fired, from that enemy, within the TTL window, with pellets left; damage is the tick-stamped value, never client-supplied. Player-ability fan/split pellets draw from their source event's pool.
- ***Server*** — `report_hit` attests the victim's claim against the live row (unknown/expired/exhausted → `Err`), then delegates to the combat sink with the stamped damage.

### BulletPatternEvent

```sync
![[00 Table Map#^table-bullet-pattern-event{seamless:true,title:false,marker:17.}]]
```

#### Shape

Append-only record of one fired shot, self-contained so renderers never join back to defs: baked origin (`origin_x/y` = enemy position at fire time) + baked origin offset (spin-applied) + `base_angle_offset`, the full `PatternType` params, `target` (resolved aggro identity or none), texture, lifetime, `damage` (resolved at insert via `step_damage`, so damage-aware bullet controllers like SlashRect compare without subscribing step defs), mid-flight `angular_speed`/`speed_acceleration`, `source_step` ref, and `chunk_index`. Angles/offsets are baked server-side at fire time; the client renders them verbatim.

#### Changers

- **Insert** in `tick_sequence` during `tick_enemy_behavior` (one row per shot the catch-up loop emits; each insert is immediately followed by its attestation stamp).
- **Append-only: never updated or deleted** (ephemeral event table — history is the clients' live bullets, not rows).

#### Readers

- ***Client*** — Subscribed as a raw event table → `GameTables` wave → the single shared `BulletPatternEventBinder` (**replay off**, on `BulletSpawnerComponent` in `bullet_spawner_component.tscn` — bound before any enemy spawns; the code comment pins this: no last-seen-id tracking exists, so replay would re-fire history) → `BulletSpawnerComponent.SpawnEnemyBullet` dispatches per `PatternType` through the BlastBullets2D factory: Ring renders the `Line` ladder as one spawn call per stack level, Volley batches each stack level as one Count-bullet instance, Shotgun/Explosion stay one-instance-per-pellet, and `ApplyMotion` maps curve/accel to rotation/speed data (re-set per event since the spawner resource is shared). Because rows carry the absolute origin, there is no per-enemy binder fan-out and no lost volley during a puppet's deferred-spawn window. Visible end: the piercing enemy bullets themselves (tracked in `LiveEnemyBullets` with elevation band + texture id, pruned on a timer/threshold), mirrored in 3D by `Bullets3DComponent`.
- ***Server*** — No row reads after insert; the attestation row is the server-side handle.

## Files — pointer deep dive

In `server/spacetimedb/src/enemy/pointers.md` order. The pointers say what each file touches; this section says how.

### def_tables.rs

Static template data: param structs, the steering model, `PatternType`, `EnemyTarget`, `StunBehavior`, and the five def tables. Read by [[server/spacetimedb/src/combat/apply.rs|combat/apply.rs]] (`EnemyTemplate`: `defense` in the sink, phases in `compute_phase`), [[server/spacetimedb/src/status/methods.rs|status/methods.rs]] (`StunBehavior` in the stagger/stun consults), [[server/spacetimedb/src/main/admin.rs|main/admin.rs]] (the five runtime upserts) and [[server/spacetimedb/src/main/seeds.rs|main/seeds.rs]] (seed construction). Edit it only to add a primitive the content needs (new pattern, new movement axis) — never for a new enemy.

### defs/gorgon_boss.rs

The reference boss: ~30 one-fn-per-emitter builders in raw source-engine numbers (speeds × `AOTMK_SPEED_SCALE` = 25, ring counts pre-decremented for the source's inclusive-endpoint stacking, lifetimes derived by `reach()` so the fastest bullet threatens ~400 units), composed into 10 HP-gated phases of `window()`ed emitters with `PHASE_REST` = 2.0 s loop silence, then built per `Difficulty::ALL` into `GorgonBoss_Easy…Madness` (`max_hp` 750 + 250/step, `defense` 8, Pause stun). Deliberately not ported (no client systems exist): boss root movement, cinematics, minion spawns, per-emitter bullet colors (shared `"Arrow"` texture — art task), music. Read it as the recipe before authoring anything.

### defs/mod.rs

Raw row-inserting helpers (`make_phase` inserts the `MovementDef` and returns the `PhaseDef`; `steering()` terse constructor with separation + cliffs on; `make_sequence` run-once vs `make_looping_sequence` self-looping; `seq_single`/`seq_repeat`/`seq_multi`). Used by `main/seeds.rs` (`make_phase`, `make_sequence`, `seq_single`, `steering` for the `Enemy`/`Archer`/`TestBoss` seeds) and by every `defs/` content file. New enemies usually touch only this vocabulary plus `emitters.rs`.

### emitters.rs

The `Emitter` builder vocabulary + `Difficulty`/`DifficultySteps`: `ring`/`volley`/`shotgun`/`new` constructors, `spin`/`burst`/`curve`/`accel`/`line`/`offset`/`angle`/`start_delay`/`active_duration`/`window` refinements, and [[server/spacetimedb/src/enemy/emitters.rs#compile|compile]] lowering one emitter to one self-contained `AttackSequence` with difficulty deltas applied (`fire_rate`/`bullet_count`/`line_count`/`bullet_speed`/`angular_speed` scaled by `step()`; continuous → infinite or windowed-finite Repeat, burst → finite Repeat in a self-looping sequence with `angle_step = spin × pause`). Not every builder is exercised by shipped content yet (burst mode, curving) — vocabulary for contributors, not dead code. No vault code outside the folder calls into it; content files compose it.

### instance_tables.rs

All twelve runtime/event/schedule tables (see the per-table sections). Read across the module: `combat/apply.rs` (Enemy/EnemyBehavior in the kill/survive path), `player/reducers.rs` (attestation consume, both outbound event inserts), `status/methods.rs` (Enemy stagger/debuff consults), `main/admin.rs` + `main/lifecycle.rs` (schedules, spawn/despawn), `item/` (AbilityVisualKind reuse). Client scenes bind its event tables and views (`game.tscn`, `bullet_spawner_component.tscn`, `default_enemy.tscn`); `TableSubscriber.cs` subscribes the AOI/global views plus the three raw event tables.

### methods.rs

Pure simulation logic, no reducers: archetype pair (`spawn_enemy_archetype`/`despawn_enemy_archetype`, the only legal spawn/despawn path — admin, spawner, and combat all route through them), tree build/teardown (`build_enemy_behavior` copies Repeat defs to instances and stamps timers at now so fresh spawns carry no backlog; `delete_enemy_behavior` collects-before-deleting down the Behavior → Phase → Attack → Step → Instance chain), `tick_phase` (waits, self-looping sequences excluded from the all-finished restart, `begin_phase_cycle`/`activate_all_attacks` with out-of-range `attack_index` treated as inactive, every wall-clock comparison through `effective_interval`), `tick_sequence` (at most one step transition per call per attack; Single fires once, Repeat catch-up loop with spin-baked offsets, Multi whole-vec burst), `compute_phase` (highest crossed threshold wins, defensive against misordering), `apply_movement` (12-direction interest map: pattern interest via `max(0, dot)`, per-pair-angled separation, cliff probing with no-fall blocking, argmax wins so blocked directions slide instead of cancelling; returns position + heading memory + client angle/speed), and the reducer-side AOI helpers (`enemies_near_point` for abilities/spells/zones; `has_player_in_simulation_range`/`has_player_near_spawn_point`/`find_nearest_player_id`, all filtering `logged_in_player` so logged-out ghosts anchor nothing). Consumed by `combat/apply.rs` (`compute_phase`, `despawn_enemy_archetype`, `recompute_aggro`), `main/admin.rs`, `player/reducers.rs` + `status/methods.rs` (`enemies_near_point`).

### mod.rs

Module declarations only (`def_tables`, `defs`, `emitters`, `instance_tables`, `methods`, `reducers`, `views`). No logic; touch it only to register a new file.

### reducers.rs

Three reducers: `tick_enemy_spawn` (thin wrapper over `spawn_from_biome_regions`), `tick_enemy_behavior` (the 100 ms sim — sim-set gating, budget, Pause freeze, aggro, phase transitions, `tick_phase` firing with damage-stamped events + attestations, movement, delta-checked writes), and `report_enemy_hit` (self-reported but gated: `require_in_world`, `PlayerHitRate` outgoing bucket at weapon `fire_rate × shot_count × HIT_RATE_SLACK`, silent `Ok` on missing/immortal victims, wrap-aware `HIT_VALIDATION_RADIUS` check, then `deal_damage_to_enemy`). Called from the client only by `HitZone.cs` (`Scenes/Components/hit_zone.tscn`) after faction-opposed contact — it never computes damage.

### views.rs

Two views: [[server/spacetimedb/src/enemy/views.rs#nearby_enemies|nearby_enemies]] (per-caller AOI over `PlayerChunk`-derived indices with the sentinel-empty + OR-chain idioms — `u64::MAX` matches nothing when the caller has no chunk) and [[server/spacetimedb/src/enemy/views.rs#enemy_templates|enemy_templates]] (global, anonymous context). Bound in `game.tscn` (`NearbyEnemiesBinder` on the spawner), `default_enemy.tscn` (per-puppet binder), and `local_player.tscn` (`CombatComponent`'s aim binder); read directly by `Enemy.cs` (`enemy_templates`) and `CombatComponent` (`nearby_enemies`).

## Cross-table flows

**Enemy kill → despawn → XP → status purge.** `report_enemy_hit` resolves the base damage and delegates to the combat sink ([[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]]); on kill the sink calls `despawn_enemy_archetype` (flat row + whole behavior tree, no orphans), credits `internal_gain_xp` to the killer's profile, and calls `status::methods::on_enemy_killed`, which deletes both the dead enemy's `ActiveEnemyStatusEffect` rows and any player mark statuses referencing it — admin `despawn_enemy`/`despawn_all_enemies` run the same consult, so no path leaks rows pointing at a dead enemy. Survivors get `compute_phase` + re-aggro onto the attacker instead. Per-table details live under [[#Enemy]], [[#EnemyBehavior]], and [[#BulletFireAttestation]] (victim-side attestation for incoming fire).

## Known gaps / stubs

- **Aspirational bullet-despawn design stays out.** The old `BulletDespawnEvent`/`slash_bullet` plan is referenced nowhere in the code (verified by grep — the only mention of the removed design is historical); the implemented protocol is `BulletControlEvent` + the `SlashRect` threshold encoding. Where this doc touches that edge it says so explicitly.
- **Multi steps have live code but no content.** `tick_sequence`'s Multi arm, `pattern_bullet_count`'s Multi accounting, and the `MAX_MULTI_SHOTS` cap are all exercised only via the admin `upsert_multi_step_def` path — `seq_multi`/`multi_shot` are `#[allow(dead_code)]` and no shipped enemy (seeds, Gorgon) fires a Multi step.
- **Unexercised emitter vocabulary.** `burst` mode and `curve` ship in `emitters.rs` but no seed calls them (the source's one curving emitter is never activated by any phase clip); they are authoring vocabulary awaiting content, not stubs.
- **Gorgon placeholder art + unported fight elements.** The boss seeds `texture_id: "Enemy"` (art task), and root movement, cinematics, minion spawns, and per-emitter colors were deliberately not ported (documented at the top of `gorgon_boss.rs`).
- **Biome spawning starts disarmed.** `init` inserts no `EnemySpawnSchedule` row, so a fresh publish spawns nothing until an admin calls `toggle_enemy_spawning(true)` — the debugging default, stated in `lifecycle.rs`.
- **Ghost positions are guarded, not cleaned.** `PlayerPosition` rows outlive logout; the sim, spawner, and aggro paths all filter through `logged_in_player` instead. The cleanup gap belongs to the Player doc (^table-player-position); this system only relies on the guard.

## Where to go next

Read [[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]] for the damage math both hit reducers delegate to, then the Status doc (^table-active-enemy-status-effect, stagger consults) and the World doc (^table-biome-region, spawn regions and hex math).
