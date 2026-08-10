# 08 Enemies & AI

## Assumed knowledge

- [[07 Terrain & World Streaming]] — `BiomeRegion` rows are planted by world generation (terrain-3) and carry each region's enemy template ids / `max_enemies` / `spawn_radius`; also the torus lap vectors, `wrap_world_pos`/`wrapped_distance_sq`, and the SplitMix64 PRNG conventions.
- [[06 Movement & Position Sync]] — the AOI view pattern (OR-chained `chunk_index` over the caller's nearby chunks, move-5) and the client interpolation machinery (`SnapTo`/`SetTarget`, move-6) that enemy puppets reuse verbatim.
- [[05 Joining the World]] — the game subscription wave (join-3) that carries `NearbyEnemies`/`EnemyTemplates`/`BulletPatternEvent`, and `EntitySpawnerComponent`'s spawn-per-row pattern (join-5).
- [[03 Boot & Connection]] — publish-time `init` inserts the scheduled-reducer rows and runs the seed functions (boot-4).
- [[02 The Component Framework]] — entities/components on the client, `TableBinderComponent`'s `LastRow`/`ReplayExistingRows`, and the server-side mirror (one table per concern, archetype helpers).
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `enemy` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

Enemies are **server-simulated, client-rendered**. At publish time the server seeds `EnemyTemplate` rows — data-only bullet-hell scripts made of phases (HP thresholds), movement behaviors, and attack sequences built from shared step-definition rows. Two scheduled reducers drive everything at runtime: every 2 s `tick_enemy_spawn` walks the world-gen `BiomeRegion` districts and spawns one enemy per region that has a player nearby and room under its cap; every 100 ms `tick_enemy_behavior` advances each enemy that has a player within simulation range — retargeting aggro, ticking its attack sequences, emitting one `BulletPatternEvent` per fired shot, and integrating one movement step. Clients see enemies through the AOI view `NearbyEnemies` and spawn a `default_enemy.tscn` puppet per row, which interpolates toward server positions and mirrors HP into its health component. Bullets are never simulated on the server at all: the event rows describe the pattern, and each client's `BulletSpawnerComponent` reconstructs the actual projectiles locally through the BlastBullets2D GDExtension, with per-pellet jitter derived deterministically from the event id so every client sees the same trajectories.

## Flowcharts

- [[flowcharts/main-enemies.canvas]] — the composed enemies flow (client `Enemy` puppet and bullet components, `default_enemy.tscn`/`bullet_manager.tscn`, the server's `enemy` module and the `main` module's seeds/lifecycle).
![[flowcharts/main-enemies.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/enemy_subfolder/enemy_subfolder.canvas]] — deep dive: the whole `enemy` module (def tables, instance tables, methods, reducers, views).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Bullets_subfolder/Bullets_subfolder.canvas]] — deep dive: the bullet components (`BulletSpawnerComponent`'s per-pattern spawn methods, `BulletHitRouterComponent`).
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/default_enemy_codefile/default_enemy_codefile.canvas]] — deep dive: `default_enemy.tscn`, the enemy puppet scene.

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

```sync
![[00 End-to-End Timeline Flowchart#^enemy-9{seamless:true,title:false,marker:09.}]]
```

## Main body

### Templates are shared data; instances are per-enemy trees

```sync
![[00 End-to-End Timeline Flowchart#^enemy-1{seamless:true,title:false,marker:01.}]]
```

