# 06 Combat Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for why the client never computes damage, [[03 Connection, Subscriptions & Views]] for how updated hp rows flow back to mirrors. This module owns **no tables** — it is the shared damage pipeline every reducer delegates to — so this doc has no Table changes section and no per-table sections. Instead it is organized one section per piece of machinery, like docs 02–03. For the rows the pipeline reads and writes, see `10 Player Tables` (`PlayerData`), `07 Enemy Tables` (`Enemy`), and `11 Status Tables` (every consult point).

## The 30-second version

All damage is computed and applied in exactly one place — `calc.rs` (pure math, no row writes) resolves base damage, `apply.rs` (the sinks) mitigates, subtracts, and handles kills — behind two policy types (`DamageRequest` for the enemy path, `IncomingDamageOptions` for the player path). The model is flat defence subtracted per hit with minimum 1: weapon damage is per trigger pull divided among the pattern's bullets, so many-weak-bullets trades off against few-strong-bullets, and true-damage enchantment bonuses apply after defence. Death ownership splits by domain: a player killing blow delegates to `kill_player` (teardown + lobby move live in the player module — combat never touches them), while an enemy kill despawns through the archetype helper plus the status kill hook here.

## Find it fast

| Question / concept | Where |
|---|---|
| Where is damage computed? (Nowhere else.) | [[#Pure math — calc.rs]] — `compute_player_damage` / `compute_incoming_damage` |
| How does a player actually lose hp / die? | [[#The sinks — apply.rs]] — `deal_damage_to_player_opts` |
| How does an enemy actually lose hp / die? | [[#The sinks — apply.rs]] — `apply_damage_to_enemy` |
| What do NORMAL / DOT_TICK / FLAT_RELEASE mean? | [[#Policy types — mod.rs]] — `DamageRequest` |
| What do skip_conversion / respect_floor / skip_redirect mean? | [[#Policy types — mod.rs]] — `IncomingDamageOptions` |
| Who calls the pipeline? | [[#Callers]] — hit reporters, spells, zones, DoTs, wagers, drains |
| How much XP is an enemy worth? | [[#The sinks — apply.rs]] — `max_hp / 10` to `xp_credit` |
| Why did my 0-damage weapon hit deal nothing (not even 1)? | [[#The sinks — apply.rs]] — amount 0 stays 0; the min-1 floor applies only to real hits |

## Flowcharts

- [[flowcharts/main-combat.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/combat_subfolder/combat_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Combat_subfolder/Combat_subfolder.canvas]]

## Pure math — calc.rs

[[server/spacetimedb/src/combat/calc.rs#compute_player_damage|compute_player_damage]] resolves the outgoing weapon damage per reported bullet hit: the effective weapon's per-trigger-pull `damage` (toggle + enchantment composition already folded by `resolve_effective_weapon` — see `08 Item Tables`), scaled by strength as `damage × (1 + strength × 0.002)`, divided among `max(shot_count, 1)` bullets. It returns the split at the mitigation seam — `ComputedDamage { base, post_mitigation }`: `base` is pre-defence (`raw + flat_bullet_mod`, floored at 0) because defence is subtracted later in `apply_damage_to_enemy` so all damage paths share one mitigation site; `post_mitigation` is the true-damage bonus (`true_flat + raw × true_pct`) defence must never touch. Two degenerate inputs are pinned: a missing `PlayerStats` row resolves to base 1 (never block damage on a missing mirror), and no equipped weapon resolves to base 0 — which the sink deliberately keeps at 0 rather than flooring to the min-1 (see below).

[[server/spacetimedb/src/combat/calc.rs#compute_incoming_damage|compute_incoming_damage]] is the entire player-side mitigation: `(base − PlayerData.defense)` with minimum 1 (missing data row: `base.max(1)`). Flat subtraction with a floor is the whole defence model — high defence is what makes many-weak-bullets patterns worse than few-strong-bullets ones, on both sides of the fight.

## Policy types — mod.rs

[[server/spacetimedb/src/combat/mod.rs#DamageRequest|DamageRequest]] is the single damage policy for the enemy path — what mitigation applies and which outgoing status consults run — with named constructors for the three call patterns, so no mode × options boolean pairs:

- `NORMAL` — weapon hits, spells, zones, wagers: full mitigation (flat defence, min 1; ArmorBreak zeroes it, Vulnerability multiplies after) plus both outgoing consults (StoreDamage withhold, DamageToBleed conversion).
- `DOT_TICK` — enemy DoT ticks: mitigated, but neither outgoing consult runs — a Bloodbath DoT can't re-convert its own tick into a fresh decaying DoT forever instead of ever landing, and a caster's own DoT ticks aren't withheld into their own store pool.
- `FLAT_RELEASE` — store-damage releases: already mitigated at capture (no double-dip on defence or Vulnerability), consults still run.

[[server/spacetimedb/src/combat/mod.rs#IncomingDamageOptions|IncomingDamageOptions]] gates the player path's two status-module consult points: `skip_conversion` bypasses the whole `intercept_incoming` chain (invulnerable/shield/redirect/reduction/bleed) for hits that already passed through it — bleed-pool drains (`BLEED_DRAIN_OPTIONS`) and DamageShare portions (`SHARED_PORTION_OPTIONS`, shared portions never re-intercept as a loop guard); `respect_floor` keeps or drops the post-mitigation HpFloor clamp; `skip_redirect` lets redirected hits re-run the intercept on the new victim without chain-looping (`REDIRECTED_OPTIONS`). The default (`DEFAULT_INCOMING_OPTIONS`) runs everything.

## The sinks — apply.rs

[[server/spacetimedb/src/combat/apply.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]] (with `deal_damage_to_player` as the default-options wrapper) is the one player damage sink: run the status intercept unless `skip_conversion` (an `absorbed` verdict returns `Ok` — invulnerable/shield/redirected hits still stamped their counters upstream), mitigate through `compute_incoming_damage`, then clamp to 1 instead of 0 when an `HpFloor` status row exists and `respect_floor` holds. At zero it delegates the full death cleanup to [[server/spacetimedb/src/player/methods.rs#kill_player|kill_player]] — combat never calls `teardown_profile` or touches the lobby rows itself — otherwise it writes the subtracted `PlayerData.hp`. No direct HP writes exist anywhere else: bleed drains, zone shares, and redirects all funnel through these flags.

[[server/spacetimedb/src/combat/apply.rs#apply_damage_to_enemy|apply_damage_to_enemy]] is the one enemy damage sink — weapon hits, spells, DoT ticks, zone damage, store-damage releases, all with a `DamageRequest`:

1. Resolve the template (missing template: defence 0 — never fail the hit on missing content).
2. Target-side consults on mitigated requests only: `enemy_defense` (ArmorBreak zeroes flat defence) and `enemy_vulnerability_mult` (×(1+magnitude) on the final damage).
3. Attacker-side `outgoing_damage_mult` (DamageDealtMult) pre-mitigation.
4. Mitigate: `amount == 0` stays 0 (the no-weapon case deals nothing, not the min-1 floor); otherwise `(amount − defence)` minimum 1; add the post-mitigation bonus; multiply Vulnerability.
5. Attacker-side consults on the final damage: `withhold_outgoing` (StoreDamage accumulates the pool INSTEAD of dealing — caller skips hp subtraction on true), then `convert_outgoing_to_bleed` (the warrior's outgoing mark turns the hit into an enemy DoT), else `note_damage_dealt` (DealDamage purge progress).
6. Kill vs survive: at zero, despawn the archetype, run `on_enemy_killed` (status rows never outlive their enemy), and award `internal_gain_xp` of `template.max_hp / 10` (default 10) to `xp_credit`. On survival, recompute the phase from hp thresholds (`compute_phase`) and re-aggro: a phase change retargets through `recompute_aggro`, otherwise aggro follows `aggro_from` unless the lock holds — skipped entirely when `aggro_from` is `None`, since scheduled-reducer ticks have no meaningful sender.

[[server/spacetimedb/src/combat/apply.rs#deal_damage_to_enemy|deal_damage_to_enemy]] is the weapon-path wrapper (`compute_player_damage` → `NORMAL` with xp credit and re-aggro onto the caller), and [[server/spacetimedb/src/combat/apply.rs#deal_damage_to_enemy_bonus|deal_damage_to_enemy_bonus]] merges a pre-mitigation flat bonus into the same hit — the armed StoreIncomingBash pool, which accumulates post-mitigation but lands inside the weapon hit's base rather than as a second mitigated hit. [[server/spacetimedb/src/combat/apply.rs#heal_player|heal_player]] adds hp clamped to `max_hp` and no-ops without a data row.

## Callers

Every damage path resolves base damage in its reducer and delegates here, so the sink list is the complete damage inventory: `report_hit` (victim-claimed, attested against ^table-bullet-fire-attestation, pellet-pool decremented) and `report_enemy_hit` (attacker-claimed, token-bucket + range-gated — see `07 Enemy Tables`) for weapon fire; the P5 Spell arm and P3 zone bursts/expiry for shape damage; enemy-DoT ticks with `DOT_TICK`; `settle_store_mark` releases with `FLAT_RELEASE`; bleed-pool drains with `BLEED_DRAIN_OPTIONS`; DamageShare portions with `SHARED_PORTION_OPTIONS`; redirected hits with `REDIRECTED_OPTIONS`; fall damage via the flagged path in `report_movement` (see `10 Player Tables`). Nothing else in the module writes hp.

## Files — pointer deep dive

Entry points: [[server/spacetimedb/src/combat/pointers.md|combat pointers]] (the consumer lists are nearly empty by design — callers depend on the re-exported names through `crate::combat::`, which the pointer generator doesn't trace; the real fan-in is the [[#Callers]] list above, plus `DamageRequest` used from `status/methods.rs`).

### mod.rs

Docs + policy vocabulary + re-exports (`mod apply; mod calc;` with `pub use` of every sink, both calc fns, and `ComputedDamage`). Callers keep their `crate::combat::` paths; the composition-mapping comment (one table per concern, `profile_id`/`enemy_id` as entity ids, split death ownership) is the design rationale for the whole pipeline shape.

### calc.rs

Pure damage math, no row writes (verified: the file reads `player_data`/`player_stats` and the effective weapon, and writes nothing). Change damage *formulas* here; the sinks and all their consult points follow automatically.

### apply.rs

The sinks that mitigate, subtract, and handle kills (verified: the only file in the module that writes `PlayerData.hp` / `Enemy.hp`, deletes via the archetype helpers, and calls `kill_player` / `internal_gain_xp` / `on_enemy_killed`). Change kill handling, XP rewards, or aggro rules here.

## Where to go next

[[07 Enemy Tables]] covers the firing side (who creates the volleys victims report) and the enemy rows this pipeline subtracts from. For the mitigation consults, read `11 Status Tables`; for the hp rows and death cleanup, `10 Player Tables`.
