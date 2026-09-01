# 06 Movement & AOI Tables

## Assumed knowledge

- [[02 The Component Framework]] — the client's entity/component model and the `TableBinderComponent` pattern every row consumer here uses.
- [[03 Connection, Subscriptions & Views]] — reducers, views, the base/lobby/game subscription waves, and what `ReplayExistingRows` does on a binder.
- [[04 Lobby & Identity Tables]] — `join_world` / `leave_world`, the transition that creates and (deliberately) keeps the movement rows.
- [[00 Table Map]] — the per-table reference spine this doc transcludes from.

## The 30-second version

Five tables carry player movement. **`PlayerPosition`** (x/y + movement as an angle/scalar pair + `chunk_index`) is written by the client's 10 Hz `report_movement` reducer — after a wrap, a speed clamp, and a displacement clamp — and read back through two views: `local_player_position` (own row, drives spawn placement and desync correction) and `nearby_remote_players` (chunk-filtered, drives remote-player puppets). **`PlayerRotation`** holds the camera/facing angle on its own faster (~30 Hz) cadence via `report_screen_rotation`. **`PlayerChunk`** exists only so AOI views recompute on real chunk crossings instead of every movement report; the client never sees it. **`PlayerMovementState`** and **`PlayerDisplacementAllowance`** are server-only bookkeeping behind the anti-teleport clamp and the one-shot grapple exemption. Because the world is a torus of hex chunks, every position write passes through `world/wrap.rs`/`world/hex.rs`, and every client-side comparison of a stored position goes through nearest-wrapped-copy math (`TorusMath.NearestCandidate`).

## Flowcharts

- [[flowcharts/main-movement.canvas]] — this system's composed map.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Movement_subfolder/Movement_subfolder.canvas]] — `PositionSyncComponent` (reporting) and `InterpolationComponent` (puppet smoothing).
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/non_local_player_codefile/non_local_player_codefile.canvas]] — the remote-player puppet scene: two binders feeding interpolation.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]] — the server `player/` module: `report_movement`, the AOI views, and the scaffold/teardown helpers.

## Table changes

```sync
![[00 Table Map#^table-player-position{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-player-rotation{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-player-chunk{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 Table Map#^table-player-movement-state{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 Table Map#^table-player-displacement-allowance{seamless:true,title:false,marker:05.}]]
```

## Main body

### Why movement is five tables, not one

Each table exists because of a different write cadence or audience, and splitting them is what keeps the network and the view layer cheap:

- Position changes every 10 Hz report — but the **chunk** a player is in changes rarely, so ^table-player-chunk is written only on real chunk crossings and the AOI views key off it (see "The AOI views" below).
- Facing changes far more often than position in a mouse-aimed game, so rotation got its own table (^table-player-rotation) and its own ~30 Hz report path rather than bloating every position row write.
- The displacement clamp needs a per-player clock, and the grapple needs a per-player exemption token; both are anti-cheat bookkeeping no client may read, so ^table-player-movement-state and ^table-player-displacement-allowance are simply not `public`.

### The world model: a torus of hex chunks

Every mechanism below rests on two geometric facts, implemented in `server/spacetimedb/src/world/`:

