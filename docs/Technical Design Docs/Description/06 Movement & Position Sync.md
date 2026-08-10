# 06 Movement & Position Sync

## Assumed knowledge

- [[05 Joining the World]] — the game subscription wave that carries the position views (join-3), how `EntitySpawnerComponent` spawns `LocalPlayer`/`RemotePlayer` from binder rows (join-4, join-5), and why `PlayerChunk` exists as a separate row.
- [[02 The Component Framework]] — how `TableBinderComponent` re-emits subscribed rows as Godot signals (`LastRow`, `ReplayExistingRows`), and how `GetSibling<T>()` resolves sibling components through the entity registry.
- [[03 Boot & Connection]] — the connection itself and the `FrameTick` pumping that makes SpacetimeDB callbacks fire on the Godot main thread (boot-3); the lap vectors mirrored from `MapConfig` (conn-4).
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `move` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

Movement is **client-authoritative with a server relay**. Your `LocalPlayer` reads WASD and moves itself with Godot physics; the server never simulates you. Ten times a second, a `PositionSyncComponent` reports your position to the `report_movement` reducer, which wraps the coordinates onto the torus world, recomputes which hex chunk you're in, and rewrites your `PlayerPosition` row. That row update flows back to you as a desync sanity check (snap only if impossibly far) and out to everyone nearby through the AOI-filtered `nearby_remote_players` view — "nearby" meaning "within two chunk rings", keyed off the slowly-changing `PlayerChunk` row so the views don't recompute ten times a second. Remote players are puppets: an `InterpolationComponent` lerps them toward each arriving position, picking the nearest wrapped copy of the target so crossing the world's wrap seam looks like normal walking instead of a map-wide teleport.

## Flowcharts

- [[flowcharts/main-movement.canvas]] — the composed movement flow (the movement components and their scenes, the local/remote player scripts, the torus math, and the server's `player` and `world` modules).
![[flowcharts/main-movement.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Movement_subfolder/Movement_subfolder.canvas]] — deep dive: `PositionSyncComponent.cs` and `InterpolationComponent.cs`.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Players_subfolder/Remote_subfolder/Remote_subfolder.canvas]] — deep dive: `RemotePlayer.cs`.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/world_subfolder.canvas]] — deep dive: `hex.rs`, `wrap.rs`, `aoi.rs` (the chunk/wrap math `report_movement` and the AOI views share).

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

## Main body

### Your movement never leaves your machine

```sync
![[00 End-to-End Timeline Flowchart#^move-1{seamless:true,title:false,marker:01.}]]
```

Two design facts fall out of this step. First, **the server trusts the client completely**: nothing in `report_movement` checks speed, teleports, or collision — whatever x/y arrives gets wrapped and stored. That's a deliberate simplicity call for a co-op roguelike (no server physics to keep in lockstep), and its cost is documented in Known gaps. Second, the **Speed stat is applied on the client**: `LocalPlayer` multiplies input by `Speed * SpeedPerStat` where Speed comes from the server-computed `PlayerStats` row (its flat/mult pipeline is [[10 Inventory, Items & Enchantments]]), so equipment that buffs Speed genuinely makes you faster — but the enforcement is honor-system, since the server never validates the positions that result.

The yaw rotation deserves a word: input is rotated by the camera rig's yaw so controls stay camera-relative while the camera spins ([[11 Camera & Presentation]] owns the rig), and while any movement key is held the body's `Rotation` is set to that same yaw — which is the value later reported to the server and used by remote clients as the puppet's facing (move-6).

### The 10 Hz heartbeat

```sync
![[00 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:02.}]]
```

