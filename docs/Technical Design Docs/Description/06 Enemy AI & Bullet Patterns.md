# 06 Enemy AI & Bullet Patterns

## Assumed knowledge

[[01 Architecture & Sync Model]] — tables, reducers, subscriptions, views, and scheduled tables (this doc's two ticks were both inserted once at `^boot-1`) are used throughout without re-explaining them. [[02 Entity & Component Framework]] — the archetype-helper pattern (`spawn_enemy_archetype`/`despawn_enemy_archetype`) and the `BulletPatternEvent` event-table kind this doc covers in full. [[03 World & Hex Grid|03]] — torus wrap (`wrap_world_pos`, `wrapped_distance_sq`) and `BiomeRegion`, the table this doc's spawn logic reads. [[04 Player System|04]] — the shared AOI mechanism (`nearby_indices`/`surrounding_chunk_indices`) and `InterpolationComponent`, which enemies share with remote players.

## The 30-second version

Every enemy is built from an `EnemyTemplate` — static, shared content describing a sequence of HP-threshold **phases**, each with its own movement style and one or more **attack sequences** of bullet-pattern **steps**. Spawning an enemy (`spawn_enemy_archetype`) walks that template once and materializes it into a parallel tree of per-instance rows — the enemy's *own* copy of "where it is in its phases/attacks/steps" — because two enemies from the same template need independent progress through identical content. Two scheduled reducers drive everything from there: `tick_enemy_spawn` (every 2s) populates biome regions up to their cap when a player is nearby, and `tick_enemy_behavior` (every 100ms) advances every enemy's aggro, phase, attack sequence, and movement one tick at a time — but only for enemies within range of a connected player, so nothing simulates off-screen. A fired step becomes one ephemeral `BulletPatternEvent` row; the client's `BulletManager` turns that into an actual BlastBullets2D projectile, and a bullet landing back on a player resolves damage through the very same step reference that fired it. Taking damage the other direction — a player's bullet hitting the enemy — is `report_enemy_hit`, which is this doc's exit into [[07 Combat & Damage Math|07]]'s territory.

## System flowchart

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
```sync
![[99 End-to-End Timeline Flowchart#^enemy-11{seamless:true,title:false,marker:11.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^enemy-12{seamless:true,title:false,marker:12.}]]
```

## Main body

### Def tables vs. instance tables: content vs. an enemy's own progress through it

`enemy/def_tables.rs` and `enemy/instance_tables.rs` are two halves of the same shape [[03 World & Hex Grid|03]] uses for world-gen (`WorldDef`/`BiomeDef` vs. `BiomeRegion`/`TriangleTile`) and [[05 Item, Equipment & Enchantment System|05]] uses for the item catalog: defs are static, shared, and read many times by many instances; instance rows are one enemy's own mutable state. Concretely:

- **Defs** — `EnemyTemplate` (`template_id`, `max_hp`, `move_sim_factor`/`attack_sim_factor`, `aggro_lock_seconds`, `phases: Vec<PhaseDef>`), and the step-shape tables `MovementDef`/`SingleStepDef`/`RepeatStepDef`/`MultiStepDef` that a `PhaseDef`'s `AttackSequence`s point into by `def_id`. None of these ever change once seeded, and every enemy spawned from the same template reads the exact same rows. [[enemy/def_tables.rs##pub struct EnemyTemplate|EnemyTemplate]]
- **Instances** — `EnemyBehavior` (aggro/velocity/active-phase), `EnemyPhase` (one per phase, `phase_waiting`/`phase_loop_timer`/`move_cycle_started_at`), `EnemyAttack` (one per attack sequence, `step_index`/`step_waiting`/`step_timer`), `EnemySequenceStep` (one per step, just a pointer back into the matching def), and `RepeatStepInstance` — the one case where a *whole row*, not just a pointer, gets copied per-instance, because a `Repeat` step's `repeat_count` genuinely needs to be private to each enemy (`enemy-3`).

`PatternType` (`Ring`/`Volley`/`Curtain`/`Shotgun`/`Explosion`, each carrying its own param struct — `count`, `speed`, and pattern-specific shape like `Curtain`'s `angle_span`/`gap_index` or `Shotgun`'s `spread`) and `MovementBehavior` (`Stationary`/`Wander`/`Chase`/`Flee`, each with `speed` and, for the three non-stationary ones, an active/pause duty cycle) are the two open enums the whole system is built from — a step or a phase's behavior is entirely data, not a code branch per enemy type. `EnemyTarget` (`Idle`/`AggroTarget`) is the third: whether a given step aims at nothing in particular or at whoever the enemy is currently locked onto. [[enemy/def_tables.rs##pub enum PatternType|PatternType]]

### What's actually seeded

`main/seeds.rs`'s `seed_default_enemies` (called from `init`, `^boot-2`) defines two templates biome regions actually spawn — `"Enemy"` (wander + an idle ring burst) and `"Archer"` (flee-and-kite with two alternating attack sequences) — plus `"TestBoss"`, a five-phase boss (turret → sweeping fan → rusher → kiter → stationary nova) authored purely for manual testing via admin `spawn_enemy`, never placed by a `BiomeRegionDef`. `seed_test_boss_p2` through `seed_test_boss_p5`/`p6` (the "five hand-authored test-boss phase progressions" `^boot-2` names) are separate single-phase templates isolating specific pattern combinations — nested `Multi`/`Repeat` steps, six-direction bullet fans built from closures (`bullet_many`/`arrow_many`) — for testing one mechanic at a time without a five-phase fight around it. None of the `TestBossP*` templates are referenced by any `BiomeRegionDef` either; they only exist for an admin to spawn directly. `make_phase`/`make_sequence`/`seq_single`/`seq_repeat`/`seq_multi`/`multi_shot` are the authoring helpers that insert the def rows and hand back the lightweight `PhaseDef`/`AttackSequence`/`SequenceStepDef` values `EnemyTemplate::seed` assembles — hand-authoring content directly in Rust, since there's no external content-authoring tool for this data. [[main/seeds.rs##pub fn seed_default_enemies|seed_default_enemies]]

Every seeded `BiomeRegionDef` (`seed_region_def`, `^boot-2`) shares the identical `enemy_template_ids: ["Enemy", "Archer"]`, `max_enemies: 5`, `spawn_radius: 300.0` — today's content varies which ground textures a biome shows, not which enemies live in it; region-level enemy variety is schema the code already supports (`enemy_template_ids` is a `Vec`) but nothing has populated with different values per region yet.

### Two simulation radii, and why they're different

`CAMERA_VIEW_RADIUS` (`main/global.rs`) is derived once from the client's own viewport constants — `(CLIENT_VIEWPORT_HEIGHT_PX / CLIENT_CAMERA_ZOOM) / 2.0` — so it stays correctly scaled if the client's camera setup ever changes, rather than being a hardcoded world-unit radius that would silently drift out of sync. Every `EnemyTemplate` scales that shared radius by two of its own per-template factors: `move_sim_factor` (`has_player_in_simulation_range`, `enemy-4`) governs whether the enemy simulates at all — movement, aggro upkeep — and is deliberately the larger of the two, so an enemy is already approaching by the time it's close; `attack_sim_factor` (`enemy-7`) is tighter and gates whether it's allowed to actually fire, so nothing shoots from a position no connected client can see. Both checks reuse the exact same `has_player_in_simulation_range`/`nearby_player_position_chunks` helper with a different `range` argument — one function, two call sites, two purposes. [[enemy/methods.rs##pub fn has_player_in_simulation_range|has_player_in_simulation_range]]

### Aggro and targeting

`recompute_aggro`/`find_nearest_player_id` (`enemy-5`) do a **full, unfiltered scan** of every `PlayerPosition` row filtered only to identities that are currently `logged_in_player` (so a `PlayerPosition` orphaned by a disconnect-without-death, which [[01 Architecture & Sync Model|01]]'s `end-3` explains outlives the session, is never targeted) — not an AOI-scoped query the way `nearby_enemies`/`nearby_terrain_tiles` are. This is fine at today's content scale (a handful of enemies, a handful of players) but is a straight `O(players)` scan run once per enemy per aggro-recompute, unlike every other per-player lookup in this doc set, which goes through a chunk-indexed AOI helper first. `aggro_locked_until` (a per-template `aggro_lock_seconds` duration from the moment aggro was acquired) is what keeps an enemy from re-targeting mid-fight every time `recompute_aggro` *could* run again — but `enemy-5` only calls it on the tick an enemy newly enters simulation range in the first place, so the lock mostly matters for the two re-aggro paths `deal_damage_to_enemy` ([[07 Combat & Damage Math|07]]) adds on top: a hit that crosses a phase threshold calls `recompute_aggro` again unconditionally, regardless of the lock (retargeting the nearest player at that moment, not necessarily whoever landed the hit); a hit that *doesn't* cross a threshold instead re-aggros directly onto the attacker (`ctx.sender()`), but only once the existing lock has actually expired.

`resolve_step_target` (`enemy-8`) is the only place an `EnemyTarget` def value actually becomes a concrete `Option<Identity>` on the fired event — `Idle` steps (the untargeted ring bursts and novas) always carry `None` regardless of whether the enemy has an aggro target at all, so an idle-style attack looks the same whether or not a player is nearby to aim at.

### Bullet events: fire-once rows, priced by the same reference that fired them

`BulletPatternEvent` rows never carry damage directly — `damage` is looked up fresh from wherever `source_step: SequenceStepRef` points, once a hit is actually reported. For `Single`/`Multi` steps that's the shared *def* row (`SingleStepDef`, or one `MultiShot` inside a `MultiStepDef`); for a `Repeat` step it's that enemy's own `RepeatStepInstance` row specifically (not the shared `RepeatStepDef` its `damage` was copied from at spawn time, `enemy-3`) — a `Repeat` step's damage is technically per-instance data, even though nothing ever actually mutates it after the copy. This is deliberate: the event only needs to describe *how the bullet looks and moves* for rendering, and `player/reducers.rs`'s `report_hit` ([[07 Combat & Damage Math|07]]'s entry point — reached when a bullet's `BulletData.SourceStep` overlaps a player's hurtbox, routed there by `BulletHitRouterComponent`/`DamageReceivingComponent.ProcessBulletHit`) re-resolves the same `def_id`/`repeat_id`/`(def_id, shot_index)` reference to look the damage back up at the moment it's needed, rather than the event carrying a redundant copy that could drift from the source if it were ever hot-swapped mid-fight via `upsert_enemy_template` ([[09 Admin, Debug & World Lifecycle|09]]). One consequence worth naming: if the enemy that fired a `Repeat` bullet is despawned (its `RepeatStepInstance` deleted along with the rest of its behavior tree) in the brief window before that bullet lands, `report_hit` finds nothing to resolve and the hit is silently priced as "attack step not found" rather than dealing damage — an edge case, not exercised by anything in this doc's read of the code, but a real consequence of pricing a bullet from live per-enemy state instead of a stable def. The event table being declared `event` rather than a normal persisted table ([[02 Entity & Component Framework|02]]) means none of this is queryable after the fact — there's no bullet-history table to audit, only the fire-once notification each connected client saw at the moment it happened.

### The BlastBullets2D bridge, at interface level

`BulletSpawnerComponent` talks to the BlastBullets2D GDExtension plugin through two long-lived resource objects it builds once in `OnRegistered` — `EnemyBulletSpawner`/`PlayerBulletSpawner`, both `ClassDB.Instantiate("DirectionalBulletsData2D")` — rather than one object per bullet. Spawning a batch of bullets is: set the shared config (`texture_size`, `max_life_time`, `bullets_custom_data` — the `BulletData` resource carrying the firing `SequenceStepRef` so a later overlap can be routed back to `report_hit`, `all_bullet_speed_data` via a `BulletSpeedData2D` helper object), build a `Godot.Collections.Array<Transform2D>` of one transform per bullet (position + firing angle), and hand both to `BlastBullets.Call("spawn_directional_bullets", spawnerData)` on the `BulletFactory2D` node declared in `bullet_manager.tscn`. Every one of `enemy-11`'s five pattern spawners (`SpawnRing`/`SpawnVolley`/`SpawnCurtain`/`SpawnShotgun`/`SpawnExplosion`) is just a different way of building that transform array and, for the three with per-pellet variance, a different lifetime/speed per call via `SpawnSingle`. Texture resolution goes through the same catalog/texture-cache path every other textured entity in this project uses (`GameManager.GetResPath` → `GD.Load<SpriteFrames>` → `GetFrameTexture` → baked into an `ImageTexture`, cached per `textureId` so repeated patterns with the same texture don't reload it). None of `BulletFactory2D`'s or `DirectionalBulletsData2D`'s own internals are this project's concern — only that this Set/Call surface is the entire hand-off point between this project's code and the plugin. [[BulletSpawnerComponent.cs##public void SpawnEnemyBullet|SpawnEnemyBullet]]

### `is_elite` / `immortal`: one flag does something, one doesn't (yet)

Both flags exist only on `Enemy` and only ever get set by admin `spawn_enemy` (`^boot-2`'s biome-region spawns always pass `false, false`) — neither is naturally occurring content today. `immortal` has a real effect: `report_enemy_hit` (`enemy-12`) no-ops immediately on an immortal enemy, before `deal_damage_to_enemy` ever runs, which is how an admin can drop a stationary target dummy into the world without it dying to the first hit. `is_elite` is plumbed all the way through — admin reducer parameter → `Enemy.is_elite` column → `nearby_enemies` view → `Enemy.cs`'s `IsElite` property — but nothing anywhere reads it back out: no stat scaling, no visual distinction, no different loot. It's a flag with a name and no behavior, unlike `immortal`'s one real gate. [[enemy/instance_tables.rs##pub struct Enemy {|Enemy]]

## Known gaps / stubs

- **`admin.rs`'s `spawn_enemy` double-inserts the `EnemyBehavior` row, bypassing `spawn_enemy_archetype` entirely.** `build_enemy_behavior(ctx, &template)` already inserts the behavior row itself (`enemy-3`) and returns it *with its real, already-committed `behavior_id`* — but `spawn_enemy` then does `ctx.db.enemy_behavior().insert(build_enemy_behavior(ctx, &template))`, inserting that same already-live row a second time rather than using its returned value directly. `behavior_id` is `#[primary_key] #[auto_inc]`; whether SpacetimeDB's insert treats a second insert carrying a non-zero, already-taken primary key as a hard conflict (aborting the whole reducer's transaction, in which case admin `spawn_enemy` fails outright today rather than leaving anything behind) or silently mints a second, unrelated `behavior_id` for it (in which case the `Enemy` row this reducer inserts ends up pointing at whichever id `inserted_behavior` actually got — possibly a *different*, phase/attack/step-less id than the one `build_enemy_behavior` just spent a whole function populating) isn't something this doc's read-only pass over the code can settle definitively; either reading is broken, just differently. The fix is one line: call `spawn_enemy_archetype` instead of re-deriving its two-step body incorrectly. [[main/admin.rs##pub fn spawn_enemy|spawn_enemy]] · [[enemy/methods.rs##pub fn spawn_enemy_archetype|spawn_enemy_archetype]]
- **`admin.rs`'s `despawn_enemy` leaks the entire behavior tree.** It's a single `ctx.db.enemy().enemy_id().delete(&enemy_id)` and nothing else — no call to `delete_enemy_behavior`/`despawn_enemy_archetype`. Every `EnemyBehavior`/`EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance` row that admin-spawned enemy ever had stays in the database forever, orphaned from any `Enemy` row that could reference it — invisible to every subscriber (nothing queries orphaned behavior rows by anything but `behavior_id`, and nothing has that id anymore once the `Enemy` row is gone) but permanently taking up space. The natural despawn path (`deal_damage_to_enemy` on a kill, [[07 Combat & Damage Math|07]]) gets this right via `despawn_enemy_archetype` — only the admin manual-despawn path is affected. [[main/admin.rs##pub fn despawn_enemy|despawn_enemy]] · [[enemy/methods.rs##pub fn despawn_enemy_archetype|despawn_enemy_archetype]]
- **`recompute_aggro`/`find_nearest_player_id` scan every logged-in player's position unfiltered**, rather than going through an AOI-scoped query the way every other per-player lookup in this doc set does. Not observable as a bug at today's player/enemy counts, but it's the one hot path in this system that doesn't share the chunk-indexed pattern the rest of the codebase converged on.
- **`is_elite` has no gameplay effect.** It's threaded end-to-end (admin reducer param → `Enemy` column → subscribed view → client puppet property) but nothing scales stats, changes visuals, or alters drops based on it — see above.
- **Region-level enemy variety is unused schema.** `BiomeRegionDef.enemy_template_ids`/`max_enemies`/`spawn_radius` support per-region difference, but every seeded region shares identical values (`seed_region_def`) — content, not code, is what's missing here.
- **`TestBossP2`–`TestBossP6` and `TestBoss` itself have no `BiomeRegionDef` reference** — they only exist to be spawned manually via admin `spawn_enemy` for testing specific pattern combinations, not as part of the ordinary spawn loop.

## Where to go next

[[07 Combat & Damage Math]] picks up both directions of damage this doc hands off — `report_enemy_hit`/`deal_damage_to_enemy` (`enemy-12`) for a player's bullet landing, and `report_hit` (referenced from `enemy-8`) for an enemy's `BulletPatternEvent` landing on a player — plus the aggro re-lock `deal_damage_to_enemy` performs into this doc's `EnemyBehavior` rows. [[09 Admin, Debug & World Lifecycle]] is where the `spawn_enemy`/`despawn_enemy` bugs flagged above get their status tracked going forward, rather than re-deriving the explanation this doc already gives.
