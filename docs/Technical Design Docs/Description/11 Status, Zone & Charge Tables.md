# 11 Status, Zone & Charge Tables

## Assumed knowledge

- [[02 The Component Framework]] — the client's entity/component model and the `TableBinderComponent` pattern every read path below uses.
- [[03 Connection, Subscriptions & Views]] — reducers, views, the `GameTables` subscription wave, and why raw `public` tables arrive unfiltered (the four tables here are all raw subscriptions, not views).
- [[09 Combat & Event Tables]] — the damage pipeline (`deal_damage_to_player_opts` / `apply_damage_to_enemy_opts`) that this module's consult points hook into.
- [[10 Items, Inventory & Loot Tables]] — ability items and the `AbilityEffect` catalog; this doc covers only the status/zone/charge arms of the ability dispatch.

## The 30-second version

The `status/` module owns four tables, all raw `public` tables subscribed in the `GameTables` wave: **ActiveStatusEffect** (one row per (player, status kind) — carried buffs/marks with runtime state), **ActiveEnemyStatusEffect** (one row per (enemy, debuff kind) — DoT/Slow/Vulnerability/ArmorBreak/Stunned), **GroundZone** (caster-owned ground circles ticked with live membership), and **AbilityCharge** (one held hold-to-charge per player). Writes come from three directions: the status arms of the ability dispatch ([[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]]), the two charge reducers ([[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]] / `release_ability_charge`), and the single 1 Hz scheduled tick ([[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] → [[server/spacetimedb/src/status/methods.rs#tick|status::methods::tick]]). The client renders them through four binder-fed components: StatusBarComponent, EnemyDebuffIndicatorComponent, ZoneRendererComponent, and AbilityChargeComponent.

## Flowcharts

- [[flowcharts/main-status.canvas]] — this system's composed flow.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Status_subfolder/Status_subfolder.canvas]] — the two client status-UI components.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Zones_subfolder/Zones_subfolder.canvas]] — the zone renderer.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/status_subfolder/status_subfolder.canvas]] — the server status module (tables, methods, reducers).

## Table changes

```sync
![[00 Table Map#^table-active-status-effect{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-active-enemy-status-effect{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-ground-zone{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 Table Map#^table-ability-charge{seamless:true,title:false,marker:04.}]]
```

## Main body

### Why this module exists, and the one-tick rule

Statuses are everything about a player or enemy that is *not* a plain stat buff: effects with runtime state (bleed pools, hit counters, wager progress) or with non-stat behavior (invulnerability, damage redirection, an HP floor) that the damage pipeline must consult mid-hit. Plain pre-baked stat buffs stay on `ActiveConsumableEffect` (doc 10) — the split is documented at the top of [[server/spacetimedb/src/status/tables.rs#ActiveStatusEffect|status/tables.rs]] and exists so that `recompute_stats` never has to know about pools and counters, and the damage pipeline never has to know about stat accumulation.

There is exactly **one** 1 Hz schedule in the whole server: `init` arms a `ConsumableEffectSchedule` job row at `CONSUMABLE_EFFECT_TICK_SECONDS` (1.0 s) in [[server/spacetimedb/src/main/lifecycle.rs#init|init]], and every second the runtime calls [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]], which runs its own housekeeping (consumable expiry, hit-flag sweep, loot-drop expiry) and then delegates to [[server/spacetimedb/src/status/methods.rs#tick|status::methods::tick]] for everything in this doc. The rule, stated in server/AGENTS.md: hang any new per-second status work off `tick`, never add a second schedule — one clock means bleed drains, zone refreshes, and charge timeouts can't drift relative to each other.

### ActiveStatusEffect: shape and invariants

One row per (profile, kind) — re-applying a status refreshes it rather than stacking — with one exception: the two *mark* kinds (`DamageToBleed`, `StoreDamage`) share a single slot, so a player carries at most one mark and recasting any mark replaces the old one. Both rules are enforced inside [[server/spacetimedb/src/status/methods.rs#apply_status_effect|apply_status_effect]], the single application path every targeting variant (self-cast, allies-in-radius via [[server/spacetimedb/src/status/methods.rs#ally_targets|ally_targets]], zone refresh) funnels through — because there is one funnel, the replace semantics can't be bypassed by a new ability. Replacing a bleed mark dumps its pool instantly as damage through the flagged `BLEED_DRAIN_OPTIONS` path (no free shed); replacing a store mark forfeits the pool.

Cast-time values ride on the row as a flat `StatusParams` struct — one field per concept, unused fields zero — rather than a per-kind enum payload, because the 1 Hz tick would otherwise have to match on every row's payload *and* its kind. Field dual-uses by kind (e.g. `violated` is the radius wager's "left r2" flag *and* the StoreIncomingBash armed flag) are documented on the table definition.

### The damage-pipeline consult points

The status module never writes hp itself; it *consults* at fixed seams inside `combat/mod.rs`, which is why doc 09's pipeline is a prerequisite.

**Incoming (player is hit):** [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]] calls [[server/spacetimedb/src/status/methods.rs#intercept_incoming|intercept_incoming]] before mitigation unless `opts.skip_conversion`. The intercept runs in a fixed order — Invulnerable → DamageShield (consumes itself) → RedirectToCaster (re-routes the raw hit to the source paladin, with `skip_redirect` as the loop guard) → DamageShare (live zone membership, below) → DamageReductionPct → DamageToBleed (only hits from the marked enemy split `conversion`% into the pool) — and *every* hit, even a fully absorbed one, stamps `counter`/`last_event_at` on the player's window rows, because the NoHits wager defines "a hit" as "any hit, absorbed or not". StoreIncoming rows accumulate the *post-mitigation* amount via [[server/spacetimedb/src/combat/mod.rs#compute_incoming_damage|compute_incoming_damage]] — the hit still lands normally; the store is a copy, not a conversion, so 1 HP taken can never read as 10 stored. After mitigation, the HpFloor clamp (`respect_floor`) refuses to drop the player below 1 HP while an `HpFloor` row exists.

**Outgoing (player hits an enemy):** [[server/spacetimedb/src/combat/mod.rs#apply_damage_to_enemy_opts|apply_damage_to_enemy_opts]] runs three attacker-side consults in sequence: [[server/spacetimedb/src/status/methods.rs#outgoing_damage_mult|outgoing_damage_mult]] scales the pre-mitigation amount (so defence still eats proportionally), then [[server/spacetimedb/src/status/methods.rs#withhold_outgoing|withhold_outgoing]] diverts the final damage into a StoreDamage pool *instead of* landing it (the caller skips the hp subtraction), then [[server/spacetimedb/src/status/methods.rs#convert_outgoing_to_bleed|convert_outgoing_to_bleed]] converts a hit on the marked enemy into an enemy DoT of damage/3 for 3 s. If neither fires, [[server/spacetimedb/src/status/methods.rs#note_damage_dealt|note_damage_dealt]] records the damage toward the DealDamage purge. `DOT_TICK_OPTIONS` skips both outgoing consults for DoT ticks — otherwise a bleed DoT would re-convert its own ticks forever, and a caster's own DoT would be withheld into their own store pool.

**Movement-stamped wager progress:** the radius wager's `progress`/`violated` flags are stamped from [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]], not sampled on the 1 Hz tick — darting out of r2 and back between ticks must not cheat "never leave r2". The same reducer applies the charge SelfSlow speed clamp (below).

### The 1 Hz tick, kind by kind

[[server/spacetimedb/src/status/methods.rs#tick|tick]] walks every `ActiveStatusEffect` row; a row whose profile is no longer logged in is deleted on sight (defense in depth behind the lifecycle purges). Per kind:

- **DamageToBleed** — the pool drains exponentially (time-constant `pool_seconds/3`, so ≈95% gone after `pool_seconds`, snapped to zero below 1) through `deal_damage_to_player_opts` with `BLEED_DRAIN_OPTIONS` — never a direct hp write, so death cleanup and the HpFloor still apply. Then the purge condition is evaluated: `NoDamageTaken` (2 s since `last_event_at`), `StayClose` (5 accumulated seconds within `BLEED_STAY_CLOSE_RADIUS`), `DealDamage` (counter ≥ `x`), or `OnKill` (handled by `on_enemy_killed`, not the tick). An expired mark stops converting but keeps draining until the pool is gone.
- **Regen** heals `per_second` through `combat::heal_player`. **StoreDamage** settles at window expiry via `settle_store_mark`: the wager multiplier (2×/1×/0×) is read off `counter`/`progress`/`violated`/`pool`, and the payout hits the marked enemy via `MitigationMode::FlatRelease` — no second defence subtraction, no Vulnerability double-dip, because the damage was already mitigated when it was first withheld. The NoHits 2× payout is corroborated against independent `PlayerHitFlag` witnesses (`NO_HIT_WITNESS_THRESHOLD` = 2); solo play has no witnesses, which the code accepts as a residual exploit.
- **StoreIncomingSmite** bursts its pool around the paladin at expiry (a scheduled tick has no cursor — recasting during the window releases early *at* the cursor instead, in the `StoreIncoming` arm of `apply_ability_effect`). **StoreIncomingBash** at first expiry freezes the pool and re-arms for `BASH_HOLD_SECONDS` (`violated` doubles as the armed flag); the next Bash merges the pool pre-mitigation into every enemy hit via `deal_damage_to_enemy_bonus` and consumes the row.

The same tick then handles charge timeouts and enemy statuses (below), decays every enemy's stagger bar by `STAGGER_DECAY_PER_SECOND` (20/s), and runs the zone pass.

### ActiveEnemyStatusEffect and the stagger bar

Enemy debuffs are simpler than player statuses because they carry no pools: one row per (enemy, kind), re-application refreshes magnitude/remaining/source inside [[server/spacetimedb/src/status/methods.rs#apply_enemy_status|apply_enemy_status]]. Their consult points are spread across two modules by design: the combat sink consults [[server/spacetimedb/src/status/methods.rs#enemy_defense|enemy_defense]] (ArmorBreak zeroes flat defence) and [[server/spacetimedb/src/status/methods.rs#enemy_vulnerability_mult|enemy_vulnerability_mult]] (final damage ×(1+magnitude)) under `MitigationMode::Normal`; the 100 ms behavior tick consults [[server/spacetimedb/src/status/methods.rs#enemy_movement_mult|enemy_movement_mult]] in `apply_movement` (Slow scales displacement; Stunned acts as Slow for Slowed templates, speeds Enraged ones up, and Pause templates are frozen wholesale before movement is ever reached) and [[server/spacetimedb/src/status/methods.rs#stunned_magnitude|stunned_magnitude]] in `effective_interval` (an Enraged stun divides every fire-pacing interval). Splitting the consults this way keeps `combat/mod.rs` free of behavior logic and `enemy/methods.rs` free of damage math. DoT ticks deal `magnitude` per second through `apply_damage_to_enemy_opts` with `DOT_TICK_OPTIONS`, crediting XP to `source_profile_id` and aggro to that player only while still logged in — a scheduled reducer has no meaningful sender. [[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]] purges every row referencing a dead enemy from every kill/despawn path (so rows never outlive their enemy), and the tick's orphan sweep catches anything that slipped through, because `on_enemy_killed` can't be the only cleanup.

The stagger bar is not a status row at all — it's the `stagger` field on the `Enemy` row itself (doc 08). Bashes add `p.stagger`; at 100 the bar resets and a Stunned row with `BASH_STUN_MAGNITUDE` (0.5) is applied per the template's `stun_behavior`, optionally plus ArmorBreak; the 1 Hz tick bleeds the bar off. The debuff chips and the bar the player sees are the same data, read client-side (below).

### GroundZone: live membership, no carried state

A `CastZone` ability arm inserts one row at the range-clamped cursor point, owned by the caster, with `remaining` = the ability's duration. The defining design choice: **membership is computed live every tick** via `players_near_point` / `enemies_near_point` on the zone center — there is no occupant list on the row, so there is nothing to desync when a player teleports out mid-tick. Ally zones (DebuffImmunity, DamageReduction, HpFloor, DamageBuff) re-apply their status each tick with `ZONE_STATUS_REFRESH_SECONDS` (1.5 s) of duration, so stepping out sheds the effect within ~1.5 s through the ordinary expiry path — the zone never needs a removal consult of its own. Enemy zones (EnemyDot, EnemySlow, EnemyVulnerability) refresh their debuffs the same way through `apply_enemy_status`. HealOverTime heals occupants directly; the burst kinds (BurstHeal, EnemyBurst) do nothing per tick and fire once at expiry; DamageShare has no per-tick effect at all because it is consulted live by `intercept_incoming`'s `share_zone_damage` — if the victim stands inside a share zone, the hit splits across the zone's *current* occupants, each other occupant's share going through the flagged `skip_conversion` path so a shared hit can never re-trigger shields/redirect/share. With overlapping share zones the first one found wins; they never stack.

Zones die three ways: the tick's expiry sweep (bursts fire, then the row is deleted), or [[server/spacetimedb/src/status/methods.rs#purge_profile_zones|purge_profile_zones]] — called from the same three lifecycle sites as the status purge (`teardown_profile`, `leave_world`, `client_disconnected`, plus the already-in-world recovery path in `client_connected`) so a caster's zones never outlive them or keep buffing allies of a logged-out player.

### AbilityCharge: hold-to-charge as a table row

Charge-capable abilities — today exactly the `charge_scaled` Teleport and Spell variants, decided in [[server/spacetimedb/src/player/methods.rs#ability_is_charge_capable|ability_is_charge_capable]] and mirrored client-side by `LocalPlayerInventoryComponent.IsChargeCapable` — are refused by `activate_ability` outright ("Hold the ability key to charge…"), so the only way to cast them is the charge pair. Key-down routes through `LocalPlayerInventoryComponent.TryBeginCharge` (same client-side cooldown/charge pre-checks as an instant cast) to [[server/spacetimedb/src/status/reducers.rs#begin_ability_charge|begin_ability_charge]], which re-runs the *shared* server validation ([[server/spacetimedb/src/player/reducers.rs#resolve_ability_slot|resolve_ability_slot]] — the same preamble `activate_ability` uses, so the checks can't diverge), refuses while a row exists (one charge per player — the row's primary key *is* `profile_id`), inserts the row, and applies a `SelfSlow` status with `CHARGE_SELF_SLOW_FACTOR` (0.5) and a `MAX_CHARGE_SECONDS + 2` safety-net duration. The slow is enforced server-side: `report_movement` tightens the speed cap to `base_speed ×` the minimum `SelfSlow` factor while any such row lives, which is what makes the client-side slow cosmetic rather than authoritative.

Key-up routes through `TryReleaseCharge` (which first checks the echoed row says we're charging *this* slot) to `release_ability_charge` → [[server/spacetimedb/src/status/reducers.rs#internal_release_charge|internal_release_charge]] — the shared path also called by the tick's timeout auto-release at `MAX_CHARGE_SECONDS` (3 s, targeting the player's own position, the only cursor-less choice; a Teleport fizzles in place but still spends its cooldown). Release deletes the row, removes the SelfSlow, dispatches the effect through the same `apply_ability_effect` an instant cast uses — with `charge_fraction` scaling the charge-range multiplier (`CHARGE_MIN_RANGE_FRACTION` 0.25 at zero charge up to 1.0 at full, via [[server/spacetimedb/src/player/reducers.rs#charge_range_multiplier|charge_range_multiplier]]) — and only *then* spends cooldown/charges via the shared `spend_ability`. Nothing is spent at begin: a charge that vanishes to death/logout/disconnect ([[server/spacetimedb/src/status/methods.rs#purge_profile_statuses|purge_profile_statuses]] deletes the row with every other status) costs no cooldown.

The table is `public` and *never updated* — begin inserts, release deletes, that's the entire lifecycle — which is exactly why the client can treat the echoed row as the whole state machine (below).

### The client: four binders, four renderers

All four tables are raw entries in the `GameTables` wave ([[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber]]) — raw, not views, because each consumer filters cheaply in code and the tables are small at co-op scale. All four binders keep `ReplayExistingRows` on: these are persistent tables, so a late joiner (or a scene re-entry) must see still-live statuses/zones/charges — the opposite of the event-table rule doc 03 explains.

- **StatusBarComponent** ([[client/Scripts/Components/Status/StatusBarComponent.cs|StatusBarComponent.cs]]) — a `ControlComponent` strip above the hotbar, declared under `InventoryCanvas/InventoryPanel` in `local_player.tscn` with its child `ActiveStatusEffectBinder` (RowInserted/RowUpdated → `OnStatusRow`, RowDeleted → `OnStatusRowDeleted`, all wired in `local_player.tscn`). The raw table carries every player's rows, so handlers filter to the local profile id (read off the `LocalPlayer` view). One "Kind N.Ns" label per kind; labels count down *locally* between the server's 1 Hz row updates, with row insert/update/delete as the source of truth — a locally-expired label removal is cosmetic only.
- **EnemyDebuffIndicatorComponent** ([[client/Scripts/Components/Status/EnemyDebuffIndicatorComponent.cs|EnemyDebuffIndicatorComponent.cs]]) — a `Node2DComponent` at the `DebuffAttach` point in `default_enemy.tscn` (counter-scaled against the enemy root's 0.5 scale), fed by its child `ActiveEnemyStatusEffectBinder` (same three signals wired in `default_enemy.tscn`), filtered per enemy instance in code. It `_Draw`s one colored chip per `EnemyStatusKind` plus the 0–100 stagger bar from the `Enemy.Stagger` mirror — per-instance filtering over a shared raw table is fine at co-op scale.
- **ZoneRendererComponent** ([[client/Scripts/Components/Zones/ZoneRendererComponent.cs|ZoneRendererComponent.cs]]) — declared inline in `game.tscn` with its child `GroundZoneBinder`. Row insert creates a 32-point `Polygon2D` circle colored per `ZoneKind` at `z_index = -1` (a ground decal: above terrain at −10, below entities at 0); row delete frees it — the row lifecycle *is* the visual lifecycle. RowUpdated is deliberately **not** wired: the tick rewrites `remaining` every second and nothing about the visual ever changes.
- **AbilityChargeComponent** ([[client/Scripts/Components/Inventory/AbilityChargeComponent.cs|AbilityChargeComponent.cs]]) — declared in `local_player.tscn` with its child `AbilityChargeBinder` (all three signals wired there), echo-only and filtered to the local profile. Echo-only is the point: with no optimistic local flag, timeout auto-releases, rejected begins, and lifecycle purges all converge on the right UI state by themselves. Its two observers are CombatComponent (holds fire while `Charging` — firing is client-authoritative, so charge fire-suppression must live client-side) and PositionSyncComponent (`SetExternalSlow` applies the cosmetic slow; the server clamps for real). The charge constants it needs are client mirrors in `World/ServerConstants.cs`, each naming its `main/global.rs` counterpart.

## Known gaps / stubs

- **`DebuffImmune` has no consult point.** The `DebuffImmunity` zone and its `DebuffImmune` status apply and tick correctly, but nothing reads the status: no enemy→player debuff path exists yet, so there is nothing to be immune to. The in-code TODO on [[server/spacetimedb/src/status/tables.rs#StatusEffectKind|StatusEffectKind]] says to implement the guard in the damage/status intercept when such a path is added. Until then the teal zone circle is cosmetic.

## Where to go next

The consult seams this module hooks are documented end-to-end in [[09 Combat & Event Tables]] (the damage pipeline and event tables). The ability items whose effects write these rows — and the inventory slots/cooldowns the charge path spends — are in [[10 Items, Inventory & Loot Tables]].