[[PositionSyncComponent.cs##public partial class PositionSyncComponent : Component|PositionSyncComponent]] owns both directions of local position sync — the doc-02 pattern of "one component per server-table concern", declared inline in `local_player.tscn` with its binder child and two `[connection]` entries (`RowInserted`/`RowUpdated`). The reporting loop is a plain accumulator: add `delta`, fire at `ReportInterval`, reset to zero. Resetting to zero (rather than subtracting the interval) discards the per-frame overshoot, so the true rate is a hair under 10 Hz — irrelevant at this cadence, but it means reports are not evenly spaced.

Note what is *not* here: there is no dirty check and no rate adaptation. Standing still for an hour produces 36,000 identical reducer calls, each rewriting the `PlayerPosition` row and re-echoing it to every subscriber. The row's `chunk_index` is what shields the expensive consumers (AOI views key off `PlayerChunk`, move-3), but the view itself still re-evaluates per call.

### The server canonicalizes, then keeps two clocks

```sync
![[00 End-to-End Timeline Flowchart#^move-3{seamless:true,title:false,marker:03.}]]
```

The reducer's first job is making the position canonical. `wrap_world_pos` computes which chunk the raw x/y falls in, Euclidean-wraps the chunk coords into the grid, and — only if they changed — shifts x/y by whole **lap vectors**: the world-space displacement of one full traversal of the grid in the q or r axis, derived from `chunk_center_hex`/`hex_to_world`. Shifting by whole laps is what makes the wrap invisible: the position moves to a different *representative* of the same torus point without changing where it sits relative to the hex grid. One edge case: the config comes from [[server/spacetimedb/src/world/tables.rs##pub fn load|MapConfig::load]](ctx, 0, 0), so if the `MapConfig` row were ever missing, the zero fallback makes `wrap_world_pos` early-return and positions accumulate unwrapped (in practice `init` upserts the row at publish, boot-5).

The second job is the two-clock split. `PlayerPosition` is the fast clock — rewritten every call, carrying x/y/rotation plus a btree-indexed `chunk_index`. `PlayerChunk` is the slow clock — same chunk, stored as (q, r), touched only on crossings. The reason for paying a whole extra row is stated in the comment above [[server/spacetimedb/src/player/tables.rs##pub struct PlayerChunk|PlayerChunk]] and restated at [[server/spacetimedb/src/player/views.rs##pub(crate) fn nearby_indices_from_chunk|nearby_indices_from_chunk]]: SpacetimeDB views re-evaluate when the rows they read change, so a view keyed on `PlayerPosition` would recompute ten times a second per moving player, while one keyed on `PlayerChunk` recomputes only when someone crosses a chunk boundary.

`spiral_chunk_index` is what makes a hex grid indexable: it maps 2D axial chunk coords to a single bijective `i64` by walking rings outward from the origin (ring ρ starts at `3ρ(ρ−1)+1`, then an arm/step pair locates the chunk within its ring). AOI views need *equality* comparisons — an OR-chain over a candidate set — and "is this chunk in my ring?" isn't expressible as a range query on axial coordinates, so every chunk gets one dense scalar id instead.

### The torus, once

The world is a 6×6 grid of hex chunks whose edges are glued together: walk off the east edge and you reappear on the west. Topologically that's a torus, and it has one consequence that shapes every position comparison in the game — **there is no single true coordinate for anything**. The stored, canonical position lives in one arbitrary copy of the world; the same point also exists at canonical ± lapQ, ± lapR, and every combination thereof.

The client and server pick different representatives on purpose. The server always canonicalizes (move-3). The local `LocalPlayer` instead runs in an **unbounded continuous frame**: it never wraps its own `GlobalPosition`, so walking three laps east just keeps increasing x — physics, camera, and input never feel a seam. The price is that any comparison between the local frame and a server position is meaningless until you pick matching copies, which is exactly what [[TorusMath.cs##public static Vector2 NearestCandidate|TorusMath.NearestCandidate]] does: try the canonical position and its 8 lap-shifted neighbors (±lapQ, ±lapR, and the four diagonal combinations), return whichever is closest to a reference point. The lap vectors themselves are server-computed, stored on the `MapConfig` row at world setup (boot-5), and mirrored client-side into [[GameManager.cs##public static Vector2 LapQ|GameManager.LapQ]]/[[GameManager.cs##public static Vector2 LapR|LapR]] (conn-4). The server has the mirror-image helper, [[server/spacetimedb/src/world/wrap.rs##pub fn wrapped_distance_sq|wrapped_distance_sq]], which checks the same 9 combinations for distance comparisons.

### The echo is a sanity check, not a puppet string

```sync
![[00 End-to-End Timeline Flowchart#^move-4{seamless:true,title:false,marker:04.}]]
```

The round trip is: report (move-2) → row update (move-3) → view re-evaluation → binder `RowUpdated` → handler. Naively you'd expect the client to snap itself to whatever the echo says — that would rubber-band the player every 100 ms, since the echo is always ~one network round trip stale. Instead [[PositionSyncComponent.cs##private void OnPositionRowUpdated|OnPositionRowUpdated]] treats the echo as a desync detector: unwrap the server position into the local frame with `NearestCandidate`, and only if the closest copy is *still* beyond `WrapSnapThreshold` (50 units — several player widths) conclude the positions genuinely disagree and hard-set. Routine wraps, latency, and frame jitter all stay far under the threshold, so in steady state the handler is a no-op that runs ten times a second. The initial-placement path is separate: the binder's `ReplayExistingRows` guarantees the first cached row fires `RowInserted`, and [[PositionSyncComponent.cs##private void OnPositionRowInserted|OnPositionRowInserted]] applies it unconditionally — that's how a rejoining player lands at their saved position instead of world origin (join-2 parked the row there; every report since kept it current).

### AOI: two chunk rings, one OR-chain

```sync
![[00 End-to-End Timeline Flowchart#^move-5{seamless:true,title:false,marker:05.}]]
```

The view is caller-scoped like the lobby views from doc 04, but scoped by *space* instead of identity: `nearby_indices_from_chunk` resolves the caller's profile → `PlayerChunk` → chunk coords, and `surrounding_chunk_indices` expands those into every chunk within `DEFAULT_AOI_CHUNK_RADIUS` (2) rings, wrapping ring cells around the torus and re-indexing each to its spiral id. The view then builds `chunk_index == i0 OR chunk_index == i1 OR …` over that set and semijoins `logged_in_player` so only in-world players (not logged-out ghosts — [[13 Disconnect & Teardown]]) match. A caller with no in-world row gets a deliberately empty query (`profile_id == u64::MAX`, the doc-05 impossible-id pattern), so the view is safe to subscribe before `join_world` commits.

The client half is split across two nodes, and the split matters. The *spawner's* binder (join-5, wired in `main.tscn`) sees inserts/deletes — entering or leaving the ring — and creates/destroys `RemotePlayer` nodes. Each *spawned* `RemotePlayer` then carries its own `NearbyRemotePlayersBinder` ([[non_local_player.tscn##[node name="NearbyRemotePlayersBinder" type="Node" parent="."|non_local_player.tscn]], no replay, only `RowUpdated` wired) that receives the 10 Hz position stream for the *whole* nearby set and discards everything whose `PlayerId` isn't its own. So position fan-out is per-puppet filtering of a shared feed, not a per-player subscription. The initial position comes from the spawner's insert row itself ([[EntitySpawnerComponent.cs##private void OnNearbyRemotePlayerInsert|OnNearbyRemotePlayerInsert]] sets `GlobalPosition` before adding the node); the puppet's binder only ever handles updates, which arrive within 100 ms anyway.

[[RemotePlayer.cs##private void OnPositionRowUpdated|RemotePlayer.OnPositionRowUpdated]] also fabricates a **velocity** the server never sent: it reads the puppet's `PlayerStats` for Speed and multiplies by the reported rotation's direction vector. The intent is extrapolation — between 10 Hz updates, keep drifting the target along the last known heading. What actually happens today is in Known gaps.

### Interpolation: living a fraction of a second in the past

```sync
![[00 End-to-End Timeline Flowchart#^move-6{seamless:true,title:false,marker:06.}]]
```

If remote puppets snapped to each arriving position you'd see 10 Hz teleport-stutter. [[InterpolationComponent.cs##public partial class InterpolationComponent : Component|InterpolationComponent]] (instanced into `non_local_player.tscn` from `interpolation_component.tscn` — a live scene reference, not one of the nine unreferenced duplicates) instead turns the position stream into motion: each frame it advances the target by `snapVelocity * timeSinceSnap` (dead reckoning), converts the extrapolated target into the puppet's frame via `NearestCandidate`, then eases toward it with `Lerp(current, target, LerpSpeed * delta)`. The lerp is frame-rate independent in spirit — `LerpSpeed * delta` is a per-second weight — so the remaining gap decays exponentially and the puppet trails the server by a small, smooth lag rather than a fixed offset. Rotation gets the same treatment through `LerpAngle`, which interpolates the short way around the circle.

The wrap handling is the subtle part: because the *candidate pick happens every frame*, a target that crosses a lap seam never produces a snap — the nearest copy glides continuously from one side of the canonical world to the other. Snapping is reserved for `WrapSnapThreshold` (50 units, set on the instance in [[non_local_player.tscn##WrapSnapThreshold = 50.0|non_local_player.tscn]]): if even the nearest copy of a fresh target is impossibly far, that's a teleport or a lost-threads desync and the puppet jumps ([[InterpolationComponent.cs##public void SetTarget(Vector2 position, float rotation, Vector2 velocity = default)|SetTarget]]). `Enemy` reuses the same component with position-only targets and the default threshold of 0 — enemies teleport-snap never, because their server sim moves them continuously (see [[08 Enemies & AI]]).

The puppet's sprite is the last consumer of this machinery: [[RemoteVisualComponent.cs##public partial class RemoteVisualComponent : VisualComponent|RemoteVisualComponent]] plays `"Walk"` whenever `Moving` is true (more than 1 unit from the current target) and `"Idle"` otherwise, and its texture comes from the profile the puppet looked up at spawn — [[RemotePlayer.cs##public override void _Ready()|RemotePlayer._Ready]] scans the `NearbyRemotePlayersProfiles` cache for its `ProfileId` and calls [[RemoteVisualComponent.cs##public void SetTexture(string textureId)|SetTexture]], resolving the texture id through the base-wave catalog (conn-4).

## Known gaps / stubs

- **`nearby_remote_players_profiles` is not AOI-filtered, despite the name.** [[server/spacetimedb/src/player/views.rs##fn nearby_remote_players_profiles|nearby_remote_players_profiles]] semijoins `player_profile` against *all* `logged_in_player` rows — no chunk filter at all (contrast [[server/spacetimedb/src/player/views.rs##fn nearby_remote_players(ctx|nearby_remote_players]], which runs the chunk OR-chain first). Every in-world client therefore downloads the profile — name, texture, aim settings — of *every* logged-in player, nearby or not. It's correctness-harmless (`RemotePlayer._Ready` just finds its `ProfileId` in a bigger-than-needed cache, and far-away profiles sit unused), but it leaks the full player list to every client and scales with total population, not neighborhood size.
- **Leftover debug print on the desync path.** [[PositionSyncComponent.cs##private void OnPositionRowUpdated|OnPositionRowUpdated]] fires `GD.Print($"[Desync] …")` on every hard correction. Corrections should be rare, but any sustained desync spams the console at 10 Hz.
- **Remote velocity extrapolation is dead code in practice.** [[RemotePlayer.cs##private void OnPositionRowUpdated|RemotePlayer.OnPositionRowUpdated]] computes `speed` from `conn.Db.PlayerStats.ProfileId.Find(ProfileId)` — but no subscription wave includes the raw `PlayerStats` table ([[TableSubscriber.cs##public static readonly string[] GameTables|GameTables]] carries only the caller-scoped `LocalPlayerStats` view), so the lookup always misses, speed is 0, and `InterpolationComponent`'s dead-reckoning always receives `Vector2.Zero`. Even if the table were subscribed, the extrapolation would still be wrong twice over: `RemotePlayer`'s [[RemotePlayer.cs##private const float SpeedPerStat = 4f|SpeedPerStat is 4f]] while real movement uses [[LocalPlayer.cs##private const float SpeedPerStat = 10f|10f]], and the direction vector assumes the reported rotation is the heading — but move-1 sets rotation to the *camera* yaw, which need not match the direction of travel.
- **Movement is unvalidated.** `report_movement` accepts any coordinates the client sends (it wraps and stores them), so a modified client can teleport or move at arbitrary speed. There is no speed cap, no distance-per-report check, and no server-side collision — acceptable for a co-op prototype, but it's a cheat surface, not an oversight that types can catch.

## Where to go next

The chunk grid these positions live on — hex layout, world generation, terrain streaming through `NearbyTerrainTiles`/`NearbyHexDecor` — is [[07 Terrain & World Streaming]]. The other big consumer of position rows is the enemy sim, which spawns and steers enemies against the same chunk/AOI machinery: [[08 Enemies & AI]].
