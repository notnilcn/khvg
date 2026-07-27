# 05 Enemy AI & Bullet Patterns

## Assumed knowledge

- [[00 Roadmap]] for how these docs are structured and the conventions used below.
- [[01 Architecture & Sync Model]] for the tables/reducers/views vocabulary, and for scheduled reducers specifically — `tick_enemy_spawn`/`tick_enemy_behavior` are this system's instance of the same "recurring timer reducer" pattern `init` sets up at boot.
- [[02 World & Hex Grid]] for the chunk/AOI mechanism this system's `nearby_enemies` view reuses, the torus-wrap helpers (`wrap_world_pos`, `wrapped_distance_sq`) enemy movement and spawning both call into directly, and `BiomeRegion` — enemy spawning's hook into world geography, covered there as "the enemy-spawning system's hook into world geography, out of scope beyond noting the table lives in `world/`." This doc is where that hook actually gets used.
- [[04 Item & Equipment System]] for `recompute_stats`/`compute_player_damage` (read by `report_enemy_hit` below) and for the loot-drop gap this doc confirms from the other side.

## The 30-second version

An `EnemyTemplate` is pure data: a max HP, a couple of simulation-range multipliers, and a `Vec<PhaseDef>`, where each phase names a `MovementBehavior` and a list of `AttackSequence`s built from `Single`/`Repeat`/`Multi` step defs, each step carrying a `PatternType` (`Ring`/`Volley`/`Curtain`/`Shotgun`/`Explosion`) and its params. None of that is simulated directly — `build_enemy_behavior` instantiates a template's phases into live, per-enemy `EnemyBehavior`/`EnemyPhase`/`EnemyAttack`/`EnemySequenceStep` rows the moment an `Enemy` is spawned, and two scheduled reducers do the rest: `tick_enemy_spawn` walks every `BiomeRegion` looking for one with a nearby player and room under its `max_enemies` cap, and `tick_enemy_behavior` runs every 100ms over every `Enemy` in the world, but only actually simulates the ones within range of some player. Simulating an enemy means three things every tick: retargeting aggro (only on the tick it *enters* range), advancing its attack sequences — which is what turns a sequence step into a `BulletPatternEvent` row — and moving it per its phase's `MovementBehavior`. Clients never see any of the def/instance machinery directly; they subscribe to `nearby_enemies` (AOI-filtered, same chunk-ring mechanism as terrain) and `BulletPatternEvent`, and `Enemy.cs`/`BulletManager.cs` turn that row stream into an interpolated sprite and actual dodgeable bullets via the BlastBullets2D plugin. Taking damage comes back the other way through `report_enemy_hit`, which this doc covers as the entry point but defers the damage formula itself to [[06 Combat & Damage Math]]; on death it awards XP but — confirmed from this side, not just [[04 Item & Equipment System|04's]] — never inserts a `LootDrop`.

## System flowchart

Ordering below is a scoped excerpt of [[99 End-to-End Timeline Flowchart]] — the "Enemy spawning and behavior" fork of the parallel in-world loop.

```sync
![[99 End-to-End Timeline Flowchart#^enemy-1{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-2{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-3{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-4{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-5{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-6{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-7{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-8{seamless:true,title:false,marker:08.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-9{seamless:true,title:false,marker:09.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-10{seamless:true,title:false,marker:10.}]]
```

## `EnemyTemplate`: data that describes an enemy without simulating one

`EnemyTemplate` (`enemy/def_tables.rs`) is the catalog row: `template_id`, `texture_id`, `display_name`, `max_hp`, `aggro_lock_seconds`, and a `Vec<PhaseDef>`. Two fields — `move_sim_factor`/`attack_sim_factor` — are deliberately unitless multipliers of `CAMERA_VIEW_RADIUS` (`main/global.rs`, itself derived from `CLIENT_VIEWPORT_HEIGHT_PX`/`CLIENT_CAMERA_ZOOM`) rather than raw world-unit ranges, so a template's simulation ranges stay correctly scaled if the client's camera or viewport ever changes — an enemy authored today doesn't silently start simulating from the wrong distance after a camera tweak next month. See [[def_tables.rs##pub struct EnemyTemplate|EnemyTemplate]].

A `PhaseDef` names a `movement_def_id` (pointing at a `MovementDef` row holding one `MovementBehavior` — `Stationary`, or `Wander`/`Chase`/`Flee` each with their own speed/duration params), an `hp_threshold` (the HP-percent below which this phase activates), a `phase_loop_delay`, and a `Vec<AttackSequence>`. An `AttackSequence` is a `Vec<SequenceStepDef>` plus a `start_delay`; each `SequenceStepDef` is one of three shapes:

- **`Single(def_id)`** → `SingleStepDef` — fire once, then wait `next_step_delay` before the sequence's next step.
- **`Repeat(def_id)`** → `RepeatStepDef` — the same shot fired every `repeat_interval` seconds until `repeat_count` reaches `repeat_target`, then it behaves like a `Single` for the purposes of advancing.
- **`Multi(def_id)`** → `MultiStepDef`, a `Vec<MultiShot>` — several *different* shots (each its own `pattern_type`/`offset`/`texture_id`) fired together as one step, e.g. a boss opening six simultaneous bursts around a ring.

Every one of those step defs carries a `PatternType` (`Ring`/`Volley`/`Curtain`/`Shotgun`/`Explosion`, each with its own param struct — count, speed, spread, jitter, etc.), an `EnemyTarget` (`Idle` or `AggroTarget`), a spawn offset/angle, a texture id, a lifetime, and a `damage`. `PatternType` is the enemy side's equivalent of `WeaponBehavior.pattern` (`WeaponPattern::Single/Triple/Cluster`, [[04 Item & Equipment System]]) — both describe "what shape of bullets to make," just with the enemy side supporting a much richer set of shapes since bosses need more variety than a player's own weapon does. See [[def_tables.rs##pub enum PatternType|PatternType]] and [[def_tables.rs##pub struct PhaseDef|PhaseDef]].

Every template today is hand-seeded in `main/seeds.rs` (`seed_default_enemies` for `Enemy`/`Archer`, `seed_test_boss_p2` through `p6` building up a multi-phase boss) via small builder helpers (`make_phase`, `make_sequence`, `seq_single`/`seq_repeat`/`seq_multi`) that insert the underlying def rows and hand back the right `SequenceStepDef`/`PhaseDef` — there's no procedural enemy generator, matching [[04 Item & Equipment System|04's]] note that items don't have one either.

## From template to instance: `build_enemy_behavior`

A `PhaseDef`'s steps are shared, read-only template data — an enemy actually fighting needs its own mutable progress through them (which step it's on, whether it's mid-wait, how many times a repeat has fired). `build_enemy_behavior` is the one-time translation from one to the other, run whenever an `Enemy` is spawned (step 3 above): one `EnemyBehavior` row (aggro target/lock, `was_simulating`, velocity), and per phase one `EnemyPhase` row, per `AttackSequence` one `EnemyAttack` row, and per step one `EnemySequenceStep` row referencing a `SequenceStepRef` (`Single`/`Repeat`/`Multi`, mirroring the def-side enum but pointing at instance data). A `Repeat` step is the one case that needs a genuinely separate row per enemy rather than just a reference back to the def: its `RepeatStepInstance` clones every field off the shared `RepeatStepDef` specifically so `repeat_count` can advance independently for every enemy running that template, instead of one enemy's repeat count leaking into another's. See [[methods.rs##pub fn build_enemy_behavior|build_enemy_behavior]].

## Spawning: `tick_enemy_spawn` and `BiomeRegion`

`tick_enemy_spawn` is a scheduled reducer (`EnemySpawnSchedule`) that does nothing but call `spawn_from_biome_regions`, which loops every `BiomeRegion` row — [[02 World & Hex Grid|02's]] Voronoi-region output, carrying a center hex, `spawn_radius`, `enemy_template_ids`, and `max_enemies`. For each region it:

1. Skips entirely if `has_player_near_spawn_point` finds no logged-in player within `SPAWN_LOOKAHEAD_CHUNK_RINGS` chunks of the region's center — a region nobody is near never spawns into, so the simulation never spends work populating map corners no one is about to see.
2. Counts already-live enemies matching that region's `enemy_template_ids` within `spawn_radius`, using `wrapped_distance_sq` rather than a plain Euclidean check — a spawn point can wrap across a torus seam to the far side of the map even when the region's own center doesn't, and a naive distance check would then never recognize an enemy as "this region's," silently blowing past `max_enemies`. Skips if already at cap.
3. Otherwise picks one template (`region_id` XORed against the current time, truncated to seconds — not a stored rotation, just enough variety that repeated ticks don't always pick the same template) and rolls a spawn point uniformly over the region's disc: `next_unit(seed, draw).sqrt()` for the radius specifically, not a raw linear draw, so points don't visually bunch toward the center the way sampling radius linearly would.
4. Re-wraps the rolled point through `wrap_world_pos`, resolves its chunk via `world_to_chunk`/`spiral_chunk_index`, calls `build_enemy_behavior`, and inserts the `Enemy` row.

See [[reducers.rs##pub fn tick_enemy_spawn|tick_enemy_spawn]] and [[methods.rs##pub fn spawn_from_biome_regions|spawn_from_biome_regions]].

## Behavior tick: aggro, phases, sequences, movement

`tick_enemy_behavior` (`EnemyBehaviorSchedule`, every `BEHAVIOR_TICK_DT` = 0.1s) iterates every `Enemy` row in the world, but the expensive parts only run for enemies within range of a player:

- **Simulation gate.** `has_player_in_simulation_range(x, y, template.move_sim_factor * CAMERA_VIEW_RADIUS)` searches the chunk rings around the enemy (`SIMULATION_CHUNK_RINGS`) for any *logged-in* player's `PlayerPosition` within that radius — filtering out logged-out players specifically, since `PlayerPosition` rows outlive a disconnect and would otherwise make an enemy simulate forever against a ghost. An enemy outside range flips `was_simulating` off and does nothing further this tick.
- **Aggro retargeting.** `was_simulating` doubles as an edge detector: aggro is only recomputed (`recompute_aggro` → `find_nearest_player_id`, locked for `template.aggro_lock_seconds`) on the tick an enemy transitions from out-of-range to in-range, not every tick it stays in range — so an enemy mid-fight doesn't keep re-evaluating which player is nearest every 100ms.
- **Phase-transition detection.** `enemy.phase` is a plain field that `report_enemy_hit` (below) advances directly the instant HP crosses a threshold; `tick_enemy_behavior` doesn't decide transitions, it just compares `enemy.phase` against `behavior.active_phase_index` and, on a mismatch, calls `begin_phase_cycle` to reset the new phase's `EnemyAttack` rows to step 0 (and start the `phase_loop_delay` wait, if any) — this is what makes a boss's next phase open on a clean beat instead of resuming wherever the old phase's per-attack counters happened to be.
- **Attack range gate + sequence ticking.** Only once a player is within the *tighter* `template.attack_sim_factor * CAMERA_VIEW_RADIUS` range does `tick_phase` run — deliberately separate from the movement range, so an enemy is already visibly approaching by the time it opens fire rather than shooting from off-screen. `tick_phase` advances every `AttackSequence` in the active phase by at most one step each, and `tick_sequence` is where the three step shapes (`Single`/`Repeat`/`Multi`) actually decide whether to fire this tick, wait, or advance — every step that fires produces a `BulletStepEvent` tuple that becomes one `BulletPatternEvent` row, `target` resolved from the step's `EnemyTarget` via `resolve_step_target` (`Idle` → `None`, `AggroTarget` → the behavior's locked identity) and stamped with the enemy's own `chunk_index` so the AOI view below can filter on it.
- **Movement.** `apply_movement` resolves the active phase's `MovementDef` into a new position and facing direction: `Stationary` never moves, `Wander` derives a reproducible-but-unstored direction from `enemy_id` + a cycle index (so its path is auditable without persisting extra state), `Chase`/`Flee` steer toward/away from the aggro target's live position, and all three duty-cycle their movement through `cycle_phase` (active/pause durations) rather than moving continuously. The result is wrapped through the same `wrap_world_pos`/`world_to_chunk`/`spiral_chunk_index` helpers [[02 World & Hex Grid]] documents for players, and written back to `Enemy`/`EnemyBehavior`/`EnemyPhase`.

See [[reducers.rs##pub fn tick_enemy_behavior|tick_enemy_behavior]], [[methods.rs##pub fn tick_phase|tick_phase]], [[methods.rs##pub fn tick_sequence|tick_sequence]], and [[methods.rs##pub fn apply_movement|apply_movement]].

## Taking damage: `report_enemy_hit`

`report_enemy_hit(enemy_id)` is this system's reducer for a player's attack landing on an enemy — the client-side hit-detection path that calls it, and `compute_player_damage`'s formula, are [[06 Combat & Damage Math|06's]] concern; this doc only covers what happens to the `Enemy`/`EnemyBehavior` rows once a damage number exists. An `immortal` enemy (debug/test tooling) no-ops. Otherwise HP is reduced by `compute_player_damage`'s result:

- **On death** (`new_hp == 0`): `delete_enemy_behavior` cascades the delete across every `EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance` row `build_enemy_behavior` created, the `Enemy` row itself is deleted, and `internal_gain_xp` awards `template.max_hp / 10` XP to the killer's profile ([[03 Player System]]'s leveling math from there). **No `LootDrop` row is inserted anywhere in this path** — confirmed by reading `enemy/reducers.rs` directly, not just re-citing [[04 Item & Equipment System|04's]] item-side finding: "enemies drop loot" doesn't exist in the code from either direction yet.
- **On a non-lethal hit**: `compute_phase` re-checks the new HP against the template's `hp_threshold`s (which is what step 5 above notices as a phase change next tick), and aggro re-locks onto the attacker if either the phase just changed or the existing lock has expired — a hit from a second player doesn't steal aggro mid-lock.

See [[reducers.rs##pub fn report_enemy_hit|report_enemy_hit]].

## Client: from `NearbyEnemies` rows to a rendered, shooting enemy

`nearby_enemies` (`enemy/views.rs`) filters `Enemy` by the caller's AOI chunk indices, the same `nearby_indices` helper [[02 World & Hex Grid]] documents for terrain — this is one of the genuinely implemented views `server/CLAUDE.md` calls out as the exception to "most `views.rs` files are placeholders" (confirmed still true by reading it directly; `enemy_templates`, the unfiltered catalog view, is the other one). `GameManager.OnEnemyInsert`/`OnEnemyDelete` spawn or free one `Enemy` node per row, exactly like `RemotePlayer` for other players.

`Enemy.cs` never re-simulates anything server-owned: `_Ready` reads its starting `(x, y, hp, phase, template)` straight out of `NearbyEnemies.Iter()`, `OnEnemyUpdate` just records the server's latest position/hp/phase, and `_Process` lerps `GlobalPosition` toward `TorusMath.NearestCandidate(canonicalPosition, GlobalPosition, LapQ, LapR)` every frame — the same wrap-aware nearest-copy trick `RemotePlayer` and `TerrainManager` use, so an enemy near a seam doesn't visually jump or double up. See [[Enemy.cs##public override void _Process|_Process]].

Each `BulletPatternEvent` row (an `event`-flagged table, so it's a fire-and-forget insert clients receive as a row rather than something they'd ever update or delete) also arrives at every client in that enemy's AOI. `Enemy.cs` listens for `BulletPatternEvent.OnInsert`, filters to rows matching its own `EnemyId`, and forwards them to `BulletManager.SpawnEnemyBullet`. `BulletManager`:

1. Resolves the event's `target` identity to a live position — the local player's own `LocalPlayerPosition` if `target` is this client, otherwise a `NearbyRemotePlayers` row — to compute a firing angle; an `Idle`-targeted event (no `target`) just fires along `base_angle_offset` with no aim correction.
2. Dispatches on `PatternType`: each of `Ring`/`Volley`/`Curtain`/`Shotgun`/`Explosion` builds its own spread of `Transform2D`s (angle + origin) and hands them to the BlastBullets2D GDExtension plugin's `DirectionalBulletsData2D`/`BulletSpeedData2D` — the plugin owns the actual bullet nodes, movement, and collision from there; `BulletManager` only configures and triggers it (`client/CLAUDE.md` scopes going deeper into the plugin itself as "only relevant when debugging plugin internals").
3. For patterns with per-pellet jitter (`Volley`'s speed/angle jitter, `Shotgun`/`Explosion`'s speed/lifetime variance), a `SplitMix64` PRNG keyed by `event_id` — deliberately mirroring the server's own hash technique in `enemy/methods.rs` rather than using Godot's own RNG — means every client watching the same event rolls identical-looking pellet spread without the server needing to ship one row per pellet.

The bullets themselves are client-side visuals and collision only: a bullet actually connecting with a player's `Area2D` is detected locally (`BulletManager.OnEnemyBulletAreaEntered`) and reported back to the server via `report_hit(source_step)` — the reverse direction of this doc's combat flow, and [[06 Combat & Damage Math|06's]] concern for the damage math and state transition (death → `LoggedOutPlayer`) that follows. See [[BulletManager.cs##public void SpawnEnemyBullet|SpawnEnemyBullet]].

## Known gaps and stubs

- **Enemies never drop loot.** Confirmed directly from this side (see "Taking damage" above): `enemy/reducers.rs` has no `LootDrop` insert anywhere in `report_enemy_hit`'s death branch. The table and pickup flow ([[04 Item & Equipment System]]) are fully wired for player-initiated drops only — "enemies drop loot on death" doesn't exist in the code yet from either direction.
- **No procedural enemy generator.** Every `EnemyTemplate` is hand-seeded in `main/seeds.rs` via the `make_phase`/`make_sequence`/`seq_single`/`seq_repeat`/`seq_multi` builder helpers — there's no generator that assembles phases/sequences from weighted parameters the way [[04 Item & Equipment System|04's]] item schema was shaped to eventually support.
- **`is_elite`/`immortal` are always `false` from the normal spawn path.** `spawn_from_biome_regions` (the only spawn path regular play ever exercises) hardcodes both to `false` on every insert; the only way either is ever `true` is via the admin-only `spawn_enemy` reducer (`main/admin.rs`, [[08 Admin, Debug & World Lifecycle|08's]] domain), which takes them as explicit arguments. `Enemy.cs` does read `IsElite` client-side, so the consumer exists even though nothing in ordinary play produces the interesting value.
- **`main/admin.rs`'s `spawn_enemy` looks like it double-inserts the `EnemyBehavior` row.** `build_enemy_behavior` already calls `ctx.db.enemy_behavior().insert(..)` internally and returns the already-inserted row (with its real, nonzero `behavior_id` — this is exactly what `spawn_from_biome_regions` above uses directly, via `let inserted_behavior = build_enemy_behavior(ctx, &template);`). `spawn_enemy` instead wraps that call in a second `ctx.db.enemy_behavior().insert(build_enemy_behavior(ctx, &template))` — inserting the same row a second time under a primary key that already exists in the table. This is outside this doc's own module (`enemy/reducers.rs`'s spawn path is correct; only the admin one-off spawn in `main/admin.rs` does this), but it's a concrete-looking bug worth flagging for whoever writes [[08 Admin, Debug & World Lifecycle|08]] rather than re-discovering it there.
- **`server/CLAUDE.md`'s claim about `views.rs` files needed a recheck, not a repeat.** [[01 Architecture & Sync Model|01]] already flagged that `player/views.rs`/`world/views.rs`/`item/views.rs` all have real views today, contrary to that file calling `enemy/views.rs` the lone exception. Reading `enemy/views.rs` directly for this doc confirms *its* half of the claim still holds — `nearby_enemies` and `enemy_templates` are both real, non-placeholder views — so this file's per-module note is accurate for `enemy/` specifically, just stale about the other three modules, exactly as 01 already noted.

## Where to go next

[[06 Combat & Damage Math]] picks up both loose threads this doc deliberately left dangling: `compute_player_damage` (the formula behind `report_enemy_hit`'s damage number) and the reverse direction, `report_hit`, which is what a player's own HP loss and eventual death actually run through. [[07 Client Rendering & Camera]] is the other natural branch — it covers why enemies (and everything else) render in 2D via `TerrainManager`-style batching while only the player's own model gets a 3D presenter.
