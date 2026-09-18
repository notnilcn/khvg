# 04 Anticheat

## 1. Assumed knowledge

Read [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] (the `# Anticheat` section holds this doc's two table entries, `^table-player-hit-flag` and `^table-move-validation-strikes`), [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] (conventions, reading order), [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] (reducers-are-transactional, the `BaseTables`/`LobbyTables`/`GameTables` subscription waves, the one-binder-per-table pattern), and 12 Player (the `report_movement`/`tick_status_effects` reducers and `teardown_profile` that write these tables) plus 13 Status (the `StoreDamage`/`NoHits` wager that reads the witness flags). Maintainer jump-off points, not authority: [[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]]. A *reducer* is a server function a client calls by name that runs transactionally and returns no data — clients learn outcomes only through table/subscription updates. A *server-only* table (no `public` flag on its `#[table]`) can never be subscribed to, so it has no client read path by construction.

## 2. The 30-second version

Anticheat owns exactly two tables, both server-only (never subscribed, no binder, no visible end): `PlayerHitFlag`, one row per third-party witness claim that somebody got hit, filed by other clients through the `flag_player_hit` reducer and swept after `HIT_FLAG_MAX_AGE_SECONDS` (60 s) in the 1 Hz `tick_status_effects` pass so the `NoHits` damage wager can corroborate "I took zero hits" against independent observers; and `MoveValidationStrikes`, one row per profile holding recent movement-validation failure timestamps, appended by `record_move_strike` from inside `report_movement` — reports are clamped along their vector until `STRIKES_BEFORE_REJECT` (5) failures land inside `STRIKE_WINDOW_SECS` (10 s), then rejected with a resync — with dead rows swept by `sweep_strikes` and full cleanup in `teardown_profile`.

## 3. Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How does another client report seeing me get hit? | `PlayerHitFlag` | [[#PlayerHitFlag\|PlayerHitFlag]] — Changers (`flag_player_hit` call chain) |
| What stops a client from lying "I took zero hits" for the wager bonus? | `PlayerHitFlag` | [[#PlayerHitFlag\|PlayerHitFlag]] — Readers ***Server*** (`settle_store_mark`) |
| Why was my movement clamped instead of applied? | `MoveValidationStrikes` | [[#MoveValidationStrikes\|MoveValidationStrikes]] — Changers (budget + clamp) |
| When does clamping escalate to rejection + resync? | `MoveValidationStrikes` | [[#MoveValidationStrikes\|MoveValidationStrikes]] — Changers (strike counting) |
| What are the strike window / threshold / flag-age numbers? | `MoveValidationStrikes` | [[#MoveValidationStrikes\|MoveValidationStrikes]] — Changers (tunables) |
| Where do I see anticheat on screen? | — | [[#PlayerHitFlag\|PlayerHitFlag]] — Readers ***Client*** (server-only, no visible end) |
| Where is damage actually computed and applied? | attestations in ^table-bullet-fire-attestation | 08 Combat (shared sinks); corroboration math in [[#Cross-table flows\|Cross-table flows]] |
| How are fired bullets proven real (vs invented hits)? | ^table-bullet-fire-attestation, ^table-bullet-pattern-event | [[#Cross-table flows\|Cross-table flows]] |
| What movement state backs the clamp budget? | ^table-player-movement-state, ^table-player-displacement-allowance | [[#MoveValidationStrikes\|MoveValidationStrikes]] — Changers |
| Where do hit-rate limits live? | ^table-player-hit-rate | [[#Cross-table flows\|Cross-table flows]] |

## 4. Flowcharts

- [[flowcharts/main-anticheat.canvas]] — the composed anticheat flow (recompose pending at write time, so this link may stay unresolved until the Obsidian "Regenerate all flowcharts" run; see `01 Roadmap.md` phase-0 notes).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/anticheat_subfolder/anticheat_subfolder.canvas]] — server-side deep dive (the whole module: both tables plus all four functions).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/anticheat_subfolder/reducers_codefile/reducers_codefile.canvas]] — reducer/helper deep dive (`flag_player_hit`, `record_move_strike`, `live_strike_count`, `sweep_strikes`).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Combat_subfolder/Combat_subfolder.canvas]] — client-side deep dive (the witness call chain: router → `RemoteHitWitnessComponent` → `FlagPlayerHit`).

## 6. Tables

### PlayerHitFlag

```sync
![[00 Table Map#^table-player-hit-flag{seamless:true,title:false,marker:01.}]]
```

#### Shape

One row is one witness claim: auto-increment `flag_id` primary key, btree-indexed `hit_player` (the victim's SpacetimeDB `Identity`), `reported_by` (the witness's identity), `reported_at`. The row deliberately carries no bullet identity — just who was hit, who saw it, and when — *because* corroboration only needs to answer "did anyone else see this player get hit during the window", never "which pellet". The victim is keyed by `Identity` rather than `profile_id` *because* that is what a witnessing client actually holds: its `RemotePlayer` puppet exposes the victim's `PlayerId` identity, and the settlement read filters flags by the victim's `LoggedInPlayer.player_id`.

#### Changers

Insert happens in exactly one place: [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]] stamps a row with `hit_player` as passed, `reported_by` as `ctx.sender()`, `reported_at` as `ctx.timestamp` — but only after two guards, each silently passing rather than erroring: the caller must be in the world via [[server/spacetimedb/src/player/methods.rs#require_in_world|require_in_world]] (lobby clients can't file), and self-reports (`hit_player == ctx.sender()`) return `Ok(())` with no row *because* the victim's own client already reports damage through `report_hit` — this table collects only independent corroboration, and letting victims corroborate themselves would defeat it.

The client call chain that reaches the reducer starts in the physics overlap, not in UI code: BlastBullets2D's `area_entered` fires into [[client/Scripts/Components/Bullets/BulletHitRouterComponent.cs#OnEnemyBulletAreaEntered|BulletHitRouterComponent.OnEnemyBulletAreaEntered]], which first applies the elevation band check (victim hex within [band − drop, band + climb], else the piercing bullet flies on unreported) and then branches on the victim node type — a `DamageReceivingComponent` reports real damage, while a [[client/Scripts/Components/Combat/RemoteHitWitnessComponent.cs#ProcessBulletHit|RemoteHitWitnessComponent]] (the remote puppet's hurtbox) calls `GameManager.Conn?.Reducers.FlagPlayerHit(player.PlayerId)` and nothing else. That hurtbox is an `Area2D` on collision layer 2 / mask 0 (detects nothing itself; bullets register against it) with a capsule shape child, declared inline in [[client/Scenes/non_local_player.tscn|non_local_player.tscn]] — the live scene, which also wires the puppet's two position/rotation binders but wires no signal for the witness component, since it only ever emits outward.

Delete is the age sweep inside [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] (the single 1 Hz `ConsumableEffectSchedule` dispatcher): every flag with `seconds_since(reported_at) >= HIT_FLAG_MAX_AGE_SECONDS` (60 s) is deleted *because* no `NoHits` wager window outlives that and the table must not grow unbounded. The table is never updated — there is no update path anywhere.

#### Readers

***Client*** — Server-only: the `#[table]` carries no `public` flag, so no view or wave can carry it — it appears in none of the `BaseTables`/`LobbyTables`/`GameTables` lists in [[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber]], no `TableBinderComponent` binds it in any live scene, and there is no visible end. The witness hurtbox `Area2D` itself is pure collision with no sprite, so even the filing path renders nothing.

***Server*** — The only read is the `NoHits` arm of [[server/spacetimedb/src/status/methods.rs#settle_store_mark|settle_store_mark]] (StoreDamage settlement at window expiry): a `counter == 0` ("0 hits taken") claim tentatively earns the 2x multiplier, then corroborates by counting flags with `hit_player == login.player_id` and `reported_at >= row.last_event_at` (the window open stamp) — at or above `NO_HIT_WITNESS_THRESHOLD` (2) independent witnesses the payout is clamped to 1x. Solo play has no witnesses, which the code documents as an accepted residual exploit rather than blocking the payout.

### MoveValidationStrikes

```sync
![[00 Table Map#^table-move-validation-strikes{seamless:true,title:false,marker:02.}]]
```

#### Shape

One row per `profile_id` (primary key) holding `failure_timestamps`, the recent move-validation failure stamps inside the strike window. Failures accumulate as timestamps rather than a counter *because* old failures must age out — a counter could never forgive, while the vec lets every read prune to `STRIKE_WINDOW_SECS` (10 s) and move on.

#### Changers

Insert/update both happen in [[server/spacetimedb/src/anticheat/reducers.rs#record_move_strike|record_move_strike]] (upsert: update the row's stamp vec when present, insert a fresh row when absent), which is called from exactly one site — the strike block inside [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]]. That block fires only for non-admins (`is_admin` bypasses, mirroring the GM pattern) on two anomalies: a non-monotonic timestamp (server clock behind the profile's last report) or a gross origin-speed violation (`dist > budget × 2.0` with no fall in progress and no live grapple allowance — double the already-generous `MOVE_SLACK` budget, so lag alone can't trip it). On a strike the returned count is compared to `STRIKES_BEFORE_REJECT` (5): below it the report proceeds to the normal clamp; at or above it the reducer restamps the report clock and returns `Ok(())` without touching `PlayerPosition` — reject plus resync, with the client's next position echo snapping it back (BitCraft's `reset_mobile_entity_timer`, here a `teleport_player`-style restamp).

Below the strike threshold, and for all unstamped violations, reports are clamped, not rejected: the budget is `speed_cap × dt × MOVE_SLACK` (dt clamped to `MOVE_TIMESTAMP_PAST_TOLERANCE_SECS` so idleness can't bank a cross-map budget, floored at `MIN_REPORT_DISPLACEMENT` = 64.0), applied along the report's own wrap-aware direction, with one-shot exemptions for a live `PlayerDisplacementAllowance` grapple row and for downhill falls under `MAX_FALL_DISPLACEMENT`. Note what does *not* strike: the cliff-climb rejection (rising past `MAX_WALKABLE_UP_DELTA`) and the mid-fall lockout both restamp the clock and return early without calling `record_move_strike` — honest clients predict the block, so those paths are treated as level geometry, not cheating.

Delete has two paths: [[server/spacetimedb/src/anticheat/reducers.rs#sweep_strikes|sweep_strikes]] removes rows whose windowed count decayed to zero (reached via `tick_cleanup`, which the 1 Hz `tick_status_effects` dispatcher calls), and [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] deletes the row outright on death and on `delete_profile`. `leave_world` and disconnects do *not* tear it down, so a logged-out player's row lingers until the window ages out and the sweep collects it. Nothing else writes this table: no seeds, no admin upserts, no view.

#### Readers

***Client*** — Server-only, same as above: no `public` flag, no wave, no binder, no visible end. The only client-observable effect is second-order — once strikes cross the threshold the server stops advancing the stored position, so subsequent `LocalPlayerPosition` echoes re-assert the old row (the reconciliation path in `PositionSyncComponent`; local motion itself stays input-driven).

***Server*** — `report_movement` consumes `record_move_strike`'s returned count for the clamp-then-reject escalation; `sweep_strikes` consumes [[server/spacetimedb/src/anticheat/reducers.rs#live_strike_count|live_strike_count]] to find collectable rows. Both helpers scan one profile's small stamp vec — no full-table read except the sweep's bounded `iter()` with per-row delete.

## 7. Files — pointer deep dive

Per-file order follows [[server/spacetimedb/src/anticheat/pointers.md|pointers]].

### mod.rs

Two `pub mod` declarations plus `pub use reducers::{record_move_strike, sweep_strikes}` — the re-export is the module's public surface *because* both cross-module callers go through the crate path: [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] calls `crate::anticheat::record_move_strike`, and [[server/spacetimedb/src/main/agents.rs#tick_cleanup|tick_cleanup]] calls `crate::anticheat::sweep_strikes`. (`flag_player_hit` needs no re-export — reducers are addressed by the SpacetimeDB runtime, not by Rust callers.)

### reducers.rs

Owns all four entry points, in opposite directions. [[server/spacetimedb/src/anticheat/reducers.rs#flag_player_hit|flag_player_hit]] is the only `#[reducer]` (client-callable): guards via `require_in_world`, ignores self-reports, inserts one `PlayerHitFlag` row carrying the passed victim identity plus sender/timestamp — see [[#PlayerHitFlag\|PlayerHitFlag]] Changers for the full caller chain. [[server/spacetimedb/src/anticheat/reducers.rs#record_move_strike|record_move_strike]] is a plain server-side helper (no client call): it loads the profile's row, filters `failure_timestamps` to those within `STRIKE_WINDOW_SECS` (10 s, from [[server/spacetimedb/src/main/global.rs|global.rs]]), appends `ctx.timestamp`, upserts the row, `log::warn!`s the running count against `STRIKES_BEFORE_REJECT`, and returns the live count to its sole caller `report_movement`. [[server/spacetimedb/src/anticheat/reducers.rs#live_strike_count|live_strike_count]] is the read-only twin (same window prune, no write), called only by [[server/spacetimedb/src/anticheat/reducers.rs#sweep_strikes|sweep_strikes]], which deletes every row whose live count is zero so idle players' rows can't accumulate — called from `tick_cleanup` on the 1 Hz agent tick, which `tick_status_effects` dispatches.

### tables.rs

Owns both table definitions and nothing else (no helpers, no reducers). `PlayerHitFlag` (`accessor = player_hit_flag`, auto-inc `flag_id` PK, btree on `hit_player`) and `MoveValidationStrikes` (`accessor = move_validation_strikes`, `profile_id` PK, `failure_timestamps` vec) both omit `public`, which is exactly what makes them server-only. Writers: `flag_player_hit` inserts flags and the `tick_status_effects` sweep deletes them; `record_move_strike` upserts strike rows, `sweep_strikes` and `teardown_profile` delete them. Reader: `settle_store_mark` for flags, `sweep_strikes` (via `live_strike_count`) for strikes.

## 8. Cross-table flows

**Witness corroboration.** Three tables conspire at `StoreDamage` settlement: the victim's self-reported hits accumulated into the mark's `counter` via `intercept_incoming` on every `report_hit`, the independent `PlayerHitFlag` rows filed by bystanders' `flag_player_hit` calls, and the `ActiveStatusEffect` mark row itself (^table-active-status-effect) — [[server/spacetimedb/src/status/methods.rs#settle_store_mark|settle_store_mark]] multiplies the withheld pool by 2x/1x/0x from the counter, then clamps a perfect zero to 1x when enough witness flags contradict it. The trust model is explicit: victims report their own damage (attested separately against ^table-bullet-fire-attestation), witnesses only corroborate, and solo players are uncorroboratable by construction.

**Clamp-then-reject movement validation.** `report_movement` layers four tables before writing ^table-player-position: the ^table-player-movement-state row supplies the last-report clock for the displacement budget, the ^table-player-displacement-allowance row grants one oversized grapple report, `PlayerData.base_speed` (plus held `SelfSlow` rows) sets the speed cap, and `MoveValidationStrikes` decides clamp vs reject — honest lag gets clamped along its vector every time, and only sustained gross violations (5 in 10 s) get dropped with a resync. Hit-rate abuse is a separate, parallel layer on ^table-player-hit-rate (`hit_rate_allow` token buckets gating `report_enemy_hit`/`report_hit`), untouched by strikes.

## 10. Where to go next

Read 12 Player for the full `report_movement` validation stack and the `tick_status_effects` dispatcher these tables hang off, then 13 Status for the wager/mark lifecycle that consumes the witness flags — and 08 Combat for the damage sinks both mechanisms protect.

**Phase 2 proposal:** regenerate pointers for `server/spacetimedb/src/anticheat`: `pointers.md` claims no vault code outside the folder uses `mod.rs`, yet its `record_move_strike`/`sweep_strikes` re-exports serve `player/reducers.rs` and `main/agents.rs`; `pointers-symbols.md` shows no external users for `record_move_strike`/`live_strike_count`/`sweep_strikes` (called from `report_movement`/`sweep_strikes`/`tick_cleanup`) and omits the `flag_player_hit` insert plus the `record_move_strike`/`sweep_strikes` writes under `tables.rs#PlayerHitFlag`/`tables.rs#MoveValidationStrikes`.
