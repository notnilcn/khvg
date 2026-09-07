# 03 Connection, Subscriptions & Views

## Assumed knowledge

[[01 Roadmap]] for conventions; [[02 The Component Framework]] for entities, components, and the server-side table-per-concern mirror. This doc covers machinery, not tables — it has no table entries of its own. Every `04+` module doc assumes this one: when those docs say "read via the `nearby_enemies` view in the Game wave", the mechanism below is what they mean.

## The 30-second version

A **reducer** is a server-side function a client *calls* (validated, transactional, and returning no data — clients read data only through tables). A **view** is a named server-side query the client *subscribes* to by name; a **subscription wave** is a batch of view/raw-table subscriptions opened together. The client connects once through the `DatabaseConnector` autoload, opens the Base + Lobby waves from `main_menu.tscn` into `game.tscn`, swaps Lobby for Game on `join_world`, and consumes every row through `TableBinderComponent` children wired in the `.tscn` — one binder per table per consumer, replaying cached rows for persistent tables and never for event tables.

## Find it fast

| Question / concept | Where |
|---|---|
| How does the client connect (and reconnect)? | [[#The connector]] |
| What is subscribed, and when do waves swap? | [[#Subscriber waves]] |
| How does a component receive row events? | [[#The binder]] |
| What are the three view flavors? | [[#The three view flavors]] |
| Why do AOI views key off `PlayerChunk`, not `PlayerPosition`? | [[#The three view flavors]] — chunk-gated recompute |
| How do event tables (bullets, spell flashes) reach the screen? | [[#The event-table pattern]] — replay stays off |
| Why does my new table show nothing on the client? | Checklist in [[#The binder]] — public flag, wave membership, binder wiring, replay flag |

## Flowcharts

- [[flowcharts/main-subscriptions.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/sstdbsdk_subfolder/sstdbsdk_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/game_codefile/game_codefile.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/main_menu_codefile/main_menu_codefile.canvas]]

## The connector

[[client/sstdbsdk/DatabaseConnector.cs#Connect|DatabaseConnector]] is an **autoload singleton** — a node Godot instantiates before any scene (declared in `project.godot` as `res://sstdbsdk/DatabaseConnector.tscn`, a one-node scene whose only job is attaching the script), so any node in any scene reaches it through `DatabaseConnector.Instance` with no entity-registry lookup. It owns the `DbConnection` (`Conn`), exposes `Host` (`https://khvgames.com/server`, the same project server `server/build.sh` publishes to) and `DbName` (`bullethell`), and pumps `FrameTick()` every frame inside try/catch — a throwing table callback is logged and the pump keeps going, because one bad handler must not stop all network processing for the session.

Consumers wire table callbacks off its `Connected` signal, but must guard with `Conn?.IsActive` first: the autoload can finish connecting before Main loads, so both orders are handled. A dropped connection (not deliberate shutdown) emits `Disconnected` — `ConnectionLostComponent` shows the reconnect panel off it, and reconnect means `Connect()` again (rebuilding `Conn` with the saved token) plus a trip back through [[client/Scenes/main_menu.tscn|main_menu.tscn]] with no auto-rejoin. Token persistence is the SpacetimeDB SDK's own `AuthToken` helper; `--pN` launch args suffix the token key (exact `^--p\d+$` match — a prefix match once caught Godot's own `--path`) so parallel run instances get distinct identities. The menu's **2D**/**3D** buttons set `GameVisuals.Mode` and enter [[client/Scenes/game.tscn|game.tscn]]; the mode/char launch args (`--2D`/`--3D`/`--charN`, parsed in `Scripts/Game/LaunchArgs.cs`) press those buttons and the lobby Join automatically.

Reducers are called through `GameManager.Conn?.Reducers.<Name>(args)` — e.g. `FlagPlayerHit`, `PickupDrop`, `JoinWorld`. The call carries intent only; the result comes back as row events. Debugging rule from `server/AGENTS.md`, repeated here because it bites: when something doesn't work, check **is the client actually calling the reducer?**

## Subscriber waves

[[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber]] (a `Component` sibling under the `GameManager` entity in [[client/Scenes/game.tscn|game.tscn]]) owns the three subscription waves as static name lists — the single source of truth for "what is subscribed", built into SQL via the generated `From` API so a server-side rename flows through on the next binding regeneration:

- **BaseTables** (`AllTextures`, `AllItems`, `AllEnchantments`, `AllAnimations`) — static catalog every screen needs; subscribed on connect.
- **LobbyTables** (`LocalLobbyPlayer`, `LocalPlayerProfiles`) — profile selection before joining; subscribed on connect alongside Base. `LocalPlayerProfiles` deliberately appears in Game too, so panels survive the wave swap.
- **GameTables** — everything the live world needs: the `LocalPlayer*` per-player views, the `Nearby*` AOI views, `EnemyTemplates`, `MapConfig`, the three event tables (`BulletPatternEvent`, `BulletControlEvent`, `AbilityVisualEvent`), and the four raw-table subscriptions (`ActiveStatusEffect`, `ActiveEnemyStatusEffect`, `GroundZone`, `AbilityCharge`).

Wave switching is join-driven, not scene-driven: `LobbyComponent` calls `SubscribeGame()` + `UnsubscribeLobby()` once the `JoinWorld` reducer callback reports success (on failure or the 5 s `JoinTimeoutSeconds` timeout the lobby re-shows with the JoinError label), and `EntitySpawnerComponent` swaps back (`DespawnAll()` frees tracked entities and cancels pending adds first, so stale ids can't wedge respawn) on death/exit. The lobby wave's applied callback raises `LobbyApplied` / `LobbySubActive`, which `LobbyComponent`'s `--charN` auto-join waits on before counting profiles.

`TableSubscriber` also owns the `LapQ`/`LapR` torus lap vectors as **statics** mirrored from the subscribed `MapConfig` row (zero until it arrives, reset in `_ExitTree`) — read directly per entity per frame by `InterpolationComponent`, saving a facade lookup per read. Its two `GD.Print`s in `OnLobbySubApplied`/`OnGameSubApplied` are unconditional leftovers (a known gap, see doc 10's lobby section); the `TableBinderComponent.Bind`/`HandleInsert` prints are instead gated behind its `[Export] Verbose` flag — opt-in diagnostics, off by default.

## The binder

[[client/sstdbsdk/TableBinderComponent.cs#TableBinderComponent|TableBinderComponent]] is the standard — and only sanctioned — way components consume row events: place one binder child per table on the consuming component and wire its signals in the `.tscn`. (Only infrastructure keeps direct `conn.Db.*` hooks: `TableSubscriber`'s `MapConfig` lap vectors and `DatabaseConnector`'s `LocalLobbyPlayer`.) The `TableName` dropdown lists `TableSubscriber.AllSubscribedTables`, so an unsubscribed table can't even be picked.

Rows are plain C# classes and can't cross the Godot signal boundary, so `RowInserted` / `RowUpdated` / `RowDeleted` carry no args — handlers read `LastRow` (inserted/new), `LastOldRow` (update only), or `LastDeletedRow` and cast. `ReplayExistingRows` replays cached rows through `RowInserted` on bind, which replaces manual `Iter()` replay loops for late binds.

When a new table shows nothing on the client, walk this checklist in order — it is always one of these: (1) the table lacks the `public` flag server-side, so it can't be subscribed; (2) its view/table name is missing from the wave list in `TableSubscriber.cs`; (3) no binder child names it, or the `.tscn` signal isn't wired to a handler; (4) the replay flag is wrong for the table kind (see next section). Regenerate bindings via `server/build.sh` after any server change — never hand-edit `client/Scripts/module_bindings/`.

## The three view flavors

Every `views.rs` uses SpacetimeDB 2.x **parameterized views** — the client never writes SQL; *adding a view is how you expose data*. There are three flavors, and the `04+` docs name them without re-explaining:

1. **Per-caller views** (`local_player*`, `local_lobby_player`) — built on `ViewContext`, which gives both `ctx.db` (imperative lookups: resolve the caller's `profile_id` off `logged_in_player`, defaulting to the `u64::MAX` sentinel so strangers match nothing) and `ctx.from` (the query builder). Note `r#where` — `where` is a Rust keyword.
2. **AOI views** (`nearby_remote_players`, `nearby_enemies`, `nearby_loot_drops`, `nearby_terrain_tiles`, `nearby_chests`, …) — the same per-caller resolution, then a chunk-index OR-chain (`IN` is spelled as seeded-`eq` folded with `.or`, because the builder has no `IN`) over `nearby_indices()`, with a deliberately-unmatchable sentinel query returned when the caller has no chunk yet (a view must always return a query — never an early empty return). They key off ^table-player-chunk, **not** `PlayerPosition`: the chunk row is written only on real chunk crossings, so views recompute on crossings instead of on every 10 Hz movement report. `nearby_remote_players` additionally excludes the caller's own row and semijoins `logged_in_player` to drop logged-out ghosts; `nearby_remote_players_profiles` is the right-semijoin mirror that yields profiles for exactly that AOI set.
3. **Global views** (`enemy_templates`, `all_items`, `all_enchantments`, `all_textures`, `all_animations`) — built on `AnonymousViewContext` with no per-caller state; the full table, for static catalog content every client caches.

Alongside views, four tables are subscribed **raw** (no view) at co-op-acceptable scale and filtered client-side: `ActiveStatusEffect` (filtered to the local profile in `StatusBarComponent`), `ActiveEnemyStatusEffect` (filtered per enemy instance in `EnemyDebuffIndicatorComponent`), `GroundZone` (all rows rendered by `ZoneRendererComponent`), and `AbilityCharge` (filtered to the local profile in `AbilityChargeComponent`) — plus raw `MapConfig` for the lap vectors above.

## The event-table pattern

`BulletPatternEvent`, `BulletControlEvent`, and `AbilityVisualEvent` are `public event` tables: append-only broadcasts, not state. Two rules follow everywhere they are consumed:

- **Replay stays OFF.** No last-seen-id tracking exists, so replay would re-fire the table's whole history — every bullet pattern, wall, slash, and control cast ever appended — each time the scene is entered. Every event binder (`BulletPatternEventBinder` on `BulletSpawnerComponent`, `BulletControlEventBinder` on `BulletControllerComponent`, `AbilityVisualEventBinder` on `SpellVisualComponent`) binds with `ReplayExistingRows = false`; persistent tables (`GroundZone`, chest views, catalog) do the opposite so late joiners see live state.
- **Rows are self-contained.** Firing bakes absolute origin, angles, damage, and texture id server-side at insert, so one shared binder renders volleys verbatim with no per-enemy fan-out — and no lost volley during a puppet's deferred-spawn window. Casters apply their own casts optimistically (`TryActivateAbility`) and skip their own echo by `cast_by`.

## Where to go next

[[04 Anticheat Tables]] begins the module docs. Every table section there (and in docs 05–12) traces the [[#The binder|binder]] → consumer → visible-end path defined here, so this doc is never repeated — only linked.
