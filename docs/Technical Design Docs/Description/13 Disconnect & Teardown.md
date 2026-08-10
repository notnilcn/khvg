# 13 Disconnect & Teardown

## Assumed knowledge

- [[02 The Component Framework]] — the archetype-helper rule (one shared table list per entity type) that `teardown_profile` is the player-side instance of.
- [[03 Boot & Connection]] — conn-1's saved-token reconnect, conn-2's `client_connected` (and its already-in-world bug), conn-3's scene switch into `main.tscn`.
- [[04 Lobby & Profiles]] — lobby-6's `delete_profile`, the third caller of the teardown helper.
- [[05 Joining the World]] — join-1's row move, join-2's lazy scaffolding, join-3/lobby-7's subscription waves, join-5/join-6's spawner insert/delete paths. This doc is the exit half of that doc's door.
- [[08 Enemies & AI]] — enemy-2/enemy-4's spawn and audience gates, the consumers of the ghost-row guards.
- [[09 Combat & Damage]] — combat-8's death path (`teardown_profile` + row move inside `deal_damage_to_player`).
- [[10 Inventory, Items & Enchantments]] — equip-6's consumable tick, the one scheduled reducer that touches logged-out profiles.
- [[12 Admin & Debug]] — admin-8's `PlayerPositionDebug` mirror, which makes ghost rows visible.
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `end` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

Leaving the game is always a **row move**, never a cleanup: `leave_world`, combat death, and `client_disconnected` all delete the caller's `LoggedInPlayer` row and insert a `LoggedOutPlayer` row — and only death (like `delete_profile`) first runs `teardown_profile`, which wipes the profile's per-row "components". The client needs no exit logic of its own: the row move empties every game-wave view, the resulting delete events despawn the local player, bullet manager, and every remote entity, and the spawner swaps the subscription waves back to the lobby. Because plain logout and disconnect skip teardown, the profile's `PlayerPosition`/`PlayerChunk` rows linger as "ghosts" — never deleted until the profile dies or is deleted, and instead *guarded against* everywhere: the enemy simulation and the remote-player views semijoin `logged_in_player` so a ghost can't attract enemies, spawn new ones, or appear on anyone's screen. Logout is therefore persistence (rejoin keeps level, gear, and position); death is the reset.

## Flowcharts

- [[flowcharts/main-teardown.canvas]] — the composed teardown flow (the client's `sstdbsdk` connection/subscription layer, the spawner component and lobby menu scene, and the server `main`/`player`/`enemy` modules).
![[flowcharts/main-teardown.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/lifecycle_codefile/lifecycle_codefile.canvas]] — deep dive: `lifecycle.rs`, the three lifecycle reducers including `client_disconnected`.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/reducers_codefile/reducers_codefile.canvas]] — deep dive: `player/reducers.rs`, home of `leave_world` and `delete_profile`.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Spawning_subfolder/EntitySpawnerComponent_codefile/EntitySpawnerComponent_codefile.canvas]] — deep dive: `EntitySpawnerComponent.cs`, the client despawn handlers.

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

## Main body

### Three exits, one row move

```sync
![[00 End-to-End Timeline Flowchart#^end-1{seamless:true,title:false,marker:01.}]]
```

