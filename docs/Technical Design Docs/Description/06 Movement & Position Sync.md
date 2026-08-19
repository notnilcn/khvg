# 06 Movement & Position Sync

## Assumed knowledge

- [[02 The Component Framework]] — what a `*Component` node is, how it registers with its `IEntity` root, and what a `TableBinderComponent` does (re-exposes subscribed table row events as Godot signals).
- [[03 Boot & Connection]] — how the client connects and what a subscription is.
- [[05 Joining the World]] — how `join_world` scaffolds the position rows this doc streams, and how `EntitySpawnerComponent` turns row inserts into spawned scenes.
- [[00 End-to-End Timeline Flowchart]] — the whole-timeline view this doc's steps are transcluded from.

## The 30-second version

Movement is **client-authoritative physics with server-canonicalized reporting**. Your `LocalPlayer` moves itself with plain Godot physics — the server is never in the input loop. Several times a second (and instantly on every start/stop), the client calls the `report_movement` reducer with its position plus its *actual* movement encoded as an angle and a scalar speed. The server wraps the position onto the torus, recomputes which hex chunk you're in, clamps the speed to your gear-resolved cap, and stores the row. Every other client subscribes to an area-of-interest view of those rows, spawns a puppet per nearby player, and each frame extrapolates the puppet's last reported position by its last reported velocity (dead reckoning) while lerping toward it. Facing/camera rotation streams on its own faster channel in a separate table. Because the world is a torus, all distance and position comparisons on both sides go through a "nearest wrapped copy" trick so nothing ever snaps when you cross a map seam.

## Flowcharts

