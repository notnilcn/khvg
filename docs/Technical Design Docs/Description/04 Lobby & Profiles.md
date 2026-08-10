# 04 Lobby & Profiles

## Assumed knowledge

- [[03 Boot & Connection]] — the connection, the `client_connected` reducer that lands every identity in the lobby, and the base/lobby subscription waves this doc's UI consumes.
- [[02 The Component Framework]] — how `LobbyComponent` registers with `GameManager` and how a `TableBinderComponent` re-emits subscribed rows as Godot signals.
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `lobby` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

"The lobby" is a server-side state, not a scene: an identity is in the lobby exactly while it has a `LoggedOutPlayer` row. Two screens cover it on the client — the `lobby_gui.tscn` main menu (scene navigation only) and the character-select `LobbyComponent` instanced inside `main.tscn`, which is where every profile reducer call lives. The lobby subscription wave delivers two caller-scoped **views** (server-side queries filtered to the requesting identity): the player's lobby row and their profile list. A binder replays each profile row into one `profile_panel.tscn` instance, and the panels' buttons drive the three lobby reducers — `create_profile` (validated, max 3, per-player unique names), `delete_profile` (ownership-checked, tears down every per-profile row), and `join_world` (moves the identity row to `LoggedInPlayer` and starts the game — the next doc's subject).

## Flowcharts

- [[flowcharts/main-lobby.canvas]] — the composed lobby & profiles flow (the lobby component script and scene, both UI scenes, and the server's `player` module).
![[flowcharts/main-lobby.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Lobby_subfolder/Lobby_subfolder.canvas]] — deep dive: `LobbyComponent.cs`.
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/Components_subfolder/Lobby_subfolder/Lobby_subfolder.canvas]] — deep dive: `lobby_component.tscn` and its binder wiring.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]] — deep dive: `tables.rs`, `reducers.rs`, `methods.rs`, `views.rs`.

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

### Two lobby UIs, and only one of them talks to the server