The design split to internalize is **identity-level rows vs. per-profile rows**. `LoggedInPlayer`/`LoggedOutPlayer` track *where the player is* (world or lobby); `PlayerData`/`PlayerStats`/`PlayerInventory`/`PlayerPosition`/`PlayerChunk` track *what the profile has*. Every exit moves the first kind; only death and profile deletion touch the second — and when they do, they both go through [[server/spacetimedb/src/player/methods.rs##pub fn teardown_profile|teardown_profile]], whose docstring states the invariant plainly: it is the one shared table list for profile cleanup (the framework's archetype rule from [[02 The Component Framework]]), and callers handle the identity-level rows themselves. That is why combat-8's death keeps the `PlayerProfile` row (the profile survives, its progress doesn't) while lobby-6's `delete_profile` deletes it (the player sits in the lobby, so no row move is needed). One mechanical detail inside the helper: it collects the `ActiveConsumableEffect` ids into a `Vec` *before* deleting, because deleting rows while iterating a btree-index filter skips entries — the same trick `delete_enemy_behavior` uses for behavior trees.

`leave_world`'s guard is worth a glance: it errors "Not in world." unless the caller has a `LoggedInPlayer` row, so it can't be used to duplicate a lobby row — the insert is always paired with the delete in one transaction, and a reducer is atomic, so the player can never transiently exist in both tables or neither.

### The client-side despawn cascade

```sync
![[00 End-to-End Timeline Flowchart#^end-2{seamless:true,title:false,marker:02.}]]
```

There is no "exit" code path on the client at all — despawn is just the spawn machinery of join-4/join-5 running on delete events instead of inserts, which is the payoff of driving everything from rows. Each of [[EntitySpawnerComponent.cs##public partial class EntitySpawnerComponent : Component|EntitySpawnerComponent]]'s delete handlers mirrors its insert handler: look the node up in the tracking dictionary, remove it, `QueueFree` it (Godot's deferred free — the node is destroyed at the end of the frame, so in-flight signal handlers finish safely). `OnLocalPlayerDelete` does double duty because the local player is also the session anchor: it frees the `BulletManager` spawned alongside (join-4), then — behind an `IsActive` check — re-shows the lobby and swaps the waves back. The guard exists because this handler can also fire while the connection is already gone (end-3): re-subscribing a dead connection would be meaningless, so the lobby swap is skipped and only the node cleanup runs. Freeing the `LocalPlayer` also unwinds its own children through Godot's `_ExitTree`: camera-3's `UnregisterCamera` lowers the phantom camera's priority behind an `IsInstanceValid` guard, and move-2's `PositionSyncComponent` stops reporting — the 10 Hz heartbeat ends because the node that sent it is gone, not because anyone told the server.

### Disconnecting

```sync
![[00 End-to-End Timeline Flowchart#^end-3{seamless:true,title:false,marker:03.}]]
```

`client_disconnected` is a SpacetimeDB **lifecycle reducer** — like `init` and `client_connected` (boot-4/conn-2), the host runs it automatically, here whenever a client connection ends, whether the client quit politely or its process died. That symmetry is the point: the server cannot tell a crash from a quit, so the exit path must be safe without any client cooperation, and it is — the row move is the same one end-1 performs voluntarily. Note what the reducer does *not* do: it touches nothing when the player was in the lobby (their `LoggedOutPlayer` row persists, which is exactly how a reconnect is recognized in conn-2), and it never deletes `LoggedOutPlayer` rows at all — every identity that has ever connected keeps a lobby row forever.

The client half is deliberately thin and currently thinner than the server half: [[DatabaseConnector.cs##private void OnDisconnected(DbConnection conn, Exception? e)|OnDisconnected]] logs and nothing else, and [[DatabaseConnector.cs##public void Connect(string host, string dbName)|Connect]] is called exactly once, from `_Ready` — so a dropped connection is permanent for that app run, and the frozen world on screen is the only symptom (see Known gaps).

### Ghost rows: guard, don't clean

```sync
![[00 End-to-End Timeline Flowchart#^end-4{seamless:true,title:false,marker:04.}]]
```

The ghosts exist because teardown is all-or-nothing: `teardown_profile` deletes position rows *and* progress rows in one list, so calling it on logout would wipe the persistence that makes rejoining worthwhile — and writing a second, partial cleanup list would break the one-list-per-entity invariant. The chosen alternative is a read-side filter: every position consumer semijoins `logged_in_player`, turning "is this row's owner actually in the world?" into a cheap per-query check. The guard sites are exactly the five listed in end-4 — three in the enemy simulation (audience, spawning, aggro), one in movement targeting ([[server/spacetimedb/src/enemy/methods.rs##pub fn find_player_pos_by_id|find_player_pos_by_id]] rejects a logged-out identity before even looking at positions, so a `Chase`/`Flee` enemy whose target logged out simply idles), and two in the views that feed other clients. The comment on [[server/spacetimedb/src/enemy/methods.rs##pub fn has_player_in_simulation_range|has_player_in_simulation_range]] states the failure mode being prevented: without the guard, an enemy near a logged-out player's last position would simulate and spawn forever against a ghost.

The cost of the pattern is bounded but real: ghost rows accumulate in `PlayerPosition`/`PlayerChunk` (and their `chunk_index` btree) for every profile that logged out without dying, and they are reclaimed only when the profile next dies or is deleted. With per-profile primary keys the accumulation is one row pair per profile, not per session — a rejoin *reuses* the ghost rather than adding a new one.

### What survives logout

```sync
![[00 End-to-End Timeline Flowchart#^end-5{seamless:true,title:false,marker:05.}]]
```

The persistence is deliberate for the inventory/data rows — that's the rejoin experience — but the consumable tick is an honest edge case: a one-minute buff popped just before logging out keeps draining on the server's 1-second schedule and is gone on return. It breaks nothing (the recompute it triggers only rewrites that profile's own `PlayerStats`/`PlayerData`), so it reads as an unconsidered corner rather than a bug — but nothing in the code says so, which is why it's listed under Known gaps rather than here as intended behavior. The other consumer of ghost positions, admin-8's debug mirror, is the friendly one: because `PlayerPositionDebug` reflects *all* position rows, an admin can count ghosts directly with `spacetime sql`.

### Reconnecting, and the one-way scene door

```sync
![[00 End-to-End Timeline Flowchart#^end-6{seamless:true,title:false,marker:06.}]]
```

Reconnection works because identity is pinned to the saved token (boot-2/conn-1), not to the connection: the same `Identity` comes back, `client_connected` sees the existing lobby row and stands down, and join-2's `is_none()` guards turn the rejoin into a no-op scaffold. The full loop — connect, lobby, join, leave, reconnect, rejoin — is therefore closed entirely by row presence checks, with no session state anywhere. The client scene graph is the one place the loop doesn't close: `lobby_gui.tscn` is entered at boot and never returned to, because [[LobbyGui.cs##public void CharSlotsPressed|CharSlotsPressed]] is the only `ChangeSceneToFile` in the codebase and it points only at `main.tscn` — after end-2's wave swap you are in the *character-select overlay* of the world scene, not back at the main menu. Combined with the missing `LeaveWorld` caller, the practical session lifecycle today is: boot → menu → character select → world → (die or quit) → character select → … → quit.

## Known gaps / stubs

- **`PlayerPosition`/`PlayerChunk` ghost rows persist while logged out.** Verified against the code: `leave_world` ([[server/spacetimedb/src/player/reducers.rs##pub fn leave_world|reducers.rs]]) and `client_disconnected` ([[server/spacetimedb/src/main/lifecycle.rs##pub fn client_disconnected|lifecycle.rs]]) both skip teardown, and the enemy sim guards against the leftovers via `logged_in_player` semijoins (end-4) instead of cleaning up — cleanup happens only in `teardown_profile`, i.e. on death or `delete_profile`. Harmless by design but unbounded across profiles that never die.
- **No disconnect UX on the client.** [[DatabaseConnector.cs##private void OnDisconnected(DbConnection conn, Exception? e)|OnDisconnected]] only prints the error: no auto-reconnect (`Connect` is called once, from `_Ready`), no despawn, no error dialog, no return to the lobby — a dropped client keeps rendering its last world state indefinitely and must be restarted.
- **`leave_world` has no client caller.** Flagged in [[05 Joining the World]] → Known gaps and repeated here because it's this doc's reducer: no "exit to lobby" button exists, so the voluntary exit is reachable only via the CLI; the in-game exits are death and closing the app.
- **Consumable buffs expire while logged out.** [[server/spacetimedb/src/player/reducers.rs##pub fn tick_active_consumable_effects|tick_active_consumable_effects]] has no logged-in guard (end-5), so logging out does not pause a buff's countdown.
- **A disconnected admin keeps the slot claimed.** `is_admin` rides the end-3 row move into `LoggedOutPlayer`, and [[12 Admin & Debug]] → Known gaps already covers the consequence: the single global admin slot stays taken until that identity reconnects and releases it.
- **No exit to the main menu.** The only scene switch in the client is into `main.tscn` (end-6); the post-exit lobby is an in-scene overlay, so returning to `lobby_gui.tscn` requires an app restart.

## Where to go next

This is the last doc in the series — the timeline that started at boot-1 now closes at end-6. To re-read the whole arc as one narrative, go back to [[00 End-to-End Timeline Flowchart]]; for the map of what each doc covers, see [[01 Roadmap]]. For day-to-day maintenance rather than learning, the terse references are [[CLAUDE.md]] at the repo root and the `CLAUDE.md` files in `client/` and `server/`.
