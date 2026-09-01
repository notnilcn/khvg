


1. test ^test

# Name
KHV:
Khvor' Gates
Khvor' means illness/disease/ailment. It usually refers to a state of being unwell.


![[server/spacetimedb/src/world/tables.rs#MapConfig]]

```
cloudflared tunnel run khvgames-server
```

```
curl https://khvgames.com/server/v1/ping     # should return 200
```








# Hostile Review + Fix Plan — bullethell client/server

Scope decisions (user): fix **everything** including Low; trust model gets the **cheap validation layer** (no server-side bullet sim).

Part 1 is the review. Part 2 is the phased fix plan. Every finding was verified against code (subagent deep-dives + direct reads); line numbers are current as of this session.

---

## Part 1 — The review, no sugar-coating

### A. The server trusts the client far past its own claims

"Server authoritative" is in the README; the reducers didn't get the memo.

1. **`report_movement` accepts absolute positions, zero displacement validation** — `server/spacetimedb/src/player/reducers.rs:91-116`. The only clamp is on the `movement_speed` scalar, which constrains nothing about position. Any client teleports anywhere at 10 Hz, and every other positional check in the module (`HIT_VALIDATION_RADIUS`, `MAX_ABILITY_TARGET_RANGE`, the Radius-wager stamp) anchors on this row. One missing check invalidates all the others.
2. **`report_enemy_hit` has no rate limit, no range check, no cadence validation** — `enemy/reducers.rs:137-149`. `require_in_world` + not-immortal, then full damage. Scripted client: arbitrary DPS on any enemy cross-map, free XP, aggro theft per call. The *outgoing* damage path has less validation than the incoming one (`report_hit` at least checks radius, `player/reducers.rs:166-170`).
3. **`control_bullets` is a free ability cast** — `enemy/reducers.rs:112-135`. No item ownership, no cooldown, no charges, no radius cap, no range clamp — it bypasses the entire `resolve_ability_slot`/`spend_ability` gate that `activate_ability` goes through. `Delete` with `radius = f32::MAX` wipes every bullet for everyone; `Attract` with hostile target homes bullets onto a victim. This reducer shouldn't exist as a public entry point at all — the ability path already appends `BulletControlEvent`s.
4. **`pickup_drop` / `take_from_chest` — no range checks, enumerable ids** — `player/reducers.rs:181-194`, `chest/mod.rs:66-82`. Vacuum every drop on the map from spawn; `regenerating` chest rows are an infinite, remotely-farmable item source.
5. **Cooldown/charge laundering via backpack round-trip** — `player/reducers.rs:307-313` deliberately re-initializes `cooldown_until → None`, `charges → max` when an item enters the ability region from a non-ability slot. Two `swap_slots` calls reset any cooldown and refill all charges. `drop_item`+`pickup_drop` launders the same way. The entire ability-economy tuning dies in two reducer calls.
6. **"Knight" profile backdoor** — `player/methods.rs:453-460` grants 999 points when `profile_name == "Knight"`; `create_profile` lets anyone pick the name. Test scaffolding reachable by any non-admin client today. This must never survive a publish.
7. **`claim_admin` is unauthenticated god mode** — `main/admin.rs:15-31`. First caller on a fresh server owns the single global admin slot: `change_stats`, spawn/despawn, world regen. Fine for local dev, full server takeover on any deployed instance.
8. **Anticheat witness flags are toothless and weaponizable** — `status/methods.rs:313-316` counts raw flag rows with no distinct-`reported_by` check (one griefer calling `flag_player_hit` twice clamps an honest player's NoHits payout); flags never *gate* `report_hit`; `StoreIncoming` pools accumulate **pre-mitigation** damage (`status/methods.rs:249-251`) so spam-reporting a heavy step charges a nuke on demand; and `player_hit_flag` rows are **never deleted** — unbounded growth.

### B. Server correctness — the sim fights itself

9. **Catch-up math turns range re-entry into a bullet flood** — `enemy/methods.rs:469-487`. `step_timer` doesn't advance while unsimulated/out-of-range; on re-entry the catch-up loop fires `MAX_CATCHUP_SHOTS` (8) per tick at 80 intervals/s regardless of authored rate. GorgonBoss (`move_sim_factor 2.75` vs `attack_sim_factor 1.35`, `gorgon_boss.rs:325-326`) is the built-in victim: a 25/s emitter idled 10 s fires 80 shots/s for ~3 s, with `angle_step` spin warping it into a wall. The cap bounds the per-tick burst; the backlog itself is unbounded. Also greets the first player after every spawn.
10. **Torus-seam blindness** — `has_player_in_simulation_range` (`enemy/methods.rs:28-32`), `find_nearest_player_id` (`:47-56`), Chase/Flee vectors (`:564-590`) use raw Euclidean distance on a torus. `enemies_near_point`/`players_near_point` were deliberately made wrap-aware; these three were missed. Enemies freeze next to adjacent players across a seam; aggro picks the wrong "nearest"; chasers walk the long way around.
11. **Aggro never refreshes while simulating** — `enemy/reducers.rs:38-44` recomputes only on the rising edge. Target logs out → `aggro_target` keeps a dead Identity → `find_player_pos_by_id` returns `None` forever → Chase/Flee idle permanently while aimed patterns fire at a ghost.
12. **`client_connected` recovery skips the runtime purge** — `main/lifecycle.rs:40-45` moves to `LoggedOutPlayer` without `purge_profile_statuses`/`purge_profile_zones`, violating the module's own "no status ticks a logged-out player" invariant; held-charge SelfSlow and owned zones survive into the lobby.
13. **`LootDrop.expires_at` is dead** — stamped (`player/reducers.rs:219`), never read, never swept. Drops persist and remain pickable forever; table grows monotonically.
14. **Admin single-`despawn_enemy` leaks status rows** — `admin.rs:108-114` skips `status::on_enemy_killed` (its sibling `despawn_all_enemies` doesn't). Self-heals at the 1 Hz sweep; inconsistent invariant application.
15. **Live template edits can panic the scheduled behavior tick** — unchecked `phase_def.attacks[attack.attack_index]` (`enemy/methods.rs:341, 276`) + runtime `upsert_enemy_template`. A panic in a scheduled reducer destroys the WASM instance — the one reducer that must never die.
16. **`place_building` is a free, range-less, cost-less map write** — `world/reducers.rs:6-18`. Any logged-in player claims any tile anywhere; only `remove_building` checks ownership.
17. **XP math overflows into an infinite loop** — `xp_for_level` in u32 wraps above level ≈9262 (`player/methods.rs:36`); `compute_level`'s `while` then never terminates and hangs the reducer. Practically unreachable; module-wide hang if reached.
18. **`use_item` ignores slot role** — `player/reducers.rs:380-395` consumes a `Consumable`-behavior item from any slot; hotbar is cosmetic server-side. Latent worse case: an item with both `Ability` and `Consumable` behaviors gets consumed off a span head, orphaning follower cells that `find_free_span` then treats as permanently blocked.

### C. Server perf — scheduled reducers do scan-and-rewrite

19. **Behavior tick: full-table scan + unconditional writes at 10 Hz** — `enemy/reducers.rs:22` iterates all enemies; per in-range enemy it writes `EnemyPhase`/`EnemyBehavior`/`Enemy` every tick even when byte-identical; `tick_phase` rewrites every `EnemyAttack` row every tick (`methods.rs:356,363,369`). Gorgon phase 7: ~27 writes/tick ≈ 270 row-writes/s for one boss, each churning indexes and fanning out subscription updates.
20. **Stunned enemies redo the full aggro scan every tick** — `reducers.rs:40-54`: the stun `continue` skips the row write, so `was_simulating` stays true and `recompute_aggro` (a full `player_position` scan) reruns every tick of the stun.
21. **Spawn pass is O(regions × enemies)** — full `enemy().iter()` per region inside `spawn_from_biome_regions` (`methods.rs:612-614`), every 2 s.
22. **1 Hz status tick = five full-table scans, plus one per kill** — `status/methods.rs:377, 491, 509, 533, 545` all `iter().collect()`; `on_enemy_killed` full-scans `active_status_effect` per kill (`:337`).
23. **Unconditional position/rotation row writes** — `report_movement` writes the row even when nothing changed (`player/reducers.rs:116`); `report_screen_rotation` at ~30 Hz likewise. ~40 N row-updates/s fanned out per subscriber for N nearby players. `PlayerChunk` shields view recompute (good — that design is correct); the row-churn bandwidth isn't mitigated.
24. **`nearby_remote_players_profiles` is not AOI-filtered at all** — `player/views.rs:125-130` semijoins against *all* `LoggedInPlayer` rows. Every client gets the whole-world roster. The name lies.
25. **Terrain AOI radius barely filters on the seeded map** — radius 2 on a 6×6 chunk map = 19 of 36 chunks (~53% of the world, ~2166 `TriangleTile` rows) per client; each chunk crossing swaps ~570 rows. Demo-scale fine; the constant is doing almost no work.
26. **`DOT_TICK_OPTIONS` misses one consult** — `withhold_outgoing` isn't gated by the options (`combat/mod.rs:224-229`): a player with StoreDamage + outgoing-bleed has their own DoT ticks withheld into their own store pool. The options struct exists to prevent exactly this self-interaction and misses one gate.

### D. Client lifecycle — one hiccup and the game lies to you

27. **Zero disconnect handling** — `sstdbsdk/DatabaseConnector.cs:93-97`: `OnDisconnected` prints, nothing else. Binders stay hooked to a dead `Conn`, the world renders stale rows, reducer calls vanish. For an MMO client this is the single biggest lifecycle hole.
28. **`FrameTick` pump is unguarded** — `DatabaseConnector.cs:55`. Every row callback runs inside it; one throwing handler aborts that tick's entire dispatch. Concrete throwing paths exist (binder `Bind` on a freed node after the never-removed `Connected += OnConnected`).
29. **Handler leaks across connect/scene boundaries** — `TableSubscriber.cs:74,82-83`, `TableBinderComponent.cs:82`: `Connected +=` and `MapConfig.OnInsert/OnUpdate` hooked forever, no `_ExitTree` unsubscribe anywhere. The first quit-to-menu button makes every re-entry double-subscribe and pile delegates onto freed nodes.
30. **`JoinWorld` has no failure path** — `LobbyComponent.cs:63-71` hides the lobby and swaps subscription waves unconditionally; a rejected reducer leaves you staring at nothing, soft-locked.
31. **`TokenAppend` matches Godot's own `--path`** — `DatabaseConnector.cs:38-53`: `StartsWith("--p") && Length > 3` matches `--path`, so every editor-run instance shares `_path` as its token key → same identity. The parallel-instance hack is broken precisely in the workflow it exists for.
32. **Spawner insert→delete race in one network batch** — `EntitySpawnerComponent.cs:96-114`: insert defers `AddChild` but registers in the dict immediately; a same-batch delete `QueueFree`s a not-yet-added node, then the deferred `AddChild` fires on it. Also captures `GetTree().CurrentScene` at insert time — an insert racing a scene change targets the old scene. And death/rejoin cleanup relies entirely on unsubscribe delivering row deletes, with no defensive despawn pass; if it doesn't, `ContainsKey` guards then suppress respawn of still-valid ids — permanently wedged entities.
33. **Death/rejoin relies on unsubscribe delivering deletes** — no defensive cleanup of spawned puppets on wave swap (`EntitySpawnerComponent.cs:60-71`).

### E. Bullet pipeline — the client spends its frame budget on bookkeeping

34. **Every enemy binds its own `BulletPatternEvent` handler** — `Enemy.cs:33-42,97-102`. One event = N delegate invocations (reflection-hooked, N−1 discarded on the id check). Correct dispatch is one filter at the BulletManager level; instead it's O(N) fan-out per event.
35. **Pattern events are dropped for just-spawned enemies** — spawner adds via `CallDeferred(AddChild)` (`EntitySpawnerComponent.cs:100-104`); the puppet's binder binds a deferred pass later. Any event in that 1–2 frame window is silently lost — and event tables are ephemeral, so there's no replay safety net. The boss's opening volley can simply not exist on a client.
36. **Prune runs per pattern event; volleys inflate the instance set** — `BulletSpawnerComponent.cs:58-66,121` does O(total live bullets) untyped `Call`s *per event*; `SpawnVolley/Shotgun/Explosion` route through `SpawnSingle` (`:245-300`) — a 20-pellet volley ×3 line = 60 tracked single-bullet instances, multiplying every prune, every ability query, every 3D-mirror frame.
37. **`EnemyBullets3DComponent` is the hottest per-frame path** — per frame, per instance: `get_amount_bullets` + `all_bullets_get_transforms` (full array alloc) + per-bullet `is_bullet_status_enabled`, plus a per-frame `GetComponent` lookup (`EnemyBullets3DComponent.cs:31-58`). Hundreds of untyped interop calls + allocs at 60 fps, scaling with the exact quantity a bullet-hell maximizes.
38. **Texture cache poisons itself permanently on a transient miss** — `BulletSpawnerComponent.cs:106-114` caches `null` forever; that texture id never resolves again this process. (TerrainComponent's `GetTexture` does it right — no null caching.)
39. **`BulletHitRouterComponent` fallback reports a self-hit for any unknown layer-2 area** — `BulletHitRouterComponent.cs:23-28`. Dead branch today; one new layer-2 area away from phantom self-damage on every bullet flyover.
40. **`bullet_max_collision_count` never set** — the plugin default of 1 is the only thing stopping curving bullets from multi-reporting the same victim (`BulletSpawnerComponent.cs:74-81`). Say it explicitly.
41. **Stale rotation data on the shared spawner resource** — `all_bullet_rotation_data` only ever set, never cleared (`BulletSpawnerComponent.cs:146-159`); one plugin behavior change away from wrong-curving bullets.

### F. Client mirrors, optimistic apply, and UI

42. **Optimistic ability apply fires before server acceptance** — `LocalPlayerInventoryComponent.TryActivateAbility` (`:174-254`) pre-checks the *mirrored* cooldown/charge row, casts, and applies bullet deletes locally. Two clicks inside one round-trip both pass the pre-check; the second reducer rejects server-side but the phantom delete already played. Same for the charge-release spell flash (`:113-133`).
43. **Grapple spams a reducer per tween frame** — `StartGrappleTween` calls `RequestMovementReport()` every frame (`:275-281`) — ~15 reducer calls per 0.25 s pull. The comment admits remotes snap anyway; the per-frame reports buy almost nothing.
44. **Four+ client mirrors of server logic with zero drift enforcement** — `EffectiveWeaponResolver` fold order, `AbilityPositionMultipliers`, `MaxAbilityTargetRange`, `IsChargeCapable`, charge constants in `AbilityChargeComponent`, `TorusMath`/`HexMath`/`HexSpiralIndex` (triplicated: both overlay components + server `hex.rs`). All guarded by "must change together" comments. Additionally the resolver `Fingerprint` covers item/toggle/enchant **ids** but not catalog content — an admin `upsert_item` changing damage doesn't change the fingerprint, so `CombatComponent` never re-resolves the live weapon.
45. **TerrainComponent rebuilds the world every frame the camera moves** — `_Process` triggers `RebuildVisibleInstances` on any `camMoved` (`TerrainComponent.cs:83-103`), i.e. continuously while walking: ~2166 tiles + decor iterated, ~9 dictionaries + lists allocated, `NearestCandidate` per hex, per frame. The dirty-flag burst collapse is good design; the camera-move path defeats it.
46. **Framework fragility (Low)** — `EntityRegistry.Get` returns first `IsInstanceOfType` (order-dependent), required-component validation is warnings-only, `GetSibling<T>()` silently returns null pre-registration.
47. **Facade/registry scans in per-frame paths** — `GameManager.Get<T>()` linear-scans the registry per call; `InterpolationComponent` pulls `LapQ/LapR` through the facade per entity per frame.
48. **Small stuff** — binder debug-log spam (`TableBinderComponent.cs:115,147`); lobby panels have no dedup and a `ulong→int` narrowing cast that breaks panel removal past `int.MaxValue` (`LobbyComponent.cs:51-61,80`); `ReportInterval = -1` "event-driven mode" documented but would fire every physics frame (`PositionSyncComponent.cs:31-33,151-153`; note the scene now reports at 1 Hz — docs saying 10 Hz are stale); dead duplicate `Scenes/UI/inventory_panel.tscn` nobody instances; `nearby_remote_players` includes the caller's own row (`player/views.rs:89-104`); dead server code: `PlayerDamageOutcome`/`EnemyDamageOutcome` payloads, `DebuffImmune` status + `DebuffImmunity` zone with no consult point, multi-row fold in `outgoing_damage_mult` for a unique (profile,kind), unused `by_profile_slot` index, unused `hex_shift`/`inv_hexmod._shift`, stale `hex_grid_overlay.gd` comment.

### What's actually good (so the fix plan doesn't break it)

- Event tables are real STDB `event` tables — ephemeral, no history replay, no storage growth. Replay flags are correctly off on all three event-table binders.
- Kill paths are transactional; no XP double-credit or teardown race within a reducer.
- `PlayerChunk` shielding view recompute from 10 Hz position writes is the right design.
- `spiral_chunk_index` is bijective (brute-force verified); wrap math and hex round-trips are sound.
- Reducer serializability makes the inventory span operations race-free; the span nudge/fits logic is correct including overlapping same-span moves.
- The slot/behavior independence in the item schema is the right call.

---

## Part 2 — Fix plan

Order: P0 trust layer → P1 server correctness → P2 client lifecycle → P3 bullet pipeline → P4 perf → P5 cleanup. Each phase is independently landable with its own verification. Server changes need `server/build.sh` (bindings regen + publish, wipes DB — seeds re-run, fine).

### P0 — Server validation layer (the trust holes)

All in `server/spacetimedb/src/`. New shared constants go in `main/global.rs`.

1. **Movement displacement clamp** (`player/reducers.rs:91-116`): compute wrap-aware distance between the stored position row and the reported `x/y`; reject (or clamp along the direction vector — prefer clamp to avoid rubber-banding legit lag) when it exceeds `base_speed × dt_since_last_report × MOVE_SLACK` (`MOVE_SLACK ≈ 2.0`, floor at ~64 units for first-report). Exempt `teleport_player` (it writes the row directly, already gated through ability validation). Grapple: the tween reports every frame within range — validated against the same clamp with the grapple range as budget; see P3 step for reducing that spam.
2. **`report_enemy_hit` validation** (`enemy/reducers.rs:137-149`): (a) wrap-aware range check, attacker position ↔ enemy, `HIT_VALIDATION_RADIUS`-style constant; (b) token-bucket rate limit per profile — store last-accepted timestamps in a small server-only table `PlayerHitRate` (profile_id PK, window_start, count), cap sustained hit rate at `equipped weapon fire_rate × effective_shot_count × HIT_RATE_SLACK (≈2)`; reject over budget. Damage math itself unchanged.
3. **`report_hit` rate limit** (`player/reducers.rs:157-178`): same `PlayerHitRate` table, separate counter, generous cap (bullet-hell: you genuinely get hit a lot — cap ~30/s). This bounds the StoreIncoming farming loop.
4. **StoreIncoming pools accumulate post-mitigation** (`status/methods.rs:249-251`): move the pool-add to after `compute_incoming_damage` so 1 HP taken ≠ 10 stored.
5. **Delete `control_bullets` as a public reducer** (`enemy/reducers.rs:112-135`): the ability path (`activate_ability` → `apply_ability_effect` → `spend_ability`) already appends `BulletControlEvent`s with full validation. Check for client callers first (grep client for `ControlBullets`/`control_bullets`) and reroute any to `activate_ability`. If a debug entry point is wanted, gate it behind `is_admin`.
6. **Range checks on `pickup_drop` and `take_from_chest`** (`player/reducers.rs:181-194`, `chest/mod.rs:66-82`): wrap-aware distance vs the position row, `INTERACT_RADIUS ≈ 128`. Regenerating chests additionally get a per-player take cooldown (small `ChestTakeCooldown` table: (profile_id, chest_id) → last_taken; 1 s).
7. **Kill the cooldown/charge laundry** (`player/reducers.rs:307-313` + `drop_item`/`pickup_drop` paths): runtime state must travel with the item in both directions. Concretely: on a span leaving the ability region, carry `cooldown_until`/`charges` onto the destination head row instead of dropping them (the rows already travel within-region); on entering, restore from the source row if it carried state, else initialize — never initialize when the source had state. For drop/pickup: stamp `cooldown_until`/`charges` onto the `LootDrop` row at drop, restore at pickup. Same-player round-trip then preserves the cooldown; a *different* player picking it up gets the residual cooldown too (acceptable, simpler).
8. **Delete the "Knight" backdoor** (`player/methods.rs:453-460`): remove the name check entirely. If devs want stats, `change_stats` behind admin exists.
9. **Gate `claim_admin`** (`main/admin.rs:15-31`): refuse unless `ctx.sender()` is in a hardcoded `ADMIN_IDENTITIES` list in `main/global.rs` (empty list = claim disabled, local dev fills it in) OR the server is local (no reliable in-module check — so the list is the mechanism; document in `server/AGENTS.md`).
10. **Fix witness flags** (`status/methods.rs:313-316`, `anticheat/`): count **distinct** `reported_by` identities (collect to a set, or store flags keyed (victim, reporter) with upsert semantics); sweep `player_hit_flag` rows older than N seconds in the 1 Hz tick. Keep flags advisory (reducing payouts) — full gating of `report_hit` is out of scope per the chosen trust model.

**Verify:** `cargo check`; `server/build.sh`; then via the spacetimedb-cli skill: movement clamp rejects a 10 000-unit jump; `report_enemy_hit` rejects out-of-range and floods; pickup/chest reject at distance; swap round-trip preserves cooldown; `claim_admin` refuses a non-listed identity; playtest skill smoke-tests normal play (no false positives from the clamps — tune `MOVE_SLACK`/`HIT_RATE_SLACK` if legit play trips them).

### P1 — Server correctness

11. **Cap the catch-up backlog** (`enemy/methods.rs:469-487`): before the catch-up loop, clamp `step_timer = max(step_timer, now - BACKLOG_CAP × interval)` with `BACKLOG_CAP ≈ 2`. Same clamp at spawn/phase-activation stamping (`methods.rs:175`). Net effect: re-entry fires at most a 2-shot burst, never a 3-second wall.
12. **Wrap-aware sim/aggro/chase** (`enemy/methods.rs:28-32, 47-56, 564-590`): route all three through the existing wrapped-distance helpers in `world/wrap.rs`; chase/flee direction vectors use the nearest-lap delta.
13. **Aggro staleness** (`enemy/reducers.rs:38-44` + `methods.rs:42-45`): when `aggro_target` is `Some` but `find_player_pos_by_id` returns `None` (or the target left sim range), run `recompute_aggro` immediately, not only on the rising edge.
14. **Lifecycle purge** (`main/lifecycle.rs:40-45`): call `purge_profile_statuses` + `purge_profile_zones` in the already-in-world recovery branch, matching `leave_world`/`client_disconnected`.
15. **LootDrop expiry sweep** (`player/reducers.rs:217-222`, `player/tables.rs:181`): enforce `expires_at` in `pickup_drop` (reject expired) + sweep expired rows in `tick_status_effects` (it already runs at 1 Hz; add one filtered pass).
16. **`despawn_enemy` calls `on_enemy_killed`** (`main/admin.rs:108-114`).
17. **Bounds-check attack indices** (`enemy/methods.rs:341, 276`): `phase_def.attacks.get(attack.attack_index)` — treat out-of-range as inactive (skip), never index.
18. **`place_building` ownership + range** (`world/reducers.rs:6-18`): range check vs position row + record `owner`; only owner (or admin) modifies. If buildings aren't a designed feature yet, make placement admin-only — simpler; decide at implementation time by checking whether any client UI calls it.
19. **XP overflow** (`player/methods.rs:36-43, 535`): `xp_for_level` in u64 (or `saturating_mul`), `compute_level` loop bound, `xp + amount` saturating.
20. **`use_item` role/span guard** (`player/reducers.rs:380-395`): reject items whose behaviors include `Ability` (can't consume a span head); document hotbar-vs-backpack as intentionally permissive (design allows eating from the backpack) or restrict to Hotbar/General — check what the client UI exposes and match it.
21. **Gate `withhold_outgoing` in DoT options** (`combat/mod.rs:224-229`): add the skip flag to `DOT_TICK_OPTIONS` alongside the existing conversion skip.

**Verify:** `cargo check`; `build.sh`; cli skill: spawn GorgonBoss, stay out of attack range 10 s, walk in — confirm no bullet wall; aggro an enemy, leave world — confirm it re-aggros/idles sanely; drop expiry; admin despawn leaves no orphan status rows (SQL `active_enemy_status_effect`). Playtest: normal boss fight feel unchanged.

### P2 — Client lifecycle & networking

All in `client/`. No server changes; verify with `dotnet build client/khvg.csproj` + godot-playtest.

22. **Disconnect handling** (`sstdbsdk/DatabaseConnector.cs:93-97`): `OnDisconnected` → emit a `Disconnected` signal; game scene tears down spawned entities, shows a "connection lost" panel with a reconnect button; reconnect reuses `AuthToken`, re-runs the subscription waves, and re-joins via the existing lobby path (no auto-rejoin of the world — route back through `main_menu.tscn`, simplest correct flow).
23. **Guard `FrameTick`** (`DatabaseConnector.cs:55`): try/catch around `Conn?.FrameTick()`; log the exception with context, keep pumping. Also fix the concrete thrower: skip binder `Bind` when the node is freed (`IsInstanceValid` check in `OnConnected`).
24. **Unhook handlers** (`TableSubscriber.cs:74,82-83`, `TableBinderComponent.cs:82`): `_ExitTree` removes `Connected +=`, `MapConfig` hooks, and disposes `lobbySub`/`gameSub` (`Unsubscribe` + `Dispose`). Binders unhook in `_ExitTree` too.
25. **JoinWorld failure path** (`LobbyComponent.cs:63-71`): don't hide the lobby until the `LocalPlayer` row actually arrives (or the reducer callback reports success — check what the C# SDK exposes for reducer results and use it); on rejection/timeout (≈5 s), re-show the lobby with an error label.
26. **Fix `TokenAppend`** (`DatabaseConnector.cs:38-53`): match `^--p\d+$` exactly (or exclude known Godot args); add a regression comment.
27. **Spawner race + scene capture + defensive cleanup** (`EntitySpawnerComponent.cs:56-114`): track pending-deferred adds; delete-before-add cancels the pending add instead of `QueueFree`ing; resolve the parent scene at deferred-execution time; add a `DespawnAll()` invoked on subscription-wave swap and `_ExitTree`, so stale ids can never wedge respawn.

**Verify:** playtest skill: join → kill the local server (`spacetime` stop) → confirm the disconnect panel appears and reconnect returns to menu; join/leave/join cycles don't leak puppets; two editor instances (`--p1`/`--p2`) get distinct identities (SQL `logged_in_player` count = 2).

### P3 — Bullet pipeline

28. **Centralize pattern-event dispatch** (kills the O(N) fan-out *and* the lost-opening-volley window): delete the per-enemy `BulletPatternEvent` binder from `default_enemy.tscn`/`Enemy.cs:33-42,97-102`; `BulletSpawnerComponent` (already lives at GameManager level, bound before any enemy spawns) owns the single binder and dispatches by `EnemyId` — looking up position via `EntitySpawnerComponent.GetEnemy`; if the puppet isn't in the tree yet, buffer the event for that enemy id for up to ~500 ms and flush on spawn (small dictionary<enemyId, List<event>>). `Enemy.cs` keeps only row-hp/stagger mirroring.
29. **Volleys spawn as multi-bullet instances** (`BulletSpawnerComponent.cs:245-300`): where the plugin's directional spawn supports an amount/spread parameter, spawn a volley as one instance of N bullets instead of N instances of 1 (check the BlastBullets2D API in `client/addons/blastbullets2d` for the multi-count spawn call; if the API genuinely requires one instance per bullet for `Line` stacks, keep per-stack single calls but document why). 
30. **Prune on interval, not per event** (`BulletSpawnerComponent.cs:58-66,121`): prune `LiveEnemyBullets` on a 1 s timer and on instance-count threshold, not inside `SpawnEnemyBullet`.
31. **Throttle `EnemyBullets3DComponent`** (`EnemyBullets3DComponent.cs:31-58`): cache the `BulletSpawnerComponent` ref at ready; tick at 30 Hz via accumulated delta, not every frame; reuse the transform array where the plugin allows.
32. **Texture cache: don't cache nulls** (`BulletSpawnerComponent.cs:106-114`) — mirror TerrainComponent's `GetTexture` behavior; log a warning on miss.
33. **HitRouter fallback** (`BulletHitRouterComponent.cs:23-28`): unknown layer-2 area → `PushWarning` + ignore, never `ReportHit`.
34. **Set `bullet_max_collision_count = 1` explicitly** in the enemy spawn-data resource setup (`BulletSpawnerComponent.cs:74-81`).
35. **Clear rotation data when `AngularSpeed == 0`** (`BulletSpawnerComponent.cs:146-159`).
36. **Optimistic-apply guard** (`LocalPlayerInventoryComponent.cs:174-254`): add a per-slot local `pendingUntil` timestamp set on cast; the pre-check rejects while pending (window ≈ expected RTT, cleared early when the echo arrives). Phantom double-deletes disappear; single-cast feel unchanged.
37. **Grapple reporting** (`LocalPlayerInventoryComponent.cs:265-289`): report at tween start and end only (plus the existing 1 Hz timer covers mid-tween); delete the per-frame `RequestMovementReport()`. Coordinate with P0 step 1 so the clamp accepts the start→end jump (grapple range as the displacement budget, or mark the end-report with a server-known "grapple just happened" window via the `AbilityCharge`/ability row — simplest: the server already validates Grapple on cast, so let `apply_ability_effect` for Grapple stamp a short server-side window in which one oversized movement report is accepted up to grapple range).
38. **Fingerprint catalog content** (`EffectiveWeaponResolver.cs:108-122` + `CatalogComponent`): `CatalogComponent` exposes a monotonically increasing `CatalogVersion` bumped on any item/enchantment row update; `CombatComponent` includes it in the fingerprint comparison.
39. **`ReportInterval = -1`** (`PositionSyncComponent.cs:31-33,151-153`): implement the documented event-driven mode (report on direction/speed change only) or delete the docs — implementer picks; also update stale "10 Hz" docs to the actual 1 Hz scene value (or set the scene back to 0.1 s — check git history for why it changed; movement at 1 Hz will make remotes rubber-band, so likely restore 0.1).

**Verify:** `dotnet build`; playtest: boss opening volley visible on spawn frame; volley-heavy fight frame-rate comparison (before/after — use `godot_profiler snapshot`); ability double-click produces exactly one delete; grapple feels identical; remotes track movement smoothly after the cadence fix.

### P4 — Server + client perf

40. **Delta-check row writes in the behavior tick** (`enemy/reducers.rs:103-108`, `enemy/methods.rs:356,363,369`): compare before update; skip `EnemyPhase`/`EnemyBehavior`/`Enemy`/`EnemyAttack` writes when byte-identical. Move the stunned-aggro recompute after the stun `continue` or gate it on the staleness check from step 13.
41. **Spawn pass via chunk index** (`enemy/methods.rs:612-614`): replace the per-region full scan with `enemies_near_point` on the region center (already btree-backed).
42. **Status tick scans** (`status/methods.rs:337,377,491,509,533,545`): add btree indexes where they don't exist (`active_enemy_status_effect.enemy_id` likely has one — check; `ground_zone.owner_id`, `active_status_effect.profile_id`) and replace full `iter()`s with index-filtered iteration where the selectivity justifies it; `on_enemy_killed` uses the enemy_id index. Keep `collect()` where mutation-during-iteration requires it.
43. **Position/rotation write delta-checks** (`player/reducers.rs:116,152`): skip the row update when nothing changed (rotation: epsilon ~0.5°). Client already gates rotation sends on facing change — this is the server-side belt to the client's suspenders.
44. **AOI-filter `nearby_remote_players_profiles`** (`player/views.rs:125-130`): semijoin against `nearby_remote_players`' chunk set (same `nearby_indices` OR-chain pattern), not all `LoggedInPlayer`. Exclude the caller's own row from `nearby_remote_players` (`:89-104`) while in there.
45. **Terrain rebuild throttling** (`TerrainComponent.cs:83-103`): rebuild on `_dirty` or when the camera's *chunk* changes (compute cam chunk via `HexMath.WorldToHex`/chunk math — cheap), not on every pixel of camera motion; pool/reuse the four grouping dictionaries + lists instead of allocating per rebuild. 
46. **Facade caching** (`GameManager.cs:39-45`, `InterpolationComponent.cs:86`): cache `LapQ/LapR` in a static once `MapConfig` arrives (TableSubscriber already owns them — expose as statics there and read directly); cache component refs at `OnEntityReady` instead of per-frame `GetComponent`.

**Verify:** `cargo check` + `build.sh`; playtest with `godot_profiler series` during a busy fight and while sprinting across chunk borders — row-write counts and frame times before/after.

### P5 — Cleanup & dead code

47. Delete `PlayerDamageOutcome`/`EnemyDamageOutcome` (`combat/mod.rs:25-37`) — the design note is gone; YAGNI.
48. Delete or implement `DebuffImmune` + `ZoneKind::DebuffImmunity` (`status/tables.rs:74,130-131`) — no consult point exists; delete unless a queued design doc claims it (grep `docs/Technical Design Docs/` first; if claimed, leave with a TODO pointing at the doc).
49. Simplify `outgoing_damage_mult` (`status/methods.rs:259-265`) to the single-row read the (profile, kind) uniqueness guarantees.
50. Remove the unused `by_profile_slot` index (`player/tables.rs:14`) — or use it in `get_slot` (`player/methods.rs:593-595`); pick one. Note index names are module-global — regen bindings after.
51. Remove `hex_shift`/`inv_hexmod._shift` (`world/hex.rs:7-9,28`); fix the stale `hex_grid_overlay.gd` comment (`hex.rs:33`) to name the actual C# consumers.
52. Delete binder debug prints (`TableBinderComponent.cs:115,147`) or gate behind an `[Export] bool Verbose`.
53. Lobby panel dedup + remove the `ulong→int` cast (`LobbyComponent.cs:51-61,80`): track panels in a `Dictionary<ulong, panel>`.
54. Delete `Scenes/UI/inventory_panel.tscn` (dead duplicate; grep all `.tscn`/`.cs` for references first — if anything references it, fix the reference to note `local_player.tscn` as the live copy).
55. Collapse the `HexSpiralIndex` triplication: both overlay components call `HexMath.SpiralIndex` (delete their local copies). Server copy stays (different language) — add a comment in `HexMath.cs` naming `world/hex.rs::spiral_chunk_index` as the sibling that must change in lockstep, and vice versa.
56. Mirror-drift hardening (the four+ "must change together" pairs): add a `client/Scripts/World/ServerConstants.cs` region header listing every mirrored constant with its server `main/global.rs` name in one place, and move `AbilityPositionMultipliers`/`MaxAbilityTargetRange`/charge constants onto it (single home, references update). No codegen — just one file to grep.
57. Update `AGENTS.md` files: stale movement-cadence references, the `control_bullets` removal, new tables (`PlayerHitRate`, `ChestTakeCooldown`, LootDrop fields), the centralized bullet dispatch (Enemy.cs no longer has a pattern binder), anything P0–P4 changes that the maps describe.

**Verify:** `cargo check`, `dotnet build`, `build.sh`, full playtest smoke (join, fight, loot, chest, abilities, boss).

### Execution notes

- Phases are independently mergeable; P0 is the only one that changes the trust surface and deserves its own publish + test pass.
- Every server phase ends with `server/build.sh` (bindings regen is required — the client references new/removed reducers and tables).
- Tune-first constants (`MOVE_SLACK`, `HIT_RATE_SLACK`, `INTERACT_RADIUS`, `BACKLOG_CAP`) go in `main/global.rs` with the measured-basis comment; playtest false-positives beat theoretical values.
- Out of scope per user decision: server-side bullet simulation, `report_hit` witness-gating.







