# 08 Combat

## Assumed knowledge

[[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] (especially the `# Combat` section and the ^table-bullet-fire-attestation, ^table-player-hit-rate, ^table-player-hit-flag, ^table-player-data, and ^table-enemy entries), [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]], [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] (reducers-as-transactions, views, subscription waves, binders), [[docs/Technical Design Docs/Description/12 Player Tables.md|12 Player]] (hp/xp/death rows), [[docs/Technical Design Docs/Description/09 Enemy Tables.md|09 Enemy]] (fire path, event tables, attestations), [[docs/Technical Design Docs/Description/13 Status Tables.md|13 Status]] (the mitigation consults). Maintainer refs, not authority: [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]].

Gloss for newcomers: a **reducer** is a server function clients call by name — it runs transactionally and returns no data, so clients only ever learn outcomes through table/subscription updates. A **view** is a named server-side query (often per-caller) that clients subscribe to instead of writing SQL. A **subscription wave** (`BaseTables`/`LobbyTables`/`GameTables` in `TableSubscriber`) is the set of tables/views subscribed at each game stage. A **binder** (`TableBinderComponent`) is a scene node that re-exposes one subscribed table's row events as editor-wireable signals. Client logic lives in **components** (node name = class name = file name, declared in `.tscn` scenes).

## The 30-second version