The def/instance split is the whole design in one move. The def tables in [[server/spacetimedb/src/enemy/def_tables.rs##pub struct EnemyTemplate|def_tables.rs]] are *authored content*: an `EnemyTemplate` is a list of phases, each phase a [[server/spacetimedb/src/enemy/def_tables.rs##pub struct PhaseDef|PhaseDef]] pairing an `hp_threshold` with a `MovementDef` id and a list of `AttackSequence`s, each sequence a list of [[server/spacetimedb/src/enemy/def_tables.rs##pub enum SequenceStepDef|SequenceStepDef]] ids pointing at `SingleStepDef`/`RepeatStepDef`/`MultiStepDef` rows. Because the ids are shared, one "8-way ring" definition serves every enemy whose template references it — and because `EnemyTemplate` itself is a plain row, a boss is just a longer template ([[server/spacetimedb/src/main/seeds.rs##pub fn seed_default_enemies|seed_default_enemies]]' "TestBoss" has five phases that cycle through every movement behavior and every pattern type; there is no separate boss code path anywhere).

Two enum vocabularies do the expressive work. [[server/spacetimedb/src/enemy/def_tables.rs##pub enum MovementBehavior|MovementBehavior]] is `Stationary`/`Wander`/`Chase`/`Flee`, the last three carrying `active_duration`/`pause_duration` so movement breathes instead of grinding constantly. [[server/spacetimedb/src/enemy/def_tables.rs##pub enum PatternType|PatternType]] is the bullet-hell pattern catalog — `Ring` (full 360°), `Volley` (a few aimed, jittered shots), `Curtain` (a fan with one indexed gap — the safe lane), `Shotgun` (a spread with per-pellet speed/lifetime variance), `Explosion` (a randomized nova) — each just a parameter struct. A step also carries a [[server/spacetimedb/src/enemy/def_tables.rs##pub enum EnemyTarget|EnemyTarget]]: `AggroTarget` means "aim at whoever this enemy has aggro on", resolved per shot; `Idle` means unaimed, fired at a fixed world angle plus the step's `base_angle_offset` — which is how you write a rotating-spray sequence (TestBossP3's `arrow_many(0.0)` … `arrow_many(50.0)` steps).

```sync
![[00 End-to-End Timeline Flowchart#^enemy-3{seamless:true,title:false,marker:03.}]]
```

Spawning *instantiates* that data into the instance tables of [[server/spacetimedb/src/enemy/instance_tables.rs##pub struct Enemy {|instance_tables.rs]], and this is where the server-mirrors-the-client idea from [[02 The Component Framework]] pays off: `EnemyBehavior` is the AI-state component, `EnemyPhase`/`EnemyAttack`/`EnemySequenceStep` are the per-phase progress components, and the flat `Enemy` row is the transform-plus-health component — all joined by `behavior_id`, all cleaned up together by [[server/spacetimedb/src/enemy/methods.rs##pub fn despawn_enemy_archetype|despawn_enemy_archetype]]. The one asymmetry worth internalizing is `RepeatStepInstance`: a repeat step's `repeat_count` is *runtime* state, so it can't live on the shared `RepeatStepDef` — [[server/spacetimedb/src/enemy/methods.rs##pub fn build_enemy_behavior|build_enemy_behavior]] copies the def into a per-enemy instance row at spawn and the `EnemySequenceStep` points at the copy. Single and Multi steps have no mutable counters, so they keep pointing at shared defs.

The same archetype helper serves the admin reducers [[server/spacetimedb/src/main/admin.rs##pub fn spawn_enemy|spawn_enemy]]/[[server/spacetimedb/src/main/admin.rs##pub fn despawn_enemy|despawn_enemy]] ([[12 Admin & Debug]]) — which is currently the *only* way the seeded TestBoss templates ever enter the world, since the region spawn pool below references just "Enemy" and "Archer".

### Spawning: regions are districts, players are the trigger

```sync
![[00 End-to-End Timeline Flowchart#^enemy-2{seamless:true,title:false,marker:02.}]]
```

The spawn tick is deliberately cheap and deliberately conservative. It iterates `BiomeRegion` rows (nine in the seeded "Earth" world, all sharing one [[server/spacetimedb/src/main/seeds.rs##fn seed_region_def|seed_region_def]] pool: "Enemy" + "Archer", cap 5, radius 300) and applies two gates before spending anything. The audience gate, [[server/spacetimedb/src/enemy/methods.rs##pub fn has_player_near_spawn_point|has_player_near_spawn_point]], is chunk-granular, not distance-granular: it only asks whether *some* logged-in player's `PlayerPosition` sits within 2 chunks of the region center — no exact range check — which is enough because the cap keeps the population bounded anyway. The population gate counts live enemies by template within `spawn_radius`, and the code comment records why it must be `wrapped_distance_sq`: a spawn that crosses a lap seam is wrapped to the far side of the map, and a Euclidean count would lose track of those children and spawn past the cap forever.

The randomness is time-seeded, not world-seeded — unlike terrain generation, spawns *should* differ run to run. The seed is `region_id.wrapping_add(microseconds_since_epoch)`, and the comment above the draws documents a real bug this fixed: at that magnitude a raw `as f32` cast has rounding granularity coarser than the per-tick increment, so the angle draw silently froze for long stretches — routing through [[server/spacetimedb/src/world/prng.rs##pub fn next_unit|next_unit]]'s `splitmix64` + `hash_to_unit` (which shifts down to 53 bits first) is what makes the draws actually vary. Note the spawn is *not* deterministic across server restarts and doesn't need to be: position is runtime state, not content.

### The behavior tick: two ranges, one state machine

```sync
![[00 End-to-End Timeline Flowchart#^enemy-4{seamless:true,title:false,marker:04.}]]
```

The 100 ms tick's most important property is what it *doesn't* do: enemies with no player within `move_sim_factor × CAMERA_VIEW_RADIUS` are skipped entirely — no movement, no attacks, no row writes — and the two-factor split (`move_sim_factor` 1.5–2.75 vs `attack_sim_factor` 0.85–1.35 in the seeds) exists so an enemy wakes up and starts approaching *before* it can open fire, never shooting from off-screen. The factors are multiples of the client's view radius rather than absolute units, so the sim envelope tracks whatever the player can actually see (the `EnemyTemplate` comment spells this out). `was_simulating` is the edge detector: aggro is recomputed only on the tick an enemy *enters* range, not every 100 ms mid-fight — retargets after that come from being hit, which is [[09 Combat & Damage]]'s `deal_damage_to_enemy` (it also honors the `aggro_locked_until` written here).

Phase transitions follow the same detect-don't-push pattern: nothing in the tick computes phases from HP. [[server/spacetimedb/src/enemy/methods.rs##pub fn compute_phase|compute_phase]] runs inside the damage path (doc 09) and writes `enemy.phase`; the tick just notices `enemy.phase != behavior.active_phase_index` and restarts the new phase's sequences via `begin_phase_cycle`. Phases are authored in descending-threshold order, but `compute_phase` takes the max matching index defensively in case that assumption is ever violated.

```sync
![[00 End-to-End Timeline Flowchart#^enemy-5{seamless:true,title:false,marker:05.}]]
```

The attack machine is a tiny interpreter over the instance rows, and its economy rule is **one firing per attack per tick**: [[server/spacetimedb/src/enemy/methods.rs##pub fn tick_sequence|tick_sequence]] can burn through any number of *expired waits* in a single call (the `continue` path when `next_step_delay` has elapsed), but it returns immediately after emitting one step's events. All the mutable progress — `step_index`, `step_waiting`, `step_timer`, `start_delaying` — persists on the `EnemyAttack` row between ticks, so the 10 Hz schedule *is* the game loop and there is no in-memory state to lose. Timers compare against `ctx.timestamp` rather than accumulating deltas, so a slow or bursty schedule stretches real time instead of accumulating drift. When all of a phase's attacks exhaust their step lists, [[server/spacetimedb/src/enemy/methods.rs##pub fn tick_phase|tick_phase]] calls `begin_phase_cycle`, which either waits out `phase_loop_delay` (attacks parked at step 0, repeat counts reset) or immediately reactivates everything — this is what makes a single-phase template loop forever.

`Repeat` steps have one corner worth reading in the code: `repeat_target == 0` means "never fire, just wait `next_step_delay`" — the step is then a pure pause, which is how you author breathing room between sequences without a dedicated delay step kind.

```sync
![[00 End-to-End Timeline Flowchart#^enemy-6{seamless:true,title:false,marker:06.}]]
```

Movement runs *after* attacks in the tick, so a shot's origin is always the pre-move position — the bullets and the enemy's next position never disagree about where it was when it fired. The `Wander` determinism trick deserves the comment it has: the direction is `splitmix64(enemy_id + cycle_index)` hashed to an angle, so the walk is fully reproducible from state that's already stored (the enemy id and the phase's `move_cycle_started_at`, from which [[server/spacetimedb/src/enemy/methods.rs##pub fn cycle_phase|cycle_phase]] derives the cycle index) — an audit tool can replay any enemy's path exactly, at the cost of zero extra columns. `Chase`/`Flee` resolve the aggro target's position fresh each tick via `find_player_pos_by_id`; if the target has no position row (logged out mid-fight — the ghost-row case, [[13 Disconnect & Teardown]]), the movement simply idles rather than chasing a stale coordinate.

### Delivery: AOI for bodies, a firehose for events

```sync
![[00 End-to-End Timeline Flowchart#^enemy-7{seamless:true,title:false,marker:07.}]]
```

The two halves of enemy state travel on deliberately different channels. The `Enemy` rows — slow-moving, persistent — ride the AOI view [[server/spacetimedb/src/enemy/views.rs##fn nearby_enemies|nearby_enemies]], built on move-5's `nearby_indices` with the same `u64::MAX` impossible-id fallback for callers without an in-world row (so the subscription is harmless in the lobby). The `BulletPatternEvent` rows — ephemeral, high-rate — are a `public, event` table: SpacetimeDB event tables don't persist or replay; a row exists only as a notification to whoever is subscribed when it commits. That is exactly the right shape for "a shot was fired", because the server has no further interest in the bullet — the client owns its whole lifecycle (visual flight, expiry by `lifetime`, collision) from the event's parameters alone.

The trade-off is that events are **not AOI-filtered**: every in-world client receives every enemy's shots and discards the ones for enemies it doesn't have. The `chunk_index` btree index on `BulletPatternEvent` hints a filtered view was planned, but nothing queries it today (Known gaps). At the current scale — a handful of regions, one step firing per attack per 100 ms tick — the firehose is cheap; it's the first thing to revisit if enemy counts grow.

### The puppet: composition, filtering, interpolation

```sync
![[00 End-to-End Timeline Flowchart#^enemy-8{seamless:true,title:false,marker:08.}]]
```

`default_enemy.tscn` is the live wiring site (the spawner's `EnemyScene` export in `main.tscn` points straight at it), and it composes the same component vocabulary the player entities use: [[InterpolationComponent.cs##public void SnapTo|InterpolationComponent]] for movement smoothing, `HealthComponent`/`StatsComponent` for the mirrored HP, `FactionComponent` (`Factions = 4`) and `DamageReceivingComponent` (collision layer 4) so bullets have something to hit — the combat half of that is [[09 Combat & Damage]]. [[Enemy.cs##public partial class Enemy : CharacterBody2D, IEntity|Enemy]] can't derive from the `Entity` base class because it needs `CharacterBody2D`, so it implements `IEntity` with its own `EntityRegistry`, the same pattern as `LocalPlayer` and `BulletManager`.

Every puppet receives the whole nearby row set and self-selects: both `OnEnemyRowInserted` and [[Enemy.cs##private void OnEnemyRowUpdated|OnEnemyRowUpdated]] bail unless `row.EnemyId == EnemyId` — the same per-puppet filtering move as `RemotePlayer` in move-5, made necessary because the binder is per-puppet but the table is global. The insert path is also the template-application path: it reads the matching `EnemyTemplates` row from the client cache to resolve `texture_id` → `SpriteFrames` and `max_hp`, so changing a template's art or health is a server-side data edit with no client deploy. Per-frame, [[Enemy.cs##public override void _Process(double delta)|_Process]] just plays Walk/Idle off the interpolation component's `Moving` flag — the puppet has no AI of its own, by design.

### Bullets: the server describes, the client simulates

```sync
![[00 End-to-End Timeline Flowchart#^enemy-9{seamless:true,title:false,marker:09.}]]
```

`BulletSpawnerComponent` is where the event's parameter structs become actual projectiles, via the BlastBullets2D GDExtension — a bullet engine whose `BulletFactory2D` node (the `BlastBullets` child in `bullet_manager.tscn`, reached through [[BulletManager.cs##[Export] public Node BlastBullets|an [Export] NodePath]] because GDExtension nodes can't be components) simulates thousands of bullets natively. The component configures a reusable `DirectionalBulletsData2D` *resource* in [[BulletSpawnerComponent.cs##private void SetupEnemyBullets|SetupEnemyBullets]] (texture size, collision layer/mask 2, a 4×4 hit shape), then per event sets its properties and calls `spawn_directional_bullets`. The C#↔GDExtension boundary is why everything goes through `Set`/`Call` with string names — there is no typed API to link against.

Two correctness details hide in the math. First, aiming: [[BulletSpawnerComponent.cs##private static Vector2? ResolveTargetPosition|ResolveTargetPosition]] reads the target's position out of the client's *own cached rows* (`LocalPlayerPosition` or `NearbyRemotePlayers`), so the aim point is where the target appears on *this* client — including its interpolation lag — which is the fairest possible aim from the target's perspective. Second, determinism: the jitter helpers ([[BulletSpawnerComponent.cs##private static ulong SplitMix64|SplitMix64]]/[[BulletSpawnerComponent.cs##private static float Jitter|Jitter]]) hash `(event_id, pellet_index, stream)` instead of using Godot's RNG, so pellet N of a given event lands identically on every client — the docstring notes a future audit script relies on this. (The one shared visual quirk: the "Arrow" art faces up-right, so [[BulletSpawnerComponent.cs##private const float ProjectileTextureAngleOffset|ProjectileTextureAngleOffset]] pre-rotates by 45° before the engine applies trajectory rotation.) Bullet textures resolve through the same `AllTextures` catalog as everything else via [[BulletSpawnerComponent.cs##private void ApplyBulletTexture|ApplyBulletTexture]] → `GameManager.GetResPath`, cached per texture id.

The event's `SourceStep` is stashed on the bullets as a [[BulletData.cs##public partial class BulletData|BulletData]] custom-data resource — the breadcrumb that lets the hit path ([[BulletHitRouterComponent.cs##public partial class BulletHitRouterComponent|BulletHitRouterComponent]]) report *which step* a hit came from. Where that report goes, and the whole damage pipeline, is [[09 Combat & Damage]].

## Known gaps / stubs

- **`Enemy.spawn_time` is write-only.** Set at insert in [[server/spacetimedb/src/enemy/methods.rs##pub fn spawn_enemy_archetype|spawn_enemy_archetype]] and never read anywhere — server or client.
- **`is_elite` is stored but never acted on.** Region spawns always pass `false` ([[server/spacetimedb/src/enemy/methods.rs##pub fn spawn_from_biome_regions|spawn_from_biome_regions]]); only the admin [[server/spacetimedb/src/main/admin.rs##pub fn spawn_enemy|spawn_enemy]] can set it, and no server logic or client script reads it (the puppet copies it into `Enemy.IsElite`, which nothing consumes — same for `Enemy.Phase`).
- **Step `damage` fields never reach the client.** `SingleStepDef`/`RepeatStepDef`/`MultiShot` all carry `damage`, but `BulletPatternEvent` has no `damage` field, so the value is stored and dropped. This is one facet of the unimplemented slash/bullet-despawn protocol in `server/spacetimedb/src/plan.md` — the full story is [[09 Combat & Damage]] → Known gaps.
- **`BulletPatternEvent.chunk_index` is a dead index.** Written at insert ([[server/spacetimedb/src/enemy/reducers.rs##pub fn tick_enemy_behavior|tick_enemy_behavior]]) but no view or query filters on it — the event table is subscribed whole and filtered client-side by `EnemyId` (enemy-7). Presumably scaffolding for a future AOI-filtered bullet view.
- **Seeded boss templates are unreachable in normal play.** `seed_region_def` gives every region the same "Enemy"/"Archer" pool, so TestBoss/TestBossP2–P6 only enter the world through the admin `spawn_enemy` reducer ([[12 Admin & Debug]]).

## Where to go next

The other half of every system here — what happens when a bullet overlaps a hurtbox, `report_enemy_hit`, phase advancement via `compute_phase`, aggro-on-hit, death and XP — is [[09 Combat & Damage]]. The admin reducers that hand-spawn bosses are [[12 Admin & Debug]], and the ghost-row guards the sim range checks depend on are [[13 Disconnect & Teardown]].
