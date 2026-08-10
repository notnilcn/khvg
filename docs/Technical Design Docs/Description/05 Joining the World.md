# 05 Joining the World

## Assumed knowledge

- [[04 Lobby & Profiles]] — the lobby-as-a-row model (`LoggedOutPlayer`/`LoggedInPlayer`), the profile panels whose JOIN button starts this doc, and the caller-scoped view pattern.
- [[03 Boot & Connection]] — the connection, the base/lobby subscription waves from conn-4, and why callbacks only fire while the client pumps `FrameTick`.
- [[02 The Component Framework]] — how `TableBinderComponent` re-emits subscribed rows as Godot signals and how `GetSibling<T>()` resolves components through the entity registry (not the node tree).
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `join` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

Joining is a **row move plus a subscription swap**. The JOIN button calls the `join_world` reducer, which deletes the caller's `LoggedOutPlayer` row and inserts a `LoggedInPlayer` row naming the chosen profile — that single transaction *is* the state change. On a profile's first join, `try_scaffold_profile` lazily fills in its data rows (stats, inventory, position at world origin). Meanwhile the client swaps subscription waves: lobby wave out, game wave in — sixteen tables and views including the caller-scoped `local_player*` views and the chunk-filtered `nearby_*` views. As rows arrive, binders declared inline in `main.tscn` fire signals and `EntitySpawnerComponent` instantiates the actual Godot nodes: your `LocalPlayer`, nearby `RemotePlayer`s, `Enemy`s, and `Drop`s. Leaving (or dying) moves the row back, and the same binder machinery despawns everything and re-opens the lobby.

## Flowcharts

