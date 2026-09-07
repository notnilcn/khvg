# 00 Table Map — every table, who changes it and who reads it

The per-table spine every system doc transcludes from. Each entry tells one story in two lenses: **Changers** (which reducer, scheduled tick, lifecycle hook, or seed writes rows) and **Readers** (which view or raw-table subscription carries rows to the client and which binder component consumes them, plus server-side reads that matter). System docs sync-embed these entries instead of re-typing them.

Sections are one per folder in `server/spacetimedb/src/`, in alphabetical directory-listing order (the phase-0 order — a new folder means a new section here plus a new `04+` doc). That order also fixes the `04+` doc numbering: 04 Anticheat, 05 Chest, 06 Combat, 07 Enemy, 08 Item, 09 Main, 10 Player, 11 Status, 12 World. Entries within a section follow table-definition order in the source file(s).

Conventions used below: a **view accessor** is a parameterized server view (see `03 Connection, Subscriptions & Views.md`) — the client subscribes to it by name. A **wave** is one of the `BaseTables` / `LobbyTables` / `GameTables` subscription waves in [[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber]]. A **binder** is a `TableBinderComponent` child whose row events are wired in a `.tscn`. "Replay on/off" is the binder's `ReplayExistingRows` flag — on for persistent tables, off for event tables. `public` means clients *can* subscribe; "never updated" / "server-only" / "public but unsubscribed" are first-class outcomes, stated explicitly.

Prerequisite maintainer refs (narrative complement — not restated here): [[AGENTS.md|root AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[server/spacetimedb/src/enemy/AGENTS.md|enemy AGENTS.md]], [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]].

## Anticheat

### File index

Generated entry points: [[server/spacetimedb/src/anticheat/pointers.md|anticheat pointers]].

### Tables

