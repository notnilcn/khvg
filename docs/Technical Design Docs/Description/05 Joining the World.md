# 05 Joining the World

## Assumed knowledge

- [[00 End-to-End Timeline Flowchart]] — the global timeline this doc's steps are transcluded from.
- [[02 The Component Framework]] — entities, components, and the `TableBinderComponent` row-signal mechanism every spawn below rides on.
- [[03 Boot & Connection]] — the `DatabaseConnector` autoload, the base/lobby subscription waves, and how `game.tscn` loads.
- [[04 Lobby & Profiles]] — the profile panels whose Join button is this doc's entry point, and the `PlayerProfile`/`LoggedInPlayer`/`LoggedOutPlayer` tables.

## The 30-second version

Joining is one reducer call plus a subscription swap. The Join button on a profile panel calls the server's `join_world` reducer with the profile id and *immediately* switches the client from the lobby subscription wave to the game wave. The server moves the identity's row from `LoggedOutPlayer` to `LoggedInPlayer` (now naming the chosen profile) and — first join only — scaffolds the full set of per-profile rows: data, stats, 38 inventory slots, position, rotation, chunk. Because the client now subscribes to the `LocalPlayer` view (your own `LoggedInPlayer` row), that row arrives as an insert, and the spawner component instantiates `local_player.tscn` — that instantiation *is* "you are in the world". From then on every other player, enemy, drop, and terrain hex appears the same way: an AOI-filtered view row inserts, a binder fires, a scene spawns. Leaving, dying, or disconnecting deletes that one `LocalPlayer` view row, and the whole thing runs in reverse.

## Flowcharts

- [[flowcharts/main-join.canvas]] — this system's composed flowchart (composed by the phase-2 pass; expected to be unresolved until then).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Spawning_subfolder/Spawning_subfolder.canvas]] — deep dive: `EntitySpawnerComponent`, the insert→instantiate / delete→free core of this doc.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Players_subfolder/Local_subfolder/Local_subfolder.canvas]] — deep dive: `LocalPlayer`, the entity the join produces.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]] — deep dive: the server `player` module — `join_world`/`leave_world`, `try_scaffold_profile`, and the `local_player*`/`nearby_*` views.

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

```sync
![[00 End-to-End Timeline Flowchart#^join-7{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^join-8{seamless:true,title:false,marker:08.}]]
```

## Main body

### What "joining" actually is