1. **The continuous world is a torus.** Walking off one edge re-enters on the opposite one. [[server/spacetimedb/src/world/wrap.rs#wrap_world_pos|wrap_world_pos]] canonicalizes a position by checking whether its chunk coordinate wrapped the `chunk_cols × chunk_rows` grid and, if so, shifting x/y by whole **lap vectors** — the world-space period of one full grid traversal in q or r. Those lap vectors live on the `MapConfig` singleton row (`lap_q_x/y`, `lap_r_x/y`), and the client mirrors them into the `TableSubscriber.LapQ`/`LapR` statics from the same subscribed row.
2. **Positions bucket into hex chunks.** [[server/spacetimedb/src/world/hex.rs#world_to_chunk|world_to_chunk]] maps a world point to a lower-resolution hex coordinate (via the `hexx` crate), and [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index|spiral_chunk_index]] flattens that to a bijective `i64` — the `chunk_index` column every AOI-filterable table carries. (The client's `HexMath.SpiralIndex` is the lockstep sibling; both must change together.)

The consequence that shapes all the client code: a stored server position is one *canonical* representative, but on a torus there are infinitely many equivalent copies (canonical ± lap combinations). Any "how far is the server's copy from my node?" question must first pick the nearest copy — [[client/Scripts/World/TorusMath.cs#NearestCandidate|TorusMath.NearestCandidate]] on the client, [[server/spacetimedb/src/player/methods.rs#nearest_lap_copy|nearest_lap_copy]] on the server — otherwise entities near a map seam appear a map-width away.

### Outbound: what the local client reports

The local player is a `CharacterBody2D` (Godot's physics-body node) whose scene root script is [[client/Scripts/Players/Local/LocalPlayer.cs#_PhysicsProcess|LocalPlayer._PhysicsProcess]]: each physics frame it reads the WASD input vector, rotates it by the camera rig's yaw, and sets `Velocity = input * CurrentSpeed` before `MoveAndSlide()`. `CurrentSpeed` comes from [[client/Scripts/Components/Movement/PositionSyncComponent.cs#CurrentSpeed|PositionSyncComponent.CurrentSpeed]] — the server-resolved `PlayerData.base_speed` (via `LocalPlayer.BaseSpeed`, falling back to 100 before the first data row) multiplied by three client-side factors: the persistent Ctrl+scroll speed scale (`increase_move_speed`/`decrease_move_speed`, 10% steps clamped to [0.1, 1]), the hold-R `self_slow` factor (×0.4, bullet-hell focus mode), and any external slow set by `SetExternalSlow` (the cosmetic half of the P4 ability-charge SelfSlow — the server clamps for real). `LocalPlayer` is client-authoritative about where it stands; the server only validates after the fact.

The actual reporting lives in the same component, declared with its binder inline in `local_player.tscn` (node `PositionSyncComponent`, child `LocalPlayerPositionBinder` with `ReplayExistingRows = true`, `TableName = "LocalPlayerPosition"`; `RowInserted`/`RowUpdated` wired to `OnPositionRowInserted`/`OnPositionRowUpdated` — that inline declaration is the live wiring):

- [[client/Scripts/Components/Movement/PositionSyncComponent.cs#ReportNow|ReportNow]] fires on a 0.1 s timer (`ReportInterval`) and sends the node's `GlobalPosition` plus **actual movement** as an angle + scalar: `velocity.Angle()` (kept from the last moving frame while idle) and `velocity.Length()` (0 when idle). It deliberately does *not* invent velocity from facing — an earlier version did, and every idle player drifted on remote screens (the "constant-drift bug" the comments reference). Movement-key edges, speed-scale changes, and self_slow edges set `reportNextFrame` so starts/stops reach remotes immediately instead of up to 100 ms late. `ReportInterval = -1` selects an event-driven mode where only those edges report — off by default in the live scene, which leaves the export at its 0.1 default.
- Screen rotation reports on its own faster timer (~30 Hz, `RotationReportInterval = 0.033`) and only when the facing angle moved past `RotationEpsilon`, calling `ReportScreenRotation`. Rotation rides its own reducer because camera/facing updates at 10 Hz look choppy, but paying that rate on the position row would triple its write volume.

### Inbound server-side: `report_movement` step by step

A **reducer** is a transactional server function the client calls by name — think of it as the only legal write path to a table. [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] runs this gauntlet per report:

1. **Guard** — `require_in_world` rejects callers without a `LoggedInPlayer` row, so a logged-out client can't move its ghost.
2. **Wrap** — the reported x/y passes through `wrap_world_pos` first, *because* the client's own frame is continuous and unbounded; the server stores only canonical positions.
3. **Speed clamp** — the reported `movement_speed` clamps into `[0, speed_cap]`, where `speed_cap` is the resolved `PlayerData.base_speed` (gear/buffs included) tightened by the minimum `SelfSlow` factor while any such status row exists (the P4 charge). Anything above the cap is a cheat or a bug and gets silently pulled down.
4. **Displacement clamp** — the anti-teleport core. The distance from the stored row to the nearest wrapped copy of the report (`nearest_lap_copy`) may not exceed `speed_cap × dt × MOVE_SLACK` (×2.0), floored at `MIN_REPORT_DISPLACEMENT` (64 units, also the entire budget for the very first report), where `dt` is measured off the ^table-player-movement-state clock via [[server/spacetimedb/src/enemy/methods.rs#seconds_since|seconds_since]]. Over-budget reports are **clamped along their own direction, not rejected** — rejecting would rubber-band legitimate lag spikes.
5. **Grapple exemption** — before clamping, an oversized report checks ^table-player-displacement-allowance: a live, unexpired row legitimizes one jump up to `max_distance` and is consumed by it; an expired row is swept on contact (see "Teleport and grapple" below).
6. **Re-wrap, chunk, delta-checked write** — the (possibly clamped) position is wrapped again, its `chunk_index` recomputed via `world_to_chunk` + `spiral_chunk_index`, and the row is rewritten **only if something a subscriber sees changed** — position, direction, speed, or chunk. A standing-still player produces zero row updates at 10 Hz, which is why the nearby-views don't churn.
7. **Restamp the clock** — the `PlayerMovementState` row updates (or inserts on first report) so the next report's budget measures from here. This is also where radius-wager status stamping happens (out of scope here — see [[11 Status, Zone & Charge Tables]]).
8. **Chunk row, conditionally** — ^table-player-chunk updates **only when `(chunk_q, chunk_r) actually changed`**. That conditional is the entire reason the table exists (next section).

[[server/spacetimedb/src/player/reducers.rs#report_screen_rotation|report_screen_rotation]] is simpler by design: it mirrors `chunk_index` off the current position row (so the rotation AOI view filters identically to the position one) and skips the write when the angle moved less than `ROTATION_REPORT_EPSILON` (0.0087 rad ≈ 0.5°) — but a chunk crossing always writes, keeping the mirror exact.

### The AOI views: `PlayerChunk` as the recompute key

A **view** in SpacetimeDB 2.x is a named, server-defined query the client subscribes to instead of writing SQL; a *per-caller* view (`ViewContext`) can read the caller's identity and other tables before building its query. The area-of-interest (AOI) views — "what's near me" — all build on one helper, [[server/spacetimedb/src/player/views.rs#nearby_indices_from_chunk|nearby_indices_from_chunk]]: look up the caller's `LoggedInPlayer` row → their ^table-player-chunk row → read `MapConfig` directly (a `ViewContext` can't call the `MapConfig::load` helper, so it defaults to 1×1) → return the spiral indices of the caller's chunk plus `rings` neighbors, wrapped around the grid, via [[server/spacetimedb/src/world/aoi.rs#surrounding_chunk_indices|surrounding_chunk_indices]]. The default ring count is `DEFAULT_AOI_CHUNK_RADIUS` = 2, i.e. up to 19 chunks.

The deliberate design point: the chunk set derives from `PlayerChunk`, **not** from `PlayerPosition`. A view re-evaluates when a table it reads changes; keying the filter off the position row would re-derive every player's nearby set on every 10 Hz report, while keying it off the chunk row re-derives only when someone actually crosses a chunk boundary. The comment on the table and the `server/AGENTS.md` rule both say it plainly: don't "simplify" an AOI view to read `PlayerPosition`.

Two query-builder idioms recur in every AOI view (the builder has no `IN` and a view must always return a query): the chunk list becomes an **OR-chain** of `chunk_index.eq(idx)` predicates, and a caller with no chunk row gets a **sentinel query** (`profile_id.eq(u64::MAX)`) that matches nothing instead of an early empty return. The three movement views:

- [[server/spacetimedb/src/player/views.rs#local_player_position|local_player_position]] — just the caller's own row (sentinel `u64::MAX` when logged out). No AOI; the owner always sees themselves.
- [[server/spacetimedb/src/player/views.rs#nearby_remote_players|nearby_remote_players]] — the chunk OR-chain **and** `profile_id != own`, plus a `left_semijoin` against `logged_in_player` so logged-out ghost rows (see Known gaps) never stream. The own-row exclusion matters because the local player already has `local_player_position` — without it, the spawner would instantiate a puppet of yourself.
- [[server/spacetimedb/src/player/views.rs#nearby_remote_player_rotations|nearby_remote_player_rotations]] — the identical filter over `PlayerRotation`, which is exactly why the rotation row mirrors the position row's `chunk_index`.
- [[server/spacetimedb/src/player/views.rs#nearby_remote_players_profiles|nearby_remote_players_profiles]] — the profiles of the same nearby set, via a **right semijoin**: the query builder can't filter the right side of a left semijoin, so the chunk-filtered position scan drives and the matching `PlayerProfile` rows come out. This is what lets a fresh puppet resolve its sprite without subscribing to every profile in the world.

All four sit in the `GameTables` wave in [[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber]] (subscribed on world join, alongside `NearbyEnemies`, terrain, and the event tables). `nearby_loot_drops` and the enemy/terrain/chest AOI views reuse the same helper — same filter, other tables.

### Inbound client-side, local: placement and desync correction

The local player's own row comes back through `local_player_position` into the `LocalPlayerPositionBinder` wired in `local_player.tscn`. Because the binder replays existing rows, the first delivery is the initial placement: [[client/Scripts/Components/Movement/PositionSyncComponent.cs#OnPositionRowInserted|OnPositionRowInserted]] hard-sets `GlobalPosition` to the row (the spawn-at-origin row `try_scaffold_profile` wrote). Later updates land in [[client/Scripts/Components/Movement/PositionSyncComponent.cs#OnPositionRowUpdated|OnPositionRowUpdated]], which is deliberately *not* an echo-follower: the local physics frame stays continuous and unbounded, so the handler only asks "is even the nearest wrapped copy of the server's position farther than `WrapSnapThreshold` (50 units)?" — via `TorusMath.NearestCandidate` against the `LapQ`/`LapR` statics. Only then does it snap (a real desync: lag burst, clamp correction, or a server-side teleport). Routine torus wraps never trigger it, because a wrap always has a near copy.

### Inbound client-side, remote: puppets

A **scene** (`.tscn` file) is a saved node tree; spawning a remote player means instantiating `non_local_player.tscn` and adding it to the live tree. That decision is made by `EntitySpawnerComponent` (declared inline in `game.tscn` with its five child binders — the live wiring — and `NonLocalPlayerScene` exported to `non_local_player.tscn`):

- Its `NearbyRemotePlayersBinder` (`TableName = "NearbyRemotePlayers"`, `ReplayExistingRows = true`, insert/delete wired in `game.tscn`) fires [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnNearbyRemotePlayerInsert|OnNearbyRemotePlayerInsert]]: skip if the row is our own identity (`DatabaseConnector.IsLocal` — belt-and-braces on top of the view's own exclusion) or already tracked, else instantiate a `RemotePlayer`, stamp `PlayerId`/`ProfileId`, place it at the row position, and defer-add it (a delete arriving before the deferred add fires cancels the add — see [[02 The Component Framework]]). [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnNearbyRemotePlayerDelete|OnNearbyRemotePlayerDelete]] frees the puppet — which is how walking out of someone's AOI or their logout makes them vanish.
- Each puppet then feeds itself. `non_local_player.tscn` declares two binders with `RowUpdated` wired (replay off — they're per-puppet updaters, and replay would deliver the whole cache to every instance): `NearbyRemotePlayersBinder` → [[client/Scripts/Players/Remote/RemotePlayer.cs#OnPositionRowUpdated|RemotePlayer.OnPositionRowUpdated]], which filters to its own `PlayerId`, **reconstructs velocity** as `Vector2.FromAngle(movement_direction) * movement_speed` (this is what the angle/scalar encoding is *for*), and hands position+velocity to `InterpolationComponent.SetTarget`; and `NearbyRemotePlayerRotationsBinder` → [[client/Scripts/Players/Remote/RemotePlayer.cs#OnRotationRowUpdated|OnRotationRowUpdated]] → `SetScreenRotationTarget`. The puppet's sprite comes from a one-shot `conn.Db.NearbyRemotePlayersProfiles.Iter()` lookup in `_Ready`, resolved through the texture catalog by [[client/Scripts/Components/Visual/RemoteVisualComponent.cs#SetTexture|RemoteVisualComponent.SetTexture]].
- The smoothing all lives in [[client/Scripts/Components/Movement/InterpolationComponent.cs#SetTarget|InterpolationComponent]] (instanced in the scene with `WrapSnapThreshold = 50`). Every frame it advances the last target by `velocity × time-since-update` — **dead reckoning** across the 100 ms report gaps — picks the nearest wrapped copy of that extrapolated target, and lerps the node toward it at `LerpSpeed × delta`, with the same nearest-copy snap as the local path for real desyncs (which is how a grappled or teleported remote "jumps" correctly). Its `Moving` flag (>1 unit from target) is what `RemoteVisualComponent` reads to play Walk vs. Idle.

So the full remote loop is: their `ReportNow` → server `report_movement` (clamped) → `PlayerPosition` row → `nearby_remote_players` chunk filter → our binder → `RemotePlayer` → `InterpolationComponent` — with rotation streaming in parallel on its own table at ~30 Hz.

### Teleport and grapple: the two legitimate clamp-breakers

The displacement clamp would reject both of the game's sanctioned fast moves, so each has an explicit bypass:

- **Teleport** (P4 ability) runs entirely server-side: the Teleport arm of [[server/spacetimedb/src/player/reducers.rs#apply_ability_effect|apply_ability_effect]] clamps the cursor into range and calls [[server/spacetimedb/src/player/methods.rs#teleport_player|teleport_player]], which writes position + chunk exactly like `report_movement` but skips the clamp and **restamps the movement-state clock** — without that restamp, the next ordinary report would measure `dt` from before the teleport and inherit a huge budget. The client needs no special handling: the echoed row lands in the usual desync snap (>50 units ⇒ snap) locally and in the interpolation snap on remotes.
- **Grapple** is client-tweened, so the server can't write the path — it writes permission instead. The Grapple arm of `apply_ability_effect` stamps ^table-player-displacement-allowance (`max_distance` = the grapple range, `GRAPPLE_ALLOWANCE_SECONDS` = 1 s expiry), and `report_movement` spends it on the first oversized report. The client half of the contract lives in `PositionSyncComponent`: while `LocalPlayer.ExternalMovementActive` is set (by the grapple tween in `LocalPlayerInventoryComponent`), timed and movement-edge reports are suppressed — a mid-tween report would read as an oversized jump and burn the one-shot allowance before the landing report — and the tween calls [[client/Scripts/Components/Movement/PositionSyncComponent.cs#RequestMovementReport|RequestMovementReport]] at tween start and end only.

### Lifecycle: where the rows come from and where they (don't) go

[[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]] (called from `join_world`) inserts the whole movement row set on first join and — because `leave_world` keeps the position/chunk rows — **resets them to the world origin on every rejoin**, so a returning player never resumes a stale spot. [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] (death cleanup and `delete_profile`) is the **only** delete path for all five tables. What happens in between — logout with rows left behind — is a known gap, below.

## Known gaps / stubs

- **Ghost position rows after logout.** `leave_world`/logout deletes the `LoggedInPlayer` row but not the ^table-player-position / ^table-player-chunk (or rotation/movement-state) rows — cleanup happens only in `teardown_profile` (death or `delete_profile`). The system copes by filtering rather than cleaning: the AOI views `left_semijoin` `logged_in_player`, and the enemy sim's proximity helpers ([[server/spacetimedb/src/enemy/methods.rs#has_player_in_simulation_range|has_player_in_simulation_range]], [[server/spacetimedb/src/enemy/methods.rs#find_nearest_player_id|find_nearest_player_id]]) re-check `logged_in_player` per row so enemies don't aggro or spawn-camp a ghost. It works, but every consumer of `PlayerPosition` must remember the check.
- **Leftover debug print.** `PositionSyncComponent.OnPositionRowUpdated` still `GD.Print`s a `[Desync]` line on every hard snap — useful while tuning `WrapSnapThreshold`, noise in production logs.

## Where to go next

The speed cap being clamped here is resolved by `recompute_stats` into `PlayerData.base_speed` — [[05 Player Data & Progression Tables]]. The chunk grid, lap vectors, and `MapConfig` singleton the wrap math depends on are [[07 Terrain & World Tables]]; the same AOI helper drives enemy visibility in [[08 Enemy Tables]].