**PlayerHitFlag** — [[server/spacetimedb/src/anticheat/tables.rs#PlayerHitFlag|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]] — any in-world caller reports that `hit_player` was seen hit; self-reports are silently ignored (the victim's own client reports damage through `report_hit`, this table collects only independent corroboration).
  - Update: never updated.
  - Delete: the 1 Hz [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] pass sweeps rows older than `HIT_FLAG_MAX_AGE_SECONDS` (60 s).
- **Client readers** — none. Server-only table (not `public`): the client never subscribes; it only *writes* via the reducer. The write path to its visible end is: enemy bullet overlaps a remote puppet's hurtbox → `BulletHitRouterComponent` forwards to [[client/Scripts/Components/Combat/RemoteHitWitnessComponent.cs#ProcessBulletHit|RemoteHitWitnessComponent.ProcessBulletHit]] (declared inline in [[client/Scenes/non_local_player.tscn|non_local_player.tscn]]) → calls the `FlagPlayerHit` reducer. Damage itself is still reported exclusively by the victim's own client.
- **Server readers** — `settle_store_mark` in [[server/spacetimedb/src/status/methods.rs#settle_store_mark|status/methods.rs]] corroborates a `StoreDamage` NoHits-wager 0-hits claim: flags on the claimant filed after the mark's `last_event_at` downgrade a 2x payout to 1x once they reach `NO_HIT_WITNESS_THRESHOLD`.

^table-player-hit-flag

## Chest

### File index

Generated entry points: [[server/spacetimedb/src/chest/pointers.md|chest pointers]].

### Tables

**Chest** — [[server/spacetimedb/src/chest/mod.rs#Chest|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]] (admin-only world authoring; wraps the position onto the torus and stamps `chunk_index` from the wrapped coordinate).
  - Update: never updated (fixed world position).
  - Delete: no delete path — chests are permanent once placed.
- **Client readers** — via the `nearby_chests` view (same AOI chunk-index OR-chain as `nearby_loot_drops`) in the Game wave → `EntitySpawnerComponent`'s replaying `NearbyChests` binder (declared inline in [[client/Scenes/game.tscn|game.tscn]]) → spawns/despawns the [[client/Scenes/chest.tscn|chest.tscn]] puppet (`Items/Chest.cs`) at the row's x/y.
- **Server readers** — `take_from_chest` reads the row for the range check (wrap-aware `INTERACT_RADIUS` against the caller's position row) and for the parent `chunk_index`.

^table-chest

**ChestItem** — [[server/spacetimedb/src/chest/mod.rs#ChestItem|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/chest/mod.rs#add_item_to_chest|add_item_to_chest]] (admin-only, single item; validates the catalog `item_id`) and [[server/spacetimedb/src/chest/mod.rs#add_all_items_to_chest|add_all_items_to_chest]] (admin-only bulk fill — one row per catalog item, its own reducer rather than a magic item_id). Both mirror the parent chest's `chunk_index` onto the row so the view filters on the chunk btree directly.
  - Update: never updated.
  - Delete: [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] deletes the row on take — unless `regenerating`, in which case the surviving row IS the next copy (the chest regenerates by never running out).
- **Client readers** — via the `nearby_chest_items` view (same AOI filter over the row's own mirrored `chunk_index`) in the Game wave → the replaying `NearbyChestItemsBinder` in [[client/Scenes/chest.tscn|chest.tscn]] → an open chest panel rebuilds draggable `ChestSlotComponent` rows (`Scenes/UI/chest_slot.tscn`) from the cache; dragging one onto a player slot reports `TakeFromChest`.
- **Server readers** — `take_from_chest` guards on the row: `regenerating` rows enforce the per-player `CHEST_TAKE_COOLDOWN_SECONDS` cooldown via ^table-chest-take-cooldown instead of being consumed.

^table-chest-item

**ChestTakeCooldown** — [[server/spacetimedb/src/chest/mod.rs#ChestTakeCooldown|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] inserts one row per (profile, chest) on the first regenerating take.
  - Update: `take_from_chest` restamps `last_taken` on every further regenerating take.
  - Delete: never deleted (one row per (profile, chest), restamped forever).
- **Client readers** — none. Server-only table (not `public`).
- **Server readers** — `take_from_chest` rejects regenerating takes within `CHEST_TAKE_COOLDOWN_SECONDS` of `last_taken`.

^table-chest-take-cooldown

## Combat

This module owns **no tables** — it is the shared damage pipeline (`mod.rs` policy types plus `calc.rs` pure math plus `apply.rs` sinks: [[server/spacetimedb/src/combat/mod.rs#DamageRequest|DamageRequest]] / `IncomingDamageOptions`, `compute_player_damage` / `compute_incoming_damage`, `apply_damage_to_enemy` / `deal_damage_to_enemy` / `deal_damage_to_player` / `heal_player`). Every reducer that resolves base damage delegates here, so the module is told as a cross-table flow in its doc (`06 Combat Tables.md`), not as table entries. Its row effects land on other modules' tables: damage subtracts `PlayerData.hp` (kills delegate death cleanup to `kill_player` in [[server/spacetimedb/src/player/methods.rs#kill_player|player/methods.rs]]) and `Enemy.hp` (kills call `despawn_enemy_archetype` plus `internal_gain_xp` for `xp_credit`; survivors get a phase recompute and re-aggro).

## Enemy

### File index

Generated entry points: [[server/spacetimedb/src/enemy/pointers.md|enemy pointers]]. Authoring detail (tick/phase/sequence semantics, field encodings, add-an-enemy recipe) lives in [[server/spacetimedb/src/enemy/AGENTS.md|enemy AGENTS.md]].

### Tables — static definitions (`def_tables.rs`)

**MovementDef** — [[server/spacetimedb/src/enemy/def_tables.rs#MovementDef|definition]]

- **Changers**
  - Insert: content seeds via `make_phase` in `defs/` (upsert-by-id through `EnemyTemplate::seed`-style helpers) and the admin [[server/spacetimedb/src/main/admin.rs#upsert_movement_def|upsert_movement_def]] (pass `def_id: 0` to insert, an existing id to update).
  - Update: `upsert_movement_def` only; the simulation never rewrites definitions.
  - Delete: no delete path.
- **Client readers** — none. `public` but unsubscribed: definitions are server-side simulation inputs; the client sees only their consequences (`Enemy` movement rows, `BulletPatternEvent` volleys).
- **Server readers** — `apply_movement` in [[server/spacetimedb/src/enemy/methods.rs#apply_movement|enemy/methods.rs]] reads the phase's movement each behavior tick (Goal × Pattern × Avoidance steering).

^table-movement-def

**SingleStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#SingleStepDef|definition]]

- **Changers**
  - Insert: content seeds (`defs/` raw step helpers) and admin [[server/spacetimedb/src/main/admin.rs#upsert_single_step_def|upsert_single_step_def]].
  - Update: `upsert_single_step_def` only.
  - Delete: no delete path.
- **Client readers** — none (`public` but unsubscribed — same definition rule as ^table-movement-def).
- **Server readers** — `tick_sequence` resolves `SequenceStepRef::Single` against this table each behavior tick.

^table-single-step-def

**RepeatStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#RepeatStepDef|definition]]

- **Changers**
  - Insert: content seeds and admin [[server/spacetimedb/src/main/admin.rs#upsert_repeat_step_def|upsert_repeat_step_def]].
  - Update: `upsert_repeat_step_def` only.
  - Delete: no delete path.
- **Client readers** — none (`public` but unsubscribed).
- **Server readers** — `tick_sequence` copies the def into a per-enemy ^table-repeat-step-instance row at build time and then ticks the copy (`repeat_count`, catch-up firing capped at `MAX_CATCHUP_SHOTS`, `angle_step` spin accumulation).

^table-repeat-step-def

**MultiStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#MultiStepDef|definition]]

- **Changers**
  - Insert: content seeds and admin [[server/spacetimedb/src/main/admin.rs#upsert_multi_step_def|upsert_multi_step_def]].
  - Update: `upsert_multi_step_def` only.
  - Delete: no delete path.
- **Client readers** — none (`public` but unsubscribed).
- **Server readers** — `tick_sequence` fans a visit out to all `shots` at once (capped at `MAX_MULTI_SHOTS`/visit).

^table-multi-step-def

**EnemyTemplate** — [[server/spacetimedb/src/enemy/def_tables.rs#EnemyTemplate|definition]]

- **Changers**
  - Insert: `seed_default_textures`-style content seeds — `seed_default_enemies` in [[server/spacetimedb/src/main/seeds.rs#seed_default_enemies|main/seeds.rs]] plus per-enemy `seed(ctx)` fns in `defs/` (`gorgon_boss.rs` is the reference), upserting by `template_id`; difficulty variants are separate rows (`Foo_Easy` … `Foo_Madness`), not a runtime flag.
  - Update: admin [[server/spacetimedb/src/main/admin.rs#upsert_enemy_template|upsert_enemy_template]] (runtime enemy authoring).
  - Delete: no delete path.
- **Client readers** — via the global `enemy_templates` view in the Game wave → `Enemy.cs` reads the row for its `HealthComponent` max_hp mirror (`SetFromServer(hp, maxHp)`) and loads the catalog `res_path3d` model for the 3D mirror; `TestBoss` doubles as the every-movement/every-pattern showcase.
- **Server readers** — `spawn_enemy_archetype` builds the whole behavior tree from the template's `phases`; the combat sink recomputes the active phase from `hp_threshold`s via `compute_phase`; `tick_enemy_behavior` consults `stun_behavior` and the `move_sim_factor` / `attack_sim_factor` sim gating.

^table-enemy-template

### Tables — runtime instances (`instance_tables.rs`)

**RepeatStepInstance** — [[server/spacetimedb/src/enemy/instance_tables.rs#RepeatStepInstance|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype|spawn_enemy_archetype]] copies the `RepeatStepDef` into a per-enemy row so `repeat_count` advances independently.
  - Update: `tick_sequence` advances `repeat_count` / `step_timer` on every behavior tick.
  - Delete: [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] removes the tree (never orphan behavior rows).
- **Client readers** — none in practice: `public` but unsubscribed (no view, not in any wave) — server-side tick state.
- **Server readers** — `tick_sequence` only.

^table-repeat-step-instance

**EnemySequenceStep** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemySequenceStep|definition]]

- **Changers**
  - Insert: `spawn_enemy_archetype` builds one row per compiled sequence step.
  - Update: never updated (step position advances on the parent `EnemyAttack.step_index` instead).
  - Delete: `despawn_enemy_archetype`.
- **Client readers** — none (`public` but unsubscribed — tick state).
- **Server readers** — `tick_sequence` walks the steps in order.

^table-enemy-sequence-step

**EnemyAttack** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyAttack|definition]]

- **Changers**
  - Insert: `spawn_enemy_archetype` (one row per attack sequence in the active phase set).
  - Update: `tick_sequence` (`step_index`, `step_timer`, `step_waiting` / `start_delaying` / `loop_waiting` pacing flags; `loop_delay >= 0` sequences self-restart independently of the phase).
  - Delete: `despawn_enemy_archetype`.
- **Client readers** — none (`public` but unsubscribed — tick state).
- **Server readers** — `tick_phase` / `tick_sequence` only.

^table-enemy-attack

**EnemyPhase** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyPhase|definition]]

- **Changers**
  - Insert: `spawn_enemy_archetype` (one row per `PhaseDef`).
  - Update: `tick_phase` (`phase_waiting` / `phase_loop_timer` loop silence, `move_cycle_started_at` for move/pause cycles); the combat sink flips `active_phase_index` on the behavior row when HP crosses a threshold.
  - Delete: `despawn_enemy_archetype`.
- **Client readers** — none (`public` but unsubscribed — tick state).
- **Server readers** — `tick_phase` only; a phase restarts only when EVERY sequence finishes.

^table-enemy-phase

**EnemyBehavior** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyBehavior|definition]]

- **Changers**
  - Insert: `spawn_enemy_archetype`.
  - Update: `tick_enemy_behavior` (`aggro_target` / `aggro_locked_until`, `was_simulating`, `active_phase_index`, the `velocity_x/y` unit-heading memory).
  - Delete: `despawn_enemy_archetype`.
- **Client readers** — none in practice: `public` but unsubscribed (no view exposes it) — the client-facing movement lives on the `Enemy` row's angle+scalar pair.
- **Server readers** — the behavior tick and `apply_movement` (Meander's persistent sway heading reads `velocity_x/y`).

^table-enemy-behavior

**Enemy** — [[server/spacetimedb/src/enemy/instance_tables.rs#Enemy|definition]]

- **Changers**
  - Insert: `spawn_enemy_archetype`, called by the scheduled [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_spawn|tick_enemy_spawn]] (biome-region spawning, disarmed at `init` — re-armed by admin [[server/spacetimedb/src/main/admin.rs#toggle_enemy_spawning|toggle_enemy_spawning]]) and by admin [[server/spacetimedb/src/main/admin.rs#spawn_enemy|spawn_enemy]].
  - Update: `tick_enemy_behavior` (position, `movement_direction`/`movement_speed` angle+scalar, `phase`); the combat sink `apply_damage_to_enemy` (hp subtraction, phase recompute, re-aggro; stagger-bar fills from bashes); the 1 Hz status `tick` (stagger decay, enemy-DoT ticks).
  - Delete: `despawn_enemy_archetype`, called on kill (plus `internal_gain_xp` for `xp_credit`) and by admin `despawn_enemy` (which also calls `on_enemy_killed` so no status rows dangle).
- **Client readers** — via the `nearby_enemies` AOI view in the Game wave → `EntitySpawnerComponent`'s replaying `NearbyEnemies` binder → spawns the [[client/Scenes/default_enemy.tscn|default_enemy.tscn]] puppet (`Players/Enemies/Enemy.cs`): row hp + template max_hp feed its `HealthComponent`, the angle+scalar pair feeds `InterpolationComponent.SetTarget` for dead reckoning, `stagger` mirrors onto `Enemy.Stagger` for the debuff indicator's bar; the 3D model loads from the template's catalog `res_path3d`. Bullet patterns bypass the puppet — one shared `BulletPatternEventBinder` on `BulletSpawnerComponent` renders volleys verbatim.
- **Server readers** — chunk-gated simulation set (enemies inside `SIMULATION_CHUNK_RINGS` of a logged-in player, capped at `BEHAVIOR_TICK_ENEMY_BUDGET` per tick — no full scan); `players_near_point`-style AOI for AoE; `on_enemy_killed` purges statuses referencing the dead id.

^table-enemy

**EnemySpawnSchedule** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemySpawnSchedule|definition]]

- **Changers** — the only changer is whoever arms/disarms the job row: `init` deliberately inserts NO row (spawning starts disarmed); admin `toggle_enemy_spawning(true/false)` inserts/deletes it to drive `tick_enemy_spawn` every 2 s.
- **Client readers** — none (scheduled table, not `public`).
- **Server readers** — the scheduler fires `tick_enemy_spawn` per row.

^table-enemy-spawn-schedule

**EnemyBehaviorSchedule** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyBehaviorSchedule|definition]]

- **Changers** — armed once by [[server/spacetimedb/src/main/lifecycle.rs#init|init]] (100 ms `BEHAVIOR_TICK_DT` interval); never disarmed.
- **Client readers** — none (scheduled table, not `public`).
- **Server readers** — the scheduler fires `tick_enemy_behavior` per row.

^table-enemy-behavior-schedule

**BulletControlEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#BulletControlEvent|definition]]

- **Changers**
  - Insert: `activate_ability` → `apply_ability_effect` in [[server/spacetimedb/src/player/reducers.rs#activate_ability|player/reducers.rs]] appends the validated cast (there is deliberately no `control_bullets` reducer — validation always runs). Geometry encodes per `BulletControlKind` (documented on the table: Delete/Split center+radius; Attract adds homing target+linger; rect kinds encode origin + far end + width; SlashRect repurposes `duration` as the damage threshold's f32 bits; WallRect as linger seconds).
  - Update / Delete: never — ephemeral event table (rows are a broadcast, not state).
- **Client readers** — raw `public event` table in the Game wave, replay OFF → the `BulletControlEventBinder` child of `BulletControllerComponent` (wired in [[client/Scenes/game.tscn|game.tscn]]) → each client resolves the proximity query against its own live bullets (`DeleteNear`/`SplitNear`/`AttractNear`/`DeleteInRect`/`SplitInRect`/`SlashInRect` + lingering attract/wall zones); the caster applies the cast optimistically via `TryActivateAbility` and skips its own echo by `cast_by`. Visible end: enemy bullets vanish/split/curve mid-flight plus the black slash-rect flash polygon.
- **Server readers** — none (the server tracks no live bullets — it validates and rebroadcasts).

^table-bullet-control-event

**AbilityVisualEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#AbilityVisualEvent|definition]]

- **Changers**
  - Insert: the P5 Spell arm of `apply_ability_effect` appends one row per cast (damage itself is applied server-side in the same dispatch — the row is visual-only relay; per-kind encoding documented on the table).
  - Update / Delete: never — ephemeral event table.
- **Client readers** — raw `public event` table in the Game wave, replay OFF → the `AbilityVisualEventBinder` child of `SpellVisualComponent` (declared inline in [[client/Scenes/game.tscn|game.tscn]]) → `Render` draws the per-`AbilityVisualKind` Polygon2D flash (Circle ring, Line rectangle, Cone wedge, Meteor/Converge streak variants) that fades after the event's duration; the caster renders optimistically and skips its own `cast_by` echo.
- **Server readers** — none.

^table-ability-visual-event

**BulletFireAttestation** — [[server/spacetimedb/src/enemy/instance_tables.rs#BulletFireAttestation|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/enemy/methods.rs#attest_bullet_fire|attest_bullet_fire]] stamps one row per fired `BulletPatternEvent` (`remaining` = the event's pellet pool).
  - Update: [[server/spacetimedb/src/player/reducers.rs#report_hit|report_hit]] decrements `remaining` per accepted claim — replaying one `event_id` past its pellets is rejected.
  - Delete: consumed rows die on acceptance; `tick_status_effects` sweeps rows past `BULLET_ATTESTATION_TTL_SECONDS`.
- **Client readers** — none. Server-only table (not `public`): clients only send the `event_id` back.
- **Server readers** — `report_hit` attests every victim claim against this row (pellet-pool decrement + TTL) before delegating to the damage sink.

^table-bullet-fire-attestation

**BulletPatternEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#BulletPatternEvent|definition]]

- **Changers**
  - Insert: `tick_phase`/`tick_sequence` append one row per shot (`damage` resolved at insert; angles/offsets baked server-side at fire time; `chunk_index` stamped for potential AOI use).
  - Update / Delete: never — ephemeral event table.
- **Client readers** — raw `public event` table in the Game wave, replay OFF → ONE shared `BulletPatternEventBinder` on `BulletSpawnerComponent` (bound before any enemy spawns; rows are self-contained with absolute origin, so no per-enemy fan-out and no lost volley during a puppet's deferred-spawn window) → per-`PatternType` dispatch into the BlastBullets2D factory; `angular_speed`/`speed_acceleration` map to rotation/speed data. Visible end: the enemy bullet hell itself on screen (plus the 3D mirror in `Bullets3DComponent`); `BulletData.Damage` (stamped from this row) is what `SlashInRect` compares against.
- **Server readers** — none beyond the attestation writer above.

^table-bullet-pattern-event

## Item

### File index

Generated entry points: [[server/spacetimedb/src/item/pointers.md|item pointers]]. The ability-authoring recipe (seed-only vs new effect type) lives in [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]].

### Tables

**Item** — [[server/spacetimedb/src/item/tables.rs#Item|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/item/seeds.rs#seed_world_items|seed_world_items]] (the whole catalog — `Bow` two-pattern, innate-toggle `Fan Bow`, P6 class-gated toggle weapons, `Null Sigil`/`Delete Orb` bullet-delete abilities; `Item::seed` upserts by key so reseeds are idempotent — every publish wipes the DB, so unseeded items don't exist).
  - Update: admin [[server/spacetimedb/src/item/reducers.rs#upsert_item|upsert_item]] (runtime catalog edit).
  - Delete: admin [[server/spacetimedb/src/item/reducers.rs#remove_item|remove_item]].
- **Client readers** — via the `all_items` view in the Base wave → `CatalogComponent`'s replaying binder (`GetItem`) → item icons/descriptions in `ItemSidebar`, weapon composition in `EffectiveWeaponResolver`/`CombatComponent` (toggle options = own `Weapon` behaviors + `WeaponToggle` grants, indexed by the slot's `active_toggle`), drop/chest sprites in `Drop.cs`/`Chest.cs`. Runtime state (cooldown/charges) is never on this row — it lives on ^table-player-inventory-slot.
- **Server readers** — capability lookups are behavior-based (`ability_behavior` / `consumable_behavior` in [[server/spacetimedb/src/item/methods.rs#ability_behavior|item/methods.rs]]), never `equip_slot`-based; `resolve_effective_weapon` composes the toggle-selected behavior; `check_stat_requirements` gates equipping.

^table-item

**Enchantment** — [[server/spacetimedb/src/item/tables.rs#Enchantment|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/item/seeds.rs#seed_world_enchantments|seed_world_enchantments]] (`splitting` shot-doubler, `echoing` on-ability buff, innate-only `point_blank`/`scatter_fan`; `Enchantment` seeds via `impl Seed for Enchantment` in [[server/spacetimedb/src/main/seeds.rs#Seed|main/seeds.rs]]).
  - Update: admin `upsert_enchantment`.
  - Delete: no delete reducer (remove the socket via `remove_enchantment` on the player side instead).
- **Client readers** — via the `all_enchantments` view in the Base wave → `CatalogComponent` (`GetEnchantment(s)`, `EnchantmentsChanged`, `CatalogVersion`) → socket/remove buttons and composition lines in `ItemSidebar`, toggle-grant folding in `EffectiveWeaponResolver` (fold order — slot ascending, innate before socketed — must match the server exactly).
- **Server readers** — `equipped_enchantment_behaviors` folds socketed **and innate** enchantments into `recompute_stats` and weapon resolution; `innate_enchantment_ids` on the item always apply, don't eat sockets, can't be removed.

^table-enchantment

## Main

### File index

Generated entry points: [[server/spacetimedb/src/main/pointers.md|main pointers]].

### Tables

**PlayerPositionDebug** — [[server/spacetimedb/src/main/debug.rs#PlayerPositionDebug|definition]]

- **Changers**
  - Insert / Update: the scheduled [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug|tick_player_position_debug]] (1 Hz while armed) mirrors every `PlayerPosition` row plus hex and facing into this row (insert or update per player; stale ids pruned).
  - Delete: the same tick prunes rows for vanished players; admin [[server/spacetimedb/src/main/debug.rs#toggle_debug|toggle_debug]] clears the whole table when disarming.
- **Client readers** — none. Server-only table (not `public`; inspect via `spacetime sql`). Note: `client/AGENTS.md`'s old "paired with server main/debug.rs" line is stale — `DebugOverlay.cs` (declared inline in [[client/Scenes/game.tscn|game.tscn]]) is a purely local perf HUD (FPS/memory/enemy count) and never touches this table.
- **Server readers** — none (debug mirror only).

^table-player-position-debug

**PlayerPositionDebugSchedule** — [[server/spacetimedb/src/main/debug.rs#PlayerPositionDebugSchedule|definition]]

- **Changers** — the only changer is whoever arms/disarms the job row: admin `toggle_debug` inserts/deletes it (1 s interval driving `tick_player_position_debug`).
- **Client readers** — none (scheduled table, not `public`).

^table-player-position-debug-schedule

(Lifecycle `init` / `client_connected` / `client_disconnected` in [[server/spacetimedb/src/main/lifecycle.rs#init|lifecycle.rs]] and the admin reducers in [[server/spacetimedb/src/main/admin.rs#claim_admin|admin.rs]] write other modules' tables — lobby rows, schedules, world rows, catalog rows — and are cited on those entries. Tunables live in [[server/spacetimedb/src/main/global.rs#DEFAULT_CHUNK_HEX_RADIUS|global.rs]], mirrored client-side by `World/ServerConstants.cs`.)

## Player

### File index

Generated entry points: [[server/spacetimedb/src/player/pointers.md|player pointers]].

### Tables

**PlayerInventorySlot** — [[server/spacetimedb/src/player/tables.rs#PlayerInventorySlot|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]] deals the 39 starter rows (indices 0 weapon, 1–4 hotbar, 5 accessory, 6 armor, 7–30 general backpack, 31 bag, 32–37 ability); `place_span` inserts follower cells for multi-slot ability items (`occupied_by = Some(head)`).
  - Update: `swap_slots` (region-based span moves; runtime state travels with the item), `use_item`, `activate_ability` / charge spend (`cooldown_until`/`charges`), `set_slot_toggle`, `apply_enchantment` / `remove_enchantment`, `transfer_item_to_inventory` (the single acquire path — `pickup_drop`, `take_from_chest`, `give_item` all route through it, then `recompute_stats`).
  - Delete: [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] wipes the profile's rows (death via `kill_player`, or `delete_profile`); `clear_span` removes follower cells; `drop_item` on a follower drops the whole span.
- **Client readers** — via the `local_player_inventory` view in the Game wave → `LocalPlayerInventoryComponent` (declared in [[client/Scenes/local_player.tscn|local_player.tscn]]) → the 39 inline `SlotComponent` nodes (hotbar/equipment/backpack), `ItemSidebar` hover composition, `CombatComponent`'s effective-weapon resolution, ability casts (`TryActivateAbility` → `ActivateAbility` reducer + optimistic bullet-control/spell visuals).
- **Server readers** — `slot_allowed` derives each slot's acceptance set from its `SlotRole`; `recompute_stats` folds only equipped heads (Weapon/Accessory/Armor at 1.0, Ability heads position-scaled, followers never); `equipped_head_slots` / `weapon_toggle_options` drive the weapon path.

^table-player-inventory-slot

**LoggedInPlayer** — [[server/spacetimedb/src/player/tables.rs#LoggedInPlayer|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/player/reducers.rs#join_world|join_world]] moves the lobby row in (deletes `LoggedOutPlayer`, inserts here with the chosen `profile_id`).
  - Update: [[server/spacetimedb/src/player/reducers.rs#set_username|set_username]] (either login row); `claim_admin` / `release_admin` flip `is_admin`.
  - Delete: [[server/spacetimedb/src/player/reducers.rs#leave_world|leave_world]], [[server/spacetimedb/src/main/lifecycle.rs#client_disconnected|client_disconnected]], and the already-in-world recovery arm of `client_connected` (purges statuses/zones, bounces the player back to the lobby — flagged only by a `log::error!`, a known gap, see doc 10) move the row back to `LoggedOutPlayer`; `teardown_profile` (death/disconnect path via `kill_player`) is the same move.
- **Client readers** — via the `local_player` view (own row only) in the Game wave → `EntitySpawnerComponent`'s replaying `LocalPlayer` binder → spawns the `LocalPlayer` entity; `LobbyComponent` waits on the `JoinWorld` callback before hiding the lobby.
- **Server readers** — the omnipresent login guard: `require_logged_in` / `require_in_world` resolve `profile_id` off this row for every world reducer; AOI views semijoin it to exclude logged-out ghosts; the enemy sim's chunk-gated set keys off logged-in players.

^table-logged-in-player

**LoggedOutPlayer** — [[server/spacetimedb/src/player/tables.rs#LoggedOutPlayer|definition]]

- **Changers**
  - Insert: `client_connected` (brand-new identity also gets a starter `PlayerProfile`), `client_disconnected`, `leave_world`, `kill_player` death cleanup, and the already-in-world recovery arm of `client_connected`.
  - Update: `set_username`, `claim_admin` / `release_admin`.
  - Delete: `join_world` moves the row to `LoggedInPlayer`.
- **Client readers** — via the `local_lobby_player` view in the Lobby wave → `DatabaseConnector`'s internal hook (username/admin display before joining). The table itself is deliberately not `public` (per-identity view only).
- **Server readers** — `require_in_lobby` gates profile management; `claim_admin` refuses when ANY login row already holds the admin slot.

^table-logged-out-player

**PlayerProfile** — [[server/spacetimedb/src/player/tables.rs#PlayerProfile|definition]]

- **Changers**
  - Insert: `client_connected` seeds the first ("Knight") profile; [[server/spacetimedb/src/player/reducers.rs#create_profile|create_profile]] adds more.
  - Update: profile settings writes (aim assist / lock-on toggles).
  - Delete: [[server/spacetimedb/src/player/reducers.rs#delete_profile|delete_profile]] (runs the same union cleanup list as death — allocation and rotation rows included).
- **Client readers** — via `local_player_profiles` (all own profiles, Lobby + Game waves) → `LobbyComponent`'s CharSlots panels (tracked by profile id; Join calls `join_world`); via `local_player_active_profile` (Game wave) → `LocalPlayerProfileComponent` → sprite frames in 2D / `SetModel3DTexture` catalog model in 3D plus aim-assist/lock-on cone for `CombatComponent`; via `nearby_remote_players_profiles` (AOI-filtered through the same chunk set as `nearby_remote_players`) → `RemotePlayer`'s one-shot texture lookup (and 3D model mirror).
- **Server readers** — `find_profile_by_name` resolves `(player_id, profile_name)` for `give_item`-style admin targeting.

^table-player-profile

**PlayerData** — [[server/spacetimedb/src/player/tables.rs#PlayerData|definition]]

- **Changers**
  - Insert: `try_scaffold_profile` (level 1, `BASE_MAX_HP`, `BASE_SPEED`).
  - Update: `recompute_stats` (resolves `max_hp` over `compute_base_max_hp`, `defense`, `base_speed` from gear/buff/enchantment folds; clamps `hp` down); `internal_gain_xp` (xp/level on enemy kill); the combat sinks (`deal_damage_to_player*` subtract, `heal_player` adds, death at zero delegates to `kill_player`).
  - Delete: `teardown_profile`.
- **Client readers** — via the `local_player_data` view in the Game wave → `LocalPlayerDataComponent` → `HealthComponent.SetFromServer(hp, maxHp)` (health bar, `HealthDidZero` death UI) plus `Level` / `Defense` / `BaseSpeed` mirrors (`StatsSidebar` DEF line, `PositionSyncComponent.CurrentSpeed`).
- **Server readers** — `report_movement` clamps speed against `base_speed`; mitigation reads `defense`; `xp_for_level` / `compute_level` gate level-ups (each grants `SKILL_POINTS_PER_LEVEL` unspent points).

^table-player-data

**PlayerStats** — [[server/spacetimedb/src/player/tables.rs#PlayerStats|definition]]

- **Changers**
  - Insert: `try_scaffold_profile` (flat `BASE_STAT` base — growth comes from allocation, not base).
  - Update: `recompute_stats` ONLY — folds allocated points + equipped-item `StatModifier`s (socketed and innate enchantments included) + active consumable buffs as `(base + flat) * (1 + mult)` with additive mults; never written directly.
  - Delete: `teardown_profile`.
- **Client readers** — via the `local_player_stats` view in the Game wave → `LocalPlayerDataComponent` → `StatsComponent.SetFromServer` per `StatKind` (the six allocatable stats feed `StatsSidebar`'s STR/WIS/DEX/DPS/SUP/ART lines and `+` buttons; `HealthComponent.Health` is shared-registered under `StatKind.Hp`).
- **Server readers** — `check_stat_requirements` gates equipping against these resolved values; spell scaling (`SpellParams.scaling`) and the strength damage term read them.

^table-player-stats

**PlayerStatAllocation** — [[server/spacetimedb/src/player/tables.rs#PlayerStatAllocation|definition]]

- **Changers**
  - Insert: `try_scaffold_profile` (zeroed + starting unspent).
  - Update: [[server/spacetimedb/src/player/reducers.rs#allocate_stat|allocate_stat]] moves unspent points into one of the six (rejects Hp/Defense/BaseSpeed); `internal_gain_xp` adds `SKILL_POINTS_PER_LEVEL` per level; consumed by `recompute_stats`.
  - Delete: `teardown_profile` (in the shared union cleanup list).
- **Client readers** — via the `local_player_stat_allocation` view in the Game wave → `LocalPlayerDataComponent.UnspentPoints` → `StatsSidebar` `+` buttons (visible only while points remain) call `AllocateStat`.
- **Server readers** — input to `recompute_stats` only.

^table-player-stat-allocation

**PlayerPosition** — [[server/spacetimedb/src/player/tables.rs#PlayerPosition|definition]]

- **Changers**
  - Insert: `try_scaffold_profile` / `join_world` spawn placement.
  - Update: [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] (10 Hz x/y + `movement_direction`/`movement_speed` angle+scalar: speed clamped to resolved `base_speed` × held-charge `SelfSlow`, displacement-clamped wrap-aware to `base_speed` × dt × `MOVE_SLACK`, one-shot `PlayerDisplacementAllowance` for grapples, elevation walk/fall rules off `BuildingTile.elevation`, unchanged positions skip the write); `teleport_player` (P4, exempt + restamps); `activate_ability` Teleport arm.
  - Delete: `teardown_profile` only on death/profile-delete — rows otherwise persist while logged out ("ghosts"; the sim guards via `logged_in_player` checks instead).
- **Client readers** — own row via `local_player_position` (Game wave, spawn/echo anchor); others via `nearby_remote_players` (own row excluded, logged-out ghosts semijoined out) → `RemotePlayer`'s binders → `InterpolationComponent.SetTarget` (velocity reconstructed from the angle+scalar pair) → moving puppets on screen. (Drops spawn from a different view — see ^table-loot-drop.) Enemy bullets collide against the puppet hurtboxes.
- **Server readers** — every range check in the module (`pickup_drop` / `take_from_chest` `INTERACT_RADIUS`, `report_enemy_hit` `HIT_VALIDATION_RADIUS`, zone membership, ally targeting) reads this row; AOI views deliberately do NOT key off it (they use ^table-player-chunk).

^table-player-position

**PlayerRotation** — [[server/spacetimedb/src/player/tables.rs#PlayerRotation|definition]]

- **Changers**
  - Insert: `try_scaffold_profile`.
  - Update: [[server/spacetimedb/src/player/reducers.rs#report_screen_rotation|report_screen_rotation]] (~30 Hz facing reports; mirrors `chunk_index` from the position row; sub-~0.5° changes skip the write).
  - Delete: `teardown_profile`.
- **Client readers** — remote rows via `nearby_remote_player_rotations` (same AOI filter as `nearby_remote_players`) → `RemotePlayer`'s rotation binder → `SetScreenRotationTarget` (puppet facing). The local client never subscribes its own row (it owns the camera).
- **Server readers** — none (facing is display + client aim state).

^table-player-rotation

**PlayerChunk** — [[server/spacetimedb/src/player/tables.rs#PlayerChunk|definition]]

- **Changers**
  - Insert: `try_scaffold_profile`.
  - Update: `report_movement` / `teleport_player` — written ONLY when the chunk actually changes (unlike `PlayerPosition` at 10 Hz).
  - Delete: `teardown_profile`.
- **Client readers** — none. `public` but unsubscribed by design: this table exists so views don't key off `PlayerPosition`.
- **Server readers** — `nearby_indices` / `nearby_indices_from_chunk` resolve the caller's chunk set for every AOI view in the module (and chest/enemy/world AOI views reuse them).

^table-player-chunk

**ActiveConsumableEffect** — [[server/spacetimedb/src/player/tables.rs#ActiveConsumableEffect|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/player/methods.rs#apply_consumable_effect|apply_consumable_effect]] (consumable use + Heal/Buff ability arms; heals delegate to `combat::heal_player`).
  - Update: `tick_status_effects` decrements `remaining` each second.
  - Delete: `tick_status_effects` expiry sweep (then `recompute_stats`); `apply_consumable_effect` replaces same-kind rows.
- **Client readers** — none. `public` but unsubscribed (not in any wave — no binder, no bindings consumer outside generated code): plain pre-baked stat buffs have no UI mirror; anything with runtime/tick visibility lives in ^table-active-status-effect instead.
- **Server readers** — `recompute_stats` folds live buffs into the stat resolution.

^table-active-consumable-effect

**ConsumableEffectSchedule** — [[server/spacetimedb/src/player/tables.rs#ConsumableEffectSchedule|definition]]

- **Changers** — the only changer is whoever arms the job row: `init` inserts the single 1 Hz row driving `tick_status_effects`. There is exactly one 1 Hz schedule in the module — new per-second work hangs off `status::methods::tick`, never a second schedule.
- **Client readers** — none (scheduled table, not `public`).

^table-consumable-effect-schedule

**LootDrop** — [[server/spacetimedb/src/player/tables.rs#LootDrop|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/player/reducers.rs#drop_item|drop_item]] (stamps the slot's `cooldown_until`/`charges` so ability runtime state survives drop → pickup; dropping a follower drops the whole span).
  - Update: never updated.
  - Delete: [[server/spacetimedb/src/player/reducers.rs#pickup_drop|pickup_drop]] on take (wrap-aware `INTERACT_RADIUS` + `expires_at` rejection up front; restores carried cooldown/charges; ability items auto-equip into the first free span if stat requirements pass, else loose backpack storage); `tick_status_effects` sweeps rows past `expires_at` (`LOOT_DROP_EXPIRY` = 10 s).
- **Client readers** — via the `nearby_loot_drops` AOI view in the Game wave → `EntitySpawnerComponent`'s replaying `NearbyLootDrops` binder → spawns the [[client/Scenes/drop.tscn|drop.tscn]] puppet (`Items/Drop.cs`, catalog-textured sprite) with its `PickupComponent` Area2D → body-entered calls `PickupDrop`. Visible end: loot on the ground that vanishes into the backpack on touch.
- **Server readers** — `pickup_drop`'s range/expiry guards.

^table-loot-drop

**PlayerMovementState** — [[server/spacetimedb/src/player/tables.rs#PlayerMovementState|definition]]

- **Changers**
  - Insert: first `report_movement` / `teleport_player` contact.
  - Update: every movement report restamps `last_report`; downhill falls set `falling_until` lockout (movement locked while the client tweens the fall).
  - Delete: `teardown_profile`.
- **Client readers** — none. Server-only table (not `public`).
- **Server readers** — `report_movement`'s displacement budget (`base_speed` × dt × `MOVE_SLACK`, floor `MIN_REPORT_DISPLACEMENT`) and the fall-damage path (`FALL_DAMAGE_PER_LEVEL` past `FALL_SAFE_DROP`).

^table-player-movement-state

**PlayerDisplacementAllowance** — [[server/spacetimedb/src/player/tables.rs#PlayerDisplacementAllowance|definition]]

- **Changers**
  - Insert: the Grapple arm of `apply_ability_effect` stamps the one-shot window (`max_distance` = scaled grapple range, short expiry).
  - Update: never updated — consumed, not edited.
  - Delete: consumed by the next oversized `report_movement` (which also skips elevation rules on that report), on expiry contact, and by `teardown_profile`.
- **Client readers** — none. Server-only table (not `public`).
- **Server readers** — `report_movement`'s clamp accepts ONE oversized report up to `max_distance` while the row lives, so legit client-tweened grapples don't rubber-band.

^table-player-displacement-allowance

**PlayerHitRate** — [[server/spacetimedb/src/player/tables.rs#PlayerHitRate|definition]]

- **Changers**
  - Insert / Update: `report_enemy_hit` and `report_hit` stamp the 1-second fixed-window `outgoing` / `incoming` counters (`hit_rate_allow` in [[server/spacetimedb/src/player/methods.rs#hit_rate_allow|player/methods.rs]]).
  - Delete: `teardown_profile`.
- **Client readers** — none. Server-only table (not `public`).
- **Server readers** — `outgoing` caps `report_enemy_hit` at weapon fire_rate × effective shot_count × `HIT_RATE_SLACK` (bounds hit-spam and StoreIncoming farming); `incoming` caps `report_hit` at `INCOMING_HIT_RATE_CAP` (~30/s, bullet-hell generous).

^table-player-hit-rate

## Status

### File index

Generated entry points: [[server/spacetimedb/src/status/pointers.md|status pointers]].

### Tables

**GroundZone** — [[server/spacetimedb/src/status/tables.rs#GroundZone|definition]]

- **Changers**
  - Insert: the P3 `CastZone` arm of `apply_ability_effect` places the zone at the range-clamped cursor point (radius/magnitude position-scaled).
  - Update: the 1 Hz `tick` decrements `remaining` (ally zones re-apply statuses with `ZONE_STATUS_REFRESH_SECONDS` on live membership; bursts fire once at expiry).
  - Delete: expiry sweep in `tick`; [[server/spacetimedb/src/status/methods.rs#purge_profile_zones|purge_profile_zones]] on death / `leave_world` / disconnect (a caster's zones die with them).
- **Client readers** — raw `public` table in the Game wave (co-op-scale raw subscription, no view), replay ON → `ZoneRendererComponent`'s `GroundZoneBinder` (declared inline in [[client/Scenes/game.tscn|game.tscn]]) → one code-created `Polygon2D` circle per row, colored per `ZoneKind`, deleted on row delete. Visible end: witch-doctor circles / wizard spire rings on the ground.
- **Server readers** — `tick` recomputes membership live each pass (`players_near_point` / enemy scans on the zone center — never carried); `intercept_incoming` consults `DamageShare` zones live per hit.

^table-ground-zone

**ActiveStatusEffect** — [[server/spacetimedb/src/status/tables.rs#ActiveStatusEffect|definition]]

- **Changers**
  - Insert / Update: [[server/spacetimedb/src/status/methods.rs#apply_status_effect|apply_status_effect]] (one row per (profile, kind); the two mark kinds share ONE slot — recasting any mark replaces it: bleed pools dump instantly, store pools forfeit). Every hit — even fully absorbed — stamps `counter` / `last_event_at`.
  - Delete: expiry sweep and purge evaluation in the 1 Hz `tick` (bleed drains via the flagged damage path, never direct HP writes; Regen ticks; StoreDamage settles through `settle_store_mark` with ^table-player-hit-flag corroboration); `purge_profile_statuses` on death / `leave_world` / disconnect (no status may kill or tick a logged-out player).
- **Client readers** — raw `public` table in the Game wave (co-op-scale raw subscription, no view), replay ON → `StatusBarComponent`'s `ActiveStatusEffectBinder` (declared under `InventoryCanvas/InventoryPanel` in [[client/Scenes/local_player.tscn|local_player.tscn]], filtered to the local profile) → "Kind N.Ns" labels counting down locally between server updates. Visible end: the status strip above the hotbar.
- **Server readers** — `intercept_incoming` runs the fixed order (Invulnerable → DamageShield → RedirectToCaster loop-guarded → DamageShare → DamageReductionPct → DamageToBleed) before mitigation; outgoing consults (`outgoing_damage_mult`, `withhold_outgoing`, `convert_outgoing_to_bleed`, `note_damage_dealt`) run per hit; `HpFloor` clamps after mitigation.

^table-active-status-effect

**AbilityCharge** — [[server/spacetimedb/src/status/tables.rs#AbilityCharge|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]] (refuses while a row exists — one held charge per player; applies the `SelfSlow` status the server's speed clamp enforces).
  - Update: never updated.
  - Delete: [[server/spacetimedb/src/status/reducers.rs#release_ability_charge|release_ability_charge]] (shared `internal_release_charge` path; removes the `SelfSlow`), the 1 Hz tick's timeout auto-release at `MAX_CHARGE_SECONDS` (target = the player's own position), and the lifecycle purge (a vanished charge spends NO cooldown).
- **Client readers** — raw `public` table in the Game wave (no view), replay ON → `AbilityChargeComponent`'s `AbilityChargeBinder` (declared in [[client/Scenes/local_player.tscn|local_player.tscn]], echo-only, filtered to the local profile) → cosmetic charge `SelfSlow` on `PositionSyncComponent` plus `Charging`/`ChargedSlot` for `CombatComponent`'s fire suppression. Visible end: the held-charge slow + suppressed fire + (on release) the scaled cast.
- **Server readers** — `report_movement` multiplies the speed clamp by the minimum held-charge `SelfSlow` factor; `activate_ability` rejects charge-capable items (they must route through begin/release).

^table-ability-charge

**ActiveEnemyStatusEffect** — [[server/spacetimedb/src/status/tables.rs#ActiveEnemyStatusEffect|definition]]

- **Changers**
  - Insert / Update: `apply_enemy_status` (one row per (enemy_id, kind) — re-applying refreshes magnitude/remaining/source; P2 curses, P3 enemy zones, stagger-stun application).
  - Delete: the 1 Hz `tick` (expiry + orphan sweep + DoT ticks through `DOT_TICK_OPTIONS`); [[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]] (rows never outlive their enemy; wager pools forfeit).
- **Client readers** — raw `public` table in the Game wave (subscribed at P6, co-op-scale raw subscription, no view), replay ON → `EnemyDebuffIndicatorComponent`'s `ActiveEnemyStatusEffectBinder` (at the `DebuffAttach` point in [[client/Scenes/default_enemy.tscn|default_enemy.tscn]], filtered per enemy instance) → colored chips per `EnemyStatusKind` (DoT green, Slow blue, Vulnerability purple, ArmorBreak orange, Stunned yellow) beside the 0–100 stagger bar. Fine at co-op scale.
- **Server readers** — `enemy_defense` (ArmorBreak zeroes flat defence) / `enemy_vulnerability_mult` / `enemy_movement_mult` / `stunned_magnitude` consults in the combat sink and behavior tick (`Pause` freezes `tick_enemy_behavior`; `Slowed`/`Enraged` scale movement and fire pacing via `effective_interval`).

^table-active-enemy-status-effect

## World

### File index

Generated entry points: [[server/spacetimedb/src/world/pointers.md|world pointers]].

### Tables — config (`tables.rs`)

**MapConfig** — [[server/spacetimedb/src/world/tables.rs#MapConfig|definition]]

- **Changers**
  - Insert: `Seed` impl in [[server/spacetimedb/src/main/seeds.rs#Seed|main/seeds.rs]] plus admin [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] / `add_chunks` (singleton id 0; lap vectors derived from the chunk grid).
  - Update: `add_chunks` rewrites the singleton when the grid changes.
  - Delete: never deleted (fallback: `MapConfig::load` with caller-chosen degenerate cols/rows).
- **Client readers** — raw `public` table in the Game wave → `TableSubscriber`'s internal `MapConfig` hooks mirror `LapQ`/`LapR` statics (read per entity per frame by `InterpolationComponent`; reset in `_ExitTree`) → `TerrainComponent` grid geometry, `HexGridOverlayComponent` debug grid, `ElevationMapComponent.GetElevationAt` radius. Visible end: every correctly wrapped tile/entity near a torus seam.
- **Server readers** — read through `MapConfig::load(ctx, …)` (simulation paths pass `0, 0`, admin paths `1, 1`); the one exception is `player/views.rs`, which reads the row directly (a `ViewContext` can't call the helper) defaulting to `1, 1`.

^table-map-config

**BuildingTile** — [[server/spacetimedb/src/world/tables.rs#BuildingTile|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] (one row per hex — empty `building_type`, no owner, `elevation` 0; called by `init` and by admin `add_chunks`).
  - Update: the `run_generation` elevation pass in [[server/spacetimedb/src/world/terrain/mod.rs#run_generation|world/terrain/mod.rs]] stamps the quantized noise `elevation` onto existing rows (`clear_world_geometry` never touches this table); admin [[server/spacetimedb/src/world/reducers.rs#place_building|place_building]] stamps `building_type` + `owner_id` (errors when the hex has no row yet — "has add_chunks been called?"; no client UI calls it — world authoring like `add_chest`); [[server/spacetimedb/src/world/reducers.rs#remove_building|remove_building]] clears them back (any logged-in owner, or admin).
  - Delete: admin [[server/spacetimedb/src/main/admin.rs#clear_chunks|clear_chunks]] wipes all tiles (regeneration rewrites geometry, not this table).
- **Client readers** — via the `nearby_building_tiles` AOI view in the Game wave, replay ON → `ElevationMapComponent`'s `MapConfigBinder` + `NearbyBuildingTilesBinder` (declared inline in [[client/Scenes/game.tscn|game.tscn]]) → the `(q, r) → level` dictionary behind `GetElevation` / `GetElevationAt` / `GetWorldHeightAt` (`ElevationChanged` event) → `LocalPlayer` movement prediction + fall tween, bullet band rule, terrain tint, 3D mesh heights. Visible end: cliffs that block, falls that tween, bullets that fly over low ground.
- **Server readers** — `report_movement`'s elevation rules (walkable ±`MAX_WALKABLE_*_DELTA`, steeper-up rejects, steeper-down falls with `FALL_DAMAGE_PER_LEVEL` past `FALL_SAFE_DROP`); enemy steering cliff probing (no enemy fall mechanic).

^table-building-tile

**TextureEntry** — [[server/spacetimedb/src/world/tables.rs#TextureEntry|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/main/seeds.rs#seed_default_textures|seed_default_textures]] (every `texture_id` an item/enemy/profile references needs a row — missing entry = blank icon, no error).
  - Update: admin [[server/spacetimedb/src/main/admin.rs#upsert_texture_entry|upsert_texture_entry]].
  - Delete: no delete path.
- **Client readers** — via the `all_textures` view in the Base wave → `CatalogComponent` (`GetResPath2D` / `GetResPath3D`, empty 3D path normalized to null = no 3D representation) → every sprite/icon/model load in the game (entities, drops, tiles, UI icons, 3D model mirrors).
- **Server readers** — none (rendering-only catalog).

^table-texture-entry

**AnimationEntry** — [[server/spacetimedb/src/world/tables.rs#AnimationEntry|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/main/seeds.rs#seed_default_animations|seed_default_animations]].
  - Update / Delete: no paths (static seed content).
- **Client readers** — via the `all_animations` view in the Base wave → `CatalogComponent`'s `AllAnimationsBinder` → `LocalPlayer` merges every clip into the 3D character model's `AnimationPlayer` under its `animation_id` (e.g. `ual1/Walk`); `DebugOverlay` lists the rows. Visible end: animated 3D character models.
- **Server readers** — none (rendering-only catalog).

^table-animation-entry

**LayeringRule** — [[server/spacetimedb/src/world/tables.rs#LayeringRule|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/main/seeds.rs#seed_layering_rules|seed_layering_rules]].
  - Update / Delete: no paths (static generation input; default is deny — compatibility is opt-in per overlay/base tag pair).
- **Client readers** — none. `public` but unsubscribed: generation input, not rendered content.
- **Server readers** — `choose_overlay` consults the rules per wedge at generation time.

^table-layering-rule

**BaseAdjacencyRule** — [[server/spacetimedb/src/world/tables.rs#BaseAdjacencyRule|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/main/seeds.rs#seed_adjacency_rules|seed_adjacency_rules]].
  - Update / Delete: no paths (deny-list: symmetric tag_a/tag_b clashing pairs like swamp-next-to-sand; default is allow).
- **Client readers** — none (`public` but unsubscribed — generation input).
- **Server readers** — ground pass adjacency checks in `run_generation`.

^table-base-adjacency-rule

**OverlayAdjacencyRule** — [[server/spacetimedb/src/world/tables.rs#OverlayAdjacencyRule|definition]]

- **Changers**
  - Insert: `seed_adjacency_rules` (same seeder as ^table-base-adjacency-rule; kept as a separate table so a shared tag string can never cross-apply between a base rule and an overlay rule).
  - Update / Delete: no paths.
- **Client readers** — none (`public` but unsubscribed — generation input).
- **Server readers** — overlay pass adjacency checks in `run_generation`.

^table-overlay-adjacency-rule

**DecorGroundRule** — [[server/spacetimedb/src/world/tables.rs#DecorGroundRule|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/main/seeds.rs#seed_decor_ground_rules|seed_decor_ground_rules]].
  - Update / Delete: no paths (deny-list decor_tag × denied_base_tag — not symmetric, since it relates two tag kinds).
- **Client readers** — none (`public` but unsubscribed — generation input).
- **Server readers** — `place_decor` conflict checks.

^table-decor-ground-rule

### Tables — generation inputs (`def_tables.rs`)

**WorldDef** — [[server/spacetimedb/src/world/def_tables.rs#WorldDef|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/main/seeds.rs#seed_world_defs|seed_world_defs]].
  - Update: admin [[server/spacetimedb/src/main/admin.rs#upsert_world_def|upsert_world_def]].
  - Delete: no delete path.
- **Client readers** — none (`public` but unsubscribed — generation input: biome-weight front-end over `run_generation`).
- **Server readers** — `internal_generate_world_proc` reads the def (biome Voronoi distribution over `BIOME_VORONOI_SEED_BUDGET` seeds).

^table-world-def

**BiomeDef** — [[server/spacetimedb/src/world/def_tables.rs#BiomeDef|definition]]

- **Changers**
  - Insert: `seed_world_defs` (ground/overlay/decor texture configs with per-biome tags/chances/rotations + noise scales + separation).
  - Update: admin [[server/spacetimedb/src/main/admin.rs#upsert_biome_def|upsert_biome_def]].
  - Delete: no delete path.
- **Client readers** — none (`public` but unsubscribed — generation input).
- **Server readers** — `choose_ground` / `choose_overlay` / `place_decor` per-hex/wedge passes (noise-clustered via `value_noise2d`, weighted picks via `prng`).

^table-biome-def

**BiomeRegionDef** — [[server/spacetimedb/src/world/def_tables.rs#BiomeRegionDef|definition]]

- **Changers**
  - Insert: [[server/spacetimedb/src/main/seeds.rs#seed_region_def|seed_region_def]].
  - Update: admin [[server/spacetimedb/src/main/admin.rs#upsert_biome_region_def|upsert_biome_region_def]].
  - Delete: no delete path.
- **Client readers** — none (`public` but unsubscribed — generation input: enemy roster + `max_enemies` + `spawn_radius` per region def).
- **Server readers** — region seeding (`seed_region_def` + manual `ManualRegionInput` front-end).

^table-biome-region-def

### Tables — generated world (`instance_tables.rs`)

**BiomeRegion** — [[server/spacetimedb/src/world/instance_tables.rs#BiomeRegion|definition]]

- **Changers**
  - Insert: `run_generation` (both proc and manual front-ends).
  - Update: never updated.
  - Delete: `clear_world_geometry` wipes all regions on every regeneration (unlike ^table-building-tile, which regeneration only re-stamps).
- **Client readers** — none. `public` but unsubscribed: spawn regions are server-side simulation inputs (there is deliberately no `EnemyZone` table — this is it).
- **Server readers** — `tick_enemy_spawn` picks spawn points inside regions (`center_hex_q/r`, `spawn_radius`, `max_enemies`, `enemy_template_ids`).

^table-biome-region

**TriangleTile** — [[server/spacetimedb/src/world/instance_tables.rs#TriangleTile|definition]]

- **Changers**
  - Insert: `write_triangle_tiles` in [[server/spacetimedb/src/world/terrain/mod.rs#write_triangle_tiles|world/terrain/mod.rs]] (one row per triangle wedge: ground texture + optional overlay + rotation/scale + biome/region ids, deterministic draw order).
  - Update / Delete: regeneration rewrites the set (no per-row updates).
- **Client readers** — via the `nearby_terrain_tiles` AOI view (`DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`) in the Game wave → `TerrainComponent`'s binder → pooled `TileComponent` chunk pool (`1+3R(R+1)` pre-warmed) with per-texture MultiMesh batch leaves; `WaterComponent3D` renders ocean wherever rows are ABSENT. Visible end: the ground itself (plus elevation tint from ^table-building-tile).
- **Server readers** — none post-generation (rendering payload).

^table-triangle-tile

**HexDecor** — [[server/spacetimedb/src/world/instance_tables.rs#HexDecor|definition]]

- **Changers**
  - Insert: `place_decor` (at most one prop per hex; hexes with no decor simply have no row).
  - Update / Delete: regeneration rewrites the set.
- **Client readers** — via the `nearby_hex_decor` AOI view in the Game wave → `TerrainComponent`'s decor layers (per-texture batches + paired shadow sprites rotating as one unit). Visible end: trees/rocks/props on the map.
- **Server readers** — none post-generation (rendering payload).

^table-hex-decor