SpacetimeDB has no concept of a "session" beyond the connection itself, so this game implements the lobby/world distinction as *which table holds your identity row*. A connected client is a row in exactly one of two tables: `LoggedOutPlayer` (the lobby seat, created by `client_connected` — see [[03 Boot & Connection]]) or `LoggedInPlayer` (in the world, carrying the chosen `profile_id` — see [[server/spacetimedb/src/player/tables.rs#LoggedInPlayer#1|LoggedInPlayer]]). Joining is the transaction that deletes one and inserts the other. Every other mechanism in this doc — the client's spawn, the AOI views, the remote puppets on *other* players' screens — is a downstream consequence of that single row move, because the views are all defined in terms of these tables.

The pairing also explains the guards: lobby-only reducers (`create_profile`, `delete_profile`, `join_world`) open with [[server/spacetimedb/src/player/methods.rs#require_in_lobby#1|require_in_lobby]], and every world reducer (`report_movement`, `swap_slots`, …) opens with `require_in_world`. A reducer called from the wrong state fails with `"Not in lobby."` / `"Not in world."` before touching anything.

### The button press: intent first, confirmation later

```sync
![[00 End-to-End Timeline Flowchart#^join-1{seamless:true,title:false,marker:01.}]]
```

[[client/Scripts/Components/Lobby/LobbyComponent.cs#OnJoinPressed#1|OnJoinPressed]] does four things in order, and the order is the design. It (1) bails out if there is no connection or the create-profile panel is open — the same guard `OnDeletePressed` uses, so a half-typed profile name can't overlap a join; (2) hides the lobby and drops the menu phantom camera's priority to 0 via `HideLobby`; (3) calls `conn.Reducers.JoinWorld(profile.ProfileId)`; (4) flips subscription waves through `GetSibling<TableSubscriber>()`.

Two things about step (4) are worth knowing. First, `GetSibling<T>()` is not Godot's node-tree sibling — it is the component-framework helper defined in [[client/Scripts/Components/Component.cs#GetSibling#1|Component.GetSibling]], which asks the owning *entity* (the `GameManager` entity at the root of `game.tscn`) for another registered component. That is why `LobbyComponent` (instanced under the `UI` CanvasLayer) can reach `TableSubscriber` (a direct child of the scene root) without a node path. Second, the wave flip happens *before* the server has processed the reducer: reducers are fire-and-forget remote calls that return nothing, so the client cannot wait for a result. It opts into the game wave immediately so that the rows `join_world` is about to create stream in as soon as they exist.

The cost of that optimism: if `join_world` *fails* (say the identity row was already moved by a stale double-join), the client has already hidden the lobby and unsubscribed the lobby wave, and nothing listens for the reducer error — the `LocalPlayer` row never arrives, so no player spawns and no UI is showing. See Known gaps.

### The game wave: 19 tables, most of them per-caller views

```sync
![[00 End-to-End Timeline Flowchart#^join-3{seamless:true,title:false,marker:03.}]]
```

The subscription waves are just three static string arrays on [[client/sstdbsdk/TableSubscriber.cs#GameTables#1|TableSubscriber]] — `BaseTables`, `LobbyTables`, `GameTables` — the single source of truth for "what is subscribed", turned into `SELECT *` statements by [[client/sstdbsdk/TableSubscriber.cs#TablesSql#1|TablesSql]] through the generated bindings' `From` API (so a server-side table rename breaks the build, not the SQL silently). `SubscribeGame()` builds one subscription covering all 19 names, with `OnApplied` logging readiness and [[client/sstdbsdk/TableSubscriber.cs#OnGameSubError#1|OnGameSubError]] only printing to the console; `UnsubscribeLobby()` drops the two lobby tables. The game wave deliberately keeps `LocalPlayerProfiles` in its list, so the profile cache stays warm across the lobby↔world transition.

The wave's 19 names fall into three kinds:

- **Per-caller `LocalPlayer*` views.** Each is defined in [[server/spacetimedb/src/player/views.rs#local_player#1|player/views.rs]] as a query filtered by `ctx.sender()` — SpacetimeDB evaluates views per subscriber, so `LocalPlayerData` on your client contains *only your* data row. The ones that key off the active profile (`local_player_data`, `local_player_stats`, `local_player_inventory`, [[server/spacetimedb/src/player/views.rs#local_player_active_profile#1|local_player_active_profile]], …) first read the caller's `profile_id` out of their `LoggedInPlayer` row — which is why they return nothing until `join_world` has run, and exactly the joined profile's rows afterwards.
- **AOI views** (`NearbyRemotePlayers`, `NearbyEnemies`, `NearbyLootDrops`, `NearbyTerrainTiles`, `NearbyHexDecor`, `NearbyRemotePlayerRotations`). All of them funnel through [[server/spacetimedb/src/player/views.rs#nearby_indices_from_chunk#1|nearby_indices_from_chunk]], which reads the caller's `PlayerChunk` row and expands it to the chunk ring (`DEFAULT_AOI_CHUNK_RADIUS` = 2 from [[server/spacetimedb/src/main/global.rs#DEFAULT_AOI_CHUNK_RADIUS#1|main/global.rs]]). Each view then OR-chains equality on `chunk_index` over those chunk ids — SQL-friendly filtering over an index, not per-row distance math.
- **Shared/event tables** (`EnemyTemplates`, `BulletPatternEvent`, `BulletControlEvent`, `MapConfig`). `MapConfig` gets special handling: `TableSubscriber.OnConnected` hooks its `OnInsert`/`OnUpdate` directly (not through a binder) and lifts the torus lap vectors into its `LapQ`/`LapR` properties, which the `GameManager` facade re-exposes for all wrap math (see [[06 Movement & Position Sync]]).

The reason `PlayerChunk` exists as a separate table at all is stated in its own comment in [[server/spacetimedb/src/player/tables.rs#PlayerChunk#1|player/tables.rs]]: `PlayerPosition` is rewritten on every `report_movement` call (~10 Hz), but `PlayerChunk` is rewritten **only when the chunk actually changes** ([[server/spacetimedb/src/player/reducers.rs#report_movement#1|report_movement]] compares `(chunk_q, chunk_r)` before updating). Since SpacetimeDB re-evaluates a view when any row it reads changes, keying the AOI views off the quiet table means the expensive ring recomputation happens per chunk crossing, not per movement tick. At join time this is also what makes the initial world snapshot well-defined: `try_scaffold_profile` plants the `PlayerChunk` row at the world origin (below), so the AOI views immediately resolve to the origin's chunk ring.

### The server transition: `join_world` and the archetype scaffold

```sync
![[00 End-to-End Timeline Flowchart#^join-2{seamless:true,title:false,marker:02.}]]
```

[[server/spacetimedb/src/player/reducers.rs#join_world#1|join_world]] is short and strictly ordered: guard (`require_in_lobby`), ownership check on the `PlayerProfile` row (`profile.player_id != ctx.sender()` → `"Not your profile."`), delete the `LoggedOutPlayer` row, insert the `LoggedInPlayer` row — carrying `username` and `is_admin` forward from the lobby row so those survive the transition — then scaffold. The whole reducer is one transaction, so a scaffold failure would roll back the identity move too; there is no half-joined state server-side.

[[server/spacetimedb/src/player/methods.rs#try_scaffold_profile#1|try_scaffold_profile]] is the server-side mirror of the client's component tree: one helper that inserts every row a logical "player entity" needs, each insert guarded by an existence check so the helper is idempotent per table. On a *first* join it inserts:

- the `PlayerData` row (level 1, `hp`/`max_hp` = `BASE_MAX_HP` 100, `base_speed` = `BASE_SPEED` 100.0, defense 0);
- the 38 `PlayerInventorySlot` rows built by the `inventory_slot` helper — slot 0 weapon (pre-equipped `Bow`), 1–4 hotbar (Bread placed at slot 1), 5 accessory (`Hat`), 6 armor (`Helmet`), 7–30 general backpack, 31 bag (`Bag`), 32–37 ability — then [[server/spacetimedb/src/player/methods.rs#place_span#1|place_span]] pre-equips the Skull at 32 and the 3-slot Tome of Mending spanning 33–35, so ability activation is testable on a fresh character (slot semantics: [[10 Inventory, Items & Enchantments]]);
- the zeroed `PlayerStatAllocation` row;
- `PlayerStats`, resolved by [[server/spacetimedb/src/player/methods.rs#recompute_stats#1|recompute_stats]] — the one place stats are ever computed — so the fresh character's stats already reflect the pre-equipped gear;
- `PlayerPosition`, `PlayerRotation`, and `PlayerChunk`, all at world origin `(0, 0)`, with the chunk computed from the `MapConfig` grid via [[server/spacetimedb/src/world/hex.rs#world_to_chunk#1|world_to_chunk]] + [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index#1|spiral_chunk_index]].

On a *re-join* after a voluntary `leave_world`, every existence check finds its rows and the helper inserts nothing — you keep your level, inventory, and last position (see [[13 Disconnect & Teardown]] for the leave/death asymmetry; death tears the rows down, so joining after death re-scaffolds from scratch).

The inverse transition, [[server/spacetimedb/src/player/reducers.rs#leave_world#1|leave_world]], is deliberately minimal: find the `LoggedInPlayer` row (error `"Not in world."` otherwise), delete it, insert the equivalent `LoggedOutPlayer` row, done. It touches no profile rows — leaving is meant to be cheap and reversible, with teardown reserved for death and `delete_profile`. *Known gap:* no client UI calls it today; the only ways back to the lobby are death and disconnect.

### Spawning yourself: one row insert becomes one scene instance

```sync
![[00 End-to-End Timeline Flowchart#^join-4{seamless:true,title:false,marker:04.}]]
```

[[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs|EntitySpawnerComponent]] (a component of the `GameManager` entity, declared inline in [[client/Scenes/game.tscn|game.tscn]] with its four `PackedScene` exports wired to `local_player.tscn`, `non_local_player.tscn`, `default_enemy.tscn`, `drop.tscn`) owns the row→node mapping for every spawnable thing. It has four child `TableBinderComponent`s — `LocalPlayerBinder`, `NearbyRemotePlayersBinder`, `NearbyEnemiesBinder`, `NearbyLootDropsBinder` — declared inline in `game.tscn` with their `TableName` exports and `RowInserted`/`RowDeleted` connections to the spawner's handlers (the `LocalPlayerBinder` also sets `ReplayExistingRows = true`, so a `LocalPlayer` row already in the client cache — e.g. a re-join where the game wave resubscribes — still fires the insert path).

[[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnLocalPlayerInsert#1|OnLocalPlayerInsert]] instantiates `LocalPlayerScene` and adds it under the current scene root via `CallDeferred(Node.MethodName.AddChild, …)` — deferred because the handler runs inside a network-callback frame where immediately mutating the scene tree is unsafe. The guard `if (localPlayer == null)` makes the spawn idempotent against duplicate inserts.

The delete path is the lobby round-trip: [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnLocalPlayerDelete#1|OnLocalPlayerDelete]] frees the player node, re-shows the lobby, and flips the waves back (`SubscribeLobby()` + `UnsubscribeGame()`). This one handler serves *all three* exit causes — `leave_world`, death, and disconnect all eventually delete the caller's `LoggedInPlayer` row server-side, and the view does the rest. That is why the `LocalPlayer` view row is the single source of truth for "am I in the world" on the client.

### The new entity's first frame: camera and 3D registration

```sync
![[00 End-to-End Timeline Flowchart#^join-5{seamless:true,title:false,marker:05.}]]
```

[[client/Scripts/Players/Local/LocalPlayer.cs#_Ready#1|LocalPlayer._Ready]] registers the freshly spawned entity with the presentation systems. It hands its child `LocalPlayerPhantomCamera2D` to [[client/Scripts/Components/Camera/Camera2DPresenterComponent.cs#RegisterCamera#1|Camera2DPresenterComponent.RegisterCamera]], which sets the phantom camera's priority to 60 — outranking the menu camera that `LobbyComponent.ShowLobby` had raised, so camera authority passes to the player exactly when the player exists (the priority handoff system is [[11 Camera & Presentation]]'s topic). It also constructs a [[client/Scripts/Players/CharacterModel3D.cs#CharacterModel3D#1|CharacterModel3D]] — a 3D mirror that copies the 2D entity's transform into the `world_3d.tscn` backdrop viewport each frame — and makes it the 3D camera's follow target via `World3DComponent.SetCameraFollowTarget`. Finally it caches the scene-level camera components (`World3DComponent`, `Camera2DPresenterComponent`, `CameraRigComponent`) reached through `GetAncestor<GameManager>()`, because `_PhysicsProcess` reads them every frame. `_ExitTree` unwinds all three registrations so a despawned player never leaves the cameras pointing at freed nodes.

### Initializing server state from replayed rows

```sync
![[00 End-to-End Timeline Flowchart#^join-6{seamless:true,title:false,marker:06.}]]
```

The rows `join_world` created server-side reach the new entity through the binders declared in [[client/Scenes/local_player.tscn|local_player.tscn]], all with `ReplayExistingRows = true` — the load-bearing detail, because these components' `_Ready` runs *after* the subscription has already delivered the rows into the client cache. Replay re-fires cached rows through the insert path, so initialization needs no separate "read current state" code:

- `LocalPlayerPosition` → [[client/Scripts/Components/Movement/PositionSyncComponent.cs#OnPositionRowInserted#1|PositionSyncComponent.OnPositionRowInserted]] snaps the node's `GlobalPosition` to the server position — the *only* time the local position is set from the row; afterwards the local frame is authoritative for rendering and the row is only a desync check (see [[06 Movement & Position Sync]]).
- `LocalPlayerData`/`LocalPlayerStats`/`LocalPlayerStatAllocation` → [[client/Scripts/Components/Data/LocalPlayerDataComponent.cs#OnDataRow#1|LocalPlayerDataComponent]], which copies level/defense/base-speed/unspent-points onto itself and pushes hp and the six stats into the sibling `HealthComponent`/`StatsComponent` mirrors via `SetFromServer`.
- `LocalPlayerInventory` → [[client/Scripts/Components/Inventory/LocalPlayerInventoryComponent.cs#OnInventoryRow#1|LocalPlayerInventoryComponent.OnInventoryRow]], one row per slot into its slot dictionary.
- `LocalPlayerActiveProfile` → [[client/Scripts/Components/Visual/LocalPlayerProfileComponent.cs#OnProfileRowInserted#1|LocalPlayerProfileComponent.OnProfileRowInserted]], which sets the profile name and loads the sprite's `SpriteFrames` by resolving the row's `texture_id` through [[client/Scripts/Game/GameManager.cs#GetResPath#1|GameManager.GetResPath]] → `CatalogComponent` (the base-wave texture cache from [[03 Boot & Connection]]).

Every one of these is a one-way mirror: rows flow in, components render, and nothing writes local values back. State changes instead go out as new reducer calls and return as row updates — the loop that join-1 opened.

### The rest of the cast: AOI inserts become puppets

```sync
![[00 End-to-End Timeline Flowchart#^join-7{seamless:true,title:false,marker:07.}]]
```

The same spawner handles the three AOI entity kinds with one repeated shape: an insert handler reads the binder's `LastRow`, checks an id-keyed dictionary (`remotePlayers` by identity string, `enemies` and `drops` by `ulong` id), instantiates the exported scene, seeds it with the row's identity fields and position, and adds it deferred; the matching delete handler looks the node up, removes it from the dictionary, and `QueueFree`s it. [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnNearbyRemotePlayerInsert#1|OnNearbyRemotePlayerInsert]] additionally skips rows that are actually you (`DatabaseConnector.IsLocal`) — your own position row is inside your own AOI ring, and without that guard you'd spawn a puppet of yourself.

The invariant this maintains: **the visible world equals the subscribed row set**. Because the AOI views delete rows as they leave your chunk ring (and the server deletes rows when entities die), the delete path — not any client-side culling logic — is what keeps the scene tree honest. A player walking away, an enemy dying, a drop being picked up: all arrive as `RowDeleted` and all funnel through the same dictionary removal.

### Remote player visuals: the profile side-channel

```sync
![[00 End-to-End Timeline Flowchart#^join-8{seamless:true,title:false,marker:08.}]]
```

A `RemotePlayer` puppet's position/rotation rows carry no appearance, so [[client/Scripts/Players/Remote/RemotePlayer.cs#_Ready#1|RemotePlayer._Ready]] does a one-shot lookup: it iterates the client cache of the `NearbyRemotePlayersProfiles` view (subscribed in the game wave), finds the row matching its `ProfileId`, and hands the `texture_id` to its `RemoteVisualComponent`. This works because the view is *not* actually AOI-filtered — see Known gaps — so the profile row is guaranteed to already be in the cache when the puppet spawns; if it were truly proximity-filtered, this read-once pattern would race the view's own inserts.

## Known gaps / stubs

- **Leftover debug prints in the spawner.** [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnDropInsert#1|EntitySpawnerComponent.OnDropInsert]] still `GD.Print`s `"Drop Inserted"` / `"Drop Being Instantiated"` on every drop row — debug noise on a hot path.
- **Stale doc comment / drift hazard.** `EntitySpawnerComponent.cs`'s header comment says its binders are "declared in `entity_spawner_component.tscn`, signals wired in the editor". The live wiring is inline in `game.tscn`; `Scenes/Components/Spawning/entity_spawner_component.tscn` is one of the seven unreferenced duplicate component scenes (see [[02 The Component Framework]]) and must not be cited or edited as the wiring site.
- **Failed joins leave the client in limbo.** `OnJoinPressed` hides the lobby and flips waves before the reducer result is known, and nothing handles a `join_world` failure — the `LocalPlayer` row never arrives, no player spawns, and the lobby stays hidden. There is no rollback path short of restarting the scene.
- **`nearby_remote_players_profiles` is not AOI-filtered** despite the name — it semijoins the profiles of *all* logged-in players, so every client caches every online player's profile row (contrast [[server/spacetimedb/src/player/views.rs#nearby_remote_players#1|nearby_remote_players]], which OR-chains the AOI chunk indices first). Owned by [[06 Movement & Position Sync]]; noted here because join-8's `RemotePlayer._Ready` read-once pattern silently depends on it.
- **`leave_world` has no client caller.** The reducer exists and works, but no UI invokes it — the only paths back to the lobby are death and disconnect (see [[13 Disconnect & Teardown]]).

## Where to go next

Your character is spawned and mirroring rows — [[06 Movement & Position Sync]] covers how it moves and how that movement reaches every other screen. The exit paths this doc referenced (leave, death, disconnect, ghost rows) are [[13 Disconnect & Teardown]]. For the camera handoff in join-5, see [[11 Camera & Presentation]].