The lobby experience is split across two scenes. The boot scene [[lobby_gui.tscn##[node name="LobbyGui" type="Control"|lobby_gui.tscn]] is the main menu: its root script [[LobbyGui.cs##public partial class LobbyGui : Control|LobbyGui]] does exactly three things, each wired by a `[connection]` entry at the bottom of the scene file — [[LobbyGui.cs##public void CharSlotsPressed|CharSlotsPressed]] switches the scene to `main.tscn` (that was conn-3), and `ServerListPressed`/`SettingsPressed`/`BackPressed` only `Show()`/`Hide()` the menu, server-list, and settings panels. **There is no server-switching or settings logic behind those panels, and no profile code here at all** — see Known gaps.

The real lobby is [[LobbyComponent.cs##public partial class LobbyComponent : ControlComponent|LobbyComponent]], instanced from `lobby_component.tscn` under the `UI` CanvasLayer of `main.tscn` (lobby-2). All three profile reducer calls — `CreateProfile`, `DeleteProfile`, `JoinWorld` — live in this one file. The split means the character-select UI only exists while the world scene is loaded; it sits on a `CanvasLayer` above the (still empty) world and uses the `MainMenuPhantomCamera2D` priority flip (60 while open, 0 while hidden) to decide whether the menu camera or the gameplay camera drives the view.

### The lobby is a row, not a flag

The server models lobby membership structurally. [[server/spacetimedb/src/player/tables.rs##pub struct LoggedOutPlayer|LoggedOutPlayer]] and [[server/spacetimedb/src/player/tables.rs##pub struct LoggedInPlayer|LoggedInPlayer]] share `player_id` (the caller's `Identity`) as primary key plus `username`/`is_admin`; `LoggedInPlayer` adds a unique `profile_id` — which profile the player is inhabiting. The invariant carried over from doc 03: an identity has a row in **exactly one** of the two tables (or neither, before its first connect), and reducers move the row rather than flipping a boolean — `join_world`/`leave_world` delete from one and insert into the other. This makes "where is this player?" a lookup, not a state machine that can desync.

[[server/spacetimedb/src/player/tables.rs##pub struct PlayerProfile|PlayerProfile]] is the character slot: an auto-increment `profile_id`, the owner's `player_id` (btree-indexed, which is what makes the per-player lookups below cheap), `profile_name`, `texture_id`, and the `aim_assist`/`lock_on` gameplay toggles. [[server/spacetimedb/src/player/tables.rs##pub struct PlayerData|PlayerData]] holds the per-profile progression (`level`, `xp`, `hp`, `max_hp`) keyed by `profile_id` — the doc-02 pattern of joining one logical entity's rows across tables by id. Note that `create_profile` inserts *only* the `PlayerProfile` row; `PlayerData` and the rest of the per-profile rows are scaffolded lazily by `try_scaffold_profile` on first join — that belongs to [[05 Joining the World]].

### Views: why the client only ever sees its own rows

A SpacetimeDB **view** is a named, server-side query whose results a client can subscribe to like a table; the three lobby-relevant ones live in [[server/spacetimedb/src/player/views.rs##fn local_lobby_player(ctx|views.rs]]. Each is *caller-scoped*: it reads `ctx.sender()` — the identity of whoever subscribed — and filters to that identity's rows, so the server never pushes another player's profiles to this client. `local_lobby_player` returns the caller's `LoggedOutPlayer` row (the username source), `local_player_profiles` the caller's profile list, and [[server/spacetimedb/src/player/views.rs##fn local_player_active_profile(ctx|local_player_active_profile]] resolves the caller's `LoggedInPlayer.profile_id` and returns just that profile — falling back to the impossible id `u64::MAX` when the caller is in the lobby, so the view is simply empty rather than wrong. The lobby wave subscribes the first two; the active-profile view rides the game wave ([[05 Joining the World]]).

### Guards: the row lookup *is* the permission check

Every lobby reducer opens with [[server/spacetimedb/src/player/methods.rs##pub fn require_in_lobby|require_in_lobby]], which returns the caller's `LoggedOutPlayer` row or the error `"Not in lobby."` — because lobby membership is a row, checking permission and fetching the player's data are the same lookup. Its siblings in the same file: `require_in_world` (the `LoggedInPlayer` mirror, used by every in-world reducer) and `require_logged_in` (the no-row variant — success or `"Not in world."`, used by the world-building reducers of [[07 Terrain & World Streaming]]), plus `is_admin`, which checks both state tables so admin rights survive the lobby/world transition — its callers are the admin reducers of [[12 Admin & Debug]].

### Creating and deleting profiles

```sync
![[00 End-to-End Timeline Flowchart#^lobby-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^lobby-6{seamless:true,title:false,marker:06.}]]
```

Edge cases the prose version glosses over:

- **Validation order matters.** `create_profile` checks the lobby guard *before* the name rules, so an in-world caller gets "Not in lobby." even with a garbage name. The 20-character cap is measured on the raw string while the emptiness check trims — a name of 21 spaces fails on length, not emptiness.
- **Name uniqueness is per-player and case-insensitive** (`"Knight"` and `"knight"` collide for the same owner, but two different players may both have a "Knight") — and the 3-profile cap counts only the caller's own rows. There is no global namespace to squat on.
- **Deletion is optimistic on the client.** lobby-6's `QueueFree` runs immediately after the reducer *call*, not after its success; if the server rejects the delete (say the row vanished in another tab), the panel disappears anyway and nothing resyncs it until the scene reloads. The binder wiring in `lobby_component.tscn` connects only `RowInserted` — there is no delete-driven UI path at all.
- **Reducer errors never reach the lobby UI.** The client attaches no failure callbacks to any of the three reducer calls, so a rejected create (duplicate name, fourth profile) just… doesn't produce a new panel. The player gets no message.

### Joining — the lobby's exit

```sync
![[00 End-to-End Timeline Flowchart#^lobby-7{seamless:true,title:false,marker:07.}]]
```

The only lobby-specific nuance worth adding here: `OnJoinPressed` refuses to act while `CreateProfilePanel` is visible, because the create panel overlays the list and a join mid-creation would strand the half-filled form. Everything after the `JoinWorld` call — the server-side row move, the game-wave tables, entity spawning — is [[05 Joining the World]].

### Username: wired end to end, except the UI

The username pipeline is complete on paper. The server's [[server/spacetimedb/src/player/reducers.rs##pub fn set_username|set_username]] validates 1–20 non-whitespace characters and updates whichever state row the caller currently has (lobby or world — it's the one reducer that works in both). On the client, the conn-1 connect handler hooked the `LocalLobbyPlayer` view's insert/update events so [[DatabaseConnector.cs##public string Username|DatabaseConnector.Username]] tracks the row, and [[GameManager.cs##public static string Username|GameManager.Username]] exposes it as a facade. The missing link is purely client-side: **nothing outside the generated bindings ever calls `SetUsername`** — no LineEdit, no admin command path through the UI — so every player's username stays the empty string `client_connected` inserted. See Known gaps.

### Levels, XP, and the lobby's math library

`player/methods.rs` also hosts the progression math that later docs lean on; it lives here because profiles are the progression's anchor. [[server/spacetimedb/src/player/methods.rs##pub fn xp_for_level|xp_for_level]] defines level thresholds as triangular numbers — reaching level `n` requires `100·(n−1)·n/2` total XP (level 2 at 100, 3 at 300, 4 at 600) — and `compute_level` walks that ladder linearly, fine at demo scale. `compute_base_stats` returns a flat `10 + (level−1)` for all six stats and `compute_base_max_hp` returns `BASE_MAX_HP` (100) + 5 per level above 1 — the base values `recompute_stats` feeds its flat/mult modifier pipeline ([[10 Inventory, Items & Enchantments]]). When combat awards XP, [[server/spacetimedb/src/player/methods.rs##pub fn internal_gain_xp|internal_gain_xp]] heals only the *delta* in level-derived max HP on level-up — the code comment explains why: a full heal would reward leveling while hurt, and `recompute_stats` afterward folds item HP bonuses in and clamps `hp` to the final `max_hp`. The XP sources themselves are [[09 Combat & Damage]].

One more helper with no lobby UI caller: [[server/spacetimedb/src/player/methods.rs##pub fn find_profile_by_name|find_profile_by_name]] resolves a (player, profile-name) pair case-insensitively; its callers are the admin reducers (`give_item`, `remove_item`, `change_stats`) that target players by name — [[12 Admin & Debug]].

## Known gaps / stubs

- **`LobbyGui` is scene navigation only.** All profile reducer calls live in `LobbyComponent`; the main menu's only functional button is Character Slots (stated inline above, listed here for completeness).
- **ServerList/Settings panels are show/hide only.** [[LobbyGui.cs##public void ServerListPressed|ServerListPressed]] and [[LobbyGui.cs##public void SettingsPressed|SettingsPressed]] reveal panels whose scroll containers are empty in [[lobby_gui.tscn##[node name="ServerList" type="Panel"|lobby_gui.tscn]] — no server-switching or settings logic exists behind them.
- **The character-select BACK button is unwired.** `lobby_component.tscn` declares a BACK button (`Panel/CenterContainer/Button`) but its `[connection]` entries wire only the two CREATE buttons and the binder's `RowInserted` — pressing BACK does nothing.
- **Leftover debug print.** [[LobbyComponent.cs##private void OnProfileRowInserted|OnProfileRowInserted]] ends with a `GD.Print($"Successfully synced a profile: …")` that fires once per profile row on every lobby entry.
- **`set_username` has no client caller.** Nothing outside `module_bindings` invokes `Reducers.SetUsername`, so usernames remain the empty string inserted by `client_connected` (see "Username" above).
- **`aim_assist`/`lock_on` are always `false`.** Both insert sites (`client_connected`'s starter profile and `create_profile`) hardcode them, no reducer updates them, and the only reader is `CombatComponent` ([[09 Combat & Damage]]) — the toggles are stored, read, and never settable.
- **Lobby reducer errors are invisible to the player.** No failure callbacks on create/delete/join, and delete is optimistic (detailed inline above).

## Where to go next

The JOIN button's server half — `join_world` moving the identity row, scaffolding the profile's data rows, and the game subscription wave that makes entities appear — is [[05 Joining the World]]. For what the lobby's `PlayerData` numbers mean in combat, skip ahead to [[09 Combat & Damage]].