- [[flowcharts/main-join.canvas]] — the composed joining flow (the spawner component script and scene, the local-player scene, the subscription SDK, and the server's `player` module).
![[flowcharts/main-join.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Spawning_subfolder/Spawning_subfolder.canvas]] — deep dive: `EntitySpawnerComponent.cs`.
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/local_player_codefile/local_player_codefile.canvas]] — deep dive: `local_player.tscn` and its component children.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]] — deep dive: `reducers.rs` (`join_world`/`leave_world`), `methods.rs` (`try_scaffold_profile`), `tables.rs`, `views.rs`.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^join-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^join-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^join-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^join-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^join-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^join-6{seamless:true,title:false,marker:06.}]]
```

## Main body

### The transition is one reducer, one transaction

```sync
![[00 End-to-End Timeline Flowchart#^join-1{seamless:true,title:false,marker:01.}]]
```

Two details the step compresses. First, the guard order: [[server/spacetimedb/src/player/reducers.rs##pub fn join_world|join_world]] checks lobby membership *before* the profile lookup, so an in-world caller re-joining gets "Not in lobby." rather than "Profile not found." — the same convention `create_profile` uses (doc 04). Second, the ownership check (`profile.player_id != ctx.sender()`) is what stops a player from joining *someone else's* profile: `profile_id`s are global auto-increment ids, so without it, guessing an id would be enough.

[[server/spacetimedb/src/player/reducers.rs##pub fn leave_world|leave_world]] is the mirror image — delete `LoggedInPlayer`, reinsert `LoggedOutPlayer`, preserving `username`/`is_admin`. Notably it does **not** call `teardown_profile`: the profile's `PlayerData`/`PlayerStats`/`PlayerInventory`/`PlayerPosition`/`PlayerChunk` rows survive logout, which is exactly what makes join-2's lazy scaffolding idempotent on rejoin. (The flip side — those position rows linger as "ghosts" that the enemy sim has to guard against — is [[13 Disconnect & Teardown]]'s Known gaps.)

### Scaffolding: six tables, one guard each

```sync
![[00 End-to-End Timeline Flowchart#^join-2{seamless:true,title:false,marker:02.}]]
```

[[server/spacetimedb/src/player/methods.rs##pub fn try_scaffold_profile|try_scaffold_profile]] is the player-side instance of the doc-02 "archetype helper" pattern: one function that knows every table making up a logical player entity, paired with `teardown_profile` as its exact inverse. Every insert is individually guarded, so the function converges a profile to "fully scaffolded" no matter which subset of rows already exists — there is no separate "first join" vs "rejoin" code path to drift apart.

The starter loadout is hardcoded here, not in seed data: slot 0 gets a `"Bow"`, and slots 1/5/9/13/23 get `"Bread"`/`"Hat"`/`"Helmet"`/`"Skull"`/`"Bag"` — string ids resolved against the item catalog the base wave (conn-4) delivers. The spawn position is equally blunt: every profile starts at world origin (0, 0), with `chunk_index` computed through [[server/spacetimedb/src/world/hex.rs##pub fn world_to_chunk|world_to_chunk]] + `spiral_chunk_index` from the `MapConfig` row — the same math `report_movement` redoes every tick ([[06 Movement & Position Sync]]).

### The game wave: sixteen tables, subscribed on faith

```sync
![[00 End-to-End Timeline Flowchart#^join-3{seamless:true,title:false,marker:03.}]]
```

The game wave in [[TableSubscriber.cs##public static readonly string[] GameTables|GameTables]] falls into four groups, each owned by a later doc: the caller-scoped `local_player*` views (this doc), the AOI-filtered `nearby_*` views (this doc's spawning, [[06 Movement & Position Sync|06]]'s filtering math), terrain/decor views (`NearbyTerrainTiles`, `NearbyHexDecor` — [[07 Terrain & World Streaming]]), and combat/enemy feeds (`NearbyEnemies`, `EnemyTemplates`, `BulletPatternEvent` — [[08 Enemies & AI|08]]/[[09 Combat & Damage|09]]). `MapConfig` rides along so the lap vectors arrive exactly when the world does.

Three subtleties worth internalizing:

- **The subscribe is optimistic.** `OnJoinPressed` calls [[TableSubscriber.cs##public void SubscribeGame()|SubscribeGame]] immediately after `JoinWorld`, without waiting for the reducer to commit. This is safe because SpacetimeDB views re-evaluate as their underlying tables change: the wave is initially empty (every `local_player*` view misses its `LoggedInPlayer` lookup and falls back to `u64::MAX`), then the join-1 row move lands and the views start producing rows. The same optimism is a hazard on the failure path — see Known gaps.
- **`LocalPlayerProfiles` is in both waves.** It appears in `LobbyTables` *and* `GameTables`, so the profile list stays subscribed while you're in the world — the lobby's binder keeps receiving rows even though the lobby UI is hidden, which is what lets `ShowLobby` after death display an already-populated list.
- **Only the game wave has an error handler.** `SubscribeGame` attaches [[TableSubscriber.cs##private void OnGameSubError|OnGameSubError]] (a `GD.PrintErr`); the base and lobby waves subscribe with no `OnError` at all. All three waves log their `OnApplied` with a plain `GD.Print`.

### Spawning yourself

```sync
![[00 End-to-End Timeline Flowchart#^join-4{seamless:true,title:false,marker:04.}]]
```

The live wiring site is `main.tscn`, not the standalone spawner scene: the [[main.tscn##[node name="EntitySpawnerComponent" type="Node" parent="."|EntitySpawnerComponent]] node is declared inline with its five `PackedScene` exports, its four binder children (`LocalPlayerBinder`, `NearbyRemotePlayersBinder`, `NearbyEnemiesBinder`, `NearbyLootDropsBinder` — all `ReplayExistingRows = true`), and eight `[connection]` entries pairing each binder's `RowInserted`/`RowDeleted` to a handler on [[EntitySpawnerComponent.cs##public partial class EntitySpawnerComponent : Component|EntitySpawnerComponent]]. (`entity_spawner_component.tscn` is one of the nine unreferenced duplicate scenes from doc 02 — the component's own docstring still points at it, see Known gaps.)

`ReplayExistingRows` matters more here than anywhere else: the game wave's initial delivery can beat the binders' hooks (binders bind on entity-ready, the subscription lands whenever the server answers), so the replay loop is what guarantees the `LocalPlayer` spawn even when the row arrived first. `OnLocalPlayerInsert` is also idempotent by hand — both instantiations are `null`-guarded, so a duplicate insert can't double-spawn the player or the `BulletManager`.

The spawned [[LocalPlayer.cs##public partial class LocalPlayer : CharacterBody2D, IEntity|LocalPlayer]] is itself an entity root: it can't inherit `Entity` (Godot needs `CharacterBody2D` for physics), so it implements `IEntity` with its own `EntityRegistry`, and its child components — position sync, data/stats mirrors, inventory state, profile visuals — each bind their own game-wave table from `local_player.tscn`. Those children are deliberately out of scope here: movement sync is [[06 Movement & Position Sync]], inventory is [[10 Inventory, Items & Enchantments]].

### Spawning everyone else

```sync
![[00 End-to-End Timeline Flowchart#^join-5{seamless:true,title:false,marker:05.}]]
```

Every remote-entity handler follows the same contract: read the row from the binder's `LastRow` (signals carry no row argument — the doc-02 binder design), instantiate, seed position and ids, `CallDeferred` into the scene tree, and record the node in a dictionary keyed by id (`remotePlayers` by `PlayerId` string, `enemies` by `EnemyId`, `drops` by `DropId`). The dictionaries do double duty: they dedupe re-delivered inserts (each handler early-returns on `ContainsKey`), and the matching `On*Delete` handlers use them to find and `QueueFree` the right node when the AOI view drops a row — walking out of range despawn things exactly as walking in spawns them.

Why does the client need `IsLocal` at all? Because [[server/spacetimedb/src/player/views.rs##fn nearby_remote_players(ctx|nearby_remote_players]] filters `PlayerPosition` rows to nearby chunks and semijoins `logged_in_player` — and the caller's own position row satisfies both. The view could exclude self but doesn't, so the client guards instead; without the check you'd spawn a ghost duplicate of yourself standing at your last reported position.

The chunk-keying is a deliberate performance trade, stated in the comment above [[server/spacetimedb/src/player/tables.rs##pub struct PlayerChunk|PlayerChunk]] and repeated at [[server/spacetimedb/src/player/views.rs##pub(crate) fn nearby_indices_from_chunk|nearby_indices_from_chunk]]: `PlayerPosition` updates every `report_movement` call (~10 Hz), so views keyed on it would recompute ten times a second per player; `PlayerChunk` is rewritten only when `report_movement` detects a chunk crossing, so AOI views recompute on crossings only. The cost is a second per-profile row to maintain — and one more ghost row when players log out ([[13 Disconnect & Teardown]]).

### The way back out

```sync
![[00 End-to-End Timeline Flowchart#^join-6{seamless:true,title:false,marker:06.}]]
```

The despawn path reuses lobby-7's wave machinery in reverse, and it lives on the *spawner*, not the lobby — [[EntitySpawnerComponent.cs##private void OnLocalPlayerDelete|OnLocalPlayerDelete]] is the only place that calls `SubscribeLobby`/`UnsubscribeGame` after boot. It guards on `Conn.IsActive` first, because the same handler can also run while the connection is down (disconnect teardown), and swapping waves on a dead connection would be pointless ([[13 Disconnect & Teardown]] picks up that thread).

## Known gaps / stubs

- **Leftover debug prints in the drop handler.** [[EntitySpawnerComponent.cs##private void OnDropInsert|OnDropInsert]] fires `GD.Print("Drop Inserted")` and `GD.Print("Drop Being Instantiated")` on every loot-drop row — the prints even bracket the dedupe check, so the first one logs rows that are then correctly skipped as duplicates.
- **`leave_world` has no client caller.** Nothing outside the generated `module_bindings` invokes `Reducers.LeaveWorld` — there is no "exit to lobby" button anywhere. In practice players leave the world only by dying (server-side row move) or disconnecting.
- **A rejected join strands the player.** `OnJoinPressed` hides the lobby and swaps waves *before* the server answers, and attaches no failure callback; if `join_world` errors (stale `profile_id`, ownership race), the `local_player*` views stay empty, no `LocalPlayer` row ever arrives, and nothing re-shows the lobby — the game sits on a hidden menu over an empty world until restart.
- **Profile panels have no dedup.** `OnProfileRowInserted` appends a new `profile_panel.tscn` per delivered row with no `ContainsKey`-style guard (unlike every spawner handler), and nothing clears the list when the lobby hides. After a death, `OnLocalPlayerDelete` re-subscribes the lobby wave — any re-delivered profile rows append duplicate panels on top of the originals.
- **Stale docstring / drift hazard.** The `EntitySpawnerComponent.cs` header says its binders are "declared in `entity_spawner_component.tscn`, signals wired in the editor" — that scene is one of the nine unreferenced duplicates from [[02 The Component Framework]]; the live declaration and all eight signal connections are inline in `main.tscn`.

## Where to go next

What happens once you're standing at (0, 0) — `report_movement`, the 10 Hz position loop, interpolation, and the torus math behind the AOI chunks — is [[06 Movement & Position Sync]]. For the full exit story (disconnects, `client_disconnected`, ghost rows), skip to [[13 Disconnect & Teardown]].