- [[flowcharts/main-movement.canvas]] — the composed movement flow (client movement components, both player scenes, server `player/` + `world/`).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Movement_subfolder/Movement_subfolder.canvas]] — `PositionSyncComponent` + `InterpolationComponent` symbol-level deep dive.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/World_subfolder/TorusMath_codefile/TorusMath_codefile.canvas]] — the torus nearest-candidate math.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/views_codefile/views_codefile.canvas]] — the AOI views (`nearby_remote_players`, `nearby_remote_player_rotations`, `nearby_indices`).

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^move-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^move-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^move-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^move-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^move-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^move-7{seamless:true,title:false,marker:07.}]]
```

## Main body

### The shape of the system: report out, mirror in

Position sync runs in two asymmetric directions, and keeping them straight is the key to reading the code.

**Outbound (your client → server):** your machine owns your character's physics entirely. Nothing asks the server "may I move here?". Instead, after the fact, the client *reports* where it ended up by calling a **reducer** — SpacetimeDB's only mutation path, a transactional remote function that returns nothing. The two movement reducers are [[server/spacetimedb/src/player/reducers.rs#report_movement#1|report_movement]] (position + movement state) and [[server/spacetimedb/src/player/reducers.rs#report_screen_rotation#1|report_screen_rotation]] (facing angle only).

**Inbound (server → every client):** clients never query for positions. They *subscribe* to **views** — per-client filtered queries the server keeps live — and the SDK mirrors matching rows into a local client cache, firing insert/update/delete callbacks as rows change. Your own row arrives through [[server/spacetimedb/src/player/views.rs#local_player_position#1|local_player_position]]; everyone else's through the AOI views [[server/spacetimedb/src/player/views.rs#nearby_remote_players#1|nearby_remote_players]] and [[server/spacetimedb/src/player/views.rs#nearby_remote_player_rotations#1|nearby_remote_player_rotations]]. On the client, no script hooks those callbacks directly: each consuming component owns a child `TableBinderComponent` that re-exposes one table's row events as editor-wireable Godot signals, and the wiring lives in the scene files (`local_player.tscn`, `non_local_player.tscn`).

Three server tables carry the whole system, all in [[server/spacetimedb/src/player/tables.rs#PlayerPosition#1|player/tables.rs]]:

- `PlayerPosition` — one row per profile: `x`, `y`, `movement_direction` (angle), `movement_speed` (scalar), and a btree-indexed `chunk_index`. Updated on every `report_movement` call.
- `PlayerRotation` — one row per profile: `screen_rotation` plus a `chunk_index` mirrored from the position row. Split into its own table *because* rotation needs a faster cadence than movement — if it rode on `PlayerPosition`, either camera-facing would lag at the movement rate or the position row would churn at the rotation rate.
- `PlayerChunk` — one row per profile: just the chunk coordinates `(chunk_q, chunk_r)`, rewritten **only when the chunk actually changes**. This row exists so the AOI views can key off it and recompute only on real chunk crossings instead of on every movement report — the table's own comment says exactly this, and the server `AGENTS.md` elevates it to a rule: never "simplify" an AOI view to read `PlayerPosition`.

All three rows are created at join time by [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile#1|try_scaffold_profile]], which places every new profile at world origin `(0, 0)` with zero movement and the chunk containing the origin.

### Local movement: plain physics, server-priced speed

```sync
![[00 End-to-End Timeline Flowchart#^move-1{seamless:true,title:false,marker:01.}]]
```

Godot runs a fixed-rate physics step and calls `_PhysicsProcess` on every physics body each tick. [[client/Scripts/Players/Local/LocalPlayer.cs#_PhysicsProcess#1|LocalPlayer._PhysicsProcess]] is the entire input-to-motion pipeline: read the four movement actions as a vector, rotate it by the camera rig's yaw so "up" on the keyboard always means "up" on screen, multiply by a speed, assign the body's `Velocity`, and call `MoveAndSlide()` — Godot's built-in "move and resolve wall collisions" for `CharacterBody2D`. While any movement key is held, the node's `Rotation` is pinned to the camera yaw (so the sprite faces screen-up relative to the camera).

The speed is the one server-flavored ingredient, and it's worth unpacking *because* it shows how the server stays authoritative without being in the input loop. `LocalPlayer` reads `positionSync?.CurrentSpeed` each physics frame, where [[client/Scripts/Components/Movement/PositionSyncComponent.cs#CurrentSpeed#1|PositionSyncComponent.CurrentSpeed]] computes:

```
CurrentSpeed = BaseSpeed × speedScale × (SlowHeld ? SelfSlowFactor : 1)
```

- `BaseSpeed` is the server-resolved move-speed cap from the `PlayerData.base_speed` row (mirrored locally by `LocalPlayerDataComponent`), defaulting to the [[server/spacetimedb/src/main/global.rs#BASE_SPEED#1|BASE_SPEED]] constant of 100 world units/sec. Items raise it via the modifier-only `StatKind.BaseSpeed`; it is never allocatable with skill points. The `100f` fallback in code only applies before the first data row arrives.
- `speedScale` is a *persistent, client-side* player setting: Ctrl+scroll (`increase_move_speed`/`decrease_move_speed`) steps it by `SpeedStep` (0.1), clamped to `[MinSpeedScale, 1]` = [0.1, 1]. It can only ever *lower* you below the server cap.
- `self_slow` (hold R) multiplies by `SelfSlowFactor` = 0.4 — the classic bullet-hell "focus mode" for fine dodging.

So the server sets the ceiling, the player chooses how far under it to cruise, and the local physics integrate the result with zero network involvement.

### Reporting: angle + speed, on a timer plus on edges

```sync
![[00 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:02.}]]
```

[[client/Scripts/Components/Movement/PositionSyncComponent.cs#ReportNow#1|PositionSyncComponent.ReportNow]] is the outbound heart. Its most important design decision is *what it sends*: not a velocity vector, and never a value derived from facing, but the body's **actual** current `Velocity` decomposed into an angle (`velocity.Angle()`) and a scalar (`velocity.Length()`). Two details fall out of that:

1. **Zero speed when idle, last direction retained.** When you stop, speed reports as 0 but `lastMovementDirection` keeps its previous value (the `if (speed > 0.001f)` guard only overwrites it while moving). Remote puppets reconstruct `velocity = direction × speed` from the pair for dead reckoning, so a puppet of an idle player gets a zero vector and stands still. The code comments on both sides record why: an earlier version invented velocity from the facing angle, which meant idle puppets kept drifting in whatever direction they faced — the "constant-drift bug".
2. **The report is a state snapshot, not a delta.** Each call carries the full current `(x, y, direction, speed)`, so a lost or delayed packet costs nothing — the next report fully re-syncs.

Reports fire from [[client/Scripts/Components/Movement/PositionSyncComponent.cs#_PhysicsProcess#1|PositionSyncComponent._PhysicsProcess]] on two triggers:

- **A timer.** `reportTimer` accumulates `delta` and fires `ReportNow` every `ReportInterval` seconds. The default is 0.1 s (10 Hz), but `local_player.tscn` overrides the export to **1.0 s** — the shipped cadence is 1 Hz.
- **Edges, via `reportNextFrame`.** Movement-key presses *and* releases, Ctrl+scroll speed changes, and `self_slow` press/release all set a flag that triggers a report on the next physics frame. The one-frame deferral exists because `LocalPlayer._PhysicsProcess` integrates `Velocity` earlier in the frame than this child component runs — reporting immediately would send the *previous* frame's velocity, so a key release would report "still moving". Deferring one frame guarantees the report reflects the new input. This is what makes starts and stops appear on remote screens instantly instead of up to a second late.

`ReportInterval` also has an event-driven mode: set it to `-1` and the timer comparison never fires, leaving key edges as the only reports (a held key then sends nothing until release). No shipped scene uses it — `local_player.tscn` sets 1.0 — but the mode is implemented and documented in the export's comment.

### The rotation side-channel

```sync
![[00 End-to-End Timeline Flowchart#^move-4{seamless:true,title:false,marker:04.}]]
```

Facing (which way the character and its camera are turned) streams on its own faster timer *because* a rotating camera makes stale facing far more visible than stale position. A second accumulator fires `ReportScreenRotation(node.Rotation)` every `RotationReportInterval` (0.033 s, ~30 Hz) — but only when the facing actually changed by more than `RotationEpsilon` (0.001 rad) since the last report, so a stationary player costs zero rotation traffic. The first report after spawn always fires (`lastReportedScreenRotation` starts as `NaN`, which fails every epsilon comparison).

### Server: canonicalize, clamp, store

```sync
![[00 End-to-End Timeline Flowchart#^move-3{seamless:true,title:false,marker:03.}]]
```

The server treats every report as untrusted input and runs three normalizations in [[server/spacetimedb/src/player/reducers.rs#report_movement#1|report_movement]]:

1. **Wrap.** The reported `(x, y)` passes through [[server/spacetimedb/src/world/wrap.rs#wrap_world_pos#1|wrap_world_pos]], which maps any continuous position back into the canonical lap of the torus (details in the torus section below). The server *stores* only canonical positions; clients handle their own local continuity.
2. **Chunk recompute.** [[server/spacetimedb/src/world/hex.rs#world_to_chunk#1|world_to_chunk]] converts the wrapped position to a chunk coordinate (it maps world → hex via the `hexx` crate, then the hex to its parent chunk at `chunk_hex_radius` resolution), and [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index#1|spiral_chunk_index]] flattens the 2D chunk coordinate into a single bijective `i64` — the `chunk_index` column every AOI view filters on.
3. **Speed clamp.** The reported `movement_speed` is clamped into `[0, base_speed]` where `base_speed` is the resolved `PlayerData.base_speed` (gear included), falling back to `BASE_SPEED` if the data row is somehow missing. Since the client's own `CurrentSpeed` formula can never exceed the cap, anything above it is a cheat or a bug — and the clamp silently neutralizes it rather than rejecting the whole report (position is still accepted; teleport-reporting is not policed at all — the server has no movement validation beyond this).

The reducer then updates the `PlayerPosition` row, and — the load-bearing subtlety — rewrites the `PlayerChunk` row **only if `(chunk_q, chunk_r)` actually changed**. Because every AOI view keys off `PlayerChunk` (see below), this is the line that makes subscription churn happen on chunk crossings instead of on every report.

[[server/spacetimedb/src/player/reducers.rs#report_screen_rotation#1|report_screen_rotation]] is simpler: it updates `PlayerRotation.screen_rotation`, copying `chunk_index` from the current position row so the rotation AOI view filters identically to the position one. The two tables never disagree about which chunk you're in *because* rotation never computes its own chunk.

### The torus: why nothing ever snaps at the edge

The world is a finite hex-chunk grid (`DEFAULT_CHUNK_COLS` × `DEFAULT_CHUNK_ROWS` = 6 × 6 chunks) whose *continuous* space wraps: walk off one edge and you reappear on the opposite one. Topologically it's a torus; the hex grid itself is not wrapped (chunk coordinates wrap via [[server/spacetimedb/src/world/wrap.rs#wrap_chunk_coords#1|wrap_chunk_coords]]'s `rem_euclid`, and continuous positions wrap by whole "lap" vectors).

The consequence that shapes all the code: **a stored canonical position has infinitely many equivalent on-screen copies** — itself, plus itself shifted by ±`LapQ`, ±`LapR`, and the four diagonal combinations. The lap vectors are the world-space displacement of one full grid traversal in each axis, precomputed server-side and published on the singleton [[server/spacetimedb/src/world/tables.rs#MapConfig#1|MapConfig]] row (`lap_q_x/y`, `lap_r_x/y`) precisely so clients don't re-derive the hex-of-hexes math. The client's [[client/sstdbsdk/TableSubscriber.cs#LapQ#1|TableSubscriber]] mirrors them into `LapQ`/`LapR` from the subscribed `MapConfig` row, and `GameManager` re-exposes them as statics.

Both sides then use the same "nearest candidate" trick wherever a stored position meets a live reference point:

- **Client:** [[client/Scripts/World/TorusMath.cs#NearestCandidate#1|TorusMath.NearestCandidate]] checks the canonical position plus all 8 lap-shifted copies and returns whichever is closest to a reference (the local player, the camera, a puppet). Rendering and interpolation always work with the nearest copy, so crossing a seam looks continuous and nothing teleports across the map.
- **Server:** [[server/spacetimedb/src/world/wrap.rs#wrapped_distance_sq#1|wrapped_distance_sq]] mirrors the same 9-candidate check (a 3×3 loop over lap combinations) wherever it compares distances between two world points — e.g. an offset from a chunk center can cross a seam even when the center itself is canonical, and plain Euclidean distance would silently never match.

The threshold that decides "routine wrap" from "real desync" is `WrapSnapThreshold` (50 world units): if even the *nearest* copy of the server's position is farther than that from where the client thinks things are, it's lag, rubber-banding, or a teleport — snap; otherwise keep interpolating.

### AOI: who sees your rows

Your position row is `public`, but no client downloads the whole table — *because* every movement-relevant view filters to the caller's area of interest. The shared helper [[server/spacetimedb/src/player/views.rs#nearby_indices#1|nearby_indices]] looks up the caller's `PlayerChunk` (deliberately not `PlayerPosition` — see above) and expands it to the surrounding chunk ring via [[server/spacetimedb/src/world/aoi.rs#surrounding_chunk_indices#1|surrounding_chunk_indices]]: `Hex::range(rings)` around the caller's chunk with [[server/spacetimedb/src/main/global.rs#DEFAULT_AOI_CHUNK_RADIUS#1|DEFAULT_AOI_CHUNK_RADIUS]] = 2 (19 chunks: `1 + 3R(R+1)`), each coordinate wrapped onto the grid and converted to its spiral `chunk_index`, deduplicated.

[[server/spacetimedb/src/player/views.rs#nearby_remote_players#1|nearby_remote_players]] then filters `PlayerPosition` to rows whose `chunk_index` is in that set. Two SpacetimeDB 2.x view idioms are on display, *because* the query builder has no `IN` operator and a view must always return a query:

- The `IN` is an **OR-chain**: seed with `chunk_index == indices[0]`, fold `.or(chunk_index == idx)` for the rest.
- The empty case returns a **sentinel query** (`profile_id == u64::MAX`, matching nothing) built up front, instead of returning early with nothing.

A `left_semijoin` against `LoggedInPlayer` keeps only positions of players currently in the world — which is what stops the "ghost rows" of logged-out players (their `PlayerPosition` rows persist until teardown; see [[13 Disconnect & Teardown]]) from spawning puppets. Note the semijoin does **not** exclude the caller: your own position row is in the result set too, and the *client* filters self out at spawn time — [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnNearbyRemotePlayerInsert#1|EntitySpawnerComponent.OnNearbyRemotePlayerInsert]] skips any row whose `PlayerId` passes the `DatabaseConnector.IsLocal` identity check. [[server/spacetimedb/src/player/views.rs#nearby_remote_player_rotations#1|nearby_remote_player_rotations]] is the same view over `PlayerRotation` — identical filter, faster-changing table.

The net effect, combined with the `PlayerChunk` write-on-crossing rule: standing still costs you one 1 Hz row update visible to a fixed set of subscribers, and crossing a chunk boundary silently re-computes everyone's AOI — inserts for the chunks you gained, deletes for the ones you lost, on every nearby client.

### Remote puppets: reconstruct, extrapolate, lerp

```sync
![[00 End-to-End Timeline Flowchart#^move-5{seamless:true,title:false,marker:05.}]]
```

A remote player exists on your screen as a `RemotePlayer` — the `Node2D` root of `non_local_player.tscn`, spawned by `EntitySpawnerComponent` when a `NearbyRemotePlayers` row inserts (doc 05 covers spawning). It's deliberately thin glue; the scene wires two child binders (`NearbyRemotePlayersBinder`, `NearbyRemotePlayerRotationsBinder`, both declared inline in `non_local_player.tscn`) whose `RowUpdated` signals land on [[client/Scripts/Players/Remote/RemotePlayer.cs#OnPositionRowUpdated#1|RemotePlayer.OnPositionRowUpdated]] and [[client/Scripts/Players/Remote/RemotePlayer.cs#OnRotationRowUpdated#1|OnRotationRowUpdated]]. Both handlers filter by `PlayerId` *because* a binder fires for every row update in its table, not just this puppet's row.

`OnPositionRowUpdated` reconstructs `velocity = Vector2.FromAngle(movement_direction) × movement_speed` — the inverse of the sender's decomposition, which is why the angle+speed encoding round-trips exactly — and hands position + velocity to [[client/Scripts/Components/Movement/InterpolationComponent.cs#SetTarget#1|InterpolationComponent.SetTarget]]. From there the per-frame math in `_Process` is:

```
target_canonical = last reported position + velocity × time since report   (dead reckoning)
target           = NearestCandidate(target_canonical, my position)          (torus fix)
position         = Lerp(position, target, LerpSpeed × delta)                (10/s smoothing)
rotation         = LerpAngle(rotation, rotationTarget, LerpSpeed × delta)
```

**Dead reckoning** — extrapolating the last known velocity forward between reports — is what makes a 1 Hz report cadence look like continuous motion: between reports the target itself keeps moving, and the lerp just trails it. `Moving` (true while the puppet is more than 1 unit from its target) drives the sprite's Walk/Idle animation in [[client/Scripts/Components/Visual/RemoteVisualComponent.cs#_Process#1|RemoteVisualComponent._Process]]. The scene sets `WrapSnapThreshold = 50.0` on the instanced `InterpolationComponent` in `non_local_player.tscn`, so `SetTarget` hard-snaps the puppet when even the nearest wrapped copy of the target is implausibly far (a real teleport/desync), while routine wraps are absorbed by the per-frame nearest-candidate pick.

The puppet's visuals come from a deliberate exception to the binder pattern: [[client/Scripts/Players/Remote/RemotePlayer.cs#_Ready#1|RemotePlayer._Ready]] does a one-shot `Iter()` over the `NearbyRemotePlayersProfiles` client cache to find its profile row and calls [[client/Scripts/Components/Visual/RemoteVisualComponent.cs#SetTexture#1|RemoteVisualComponent.SetTexture]], which resolves the `texture_id` through the catalog and loads the `SpriteFrames`. This works as a plain cache read because that view is *not* AOI-filtered (see Known gaps) — the profile row is guaranteed present even though the player might be at the edge of the AOI.

`InterpolationComponent` is deliberately shared: `Enemy` uses the same component with position-only targets and no snap threshold (doc 08) — the component's doc comment notes it absorbed lerp logic that `RemotePlayer` and `Enemy` used to carry inline.

### Your own row's echo: advisory, not authoritative

```sync
![[00 End-to-End Timeline Flowchart#^move-6{seamless:true,title:false,marker:06.}]]
```

Because you subscribe to `local_player_position`, your own reports echo back to you through the `LocalPlayerPositionBinder` child of `PositionSyncComponent` (wired in `local_player.tscn`, `ReplayExistingRows = true` so the row already in cache at bind time replays through the insert path). The two handlers treat the echo very differently:

- **Insert = initial placement.** [[client/Scripts/Components/Movement/PositionSyncComponent.cs#OnPositionRowInserted#1|OnPositionRowInserted]] hard-sets `GlobalPosition` to the row — this is how a joining player lands at the scaffolded origin.
- **Update = desync check only.** [[client/Scripts/Components/Movement/PositionSyncComponent.cs#OnPositionRowUpdated#1|OnPositionRowUpdated]] never copies the server's position. Your local `GlobalPosition` is a *continuous, unbounded* reference frame (it drifts ever further from the canonical `[0, lap)` range as you lap the torus, and the physics don't care), while the server stores only the wrapped canonical. Forcing one onto the other would snap you across the map at every seam crossing. Instead the handler asks: is even the *nearest wrapped copy* of the server's position farther than `WrapSnapThreshold` (50) from me? Only then — genuine desync, not a routine wrap — does it hard-correct to that nearest copy (and currently prints a `[Desync]` debug line; see Known gaps).

This is the same nearest-candidate rule the puppets use, applied in the opposite direction: puppets chase the server's canonical position from a continuous local one; you ignore the canonical position unless it implies you're truly lost.

```sync
![[00 End-to-End Timeline Flowchart#^move-7{seamless:true,title:false,marker:07.}]]
```

## Known gaps / stubs

- **`nearby_remote_players_profiles` is not AOI-filtered despite its name.** [[server/spacetimedb/src/player/views.rs#nearby_remote_players_profiles#1|The view]] semijoins `PlayerProfile` against *all* `LoggedInPlayer` rows — every logged-in player's profile is in every client's cache, contrast `nearby_remote_players` which applies the AOI OR-chain first. In practice this is what makes `RemotePlayer._Ready`'s one-shot profile `Iter()` reliable, but the name lies about the filter and the table is larger than it needs to be.
- **Leftover debug print in `PositionSyncComponent`.** The desync hard-correction in `OnPositionRowUpdated` still fires `GD.Print($"[Desync] …")` on every snap — fine for development, noise in production.
- **The event-driven reporting mode is implemented but unwired.** `ReportInterval = -1` (report only on key edges) has full code support in `PositionSyncComponent`, but no scene sets it — `local_player.tscn` overrides the export to `1.0`, so the live cadence is 1 Hz timed plus edge-triggered.
- **No server-side movement validation beyond the speed clamp.** `report_movement` accepts any reported position (wrapped, but not distance-checked against the previous row), so teleport-reporting is possible; only speed is clamped. Whether that's a gap or a deliberate trust decision is undocumented in the code.

## Where to go next

Chunk crossings are the same event that drives terrain streaming — read [[07 Terrain & World Streaming]] for what the AOI ring change inserts and deletes on the world side. [[08 Enemies & AI]] reuses `InterpolationComponent` for server-driven enemy puppets, where the *server* computes movement on a 100 ms tick instead of mirroring player reports. When you're done in the world, [[13 Disconnect & Teardown]] covers what happens to these rows on logout and death — including the ghost-row problem the AOI semijoins work around.
