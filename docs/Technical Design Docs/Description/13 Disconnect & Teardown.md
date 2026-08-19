# 13 Disconnect & Teardown

## Assumed knowledge

- [[00 End-to-End Timeline Flowchart]] — the whole-game timeline this doc transcludes from; the `# Disconnect & Teardown` section there is the compressed version of everything below.
- [[02 The Component Framework]] — what a `*Component` and a `TableBinderComponent` are, and how binder row events become Godot signals.
- [[03 Boot & Connection]] — the `DatabaseConnector` autoload, the connection lifecycle, and the three subscription waves (base/lobby/game).
- [[04 Lobby & Profiles]] — `LoggedInPlayer` vs `LoggedOutPlayer`, the `PlayerProfile` row, and `delete_profile` (one of `teardown_profile`'s two callers).
- [[05 Joining the World]] — `join_world` / `try_scaffold_profile`, the game-wave subscription, and how `EntitySpawnerComponent` turns view rows into nodes. Teardown is this doc run in reverse.
- [[09 Combat & Damage]] — `deal_damage_to_player`, the zero-hp path that triggers the death teardown.

## The 30-second version

There are four ways out of the world, and they are deliberately *not* symmetric. **Leaving voluntarily** (`leave_world`) and **disconnecting** (`client_disconnected`) only move your identity row from `LoggedInPlayer` back to `LoggedOutPlayer` — all your profile rows (stats, inventory, position) survive, so a rejoin resumes where you left off; the position rows left behind by a disconnect are "ghosts" that the enemy simulator explicitly ignores rather than deletes. **Death** (`deal_damage_to_player` at zero hp) and **`delete_profile`** run `teardown_profile`, the one shared cleanup list that deletes every per-profile row, so the next join re-scaffolds the character from scratch. On the client, every exit while still connected funnels through a single event — your `LocalPlayer` view row disappearing — which makes `EntitySpawnerComponent` free your player node, re-show the lobby, and flip the subscription waves back from game to lobby. Quitting the app entirely is handled by Godot's `_ExitTree` callbacks: the connector disconnects, and every binder unhooks its table event handlers so nothing calls back into freed nodes.

## Flowcharts

- [[flowcharts/main-teardown.canvas]] — the composed teardown flow (server lifecycle reducers, `teardown_profile`, client despawn and unsubscribe paths).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/lifecycle_codefile/lifecycle_codefile.canvas]] — deep dive: `init`, `client_connected`, `client_disconnected`.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/methods_codefile/methods_codefile.canvas]] — deep dive: `teardown_profile` next to its inverse `try_scaffold_profile`.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Spawning_subfolder/EntitySpawnerComponent_codefile/EntitySpawnerComponent_codefile.canvas]] — deep dive: the insert/delete handlers that spawn and free every puppet.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^end-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^end-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^end-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^end-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^end-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^end-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^end-7{seamless:true,title:false,marker:07.}]]
```

## The identity row is the whole protocol

Everything in this doc reduces to one design decision: **"in the world" is a row, not a connection.** `join_world` moved your identity from the `LoggedOutPlayer` table to the `LoggedInPlayer` table (carrying the chosen `profile_id`), and every exit path is some variation on moving it back. That single row is load-bearing in three directions at once:

- The per-caller views (`LocalPlayer`, `LocalPlayerData`, `LocalPlayerInventory`, …) are all keyed off your `LoggedInPlayer` row, so deleting it silently empties every game-wave view for your client — one delete is the entire "you left the world" broadcast.
- The AOI views other clients see you through — [[server/spacetimedb/src/player/views.rs#nearby_remote_players#1|nearby_remote_players]] and `nearby_remote_player_rotations` — left-semijoin `logged_in_player`, so deleting the row also erases you from everyone else's screens even if your `PlayerPosition` row still exists (this is what makes the ghost rows of step 04 invisible).
- The server-side guards [[server/spacetimedb/src/player/methods.rs#require_in_world#1|require_in_world]] / `require_in_lobby` read the same two tables, so every gameplay reducer starts failing the moment you leave — a disconnected player can't be impersonated by a replayed reducer call because there is no connection, and a lobby-seated player calling `report_movement` gets `"Not in world."`.

Keep this in mind below: each exit path differs only in *what else* it deletes besides the identity row.

## Path 1 — voluntary leave

```sync
![[00 End-to-End Timeline Flowchart#^end-1{seamless:true,title:false,marker:01.}]]
```

`leave_world` is the minimal exit: it guards on actually being in the world (the `LoggedInPlayer` lookup doubles as the guard and the data source, since the new `LoggedOutPlayer` row copies `username`/`is_admin` out of it), then swaps the identity tables and returns. Nothing else is touched — not `PlayerData`, not the 38 inventory slots, not `PlayerPosition`. That is deliberate: leaving is framed as "stepping out of the world", the exact inverse of `join_world`'s identity move, and because [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile#1|try_scaffold_profile]] only inserts rows that are missing, a later rejoin finds the full scaffold intact and resumes — including at your old position, since the surviving `PlayerPosition` row is never rewritten at join time.

The gap named in the step is real: grepping the client, `LeaveWorld` appears only inside the generated bindings — no component, button, or key binding calls it. The reducer exists, is correct, and is unreachable from the shipped UI.

## Path 2 — death (the real teardown)

```sync
![[00 End-to-End Timeline Flowchart#^end-2{seamless:true,title:false,marker:02.}]]
```

`teardown_profile` is the server-side mirror of "free the entity's component tree" — the client composes a player from components, and the server composes one from rows joined by `profile_id`, so destroying the entity means deleting one row set per concern. The exact list in [[server/spacetimedb/src/player/methods.rs#teardown_profile#1|teardown_profile]]: `PlayerData` (level/xp/hp), `PlayerStats`, `PlayerStatAllocation`, every `PlayerInventorySlot` (looped, because slots are individual rows keyed by `slot_id`, not one row with a list), `PlayerPosition`, `PlayerChunk`, and every `ActiveConsumableEffect` (also looped, keyed by `effect_id`). Its doc comment spells out the contract: this is *the one shared table list* — callers handle the identity-level rows themselves, because the two callers want different endings:

- **Death** — [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_player#1|deal_damage_to_player]] at zero hp calls `teardown_profile`, then moves the identity to `LoggedOutPlayer` but **keeps `PlayerProfile`**. Your character still exists in the lobby with its name and texture; its progress and gear are gone.
- **Profile deletion** — [[server/spacetimedb/src/player/reducers.rs#delete_profile#1|delete_profile]] (lobby-only, ownership-checked) calls `teardown_profile` and then deletes the `PlayerProfile` row itself. Nothing survives.

The asymmetry with path 1 is the design: *leaving* preserves, *dying* resets. That is what makes death the roguelike loop — run ends, character persists, gear and levels do not.

One subtlety the step flags: `teardown_profile` does **not** delete `PlayerRotation`. The rotation table was split out of `PlayerPosition` (it streams on a faster cadence) *after* the teardown list was written, and the list was never updated. The orphan is harmless to other clients (the rotation AOI view also semijoins `logged_in_player`, so the ghost is invisible) and `try_scaffold_profile` skips re-inserting it, so a re-joined profile keeps its last facing — but it is a genuine leak in the shared cleanup list, see Known gaps.

## Path 3 — the client reacts (all connected exits)

```sync
![[00 End-to-End Timeline Flowchart#^end-3{seamless:true,title:false,marker:03.}]]
```

Why is one delete enough to drive the whole client-side exit? Because of the view design from §"The identity row is the whole protocol": the `LocalPlayer` view is your `LoggedInPlayer` row filtered to your identity, so *every* connected exit — leave, death, or the server-side half of a disconnect race — deletes that row, and the game-wave subscription delivers the delete. [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnLocalPlayerDelete#1|EntitySpawnerComponent.OnLocalPlayerDelete]] (wired inline in [[client/Scenes/game.tscn|game.tscn]] as `LocalPlayerBinder.RowDeleted → OnLocalPlayerDelete`) then does four things, in an order that matters:

1. `localPlayer.QueueFree()` — frees the `local_player.tscn` entity. `QueueFree` defers the free to end of frame, so the row-delete handler itself can safely finish executing on nodes that are about to die. The free cascades: [[client/Scripts/Players/Local/LocalPlayer.cs#_ExitTree#1|LocalPlayer._ExitTree]] disposes the 3D backdrop mirror (`_model3D?.Dispose()`), clears the 3D camera's follow target, and calls [[client/Scripts/Components/Camera/Camera2DPresenterComponent.cs#UnregisterCamera#1|Camera2DPresenterComponent.UnregisterCamera]], which drops the player phantom camera's priority to 0 — the camera handoff of join-5 run backwards. Both 3D/2D follow-target clears are `IsInstanceValid`-guarded because scene-level nodes may already be gone if the whole scene is exiting.
2. An early return if the connection is already dead (`Conn?.IsActive != true`) — if the row disappeared because the *connection* died, there is nothing to resubscribe, and the steps below would just throw.
3. `GetSibling<LobbyComponent>()?.ShowLobby()` — [[client/Scripts/Components/Lobby/LobbyComponent.cs#ShowLobby#1|ShowLobby]] re-shows the character-select UI and raises the menu `MainMenuPhantomCamera2D` back to priority 60, so the viewport has an owner again the moment the player's pcam drops. The two camera priority writes (step 1 down, step 3 up) are what make the death→lobby transition seamless instead of a black frame.
4. `SubscribeLobby()` + `UnsubscribeGame()` on the sibling [[client/sstdbsdk/TableSubscriber.cs|TableSubscriber]] — [[client/sstdbsdk/TableSubscriber.cs#UnsubscribeGame#1|UnsubscribeGame]] calls `Unsubscribe()` on the stored `SubscriptionHandle` and nulls the field, so a later death can't double-unsubscribe; [[client/sstdbsdk/TableSubscriber.cs#SubscribeLobby#1|SubscribeLobby]] reopens the lobby wave (`LocalLobbyPlayer`, `LocalPlayerProfiles`). The base wave (`AllTextures`/`AllItems`/`AllEnchantments`) is never torn down — catalogs are needed by every screen, so it lives for the whole connection.

Note what does *not* happen: no scene change. The lobby the player returns to is `LobbyComponent` inside `game.tscn`, not the `main_menu.tscn` menu scene. [[client/Scripts/Game/LobbyGui.cs|LobbyGui]]'s only scene transition is *into* the game (`CharSlotsPressed` → `ChangeSceneToFile("res://Scenes/game.tscn")`); there is no path back to the menu scene short of quitting the app, and nothing in the codebase calls `ChangeSceneToFile` with `main_menu.tscn`. The menu scene is a launcher; the lobby is a UI panel.

## Path 4 — disconnect and the ghost rows

```sync
![[00 End-to-End Timeline Flowchart#^end-4{seamless:true,title:false,marker:04.}]]
```

[[server/spacetimedb/src/main/lifecycle.rs#client_disconnected#1|client_disconnected]] is a SpacetimeDB **lifecycle reducer** — the runtime calls it when a connection drops, no client code involved — and it is deliberately the *cheap* exit: identity row moves to `LoggedOutPlayer`, nothing else. If the player was already lobby-seated (never joined, or already left), it does nothing at all, so it can't duplicate a `LoggedOutPlayer` row.

The rows it leaves behind — `PlayerPosition`, `PlayerChunk`, and (via the rotation gap) `PlayerRotation` — are the **ghosts**. They are kept rather than swept for a reason: `try_scaffold_profile`'s insert-if-missing guards mean a reconnecting player resumes at their last position instead of the world origin, which is exactly what you want for a dropped connection in an MMO. The cost is that the tables accumulate rows for players who are not online, and every system that iterates positions must decide what to do about them. The codebase's answer is *guard, don't sweep*:

- The 100 ms behavior tick simulates an enemy only if [[server/spacetimedb/src/enemy/methods.rs#has_player_in_simulation_range#1|has_player_in_simulation_range]] finds a nearby `PlayerPosition` **whose `profile_id` still has a `LoggedInPlayer` row** — the inline comment says it plainly: without the filter, "an enemy near a logged-out player's last known position would simulate/spawn forever against a ghost."
- The 2 s spawn tick applies the same check in [[server/spacetimedb/src/enemy/methods.rs#has_player_near_spawn_point#1|has_player_near_spawn_point]], so a ghost can't hold a `BiomeRegion` open and keep enemies spawning around an empty spot.
- Targeting is ghost-proofed the same way: [[server/spacetimedb/src/enemy/methods.rs#find_nearest_player_id#1|find_nearest_player_id]] and `find_player_pos_by_id` both require the `logged_in_player` row before an enemy may aggro or aim at a position.

On every *other* client, the disconnect is observed as ordinary row deletes: the victim's `PlayerPosition` drops out of their `NearbyRemotePlayers` view (the semijoin, not an AOI shrink — step 04 says the same), and [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnNearbyRemotePlayerDelete#1|OnNearbyRemotePlayerDelete]] removes the puppet from its dictionary and `QueueFree`s it. Enemies and drops near the departed player do **not** despawn on other clients — those rows belong to the world, not to the player, and each client's own AOI still covers them.

On the disconnecting client itself there is no graceful path at all, and none is needed: the process (or at least the connection) is going away, the server cleans up the identity row, and the next connect starts fresh from the lobby wave.

## Path 4b — the reconnect sharp edge

```sync
![[00 End-to-End Timeline Flowchart#^end-5{seamless:true,title:false,marker:05.}]]
```

`client_connected` handles three cases: already in world (the bug), already in lobby (no-op — the normal reconnect, since `client_disconnected` already seated you), and brand-new identity (insert `LoggedOutPlayer` + a default "Knight" `PlayerProfile`). The first case "can't happen" in the current flow because `client_disconnected` always runs first — but SpacetimeDB does not guarantee lifecycle ordering across a flapping connection, and if it ever does happen, [[server/spacetimedb/src/main/lifecycle.rs#client_connected#1|client_connected]] responds by *deleting* your `LoggedInPlayer` row and seating you in the lobby, destroying your in-world session state (your views empty, your client — which thinks it is in the world — sees its `LocalPlayer` row vanish and snaps back to the lobby via step 03). The only record that this is unintended is the text of the `log::error!` call; there is no code comment and no tracking issue. A kinder implementation would reject the duplicate connection or treat it as a resume. See Known gaps.

## Path 5 — quitting the app (client-side teardown)

```sync
![[00 End-to-End Timeline Flowchart#^end-6{seamless:true,title:false,marker:06.}]]
```

Godot calls `_ExitTree()` on every node as it leaves the scene tree — on `QueueFree`, on scene change, and on shutdown — and the connection layer uses it as its disposal hook, which is why there is no explicit "cleanup" call anywhere in game code. [[client/sstdbsdk/DatabaseConnector.cs#_ExitTree#1|DatabaseConnector._ExitTree]] runs when the autoload is torn down (app quit): `Conn.Disconnect()` closes the socket if still active — that close is what triggers the server's `client_disconnected` of step 04 — and the `Instance` singleton is nulled so no late-running code grabs a half-dead connector. (The SDK also fires [[client/sstdbsdk/DatabaseConnector.cs#OnDisconnected#1|OnDisconnected]], which today only prints errors — no reconnect logic, no UI.)

The more pervasive half is [[client/sstdbsdk/TableBinderComponent.cs#_ExitTree#1|TableBinderComponent._ExitTree]]. Every binder registered C# event handlers (`OnInsert`/`OnUpdate`/`OnDelete`) against tables on the *autoload-owned* connection — an object that outlives every scene. Without unregistration, freeing a puppet or changing scenes would leave the connection holding delegates into freed Godot objects, and the next row event would dispatch into a dangling target. So each binder's `_ExitTree` walks its `_bindings` list, calls `RemoveEventHandler` for each, and clears the list. This is the symmetric counterpart of `Bind`: subscription wiring is acquired per-node-lifetime and released per-node-lifetime, while the subscription *waves* (step 03) are managed separately at scene lifetime by `TableSubscriber`. The net effect: despawning a hundred enemies on an AOI shrink registers zero leaks, because each enemy's binders unhook themselves as the scene tree tears them down.

## Closing the loop — rejoining after each exit

```sync
![[00 End-to-End Timeline Flowchart#^end-7{seamless:true,title:false,marker:07.}]]
```

`try_scaffold_profile` is the hinge that makes the four exit paths feel different on return. Every insert in it is guarded by an `is_none()` check (`PlayerData`, the slot set, position, rotation, chunk), so the join cost and the resume semantics are entirely determined by which rows the exit deleted:

- After **leave** or **disconnect**: everything survives → scaffold is a no-op → same gear, same level, same position. The ghost rows are a feature here.
- After **death**: `teardown_profile` deleted the scaffold rows (but not `PlayerProfile`) → full re-scaffold → level 1, starter kit (Bow, Bread, Hat, Helmet, Skull, Tome of Mending), world origin — except the rotation ghost, which survives and is reused.
- After **`delete_profile`**: there is no profile to join with; the lobby's CREATE flow inserts a fresh bare `PlayerProfile`, and the first join scaffolds it.

## Known gaps / stubs

- **Ghost `PlayerPosition`/`PlayerChunk` rows (the assigned gap for this doc).** Disconnect and voluntary leave never delete the position/chunk rows; they persist until death or `delete_profile` runs `teardown_profile`. Every consumer guards instead of cleaning: the enemy sim filters on `logged_in_player` (`has_player_in_simulation_range`, `has_player_near_spawn_point`, `find_nearest_player_id`, `find_player_pos_by_id`), and the `nearby_remote_players*` views semijoin the same table. No sweeper exists, so rows for never-returning players live until the next publish wipes the database.
- **`teardown_profile` misses `PlayerRotation`.** The rotation table was split out of `PlayerPosition` after the teardown list was written, and the list was never updated — a rotation ghost survives even full teardown (death/`delete_profile`). Harmless but real: invisible to clients (the rotation view semijoins `logged_in_player`) and silently reused on rejoin.
- **`leave_world` has no client caller.** The reducer exists and works; `LeaveWorld` appears only in the generated bindings. The only in-game paths back to the lobby are death and disconnecting.
- **`client_connected` already-in-world bug (shared with [[03 Boot & Connection]]).** A reconnect that finds a stale `LoggedInPlayer` row is handled by deleting it and dropping the player to the lobby — flagged as a bug only inside the `log::error!` message text, not a comment.
- **No path back to the menu scene.** `LobbyGui`'s Server List and Settings panels are show/hide stubs, and nothing calls `ChangeSceneToFile` for `main_menu.tscn` — once past the launcher, the only way back to it is restarting the app.
- **Stale wiring comment.** `EntitySpawnerComponent.cs`'s doc comment says its binders are "declared in `entity_spawner_component.tscn`, signals wired in the editor" — that scene is one of the seven unreferenced duplicate component scenes; the live binder declarations and `RowInserted`/`RowDeleted` connections are inline in `game.tscn`. Reading the comment instead of the scene sends you to a dead file.

## Where to go next

This is the last system doc — the loop it closes (lobby → join → play → exit → lobby) starts over in [[03 Boot & Connection]] and [[05 Joining the World]]. If you arrived here chasing the death path, [[09 Combat & Damage]] owns everything up to the zero-hp branch; if you are auditing row lifecycles across the whole schema, [[02 The Component Framework]] explains the one-table-per-concern model that `teardown_profile`'s delete list mirrors.
