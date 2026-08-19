# 04 Lobby & Profiles

## Assumed knowledge

- [[03 Boot & Connection]] — how the client connects (the `DatabaseConnector` autoload) and how the two subscription waves that feed the lobby get opened (steps conn-4 and conn-5).
- [[02 The Component Framework]] — what a `*Component` is and how a `TableBinderComponent` turns table row events into Godot signals (step conn-6); every UI panel below is driven by that mechanism.
- [[00 End-to-End Timeline Flowchart]] — the global timeline this doc transcludes its steps from.

## The 30-second version

The lobby is where an *identity* (you, the connection) picks a *profile* (a character) to play as. The server models this with two "seat" tables — `LoggedOutPlayer` (in the lobby) and `LoggedInPlayer` (in the world, carrying the chosen `profile_id`) — plus one public `PlayerProfile` row per character, capped at three per identity. The client has two lobbies: `main_menu.tscn` (a pure scene-navigation menu) and the character-select `LobbyComponent` inside the gameplay scene, which lists your profiles as data-driven panels and is the only place that calls the `create_profile` / `delete_profile` / `join_world` reducers. Everything the lobby shows comes from three per-client views (`local_lobby_player`, `local_player_profiles`, `local_player_active_profile`); everything it changes goes through reducers. XP and leveling math also lives in this module — profiles are created bare and only gain stats/position/inventory rows when they first join the world.

## Flowcharts

