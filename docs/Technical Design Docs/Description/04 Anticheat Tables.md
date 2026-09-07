# 04 Anticheat Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for the mirror/report split, [[03 Connection, Subscriptions & Views]] for waves, binders, and the event-table pattern. This doc owns one table: `PlayerHitFlag`. The wager-settlement read path assumes the status module's NoHits wager (see `11 Status Tables`); the victim-hit write path assumes the bullet pipeline (see `07 Enemy Tables` for `BulletPatternEvent` and `06 Combat Tables` for `report_hit` attestation).

## The 30-second version

`PlayerHitFlag` is a server-only witness ledger: whenever any client's enemy bullets overlap another player's puppet, that client calls `flag_player_hit` — not to deal damage (the victim's own client reports that through `report_hit`), but to leave an independent "I saw them get hit" row. The server never shows these rows to anyone; it reads them in exactly one place, corroborating a `StoreDamage` NoHits-wager 0-hits claim before paying 2x, and sweeps them after 60 s on the 1 Hz tick.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How does the server know I really got hit? | `PlayerHitFlag` | [[#PlayerHitFlag]] — victim claims via `report_hit` + attestation; witnesses corroborate via flags |
| What stops me from claiming zero hits for the NoHits wager bonus? | `PlayerHitFlag` | [[#PlayerHitFlag]] — `settle_store_mark` downgrades 2x to 1x past the witness threshold |
| Where do remote-player bullet overlaps go? | `PlayerHitFlag` | [[#PlayerHitFlag]] — `RemoteHitWitnessComponent` → `FlagPlayerHit` reducer, damage never included |
| Why is there no anti-cheat UI or debug view? | `PlayerHitFlag` | [[#PlayerHitFlag]] — server-only table, inspect via `spacetime sql` |
| How long do witness flags live? | `PlayerHitFlag` | [[#PlayerHitFlag]] — swept past `HIT_FLAG_MAX_AGE_SECONDS` in `tick_status_effects` |

## Flowcharts

- [[flowcharts/main-anticheat.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/anticheat_subfolder/anticheat_subfolder.canvas]]

## Table changes

```sync
![[00 Table Map#^table-player-hit-flag{seamless:true,title:false,marker:01.}]]
```

## PlayerHitFlag

### Shape

One row per witnessed hit in [[server/spacetimedb/src/anticheat/tables.rs#PlayerHitFlag|tables.rs]]: `flag_id` (auto-inc primary key), `hit_player` (btree-indexed victim identity — the query column), `reported_by` (the witness), `reported_at` (server timestamp for the 60 s sweep). The row deliberately carries **no bullet identity** — not which volley, pellet, or enemy fired it — because its only job is coarse corroboration ("someone independent saw this player get hit recently"), not hit adjudication. Hit adjudication is the victim's `report_hit(event_id)` claim checked against the per-volley pellet pool in ^table-bullet-fire-attestation; the two mechanisms are complementary, not redundant.

### Changers

Inserts come only from [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]]: it requires the caller to be in-world (`require_in_world` — spectators and lobby clients can't file flags) and then **silently ignores self-reports** (`hit_player == ctx.sender()` returns `Ok(())` with no write), because the victim's own client already has its damage voice through `report_hit` and this table must contain only independent corroboration. The row stamps `reported_by: ctx.sender()` — never a passed identity, since `ctx.sender()` is the authenticated principal — and `reported_at: ctx.timestamp`.

Nothing ever updates a flag. Deletes come only from the 1 Hz [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] housekeeping pass, which deletes rows older than `HIT_FLAG_MAX_AGE_SECONDS` (60 s, in [[server/spacetimedb/src/main/global.rs#HIT_FLAG_MAX_AGE_SECONDS|global.rs]]) — no NoHits wager window outlives that, and without the sweep the table would grow with the global bullet-collision rate.

### Readers

- ***Client*** — there is no client reader, and that is load-bearing, not an omission: the table is not `public`, appears in no subscription wave, and has no binder. Its only observable effect on screen is second-order, through the wager settlement below (a challenged 2x payout lands as 1x damage instead). The *write* path does start on the client, and it ends here: enemy bullets are piercing (`bullet_max_collision_count = 0`, so one bullet can overlap several puppets), which is why single-hit enforcement moved to the router's manual `disable_bullet` — and why every client sees overlaps against *other* players' puppets. `BulletHitRouterComponent` applies the elevation band check first (victim hex within [band − drop, band + climb], else the overlap flies through), then forwards remote-player overlaps to [[client/Scripts/Components/Combat/RemoteHitWitnessComponent.cs#ProcessBulletHit|RemoteHitWitnessComponent.ProcessBulletHit]] — the hurtbox child declared inline in [[client/Scenes/non_local_player.tscn|non_local_player.tscn]] on collision layer 2 / mask 0 — which calls the `FlagPlayerHit` reducer with the puppet's identity. Witnesses only corroborate; damage stays reported exclusively by the victim's own client through `ReportHit`.
- ***Server*** — exactly one read site: `settle_store_mark` in [[server/spacetimedb/src/status/methods.rs#settle_store_mark|status/methods.rs]] settles an expired `StoreDamage` mark with a `NoHits` wager. `counter == 0` means `last_event_at` is still the apply time, so it counts flags on the claimant filed after that timestamp; at or above `NO_HIT_WITNESS_THRESHOLD` the 2x multiplier drops to 1x. The `Radius` and `Damage` wagers never touch this table (movement-violation stamps and pool thresholds settle them), and the tick's expiry sweep is the only scan — indexed by `hit_player`, never a full-table gameplay query.

### Files — pointer deep dive

Entry points: [[server/spacetimedb/src/anticheat/pointers.md|anticheat pointers]].

### mod.rs

Two module declarations (`reducers`, `tables`) — the composition root, nothing else. No vault code outside the folder uses it directly; both consumers (the `FlagPlayerHit` client caller and the two server table users) point at the files below.

### reducers.rs

Owns the single reducer [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]]: guard (`require_in_world` from [[server/spacetimedb/src/player/methods.rs#require_in_world|player/methods.rs]] — lobby/disconnected callers get an `Err`, never a row), self-report filter (silent `Ok`, so victims can't corroborate themselves even by accident), then the insert. Called from exactly one client site — `RemoteHitWitnessComponent.ProcessBulletHit` via [[client/Scenes/non_local_player.tscn|non_local_player.tscn]] — which is why the reducer takes only the victim identity and derives everything else from context.

### tables.rs

Owns the [[server/spacetimedb/src/anticheat/tables.rs#PlayerHitFlag|PlayerHitFlag]] definition plus its doc comment (the "no bullet identity" rationale). Read from [[server/spacetimedb/src/status/methods.rs#settle_store_mark|status/methods.rs]] (wager corroboration) and swept from [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|player/reducers.rs]] (age sweep) — the pointers' consumer/related lists name exactly these two files, and there are no others.

### Cross-table flows

The witness loop spans three tables but fits in one sentence each: a bullet overlap on a remote puppet files ^table-player-hit-flag (this doc); the victim's own `report_hit` claim decrements ^table-bullet-fire-attestation and lands damage through the combat sink (`06 Combat Tables`); an unsettled `StoreDamage` mark (^table-active-status-effect) later settles against both — pool size from withheld hits, multiplier from these flags. No separate section needed beyond those entries.

### Known gaps / stubs

- **Solo play has no witnesses — accepted residual exploit.** `settle_store_mark` notes it inline: with nobody else around to file flags, a solo player can claim an unchallenged 0-hits 2x payout. The code accepts this rather than blocking the wager offline; document, don't "fix" without a design decision.
- **Coarse corroboration only.** Flags carry no bullet identity, volley id, or damage, so a flag can't tie a witness to a specific `report_hit` claim — it only proves *some* hit was seen recently. Fine for wager challenges; insufficient for per-hit fraud proofs if those are ever wanted.

## Where to go next

[[05 Chest Tables]] continues the module docs with loot containers. For the other half of the hit pipeline (victim claims, pellet pools, mitigation), read [[06 Combat Tables]] and `07 Enemy Tables` once written.
