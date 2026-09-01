# 09 Combat & Event Tables

## Assumed knowledge

[[02 The Component Framework]] (the client's entity/component model and the binder pattern), [[03 Connection, Subscriptions & Views]] (reducers, views, the `GameTables` subscription wave, `TableBinderComponent`, and why event tables bind with replay **off**), [[05 Player Data & Progression Tables]] (the `PlayerData` hp rows combat writes), and [[08 Enemy Tables]] (the `Enemy` row, the behavior tick, and the step-def tables that resolve bullet damage).

## The 30-second version

Combat is unusual: its pipeline lives in `server/spacetimedb/src/combat/mod.rs`, which defines **no tables of its own** — it *writes* `PlayerData.hp` and `Enemy.hp`/`phase` on behalf of two self-reported hit reducers. What this system *does* own is the messaging around those writes: three append-only `event` tables (`BulletPatternEvent` — the server broadcasting every enemy shot; `BulletControlEvent` and `AbilityVisualEvent` — the server relaying player ability casts to every client) plus two server-only anti-abuse tables (`PlayerHitRate`, the token bucket rate-limiting self-reported hits; `PlayerHitFlag`, witness corroboration from other clients). The shape of every path: the **client detects** a hit or a cast, the **server validates and computes** the outcome, and the updated row (or a fresh event row) flows back into client mirrors.

## Flowcharts

- [[flowcharts/main-combat.canvas]] — the composed combat flow.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/combat_subfolder/combat_subfolder.canvas]] — `combat/mod.rs`, the whole damage pipeline.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Bullets_subfolder/Bullets_subfolder.canvas]] — the bullet components (spawner, hit router, controller, 3D mirror).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Combat_subfolder/Combat_subfolder.canvas]] — the hitbox/hurtbox components (`HitZone`, `DamageReceivingComponent`, `HealthComponent`, `RemoteHitWitnessComponent`).

## Table changes

```sync
![[00 Table Map#^table-bullet-pattern-event{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-bullet-control-event{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-ability-visual-event{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 Table Map#^table-player-hit-rate{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 Table Map#^table-player-hit-flag{seamless:true,title:false,marker:05.}]]
```

## Main body

### The trust model: client-detected, server-computed

Hit *detection* is a physics problem, and the physics lives on the client: Godot's `Area2D` overlap system and the BlastBullets2D plugin are what know a bullet touched a hurtbox. But the server is authoritative for outcomes, so the client never applies damage locally — it calls a **reducer** (a transactional server function the client invokes by name) reporting the hit, and the server computes the damage and writes the row. Both hit reducers are therefore *self-reported*, and both are gated the same way before any math runs: a 1-second token bucket (`^table-player-hit-rate`) and a wrap-aware range check (`HIT_VALIDATION_RADIUS` = 4× the camera view radius, [[server/spacetimedb/src/main/global.rs#HIT_VALIDATION_RADIUS|main/global.rs]]) that rejects attributions against entities nowhere near the caller. What the client shows for health is always a *mirror*: [[client/Scripts/Components/Combat/HealthComponent.cs#SetFromServer|HealthComponent.SetFromServer]] applies the server row's hp/max_hp and emits `HealthDidDecrease`/`HealthDidIncrease`/`HealthDidZero` — it deliberately has no `Damage()`/`Heal()` methods.

`combat/mod.rs` is the single sink both reducers delegate to, so weapon hits, spells, DoT ticks, zone damage, and store-damage releases all share one mitigation implementation — there is exactly one place where defence is subtracted and exactly one place where death cleanup runs.

### Outgoing damage: player bullet → enemy

The local player's [[client/Scripts/Components/Weapon/CombatComponent.cs|CombatComponent]] (a child of `local_player.tscn`) owns firing. Its effective weapon — fire rate, pattern, range, effective shot count — comes from `EffectiveWeaponResolver`, re-resolved only when the resolver fingerprint changes ([[client/Scripts/Components/Weapon/CombatComponent.cs#OnInventoryChanged|OnInventoryChanged]], which also listens to `GameManager.CatalogChanged` so a catalog row edit re-resolves without any inventory change). `_Process` gates on the `fire` input action, the fire-rate timer, and the P4 charge suppression (`chargeComponent?.Charging` — firing is client-authoritative, so the hold-fire rule for a held ability charge lives here).

