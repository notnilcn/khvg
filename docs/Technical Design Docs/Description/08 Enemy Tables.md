# 08 Enemy Tables

## Assumed knowledge

- [[02 The Component Framework]] — the client's entity/component model and the `TableBinderComponent` pattern every row consumer uses.
- [[03 Connection, Subscriptions & Views]] — reducers, views, the base/lobby/game subscription waves, and why event tables get `ReplayExistingRows = false`.
- [[06 Movement & AOI Tables]] — chunk indices, the torus wrap, and the `nearby_indices` AOI helpers the enemy views build on. [[07 Terrain & World Tables]] owns `BiomeRegion` (^table-biome-region), which doubles as the spawn-zone table here.

## The 30-second version

Enemies are **template + instance**. The static content layer is `EnemyTemplate` (HP, defence, sim radii, and an inline list of `PhaseDef`s) plus four def tables (`MovementDef`, `SingleStepDef`, `RepeatStepDef`, `MultiStepDef`) that phases reference by id — all seeded at publish time and upsertable by admins. The runtime layer is the flat `Enemy` row (position, hp, phase, stagger, chunk) plus a five-table behavior tree (`EnemyBehavior` → `EnemyPhase` → `EnemyAttack` → `EnemySequenceStep` → `RepeatStepInstance`) that mirrors the client's component model: one table per concern, `enemy_id`/`behavior_id` as the entity id. A 100 ms scheduled reducer (`tick_enemy_behavior`, armed by `EnemyBehaviorSchedule` at `init`) full-scans `Enemy`, simulates every in-range enemy — movement, aggro, attack sequences — and appends one `BulletPatternEvent` per shot; a second, 2 s job (`tick_enemy_spawn` via `EnemySpawnSchedule`, deliberately disarmed until an admin toggles it) spawns enemies from `BiomeRegion` zones. Combat writes `Enemy.hp`/`phase`; kills despawn through the archetype helper. Clients read exactly two things: the AOI-filtered `nearby_enemies` view and the global `enemy_templates` view, both in the `GameTables` wave — the behavior tree and step defs are `public` but deliberately unsubscribed, because `BulletPatternEvent` carries every resolved parameter the client needs.

## Flowcharts

- [[flowcharts/main-enemies.canvas]] — this system's composed flow (server `enemy/` + `world/` spawn zones + `main/` lifecycle/admin, and the client puppet/spawner).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/enemy_subfolder/enemy_subfolder.canvas]] — the whole server enemy module (defs, instances, tick, emitters).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Players_subfolder/Enemies_subfolder/Enemies_subfolder.canvas]] — `Enemy.cs`, the client puppet.
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/default_enemy_codefile/default_enemy_codefile.canvas]] — `default_enemy.tscn`, where the puppet's binder wiring lives.

## Table changes

```sync
![[00 Table Map#^table-enemy{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-enemy-behavior{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-enemy-phase{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 Table Map#^table-enemy-attack{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 Table Map#^table-enemy-sequence-step{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 Table Map#^table-repeat-step-instance{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 Table Map#^table-enemy-template{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 Table Map#^table-movement-def{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 Table Map#^table-single-step-def{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 Table Map#^table-repeat-step-def{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 Table Map#^table-multi-step-def{seamless:true,title:false,marker:11.}]]
```

```sync
![[00 Table Map#^table-enemy-spawn-schedule{seamless:true,title:false,marker:12.}]]
```

```sync
![[00 Table Map#^table-enemy-behavior-schedule{seamless:true,title:false,marker:13.}]]
```

## Main body

### Two layers: content defs and runtime instances