- [[flowcharts/main-lobby.canvas]] — the composed flow for this doc.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Lobby_subfolder/LobbyComponent_codefile/LobbyComponent_codefile.canvas]] — deep dive: the character-select component (all reducer call sites).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Game_subfolder/LobbyGui_codefile/LobbyGui_codefile.canvas]] — deep dive: the menu scene's navigation script.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]] — deep dive: the server `player/` module (tables, views, reducers, methods).

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^lobby-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^lobby-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^lobby-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^lobby-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^lobby-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^lobby-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^lobby-7{seamless:true,title:false,marker:07.}]]
```

## Main body

### Two scenes, two "lobbies"

```sync
![[00 End-to-End Timeline Flowchart#^lobby-1{seamless:true,title:false,marker:08.}]]
```

The file names read swapped, and it's worth internalizing: `Scenes/main_menu.tscn` is the *menu* (the project's main scene, per `project.godot`), while `Scenes/game.tscn` is the gameplay scene that also hosts the character-select. [[client/Scripts/Game/LobbyGui.cs|LobbyGui]] is a 30-line `Control` script with four handlers, all wired by the `[connection]` blocks at the bottom of [[client/Scenes/main_menu.tscn|main_menu.tscn]]: [[client/Scripts/Game/LobbyGui.cs#CharSlotsPressed#1|CharSlotsPressed]] swaps the whole scene tree to `game.tscn` via `ChangeSceneToFile`, while [[client/Scripts/Game/LobbyGui.cs#ServerListPressed#1|ServerListPressed]] / [[client/Scripts/Game/LobbyGui.cs#SettingsPressed#1|SettingsPressed]] / [[client/Scripts/Game/LobbyGui.cs#BackPressed#1|BackPressed]] just show/hide three sibling `Panel` nodes. The ServerList and Settings panels are empty `ScrollContainer`s — there is no server browser or settings logic behind them.

Nothing in `main_menu.tscn` touches SpacetimeDB. That is deliberate ordering: the `DatabaseConnector` autoload connects in the background while the menu is up (conn-2), and no subscription exists until `game.tscn` loads (conn-4). So by the time you click **Character Slots**, your identity is already authenticated and the server has already run `client_connected` for you — the menu never has to wait on the network.

### The server's lobby model: seats and profiles

```sync
![[00 End-to-End Timeline Flowchart#^conn-3{seamless:true,title:false,marker:09.}]]
```

The server identifies you by your SpacetimeDB `Identity` (a stable hash derived from your auth token — the same value the client prints as `--pN`-suffixed for parallel debug instances). Your lobby state is modeled as a *seat* in exactly one of two tables in [[server/spacetimedb/src/player/tables.rs|player/tables.rs]]:

- [[server/spacetimedb/src/player/tables.rs#LoggedOutPlayer#1|LoggedOutPlayer]] — the lobby seat: `player_id`, `username`, `is_admin`. Note it is declared **without** `public`, so clients cannot subscribe to it directly; it is only visible through the `local_lobby_player` view below.
- [[server/spacetimedb/src/player/tables.rs#LoggedInPlayer#1|LoggedInPlayer]] — the in-world seat: the same fields plus a `#[unique]` `profile_id` recording which character you're playing. This one *is* public, because other systems (AOI views, `nearby_remote_players`' semijoin) filter on it.

Joining the world is literally moving the row from one table to the other ([[server/spacetimedb/src/player/reducers.rs#join_world#1|join_world]] deletes the `LoggedOutPlayer` row and inserts a `LoggedInPlayer` row with the `profile_id` attached), and leaving reverses it. Because a reducer is transactional, you can never occupy both seats or neither.

[[server/spacetimedb/src/player/tables.rs#PlayerProfile#1|PlayerProfile]] is the character itself: an auto-increment `profile_id`, the owner `player_id`, `profile_name`, a `texture_id` naming the sprite set, and two gameplay toggles (`aim_assist`, `lock_on`). It carries a `by_player_profile_name` btree index on `(player_id, profile_name)` so name lookups stay indexed. A brand-new identity gets one default "Knight" profile for free from [[server/spacetimedb/src/main/lifecycle.rs#client_connected#1|client_connected]], so the character-select is never empty.

Almost every reducer in the game starts with one of the two lobby guards in [[server/spacetimedb/src/player/methods.rs|player/methods.rs]]: [[server/spacetimedb/src/player/methods.rs#require_in_lobby#1|require_in_lobby]] (seat must be in `LoggedOutPlayer` — used by `create_profile`/`delete_profile`/`join_world`) and [[server/spacetimedb/src/player/methods.rs#require_in_world#1|require_in_world]] (seat must be in `LoggedInPlayer` — used by everything in-world). The seat tables are therefore the state machine that decides which reducer set you may call at all.

### The three lobby views

```sync
![[00 End-to-End Timeline Flowchart#^lobby-3{seamless:true,title:false,marker:10.}]]
```

A **view** in SpacetimeDB 2.2 is a per-client query: it runs with `ctx.sender()` bound to *your* identity, so every subscriber sees a different row set. The lobby uses three, all in [[server/spacetimedb/src/player/views.rs|player/views.rs]]:

- [[server/spacetimedb/src/player/views.rs#local_lobby_player#1|local_lobby_player]] — your `LoggedOutPlayer` row and nothing else. This is the *only* way the client can read the non-public seat table, which is why `DatabaseConnector` wires it directly (no binder): [[client/sstdbsdk/DatabaseConnector.cs#OnConnected#1|OnConnected]] hooks the generated table's `OnInsert`/`OnUpdate` and copies `p.Username` into the `Username` property. (Nothing renders `Username` yet — see Known gaps.)
- [[server/spacetimedb/src/player/views.rs#local_player_profiles#1|local_player_profiles]] — all `PlayerProfile` rows you own; this drives the panel list (next subsection). It's part of the lobby subscription wave (`LobbyTables` in [[client/sstdbsdk/TableSubscriber.cs#LobbyTables#1|TableSubscriber]], opened at conn-5) and dropped by `UnsubscribeLobby()` when you join.
- [[server/spacetimedb/src/player/views.rs#local_player_active_profile#1|local_player_active_profile]] — the single profile your `LoggedInPlayer` seat points at. When you're logged out there is no seat, so it resolves `profile_id` to the sentinel `u64::MAX`, which matches no row — a neat way to make the view naturally empty in the lobby without a special case. In-world it feeds [[client/Scripts/Components/Visual/LocalPlayerProfileComponent.cs#OnProfileRowInserted#1|LocalPlayerProfileComponent]] (name + sprite on your `local_player.tscn` entity, join-6) and is read directly by `CombatComponent.OnAimSettingsChanged` for the aim toggles.

### The character-select screen

```sync
![[00 End-to-End Timeline Flowchart#^lobby-2{seamless:true,title:false,marker:11.}]]
```

The markup for this screen lives in [[client/Scenes/Components/Lobby/lobby_component.tscn|lobby_component.tscn]], instanced once under the `UI` CanvasLayer of [[client/Scenes/game.tscn|game.tscn]]; the scene file also sets the `PlayerProfileScene` export to `profile_panel.tscn` and wires the three button/binder signals its script consumes. [[client/Scripts/Components/Lobby/LobbyComponent.cs|LobbyComponent]] is a `ControlComponent`, so it registers on the `game` root's `GameManager` entity and reaches its sibling `TableSubscriber` through the framework's `GetSibling<T>()` (an `Entity.GetComponent<T>()` lookup, not a Godot sibling walk) rather than node paths.

```sync
![[00 End-to-End Timeline Flowchart#^lobby-4{seamless:true,title:false,marker:12.}]]
```

Two wiring details matter here. First, the `LocalPlayerProfilesBinder` declared in `lobby_component.tscn` has `ReplayExistingRows = true`, so profiles that arrived before the component finished registering still come through the insert path — without replay, a fast subscription would leave the list empty. Second, only `RowInserted` is connected (to [[client/Scripts/Components/Lobby/LobbyComponent.cs#OnProfileRowInserted#1|OnProfileRowInserted]]); there is no `RowDeleted` handler at all, which forces the delete flow to clean up panels manually (below). Each panel is an instance of [[client/Scenes/profile_panel.tscn|profile_panel.tscn]] — a `Panel` with `Name`/`ID` labels, Join/Delete buttons, and a placeholder noise-texture `Icon` (the profile's `texture_id` is *not* shown in the lobby; it only takes effect in-game) — with the buttons' `Pressed` signals connected in C# to closures capturing that row's `PlayerProfile` value, so every panel carries its own identity.

The camera handoff in [[client/Scripts/Components/Lobby/LobbyComponent.cs#ShowLobby#1|ShowLobby]] works because the phantom-camera plugin gives the viewport to whichever `PhantomCamera2D` has the highest priority: raising the scene-level `MainMenuPhantomCamera2D` to 60 keeps a static menu camera in charge until your player entity registers its own camera at the same priority (join-5). The full handoff is [[11 Camera & Presentation]]'s story; the lobby only owns the "menu camera wins while no player exists" half. Note also the modal quirk shared by all three button handlers: [[client/Scripts/Components/Lobby/LobbyComponent.cs#OnJoinPressed#1|OnJoinPressed]] and [[client/Scripts/Components/Lobby/LobbyComponent.cs#OnDeletePressed#1|OnDeletePressed]] refuse to run while `CreateProfilePanel` is visible, and [[client/Scripts/Components/Lobby/LobbyComponent.cs#OnCreatePressed#1|OnCreatePressed]] refuses to run unless it is — the create panel acts as a modal that locks the list behind it.

### Creating a profile

```sync
![[00 End-to-End Timeline Flowchart#^lobby-5{seamless:true,title:false,marker:13.}]]
```

On the client, [[client/Scripts/Components/Lobby/LobbyComponent.cs#OnCreatePanelPressed#1|OnCreatePanelPressed]] just toggles `CreateProfilePanel`; [[client/Scripts/Components/Lobby/LobbyComponent.cs#OnCreatePressed#1|OnCreatePressed]] reads the `LineEdit` and calls `conn.Reducers.CreateProfile(name, "Knight")` — the texture id is hardcoded, so every created profile is a Knight regardless of name. On the server, [[server/spacetimedb/src/player/reducers.rs#create_profile#1|create_profile]] enforces four invariants in order: you must be in the lobby (the seat guard), the name must be 1–20 non-whitespace characters, you must own fewer than three profiles, and the name must not collide with one of your existing profiles *case-insensitively* (a `to_lowercase` comparison — the same rule [[server/spacetimedb/src/player/methods.rs#find_profile_by_name#1|find_profile_by_name]] uses, so admin lookups can't address a profile you couldn't have created). It then inserts the bare row with `aim_assist`/`lock_on` false and lets `profile_id` auto-increment.

The reason creation inserts *only* the profile row is the scaffolding split: a `PlayerProfile` is cheap metadata, while the ~44 per-profile rows (data, stats, 38 inventory slots, position, rotation, chunk) only have meaning once the character enters the world — so they're deferred to `try_scaffold_profile` at join (join-2). Creation therefore stays one row insert, and the new panel appears with no extra client code because the inserted row flows back through the `local_player_profiles` view into the same replaying binder from lobby-4. That round trip is the whole pattern: call the reducer, mirror what comes back.

### Deleting a profile

```sync
![[00 End-to-End Timeline Flowchart#^lobby-6{seamless:true,title:false,marker:14.}]]
```

[[server/spacetimedb/src/player/reducers.rs#delete_profile#1|delete_profile]] re-checks both guards — lobby seat and `profile.player_id == ctx.sender()` — because reducers must assume a hostile client (the panel's Delete button is convenience, not security). The heavy lifting is [[server/spacetimedb/src/player/methods.rs#teardown_profile#1|teardown_profile]], the shared "archetype cleanup" helper also used by the death path (doc 09): it deletes the profile's `PlayerData`, `PlayerStats`, `PlayerStatAllocation`, every `PlayerInventorySlot`, `PlayerPosition`, `PlayerChunk`, and all `ActiveConsumableEffect` rows. Only then is the `PlayerProfile` row itself deleted — order matters because the per-profile rows key off `profile_id`, so removing the profile first would orphan them. One omission: `teardown_profile` does **not** delete the `PlayerRotation` row, even though `try_scaffold_profile` inserts one — see Known gaps.

The client half ([[client/Scripts/Components/Lobby/LobbyComponent.cs#OnDeletePressed#1|OnDeletePressed]]) is a consequence of the binder being insert-only: since no `RowDeleted` handler will ever fire, the handler itself walks the `VBoxContainer`, finds the panel whose `ID` label matches the deleted `profile_id`, and `QueueFree()`s it. The panel disappears immediately without waiting for the server round trip, but the flip side is that a profile deleted by *any other path* leaves a stale panel until the scene reloads (Known gaps).

### XP, levels, and what `PlayerData` means

```sync
![[00 End-to-End Timeline Flowchart#^lobby-7{seamless:true,title:false,marker:15.}]]
```

[[server/spacetimedb/src/player/tables.rs#PlayerData#1|PlayerData]] is the progression row that `try_scaffold_profile` will create at first join: `level`, `xp`, `hp`, `max_hp`, plus two modifier-only resolved values (`defense`, `base_speed`) that live here rather than in `PlayerStats` because they're never allocatable. The math behind `level` and `max_hp` is three small functions in [[server/spacetimedb/src/player/methods.rs|player/methods.rs]] over constants from [[server/spacetimedb/src/main/global.rs|main/global.rs]]:

- [[server/spacetimedb/src/player/methods.rs#xp_for_level#1|xp_for_level]]`(n) = 100·(n−1)·n/2` — triangular thresholds, so level 2 costs 100 XP, level 3 a cumulative 300, level 4 a cumulative 600. Triangular growth means each level costs 100 more than the previous increment.
- [[server/spacetimedb/src/player/methods.rs#compute_level#1|compute_level]] walks those thresholds upward from 1 — total XP is stored, level is always *derived*, so the two can never disagree.
- [[server/spacetimedb/src/player/methods.rs#compute_base_max_hp#1|compute_base_max_hp]]`(level) = BASE_MAX_HP (100) + (level−1)·HP_PER_LEVEL (5)`.

XP enters only from combat: killing an enemy calls [[server/spacetimedb/src/player/methods.rs#internal_gain_xp#1|internal_gain_xp]] (from `combat/mod.rs`), which adds the XP, re-derives the level, and on a level-up does three things — heals by exactly the gained levels' `HP_PER_LEVEL` increase (so leveling while hurt doesn't fully restore you), banks `(levels gained)·SKILL_POINTS_PER_LEVEL (2)` unspent points into the `PlayerStatAllocation` row (spent later by the `allocate_stat` reducer, doc 10), and re-runs [[server/spacetimedb/src/player/methods.rs#recompute_stats#1|recompute_stats]] so gear-multiplied `max_hp` and the HP clamp settle on the new level. None of this has lobby UI today — the profile panel shows only name and id — but it defines what the numbers on a profile will mean the moment it joins.

For completeness, this module also hosts [[server/spacetimedb/src/player/methods.rs#find_profile_by_name#1|find_profile_by_name]], a case-insensitive `(identity, name)` lookup used by the admin/item reducers ([[server/spacetimedb/src/main/admin.rs|admin.rs]], [[server/spacetimedb/src/item/reducers.rs|item/reducers.rs]]) to target "player X's profile named Y" — the server-side mirror of the duplicate-name rule in `create_profile`.

### Profile-scoped settings that go nowhere (yet)

`PlayerProfile.aim_assist` and `PlayerProfile.lock_on` are read in-game — `CombatComponent.OnAimSettingsChanged` copies them off `LocalPlayerActiveProfile` whenever `LocalPlayerProfileComponent` raises `AimSettingsChanged` — but both are written only as `false` (in `client_connected` and `create_profile`), and no reducer exists to change them. They are schema-ahead-of-feature: the combat code already honors them, the lobby has no UI to set them.

## Known gaps / stubs

- **`LobbyGui` is scene navigation only.** Its Server List and Settings panels are empty show/hide shells, and no reducer call is wired anywhere in `main_menu.tscn` — including [[server/spacetimedb/src/player/reducers.rs#set_username#1|set_username]], which exists server-side but has no client caller, so the empty `username` written by `client_connected` can never be changed (and the `Username` property `DatabaseConnector` maintains is never rendered anywhere).
- **Leftover debug print:** [[client/Scripts/Components/Lobby/LobbyComponent.cs#OnProfileRowInserted#1|LobbyComponent.OnProfileRowInserted]] ends in a `GD.Print($"Successfully synced a profile: …")` on every profile row.
- **Unwired BACK button:** the `Panel/CenterContainer/Button` ("BACK") in [[client/Scenes/Components/Lobby/lobby_component.tscn|lobby_component.tscn]] has no `[connection]` and no matching handler in `LobbyComponent.cs` — the only way out of the character-select is joining a world (or leaving it, which re-opens the lobby).
- **Insert-only profile list:** the `LocalPlayerProfilesBinder` wires only `RowInserted`, so panel removal is manual and local to [[client/Scripts/Components/Lobby/LobbyComponent.cs#OnDeletePressed#1|OnDeletePressed]]. A profile deleted through any other path (e.g. an admin reducer) leaves a stale panel until the scene reloads.
- **`teardown_profile` leaks rotation rows:** it deletes position and chunk rows but not the `PlayerRotation` row that `try_scaffold_profile` inserted, so every deleted profile leaves a dangling rotation row behind.
- **`aim_assist` / `lock_on` are unreachable:** no reducer or UI ever sets them true (see the section above).
- **Create hardcodes the texture:** `OnCreatePressed` always passes `"Knight"` as `texture_id`, and the profile panel's `Icon` is a placeholder noise texture — `texture_id` has no lobby presentation.

## Where to go next

Pressing Join is where this doc ends and [[05 Joining the World]] begins — `OnJoinPressed` flips the subscription waves and `join_world` scaffolds the profile's rows. For how a profile's rows are cleaned up on death, disconnect, and leave, read [[13 Disconnect & Teardown]]; for what the 38 scaffolded inventory slots mean, [[10 Inventory, Items & Enchantments]].
