# 11 Status Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for the mirror/report split, [[03 Connection, Subscriptions & Views]] for raw-table subscriptions, [[06 Combat Tables]] for the sink both consult families plug into (read that doc's policy section first — `DamageRequest` / `IncomingDamageOptions` are the vocabulary used below). Ability items that create these rows are catalogued in `08 Item Tables`; the slots holding them in `10 Player Tables`.

## The 30-second version

Status effects split by visibility needs: plain pre-baked stat buffs stay on `ActiveConsumableEffect` (see `10 Player Tables` — no UI mirror), while anything with runtime state (pools, counters, marks) or non-stat effects (reduction %, invuln, floors) lives on `ActiveStatusEffect` — one row per (profile, kind), the two mark kinds sharing one slot — plus the P2 enemy-side debuffs (`ActiveEnemyStatusEffect`, one row per (enemy, kind)), the P3 ground zones (`GroundZone`, live membership reticked), and the P4 held charge (`AbilityCharge`, one per player). Every targeting variant (self, allies-in-radius, zone) funnels through `apply_status_effect`; every incoming hit funnels through `intercept_incoming` before mitigation and the outgoing consults after; **one 1 Hz tick** (`status::methods::tick`, hung off `tick_status_effects` — never a second schedule) drains pools, ticks Regen/DoT, evaluates purges and wagers, auto-releases charges, decays stagger, and runs the zone pass.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do buffs/debuffs get applied? | `ActiveStatusEffect` | [[#ActiveStatusEffect]] — `apply_status_effect`, one row per (profile, kind), marks share a slot |
| What happens to my incoming hits? | `ActiveStatusEffect` | [[#ActiveStatusEffect]] — intercept order: Invulnerable → Shield → Redirect → Share → Reduction → Bleed |
| How do bleed/store marks settle? | `ActiveStatusEffect` | [[#ActiveStatusEffect]] — exponential drain, purges, wager payouts |
| What do the circles on the ground do? | `GroundZone` | [[#GroundZone]] — live membership, refresh-or-shed, bursts at expiry |
| How does hold-to-charge work? | `AbilityCharge` | [[#AbilityCharge]] — begin/release, timeout auto-release, no-cooldown vanish |
| What are the colored chips on enemies? | `ActiveEnemyStatusEffect` | [[#ActiveEnemyStatusEffect]] — per-(enemy, kind) debuffs + stagger bar |
| Why is my enemy frozen / slowed / raging? | `ActiveEnemyStatusEffect` | [[#ActiveEnemyStatusEffect]] — stun behavior consults |
| Where does the 1 Hz tick run? | all | [[#Cross-table flows]] — single tick order across all four tables |

## Flowcharts

- [[flowcharts/main-status.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/status_subfolder/status_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Status_subfolder/Status_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Zones_subfolder/Zones_subfolder.canvas]]

## Table changes

```sync
![[00 Table Map#^table-ground-zone{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-active-status-effect{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-ability-charge{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 Table Map#^table-active-enemy-status-effect{seamless:true,title:false,marker:04.}]]
```

(The entries above are the complete changer/reader reference. The sections below add Shape plus the consult/tick mechanics the entries summarize.)

## GroundZone

### Shape

One live zone in [[server/spacetimedb/src/status/tables.rs#GroundZone|tables.rs]]: auto-inc `zone_id`, btree `owner_profile_id` (zones die with their caster), `ZoneKind` (11 kinds: ally auras/buffs/heals/bursts, enemy DoT/burst/slow/vulnerability, DamageShare), center + `radius`, per-kind `magnitude`, and its own `remaining` lifetime. Membership is never carried — every tick recomputes it live (`players_near_point` / `enemies_near_point` on the center).

### Changers

Placed by the `CastZone` arm at the range-clamped cursor point (radius + magnitude position-scaled); `tick` decrements `remaining` and deletes on expiry; `purge_profile_zones` kills the caster's zones on death / leave / disconnect (see entries above).

### Readers

- ***Client*** — raw `public` table, Game wave, replay ON → `ZoneRendererComponent`'s `GroundZoneBinder` (inline in [[client/Scenes/game.tscn|game.tscn]]) → one code-created `Polygon2D` circle per row colored per kind, deleted on row delete. Visible end: witch-doctor circles and the wizard's spire rings on the ground (replay shows late joiners still-live zones).
- ***Server*** — the tick's zone pass: ally zones re-apply their status with `ZONE_STATUS_REFRESH_SECONDS` (1.5 s) remaining so stepping out sheds within ~1.5 s (`DamageReductionPct` with pct = magnitude, `HpFloor`, `DebuffImmune`, `DamageDealtMult` with mult = magnitude); `HealOverTime` heals per tick; enemy zones refresh DoT/Slow/Vulnerability the same way; `BurstHeal`/`EnemyBurst` do nothing per tick and fire once at expiry (burst damage is `NORMAL` mitigation with owner credit); `DamageShare` has no per-tick effect at all — it is consulted live per hit by `intercept_incoming`, splitting across the zone's live occupants via the flagged path (shared portions never re-intercept).

## ActiveStatusEffect

### Shape

One live effect in [[server/spacetimedb/src/status/tables.rs#ActiveStatusEffect|tables.rs]]: auto-inc `effect_id`, btree `profile_id`, `kind` (bleed/store marks, Invulnerable, DamageShield, DamageReductionPct, DamageDealtMult, HpFloor, DebuffImmune, RedirectToCaster, Regen, StoreIncomingSmite/Bash, SelfSlow), one flat `StatusParams` (a field per concept, unused = zero — avoids matching twice per row per tick), and the dual-use runtime fields documented on the table (`pool`, `counter`, `progress`, `violated`, `marked_enemy_id`, `source_profile_id`, `last_event_at`).

### Changers

[[server/spacetimedb/src/status/methods.rs#apply_status_effect|apply_status_effect]] is the one shared application path: one row per (profile, kind) — re-applying refreshes params/remaining — except the two mark kinds, which share ONE slot per player, so casting any mark replaces it (bleed pools dump instantly through the flagged damage path — which may kill, so the applier re-checks the login row before inserting; store pools forfeit). The 1 Hz `tick` owns the lifecycle: exponential bleed drains (time-constant `pool_seconds`/3, ~95% per window, snapped below 1, through `BLEED_DRAIN_OPTIONS` so HpFloor and death cleanup still apply — never direct HP writes), Regen heals, purge evaluation (`NoDamageTaken` after `BLEED_NO_DAMAGE_SECONDS` — absorbed hits count; `StayClose` after accumulated seconds inside r1; `DealDamage` after counter damage; `OnKill` via `on_enemy_killed`), window settlements (`settle_store_mark` below; Smite bursts around the paladin at tick expiry — a tick has no cursor, so recasting releases early AT the cursor instead; Bash first-expiry freezes the pool and re-arms `violated` for `BASH_HOLD_SECONDS` awaiting the next bash), charge-timeout releases, and the expiry sweep — plus `purge_profile_statuses` on death/leave/disconnect. An expired bleed mark stops converting but keeps draining until its pool is gone.

### Readers

- ***Client*** — raw `public` table, Game wave, replay ON → `StatusBarComponent`'s binder (under `InventoryCanvas/InventoryPanel` in [[client/Scenes/local_player.tscn|local_player.tscn]], filtered to the local profile) → "Kind N.Ns" labels counting down locally between the server's 1 Hz updates. Visible end: the status strip above the hotbar.
- ***Server*** — `intercept_incoming` runs the fixed order before mitigation (Invulnerable → DamageShield → loop-guarded RedirectToCaster → DamageShare → DamageReductionPct → DamageToBleed), and **every hit — even fully absorbed — stamps `counter`/`last_event_at`**; outgoing consults run per hit after mitigation (`outgoing_damage_mult`, `withhold_outgoing` accumulating the StoreDamage pool INSTEAD of dealing, `convert_outgoing_to_bleed` turning the warrior's marked hits into an enemy DoT of damage/3 for 3 s, `note_damage_dealt` for DealDamage purge progress); `HpFloor` clamps post-mitigation in the combat sink. `settle_store_mark` pays withheld pools at 2x/1x/0x per wager (`NoHits` by hits taken with ^table-player-hit-flag corroboration downgrading false 2x claims; `Radius` by the never-leave markers stamped from `report_movement`; `Damage` by pool thresholds) via `FLAT_RELEASE` — no second mitigation — then deletes the row; marked-enemy death forfeits through `on_enemy_killed`.

## AbilityCharge

### Shape

One held charge in [[server/spacetimedb/src/status/tables.rs#AbilityCharge|tables.rs]]: `profile_id` PK (one charge per player — begin refuses while a row exists), `slot_index`, `started_at`. The charge's `SelfSlow` is a separate `ActiveStatusEffect` row removed on release.

### Changers

[[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]] runs the shared slot validation (no spend yet) plus the charge-capability gate, inserts the row, and applies `SelfSlow` (`CHARGE_SELF_SLOW_FACTOR` 0.5, duration `MAX_CHARGE_SECONDS` + 2 s safety net); [[server/spacetimedb/src/status/reducers.rs#release_ability_charge|release_ability_charge]] checks the slot matches and calls the shared `internal_release_charge` — delete row, remove `SelfSlow`, dispatch scaled by `charge_fraction` (elapsed/`MAX_CHARGE_SECONDS`, capped 1.0; range scales from `CHARGE_MIN_RANGE_FRACTION` 0.25 the same formula the client mirrors), then spend exactly like `activate_ability`. The tick auto-releases at `MAX_CHARGE_SECONDS` (3 s) targeting the player's own position (a Teleport fizzles in place but still spends cooldown); the lifecycle purge vanishes charges with NO cooldown spent (see entries above).

### Readers

- ***Client*** — raw `public` table, Game wave, replay ON → `AbilityChargeComponent`'s binder (in [[client/Scenes/local_player.tscn|local_player.tscn]], echo-only, local profile) → cosmetic charge `SelfSlow` on `PositionSyncComponent` (the server clamps for real) plus `Charging`/`ChargedSlot` for `CombatComponent`'s fire suppression (firing is client-authoritative, so suppression lives there). Called from `LocalPlayerInventoryComponent.TryBeginCharge`/`TryReleaseCharge` on key-down/up for charge-capable items. Visible end: the held slow, the held fire, the scaled release.
- ***Server*** — `report_movement` multiplies the speed clamp by the held `SelfSlow` factor; `activate_ability` rejects charge-capable items (they must route through begin/release).

## ActiveEnemyStatusEffect

### Shape

One live enemy debuff in [[server/spacetimedb/src/status/tables.rs#ActiveEnemyStatusEffect|tables.rs]]: auto-inc `effect_id`, btree `enemy_id`, `kind` (DoT / Slow / Vulnerability / ArmorBreak / Stunned), per-kind `magnitude` (damage/s, fraction, fraction, unused-always-1.0, stun factor), `remaining`, and `source_profile_id` crediting DoT kills and aggro (0 = none). Uniqueness is one row per (enemy_id, kind) — re-applying refreshes magnitude/remaining/source.

### Changers

[[server/spacetimedb/src/status/methods.rs#apply_enemy_status|apply_enemy_status]] (no-op on missing enemies; P2 curses, P3 enemy zones, stagger-stun application); the tick decrements, DoT-ticks magnitude through `DOT_TICK_OPTIONS` (mitigated min 1 — weak vs armor — and never re-converted), and sweeps expiries + orphans; `on_enemy_killed` purges rows referencing the dead enemy AND that enemy's own debuff rows (wager pools forfeit) — rows never outlive their enemy (see entries above).

### Readers

- ***Client*** — raw `public` table, Game wave, replay ON → `EnemyDebuffIndicatorComponent`'s binder (at `DebuffAttach` in [[client/Scenes/default_enemy.tscn|default_enemy.tscn]], counter-scaled, filtered per enemy instance — fine at co-op scale) → colored chips per kind (DoT green, Slow blue, Vulnerability purple, ArmorBreak orange, Stunned yellow) beside the 0–100 `stagger` bar (bashes fill it at `BASH_STUN_MAGNITUDE`, the tick decays it at `STAGGER_DECAY_PER_SECOND`, full = Stunned + reset).
- ***Server*** — the combat/behavior consults: `enemy_defense` (ArmorBreak zeroes flat defence), `enemy_vulnerability_mult` (×(1+magnitude)), `enemy_movement_mult` (Slow scales displacement; Stunned counts as Slow for Slowed templates, speed-up for Enraged, nothing for Pause — Pause freezes wholesale in the behavior tick via `stunned_magnitude`, checked before aggro recompute), and `effective_interval` dividing Enraged fire pacing (convert any new wall-clock timing through it).

## Files — pointer deep dive

Entry points: [[server/spacetimedb/src/status/pointers.md|status pointers]].

### methods.rs

All shared entry points: `apply_status_effect` / `apply_enemy_status`, `ally_targets`, the incoming intercept + verdict, all outgoing consults, all enemy-side consults, `on_enemy_killed`, both lifecycle purges, and the 1 Hz `tick` (bleed drains → Regen → settlements → charge auto-release → enemy-DoT ticks → stagger decay → zone pass, in that order). Consumed by player reducers (`ally_targets`, `apply_status_effect` for zone-adjacent arms) and combat (every consult above).

### mod.rs

Module declarations only.

### reducers.rs

Only the P4 charge pair (`begin_ability_charge` / `release_ability_charge` + shared `internal_release_charge` + `remove_self_slow`) — validation/dispatch/spend all shared with `activate_ability` via `resolve_ability_slot` / `apply_ability_effect` / `spend_ability`. Called from `LocalPlayerInventoryComponent` ([[client/Scenes/local_player.tscn|local_player.tscn]]).

### tables.rs

`StatusEffectKind` / `StatusParams` (+ `PurgeKind` / `WagerKind` / `ReleaseKind`), `EnemyStatusKind`, `ZoneKind`, and the four tables above. The enums are shared vocabulary: item seeds/tables build ability params from them; combat reads the status rows; player reducers read/write statuses and zones around casts.

## Cross-table flows

The single 1 Hz tick is the module's spanning mechanism and runs in fixed order inside `tick_status_effects`: consumable expiry (player) → hit-flag/attestation/loot sweeps (player/enemy/anticheat) → `status::methods::tick` (status rows → charges → enemy debuffs → stagger → zones). Death/leave/disconnect run the triple purge (statuses, zones, charge-without-spend) at three call sites (`teardown_profile` via `kill_player`, `leave_world`, `client_disconnected` + the already-in-world recovery). Each link lives on its own entry above.

## Known gaps / stubs

- **`StatusEffectKind::DebuffImmune` has no consult point.** The table comment marks it TODO: nothing applies debuffs TO players yet (enemy debuffs land on enemies only), so the witch-doctor `DebuffImmunity` zone applies a kind nothing consults. Implement the guard in the damage/status intercept when an enemy→player debuff path is added. Design source is the Items game-design doc ("Warding Circle").

## Where to go next

[[12 World Tables]] covers the ground zones stand on. For the slots and casts that create these rows, read `10 Player Tables` and `08 Item Tables`.
