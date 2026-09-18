# 13 Status

## 1. Assumed knowledge

Read [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] (the per-table spine this doc transcludes from) and [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] (conventions, reading order) first. [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] explains the read path every table below shares — raw-table subscription → `GameTables` wave → one `TableBinderComponent` child per table re-exposing row events as editor-wireable signals, with `ReplayExistingRows` on for these persistent tables — so this doc never re-teaches it. [[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]] owns the damage pipeline these statuses consult (the only place damage is computed); [[docs/Technical Design Docs/Description/12 Player Tables.md|12 Player]] owns `activate_ability`/`apply_ability_effect`/`spend_ability`, the dispatch this module's effects plug into. Maintainer jump-off points (not authority — code wins): [[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]].

Gloss for newcomers: a *reducer* is a transactional server function clients call but get no return value from — clients learn results only through table subscriptions. A *view* is a named server query clients subscribe to; these five tables are *raw-table* subscriptions (no view, acceptable at co-op scale). A *scheduled tick* is a reducer the database fires on a timer row. A Godot *scene* (`.tscn`) is the declared node tree the game actually runs — binder wiring lives inline in the scenes cited below, never in standalone component scenes.

## 2. The 30-second version

The status module owns five tables: `ActiveStatusEffect` (one player status row per profile+kind, with pool/counter/progress/violated fields that dual-purpose per kind), `ActiveEnemyStatusEffect` (one debuff row per enemy+kind), `GroundZone` (placed circles with live per-tick membership), `AbilityCharge` (one held charge per player), and `ChanneledAction` (progressive actions with no client UI yet). Writes funnel through two shared entry points in [[server/spacetimedb/src/status/methods.rs#apply_status_effect|apply_status_effect]] / [[server/spacetimedb/src/status/methods.rs#apply_enemy_status|apply_enemy_status]], one fixed-order incoming-damage intercept ([[server/spacetimedb/src/status/methods.rs#intercept_incoming|intercept_incoming]]), outgoing consults called by the combat sink, and a single 1 Hz [[server/spacetimedb/src/status/methods.rs#tick|tick]] hung off `tick_status_effects` — the only 1 Hz schedule in the module. Clients mirror rows echo-only (never optimistic, except spell flashes): a status strip, zone circles, debuff chips plus the stagger bar, and charge state driving fire suppression.

## 3. Find it fast

| Question / concept | Table | Where |
|---|---|---|
| Where do I see my active buffs and marks? | `ActiveStatusEffect` | [## ActiveStatusEffect](#activestatuseffect) |
| What do the bleed / store-damage marks do? | `ActiveStatusEffect` | [## ActiveStatusEffect](#activestatuseffect) (marks) + [Charge, marks, and zones](#8-cross-table-flows) |
| How do the witch-doctor circles / wizard spire work? | `GroundZone` | [## GroundZone](#groundzone) |
| How does hold-to-charge an ability work? | `AbilityCharge` | [## AbilityCharge](#abilitycharge) |
| Why can't I move at full speed / fire while charging? | `AbilityCharge` | [## AbilityCharge](#abilitycharge) (`SelfSlow` + fire suppression) |
| What is the stagger bar, and how are enemies stunned? | `ActiveEnemyStatusEffect` | [## ActiveEnemyStatusEffect](#activeenemystatuseffect) |
| What are channeled actions, and why is there no UI? | `ChanneledAction` | [## ChanneledAction](#channeledaction) |
| What happens to my statuses / zones / charge when I die or leave? | `ActiveStatusEffect` | [## ActiveStatusEffect](#activestatuseffect) (purges) |
| Where is damage actually computed? | — (other doc) | [[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]] — statuses only consult |
| How do ability casts dispatch into these effects? | — (other doc) | [[docs/Technical Design Docs/Description/12 Player Tables.md|12 Player]] (`apply_ability_effect`) |

## 4. Flowcharts

- [[flowcharts/main-status.canvas]] — the composed status flow (composes in the phase-2 pass; the `status` flow exists in `flowcharts/flows.json` with members).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/status_subfolder/status_subfolder.canvas]] — server-side status logic deep dive.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Status_subfolder/Status_subfolder.canvas]] — the status-strip / debuff-chip client read paths.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Zones_subfolder/Zones_subfolder.canvas]] — the zone-rendering read path.

## 6. Tables

### GroundZone

```sync
![[00 Table Map#^table-ground-zone{seamless:true,title:false,marker:01.}]]
```

#### Shape

One placed ground circle: `zone_id` key, `owner_profile_id` btree index (zones die with their caster), `kind` (`ZoneKind`, 11 variants), center x/y, `radius`, `magnitude` (meaning is per-kind — heal-per-tick, DoT-per-second, reduction fraction, burst damage — see the `ZoneKind` docs in [[server/spacetimedb/src/status/tables.rs#ZoneKind|tables.rs]]), and `remaining` (the zone's own lifetime). There is deliberately no occupant list — membership is recomputed live every tick via `players_near_point` / `enemies_near_point`, so stepping out sheds the effect within ~1.5 s.

#### Changers

Insert happens only in the `CastZone` arm of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]]: the cursor target is clamped to `MAX_ABILITY_TARGET_RANGE` around the server position row (the same clamp math as the bullet-control arms), the row is stamped with the caster as owner and `remaining = ability.duration`. The 1 Hz [[server/spacetimedb/src/status/methods.rs#tick|tick]] owns every update: it decrements `remaining`, re-applies ally statuses / enemy debuffs to whoever is inside *right now* with `ZONE_STATUS_REFRESH_SECONDS` (1.5 s) remaining, heals `HealOverTime` occupants directly, and on expiry fires bursts exactly once (`BurstHeal` heals occupants; `EnemyBurst` deals `NORMAL`-mitigated damage with xp credit to the owner) before deleting the row. `DamageShare` zones get no per-tick effect at all — they are consulted live by the intercept. Deletes are expiry in `tick` plus [[server/spacetimedb/src/status/methods.rs#purge_profile_zones|purge_profile_zones]] (death via `teardown_profile`, `leave_world`, `client_disconnected` — no zone outlives its owner).

#### Readers

- ***Client*** — raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `GroundZoneBinder` on [[client/Scripts/Components/Zones/ZoneRendererComponent.cs#ZoneRendererComponent|ZoneRendererComponent]] (replay on, wired in `game.tscn`) → one code-created `Polygon2D` circle per row, colored per `ZoneKind` (bursts and `DamageShare` visually distinct), `ZIndex -1` as a ground decal above terrain and below entities; row delete frees the circle, so the row lifecycle *is* the visual lifecycle. `RowUpdated` is deliberately not wired — the tick rewrites `remaining` every second and nothing visual ever changes.
- ***Server*** — [[server/spacetimedb/src/status/methods.rs#intercept_incoming|intercept_incoming]] calls `share_zone_damage` between redirect and reduction: if the victim stands in a `DamageShare` zone, the hit splits across the zone's live occupants (victim keeps `damage / occupants`, each other occupant takes the same share through the flagged `SHARED_PORTION_OPTIONS` path whose `skip_conversion` is the loop guard against re-intercepting); with overlapping share zones the first found wins, never stacked.

### ActiveStatusEffect

```sync
![[00 Table Map#^table-active-status-effect{seamless:true,title:false,marker:02.}]]
```

#### Shape

One active player status row: `effect_id` key, `profile_id` btree index, `kind` (13 `StatusEffectKind` variants), flat [[server/spacetimedb/src/status/tables.rs#StatusParams|StatusParams]] (one field per concept, unused stays zero — a per-kind enum payload would make the tick match twice), `remaining`, `marked_enemy_id` btree index (0 = none), `source_profile_id`, `last_event_at`. The next four columns dual-purpose by kind per the table docs: `pool` (bleed pool / withheld store pool), `counter` (hits taken in a `NoHits` window / damage dealt toward a `DealDamage` purge), `progress` (seconds inside r1 for `StayClose` / the r1-exit flag for the `Radius` wager), `violated` (r2 ever exited / the `StoreIncomingBash` armed flag). Uniqueness is one row per (profile, kind) — except the two mark kinds (`DamageToBleed`/`StoreDamage`), which share a single mark slot per player, so recasting any mark replaces it: a bleed mark dumps its remaining pool instantly through the flagged `BLEED_DRAIN_OPTIONS` path (which may kill, so `apply_status_effect` re-checks the login row before proceeding), a store mark forfeits its pool.

#### Changers

Every targeting variant (self-cast, allies-in-radius via [[server/spacetimedb/src/status/methods.rs#ally_targets|ally_targets]] — radius 0 means self-only, otherwise every logged-in player in radius including the caster — and zone refresh) funnels into [[server/spacetimedb/src/status/methods.rs#apply_status_effect|apply_status_effect]], which enforces the uniqueness/replacement rule above and inserts a zeroed-state row. Updates come from four sites: [[server/spacetimedb/src/status/methods.rs#intercept_incoming|intercept_incoming]] (stamps `counter`/`last_event_at` on *every* hit even when fully absorbed, consumes `DamageShield`, routes bleed conversion into `pool`, accumulates post-mitigation damage into `StoreIncoming*` pools via `compute_incoming_damage`); [[server/spacetimedb/src/status/methods.rs#note_damage_dealt|note_damage_dealt]] (accumulates the `DealDamage` purge counter and sheds the mark at threshold); [[server/spacetimedb/src/status/methods.rs#withhold_outgoing|withhold_outgoing]] (accumulates withheld damage into the `StoreDamage` pool *instead of dealing it*); and `report_movement` (stamps the `Radius`-wager exits per report — `violated` past r2, `progress = 1.0` past r1 — at report cadence rather than 1 Hz so darting out and back between ticks can't cheat it). The 1 Hz [[server/spacetimedb/src/status/methods.rs#tick|tick]] drains bleed pools exponentially (time-constant `pool_seconds / 3`, snapped below 1.0, through the flagged damage path so `HpFloor` and death cleanup still apply — never direct HP writes), applies `Regen` via `heal_player`, evaluates the three tick-side purges (`NoDamageTaken` after `BLEED_NO_DAMAGE_SECONDS`, `StayClose` accumulation, `DealDamage`; `OnKill` is handled by kill instead), settles `StoreDamage` windows via `settle_store_mark` (multiplier per `WagerKind`, released `FLAT` so defence isn't subtracted twice), runs the `StoreIncomingSmite` burst / `StoreIncomingBash` freeze-and-re-arm (`violated` doubles as the armed flag, `BASH_HOLD_SECONDS` second window) state machines, and sweeps expired rows — except an expired bleed mark keeps draining until its pool is gone. Mass deletes: [[server/spacetimedb/src/status/methods.rs#purge_profile_statuses|purge_profile_statuses]] (death/`leave_world`/disconnect — no status may tick a logged-out player; the held charge vanishes too with no cooldown spent) and [[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]] (every row referencing the dead enemy, so `OnKill` purges and store wagers forfeit; the enemy's own debuff rows die in the same call).

#### Readers

- ***Client*** — raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `ActiveStatusEffectBinder` on [[client/Scripts/Components/Status/StatusBarComponent.cs#StatusBarComponent|StatusBarComponent]] (replay on, wired in `local_player.tscn`, filtered to the local profile in code because the raw table carries everyone) → the "Kind N.Ns" label strip above the hotbar, counting down locally between the server's 1 Hz row updates with the row insert/update/delete as source of truth.
- ***Server*** — the fixed-order intercept (`Invulnerable` → shield → `RedirectToCaster` with its `skip_redirect` loop guard → zone share → `DamageReductionPct` → `DamageToBleed` conversion, marked-enemy hits only) runs inside `deal_damage_to_player_opts` before mitigation unless `skip_conversion`; the outgoing consults (`outgoing_damage_mult` scaling pre-mitigation damage, `withhold_outgoing`, `convert_outgoing_to_bleed` turning a marked hit into an enemy DoT, `note_damage_dealt`) run in `apply_damage_to_enemy`; the `HpFloor` clamp applies post-mitigation when `respect_floor`; `report_movement` reads `SelfSlow` rows for the real speed clamp (`base_speed × minimum slow_factor`); `PlayerSurvival` is restamped on every intercepted hit for the regen/attrition pass.

### AbilityCharge

```sync
![[00 Table Map#^table-ability-charge{seamless:true,title:false,marker:03.}]]
```

#### Shape

One held charge per player: `profile_id` primary key (so a second begin while a row exists is refused by lookup), `slot_index`, `started_at`. There is no `remaining` column — the timeout is computed from the timestamp (`seconds_since`), which is why the row is never updated.

#### Changers

[[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]] validates exactly like `activate_ability` (same [[server/spacetimedb/src/player/reducers.rs#resolve_ability_slot|resolve_ability_slot]] slot/cooldown/charges check, plus the `ability_is_charge_capable` gate) but spends nothing; it inserts the row and applies a `SelfSlow` status (`CHARGE_SELF_SLOW_FACTOR` 0.5, duration `MAX_CHARGE_SECONDS + 2.0` as tick-drift safety). [[server/spacetimedb/src/status/reducers.rs#release_ability_charge|release_ability_charge]] checks the slot matches, then [[server/spacetimedb/src/status/reducers.rs#internal_release_charge|internal_release_charge]] — the shared release path — deletes the row, removes the `SelfSlow` rows, computes `charge_fraction = min(seconds / MAX_CHARGE_SECONDS, 1)`, dispatches via `apply_ability_effect` scaled by `charge_range_multiplier` (25% at a 0-charge release up to 100% at full charge), and *then* spends cooldown/charges exactly like `activate_ability`. The tick auto-releases at `MAX_CHARGE_SECONDS` (3 s) targeting the player's own position — a Teleport fizzles in place but still spends — and the lifecycle purge deletes the row with no cooldown spent. Never updated.

#### Readers

- ***Client*** — raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `AbilityChargeBinder` on [[client/Scripts/Components/Inventory/AbilityChargeComponent.cs#AbilityChargeComponent|AbilityChargeComponent]] (replay on, wired in `local_player.tscn`, filtered to the local profile; echo-only with no optimistic flag, so rejected begins, timeout auto-releases, and lifecycle purges all converge by themselves) → the `Charging`/`ChargedSlot` state driving the *cosmetic* `SelfSlow` on `PositionSyncComponent` (`SetExternalSlow`) and [[client/Scripts/Components/Weapon/CombatComponent.cs#CombatComponent|CombatComponent]]'s fire suppression (holds fire while `Charging`, because firing is client-authoritative). Charge constants are mirrored in `ServerConstants.cs` — both sides must change together.
- ***Server*** — `report_movement` enforces the real clamp (`base_speed × minimum SelfSlow factor` feeds both the speed cap and the displacement budget); release scales every effect's radius/magnitude/damage through the `multiplier` threading in `apply_ability_effect` (spells additionally gate on `charge_scaled`).

### ChanneledAction

```sync
![[00 Table Map#^table-channeled-action{seamless:true,title:false,marker:04.}]]
```

#### Shape

One progressive action (`Revive`/`Capture`/`ChannelCast` sharing one lock/progress/expiry shape): `action_id` key, `profile_id` btree index, `target_id` with its own `by_channel_target` index for the per-target cap, target point, `progress`/`required`, `lock_expires_at`, `is_public`. Caps are max 1 active per player (`MAX_ACTIVE_CHANNELED_PER_PLAYER`) and max N per target (`MAX_CHANNELED_PER_TARGET`), enforced at begin.

#### Changers

[[server/spacetimedb/src/status/reducers.rs#begin_channeled_action|begin_channeled_action]] range-checks against the server position row (`INTERACT_RADIUS × 2` leash), enforces both caps, stamps the lock (`required + 10 s`), and applies a `SelfSlow` reusing the charge pattern. [[server/spacetimedb/src/status/reducers.rs#continue_channeled_action|continue_channeled_action]] is keep-alive only — it validates ownership and lock liveness and deletes plus un-slows on expiry, but writes no progress. Progress advances in the 1 Hz `tick_channeled` sweep (running inside the `tick_regen` pass slot): it drops expired locks, drops casters that left leash range, advances `progress` by the tick, and on completion dispatches via `complete_channeled` and deletes the row. [[server/spacetimedb/src/status/reducers.rs#cancel_channeled_action|cancel_channeled_action]] deletes plus un-slows with no dispatch and no cooldown spent (charge-cancel semantics). `purge_profile_statuses` cancels a departing player's channels the same way.

#### Readers

- ***Client*** — subscribed as a raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → but no binder consumes it anywhere outside the generated bindings, so there is no visible end (no progress bar, no prompt — see Known gaps).
- ***Server*** — no consult points; the only readers are the tick sweep and the cap checks at begin.

### ActiveEnemyStatusEffect

```sync
![[00 Table Map#^table-active-enemy-status-effect{seamless:true,title:false,marker:05.}]]
```

#### Shape

One enemy debuff row per (enemy, kind): `effect_id` key, `enemy_id` btree index, `kind` (5 `EnemyStatusKind` variants), per-kind `magnitude` (DoT damage/s, Slow fraction, Vulnerability fraction, ArmorBreak always 1.0, Stun magnitude), `remaining`, `source_profile_id` (credits DoT kills and aggro, 0 = none). Rows never outlive their enemy.

#### Changers

[[server/spacetimedb/src/status/methods.rs#apply_enemy_status|apply_enemy_status]] is the one shared path: no-op when the enemy is gone, refresh-in-place (replace magnitude/remaining/source) on re-apply, insert otherwise. Entry routes are `Curse` abilities (cursor-picked enemy), enemy zones (refreshed every tick with `ZONE_STATUS_REFRESH_SECONDS` remaining), and the `Bash` arm, which adds `p.stagger` to `Enemy.stagger` per enemy hit and at 100 applies `Stunned` (`BASH_STUN_MAGNITUDE` for `p.stun_seconds`, plus `ArmorBreak` when the bash grants it) and resets the bar. The 1 Hz `tick` deals DoT magnitudes through `apply_damage_to_enemy` with `DOT_TICK` (per-tick defence applies, min 1, and an outgoing bleed mark can't re-convert the tick into itself), credits xp to the source profile with aggro only while that player is still logged in, then decrements `remaining` and sweeps expired rows plus orphans whose enemy vanished without a kill path. [[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]] deletes every row on a killed enemy. The same tick decays `Enemy.stagger` by `STAGGER_DECAY_PER_SECOND` (20/s) — the stagger bar itself lives on the `Enemy` row, not in this table.

#### Readers

- ***Client*** — raw public table → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → `ActiveEnemyStatusEffectBinder` on [[client/Scripts/Components/Status/EnemyDebuffIndicatorComponent.cs#EnemyDebuffIndicatorComponent|EnemyDebuffIndicatorComponent]] (replay on, wired at `DebuffAttach` in `default_enemy.tscn`, filtered per enemy instance in code — fine at co-op scale) → colored kind chips (DoT green, Slow blue, Vulnerability purple, ArmorBreak orange, Stunned yellow) plus the 0–100 stagger bar, which reads `Enemy.Stagger` mirrored by [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowUpdated|Enemy.OnEnemyRowUpdated]] from the `NearbyEnemies` row rather than this table.
- ***Server*** — the combat sink consults per hit (`enemy_defense` zeroes flat defence under ArmorBreak, `enemy_vulnerability_mult` multiplies final damage — mitigated requests only, releases never double-dip); the behavior tick consults per enemy (`enemy_movement_mult` scales displacement for Slow and for Stunned-as-Slow / speeds Enraged up, `stunned_magnitude` shortens fire pacing for Enraged via `effective_interval`, and `Pause` templates freeze wholesale in `tick_enemy_behavior`).

## 7. Files — pointer deep dive

In the order `[[server/spacetimedb/src/status/pointers.md|pointers]]` lists them:

### methods.rs

The shared logic: [[server/spacetimedb/src/status/methods.rs#apply_status_effect|apply_status_effect]] (player-side application with mark replacement), [[server/spacetimedb/src/status/methods.rs#ally_targets|ally_targets]] (self-only at radius 0, else logged-in players in radius), [[server/spacetimedb/src/status/methods.rs#intercept_incoming|intercept_incoming]] plus its `share_zone_damage` helper (the fixed incoming order), the outgoing consults ([[server/spacetimedb/src/status/methods.rs#outgoing_damage_mult|outgoing_damage_mult]], [[server/spacetimedb/src/status/methods.rs#withhold_outgoing|withhold_outgoing]], [[server/spacetimedb/src/status/methods.rs#convert_outgoing_to_bleed|convert_outgoing_to_bleed]], [[server/spacetimedb/src/status/methods.rs#note_damage_dealt|note_damage_dealt]]) and `settle_store_mark` (wager settlement with the `PlayerHitFlag` corroboration for `NoHits`), the enemy-side apply/consults ([[server/spacetimedb/src/status/methods.rs#apply_enemy_status|apply_enemy_status]], [[server/spacetimedb/src/status/methods.rs#enemy_defense|enemy_defense]], [[server/spacetimedb/src/status/methods.rs#enemy_vulnerability_mult|enemy_vulnerability_mult]], [[server/spacetimedb/src/status/methods.rs#enemy_movement_mult|enemy_movement_mult]], [[server/spacetimedb/src/status/methods.rs#stunned_magnitude|stunned_magnitude]]), lifecycle ([[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]], [[server/spacetimedb/src/status/methods.rs#purge_profile_statuses|purge_profile_statuses]], [[server/spacetimedb/src/status/methods.rs#purge_profile_zones|purge_profile_zones]]), and the tick family: [[server/spacetimedb/src/status/methods.rs#tick|tick]] (status/charge/enemy-DoT/stagger/zone passes, called from `tick_status_effects`), [[server/spacetimedb/src/status/methods.rs#tick_regen|tick_regen]] (regen-with-delay + attrition through the shared sink, plus the `tick_channeled` sweep) and [[server/spacetimedb/src/status/methods.rs#tick_zones|tick_zones]] (elevation-band heal/chip) — the latter two called via the `main/agents.rs` dispatcher (`tick_regen`/`tick_zones`), not from `tick`, even though they live in this file. Call direction is inward: `player/reducers.rs`, `combat/apply.rs`, `enemy/methods.rs`, and `enemy/reducers.rs` call in; this file never calls reducers, only the sinks and queries.

### mod.rs

Three module declarations (`tables`, `methods`, `reducers`) — composition only, no logic.

### reducers.rs

The thin reducer shell over the methods: [[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]] / [[server/spacetimedb/src/status/reducers.rs#release_ability_charge|release_ability_charge]] (validate, delegate to [[server/spacetimedb/src/status/reducers.rs#internal_release_charge|internal_release_charge]], the shared path the tick's timeout auto-release also calls) plus `remove_self_slow`; and the channeled trio [[server/spacetimedb/src/status/reducers.rs#begin_channeled_action|begin_channeled_action]] / [[server/spacetimedb/src/status/reducers.rs#continue_channeled_action|continue_channeled_action]] / [[server/spacetimedb/src/status/reducers.rs#cancel_channeled_action|cancel_channeled_action]]. Called from the client: `local_player.tscn` routes through `LocalPlayerInventoryComponent` (`TryBeginCharge`/`TryReleaseCharge`); nothing calls the channeled reducers yet. Slot validation, effect dispatch, and the spend are borrowed from `player/reducers.rs` (`resolve_ability_slot`/`apply_ability_effect`/`spend_ability`) rather than reimplemented.

### tables.rs

All five table definitions plus the four enums and `StatusParams` (`StatusEffectKind`, `EnemyStatusKind`, `ZoneKind`, `PurgeKind`/`WagerKind`/`ReleaseKind`, `ChanneledActionKind`). The doc comments on `ZoneKind`, `StatusEffectKind::DebuffImmune`, and the `ActiveStatusEffect` field roles are load-bearing design notes (consult points, dual-purposing, zone refresh semantics) — read them alongside the sections above. Referenced by `item/seeds.rs` + `item/tables.rs` (kind enums for ability authoring) and read by `combat/apply.rs` (`HpFloor`) and `player/methods.rs` + `player/reducers.rs` (writes).

## 8. Cross-table flows

**Charge lifecycle.** `AbilityCharge` + `ActiveStatusEffect` (`SelfSlow`) + `PlayerInventorySlot` (cooldown/charges spent only on release): begin inserts both rows; the client suppresses fire and slows cosmetically off the echo while `report_movement` clamps for real; release deletes both, dispatches scaled, spends. Timeout and purge converge through the same delete path, which is why the client can afford to be echo-only.

**Zone tick.** `GroundZone` → `ActiveStatusEffect` / `ActiveEnemyStatusEffect`: every tick rebuilds membership live and re-applies short-lived rows, so zones never store who is inside and leaving sheds the effect within ~1.5 s; bursts bypass the refresh and fire once at expiry. `DamageShare` never enters this flow — it lives purely in the intercept.

**Bleed / store marks.** `ActiveStatusEffect` mark rows ↔ the combat sinks ↔ `Enemy` (death) ↔ `PlayerHitFlag` (witness corroboration): outgoing consults withhold or convert damage into pools/DoTs, the intercept feeds the bleed pool from incoming marked hits, the tick drains and settles, `on_enemy_killed` forfeits, and `report_movement` stamps the radius wager — five writers, one row, all through the methods in §7.

## 9. Known gaps / stubs

- `StatusEffectKind::DebuffImmune` (and therefore `ZoneKind::DebuffImmunity`) has no consult point — the TODO on [[server/spacetimedb/src/status/tables.rs#StatusEffectKind|StatusEffectKind]] says nothing applies debuffs *to* players yet, so the witch-doctor warding circle applies a row that nothing reads.
- `complete_channeled` is placeholder dispatch: `Revive` heals the target for a flat 50, while `Capture`/`ChannelCast` just heal the caster for 10 through the shared helpers — no capture or cast effect exists yet ([[server/spacetimedb/src/status/methods.rs#tick_regen|tick_regen]] region).
- `ChanneledAction` is subscribed but has no binder and no client caller — no progress bar, no prompt, and nothing invokes the begin/continue/cancel reducers outside the generated bindings.
- Smite-at-expiry centers on the paladin, not the cursor: a scheduled tick has no cursor, so recasting early (cursor release in `activate_ability`) and expiring (paladin-centered in `tick`) deliberately differ — noted as a plan deviation in the code.
- The `NoHits` 2x corroboration accepts a residual solo-play exploit (no witnesses exist solo), stated in the `settle_store_mark` comment.
- `tick_zones` lowest-elevation chip damage / top-band heal is a stand-in band rule — lava-style tiles don't exist, per the code comment.

## 10. Where to go next

Read [[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]] for the damage pipeline these consults plug into (mitigation seam, `DamageRequest` policies, death ownership), then [[docs/Technical Design Docs/Description/12 Player Tables.md|12 Player]] for the ability dispatch (`apply_ability_effect` arms, `spend_ability`) and the `tick_status_effects` dispatcher that drives the 1 Hz work.
