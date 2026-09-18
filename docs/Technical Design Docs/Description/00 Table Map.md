# 00 Table Map

The per-table spine for the Description docs. One section per folder in `server/spacetimedb/src/`, in phase-0 listing order; one entry per table in definition order. Every entry tells the same two stories: **how the table is changed** (which reducer, tick, hook, or seed writes it) and **how it is viewed** (which view or raw subscription carries it, which binder consumes it, what on screen it drives).

Code is the only source of truth — every claim names its file/function, and the maintainer refs are the jump-off points, not the authority: [[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]], [[server/spacetimedb/src/enemy/AGENTS.md|enemy AGENTS.md]], [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]].

Reading an entry: **Changers** groups writes by operation (an operation with no path says so explicitly — "never updated" is information). **Client readers** traces the full path to its visible end — view accessor or raw public table → subscription wave (`BaseTables`/`LobbyTables`/`GameTables` in [[client/sstdbsdk/TableSubscriber.cs#TableSubscriber|TableSubscriber]]) → binder component → concrete UI element or scene node — or says outright when the table is server-only or `public` but unsubscribed. **Server readers** appears only where the read pattern matters (guards, AOI semijoins, scans). Each entry ends with its `^table-*` anchor; entries cross-refer by anchor name.

Two mechanics every entry assumes: reducers are transactional and return no data, so clients only ever learn through tables/subscriptions; and one [[client/sstdbsdk/TableBinderComponent.cs#TableBinderComponent|TableBinderComponent]] child per consumed table re-exposes row events as editor-wireable signals, with `ReplayExistingRows` on for persistent tables and **off** for the append-only event tables (`BulletPatternEvent`, `BulletControlEvent`, `AbilityVisualEvent`), where replay would re-fire history.

# Anticheat

Pointer index: [[server/spacetimedb/src/anticheat/pointers.md|pointers]].

**PlayerHitFlag** — [[server/spacetimedb/src/anticheat/tables.rs#PlayerHitFlag|definition]] — one row per witness report that some client saw `hit_player` get hit (no bullet identity, just who/when).
- **Changers** — Insert in [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]] (requires in-world; self-reports silently ignored); Delete of rows older than `HIT_FLAG_MAX_AGE_SECONDS` in the 1 Hz [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] sweep; never updated.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — The `NoHits` wager corroborates against these flags in the `NoHits` arm of [[server/spacetimedb/src/status/methods.rs#settle_store_mark|settle_store_mark]] (witness-count block).
^table-player-hit-flag

**MoveValidationStrikes** — [[server/spacetimedb/src/anticheat/tables.rs#MoveValidationStrikes|definition]] — one row per profile holding the timestamps of recent move-validation failures inside the strike window.
- **Changers** — Insert/Update in [[server/spacetimedb/src/anticheat/reducers.rs#record_move_strike|record_move_strike]] (called from [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]], prunes outside-window stamps before appending); Delete in [[server/spacetimedb/src/anticheat/reducers.rs#sweep_strikes|sweep_strikes]] when the live count hits zero and in [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]].
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] consumes [[server/spacetimedb/src/anticheat/reducers.rs#record_move_strike|record_move_strike]]'s returned count against `STRIKES_BEFORE_REJECT` for the clamp-then-reject escalation; the read-only [[server/spacetimedb/src/anticheat/reducers.rs#live_strike_count|live_strike_count]] serves [[server/spacetimedb/src/anticheat/reducers.rs#sweep_strikes|sweep_strikes]].
^table-move-validation-strikes

# Chat

Pointer index: [[server/spacetimedb/src/chat/pointers.md|pointers]].

**ChatChannel** — [[server/spacetimedb/src/chat/mod.rs#ChatChannel|definition]] — one row per channel (name, owner profile, `members`/`banned` id lists).
- **Changers** — Insert in [[server/spacetimedb/src/chat/mod.rs#chat_create|chat_create]]; Update (members/banned) in [[server/spacetimedb/src/chat/mod.rs#chat_join|chat_join]], [[server/spacetimedb/src/chat/mod.rs#chat_leave|chat_leave]], [[server/spacetimedb/src/chat/mod.rs#chat_invite|chat_invite]], [[server/spacetimedb/src/chat/mod.rs#chat_ban|chat_ban]], [[server/spacetimedb/src/chat/mod.rs#chat_unban|chat_unban]]; never deleted.
- **Client readers** — Via the [[server/spacetimedb/src/chat/mod.rs#all_chat_channels|all_chat_channels]] global view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → subscribed but no binder consumes it yet, so no visible end.
- **Server readers** — Membership/ban gates in [[server/spacetimedb/src/chat/mod.rs#chat_post|chat_post]].
^table-chat-channel

**ChatChannelMember** — [[server/spacetimedb/src/chat/mod.rs#ChatChannelMember|definition]] — membership mirror, one row per channel+profile, because the view builder has no `IN` over the `members` vec.
- **Changers** — Insert alongside every channel-membership grant in `chat_create`/`chat_join`/`chat_invite`; Delete on every removal in `chat_leave`/`chat_ban`; never updated.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — The [[server/spacetimedb/src/chat/mod.rs#local_chat_messages|local_chat_messages]] view right-semijoins messages through it.
^table-chat-channel-member

**ChatMessage** — [[server/spacetimedb/src/chat/mod.rs#ChatMessage|definition]] — one row per posted message (channel, sender profile, text, timestamp).
- **Changers** — Insert in [[server/spacetimedb/src/chat/mod.rs#chat_post|chat_post]] (membership + length + rate gates); Delete by admin [[server/spacetimedb/src/chat/mod.rs#moderate_delete_message|moderate_delete_message]] and by the retention sweep in [[server/spacetimedb/src/chat/mod.rs#tick_chat_cleanup|tick_chat_cleanup]]; never updated.
- **Client readers** — Via the [[server/spacetimedb/src/chat/mod.rs#local_chat_messages|local_chat_messages]] per-caller view (member channels only) → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → subscribed but no binder consumes it yet, so no visible end.
- **Server readers** — [[server/spacetimedb/src/chat/mod.rs#report_chat_message|report_chat_message]] reads the row being reported.
^table-chat-message

**ChatReport** — [[server/spacetimedb/src/chat/mod.rs#ChatReport|definition]] — one row per abuse report against a message.
- **Changers** — Insert in [[server/spacetimedb/src/chat/mod.rs#report_chat_message|report_chat_message]]; never updated or deleted (admins read via logs/SQL).
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
^table-chat-report

**ChatRate** — [[server/spacetimedb/src/chat/mod.rs#ChatRate|definition]] — one row per profile holding the 5-second chat rate window.
- **Changers** — Insert/Update in [[server/spacetimedb/src/chat/mod.rs#chat_rate_allow|chat_rate_allow]] on every post; never deleted.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — Gates [[server/spacetimedb/src/chat/mod.rs#chat_post|chat_post]] ("chatting too fast").
^table-chat-rate

# Chest

Pointer index: [[server/spacetimedb/src/chest/pointers.md|pointers]].

**Chest** — [[server/spacetimedb/src/chest/mod.rs#Chest|definition]] — one loot container at a fixed world position, with `chunk_index` stamped from the wrapped coordinate.
- **Changers** — Insert in [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]] (admin-only); never updated or deleted.
- **Client readers** — Via the [[server/spacetimedb/src/chest/mod.rs#nearby_chests|nearby_chests]] AOI view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#EntitySpawnerComponent|EntitySpawnerComponent]] `NearbyChestsBinder` (replay on, wired in `game.tscn`) → spawns one `Chest` scene node (`chest.tscn`) per row at its x/y.
- **Server readers** — [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] reads the row for the range check and the item lookup.
^table-chest

**ChestItem** — [[server/spacetimedb/src/chest/mod.rs#ChestItem|definition]] — one row per item in a chest, mirroring the parent's `chunk_index` so the view filters directly; `regenerating` rows are never consumed (the surviving row *is* the next copy).
- **Changers** — Insert in [[server/spacetimedb/src/chest/mod.rs#add_item_to_chest|add_item_to_chest]] and [[server/spacetimedb/src/chest/mod.rs#add_all_items_to_chest|add_all_items_to_chest]] (both admin-only); Delete in [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] for non-regenerating rows only; never updated.
- **Client readers** — Via the [[server/spacetimedb/src/chest/mod.rs#nearby_chest_items|nearby_chest_items]] AOI view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → [[client/Scripts/Items/Chest.cs#RefreshItems|Chest]] `NearbyChestItemsBinder` (replay on, wired in `chest.tscn`) → rebuilds the open chest panel's draggable `ChestSlotComponent` entries.
^table-chest-item

**ChestTakeCooldown** — [[server/spacetimedb/src/chest/mod.rs#ChestTakeCooldown|definition]] — one row per (profile, chest) restamped on every regenerating-chest take.
- **Changers** — Insert/Update in [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]]; never deleted.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — Gates regenerating takes to one per `CHEST_TAKE_COOLDOWN_SECONDS`.
^table-chest-take-cooldown

# Claims

Pointer index: [[server/spacetimedb/src/claims/pointers.md|pointers]].

**Territory** — [[server/spacetimedb/src/claims/mod.rs#Territory|definition]] — one claimed territory: owner, hex-tile footprint as parallel `tiles_q`/`tiles_r` vecs, members plus per-member permission bits, upkeep treasury.
- **Changers** — Insert in [[server/spacetimedb/src/claims/mod.rs#claim_territory|claim_territory]] (rejects overlapping claims); Update in [[server/spacetimedb/src/claims/mod.rs#territory_add_member|territory_add_member]], [[server/spacetimedb/src/claims/mod.rs#territory_remove_member|territory_remove_member]], [[server/spacetimedb/src/claims/mod.rs#territory_deposit|territory_deposit]], and the upkeep charge in [[server/spacetimedb/src/claims/mod.rs#tick_upkeep|tick_upkeep]]; Delete in [[server/spacetimedb/src/claims/mod.rs#disband_territory|disband_territory]] and on unpaid upkeep in `tick_upkeep` (both release buildings/sites to wilderness first).
- **Client readers** — Via the [[server/spacetimedb/src/claims/mod.rs#all_territories|all_territories]] global view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → subscribed but no binder consumes it yet, so no visible end (hex tint overlays planned).
- **Server readers** — [[server/spacetimedb/src/claims/mod.rs#can_interact_pos|can_interact_pos]] and its `check_interact`/`check_build` wrappers gate [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] and [[server/spacetimedb/src/player/reducers.rs#pickup_drop|pickup_drop]] (both via the shared `validate_interact`), `place_building`, and footprint checks; [[server/spacetimedb/src/claims/mod.rs#territory_id_at_hex|territory_id_at_hex]] resolves the claim under a building footprint.
^table-territory

# Combat

Pointer index: [[server/spacetimedb/src/combat/pointers.md|pointers]].

The combat module defines **no tables** — it is the shared damage pipeline (`mod.rs` policy types plus re-exports, `calc.rs` pure math, `apply.rs` the sinks that mitigate, subtract, and handle kills). Its state lives in other modules' tables: hp on ^table-player-data and ^table-enemy, defences on the template/row, statuses on ^table-active-status-effect and ^table-active-enemy-status-effect, attestations on ^table-bullet-fire-attestation. Damage enters only through its sinks ([[server/spacetimedb/src/combat/mod.rs#DamageRequest|DamageRequest]] policies for enemies, `IncomingDamageOptions` for players) and reaches clients through those tables' own views.

# Enemy

Pointer index: [[server/spacetimedb/src/enemy/pointers.md|pointers]].

**MovementDef** — [[server/spacetimedb/src/enemy/def_tables.rs#MovementDef|definition]] — one named context-steering movement (Goal × Pattern × Avoidance plus move/pause cycle) per phase.
- **Changers** — Insert by the content helpers in [[server/spacetimedb/src/enemy/defs/mod.rs#MovementDef|defs]] at seed time and by admin [[server/spacetimedb/src/main/admin.rs#upsert_movement_def|upsert_movement_def]] (def_id 0 inserts, existing id updates); never deleted.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — The movement tick resolves each phase's id in [[server/spacetimedb/src/enemy/methods.rs#apply_movement|apply_movement]].
^table-movement-def
**SingleStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#SingleStepDef|definition]] — one single-shot bullet pattern (pattern params, target, offsets, texture, lifetime, damage, curve/accel, next-step delay).
- **Changers** — Insert by the content helpers in [[server/spacetimedb/src/enemy/defs/mod.rs#SingleStepDef|defs]] at seed time and by admin [[server/spacetimedb/src/main/admin.rs#upsert_single_step_def|upsert_single_step_def]]; never deleted.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — The sequence tick resolves the def per shot in [[server/spacetimedb/src/enemy/methods.rs#tick_sequence|tick_sequence]], including the stamped per-bullet damage.
^table-single-step-def

**RepeatStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#RepeatStepDef|definition]] — one repeating emitter (single-shot fields plus interval, repeat target with 0 = forever, and per-shot angle step for spinning arms).
- **Changers** — Insert by the content helpers in [[server/spacetimedb/src/enemy/defs/mod.rs#RepeatStepDef|defs]] at seed time and by admin [[server/spacetimedb/src/main/admin.rs#upsert_repeat_step_def|upsert_repeat_step_def]]; never deleted.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — Copied per enemy into ^table-repeat-step-instance at spawn; read every behavior tick in `tick_sequence`.
^table-repeat-step-def

**MultiStepDef** — [[server/spacetimedb/src/enemy/def_tables.rs#MultiStepDef|definition]] — one multi-shot volley (a `shots` vec fired together plus next-step delay).
- **Changers** — Insert by the content helpers in [[server/spacetimedb/src/enemy/defs/mod.rs#MultiStepDef|defs]] at seed time and by admin [[server/spacetimedb/src/main/admin.rs#upsert_multi_step_def|upsert_multi_step_def]]; never deleted.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — Read per visit in `tick_sequence` (capped at `MAX_MULTI_SHOTS`).
^table-multi-step-def

**EnemyTemplate** — [[server/spacetimedb/src/enemy/def_tables.rs#EnemyTemplate|definition]] — one enemy kind: texture, hp, flat per-bullet defence, sim factors, aggro lock, stun behavior, and the `PhaseDef` vec (difficulty is compile-time — one row per tier like `Foo_Easy`).
- **Changers** — Upsert by [[server/spacetimedb/src/main/seeds.rs#EnemyTemplate|EnemyTemplate::seed]] at publish and by admin [[server/spacetimedb/src/main/admin.rs#upsert_enemy_template|upsert_enemy_template]]; never deleted.
- **Client readers** — Via the [[server/spacetimedb/src/enemy/views.rs#enemy_templates|enemy_templates]] global view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → no binder, but [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowInserted|Enemy.OnEnemyRowInserted]] reads `EnemyTemplates.Iter()` directly per spawned puppet for max_hp, the 2D sprite frames, and the 3D model — visible end is each `Enemy` puppet's health bar, sprite, and model.
- **Server readers** — Spawn paths resolve the template by id; the combat sink reads `defense`; the behavior tick reads sim factors and `stun_behavior`.
^table-enemy-template

**RepeatStepInstance** — [[server/spacetimedb/src/enemy/instance_tables.rs#RepeatStepInstance|definition]] — per-enemy copy of a `RepeatStepDef` so `repeat_count` advances independently per enemy.
- **Changers** — Insert in [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior|build_enemy_behavior]] per repeat step; Update (`repeat_count`, timer re-arm) in `tick_sequence`; Delete in [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior|delete_enemy_behavior]].
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
^table-repeat-step-instance

**EnemySequenceStep** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemySequenceStep|definition]] — one step slot (index + Single/Repeat/Multi reference) inside an attack's sequence.
- **Changers** — Insert in `build_enemy_behavior`; never updated; Delete in `delete_enemy_behavior`.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — `tick_sequence` walks these rows to pick the current step.
^table-enemy-sequence-step

**EnemyAttack** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyAttack|definition]] — runtime cursor (step index, timers, start/loop-wait flags) for one attack sequence inside a phase.
- **Changers** — Insert in `build_enemy_behavior`; Update every behavior tick in [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] via `tick_sequence`/`tick_phase`; Delete in `delete_enemy_behavior`.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
^table-enemy-attack

**EnemyPhase** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyPhase|definition]] — runtime phase row (index, hp threshold, movement def, loop delay/wait flags, move-cycle start) per behavior.
- **Changers** — Insert in `build_enemy_behavior`; Update in `tick_phase` (wait flags, loop timers, move-cycle anchor only — `movement_def_id` is fixed at build); Delete in `delete_enemy_behavior`.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
^table-enemy-phase
**EnemyBehavior** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyBehavior|definition]] — one behavior root per enemy (aggro target + lock, sim flag, active phase, unit heading memory).
- **Changers** — Insert in `build_enemy_behavior`; Update in `tick_enemy_behavior` (aggro, `was_simulating`) and the combat sink on re-aggro; Delete in `delete_enemy_behavior`.
- **Client readers** — `public` but unsubscribed: no view exposes it (the client-facing angle+speed lives on ^table-enemy), no visible end.
^table-enemy-behavior

**Enemy** — [[server/spacetimedb/src/enemy/instance_tables.rs#Enemy|definition]] — one live enemy: template link, behavior link, x/y plus spawn anchor, movement as angle+scalar, hp/phase/elite/immortal flags, 0–100 stagger bar, `chunk_index`.
- **Changers** — Insert in [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype|spawn_enemy_archetype]] (via admin [[server/spacetimedb/src/main/admin.rs#spawn_enemy|spawn_enemy]] and the biome spawner in [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_spawn|tick_enemy_spawn]]); Update of position/movement/chunk in `tick_enemy_behavior`, of hp/phase in the combat sink, of stagger in the 1 Hz status tick; Delete in [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] (via admin despawn, combat kills with xp credit, `despawn_all_enemies`).
- **Client readers** — Via the [[server/spacetimedb/src/enemy/views.rs#nearby_enemies|nearby_enemies]] AOI view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#EntitySpawnerComponent|EntitySpawnerComponent]] `NearbyEnemiesBinder` (replay on) spawns one `Enemy` puppet (`default_enemy.tscn`) per row, and each puppet's own `NearbyEnemiesBinder` feeds [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowUpdated|OnEnemyRowUpdated]] → `InterpolationComponent` dead-reckoning targets, `HealthComponent`, phase/stagger state, and the debuff indicator's stagger bar; [[client/Scripts/Components/Weapon/CombatComponent.cs#CombatComponent|CombatComponent]] also binds it to invalidate its nearest-enemy aim.
- **Server readers** — The behavior tick's chunk-gated simulation set; `report_enemy_hit`'s victim lookup; zone/enemy-proximity queries.
^table-enemy

**EnemySpawnSchedule** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemySpawnSchedule|definition]] — the scheduled-job row arming the biome spawner tick.
- **Changers** — Insert by admin [[server/spacetimedb/src/main/admin.rs#toggle_enemy_spawning|toggle_enemy_spawning]] only (`enabled=true` arms the tick; [[server/spacetimedb/src/main/lifecycle.rs#init|init]] starts disarmed); Delete by the same toggle; never updated (interval rows persist).
- **Client readers** — Server-only schedule table (not `public`): never subscribed, no visible end.
^table-enemy-spawn-schedule

**EnemyBehaviorSchedule** — [[server/spacetimedb/src/enemy/instance_tables.rs#EnemyBehaviorSchedule|definition]] — the scheduled-job row arming the 100 ms behavior tick.
- **Changers** — Insert in `init`; never updated or deleted in normal operation.
- **Client readers** — Server-only schedule table (not `public`): never subscribed, no visible end.
^table-enemy-behavior-schedule

**BulletControlEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#BulletControlEvent|definition]] — append-only relay of one player's bullet-controller cast (delete/split/attract/rects/slash/wall); the server validates and rebroadcasts, every client resolves proximity locally, so geometry is encoded in the fields (see the table docs).
- **Changers** — Insert in [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] for bullet-control ability effects (also via `activate_ability` and charge release); append-only, never updated or deleted.
- **Client readers** — Subscribed as a raw event table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `BulletControlEventBinder` (**replay off**, wired under `BulletManager/BulletControllerComponent` in `game.tscn`) → [[client/Scripts/Components/Bullets/BulletControllerComponent.cs#BulletControllerComponent|BulletControllerComponent]] deletes/splits/attracts live enemy bullets near the cast (plus lingering attract/wall zones and the slash flash polygon); own echoes skipped via `cast_by`, and the caster applies the same cast optimistically from [[client/Scripts/Components/Inventory/LocalPlayerInventoryComponent.cs#TryActivateAbility|TryActivateAbility]].
^table-bullet-control-event

**AbilityVisualEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#AbilityVisualEvent|definition]] — append-only visual-only relay of a Spell cast (damage is applied server-side in the same dispatch); per-kind field encoding in the table docs.
- **Changers** — Insert in `apply_ability_effect` on the Spell path; append-only, never updated or deleted.
- **Client readers** — Subscribed as a raw event table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `AbilityVisualEventBinder` (**replay off**, wired on `SpellVisualComponent` in `game.tscn`) → [[client/Scripts/Components/Visual/SpellVisualComponent.cs#SpellVisualComponent|SpellVisualComponent]] draws the per-kind polygon flash; the caster renders optimistically and skips its own `cast_by` echo.
^table-ability-visual-event

**BulletFireAttestation** — [[server/spacetimedb/src/enemy/instance_tables.rs#BulletFireAttestation|definition]] — server-only proof that an enemy actually fired a `BulletPatternEvent`: pellet pool plus TTL, consumed by victim hit reports.
- **Changers** — Insert in [[server/spacetimedb/src/enemy/methods.rs#attest_bullet_fire|attest_bullet_fire]] per fired event; Update (pellet-pool decrement) or Delete (pool exhausted) in [[server/spacetimedb/src/player/reducers.rs#report_hit|report_hit]]; Delete of expired rows past `BULLET_ATTESTATION_TTL_SECONDS` in `tick_status_effects`.
- **Client readers** — Server-only (not `public`): clients only send the `event_id` back, no visible end.
^table-bullet-fire-attestation

**BulletPatternEvent** — [[server/spacetimedb/src/enemy/instance_tables.rs#BulletPatternEvent|definition]] — append-only record of one fired shot, with angles/offsets baked server-side at fire time plus damage, curve/accel, and `chunk_index`.
- **Changers** — Insert in `tick_sequence` during [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] (damage resolved at insert); append-only, never updated or deleted.
- **Client readers** — Subscribed as a raw event table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → the single shared `BulletPatternEventBinder` (**replay off**, on `BulletSpawnerComponent`) → [[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#BulletSpawnerComponent|BulletSpawnerComponent]] dispatches per `PatternType` through the BlastBullets2D factory (self-contained absolute origin, so no per-enemy binder fan-out and no lost volley during a puppet's deferred-spawn window).
^table-bullet-pattern-event

# Item

Pointer index: [[server/spacetimedb/src/item/pointers.md|pointers]].

**Item** — [[server/spacetimedb/src/item/tables.rs#Item|definition]] — the unified catalog: one row per item (`equip_slot` says where it goes, `stat_modifiers` + `behaviors` say what it does; `slot_cost` > 1 only for multi-slot abilities; innate enchantments always apply).
- **Changers** — Upsert by [[server/spacetimedb/src/item/seeds.rs#Item|Item::seed]] in `seed_world_items` and by admin [[server/spacetimedb/src/item/reducers.rs#upsert_item|upsert_item]] / [[server/spacetimedb/src/item/reducers.rs#import_staged_items|import_staged_items]]; never deleted.
- **Client readers** — Via the [[server/spacetimedb/src/item/views.rs#all_items|all_items]] global view → [[client/sstdbsdk/TableSubscriber.cs#BaseTables|BaseTables]] wave → `AllItemsBinder` on [[client/Scripts/Components/Catalog/CatalogComponent.cs#CatalogComponent|CatalogComponent]] (replay on, wired in `game.tscn`) → the `GetItem` cache behind slot icons (`InventoryPanel`), the hover composition (`ItemSidebar`), and the effective-weapon resolution behind every trigger pull (`CombatComponent`).
- **Server readers** — Behavior/cost/requirement lookups on every inventory touch (`transfer_item_to_inventory`, `slot_allowed`, `resolve_ability_slot`).
^table-item

**Enchantment** — [[server/spacetimedb/src/item/tables.rs#Enchantment|definition]] — one row per enchantment (stat modifiers, behaviors like shot adders and weapon toggles, allowed slots).
- **Changers** — Upsert by `Enchantment::seed` in `seed_world_enchantments` and by admin [[server/spacetimedb/src/item/reducers.rs#upsert_enchantment|upsert_enchantment]] / `import_staged_enchantments`; never deleted.
- **Client readers** — Via the [[server/spacetimedb/src/item/views.rs#all_enchantments|all_enchantments]] global view → [[client/sstdbsdk/TableSubscriber.cs#BaseTables|BaseTables]] wave → `AllEnchantmentsBinder` on `CatalogComponent` → socket/remove buttons and toggle lists in `ItemSidebar`, folded into the effective weapon by `EffectiveWeaponResolver` in the same slot order the server uses.
- **Server readers** — Folded into resolved stats and weapons in `recompute_stats` / `resolve_effective_weapon`.
^table-enchantment

**StagedItem** — [[server/spacetimedb/src/item/tables.rs#StagedItem|definition]] — staging copy of `Item` plus `staged_by`; live rows untouched until import.
- **Changers** — Insert/Update in [[server/spacetimedb/src/item/reducers.rs#stage_item|stage_item]] (admin-only); Delete in [[server/spacetimedb/src/item/reducers.rs#clear_staged_items|clear_staged_items]] only (`import_staged_items` upserts live rows without deleting staged rows).
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — [[server/spacetimedb/src/item/reducers.rs#validate_staged_items|validate_staged_items]] checks enchantment references before import.
^table-staged-item

**AbilityKnowledgeRequirement** — [[server/spacetimedb/src/item/tables.rs#AbilityKnowledgeRequirement|definition]] — per-ability knowledge gate (no row = no gate, backward compatible).
- **Changers** — Insert/Update in [[server/spacetimedb/src/player/reducers.rs#set_ability_knowledge|set_ability_knowledge]] (admin-only); never deleted.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — [[server/spacetimedb/src/player/reducers.rs#resolve_ability_slot|resolve_ability_slot]] rejects gated abilities the profile hasn't acquired.
^table-ability-knowledge-requirement

**StagedEnchantment** — [[server/spacetimedb/src/item/tables.rs#StagedEnchantment|definition]] — staging copy of `Enchantment` plus `staged_by`.
- **Changers** — Insert/Update in [[server/spacetimedb/src/item/reducers.rs#stage_enchantment|stage_enchantment]] (admin-only); Delete in `clear_staged_enchantments` only (`import_staged_enchantments` upserts live rows without deleting staged rows).
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — `validate_staged_enchantments` runs the pre-import checks.
^table-staged-enchantment
# Main

Pointer index: [[server/spacetimedb/src/main/pointers.md|pointers]].

**AgentConfig** — [[server/spacetimedb/src/main/agents.rs#AgentConfig|definition]] — single-row master switch for background agents, keyed by fixed id 0.
- **Changers** — Insert in [[server/spacetimedb/src/main/agents.rs#init_agents|init_agents]] (called from `init` after seeds, idempotent); Insert/Update in [[server/spacetimedb/src/main/agents.rs#set_agents_enabled|set_agents_enabled]] (admin-only); never deleted.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — [[server/spacetimedb/src/main/agents.rs#should_run|should_run]] gates the regen/zone/cleanup passes (fail-closed when the row is missing).
^table-agent-config

**PlayerPositionDebug** — [[server/spacetimedb/src/main/debug.rs#PlayerPositionDebug|definition]] — one mirror row per live `PlayerPosition` (plus hex + rotation) while debug mode is armed.
- **Changers** — Insert/Update per position row in [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug|tick_player_position_debug]], which also deletes rows for vanished players and all rows on toggle-off; never written by gameplay.
- **Client readers** — Server-only (not `public`): inspect via `spacetime sql`; the client's `DebugOverlay` is a purely local perf HUD and never touches this table.
^table-player-position-debug

**PlayerPositionDebugSchedule** — [[server/spacetimedb/src/main/debug.rs#PlayerPositionDebugSchedule|definition]] — the scheduled-job row arming the debug tick.
- **Changers** — Insert/Delete (arm/disarm) in [[server/spacetimedb/src/main/debug.rs#toggle_debug|toggle_debug]] (admin-only); whoever arms it is its only changer.
- **Client readers** — Server-only schedule table (not `public`): never subscribed, no visible end.
^table-player-position-debug-schedule

# Player

Pointer index: [[server/spacetimedb/src/player/pointers.md|pointers]].

**PlayerInventorySlot** — [[server/spacetimedb/src/player/tables.rs#PlayerInventorySlot|definition]] — one row per inventory slot (role-derived acceptance, per-slot cooldown/charges/toggle, multi-slot span occupancy via `occupied_by`).
- **Changers** — Insert of the full slot set in [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]]; Update through [[server/spacetimedb/src/player/methods.rs#update_slot|update_slot]] from `swap_slots`, `use_item`, ability spend, `apply_enchantment`/`remove_enchantment`, `pickup_drop`, `drop_item`, [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]], trade `confirm_swap`, and admin `give_item`/`remove_item`; Delete of all rows in `teardown_profile`.
- **Client readers** — Via the [[server/spacetimedb/src/player/views.rs#local_player_inventory|local_player_inventory]] per-caller view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `LocalPlayerInventoryBinder` on [[client/Scripts/Components/Inventory/LocalPlayerInventoryComponent.cs#LocalPlayerInventoryComponent|LocalPlayerInventoryComponent]] (replay on, wired in `local_player.tscn`) → the `InventoryPanel` slot grid, the `ItemSidebar` hover composition, and the effective weapon behind `CombatComponent` firing.
- **Server readers** — `equipped_head_slots` / `resolve_effective_weapon` fold these rows for stats and firing; `slot_allowed` derives acceptance from the role.
^table-player-inventory-slot

**LoggedInPlayer** — [[server/spacetimedb/src/player/tables.rs#LoggedInPlayer|definition]] — one row per identity currently in the world (username, admin flag, unique active profile).
- **Changers** — Insert in [[server/spacetimedb/src/player/reducers.rs#join_world|join_world]]; Delete + move back to ^table-logged-out-player in [[server/spacetimedb/src/player/reducers.rs#leave_world|leave_world]], `client_disconnected`, `kill_player` (via `teardown_profile`), and the already-in-world branch of `client_connected`; Update of `is_admin` in `claim_admin`/`release_admin` and of `username` in `set_username`.
- **Client readers** — Via the [[server/spacetimedb/src/player/views.rs#local_player|local_player]] per-caller view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `LocalPlayerBinder` on `EntitySpawnerComponent` (replay on) → spawns the single `LocalPlayer` scene when the row arrives.
- **Server readers** — [[server/spacetimedb/src/player/methods.rs#require_logged_in|require_logged_in]] / `require_in_world` guard every world reducer; AOI views left-semijoin through it so ghosts never leak to clients.
^table-logged-in-player

**LoggedOutPlayer** — [[server/spacetimedb/src/player/tables.rs#LoggedOutPlayer|definition]] — one row per known identity currently in the lobby.
- **Changers** — Insert in [[server/spacetimedb/src/main/lifecycle.rs#client_connected|client_connected]] (first connect and every world exit path); Delete in `join_world`; Update of flags in `claim_admin`/`release_admin`/`set_username`.
- **Client readers** — Via the [[server/spacetimedb/src/player/views.rs#local_lobby_player|local_lobby_player]] per-caller view → [[client/sstdbsdk/TableSubscriber.cs#LobbyTables|LobbyTables]] wave → `DatabaseConnector`'s direct `OnInsert`/`OnUpdate` hooks mirror `Username` (the one sanctioned non-binder read); lobby panels themselves render ^table-player-profile rows.
- **Server readers** — [[server/spacetimedb/src/player/methods.rs#require_in_lobby|require_in_lobby]] gates profile management; the admin-slot check scans both login tables.
^table-logged-out-player

**PlayerProfile** — [[server/spacetimedb/src/player/tables.rs#PlayerProfile|definition]] — one playable character per player (name, texture, aim settings), the `profile_id` entity key joining every per-character row.
- **Changers** — Insert in `client_connected` (first profile scaffold) and [[server/spacetimedb/src/player/reducers.rs#create_profile|create_profile]]; Delete in [[server/spacetimedb/src/player/reducers.rs#delete_profile|delete_profile]] (plus full `teardown_profile` cleanup); never updated after creation.
- **Client readers** — Three views: [[server/spacetimedb/src/player/views.rs#local_player_profiles|local_player_profiles]] → `LobbyTables` → `LobbyComponent`'s profiles binder builds one profile panel per row; [[server/spacetimedb/src/player/views.rs#local_player_active_profile|local_player_active_profile]] → `GameTables` → `LocalPlayerProfileComponent`'s binder drives the local sprite/model texture and aim-assist settings; [[server/spacetimedb/src/player/views.rs#nearby_remote_players_profiles|nearby_remote_players_profiles]] (AOI-filtered through the same chunk set as positions) → `GameTables` → [[client/Scripts/Players/Remote/RemotePlayer.cs#RemotePlayer|RemotePlayer]] reads it one-shot via `Iter()` to skin each puppet (2D SpriteFrames or catalog 3D model).
^table-player-profile

**PlayerData** — [[server/spacetimedb/src/player/tables.rs#PlayerData|definition]] — one row per profile: level/xp plus resolved hp/max_hp, `defense`, and `base_speed` (modifier-only outputs of `recompute_stats`, same pattern as max_hp).
- **Changers** — Insert in `try_scaffold_profile`; Update in [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] (resolution), [[server/spacetimedb/src/player/methods.rs#internal_gain_xp|internal_gain_xp]] (level/xp), the combat sinks (hp subtract/heal), and admin `change_stats`; Delete in `teardown_profile`.
- **Client readers** — Via the [[server/spacetimedb/src/player/views.rs#local_player_data|local_player_data]] per-caller view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `LocalPlayerDataBinder` on `LocalPlayerDataComponent` (replay on) → mirrors hp into `HealthComponent` (health bar, zero-death) and exposes Level/Defense/BaseSpeed to `StatsSidebar` and movement clamps.
- **Server readers** — `report_movement` clamps speed to `base_speed`; damage math reads `defense`.
^table-player-data

**PlayerStats** — [[server/spacetimedb/src/player/tables.rs#PlayerStats|definition]] — the six resolved allocatable stats (strength/wisdom/dexterity + three temperaments); Hp/Defense/BaseSpeed resolve into ^table-player-data instead and have no field here.
- **Changers** — Insert in `try_scaffold_profile`; Update in `recompute_stats` only; Delete in `teardown_profile`.
- **Client readers** — Via the [[server/spacetimedb/src/player/views.rs#local_player_stats|local_player_stats]] per-caller view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `LocalPlayerStatsBinder` on `LocalPlayerDataComponent` → the `StatsComponent` mirrors behind `StatsSidebar`'s six stat lines and `+` buttons.
- **Server readers** — `compute_player_damage` scales off strength; `check_stat_requirements` gates equipping.
^table-player-stats

**PlayerStatAllocation** — [[server/spacetimedb/src/player/tables.rs#PlayerStatAllocation|definition]] — the allocation *input* behind ^table-player-stats: per-stat assigned points plus `unspent_points`.
- **Changers** — Insert in `try_scaffold_profile` (unspent from level); Update in [[server/spacetimedb/src/player/reducers.rs#allocate_stat|allocate_stat]] and `internal_gain_xp` (grants `SKILL_POINTS_PER_LEVEL` per level), plus admin `change_stats`; Delete in `teardown_profile`.
- **Client readers** — Via the [[server/spacetimedb/src/player/views.rs#local_player_stat_allocation|local_player_stat_allocation]] per-caller view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `LocalPlayerStatAllocationBinder` on `LocalPlayerDataComponent` → the unspent-points line and per-stat `+` buttons in `StatsSidebar`.
^table-player-stat-allocation
**PlayerPosition** — [[server/spacetimedb/src/player/tables.rs#PlayerPosition|definition]] — one row per profile: x/y plus movement as angle+scalar (clients reconstruct velocity for dead reckoning), with `chunk_index` for AOI filtering.
- **Changers** — Insert in `try_scaffold_profile` and on respawn; Update in [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] (displacement-clamped along the reported vector, elevation-gated, grapple/fall exemptions) and in [[server/spacetimedb/src/player/methods.rs#teleport_player|teleport_player]] (Teleport abilities, exempt and restamping); Delete in `teardown_profile` (rows otherwise persist while logged out as "ghosts").
- **Client readers** — Own row via [[server/spacetimedb/src/player/views.rs#local_player_position|local_player_position]] → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `LocalPlayerPositionBinder` on `PositionSyncComponent` (echo/reconciliation; local motion itself is input-driven); remote rows via [[server/spacetimedb/src/player/views.rs#nearby_remote_players|nearby_remote_players]] (own row excluded, logged-out ghosts semijoined out) → `EntitySpawnerComponent` spawns one `RemotePlayer` puppet per row, and each puppet's `NearbyRemotePlayersBinder` (wired in `non_local_player.tscn`) feeds `InterpolationComponent` dead-reckoning targets.
- **Server readers** — Every AOI view builds its chunk set from ^table-player-chunk, then filters *this* table's `chunk_index`; enemy sim, trade range, zone membership, and `players_near_point` all read positions.
^table-player-position

**PlayerRotation** — [[server/spacetimedb/src/player/tables.rs#PlayerRotation|definition]] — one row per profile: screen/camera facing angle on its own faster cadence, with `chunk_index` mirrored so AOI filters identically.
- **Changers** — Insert in `try_scaffold_profile`; Update in [[server/spacetimedb/src/player/reducers.rs#report_screen_rotation|report_screen_rotation]] (sub-half-degree changes skip the write); Delete in `teardown_profile`.
- **Client readers** — Via [[server/spacetimedb/src/player/views.rs#nearby_remote_player_rotations|nearby_remote_player_rotations]] (same AOI filter, logged-in semijoin; no separate local view — the owner's own row arrives through this view too) → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → each `RemotePlayer`'s `NearbyRemotePlayerRotationsBinder` → `InterpolationComponent` screen-rotation targets (puppet facing).
^table-player-rotation

**PlayerChunk** — [[server/spacetimedb/src/player/tables.rs#PlayerChunk|definition]] — one row per profile holding the current chunk (q/r), written only on real chunk crossings so views don't recompute on every 10 Hz movement report.
- **Changers** — Insert in `try_scaffold_profile`; Update in `report_movement`/`teleport_player` only when the chunk actually changes; Delete in `teardown_profile`.
- **Client readers** — `public` but unsubscribed: no wave carries it and no view exposes it — it exists so views key off it server-side instead of ^table-player-position.
- **Server readers** — [[server/spacetimedb/src/player/views.rs#nearby_indices_from_chunk|nearby_indices_from_chunk]] builds every AOI view's chunk set from it; the enemy sim reads it per logged-in player.
^table-player-chunk

**ActiveConsumableEffect** — [[server/spacetimedb/src/player/tables.rs#ActiveConsumableEffect|definition]] — one row per live plain-stat consumable buff (modifier + remaining seconds); anything needing tick consults lives in ^table-active-status-effect instead.
- **Changers** — Insert in [[server/spacetimedb/src/player/methods.rs#apply_consumable_effect|apply_consumable_effect]] (via `use_item`, `activate_ability`, zone refresh); Update/Decrement and expiry Delete in [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]]; Delete of all rows in `teardown_profile`.
- **Client readers** — `public` but unsubscribed: no view or wave carries it, no visible end (buffs surface through resolved stats, not these rows).
- **Server readers** — `recompute_stats` folds every row into the resolved accumulators.
^table-active-consumable-effect

**ConsumableEffectSchedule** — [[server/spacetimedb/src/player/tables.rs#ConsumableEffectSchedule|definition]] — the single 1 Hz scheduled-job row driving the whole status tick (consumable expiry plus `status::methods::tick`).
- **Changers** — Insert in `init` (and gap-filled in `init_agents`); never updated or deleted in normal operation.
- **Client readers** — Server-only schedule table (not `public`): never subscribed, no visible end.
^table-consumable-effect-schedule

**LootDrop** — [[server/spacetimedb/src/player/tables.rs#LootDrop|definition]] — one dropped item in the world (position, chunk, expiry, source, carried enchantments plus the dropped slot's cooldown/charges so ability runtime state survives the trip).
- **Changers** — Insert in [[server/spacetimedb/src/player/reducers.rs#drop_item|drop_item]] (span heads drop whole spans); Delete in [[server/spacetimedb/src/player/reducers.rs#pickup_drop|pickup_drop]] and on `expires_at` in `tick_status_effects` (plus `expires_at` rejection at pickup); never updated.
- **Client readers** — Via the [[server/spacetimedb/src/player/views.rs#nearby_loot_drops|nearby_loot_drops]] AOI view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `NearbyLootDropsBinder` on `EntitySpawnerComponent` (replay on) → spawns one `Drop` node (`drop.tscn`: catalog sprite + `PickupComponent`) per row, whose area touch reports `pickup_drop`.
^table-loot-drop

**PlayerMovementState** — [[server/spacetimedb/src/player/tables.rs#PlayerMovementState|definition]] — server-only per-profile movement bookkeeping: last report timestamp plus downhill-fall lockout.
- **Changers** — Insert/Update in `report_movement` and `teleport_player` (teleports restamp, exempt from the clamp); Delete in `teardown_profile`.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — The per-report budget (`base_speed` × dt × `MOVE_SLACK`) and the `falling_until` movement lockout.
^table-player-movement-state

**PlayerDisplacementAllowance** — [[server/spacetimedb/src/player/tables.rs#PlayerDisplacementAllowance|definition]] — server-only one-shot Grapple window: a single oversized movement report up to `max_distance` is accepted while the row lives.
- **Changers** — Insert/Update on Grapple cast in `apply_ability_effect`; Delete on use, on expiry contact in `report_movement`, and in `teardown_profile`.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end (the pull itself is client-tweened).
^table-player-displacement-allowance

**PlayerHitRate** — [[server/spacetimedb/src/player/tables.rs#PlayerHitRate|definition]] — server-only 1-second token bucket per profile with separate outgoing/incoming counters.
- **Changers** — Insert/Update in [[server/spacetimedb/src/player/methods.rs#hit_rate_allow|hit_rate_allow]] on every reported hit; Delete in `teardown_profile`.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — Caps `report_enemy_hit` at weapon rate × shot count × slack and `report_hit` at `INCOMING_HIT_RATE_CAP`.
^table-player-hit-rate

**PlayerKnowledge** — [[server/spacetimedb/src/player/tables.rs#PlayerKnowledge|definition]] — one row per profile: auto-filled `discovered` ids plus explicitly unlocked `acquired` ids (unknown items render as ???).
- **Changers** — Insert in `try_scaffold_profile`; Update in [[server/spacetimedb/src/player/methods.rs#discover_ids|discover_ids]] (auto-discover on `pickup_drop`/`take_from_chest`/observe) and in [[server/spacetimedb/src/player/reducers.rs#discover_knowledge|discover_knowledge]] / [[server/spacetimedb/src/player/reducers.rs#acquire_knowledge|acquire_knowledge]]; Delete in `teardown_profile`.
- **Client readers** — Via the [[server/spacetimedb/src/player/views.rs#local_player_knowledge|local_player_knowledge]] per-caller view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → subscribed but no binder consumes it yet, so no visible end (the `ItemSidebar` ??? masking is planned).
- **Server readers** — Ability knowledge gates consult `acquired` in `resolve_ability_slot`.
^table-player-knowledge

**PlayerSurvival** — [[server/spacetimedb/src/player/tables.rs#PlayerSurvival|definition]] — server-only per-profile survival sim: last-damage stamp, attrition ticks, last regen.
- **Changers** — Insert in `try_scaffold_profile`; Update (stamp) in `intercept_incoming` on every hit and in the regen pass of the 1 Hz tick; Delete in `teardown_profile`.
- **Client readers** — Server-only (not `public`): never subscribed, no visible end.
- **Server readers** — The regen/attrition pass only ticks `LoggedInPlayer` profiles and deals attrition through the shared damage sink, never direct HP writes.
^table-player-survival
# Status

Pointer index: [[server/spacetimedb/src/status/pointers.md|pointers]].

**GroundZone** — [[server/spacetimedb/src/status/tables.rs#GroundZone|definition]] — one placed ground circle (owner, kind, center, radius, magnitude, remaining); ally zones re-apply on the 1 Hz tick, bursts fire once at expiry, `DamageShare` is consulted live with no per-tick effect.
- **Changers** — Insert by the `CastZone` arm of `apply_ability_effect`; Update (`remaining` countdown) and expiry Delete in [[server/spacetimedb/src/status/methods.rs#tick|tick]]; Delete of the owner's rows in [[server/spacetimedb/src/status/methods.rs#purge_profile_zones|purge_profile_zones]] (death/`leave_world`/disconnect).
- **Client readers** — Subscribed as a raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `GroundZoneBinder` on [[client/Scripts/Components/Zones/ZoneRendererComponent.cs#ZoneRendererComponent|ZoneRendererComponent]] (replay on, wired in `game.tscn`) → one code-created `Polygon2D` circle per row, colored per `ZoneKind`.
- **Server readers** — `intercept_incoming` splits hits across a `DamageShare` zone's live occupants; the zone pass refreshes debuffs/statuses by live membership each tick.
^table-ground-zone

**ActiveStatusEffect** — [[server/spacetimedb/src/status/tables.rs#ActiveStatusEffect|definition]] — one active player status row (one per profile+kind, except the mark kinds sharing a single mark slot); `pool`/`counter`/`progress`/`violated` dual-purpose by kind per the table docs.
- **Changers** — Insert in [[server/spacetimedb/src/status/methods.rs#apply_status_effect|apply_status_effect]] (every targeting variant funnels here); Update/Delete across `intercept_incoming` (counters, shields consumed), wager settlement, purge evaluation, and the 1 Hz `tick` (drains via the flagged damage path, never direct HP writes); mass Delete in [[server/spacetimedb/src/status/methods.rs#purge_profile_statuses|purge_profile_statuses]] and [[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]] for mark rows.
- **Client readers** — Subscribed as a raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `ActiveStatusEffectBinder` on [[client/Scripts/Components/Status/StatusBarComponent.cs#StatusBarComponent|StatusBarComponent]] (replay on, wired in `local_player.tscn`, filtered to the local profile in code) → the "Kind N.Ns" label strip above the hotbar, counting down locally between server updates.
- **Server readers** — Fixed-order intercept (`Invulnerable` → shield → redirect → share → reduction → bleed) runs on every hit; outgoing consults scale or convert dealt damage.
^table-active-status-effect

**AbilityCharge** — [[server/spacetimedb/src/status/tables.rs#AbilityCharge|definition]] — one held ability charge per player (slot, start time); a vanished charge spends no cooldown.
- **Changers** — Insert in [[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]] (refused while a row exists); Delete in [[server/spacetimedb/src/status/reducers.rs#release_ability_charge|release_ability_charge]] / [[server/spacetimedb/src/status/reducers.rs#internal_release_charge|internal_release_charge]] (shared release path, also used by the timeout auto-release at `MAX_CHARGE_SECONDS`) and by the lifecycle purge; never updated.
- **Client readers** — Subscribed as a raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `AbilityChargeBinder` on `AbilityChargeComponent` (replay on, echo-only — timeouts and purges clear it with no optimistic flag) → the `Charging` state driving the cosmetic SelfSlow on `PositionSyncComponent` and `CombatComponent`'s fire suppression.
- **Server readers** — `report_movement` enforces the real speed clamp while a charge is held; release scales range/damage by the charge fraction.
^table-ability-charge

**ChanneledAction** — [[server/spacetimedb/src/status/tables.rs#ChanneledAction|definition]] — one progressive action (Revive/Capture/ChannelCast) with lock/progress/expiry; max 1 active per player plus max N per target.
- **Changers** — Insert in [[server/spacetimedb/src/status/reducers.rs#begin_channeled_action|begin_channeled_action]]; Update (progress) in the 1 Hz `tick_channeled` sweep ([[server/spacetimedb/src/status/reducers.rs#continue_channeled_action|continue_channeled_action]] is keep-alive only — no progress write, expiry delete only); Delete in [[server/spacetimedb/src/status/reducers.rs#cancel_channeled_action|cancel_channeled_action]], on completion (dispatches into `heal_player` only), on lock expiry in the tick, and in `teardown_profile`.
- **Client readers** — Subscribed as a raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → subscribed but no binder consumes it yet, so no visible end.
^table-channeled-action

**ActiveEnemyStatusEffect** — [[server/spacetimedb/src/status/tables.rs#ActiveEnemyStatusEffect|definition]] — one enemy debuff row per (enemy, kind): DoT/Slow/Vulnerability/ArmorBreak/Stunned with per-kind magnitude.
- **Changers** — Insert (or refresh-in-place on re-apply) in [[server/spacetimedb/src/status/methods.rs#apply_enemy_status|apply_enemy_status]] (via `Curse` abilities and enemy zones); Update/DoT-tick/expiry Delete in the 1 Hz `tick`; Delete of all rows for a dead enemy in `on_enemy_killed` (rows never outlive their enemy).
- **Client readers** — Subscribed as a raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `ActiveEnemyStatusEffectBinder` on [[client/Scripts/Components/Status/EnemyDebuffIndicatorComponent.cs#EnemyDebuffIndicatorComponent|EnemyDebuffIndicatorComponent]] (replay on, wired at `DebuffAttach` in `default_enemy.tscn`, filtered per enemy in code) → colored kind chips plus the 0–100 stagger bar over each enemy.
- **Server readers** — The combat sink consults ArmorBreak/Vulnerability per hit; the behavior tick scales movement and fire pacing per Slow/Stun/Enraged.
^table-active-enemy-status-effect

# Trade

Pointer index: [[server/spacetimedb/src/trade/pointers.md|pointers]].

**TradeSession** — [[server/spacetimedb/src/trade/mod.rs#TradeSession|definition]] — one two-party trade (offers as slot-index pockets both directions, dual-accept status, resolution message); the swap executes atomically in one reducer so partial swaps are impossible.
- **Changers** — Insert in [[server/spacetimedb/src/trade/mod.rs#trade_initiate|trade_initiate]] (one session per player, in-range, out-of-combat gates); Update in [[server/spacetimedb/src/trade/mod.rs#trade_add_item|trade_add_item]], [[server/spacetimedb/src/trade/mod.rs#trade_remove_item|trade_remove_item]], [[server/spacetimedb/src/trade/mod.rs#trade_accept|trade_accept]] (second accept runs `confirm_swap`), [[server/spacetimedb/src/trade/mod.rs#trade_decline|trade_decline]], and the timeout/logout/range/combat sweep in [[server/spacetimedb/src/trade/mod.rs#tick_trade_sessions|tick_trade_sessions]] (which also deletes resolved rows); multi-slot ability spans are rejected at swap time to avoid orphaning followers.
- **Client readers** — Via the [[server/spacetimedb/src/trade/mod.rs#local_trade_session|local_trade_session]] per-caller view (either party) → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → subscribed but no binder consumes it yet, so no visible end (trade offer panel planned).
- **Server readers** — `active_session_for` blocks double sessions at initiate time.
^table-trade-session

# World

Pointer index: [[server/spacetimedb/src/world/pointers.md|pointers]].

**MapConfig** — [[server/spacetimedb/src/world/tables.rs#MapConfig|definition]] — singleton world-grid row (id 0): chunk radii/counts, hex size, and the torus lap vectors clients need to render near seams; read through `MapConfig::load` with caller-chosen degenerate-grid fallbacks, never re-derived.
- **Changers** — Insert/Update (upsert id 0) in [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] (via `add_chunks` and `init`) only; never deleted.
- **Client readers** — Subscribed as a raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `TableSubscriber`'s direct `OnInsert`/`OnUpdate` hooks mirror the lap vectors into its `LapQ`/`LapR` statics (read per entity per frame by `InterpolationComponent` torus math), while replaying `MapConfigBinder` children on `TerrainComponent`, `ElevationMapComponent`, `HexGridOverlayComponent`, and the 3D components rebuild grids and meshes from the row.
- **Server readers** — `MapConfig::load` in every wrap/AOI/spawn path (the one place `player/views.rs` reads the row directly with 1/1 defaults, since a `ViewContext` can't call the helper).
^table-map-config

**BuildingTile** — [[server/spacetimedb/src/world/tables.rs#BuildingTile|definition]] — one row per hex: grid position, chunk index, legacy `building_type` stamp plus owner, and the integer elevation level stamped by generation.
- **Changers** — Insert per hex in `internal_add_chunks`; Delete of all rows in `clear_chunks`; Update of the stamp in [[server/spacetimedb/src/world/reducers.rs#place_building|place_building]] / [[server/spacetimedb/src/world/reducers.rs#remove_building|remove_building]] (admin-only world authoring, no client UI calls them) and of elevation in world generation; footprint completion also stamps/clears these rows for backward compatibility.
- **Client readers** — Via the [[server/spacetimedb/src/world/views.rs#nearby_building_tiles|nearby_building_tiles]] AOI view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `NearbyBuildingTilesBinder` on [[client/Scripts/Components/Terrain/ElevationMapComponent.cs#ElevationMapComponent|ElevationMapComponent]] (replay on, wired in `game.tscn`) → the per-hex elevation dictionary behind 2D tint cues, movement fall prediction, and the bullet elevation-band rule (no building visuals exist — the stamp/owner columns are write-only world authoring for now).
- **Server readers** — Hex-existence check ("has `add_chunks` been called?"); elevation deltas gate `report_movement` (walk/block/fall); enemies probe cliffs through the same levels.
^table-building-tile

**TextureEntry** — [[server/spacetimedb/src/world/tables.rs#TextureEntry|definition]] — one row per texture id: 2D sprite/icon path plus 3D model path (empty = no 3D representation) and kind.
- **Changers** — Upsert in `seed_default_textures` and admin [[server/spacetimedb/src/main/admin.rs#upsert_texture_entry|upsert_texture_entry]] / `upsert_texture_entry_fields`; never deleted.
- **Client readers** — Via the [[server/spacetimedb/src/world/views.rs#all_textures|all_textures]] global view → [[client/sstdbsdk/TableSubscriber.cs#BaseTables|BaseTables]] wave → `AllTexturesBinder` on `CatalogComponent` → `GetResPath2D`/`GetResPath3D` behind every sprite, icon, drop, enemy, remote-player, and character-model load (3D mode loads no 2D world asset at all).
^table-texture-entry

**AnimationEntry** — [[server/spacetimedb/src/world/tables.rs#AnimationEntry|definition]] — one named animation clip living in a source scene's `AnimationPlayer`, merged into character models at runtime and played by id.
- **Changers** — Upsert in the default-animation seeds and admin [[server/spacetimedb/src/main/admin.rs#upsert_animation_entry|upsert_animation_entry]]; never deleted.
- **Client readers** — Via the [[server/spacetimedb/src/world/views.rs#all_animations|all_animations]] global view → [[client/sstdbsdk/TableSubscriber.cs#BaseTables|BaseTables]] wave → `AllAnimationsBinder` on `CatalogComponent` → clip merges into the active character model's `AnimationPlayer`.
^table-animation-entry

**LayeringRule** — [[server/spacetimedb/src/world/tables.rs#LayeringRule|definition]] — allow-list: an overlay tag may layer on an allowed base tag (default deny).
- **Changers** — Delete-all plus Insert of the seed set in [[server/spacetimedb/src/main/seeds.rs#seed_layering_rules|seed_layering_rules]]; never touched by reducers.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — Per-hex compatibility check in world generation.
^table-layering-rule

**BaseAdjacencyRule** — [[server/spacetimedb/src/world/tables.rs#BaseAdjacencyRule|definition]] — deny-list of base-tag pairs that may never sit in neighboring tiles (symmetric, one row per pair; default allow).
- **Changers** — Delete-all in seeds (companion to the layering seeds); never touched by reducers.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — Neighbor check in world generation.
^table-base-adjacency-rule

**OverlayAdjacencyRule** — [[server/spacetimedb/src/world/tables.rs#OverlayAdjacencyRule|definition]] — same shape as ^table-base-adjacency-rule but overlay-to-overlay, kept separate so a shared tag string can never cross-apply between layers.
- **Changers** — Delete-all in seeds; never touched by reducers.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — Neighbor check in world generation.
^table-overlay-adjacency-rule

**DecorGroundRule** — [[server/spacetimedb/src/world/tables.rs#DecorGroundRule|definition]] — deny-list: decor with a tag may never sit on a hex carrying a denied ground/overlay tag (not symmetric — two different tag kinds).
- **Changers** — Delete-all plus Insert of the seed set in [[server/spacetimedb/src/main/seeds.rs#seed_decor_ground_rules|seed_decor_ground_rules]]; never touched by reducers.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — Per-wedge decor conflict check in world generation.
^table-decor-ground-rule
**WorldDef** — [[server/spacetimedb/src/world/def_tables.rs#WorldDef|definition]] — one named world: display name plus biome weight list for procedural generation.
- **Changers** — Upsert in `seed_world_defs` and admin [[server/spacetimedb/src/main/admin.rs#upsert_world_def|upsert_world_def]]; never deleted.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — `internal_generate_world_proc` reads the def (and its biomes) to generate.
^table-world-def

**BiomeDef** — [[server/spacetimedb/src/world/def_tables.rs#BiomeDef|definition]] — one biome: ground/overlay/decor texture configs with chances, tags, rotations, noise scales, and decor separation.
- **Changers** — Upsert in the world seeds and admin [[server/spacetimedb/src/main/admin.rs#upsert_biome_def|upsert_biome_def]]; never deleted.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — Read per hex by `choose_ground`/`choose_overlay`/`place_decor` during generation.
^table-biome-def

**BiomeRegionDef** — [[server/spacetimedb/src/world/def_tables.rs#BiomeRegionDef|definition]] — one spawn-region definition: enemy template ids, max enemies, spawn radius.
- **Changers** — Upsert in `seed_region_def` and admin [[server/spacetimedb/src/main/admin.rs#upsert_biome_region_def|upsert_biome_region_def]]; never deleted.
- **Client readers** — `public` but unsubscribed: no wave carries it, no visible end.
- **Server readers** — Generation instantiates regions from these defs; the spawner reads live ^table-biome-region rows.
^table-biome-region-def

**BiomeRegion** — [[server/spacetimedb/src/world/instance_tables.rs#BiomeRegion|definition]] — one generated spawn region (biome link, center hex, template ids, caps).
- **Changers** — Delete-all plus Insert per region in world generation (`run_generation`); never touched by reducers.
- **Client readers** — `public` but unsubscribed: no view or wave carries it, no visible end (there is deliberately no `EnemyZone` table — spawn regions are these rows).
- **Server readers** — `tick_enemy_spawn` picks templates and caps live enemies per region.
^table-biome-region

**HexTile** — [[server/spacetimedb/src/world/instance_tables.rs#HexTile|definition]] — one row per generated hex: position, chunk, ground texture + rotation, optional overlay (with copied scale so clients render without joining `BiomeDef`), biome/region links.
- **Changers** — Delete-all plus Insert per hex in `run_generation` (`write_hex_tile`); never updated or touched by reducers.
- **Client readers** — Via the [[server/spacetimedb/src/world/views.rs#nearby_terrain_tiles|nearby_terrain_tiles]] AOI view (wider terrain ring) → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `TerrainTilesBinder` on `TerrainComponent` (replay on, wired in `game.tscn`) → pooled `TileComponent` ground/overlay MultiMesh batches, plus [[client/Scripts/Components/PixelArt/WaterComponent3D.cs#WaterComponent3D|WaterComponent3D]]'s `NearbyTerrainTilesBinder` (wired in `world_3d.tscn`), which floats water tiles exactly where rows are *missing*. Staleness note: the generated bindings still expose the old `TriangleTile` row shape, which `TerrainComponent`/`WaterComponent3D` read until `server/build.sh` regenerates the bindings and migrates them to `HexTile`.
^table-hex-tile

**HexDecor** — [[server/spacetimedb/src/world/instance_tables.rs#HexDecor|definition]] — at most one decor prop per hex (texture + paired shadow sprite rotating as one unit, rotation, biome/region); hexes without decor simply have no row.
- **Changers** — Delete-all plus Insert in `run_generation` (`place_decor`, separation rules enforced); never updated or touched by reducers.
- **Client readers** — Via the [[server/spacetimedb/src/world/views.rs#nearby_hex_decor|nearby_hex_decor]] AOI view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `HexDecorBinder` on `TerrainComponent` → the decor/shadow MultiMesh layers of each pooled tile.
^table-hex-decor

**BuildingDesc** — [[server/spacetimedb/src/world/building.rs#BuildingDesc|definition]] — static building catalog: footprint as rotated axial deltas, max health, build cost in work actions.
- **Changers** — Upsert in [[server/spacetimedb/src/world/building.rs#seed_default_buildings|seed_default_buildings]] and admin [[server/spacetimedb/src/world/building.rs#upsert_building_desc|upsert_building_desc]]; never deleted.
- **Client readers** — `public` but unsubscribed (no wave carries `all_building_desc`), no visible end.
- **Server readers** — Footprint math (`footprint_cells`) for placement, occupancy, and claim checks; completion reads costs.
^table-building-desc

**BuildingState** — [[server/spacetimedb/src/world/building.rs#BuildingState|definition]] — one finished building anchor (anchor hex, chunk, direction, def link, territory, owner).
- **Changers** — Insert on site completion in `complete_site` (instant for zero-action descs); Update of `territory_id` to wilderness on claim dissolve; Delete in [[server/spacetimedb/src/world/building.rs#demolish_building|demolish_building]] and `clear_chunks`; never otherwise updated.
- **Client readers** — `public` but unsubscribed (no wave carries `nearby_buildings`), no visible end.
- **Server readers** — Occupancy checks; claim-dissolve releases these rows.
^table-building-state

**FootprintTileState** — [[server/spacetimedb/src/world/building.rs#FootprintTileState|definition]] — one row per footprint hex of a finished building (owner building, hex, chunk, per-tile role).
- **Changers** — Insert per cell in `complete_site`; Delete in `demolish_building` and `clear_chunks`; never updated.
- **Client readers** — `public` but unsubscribed (no wave carries `nearby_footprint_tiles`), no visible end.
- **Server readers** — `hex_occupied`, the first occupancy check consulted.
^table-footprint-tile

**ProjectSiteState** — [[server/spacetimedb/src/world/building.rs#ProjectSiteState|definition]] — one construction site (anchor, def, direction, work progress, owner, territory); footprint stays implicit from the desc so site and building ids never collide.
- **Changers** — Insert in [[server/spacetimedb/src/world/building.rs#place_project_site|place_project_site]] (footprint + claim-permission validated); Update (progress) in [[server/spacetimedb/src/world/building.rs#advance_project_site|advance_project_site]] until completion converts it via `complete_site`; Delete in `complete_site`, [[server/spacetimedb/src/world/building.rs#cancel_project_site|cancel_project_site]], and `clear_chunks`.
- **Client readers** — `public` but unsubscribed (no wave carries `nearby_project_sites`), no visible end.
- **Server readers** — Occupancy re-checks against other sites at advance time; claim-dissolve releases the territory link.
^table-project-site