Here's the part that surprises people: **player bullets are cosmetic.** [[client/Scripts/Components/Weapon/CombatComponent.cs#Fire|Fire]] spawns a visual bullet through `BulletManager.Instance.SpawnPlayerBullet`, and *in the same breath* calls [[client/Scripts/Components/Weapon/CombatComponent.cs#SpawnZonesAlong|SpawnZonesAlong]], which instances a chain of `HitZone`s along the bullet's path — one per `Range / ZoneCount` step, each launched with a delay equal to the bullet's travel time to that point. The bullet the player sees and the hitboxes that do the work are separate objects traveling in lockstep.

A [[client/Scripts/Components/Combat/HitZone.cs|HitZone]] ([[client/Scenes/Components/hit_zone.tscn|hit_zone.tscn]] — an `Area2D` on mask 4 with a `CircleShape2D` and a one-shot `LifetimeTimer`) is an attacker hitbox with a fuse. When the timer expires, [[client/Scripts/Components/Combat/HitZone.cs#ReportHits|ReportHits]] walks `GetOverlappingAreas()`, keeps only faction-opposed `DamageReceivingComponent`s ([[client/Scripts/Components/Combat/HitZone.cs#CanDamage|CanDamage]] → [[client/Scripts/Components/Combat/FactionComponent.cs#CheckOpposition|FactionComponent.CheckOpposition]] — opposition = no shared flag, and a missing component falls back to Neutral, which is opposed to everything), emits `DidHitReceiver` for observers, and — because the server owns damage — calls the `ReportEnemyHit` reducer with the victim's id. Then the zone frees itself.

Server side, [[server/spacetimedb/src/enemy/reducers.rs#report_enemy_hit|report_enemy_hit]] runs its two gates (the rate cap is dynamic: equipped weapon `fire_rate` × effective `shot_count` × `HIT_RATE_SLACK` = 2.0; a missing enemy or an `immortal` one is a silent no-op), then delegates to [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_enemy|deal_damage_to_enemy]] → [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_enemy_bonus|deal_damage_to_enemy_bonus]], which resolves the attacker's damage via [[server/spacetimedb/src/combat/mod.rs#compute_player_damage|compute_player_damage]]:

```
raw  = weapon.damage × (1 + strength × 0.002) / effective_shot_count
base = max(raw + flat_bullet_mod, 0)          ← pre-defence
post = true_flat + raw × true_pct             ← true damage, added after defence
```

Weapon `damage` is **per trigger pull**, divided among the pattern's bullets — that's the design lever making many-weak-bullets weapons trade against few-strong ones, because defence subtracts *per bullet*. Defence is not subtracted here; the split exists so that [[server/spacetimedb/src/combat/mod.rs#apply_damage_to_enemy_opts|apply_damage_to_enemy_opts]] stays the one mitigation site for every damage path (the shield-bash StoreIncomingBash pool rides in as the `bonus`, merged *pre*-mitigation into this same hit).

`apply_damage_to_enemy_opts` then, in order:

1. Resolves the target's flat defence from its `EnemyTemplate`, consulting the target's enemy statuses under `MitigationMode::Normal` — ArmorBreak zeroes defence, Vulnerability multiplies final damage (`MitigationMode::FlatRelease` skips both: a store-damage release was already mitigated at capture and must not double-dip).
2. Applies the attacker's `DamageDealtMult` consult pre-mitigation (`outgoing_damage_mult`), then subtracts defence with a **minimum of 1** — except that `amount == 0` stays 0, so having no weapon equipped deals nothing rather than floor-ticking.
3. Runs the attacker-side outgoing consults: StoreDamage can withhold the hit into its pool (`withhold_outgoing` — returns early), an outgoing bleed mark can convert it into an enemy DoT (`convert_outgoing_to_bleed` — returns early), otherwise `note_damage_dealt` records it for purge progress. DoT ticks call with `DOT_TICK_OPTIONS` to skip both, so a bleed can't re-convert its own tick forever.
4. Subtracts from `Enemy.hp`. On a kill: xp reward = template `max_hp / 10`, [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] tears down the whole behavior tree, `on_enemy_killed` purges its status rows, and [[server/spacetimedb/src/player/methods.rs#internal_gain_xp|internal_gain_xp]] credits `xp_credit` (scheduled ticks pass `None` — they have no attacker).
5. On survival: recomputes the phase from HP thresholds ([[server/spacetimedb/src/enemy/methods.rs#compute_phase|compute_phase]]) and re-aggros the behavior onto the attacker — a phase change forces a full `recompute_aggro`; otherwise the attacker becomes the target only if the aggro lock has expired. The hp/phase write to ^table-enemy is what the client puppet's binder mirrors back into its HealthComponent.

### Incoming damage: enemy bullet → player

The upstream half is doc 08's: the 100 ms behavior tick advances each enemy's attack sequences, and for every fired shot [[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior|tick_enemy_behavior]] appends one `BulletPatternEvent` — but only while a player is within `attack_sim_factor` of the camera view radius, so enemies never shoot from off-screen. Two fields are load-bearing downstream: the origin is stamped *absolutely* (`origin_x/y` = the enemy's world position at fire time), and `damage` is pre-resolved via [[server/spacetimedb/src/enemy/methods.rs#step_damage|step_damage]] — the same function `report_hit` uses to resolve a reported hit — so clients can compare bullet damage without subscribing the step-def tables.

On the client, the row lands in the `GameTables` wave and fires **one shared** `BulletPatternEventBinder` on the BulletSpawnerComponent (wired in [[client/Scenes/Components/Bullets/bullet_spawner_component.tscn|bullet_spawner_component.tscn]], replay off). One binder, not one per enemy, *because* the events are self-contained: [[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#OnBulletPatternRowInserted|OnBulletPatternRowInserted]] needs no Enemy puppet lookup, so no volley is lost during a puppet's deferred-spawn window. [[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#SpawnEnemyBullet|SpawnEnemyBullet]] resolves the aim angle (toward the event's `target` identity, read from the local position cache), stamps a [[client/Scripts/Game/BulletData.cs|BulletData]] (`SourceStep` + `EnemyId` + `Damage`) onto the spawner as `bullets_custom_data` — this is how a later hit report knows which step and which enemy to attribute — applies mid-flight curving/acceleration ([[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#ApplyMotion|ApplyMotion]] maps `angular_speed`/`speed_acceleration` to BlastBullets2D rotation/speed data, re-set per event because the spawner resource is shared), and dispatches per `PatternType` (Ring/Volley/Curtain/Shotgun/Explosion) into the BlastBullets2D factory through untyped `BlastBullets.Call(…)`. Pellet jitter is a SplitMix64 hash seeded by `event_id`, so every client derives identical trajectories. Enemy bullets spawn via `spawn_controllable_directional_bullets`, and the returned `DirectionalBullets2D` instances accumulate in `LiveEnemyBullets` — the factory cannot enumerate active instances, so the set is captured at spawn and pruned on a 1 s cadence plus a 256-instance threshold ([[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#PruneLiveEnemyBullets|PruneLiveEnemyBullets]]), never per event. `bullet_max_collision_count = 1` is pinned explicitly on the enemy spawn data: it is the only thing stopping one bullet from multi-reporting the same victim.

Enemy bullets live on collision layer/mask 2. When one overlaps a hurtbox, the plugin's `area_entered` signal reaches [[client/Scripts/Components/Bullets/BulletHitRouterComponent.cs#OnEnemyBulletAreaEntered|BulletHitRouterComponent.OnEnemyBulletAreaEntered]] (connected in `OnRegistered`), which branches on what was hit:

- A `DamageReceivingComponent` (the local player's hurtbox, declared in [[client/Scenes/local_player.tscn|local_player.tscn]] on layer 2) → [[client/Scripts/Components/Combat/DamageReceivingComponent.cs#ProcessBulletHit|ProcessBulletHit]] calls the `ReportHit` reducer with the stamped `SourceStep` and `EnemyId`. (The same component's other entry point, `ProcessHit`, is the HitZone path — it only emits `DidReceiveDamage`; enemy hits are reported by the *attacker's* zone, not the victim.)
- A `RemoteHitWitnessComponent` (another player's puppet) → the anti-cheat witness path, below.
- Anything else on layer 2 → `PushWarning` and ignore. The old fallback reported a self-hit for any unrecognized area — one stray layer-2 area away from phantom self-damage on every bullet flyover.

Server side, [[server/spacetimedb/src/player/reducers.rs#report_hit|report_hit]] rate-limits (`incoming` counter, flat `INCOMING_HIT_RATE_CAP` = 30/s — bullet-hell generous, it exists to bound StoreIncoming farming loops), validates the attributed enemy exists and is within `HIT_VALIDATION_RADIUS` of the caller's position row, resolves base damage with the same `step_damage`, and delegates to [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]]:

1. **Status intercept** (skipped under `skip_conversion` — the bleed pool's own drains): `intercept_incoming` runs its fixed chain — Invulnerable → DamageShield → RedirectToCaster (loop-guarded by `skip_redirect`) → DamageShare → DamageReductionPct → DamageToBleed — and can absorb the hit entirely.
2. **Mitigation**: [[server/spacetimedb/src/combat/mod.rs#compute_incoming_damage|compute_incoming_damage]] subtracts the player's resolved `PlayerData.defense`, minimum 1.
3. **Floor**: if hp would hit 0 while an `HpFloor` status row exists, hp clamps to 1 instead (`respect_floor`).
4. **Death or write**: at 0, [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] drops every profile-scoped row (including ^table-player-hit-rate), the `LoggedInPlayer` row is deleted by `player.player_id` — not `ctx.sender()`, because scheduled-reducer drains have no sender — and a `LoggedOutPlayer` row drops the player back to the lobby. Otherwise the new hp is written to ^table-player-data, and the client's `LocalPlayerDataComponent` mirror pushes it into HealthComponent.

Healing bypasses all of this: [[server/spacetimedb/src/combat/mod.rs#heal_player|heal_player]] clamps to max_hp and writes the same row.

### Why event tables, and the replay-off rule

All three `^table-*-event` tables share one shape: `#[table(public, event)]`, append-only, never updated, no explicit delete. A reducer can't return data and can't push messages — a table row *is* the broadcast channel — and the `event` flag marks the table as ephemeral broadcast rather than retained state, so history doesn't accumulate. The corollary lives on the client: their binders must keep `ReplayExistingRows = false` (the default), because there is no last-seen-id tracking — replaying cached rows on bind would re-fire the table's whole history every time the scene is entered (every volley ever fired, every wall ever cast). Persistent tables want the opposite; see the `GroundZone` binder in doc 11.

`BulletPatternEvent` is a *server→client* broadcast (the tick fires, clients render verbatim). The other two are *client→server→clients* relays, and they share a second pattern: the caster applies the effect **optimistically** and the echoed row is skipped via `cast_by` ([[client/Scripts/Components/Bullets/BulletControllerComponent.cs#OnBulletControlEventRow|BulletControllerComponent.OnBulletControlEventRow]] and [[client/Scripts/Components/Visual/SpellVisualComponent.cs#OnAbilityVisualEventRow|SpellVisualComponent.OnAbilityVisualEventRow]] both early-return on `GameManager.IsLocal(row.CastBy)`), so the caster never double-applies its own cast.

### The bullet-control protocol

There is no `control_bullets` reducer — `BulletControlEvent` **is** the protocol, because the server tracks no live bullets at all. When a bullet-manipulation ability casts (Delete/Split/Attract near a point, the rectangle variants, Slash, Wall), [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] — reached through `activate_ability` or the charge-release path with full slot/cooldown/charge validation — clamps the cursor target into `MAX_ABILITY_TARGET_RANGE` of the server's own position row and appends one event row describing the *cast*, not the bullets. Every client, caster included, then resolves the proximity query against **its own** live bullets.

The encodings are field-reuse, documented on the table ([[server/spacetimedb/src/enemy/instance_tables.rs#BulletControlEvent|definition]]): circle kinds use (x, y) + `radius`; the rect kinds use (x, y) as origin, (target_x, target_y) as far end, `radius` as width; Attract's `duration` is the black hole's linger (0 = one-shot pull); SlashRect repurposes `duration` to carry its max-bullet-damage threshold as f32 bits (the client decodes with `BitConverter.SingleToUInt32Bits`); WallRect's `duration` is the wall's linger seconds.

Client side, [[client/Scripts/Components/Bullets/BulletControllerComponent.cs|BulletControllerComponent]] (declared inline in [[client/Scenes/game.tscn|game.tscn]] with its `BulletControlEventBinder` child, `RowInserted` wired to `OnBulletControlEventRow`) implements all of them over `BulletSpawnerComponent.LiveEnemyBullets`: `FindBulletsNear`/[[client/Scripts/Components/Bullets/BulletControllerComponent.cs#FindBulletsInRect|FindBulletsInRect]] iterate enabled bullets per instance; Delete disables, Split respawns each hit as a fan via `SpawnEnemyBulletFan` (keeping the original `BulletData`, so split pellets still attribute hits to the original step), [[client/Scripts/Components/Bullets/BulletControllerComponent.cs#SlashInRect|SlashInRect]] skips bullets whose stamped `Damage` exceeds the threshold — the reason `BulletPatternEvent.damage` exists — and [[client/Scripts/Components/Bullets/BulletControllerComponent.cs#SpawnAttractZone|SpawnAttractZone]]/[[client/Scripts/Components/Bullets/BulletControllerComponent.cs#SpawnWallZone|SpawnWallZone]] register lingering zones that `_Process` re-applies every `AttractTickInterval` (0.2 s) so late-entering bullets get caught too. Every rect/zone cast also draws its black Polygon2D visual through the same code path on every client, so the visual replicates with the cast for free.

### Spell visuals

The Spell arm of `apply_ability_effect` does *both* halves: it applies damage server-side — shape targets from [[server/spacetimedb/src/player/methods.rs#spell_targets|spell_targets]], `damage × (1 + scaling_stat × 0.002) × charge_mult`, a volley landing as `hits` separate mitigated hits of `damage/hits` each through `apply_damage_to_enemy` (the enemy row re-found before every pellet so a mid-volley kill makes the rest no-ops) — and appends one `AbilityVisualEvent` so every client draws the flash. [[client/Scripts/Components/Visual/SpellVisualComponent.cs|SpellVisualComponent]] (declared inline in game.tscn with its `AbilityVisualEventBinder`, replay off) renders per `AbilityVisualKind` in [[client/Scripts/Components/Visual/SpellVisualComponent.cs#Render|Render]]: Circle = ring, Line = rectangle, Cone = wedge, Meteor/Converge add shrapnel/converging streaks that are **visual-only** — the damage is the plain circle either way. Each flash is a code-created Polygon2D container (z-index 2, above entities and bullets) faded and freed after the event's `duration` (`SPELL_VISUAL_SECONDS` = 0.5 s, mirrored client-side as `SpellVisualComponent.DefaultDuration` for the optimistic path).

### Rate limiting: the token bucket

[[server/spacetimedb/src/player/methods.rs#hit_rate_allow|hit_rate_allow]] is a fixed-window token bucket over ^table-player-hit-rate: one row per profile, reset when the window is a second old, two independent counters. The `outgoing` counter caps `report_enemy_hit` at the equipped weapon's `fire_rate × shot_count.max(1) × HIT_RATE_SLACK` (slack 2.0 absorbs burst/jitter); the `incoming` counter caps `report_hit` at a flat 30/s. Both reducers reject over-budget reports *before* any damage math — the bucket bounds spam without touching the formulas. The row is deleted by `teardown_profile` and is never seen by the client.

### The anti-cheat witness flag

`report_hit` is self-reported, so the module collects independent corroboration. Every remote player puppet carries a [[client/Scripts/Components/Combat/RemoteHitWitnessComponent.cs|RemoteHitWitnessComponent]] hurtbox (declared in [[client/Scenes/non_local_player.tscn|non_local_player.tscn]], layer 2 / mask 0 — it detects nothing itself, it exists so enemy bullets register collisions against *other* players on every client; the plugin's collision counting then despawns the bullet locally everywhere). BulletHitRouterComponent forwards those overlaps to [[client/Scripts/Components/Combat/RemoteHitWitnessComponent.cs#ProcessBulletHit|ProcessBulletHit]], which calls [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]] with the *victim's* identity — never damage. The reducer requires in-world and silently ignores self-reports (the victim reports through `report_hit`; flags are witnesses only). Rows carry no bullet identity — just who was hit, who saw it, when — and the 1 Hz [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] sweep deletes flags older than `HIT_FLAG_MAX_AGE_SECONDS` (60 s) so the table can't grow unbounded. The one consumer today is [[server/spacetimedb/src/status/methods.rs#settle_store_mark|settle_store_mark]]: a StoreDamage `NoHits` wager claiming a ×2 payout for taking zero hits gets demoted to ×1 if at least `NO_HIT_WITNESS_THRESHOLD` witnesses flagged the player inside the window.

### The BulletManager entity and the 3D mirror

All bullet components hang off one entity: [[client/Scripts/Game/BulletManager.cs|BulletManager]] (`Node2D, IEntity`, declared inline in [[client/Scenes/game.tscn|game.tscn]] — there is no `bullet_manager.tscn`; `z_index = 1` keeps bullets above the dynamically spawned entities). Its children are the `BlastBullets` `BulletFactory2D` GDExtension node (reached via an exported NodePath, and only through untyped `Call(…)` — no compile-time checking on those calls or the `.tres` spawn-data resources, so typos surface at runtime), the BulletSpawnerComponent (instanced from bullet_spawner_component.tscn), the BulletHitRouterComponent, and the BulletControllerComponent with its binder. It keeps a static `Instance` plus spawn pass-throughs because external entities (CombatComponent) have no registry path to it. Since the factory renders only into the 2D world, [[client/Scripts/Components/Bullets/EnemyBullets3DComponent.cs|EnemyBullets3DComponent]] (in `world_3d.tscn`) mirrors live enemy bullets into the 3D backdrop: on a throttled 30 Hz tick ([[client/Scripts/Components/Bullets/EnemyBullets3DComponent.cs#SyncInstance|SyncInstance]]) it reads enabled bullet transforms out of `LiveEnemyBullets` — per-index `is_bullet_status_enabled`, not the buggy bulk array — and lays a pooled, never-shrinking set of `hex_bullet.tscn` instances over them.

## Known gaps / stubs

- **Hits are self-reported; the validation is deliberately thin.** `report_hit`/`report_enemy_hit` trust the caller beyond the token bucket and the `HIT_VALIDATION_RADIUS` proximity check (both reducers say so in comments — "report_hit remains self-reported (see the trust model)"). ^table-player-hit-flag corroborates, but its only consumer is the `NoHits` wager demotion in `settle_store_mark` — there is no general cross-check that rejects an unwitnessed hit, and the code accepts the residual exploit explicitly: "Solo play has no witnesses" (`status/methods.rs`).
- **Bullet-control effects resolve per client, so bullet state diverges between clients.** The server tracks no live bullets; each client runs the proximity query against its own `LiveEnemyBullets`, and `BulletControllerComponent`'s own doc comment notes edge-of-radius results can differ slightly. There is no reconciliation pass — divergence persists until the affected bullets expire.
- **BlastBullets2D plugin bug (worked around, not fixed):** the bulk `get_all_bullets_status` array never increments its index, so only slot 0 is ever written. Every liveness check goes through the per-index `is_bullet_status_enabled` instead (`BulletSpawnerComponent.PruneLiveEnemyBullets`, `EnemyBullets3DComponent.SyncInstance`, `BulletControllerComponent` queries). Anything new that needs bullet liveness must follow the same workaround.
- **Untyped plugin interop:** every spawn/control call into the BlastBullets2D factory is an untyped `Call(…)` on GDExtension objects with `.tres` spawn-data resources — no compile-time checking, so renames/typos in those strings surface only at runtime (`BulletSpawnerComponent`, `BulletManager` doc comments).

## Where to go next

The status consults that gate and reshape every hit (intercept chain, bleed/store pools, enemy debuffs, zones, charges) are [[11 Status, Zone & Charge Tables]]. What feeds `compute_player_damage` — the weapon toggle/enchantment composition and the effective-shot-count math — is [[10 Items, Inventory & Loot Tables]], and the pattern/step-def machinery that authors what `BulletPatternEvent` carries is [[08 Enemy Tables]].