The enemy module is split the way a game usually splits "data" from "save state", except both halves are SpacetimeDB tables. `enemy/def_tables.rs` holds the content: [[server/spacetimedb/src/enemy/def_tables.rs#EnemyTemplate|EnemyTemplate]] is keyed by a human-readable `template_id` string (not an auto-increment id) because spawn sites name their enemies (`GorgonBoss_Hard`), and its `phases: Vec<PhaseDef>` embeds the whole fight inline on the row — the same `Vec<enum>`-on-one-row shape `Item.behaviors` uses (see [[10 Items, Inventory & Loot Tables]]). A `PhaseDef` references its movement by `movement_def_id` and its attacks by `SequenceStepDef` enum ids into the three step-def tables, because step defs are *shared* — ten Gorgon phases that fire the same ring point at one `RepeatStepDef` row instead of ten copies.

`enemy/instance_tables.rs` holds what exists in the world right now. The flat `Enemy` row carries everything the client renders — position, `hp`, `phase`, `stagger`, `chunk_index` — deliberately flat (per [[server/AGENTS.md]]) so the common read path never joins. Everything the client *doesn't* render — aggro target and lock, per-phase timers, per-sequence step indices, per-step repeat counters — lives in the behavior tree, where each level points down by foreign key: `EnemyBehavior.behavior_id` ← `EnemyPhase.behavior_id` ← `EnemyAttack.phase_id` ← `EnemySequenceStep.attack_id` → a `SequenceStepRef` into a def table or a `RepeatStepInstance`. This is the server-side mirror of the client's component framework from [[02 The Component Framework]]: one table per concern = one component, and the archetype helpers [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype|spawn_enemy_archetype]] / [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] are the only sanctioned spawn/despawn paths *because* they keep the tree complete — build inserts every level, teardown's `delete_enemy_behavior` collects each level before deleting (deleting rows while iterating a btree filter skips entries) so no orphan `EnemyAttack` can outlive its `Enemy`.

`RepeatStepInstance` exists for one reason: `repeat_count` is runtime state, but `RepeatStepDef` is shared content. Copying the def per enemy at spawn ([[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]]) lets two Gorgons run the same spiral emitter with independent spin phases — `angle_step × repeat_count` is what rotates each successive shot (and orbits the origin offset by the same angle, an emitter riding a spinning arm), so a shared counter would synchronize every enemy's emitter.

### Authoring: seeds, emitters, compile-time difficulty

New content is written in `enemy/defs/` against two vocabularies. The raw helpers in [[server/spacetimedb/src/enemy/defs/mod.rs#make_phase|defs/mod.rs]] — `make_phase` (inserts the phase's `MovementDef` and returns the `PhaseDef` pointing at it), `make_sequence`/`make_looping_sequence`, `seq_single`/`seq_repeat`/`seq_multi` (each inserts its def row and returns the enum reference) — are what `main/seeds.rs`'s [[server/spacetimedb/src/main/seeds.rs#seed_default_enemies|seed_default_enemies]] uses for the basic `Enemy`/`Archer` and the `TestBoss` showcase (one phase per movement behavior × pattern family). The higher-level vocabulary is the `Emitter` builder in [[server/spacetimedb/src/enemy/emitters.rs#Emitter|emitters.rs]] — a pattern fired at a rate, with optional spin, burst cadence, curving/acceleration, and per-difficulty deltas — whose [[server/spacetimedb/src/enemy/emitters.rs#compile|compile]] lowers one emitter to a self-contained `AttackSequence` at seed time: continuous emitters become an infinite `Repeat` step (`repeat_target: 0` = fire forever), burst emitters a finite `Repeat` inside a self-looping sequence. [[server/spacetimedb/src/enemy/defs/gorgon_boss.rs#seed|gorgon_boss.rs]] is the reference boss — ten HP-gated phases ported from a decompiled DanmakU boss, with per-emitter activation windows mapping to `window(start, duration)`.

Difficulty is **compile-time**, not a runtime flag: `Difficulty::ALL` iterates Easy/Normal/Hard/Madness, each tier adds its `DifficultySteps` deltas at `compile`, and the result is seeded as four separate template rows (`Foo_Easy` … `Foo_Madness`) — spawn picks the tier by name. All template writes are upserts by `template_id` via `impl Seed for EnemyTemplate` ([[server/spacetimedb/src/main/seeds.rs#seed#3|main/seeds.rs]]), so re-publishing reseeds cleanly after the database wipe; the same upsert is exposed live to admins as [[server/spacetimedb/src/main/admin.rs#upsert_enemy_template|upsert_enemy_template]], with `upsert_movement_def`/`upsert_single_step_def`/`upsert_repeat_step_def`/`upsert_multi_step_def` for the def tables (`def_id: 0` inserts). Both seed paths run from [[server/spacetimedb/src/main/lifecycle.rs#init|init]].

### The 100 ms behavior tick

`init` arms one `EnemyBehaviorSchedule` row with a 100 ms interval, and the runtime calls [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] forever (there is no disarm path — the tick always runs; only *spawning* is gated). Each call full-scans `Enemy` — fine at co-op scale, and the scan is cheap because per-enemy work early-outs in layers:

1. **Sim range.** `has_player_in_simulation_range` checks the enemy's surrounding chunks (`SIMULATION_CHUNK_RINGS`) for a `PlayerPosition` row within `move_sim_factor × CAMERA_VIEW_RADIUS` ([[server/spacetimedb/src/main/global.rs|main/global.rs]] — sim radii are multiples of the camera view, so content survives viewport changes). Two guards hide in that check: it semijoins `logged_in_player` *because* `PlayerPosition` rows outlive logout (the "ghost" rows of [[06 Movement & AOI Tables]]), and it uses `wrapped_distance_sq` *because* the world is a torus — a player just across the lap seam is near, not a map-width away. An enemy with no player in move range flips `was_simulating` off and skips the tick.
2. **Stun.** A `Stunned` debuff on a `Pause`-behavior template skips the enemy wholesale (all current seeds are Pause); `Slowed`/`Enraged` instead scale movement in `apply_movement` and fire pacing through `effective_interval` — every wall-clock comparison in the tick goes through it *because* an Enraged stun divides every interval by `1 + magnitude`.
3. **Aggro.** Re-entering sim range, or the current target going stale (logged out or out of range), triggers `recompute_aggro` — nearest logged-in player by wrapped distance, locked for the template's `aggro_lock_seconds`.
4. **Phase transition.** `enemy.phase` is written by the combat path, not the tick; the tick detects `enemy.phase != active_phase_index` and calls [[server/spacetimedb/src/enemy/methods.rs#begin_phase_cycle|begin_phase_cycle]] to reset the new phase's sequences (and rest the fight for `phase_loop_delay` seconds — also the silence between phase loops).
5. **Fire.** Only with a player inside `attack_sim_factor × CAMERA_VIEW_RADIUS` (smaller than move range, so enemies approach before they shoot and never fire from off-screen) does [[server/spacetimedb/src/enemy/methods.rs#tick_phase|tick_phase]] run; each `BulletStepEvent` it returns is inserted as a `BulletPatternEvent` with the absolute origin stamped (`origin_x/y` = the enemy's position, offset/angle already baked), `damage` resolved server-side via [[server/spacetimedb/src/enemy/methods.rs#step_damage|step_damage]] (the same function `report_hit` uses, so damage-aware bullet controllers can compare without subscribing step defs), and `chunk_index` copied from the enemy for AOI.
6. **Move.** [[server/spacetimedb/src/enemy/methods.rs#apply_movement|apply_movement]] reads the phase's `MovementDef`: Stationary is a no-op; Wander picks a deterministic direction per cycle from `splitmix64(enemy_id + cycle_index)` (reproducible without storing state); Chase/Flee walk toward/away from the *nearest lap copy* of the aggro target (raw deltas point the long way around on a torus). Displacement is scaled by the enemy-status movement multiplier, wrapped back into the map, and the chunk index recomputed via `spiral_chunk_index`.

Every row write at the end of the tick is **delta-checked** — the reducer snapshots each row's mutable fields up front and skips the update when nothing changed, because a no-op `update` still fires subscription traffic to every client in range.

### Phase, sequence, and step pacing

Inside `tick_phase`, all of a phase's attack sequences run **concurrently** and advance independently; the phase loop restarts only when *every* sequence with `loop_delay < 0` has exhausted its steps. Two cadence knobs fall out of that rule:

- `loop_delay >= 0` on an `AttackSequence` makes it self-restarting after that delay, excluded from the phase-level restart check — a burst emitter must not gate, or be gated by, its siblings. `Emitter::burst` compiles to exactly this.
- `repeat_target: 0` on a `Repeat` step fires forever, so its sequence never finishes — infinite emitters are the right shape for HP-driven phases, and windowed (finite) emitters are what let a phase loop at all.

[[server/spacetimedb/src/enemy/methods.rs#tick_sequence|tick_sequence]] persists step progress on the `EnemyAttack` row (`step_index`, `step_timer`, `step_waiting`, `start_delaying`, `loop_waiting`) between ticks and advances at most one step transition per call. Because the tick is 100 ms (`BEHAVIOR_TICK_DT`) but `repeat_interval` can be far shorter, Repeat steps **catch up**: the fire loop burns through missed intervals, advancing `step_timer` by whole intervals so there's no drift, capped at `MAX_CATCHUP_SHOTS` (8) per tick. The cap alone would still dump 8 shots every tick after a long stall, so `step_timer` is first clamped to `now − BACKLOG_CAP × interval` — re-entering sim range fires a ~2-shot burst, not a seconds-long wall of bullets.

HP-threshold phases are computed on the damage path, not the tick: [[server/spacetimedb/src/combat/mod.rs#apply_damage_to_enemy_opts|apply_damage_to_enemy_opts]] runs [[server/spacetimedb/src/enemy/methods.rs#compute_phase|compute_phase]] (highest `phase_index` whose `hp_threshold` has been crossed) after every surviving hit and re-aggros the enemy onto the attacker — which is why the tick only *detects* phase changes instead of computing them.

### Spawning: BiomeRegion zones and the admin path

There is no `EnemyZone` table — spawn zones are `BiomeRegion` rows (^table-biome-region, generated by world-gen; see [[07 Terrain & World Tables]]), which carry `center_hex_q/r`, `spawn_radius`, `max_enemies`, and a list of `enemy_template_ids`. The 2 s `tick_enemy_spawn` job runs [[server/spacetimedb/src/enemy/methods.rs#spawn_from_biome_regions|spawn_from_biome_regions]], which for each region: skips it unless a logged-in player is within `SPAWN_LOOKAHEAD_CHUNK_RINGS` of the center (no populating the far side of the map); counts live enemies of the region's templates inside the radius with the wrap-aware `enemies_near_point` chunk-index query (a plain distance check would miss the region's own children when a spawn offset wraps across the lap seam, silently defeating the cap); then draws a uniform point in the disc — `sqrt` on the radius draw, not a raw scale, so points don't bunch at the center — wraps it, and calls `spawn_enemy_archetype`.

The spawn job **starts disarmed**: `init` deliberately inserts no `EnemySpawnSchedule` row, so a fresh publish has no biome spawning; admin [[server/spacetimedb/src/main/admin.rs#toggle_enemy_spawning|toggle_enemy_spawning]](true) arms the 2 s interval row, (false) deletes it. Admin [[server/spacetimedb/src/main/admin.rs#spawn_enemy|spawn_enemy]] is the manual path — it wraps the coordinates, stamps the chunk, and goes through the same archetype helper, and it's the only way to set `is_elite`/`immortal` (`immortal` makes `report_enemy_hit` a no-op). Despawning mirrors spawning: the combat kill path and admin [[server/spacetimedb/src/main/admin.rs#despawn_enemy|despawn_enemy]] / [[server/spacetimedb/src/main/admin.rs#despawn_all_enemies|despawn_all_enemies]] all route through `despawn_enemy_archetype` plus `status::methods::on_enemy_killed`, so no behavior-tree or status rows reference a dead enemy.

### The client read path

The client's game wave ([[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber.GameTables]]) subscribes `NearbyEnemies` and `EnemyTemplates` — and nothing else from this doc. The behavior tree and step defs are `public` but unsubscribed by design: `BulletPatternEvent` (^table-bullet-pattern-event, [[09 Combat & Event Tables]]) carries each shot's resolved damage, pattern, origin, and curving, so clients never join step defs, and puppets mirror the flat `Enemy` row instead of the tree.

Three binders consume `NearbyEnemies`, each declared inline in its live scene (the wiring is in the `.tscn` — Godot's text scene format — not in code):

1. **`EntitySpawnerComponent/NearbyEnemiesBinder`** in [[client/Scenes/game.tscn|game.tscn]] (replay on) wires `RowInserted`/`RowDeleted` to [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnEnemyInsert|OnEnemyInsert]] / [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnEnemyDelete|OnEnemyDelete]]: insert instantiates `default_enemy.tscn`, sets `EnemyId` and position, and defers the `AddChild` (the parent scene is resolved when the deferred call runs, and a delete arriving first cancels the pending add via `pendingAdds`); delete removes the puppet from the tracking dictionary that backs `GameManager.GetEnemy`/`EnemyCount`.
2. **Each puppet's own `NearbyEnemiesBinder`** in [[client/Scenes/default_enemy.tscn|default_enemy.tscn]] (replay on, so a puppet spawned from a cached row still initializes) wires insert/update to [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowInserted|Enemy.cs]]: insert filters to its own `EnemyId` (the binder sees every nearby enemy's rows), snaps `InterpolationComponent` to the row position, looks the template up in the client's `EnemyTemplates` cache for `max_hp` and the sprite's `SpriteFrames` (via the catalog's `GetResPath`), and mirrors hp/max_hp into `HealthComponent.SetFromServer` — the puppet never computes damage, it mirrors; [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowUpdated|OnEnemyRowUpdated]] feeds position to `InterpolationComponent.SetTarget` (which dead-reckons between 100 ms tick writes) and refreshes hp, phase, and the 0–100 `Stagger` bar the `EnemyDebuffIndicatorComponent` renders.
3. **`CombatComponent/NearbyEnemiesBinder`** in [[client/Scenes/local_player.tscn|local_player.tscn]] (replay **off**, and only `RowDeleted` wired) exists solely to invalidate the local player's aim lock when its locked enemy despawns.

Bullet patterns deliberately bypass the puppet: one shared `BulletPatternEventBinder` on `BulletSpawnerComponent` renders every enemy's shots, because the event rows are self-contained (absolute origin, baked angles) — no per-enemy binder fan-out, and no volley is lost during a puppet's deferred-spawn window. That path, and the hit-reporting direction (`report_enemy_hit`'s rate budget and wrap-aware range check in [[server/spacetimedb/src/enemy/reducers.rs#report_enemy_hit|enemy/reducers.rs]]), belong to [[09 Combat & Event Tables]].

## Known gaps / stubs

- **`MultiStepDef` is unused by shipped content** — `seq_multi`/`multi_shot` are `#[allow(dead_code)]` authoring API ([[server/spacetimedb/src/enemy/defs/mod.rs#seq_multi|defs/mod.rs]]); every shipped enemy composes Single/Repeat steps or emitters.
- **The Gorgon port is incomplete by its own header** ([[server/spacetimedb/src/enemy/defs/gorgon_boss.rs#build|gorgon_boss.rs]]): boss root movement (hops, orbits, lunges), intro/death cinematics, minion spawns, and per-emitter bullet colors are not ported; `texture_id` is the placeholder `"Enemy"` sprite; the source's one curving emitter is never activated, so `Emitter::curve` is supported but unused by content.
- **`is_elite` is dead weight today** — settable only via the admin `spawn_enemy` args, mirrored by `Enemy.cs`, and consumed by no gameplay on either side.
- The enemy-facing gap in [[06 Movement & AOI Tables]] applies here: `tick_enemy_behavior` and `spawn_from_biome_regions` guard against logged-out "ghost" `PlayerPosition` rows with `logged_in_player` semijoins rather than the rows being cleaned up on logout.

## Where to go next

[[09 Combat & Event Tables]] picks up exactly where this doc stops: how `BulletPatternEvent` rows become rendered bullets, and how `report_hit`/`report_enemy_hit` flow back into `Enemy.hp`/`phase` through the combat pipeline. [[11 Status, Zone & Charge Tables]] covers the stagger/Stunned mechanics and enemy debuffs this doc only touches.
