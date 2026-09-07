# 07 Enemy Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for the archetype helpers, [[03 Connection, Subscriptions & Views]] for AOI views and the event-table pattern, [[06 Combat Tables]] for the damage sink that kills enemies and advances phases. The tick/phase/sequence semantics, event field encodings, and add-an-enemy recipe below are the narrative companion to [[server/spacetimedb/src/enemy/AGENTS.md|enemy AGENTS.md]] — the canonical reference for this module.

## The 30-second version

Template = data: static `EnemyTemplate` rows (phases of attack sequences of step defs, plus per-phase steering movement) are authored in `defs/` through the `Emitter` vocabulary and seeded per difficulty tier. At runtime `spawn_enemy_archetype` builds a live behavior tree (`EnemyBehavior` → `EnemyPhase` → `EnemyAttack` → `EnemySequenceStep` → per-enemy `RepeatStepInstance`) under one flat `Enemy` row, and the 100 ms `tick_enemy_behavior` walks only the chunk-gated simulation set — ticking each candidate's phases into `BulletPatternEvent` volleys (+ `BulletFireAttestation` rows) and steering movement — while clients render events verbatim and mirror the `Enemy` row onto puppets through the `nearby_enemies` view.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do I add a new enemy? | `EnemyTemplate` | [[#EnemyTemplate]] — `defs/` file + `seed`, compile emitters, spawn via admin |
| How do bullet patterns get authored? | `SingleStepDef` / `RepeatStepDef` / `MultiStepDef` | [[#SingleStepDef]], [[#RepeatStepDef]], [[#MultiStepDef]] — `Emitter` vocabulary + step-def rows |
| What does `repeat_target: 0` mean? | `RepeatStepDef` | [[#RepeatStepDef]] — fire forever; the sequence never advances |
| When does a phase loop / advance? | `EnemyPhase` | [[#EnemyPhase]] — restarts when EVERY sequence finishes; HP thresholds via combat |
| What is `loop_delay` on a sequence? | `EnemyAttack` | [[#EnemyAttack]] — ≥ 0 self-restarts, excluded from the phase restart check |
| How does enemy movement work? | `MovementDef` | [[#MovementDef]] — context steering: Goal × Pattern × Avoidance |
| How do volleys reach the screen? | `BulletPatternEvent` | [[#BulletPatternEvent]] — tick appends, shared binder renders verbatim |
| How are player bullet-controller casts relayed? | `BulletControlEvent` | [[#BulletControlEvent]] — validated append, every client resolves locally |
| What are the colored spell flashes? | `AbilityVisualEvent` | [[#AbilityVisualEvent]] — visual-only relay of P5 casts |
| How is a victim's hit claim verified? | `BulletFireAttestation` | [[#BulletFireAttestation]] — pellet pool + TTL behind `report_hit` |
| How is a player's hit on an enemy verified? | `Enemy` | [[#Enemy]] — `report_enemy_hit` token bucket + range check |
| Where is per-enemy tick state? | `EnemyBehavior` etc. | [[#EnemyBehavior]], [[#EnemyPhase]], [[#EnemyAttack]], [[#EnemySequenceStep]], [[#RepeatStepInstance]] |
| How does biome spawning work? | `EnemySpawnSchedule` | [[#EnemySpawnSchedule]] — disarmed at init, 2 s tick when re-armed |
| What do clients subscribe to? | `Enemy` / `EnemyTemplate` | [[#Enemy]] — `nearby_enemies` + `enemy_templates` views |

## Flowcharts

- [[flowcharts/main-enemy.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/enemy_subfolder/enemy_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Players_subfolder/Enemies_subfolder/Enemies_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/default_enemy_codefile/default_enemy_codefile.canvas]]

## Table changes

```sync
![[00 Table Map#^table-movement-def{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-single-step-def{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-repeat-step-def{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 Table Map#^table-multi-step-def{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 Table Map#^table-enemy-template{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 Table Map#^table-repeat-step-instance{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 Table Map#^table-enemy-sequence-step{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 Table Map#^table-enemy-attack{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 Table Map#^table-enemy-phase{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 Table Map#^table-enemy-behavior{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 Table Map#^table-enemy{seamless:true,title:false,marker:11.}]]
```

```sync
![[00 Table Map#^table-enemy-spawn-schedule{seamless:true,title:false,marker:12.}]]
```

```sync
![[00 Table Map#^table-enemy-behavior-schedule{seamless:true,title:false,marker:13.}]]
```

```sync
![[00 Table Map#^table-bullet-control-event{seamless:true,title:false,marker:14.}]]
```

```sync
![[00 Table Map#^table-ability-visual-event{seamless:true,title:false,marker:15.}]]
```

```sync
![[00 Table Map#^table-bullet-fire-attestation{seamless:true,title:false,marker:16.}]]
```

```sync
![[00 Table Map#^table-bullet-pattern-event{seamless:true,title:false,marker:17.}]]
```

(The entries above are the complete changer/reader reference. The sections below add Shape plus the tick-level mechanics the entries summarize.)

## MovementDef

### Shape

One row per authored steering block: a `SteeringMovement` = **Goal × Pattern × Avoidance** — `MoveGoal` (Idle / AggroTarget / SpawnPoint / FixedPoint), `MovePattern` (Direct / Flee / Orbit / Meander / Zigzag / Lunge with per-pattern params), `speed`, `separation_weight`, `avoid_cliffs`, and an optional move/pause cycle (`active_duration` / `pause_duration` off `move_cycle_started_at`). All movement-layer angles are **radians**; bullet angles stay degrees.

### Changers

Content seeds plus `upsert_movement_def` (see entry 01 above) — the simulation never rewrites definitions.

### Readers

- ***Client*** — none (`public` but unsubscribed).
- ***Server*** — `apply_movement` in [[server/spacetimedb/src/enemy/methods.rs#apply_movement|enemy/methods.rs]] scores 12 fixed `STEERING_DIRECTIONS` candidates per tick in an interest map (`max(0, dot(candidate, desired))` per pattern, shaped separation with the anti-jitter angle offset, cliff probing ~1.5 hexes ahead against `MAX_WALKABLE_*_DELTA` — steep down blocked too, enemies have no fall mechanic) and moves along the argmax, so blocked directions slide along cliffs instead of cancelling out. `velocity_x/y` on the behavior row is the unit heading memory (Meander's persistent sway heading); the client-facing angle+scalar lives on the `Enemy` row.

## SingleStepDef

### Shape

One fire-once step: pattern params + target (`Idle` / `AggroTarget`), origin offset, `base_angle_offset`, texture, lifetime, damage, mid-flight `angular_speed` / `speed_acceleration`, and `next_step_delay`. `line`/`line_speed_step` on the pattern params is a speed ladder (bullet j flies at `speed + step × (j+1)`), not spatial copies.

### Changers

Content seeds plus `upsert_single_step_def` (entry 02 above).

### Readers

- ***Client*** — none (`public` but unsubscribed).
- ***Server*** — `tick_sequence` fires the step once per visit, then waits `next_step_delay`.

## RepeatStepDef

### Shape

A repeating step: everything on `SingleStepDef` plus `repeat_interval`, `repeat_target`, and `angle_step` — which rotates each successive shot (`base + step × repeat_count`) *and* rotates the origin offset by the same accumulated spin (orbiting-arm model for spirals). **`repeat_target: 0` means fire forever**, never advancing — the right shape for continuous emitters in HP-driven phases, and the reason a phase with an infinite emitter never loops.

### Changers

Content seeds plus `upsert_repeat_step_def` (entry 03 above).

### Readers

- ***Client*** — none (`public` but unsubscribed).
- ***Server*** — copied per-enemy into ^table-repeat-step-instance at build; `tick_sequence` fires with catch-up (intervals < 100 ms fire multiple shots per tick, capped at `MAX_CATCHUP_SHOTS` = 8, ~80 shots/s ceiling; `step_timer` advances by whole intervals so no drift), clamped to `now − BACKLOG_CAP × interval` after out-of-sim gaps so re-entry fires at most a ~2-shot burst.

## MultiStepDef

### Shape

A fan-out step: a whole `shots` vec fired at once plus `next_step_delay`. Content-sized with no pacing of its own — one authored mega-Multi is a row-insert storm (event + attestation per shot, per enemy, per tick), which is why `MAX_MULTI_SHOTS` (24) caps each visit and no shipped content uses Multi.

### Changers

Content seeds plus `upsert_multi_step_def` (entry 04 above).

### Readers

- ***Client*** — none (`public` but unsubscribed).
- ***Server*** — `tick_sequence` emits all shots in one visit.

## EnemyTemplate

### Shape

The static enemy: string `template_id` primary key (difficulty variants are separate rows — `Foo_Easy` … `Foo_Madness`, no runtime flag), catalog `texture_id`, `max_hp`, flat per-bullet `defense`, `move_sim_factor` / `attack_sim_factor` (multiples of `CAMERA_VIEW_RADIUS`, so all templates rescale if the camera ever changes), `aggro_lock_seconds`, `stun_behavior` (all current seeds Pause), and `phases: Vec<PhaseDef>` — each phase an hp threshold + movement def id + loop delay + concurrent attack sequences.

### Changers

`seed_default_enemies` + per-enemy `seed(ctx)` fns upserting by `template_id` (entry 05 above); `upsert_enemy_template` inserts-or-updates at runtime.

### Readers

- ***Client*** — via the global `enemy_templates` view in the Game wave → `Enemy.cs` max-hp mirror + catalog 3D model (see entry 05 above).
- ***Server*** — `spawn_enemy_archetype` compiles phases into the behavior tree; the combat sink recomputes the live phase via `compute_phase`; the behavior tick gates movement on `move_sim_factor` and firing on `attack_sim_factor` (enemies approach before they ever shoot from off-screen).

To add an enemy: new file `defs/my_enemy.rs` (one fn per emitter returning a base `Emitter`, phases via [[server/spacetimedb/src/enemy/defs/mod.rs#make_phase|make_phase]] with `steering(...)` or `SteeringMovement::idle()`), `pub mod` in `defs/mod.rs`, seed call in `init`, `cargo check` → `server/build.sh`, spawn with admin `spawn_enemy`, inspect with `spacetime sql`. Never hand-unroll steps, never insert behavior rows directly, never mix infinite emitters with an expected phase loop.

## RepeatStepInstance

### Shape

The per-enemy mutable copy of a `RepeatStepDef` (same fields plus `repeat_count`), so parallel enemies advance independently.

### Changers

Built by `spawn_enemy_archetype`, advanced by `tick_sequence`, destroyed by `despawn_enemy_archetype` (entry 06 above).

### Readers

- ***Client*** — none (`public` but unsubscribed — tick state).
- ***Server*** — `tick_sequence` only.

## EnemySequenceStep

### Shape

One compiled step instance: parent `attack_id` (btree), `step_index`, and the `SequenceStepRef` (Single id / Repeat id / Multi ref + shot index) pointing back at the def or live copy.

### Changers

Built at spawn; position advances on the parent attack row, never here; destroyed at despawn (entry 07 above).

### Readers

- ***Client*** — none (`public` but unsubscribed — tick state).
- ***Server*** — `tick_sequence` walks steps in order.

## EnemyAttack

### Shape

One live attack sequence: parent `phase_id` (btree), `attack_index`, `step_index`, `step_timer`, and three pacing flags — `step_waiting`, `start_delaying`, and `loop_waiting` (true while a `loop_delay >= 0` sequence waits out its self-restart; `step_timer` doubles as the loop timer). `loop_delay < 0` runs once per phase cycle (default); `>= 0` self-restarts after that delay and is **excluded from the phase-level restart check** — burst cadence (`Emitter::burst` compiles to it).

### Changers

Built at spawn; `tick_sequence` advances timers/flags every behavior tick; destroyed at despawn (entry 08 above).

### Readers

- ***Client*** — none (`public` but unsubscribed — tick state).
- ***Server*** — `tick_phase` / `tick_sequence` only; phase transition resets sequences via `begin_phase_cycle` when `enemy.phase != active_phase_index`.

## EnemyPhase

### Shape

One live phase: parent `behavior_id` (btree), `phase_index`, `hp_threshold`, `movement_def_id`, `phase_loop_delay`, `phase_waiting` / `phase_loop_timer` (the silence between loops — also the rest after an HP-threshold change), and `move_cycle_started_at` for move/pause cycles.

### Changers

Built at spawn (one row per `PhaseDef`); `tick_phase` advances loop timers; destroyed at despawn (entry 09 above).

### Readers

- ***Client*** — none (`public` but unsubscribed — tick state).
- ***Server*** — `tick_phase` only. A phase restarts only when EVERY sequence finishes — windowed (finite) emitters are what make a phase loop.

## EnemyBehavior

### Shape

One live behavior root per enemy: `aggro_target` / `aggro_locked_until`, `was_simulating` (re-entry retarget flag), `active_phase_index`, and the server-only `velocity_x/y` heading memory. The client never sees this row — its movement lives on the `Enemy` row's angle+scalar pair.

### Changers

Built at spawn; the behavior tick updates aggro/locks/phase index/heading with delta-checked writes (entry 10 above — rows are only rewritten when a field actually changed).

### Readers

- ***Client*** — none in practice (`public` but unsubscribed).
- ***Server*** — the behavior tick (sim gating, Pause-stun freeze, retarget on sim re-entry and on stale targets — logged-out/dead/out-of-range — instead of chasing ghosts) and `apply_movement`.

## Enemy

### Shape

The flat live row: `enemy_id`, `template_id`, `behavior_id`, position + `spawn_x/y` leash anchor, movement as angle+scalar (`movement_direction` radians + `movement_speed`, 0 when standing — the `PlayerPosition` convention; `Enemy.cs` reconstructs velocity for `InterpolationComponent.SetTarget`), `spawn_time`, `hp`, `phase`, `is_elite` / `immortal`, the 0–100 `stagger` bar, and btree `chunk_index`.

### Changers

Spawned by `spawn_enemy_archetype` via the scheduled `tick_enemy_spawn` (biome regions, disarmed at `init` until `toggle_enemy_spawning(true)`) or admin `spawn_enemy`; updated by the behavior tick (position/movement/phase) and the combat sink (hp, phase, aggro) with stagger fills from bashes and decay on the 1 Hz status tick; destroyed by `despawn_enemy_archetype` on kill (+ XP), admin `despawn_enemy` / `despawn_all_enemies` (no XP), always with `on_enemy_killed` (entry 11 above).

### Readers

- ***Client*** — via the `nearby_enemies` AOI view in the Game wave → `EntitySpawnerComponent` → `default_enemy.tscn` puppets; `CombatComponent` also reads the view for nearest-enemy invalidation (lock-on), and `BulletSpawnerComponent` reads `local_player_position` + `nearby_remote_players` alongside events for band math (see entries above).
- ***Server*** — the chunk-gated simulation set (`SIMULATION_CHUNK_RINGS` of logged-in players' `PlayerChunk`s, capped at `BEHAVIOR_TICK_ENEMY_BUDGET` = 96/tick with lowest ids winning and overflow logged); `report_enemy_hit` is self-reported but gated — `PlayerHitRate.outgoing` token bucket at weapon fire_rate × shot_count × `HIT_RATE_SLACK` plus a wrap-aware `HIT_VALIDATION_RADIUS` check — and `immortal` rows accept the call as a no-op (see [[server/spacetimedb/src/enemy/reducers.rs#report_enemy_hit|report_enemy_hit]], called from `HitZone` in [[client/Scenes/Components/hit_zone.tscn|hit_zone.tscn]]); `enemies_near_point` serves AoE/zone targeting.

## EnemySpawnSchedule

### Shape

Scheduled-job row driving `tick_enemy_spawn` every 2 s. Its only changer is whoever arms/disarms it: `init` inserts nothing (spawning starts disarmed for debugging); `toggle_enemy_spawning(true/false)` inserts/deletes (entry 12 above). Global cap `MAX_LIVE_ENEMIES` (64) plus `MAX_SPAWNS_PER_TICK` (3) bound per-tick insert pressure — each spawn fans out a full behavior tree.

### Readers

- ***Client*** — none (not `public`).
- ***Server*** — the scheduler fires `spawn_from_biome_regions` per row (spawn points inside `BiomeRegion` rows with players in `SPAWN_LOOKAHEAD_CHUNK_RINGS`).

## EnemyBehaviorSchedule

### Shape

Scheduled-job row driving `tick_enemy_behavior` every 100 ms (`BEHAVIOR_TICK_DT`), armed once by `init`, never disarmed (entry 13 above).

### Readers

- ***Client*** — none (not `public`).
- ***Server*** — the 10 Hz simulation described under [[#Enemy]].

## BulletControlEvent

### Shape

Validated relay of a player's bullet-controller cast: `kind` (`Delete` / `Split` / `Attract` / rect variants / `SlashRect` / `WallRect`), center or origin + far end + width/radius encoding per kind (documented on the table), `duration` dual-used (Attract/Wall linger; SlashRect damage threshold as f32 bits), and `cast_by` for echo-skipping. The server tracks no live bullets.

### Changers

Appended only by `activate_ability` → `apply_ability_effect` with full ability validation — there is deliberately no `control_bullets` reducer (entry 14 above). Never updated or deleted (ephemeral event table).

### Readers

- ***Client*** — raw `public event` table, Game wave, replay OFF → `BulletControllerComponent`'s binder → local proximity resolution against `LiveEnemyBullets` (+ slash flash polygon); caster applies optimistically and skips its own echo.
- ***Server*** — none.

## AbilityVisualEvent

### Shape

Visual-only relay of a P5 Spell cast: `kind` (Circle / Line / Cone / Meteor / Converge — the last two are visual-only variants of the Circle shape), origin + center-or-far-end + radius-or-radians encoding per kind, `duration` = `SPELL_VISUAL_SECONDS`, `cast_by` for echo-skipping. Damage is applied server-side in the same dispatch; this row only draws the flash.

### Changers

Appended by the Spell arm of `apply_ability_effect` (entry 15 above). Never updated or deleted.

### Readers

- ***Client*** — raw `public event` table, Game wave, replay OFF → `SpellVisualComponent`'s binder → per-kind Polygon2D flash fading after the duration; caster renders optimistically via the same `Render` and skips its own echo.
- ***Server*** — none.

## BulletFireAttestation

### Shape

Server-only proof that a volley was really fired: `event_id` primary key matching the `BulletPatternEvent`, `enemy_id`, stamped `damage`, `remaining` pellet pool, `fired_at`. One row per fired event — missed volleys are never claimed, so the TTL sweep is what bounds the table.

### Changers

Stamped by `attest_bullet_fire` at fire time; decremented per accepted `report_hit` (replaying one `event_id` past its pellets is rejected); deleted on acceptance or past `BULLET_ATTESTATION_TTL_SECONDS` (30 s) by the 1 Hz tick (entry 16 above).

### Readers

- ***Client*** — none (not `public`; clients only send the `event_id` back).
- ***Server*** — `report_hit` attests every victim claim (pellet-pool decrement + TTL) before delegating to the damage sink with the stamped damage — so clients never resolve step defs themselves.

## BulletPatternEvent

### Shape

One fired shot: `enemy_id`, `source_step` ref, stamped `damage` (so slash controllers and attestation never read step defs), absolute `origin_x/y` + baked `origin_offset_*` + `base_angle_offset`, full `pattern_type` params, optional targeted `target` (aggro identity resolved via `resolve_step_target`), texture, lifetime, mid-flight `angular_speed` / `speed_acceleration`, and `chunk_index`.

### Changers

Appended once per shot by the behavior tick (`tick_phase` → insert + `attest_bullet_fire` in the same pass — see [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]]), with damage resolved by `step_damage` at insert (entry 17 above). Never updated or deleted.

### Readers

- ***Client*** — raw `public event` table, Game wave, replay OFF → ONE shared `BulletPatternEventBinder` on `BulletSpawnerComponent` (declared in [[client/Scenes/Components/Bullets/bullet_spawner_component.tscn|bullet_spawner_component.tscn]], bound before any enemy spawns; rows self-contained, so no per-enemy fan-out and no lost volley during deferred spawn) → per-`PatternType` BlastBullets2D dispatch (Ring speed-ladder one call per stack level; Volley one batched instance per level; Shotgun/Explosion one instance per pellet; `ApplyMotion` maps curve/accel; piercing pinned so off-band overlaps survive for the router's manual disable). Visible end: the bullet hell itself, plus the 3D mirror.
- ***Server*** — none beyond the attestation writer above.

## Files — pointer deep dive

Entry points: [[server/spacetimedb/src/enemy/pointers.md|enemy pointers]].

### def_tables.rs

Static template data: step-def tables, `MovementDef` + the steering model, `PatternType` + param structs, `AttackSequence` / `PhaseDef` / `SequenceStepDef`, `EnemyTemplate`. Edit for new primitives the content needs (a field/pattern), never for new enemies. Read by combat (`EnemyTemplate`), admin (upserts), seeds (content vocabulary), status (`StunBehavior`).

### defs/gorgon_boss.rs

Reference enemy content: per-emitter fns + `seed(ctx)` building tiered templates. The recipe target — usually the only file an enemy change touches.

### defs/mod.rs

Raw step helpers (`seq_single`, …) + `make_phase` / `make_sequence` / `steering` constructors + module declarations. Used by `main/seeds.rs` for the `TestBoss` showcase (the second content example).

### emitters.rs

The authoring vocabulary: `Emitter` builder (`shotgun`, `burst`, `window`, …) + `Difficulty`/`DifficultySteps`; `compile()` lowers an emitter to an `AttackSequence`. Extend here when the model can't express a pattern (like `angle_step`/`loop_delay` were added) instead of hand-unrolling steps.

### instance_tables.rs

Runtime rows + schedules + event tables + attestation (see all entries above). Rarely edited — schema changes only. Consumed client-side by the spawner/bullet/spell binders and server-side by combat, player reducers (event appends, attestation reads), status (enemy reads/writes), and admin/lifecycle (schedules, spawn rows).

### methods.rs

Pure simulation logic: archetype helpers, behavior-tree build/teardown, `tick_phase` / `tick_sequence`, `apply_movement`, `spawn_from_biome_regions`, `compute_phase` / `recompute_aggro` / `enemies_near_point` / `step_damage` / `resolve_step_target` / `begin_phase_cycle` / `attest_bullet_fire`. Change *how* enemies simulate here. Consumed by combat (phase/aggro/despawn), admin (spawn/despawn), player + status (`enemies_near_point`).

### mod.rs

Module declarations only.

### reducers.rs

Wiring, not content: the two scheduled ticks plus `report_enemy_hit` (rate-limited, range-checked, immortal-tolerant). Called from `HitZone` ([[client/Scenes/Components/hit_zone.tscn|hit_zone.tscn]]) — player bullet hits on enemy victims are reported on `HitZone` expiry through faction-opposed `DamageReceivingComponent`s, never computed locally.

### views.rs

`nearby_enemies` (AOI chunk OR-chain over `nearby_indices()`, sentinel-empty) + `enemy_templates` (global). Subscribed in the Game wave; bound by `EntitySpawnerComponent` (`NearbyEnemies` → puppets), `Enemy.cs` (`enemy_templates` → max hp), and `CombatComponent` (`nearby_enemies` → nearest-enemy invalidation).

## Cross-table flows

The volley pipeline is the module's spanning mechanism: defs compile to template rows at seed → spawn builds the behavior tree → the behavior tick appends `BulletPatternEvent` + `BulletFireAttestation` → clients render verbatim → victim `report_hit` attests and lands damage through the combat sink (kills despawn + award XP + advance phases). The behavior-tree side tables (`RepeatStepInstance` … `EnemyBehavior`) never leave the server; the event tables never persist. Each link is documented on its own entry above.

## Where to go next

[[08 Item Tables]] covers the catalog whose weapons report the hits this sim consumes. For the enemy-side debuffs and stagger that gate this tick, read `11 Status Tables`.
