# 03 Boot & Connection

## Assumed knowledge

- [[02 The Component Framework]] — the entity/component model: this doc is where the `DatabaseConnector` autoload and the `TableSubscriber` component that every `TableBinderComponent` depends on actually come to life.
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `boot`/`conn` sections.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

Two sides boot independently. The Godot client loads its `DatabaseConnector` **autoload** (a node kept alive across all scenes) before any scene, and that autoload immediately opens the SpacetimeDB connection — reusing a per-host auth token file so returning players keep their identity — while the main-menu scene `lobby_gui.tscn` appears on screen. The server module, on publish, runs its `init` reducer once: it arms the game's three scheduled timers, seeds every catalog/definition table, builds the hex grid (`BuildingTile` rows + the `MapConfig` row), and generates the "Earth" world's terrain. When a client connects, the server's `client_connected` reducer puts a new identity into the lobby (`LoggedOutPlayer` row plus a default "Knight" profile), and the client, once the player clicks through to `main.tscn`, subscribes its first two **subscription waves** — the static catalogs and the lobby views — through the inline `TableSubscriber` component. The game wave waits until join ([[05 Joining the World]]).

## Flowcharts

- [[flowcharts/main-boot.canvas]] — the composed boot & connection flow (the `sstdbsdk` layer, both boot scenes, and the server's `main` module).
![[flowcharts/main-boot.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/sstdbsdk_subfolder/sstdbsdk_subfolder.canvas]] — deep dive: `DatabaseConnector`, `AuthToken`, `TableSubscriber`, `TableBinderComponent`.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/main_subfolder.canvas]] — deep dive: `lifecycle.rs` (init/connect hooks), `seeds.rs`, `admin.rs` (world-gen reducers), `global.rs` (constants).

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^boot-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^boot-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^boot-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^boot-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^boot-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^boot-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-1{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-2{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-3{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-4{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-5{seamless:true,title:false,marker:11.}]]
```

## Main body

### Why the connection is an autoload

The connection must outlive every scene: the game switches scenes at least twice per session (main menu → world on join, world → menu on exit), and a `DbConnection` bound to a scene node would die with it. Godot's answer is the **autoload** — entries in [[project.godot##[autoload]|project.godot]]'s `[autoload]` section are instanced before the main scene and never unloaded. Three are registered: `PhantomCameraManager` and `McpRuntimeAutoload` belong to plugins (camera system and a dev MCP bridge); the game-relevant one is [[DatabaseConnector.cs##public partial class DatabaseConnector : Node|DatabaseConnector]].

Because the autoload exists before any entity does, it can't be found through the component registry of doc 02 — so it inverts the pattern: [[DatabaseConnector.cs##public override void _Ready()|_Ready]] stores itself in the static `Instance` property, and consumers (including the [[GameManager.cs##public static DbConnection? Conn|GameManager.Conn]] facade) read through that. The class docstring states the contract: hook the `Connected` signal to wire table callbacks, but **guard against the connection already being active first**, because the autoload usually finishes connecting before `main.tscn` loads. Both [[TableSubscriber.cs##protected override void OnRegistered|TableSubscriber.OnRegistered]] and every `TableBinderComponent` implement exactly that both-orders guard; the timeline's conn-4 step shows the subscriber's version.

Two smaller mechanisms in the same file: `_Process` pumps [[DatabaseConnector.cs##public override void _Process(double delta)|Conn.FrameTick()]] every rendered frame because the SpacetimeDB C# SDK is explicitly non-threaded — network messages queue up and only dispatch (as row events, reducer callbacks, signal emissions) when the client pumps them, which conveniently lands all of it on Godot's main thread where touching the node tree is legal. And [[DatabaseConnector.cs##public override void _ExitTree()|_ExitTree]] disconnects cleanly when the app quits, so the server's `client_disconnected` hook fires (its consequences belong to [[13 Disconnect & Teardown]]).

### Identity is a file: `AuthToken`

SpacetimeDB identifies a client by an `Identity` derived from an auth token; with no token supplied, the server mints a fresh anonymous identity per connection — which would orphan the player's profiles every launch. [[AuthToken.cs##public static class AuthToken|AuthToken]] is the whole fix: [[AuthToken.cs##public static void Init(string filePath)|Init]] reads a token file from the user-data dir at connect time, and [[AuthToken.cs##public static void SaveToken(string token)|SaveToken]] overwrites it with the server-issued token on every successful connect. The filename embeds a sanitized host tag (`http_127.0.0.1_3000`, etc.) — the code comment in [[DatabaseConnector.cs##public void Connect(string host, string dbName)|Connect]] gives the reason: a token is signed by the SpacetimeDB instance that minted it, so offering a local token to maincloud (or vice versa) earns a 401, and per-host files keep the two worlds from clobbering each other.

### What `main.tscn` contributes at load time

The boot scene is the menu, so the world scene's arrival (conn-3) is a second mini-boot. [[main.tscn##[node name="Main" type="Node2D"|main.tscn]] declares every scene-level component inline under the `Main` root — `TableSubscriber`, `CatalogComponent` with its three catalog binders, `EntitySpawnerComponent` with its four spawn binders, the terrain/camera/overlay components, and the `DebugOverlay` — with the binder→component signal wiring as `[connection]` entries at the bottom of the file. Those components register with `GameManager` through the standard lifecycle of doc 02, and their binders then bind against the already-live connection. Note the doc-02 warning applies here too: the live wiring is *this file*; the standalone `subscription_component.tscn` under `Scenes/Components/Subscription/` is one of the nine unreferenced duplicates, not a wiring site.

### Subscription waves, and why there are three

A SpacetimeDB **subscription** is a SQL query whose matching rows the server pushes to the client and keeps pushed as they change; `TableSubscriber` groups the game's queries into three waves, subscribed at three different life moments so the client never holds rows it can't use yet. The static name lists — [[TableSubscriber.cs##public static readonly string[] BaseTables|BaseTables]], `LobbyTables`, `GameTables` — are the single source of truth for "what is subscribed":

- **Base** (`AllTextures`, `AllItems`, `AllEnchantments`): static catalogs needed by every screen, subscribed the moment the connection is up (conn-4).
- **Lobby** (`LocalLobbyPlayer`, `LocalPlayerProfiles`): the profile-selection views, subscribed right after base and unsubscribed when leaving the lobby (their contents are doc 04's subject).
- **Game** (sixteen tables: local player data, nearby remotes/enemies/loot/terrain, `MapConfig`, …): subscribed only on `join_world` — see [[05 Joining the World]]. This is why the `MapConfig` hooks installed at conn-4 don't deliver a row until the game wave lands: `MapConfig` is in `GameTables`, not base.

The SQL is built indirectly on purpose: [[TableSubscriber.cs##private static string TableSql|TableSql]] resolves each PascalCase name through the generated `From` API and calls `ToSql()`, so a server-side table rename followed by a bindings regenerate surfaces as a compile break, not a silent runtime miss. The same lists feed `AllSubscribedTables`, which populates `TableBinderComponent`'s inspector dropdown — a binder pointed at an unsubscribed table can never fire, and the binder warns about exactly that at configuration time.

### Server boot: one `init`, written to be re-runnable

SpacetimeDB runs the reducer marked `init` when the module is published. [[server/spacetimedb/src/main/lifecycle.rs##pub fn init|init]] does three kinds of work, in order:

1. **Arm the timers.** It inserts the three schedule rows — `EnemyBehaviorSchedule` (100 ms, the enemy sim tick), `EnemySpawnSchedule` (2 s), `ConsumableEffectSchedule` ([[server/spacetimedb/src/main/global.rs##pub const CONSUMABLE_EFFECT_TICK_SECONDS|CONSUMABLE_EFFECT_TICK_SECONDS]] = 1 s) — the recurring reducers those rows drive are covered by docs 08 and 10.
2. **Seed the definitions.** The `seed_*` functions in `main/seeds.rs` fill the catalog and def tables: the texture catalog ([[server/spacetimedb/src/main/seeds.rs##pub fn seed_default_textures|seed_default_textures]]), terrain layering/adjacency/decor rules, enemy templates and the test bosses ([[server/spacetimedb/src/main/seeds.rs##pub fn seed_default_enemies|seed_default_enemies]] and `seed_test_boss_p2`–`p6`), world items/enchantments, and the biome/world defs ([[server/spacetimedb/src/main/seeds.rs##pub fn seed_world_defs|seed_world_defs]]). Re-runnability is designed in two ways: most seeds go through the `Seed` trait, which upserts by natural key (find by id → update, else insert), while the rule tables have no natural key and instead clear-and-reinsert — the file's own comments call out both strategies as what keeps republishes correct.
3. **Build the world.** [[server/spacetimedb/src/main/admin.rs##pub fn internal_add_chunks|internal_add_chunks]] materializes the hex grid — one `BuildingTile` row per hex across the 6×6 chunk map — and upserts the `MapConfig` row whose lap vectors define the torus wrap; then [[server/spacetimedb/src/world/terrain/mod.rs##pub fn internal_generate_world_proc|internal_generate_world_proc]] generates "Earth" from the just-seeded defs. Both calls are prefixed with `let _ =`, so a world-gen failure at init is silently discarded — no log, no retry. In practice the seeded defs are consistent, so this is a robustness gap rather than a live bug.

The grid constants deserve one sentence because everything downstream divides by them: [[server/spacetimedb/src/main/global.rs##pub const DEFAULT_CHUNK_HEX_RADIUS|DEFAULT_CHUNK_HEX_RADIUS]] = 2 (a chunk is a hex of hexes), `DEFAULT_CHUNK_COLS`/`ROWS` = 6×6 chunks, and `DEFAULT_HEX_OUTER_RADIUS` = 48 world units per hex — global.rs's own comment explains the map was shrunk from 22 chunks to 6 so players actually meet enemies on the demo map.

### Who are you? `client_connected`

Every successful handshake — boot-2's connect, and every later reconnect — runs [[server/spacetimedb/src/main/lifecycle.rs##pub fn client_connected|client_connected]] with `ctx.sender()` set to the connecting identity. The reducer is a three-way branch on where that identity already stands:

- **In the world** (`LoggedInPlayer` row exists): shouldn't be possible through the normal client, and the handler is buggy — see Known gaps.
- **In the lobby already** (`LoggedOutPlayer` row exists): a reconnect; nothing to do.
- **Nowhere**: a genuinely new identity. The server inserts a `LoggedOutPlayer` row (empty username, `is_admin: false`) **and** a starter `PlayerProfile` named "Knight" with the "Knight" texture — so even a first-time player lands in the lobby with one selectable profile. How profiles are listed, renamed, created, and deleted from there is [[04 Lobby & Profiles]].

The lobby/world split itself is the invariant to carry forward: an identity is represented by exactly one of `LoggedInPlayer` / `LoggedOutPlayer` (or nothing, before first connect), and reducers move the row between the two tables rather than flipping a flag — `join_world`/`leave_world` (doc 05) and `client_disconnected` (doc 13) are the other movers.

## Known gaps / stubs

- **Leftover debug prints in the binder.** [[TableBinderComponent.cs##private void Bind(DbConnection conn)|TableBinderComponent.Bind]] ends with a `GD.Print($"[TableBinderComponent] DEBUG bound …")` and [[TableBinderComponent.cs##private void HandleInsert<TRow>|HandleInsert]] prints a `DEBUG first insert` line once per binder — with a dozen-plus binders inline in `main.tscn`, every boot spams the output log.
- **`client_connected` mishandles the already-in-world case.** When a connecting identity still has a `LoggedInPlayer` row, the handler logs [[server/spacetimedb/src/main/lifecycle.rs##connected while already in world|log::error!("…this is a bug.")]] — and then does the buggy thing anyway: it deletes the `LoggedInPlayer` row and inserts a `LoggedOutPlayer` row, silently dropping the player back to the lobby instead of rejecting the duplicate connection. The bug is flagged only inside that log message; there is no comment or TODO marking it in code.
- **Init world-gen errors are swallowed.** The `let _ = internal_add_chunks(...)` / `let _ = internal_generate_world_proc(...)` calls in `init` discard `Err` results without logging (noted in the main body; listed here so it isn't mistaken for error handling).

## Where to go next

The player is now sitting in the lobby with a "Knight" profile and two subscription waves live. Continue to [[04 Lobby & Profiles]] for the profile-selection UI and its reducers/views, then [[05 Joining the World]] for what happens when the game wave subscribes and entities start spawning.