Combat owns **no tables** — it is the shared damage pipeline every damage path funnels through: pure math in `calc.rs` (per-bullet division, flat defence, true-damage-after-defence), policy types in `mod.rs` (`DamageRequest` for enemies, `IncomingDamageOptions` for players), and the sinks in `apply.rs` that mitigate, subtract hp, and split death ownership (player killing blows delegate to `player::methods::kill_player`; enemy kills despawn through `despawn_enemy_archetype` plus `internal_gain_xp`). Clients only *report* hits (`report_hit`/`report_enemy_hit`, corroborated by attestations and rate buckets) and *mirror* the resulting hp rows (`HealthComponent`); the server never trusts client-supplied damage numbers.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| Where is damage computed? | — (pipeline, no table) | [Cross-table flows](#cross-table-flows), [[server/spacetimedb/src/combat/calc.rs#compute_player_damage|compute_player_damage]] / [[server/spacetimedb/src/combat/calc.rs#compute_incoming_damage|compute_incoming_damage]] |
| How does my bullet hurt an enemy? | ^table-enemy | [Outgoing: HitZone → report_enemy_hit → sink](#outgoing-player--enemy) |
| How do enemy bullets hurt me? | ^table-bullet-fire-attestation | [Incoming: attested report_hit → sink](#incoming-enemy--player) + embed `01.` below |
| Why was my hit report rejected? | ^table-player-hit-rate | [Report gates](#report-paths-gates-and-corroboration) + embed `02.` below |
| What corroborates that I was hit? | ^table-player-hit-flag | [Report gates](#report-paths-gates-and-corroboration) + embed `03.` below |
| What happens when I die? | ^table-player-data | [Death ownership](#death-ownership-split) |
| What happens when an enemy dies? | ^table-enemy | [Death ownership](#death-ownership-split) |
| Where does healing come from? | ^table-player-data | [Healing](#healing) |
| Where do buffs/debuffs change damage? | ^table-active-status-effect, ^table-active-enemy-status-effect | [[docs/Technical Design Docs/Description/13 Status Tables.md|13 Status]]; seam points in [Cross-table flows](#cross-table-flows) |
| How does hp show on screen? | ^table-player-data, ^table-enemy | [Client read path](#client-read-path-mirrors-only) |
| Why do volley pellets hit softer than single shots? | — | Per-pellet division in [Outgoing](#outgoing-player--enemy): weapon `damage` is per trigger pull, divided by `shot_count` |
| Why does DoT tickle armored enemies? | — | [Policies](#policies-one-struct-per-path): every mitigated hit subtracts flat defence with minimum 1 |

## Flowcharts

[[flowcharts/main-combat.canvas]] is this system's aggregate flowchart (pending the phase-2 recompose — see [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]]). Deep dives:

- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/combat_subfolder/combat_subfolder.canvas]] — the three pipeline files and their callers.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Combat_subfolder/Combat_subfolder.canvas]] — `HealthComponent` mirror, `HitZone`, `DamageReceivingComponent`, witness path.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Weapon_subfolder/Weapon_subfolder.canvas]] — `CombatComponent` firing and `HitZone` spawning.

## Tables

Combat defines **no tables** — it is the pipeline that writes other modules' rows. (Verified: no `#[table(` definition under `server/spacetimedb/src/combat/`; the only `table`-shaped tokens there are `ctx.db.*()` accessor calls and doc comments.) Its state lives in Player rows (^table-player-data hp/`defense`, ^table-player-stats strength, ^table-active-status-effect consults), Enemy rows (^table-enemy hp/phase/stagger, ^table-enemy-template `defense`, ^table-active-enemy-status-effect consults), and the report-path rows (^table-bullet-fire-attestation pellet pools, ^table-player-hit-rate buckets, ^table-player-hit-flag witness flags). Damage enters only through the sinks ([[server/spacetimedb/src/combat/mod.rs#DamageRequest|DamageRequest]] policies for enemies, `IncomingDamageOptions` for players) and reaches clients through those tables' own views — see [[docs/Technical Design Docs/Description/12 Player Tables.md|12 Player]] and [[docs/Technical Design Docs/Description/09 Enemy Tables.md|09 Enemy]] for the per-table Changers/Readers.

## Cross-table flows

### Outgoing: player → enemy

The client never deals damage — `HitZone` (attacker hitbox, `AreaComponent` subclass, spawned by [[client/Scripts/Components/Weapon/CombatComponent.cs#CombatComponent|CombatComponent]] along each bullet path) only *reports*: on its lifetime fuse `ReportHits` finds faction-opposed `DamageReceivingComponent`s in contact, gates them to the elevation window around its stamped `Elevation` (firing hex level ± `BulletElevationDrop`/`BulletElevationClimb`, mirrored in `ServerConstants.cs`), and calls `ReportEnemyHit(enemy.EnemyId)` per enemy victim in [[client/Scripts/Components/Combat/HitZone.cs#ReportHits|HitZone.ReportHits]] — the victim id is all the server needs, because damage resolves server-side.

`report_enemy_hit` in [[server/spacetimedb/src/enemy/reducers.rs#report_enemy_hit|report_enemy_hit]] gates before any math: `require_in_world`, then the sustained-rate budget from the effective weapon (`fire_rate × shot_count × HIT_RATE_SLACK`, `HIT_RATE_SLACK = 2.0` in `main/global.rs`) against the outgoing counter in [[server/spacetimedb/src/player/methods.rs#hit_rate_allow|hit_rate_allow]] — over budget is spam/farming and rejected. A missing enemy or an `immortal` enemy returns `Ok` silently (no error, no damage), and a wrap-aware `HIT_VALIDATION_RADIUS` range check between the attacker's position row and the enemy rejects nonsense attributions. Only then does it call [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_enemy|deal_damage_to_enemy]], the weapon-path wrapper that resolves [[server/spacetimedb/src/combat/calc.rs#compute_player_damage|compute_player_damage]] and delegates with `(NORMAL, Some(profile_id), Some(ctx.sender()))` for xp credit and re-aggro.

The math splits at the mitigation seam (`ComputedDamage` in [[server/spacetimedb/src/combat/calc.rs#ComputedDamage|calc.rs]]): `base` is pre-defence, `post_mitigation` is the true-damage bonus defence must not touch. Per reported bullet hit: `raw = weapon.damage × (1 + strength × 0.002) / max(shot_count, 1)`, `base = max(raw + flat_bullet_mod, 0)`, `post = true_flat + raw × true_pct` — toggle + enchantment composition already folded by `resolve_effective_weapon`. Weapon `damage` is therefore **per trigger pull, divided among the pattern's bullets**, which is what makes many-weak vs few-strong patterns trade off. Missing stats row degrades to base 1; no weapon resolves to base 0 (which downstream deals no damage rather than the min-1 floor).

The sink [[server/spacetimedb/src/combat/apply.rs#apply_damage_to_enemy|apply_damage_to_enemy]] is the one enemy damage path — weapon hits, spells, DoT ticks, zone damage, and store releases all arrive with a policy (see [Policies](#policies-one-struct-per-path)). For mitigated requests it consults the target's enemy statuses ([[server/spacetimedb/src/status/methods.rs#enemy_defense|enemy_defense]] — ArmorBreak zeroes defence; [[server/spacetimedb/src/status/methods.rs#enemy_vulnerability_mult|enemy_vulnerability_mult]] — Vulnerability multiplies final damage), scales the pre-mitigation amount by the attacker's `DamageDealtMult` ([[server/spacetimedb/src/status/methods.rs#outgoing_damage_mult|outgoing_damage_mult]]), then `base = max(amount − defense, 1)` (except `amount == 0`, which stays 0) and `damage = (base + post_mitigation) × vulnerability_mult` — **flat defence subtracted per hit with minimum 1, true damage added after defence**. Attacker-side consults run on the final number in fixed order: `StoreDamage` withhold ([[server/spacetimedb/src/status/methods.rs#withhold_outgoing|withhold_outgoing]] — absorbed into the pool, hit returns early), outgoing bleed conversion ([[server/spacetimedb/src/status/methods.rs#convert_outgoing_to_bleed|convert_outgoing_to_bleed]] — becomes an enemy DoT instead), otherwise [[server/spacetimedb/src/status/methods.rs#note_damage_dealt|note_damage_dealt]] records it for DealDamage-purge progress. On survival the row updates hp/phase (phase recomputed via `compute_phase` from hp thresholds) and re-aggros onto `aggro_from` unless locked or `None` (scheduled ticks pass `None` — no meaningful sender).

Non-weapon outgoing paths reuse the same sink: `Spell` abilities land volleys as separate mitigated hits of `total / hits` each (`total = p.damage × (1 + scaling stat × 0.002) × charge_mult`, scaling off Wisdom/Dexterity per spell, not Strength), re-finding the enemy row before every pellet so a mid-volley kill makes the rest no-ops, in `apply_ability_effect` ([[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]]); `Bash` swings go through the shared weapon path via [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_enemy_bonus|deal_damage_to_enemy_bonus]], whose pre-mitigation `bonus` merges the armed `StoreIncomingBash` pool into the same hit (post-mitigation pool captured earlier, so merging pre-mitigation avoids a double defence dip); enemy-zone ticks and store settlements call the sink directly with their policy.

### Incoming: enemy → player

Enemy bullets are rendered client-side from `BulletPatternEvent` rows, but the hit claim is server-validated. `BulletHitRouterComponent` routes BlastBullets2D overlaps: after the elevation band check (victim hex within [band − Drop, band + Climb] of the bullet's origin band, else the piercing bullet flies through), the local victim's [[client/Scripts/Components/Combat/DamageReceivingComponent.cs#ProcessBulletHit|DamageReceivingComponent.ProcessBulletHit]] calls `ReportHit(event_id)` while remote puppets' [[client/Scripts/Components/Combat/RemoteHitWitnessComponent.cs#ProcessBulletHit|RemoteHitWitnessComponent.ProcessBulletHit]] calls `FlagPlayerHit` (witness corroboration only — damage stays victim-reported).

`report_hit` in [[server/spacetimedb/src/player/reducers.rs#report_hit|report_hit]] gates in order: `require_in_world`; the incoming token bucket ([[server/spacetimedb/src/player/methods.rs#hit_rate_allow|hit_rate_allow]] at `INCOMING_HIT_RATE_CAP = 30/s`); then **attestation** — the claimed `event_id` must match a live ^table-bullet-fire-attestation row stamped by `attest_bullet_fire` at fire time, inside `BULLET_ATTESTATION_TTL_SECONDS = 30 s`, with `remaining > 0`; the attested enemy must still exist and be within `HIT_VALIDATION_RADIUS`. Damage is the tick-stamped value on the attestation row — **never client-supplied** — and each accepted claim decrements the pellet pool (pool exhausted → row deleted), so one volley cannot be replayed past its bullets. Then [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_player|deal_damage_to_player]] runs the sink.

The player sink [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]] runs the status intercept first ([[server/spacetimedb/src/status/methods.rs#intercept_incoming|intercept_incoming]]: Invulnerable → DamageShield → RedirectToCaster → DamageShare → DamageReductionPct → DamageToBleed, unless `skip_conversion`), mitigates via [[server/spacetimedb/src/combat/calc.rs#compute_incoming_damage|compute_incoming_damage]] (`max(base − PlayerData.defense, 1)`), applies the HpFloor clamp (a killing blow lands at 1 hp while the floor status lives), and on zero delegates to `kill_player` — otherwise it updates the `PlayerData` hp row. Fall damage arrives through the same sink from `report_movement` (`FALL_DAMAGE_PER_LEVEL × drop` past `FALL_SAFE_DROP`, with `?` so it can kill). Internal callers bypass or reshape the intercept via `IncomingDamageOptions` (bleed drains and zone-share portions skip re-interception as loop guards; redirected hits skip only redirect).

```sync
![[00 Table Map#^table-bullet-fire-attestation{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-player-hit-rate{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-player-hit-flag{seamless:true,title:false,marker:03.}]]
```

### Report paths, gates, and corroboration

Both report reducers are self-reported by design (the victim's client saw the overlap) and gated the same way: in-world guard → per-direction 1-second token bucket → plausibility range check → delegate. The asymmetry is what each attests against: incoming claims attest pellet-by-pellet against a server-stamped fire row (enemies are server-simulated, so the server knows exactly what fired); outgoing claims have no fire attestation (the client aims and fires freely) and are instead budgeted against the equipped weapon's own rate — you cannot report more hits than your weapon could physically land. `flag_player_hit` in [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]] (in-world required, self-reports silently ignored) collects the independent corroboration: every other client whose `RemoteHitWitnessComponent` overlapped the victim flags it, rows older than `HIT_FLAG_MAX_AGE_SECONDS` are swept in `tick_status_effects`, and the `NoHits` wager settlement in [[server/spacetimedb/src/status/methods.rs#settle_store_mark|settle_store_mark]] downgrades a claimed 0-hit 2× payout to 1× when ≥ `NO_HIT_WITNESS_THRESHOLD = 2` witnesses disagree (solo play has no witnesses — a documented residual exploit).

### Policies: one struct per path

[[server/spacetimedb/src/combat/mod.rs#DamageRequest|DamageRequest]] replaces mode × options boolean pairs for the enemy path: `NORMAL` (weapon hits, spells, zones, wagers — full mitigation + both outgoing consults), `DOT_TICK` (enemy DoT ticks — mitigated, but no withhold/bleed consults, so a Bloodbath DoT can't re-convert its own tick into a fresh decaying DoT forever instead of landing), `FLAT_RELEASE` (store-damage releases — already mitigated at capture, so no second defence subtraction and no vulnerability double-dip, consults run). `IncomingDamageOptions` is the player-path equivalent: `DEFAULT_INCOMING_OPTIONS` (full intercept + floor), `BLEED_DRAIN_OPTIONS` / `SHARED_PORTION_OPTIONS` (skip intercept + redirect, still respect floor — drains and share portions were already intercepted at the source hit), `REDIRECTED_OPTIONS` (intercept runs on the new victim, only redirect itself skipped so chains can't loop).

### Death ownership split

Death ownership is split by domain, stated once in [[server/spacetimedb/src/combat/mod.rs|mod.rs]] and enforced by both sinks: combat never touches lobby rows or teardown lists itself. A player killing blow calls [[server/spacetimedb/src/player/methods.rs#kill_player|kill_player]] — the single death-cleanup entry that runs `teardown_profile` (all world rows for the profile) plus the `LoggedInPlayer` → `LoggedOutPlayer` lobby move, so the client learns death through the same subscriptions that spawned it (local-player row deletes → `EntitySpawnerComponent` despawns the puppet; lobby row appears). An enemy kill calls [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] (flat `Enemy` row + whole behavior tree, no orphans) plus [[server/spacetimedb/src/status/methods.rs#on_enemy_killed|on_enemy_killed]] (purges statuses referencing the dead enemy and its debuff rows, forfeiting unsettled wager pools), then awards `max_hp / 10` xp to `xp_credit` via [[server/spacetimedb/src/player/methods.rs#internal_gain_xp|internal_gain_xp]] (level-up heals by the gained max-hp delta on top of current hp, never a full restore).

### Healing

[[server/spacetimedb/src/combat/apply.rs#heal_player|heal_player]] is the only heal path: `min(hp + amount, max_hp)`, no-op without a data row — it can never overheal or resurrect. Callers: `apply_consumable_effect` for `ConsumableEffect::Heal` ([[server/spacetimedb/src/player/methods.rs#apply_consumable_effect|apply_consumable_effect]]), the status tick's Regen/regen-aura/zone-heal passes, and channeled-action completion — all in the status/player modules, never the client.

### Client read path (mirrors only)

Hp flows one way — server row → view → wave → binder → mirror — and damage is never computed locally. Local hp: `local_player_data` per-caller view → `GameTables` wave → `LocalPlayerDataBinder` on [[client/Scripts/Components/Data/LocalPlayerDataComponent.cs#OnDataRow|LocalPlayerDataComponent]] (wired in `local_player.tscn`) → [[client/Scripts/Components/Combat/HealthComponent.cs#SetFromServer|HealthComponent.SetFromServer]], which emits `HealthDidDecrease`/`HealthDidIncrease`/`HealthDidZero` for observers; the visible number is the `HP: {Hp}/{MaxHp}` line in `StatsSidebar` reading `LocalPlayer`'s pass-throughs. (No client code subscribes to the Health signals yet — death's visible end is server-driven: `kill_player`'s row deletes despawn the puppet and re-show the lobby.) Enemy hp: `nearby_enemies` AOI view → `EntitySpawnerComponent` spawns one `Enemy` puppet per row, and each puppet's own binder feeds [[client/Scripts/Players/Enemies/Enemy.cs#OnEnemyRowUpdated|OnEnemyRowUpdated]]/`OnEnemyRowInserted`, which pushes `row.Hp` plus the template max_hp (read one-shot from `EnemyTemplates`) into its `HealthComponent` and mirrors `row.Stagger` onto the debuff bar. `HealthComponent` deliberately has no `Damage()`/`Heal()` methods — it is a read-only mirror, the client-side counterpart the pipeline docs describe (see Known gaps for the stale name in those comments).

## Files — pointer deep dive

In `server/spacetimedb/src/combat/` (pointer index: [[server/spacetimedb/src/combat/pointers.md|pointers]]).

### `mod.rs`

Policy vocabulary + re-exports so callers keep `crate::combat::` paths. Defines [[server/spacetimedb/src/combat/mod.rs#DamageRequest|DamageRequest]] with its three const policies (`NORMAL` / `DOT_TICK` / `FLAT_RELEASE`, each documented with *why* its consult flags are set), `IncomingDamageOptions` with the four consts (`DEFAULT` / `BLEED_DRAIN` / `SHARED_PORTION` / `REDIRECTED`, each a loop-guard or floor choice), and the module docs stating the damage model (flat per-bullet defence, min 1; per-trigger-pull division; true damage after defence) and the death-ownership split. Called into by `status/methods.rs` (uses `DamageRequest` in `convert_outgoing_to_bleed`, `settle_store_mark`, and `tick`).

### `calc.rs`

Pure math, no row writes — but not side-effect-free in the SpacetimeDB sense: both functions *read* rows. [[server/spacetimedb/src/combat/calc.rs#compute_player_damage|compute_player_damage]] reads `PlayerStats` (strength scale) and the resolved effective weapon (toggle + enchantment composition already applied) and returns the pre/post-mitigation split; [[server/spacetimedb/src/combat/calc.rs#compute_incoming_damage|compute_incoming_damage]] reads `PlayerData.defense` and returns `max(base − defense, 1)`. Called only from `apply.rs` — reducers never call these directly; they resolve base damage (attested stamp, spell total, fall depth) and delegate to the sinks so every path shares the one mitigation site.

### `apply.rs`

The sinks — the only functions that write hp. [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_player|deal_damage_to_player]] is the public wrapper pinning `DEFAULT_INCOMING_OPTIONS`; [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]] runs intercept → mitigate → HpFloor → `kill_player`-or-update, reading `PlayerData` and `ActiveStatusEffect` (HpFloor). [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_enemy|deal_damage_to_enemy]] / [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_enemy_bonus|deal_damage_to_enemy_bonus]] resolve the attacker's weapon damage (bonus variant merges the armed StoreIncomingBash pool pre-mitigation) and delegate; [[server/spacetimedb/src/combat/apply.rs#apply_damage_to_enemy|apply_damage_to_enemy]] mitigates, consults, writes hp/phase, re-aggros, and owns the kill branch (despawn + status purge + xp). [[server/spacetimedb/src/combat/apply.rs#heal_player|heal_player]] clamps to max_hp. Called from `player/reducers.rs` (`report_hit`, `report_movement` fall damage, `apply_ability_effect` spells/bash), `enemy/reducers.rs` (`report_enemy_hit`), and `status/methods.rs` (drains, shares, redirects, settlements, DoTs, zones, regen).

## Known gaps / stubs

- Stale `DamageComponent` name in combat's own doc comments: [[server/spacetimedb/src/combat/mod.rs|mod.rs]] and [[server/spacetimedb/src/combat/calc.rs|calc.rs]] both describe the pipeline as "the server-side counterpart of the client's HealthComponent/DamageComponent composition" — `DamageComponent` was deleted (its successor pattern is `HitZone`, which derives from `AreaComponent`). Comment-only staleness; behavior unaffected.
- Stale sink/option names in the maintainer ref: [[server/AGENTS.md|server AGENTS.md]]'s combat section names `apply_damage_to_enemy_opts` and `DOT_TICK_OPTIONS` — neither exists in code (the sink is [[server/spacetimedb/src/combat/apply.rs#apply_damage_to_enemy|apply_damage_to_enemy]], the policy is `DamageRequest::DOT_TICK`). Code wins; the ref needs updating.

## Where to go next

Read [[docs/Technical Design Docs/Description/09 Enemy Tables.md|09 Enemy]] for the fire path that stamps the rows combat attests (patterns, pellet pools, `BulletPatternEvent`), then [[docs/Technical Design Docs/Description/13 Status Tables.md|13 Status]] for the consults at both seams (intercept order, wagers, zones, DoTs). [[docs/Technical Design Docs/Description/12 Player Tables.md|12 Player]] owns the hp/xp/death rows this pipeline writes.
