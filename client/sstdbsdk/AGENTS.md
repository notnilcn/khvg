# AGENTS.md — `client/sstdbsdk/`

> A small, self-contained **SpacetimeDB client layer for Godot 4 C#**. It was extracted from a
> bullethell project and is meant to be copied into a new Godot project wholesale. This file is
> written for a fresh agent arriving in a project that contains `client/sstdbsdk/` and `server/`
> and explains the whole path: server table → published module → generated C# bindings → live
> client cache → Godot signal → your gameplay code.

The folder owns exactly four concerns:

1. **Connection** — one `DbConnection`, pumped every frame (`DatabaseConnector`, an autoload singleton).
2. **Subscriptions** — server tables/views are subscribed in named waves (`TableSubscriber`).
3. **Row events** — each table's insert/update/delete events become editor-wireable Godot signals (`TableBinderComponent`).
4. **Identity/tokens** — auth token persistence per host, plus `--pN` launch-arg support for parallel instances.

Everything else (entities, UI, interpolation) is built **on top**. The three classes have no
gameplay logic, but they do contain a few hardcoded hooks from the origin project that you must
adapt before they compile against a different server module — see
[Adapt the copied files](#6-adapt-the-copied-files).

---

## The 30-second version

For every piece of server data you want on the client:

1. Declare a `public` table/view in `server/` and a reducer that mutates it.
2. `spacetime publish` the module and run `spacetime generate --lang csharp` to write the bindings into `client/`.
3. Add the accessor's **PascalCase name** to a wave list in `TableSubscriber` (`BaseTables`, `LobbyTables`, or `GameTables`).
4. Add a **`TableBinderComponent` child** to the consuming node, pick the table in the inspector, and wire `RowInserted`/`RowUpdated`/`RowDeleted` to handlers **in the `.tscn`**.
5. In the handler, read `LastRow` / `LastOldRow` / `LastDeletedRow`, cast to the row type, and update your mirror/UI.
6. To change state, call `DatabaseConnector.Instance?.Conn?.Reducers.YourReducer(args)` — never mutate stats locally.

**Never** hook `conn.Db.*.OnInsert/OnUpdate/OnDelete` outside this folder, and **never** set
`ReplayExistingRows = true` on an event table.

---

## The data flow

```
server/spacetimedb/  (Rust: public tables & views + reducers)
        │
        │  spacetime publish <db>          → server now serves the schema
        │  spacetime generate --lang csharp → client/Scripts/module_bindings/*.g.cs
        ▼
generated C# bindings  (DbConnection, RemoteTables, From, row types, Reducers)
        │
        │  referenced by your .csproj
        ▼
DatabaseConnector   (autoload, created before any scene)
        │  _Ready → Connect()                  ★ without FrameTick() nothing dispatches
        │  _Process → Conn.FrameTick() every frame
        │  OnConnect → emits Connected
        ▼
TableSubscriber   (a Component in your main scene; subscribes on Connected)
        │  base wave + lobby wave automatically; game wave via SubscribeGame()
        │  rows land in the client cache: conn.Db.<Table>.Iter()
        ▼
TableBinderComponent   (one child per table per consumer)
        │  wraps conn.Db.<Table>.OnInsert/OnUpdate/OnDelete
        │  emits RowInserted / RowUpdated / RowDeleted (no args — read LastRow)
        ▼
your components   (read rows → update mirrors, spawn visuals, fill UI)
        │
        │  conn.Reducers.YourReducer(args)   ← intent goes up, rows come back down
        └────────────────────────────────────────────────────────────────► server
```

The server is authoritative. The client mirrors subscribed rows and reports intent through
reducers; it never computes damage or mutates stats locally. A feature that spans both sides
always has the same five steps: **table → reducer → wave entry → binder + handler → call the
reducer**. Forgetting the last two is the recurring mistake.

---

## What each file is

| File | Type | Role |
|---|---|---|
| `DatabaseConnector.cs` | autoload `Node` | Builds `DbConnection` on `_Ready`, pumps `FrameTick()` every frame, saves/loads the auth token, emits `Connected`/`Disconnected`, exposes `Conn`, `LocalIdentity`, `Username`, `IsLocal(id)`. |
| `DatabaseConnector.tscn` | scene | One-node scene attaching the script. This is what gets registered as an autoload. Edit its exported `Host`/`DbName` here. |
| `TableSubscriber.cs` | `Component` (needs an `IEntity` ancestor) | The static wave lists (`BaseTables`/`LobbyTables`/`GameTables`), the SQL builder for them, the subscribe/unsubscribe methods, `LobbyApplied`/`LobbySubActive`, and (origin-specific) `LapQ`/`LapR`. |
| `TableBinderComponent.cs` | `[Tool] Component` (needs an `IEntity` ancestor) | Binds **one** table and re-exposes its row events as Godot signals. Table picked from a dropdown built from `TableSubscriber.AllSubscribedTables`. |

---

## Prerequisites

A new project needs all of the following before this folder will do anything:

- **Godot 4.7+ mono** (the standard `.NET` build; GDScript-only Godot cannot compile C#).
- **.NET 8 target framework** — `Godot.NET.Sdk` projects here use `<TargetFramework>net8.0</TargetFramework>`.
- **A SpacetimeDB 2.x server module** in `server/spacetimedb/` (Rust), published to a reachable server.
- **The `SpacetimeDB.ClientSDK.Godot` NuGet package** referenced by the client `.csproj`. Match the SDK major/minor to your server release; the origin pairs a `spacetimedb` 2.2 crate with `SpacetimeDB.ClientSDK.Godot` 2.7.1.
- **Generated C# bindings** from that module, inside the Godot project (the origin puts them in `Scripts/module_bindings/`). They provide `DbConnection`, `RemoteTables`, `From`, the row types (`SpacetimeDB.Types`), and `Reducers`.
- **The component framework base classes** — `Component`, `IComponent`, `ComponentRegistration`, `IEntity`. `TableSubscriber` and `TableBinderComponent` both derive from `Component`, so an empty project **will not compile** until these exist. Two options:
  - Copy the origin project's files (`Scripts/Components/Component.cs`, `IComponent.cs`, `ComponentRegistration.cs`, and `Scripts/Entities/IEntity.cs` + `EntityRegistry.cs`).
  - Paste the minimal shim in [Appendix A](#appendix-a--minimal-component-framework-shim).

---

## Setup, start to finish

### 1. Server: define the data and the reducers

In `server/spacetimedb/src/` add `public` tables or parameterized views plus reducers that mutate
them. The client SDK only cares that they are `public` and that the accessor has a name. Example:

```rust
#[table(accessor = score, public)]
pub struct Score {
    #[primary_key] #[auto_inc] pub id: u64,
    pub points: u64,
}

#[reducer]
pub fn add_score(ctx: &ReducerContext, points: u64) {
    ctx.db.score().insert(Score { id: 0, points });
}
```

Use a **view** for anything per-caller or AOI-filtered (rows only the caller should see); plain
tables are fine for global data. Event tables (append-only, e.g. one-shot effects) are declared
with `event`; they arrive on the client as insert-only and get special handling by the binder
(see [Replay vs. event tables](#replay-vs-event-tables)).

### 2. Publish and generate the client bindings

From `server/`:

```bash
spacetime generate --lang csharp --out-dir ../client/Scripts/module_bindings --module-path ./spacetimedb -y
spacetime publish <your-db-name> -y          # add --delete-data when the schema changed
                                             # add --server <url> when not using the logged-in default
```

- On Windows run the `spacetime` commands directly (the repo's `build.sh` is bash).
- `--delete-data` **wipes the database**; seeds re-run. Schema changes are hard cuts.
- The `--out-dir` can be any folder inside the Godot project (Godot's SDK compiles all `.cs` under the project). `Scripts/module_bindings/` is just the origin convention.
- **Never hand-edit generated bindings.** When the server schema changes, regenerate, then rebuild the client. Stale bindings are a top source of compile errors and "no such table" runtime errors.

### 3. Reference the SDK package

In the client's `.csproj` (Godot-generated, e.g. `project.csproj`):

```xml
<ItemGroup>
  <PackageReference Include="SpacetimeDB.ClientSDK.Godot" Version="2.7.1" />
</ItemGroup>
```

> **Warning (Godot):** never use *Project → Tools → C# → Create C# solution*. It regenerates the
> `.csproj` from scratch and silently drops this package reference. If it is ever lost, restore it
> with Godot closed: `dotnet add package SpacetimeDB.ClientSDK.Godot --version 2.7.1`.

### 4. Register `DatabaseConnector` as an autoload

`project.godot`:

```ini
[autoload]

DatabaseConnector="*res://sstdbsdk/DatabaseConnector.tscn"
```

`DatabaseConnector.tscn` is a one-node scene whose only job is attaching the script to a `Node` —
register the **scene**, not the `.cs`. Register it under whatever `res://` path you placed this
folder at (the path above assumes `project.godot` and `sstdbsdk/` share a parent, e.g. the Godot
project is the `client/` folder). The autoload connects before any scene loads, which is why it
must be a singleton and why consumers have to guard the "already connected" case.

### 5. Point it at your database

Select the autoload in the editor (or edit `DatabaseConnector.tscn` directly) and set the exports:

| Export | Meaning | Example |
|---|---|---|
| `Host` | Full server URL, with scheme | `http://127.0.0.1:3000`, `https://maincloud.spacetimedb.com` |
| `DbName` | SpacetimeDB database/module name | `mygame` |
| `TokenAppend` | Leave blank; set automatically from `--pN` launch args | `""` |

The script's own defaults are inherited from the origin project — always override them for a new
project. Token persistence is handled by the SDK's `AuthToken` helper keyed by
`Host` (punctuation replaced with `_`) plus `TokenAppend`; changing `Host` produces a new identity
unless the saved token is carried over.

### 6. Adapt the copied files

The three classes are generic **except** for a few hooks that reference tables only the origin
server has. With a different module these will be compile errors, so handle them up front:

- `DatabaseConnector.cs` → `OnConnected` hooks `conn.Db.LocalLobbyPlayer` to keep `Username` in
  sync. Replace it with your own "local/current player" table (or delete the hook — `Username`
  then stays `""`).
- `TableSubscriber.cs` → the `MapConfig` `OnInsert`/`OnUpdate` hooks, `SetLap(MapConfig)`, and the
  static `LapQ`/`LapR` vectors are origin game logic (a wrapping-torus map config). Remove them or
  replace with your own world singleton. Leaving them requires your server to define a table that
  generates a `MapConfig` type with `LapQX/LapQY/LapRX/LapRY` fields.
- `TableSubscriber.cs` → the three wave list constants reference origin accessors
  (`AllTextures`, `LocalPlayer`, `NearbyEnemies`, …). Replace their contents with **your**
  accessors; empty arrays are valid and the lists are the only source of "what gets subscribed".
- `TableBinderComponent.cs` → no project-specific references. It builds its inspector dropdown
  from `TableSubscriber.AllSubscribedTables`, so it starts working the moment `TableSubscriber`
  compiles.

### 7. Name your tables in the subscription waves

Edit the wave lists in `TableSubscriber.cs`. Names are the **PascalCase generated accessor
names** — the same names as the fields on the generated `RemoteTables` class and the methods on
`From` (e.g. Rust accessor `score` → `"Score"`; view `local_player` → `"LocalPlayer"`).

```csharp
// Static wave lists — the single source of truth for "what is subscribed".
public static readonly string[] BaseTables  = { };                 // global catalogs, available on every screen
public static readonly string[] LobbyTables = { "LocalProfile" };  // pre-join / character select
public static readonly string[] GameTables  = { "Score", "LocalPlayer", "NearbyEnemies" };
```

How the waves behave (all in `TableSubscriber`):

- On connect, `OnConnected` subscribes **base** and **lobby** automatically. The base wave has no
  applied callback; the lobby wave raises `LobbyApplied` and reports `LobbySubActive`.
- The **game** wave is explicit: call `SubscribeGame()` when the player joins the world and
  `UnsubscribeGame()` when they leave. `SubscribeLobby()`/`UnsubscribeLobby()` are the manual
  lobby toggles (the origin calls these from its join/leave flow).
- Every name is turned into SQL through the generated `From` API by reflection
  (`TablesSql` → `TableSql`). A listed name that is not a generated accessor throws at subscribe
  time; a table that is *not* in any list warns in the binder and never receives rows.

Typical wave transition:

```csharp
// join pressed:
GetSibling<TableSubscriber>()?.SubscribeGame();
GetSibling<TableSubscriber>()?.UnsubscribeLobby();
// leave / death back to lobby:
GetSibling<TableSubscriber>()?.UnsubscribeGame();
GetSibling<TableSubscriber>()?.SubscribeLobby();
```

### 8. Put a `TableSubscriber` in your main scene

`TableSubscriber` is a `Component`, so it needs an **`IEntity` ancestor**. Declare exactly one as
a child of your main gameplay root (the root must implement `IEntity`; in a plain-Node project
that is the `Entity` shim from Appendix A).

Its `OnRegistered` hooks `DatabaseConnector.Connected` (guarding the already-connected case) and
then subscribes the base + lobby waves. Nothing else in the project has to subscribe anything.

### 9. Consume a table — the binder recipe

This is **the** pattern for consuming server rows. One binder per table per consumer, signals
wired in the editor, no direct `conn.Db.*` event hooks in gameplay code.

1. Add a child `Node` to the consuming node and attach `TableBinderComponent`.
2. Name it `<Table>Binder` (e.g. `ScoreBinder`) — node name is how components find it.
3. In the inspector, pick `TableName` from the dropdown. It only lists tables from the waves.
4. Set `ReplayExistingRows = true` **only for persistent tables** (see warning below).
5. In the `.tscn`, connect `RowInserted` / `RowUpdated` / `RowDeleted` to methods on the consumer.

```ini
; scoreboard.tscn (excerpt)
[ext_resource type="Script" path="res://Scripts/Scoreboard.cs" id="1_script"]
[ext_resource type="Script" path="res://sstdbsdk/TableBinderComponent.cs" id="2_binder"]

[node name="Scoreboard" type="Node"]
script = ExtResource("1_script")

[node name="ScoreBinder" type="Node" parent="."]
script = ExtResource("2_binder")
TableName = "Score"
ReplayExistingRows = true

[connection signal="RowInserted" from="ScoreBinder" to="." method="OnScoreInserted"]
[connection signal="RowDeleted" from="ScoreBinder" to="." method="OnScoreDeleted"]
```

```csharp
#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class Scoreboard : Component
{
    private TableBinderComponent binder = null!;

    public override void _Ready()
    {
        base._Ready();
        binder = GetNode<TableBinderComponent>("ScoreBinder");
    }

    private void OnScoreInserted()
    {
        var row = (Score)binder.LastRow!;          // LastRow: inserted row / new row after update
        GD.Print($"score {row.Id}: {row.Points}");
    }

    private void OnScoreDeleted()
    {
        var row = (Score)binder.LastDeletedRow!;   // LastDeletedRow: the removed row
        // remove the visual for row.Id
    }
}
```

Key facts baked into this pattern:

- **Signals carry no arguments.** Rows are plain C# classes and cannot cross the Godot signal
  boundary. Always read `LastRow` (insert/new), `LastOldRow` (previous row on update), or
  `LastDeletedRow` (delete) and cast. `LastRow` etc. are `object?`, hence the `!`.
- **The binder finds the connection itself** through `DatabaseConnector.Instance`, so it works
  under any entity in any scene. It waits for `Connected` if the autoload has not finished yet.
- **Rows fire inside `_Process`** (from `FrameTick`). Keep handlers cheap; heavy work stalls all
  networking. Buffer and rebuild at most once per frame for large row bursts.
- **`ReplayExistingRows`** re-emits `RowInserted` once per already-cached row when the binder
  binds. That is what late-bound/late-joining consumers need for persistent tables. It is
  **forbidden on event tables** — there is no last-seen-id tracking, so replay re-fires the
  table's entire history (every bullet event ever appended) on each scene entry.
- **Binders are cleaned up on `_ExitTree`** — event handlers are removed from the table, so
  dynamically spawned binders do not leak.

### 10. Call a reducer

Reducers are fire-and-forget methods on `conn.Reducers`; results arrive as row changes. Always
go through the singleton:

```csharp
var conn = DatabaseConnector.Instance?.Conn;
conn?.Reducers.AddScore(10);
```

For failure handling, subscribe to the generated per-reducer callback. It is dispatched through
`FrameTick` like everything else, so unsubscribe when done:

```csharp
using SpacetimeDB;        // ReducerEventContext, Status
using SpacetimeDB.Types;  // generated row types

void OnAddScoreResult(ReducerEventContext ctx, ulong points)
{
    if (ctx.Event.Status is Status.Failed(var reason)) GD.PrintErr($"add_score failed: {reason}");
    else if (ctx.Event.Status is Status.OutOfEnergy)  GD.PrintErr("out of energy");
    // committed: the Score row arrives through the binder
}

conn!.Reducers.OnAddScore += OnAddScoreResult;
conn.Reducers.AddScore(10);
// later / in _ExitTree:
conn.Reducers.OnAddScore -= OnAddScoreResult;
```

The callback's extra parameters mirror the reducer's arguments (`OnJoinWorld(ctx, profileId)`,
`OnAddScore(ctx, points)`). Use `conn.Reducers.OnYourReducer` (PascalCase) — the generated name
matches the Rust reducer.

### 11. Connection lifecycle

- `DatabaseConnector.Connected` fires after `OnConnect`. Because the autoload can connect before
  any scene loads, consumers must handle both orders:

  ```csharp
  var connector = DatabaseConnector.Instance!;
  if (connector.Conn?.IsActive == true) DoWork();          // already connected
  else connector.Connected += DoWork;                      // connect later
  ```

  Both `TableSubscriber` and `TableBinderComponent` already do this for you; only direct
  consumers need to copy it.
- `Disconnected` fires on an unexpected drop (not on deliberate shutdown). Show a reconnect
  panel, then call `connector.Connect()` (rebuilds `Conn` with the saved token) and reload your
  menu scene. **Binders do not migrate to the rebuilt connection** — a bound binder stays
  attached to the old, dead `DbConnection`, so re-entering the scene is what rewires everything.
- `IsLocal(Identity id)` compares against the local identity; useful to ignore your own rows in
  global tables.
- The pump is exception-safe: `_Process` wraps `FrameTick()` in try/catch so one throwing handler
  cannot kill all networking. Errors are logged to the console — fix them rather than ignoring
  the spam.
- `--pN` launch args (exact `^--p\d+$`, so Godot's own `--path` is ignored) suffix the auth token
  key, giving each parallel editor instance a distinct identity.

### 12. Verify

- Build the client: `dotnet build <project>.csproj` from the `client/` folder (or let Godot compile on run).
- Run the game (F5) and watch the output panel. On success you should see:
  `[TableSubscriber] Lobby subscription ready`, then `[TableSubscriber] Game subscription ready`
  after joining.
- If rows are missing, set `Verbose = true` on one binder — it logs `bound <Table> at <path>` and
  `first insert for <Table>`. If you never see `bound`, the binder is not reaching the connection
  (no `IEntity` ancestor, unset `TableName`, or the connector autoload is missing).
- Server side: `spacetime logs <db-name>` shows reducer logs (`log::info!` etc.).

---

## Recipes

### Replay vs. event tables

| Table kind | `ReplayExistingRows` | Why |
|---|---|---|
| Persistent table/view (catalog, profile, world state, a live zone) | `true` | Late binders/late joiners must see rows that already exist. Replay is exactly "iterate the cache and treat each row as an insert". |
| Event table (one-shot append-only effects, declared `event` server-side) | `false` | No last-seen-id tracking exists. Replay re-fires the whole history every time the scene is entered. |

In the origin project, `BulletPatternEvent` and `BulletControlEvent` are event tables (replay
off); `GroundZone`, `AbilityCharge`, catalogs, and AOI views are persistent (replay on). When in
doubt: if the server row is deleted/expired after its effect, replay is safe; if the row is an
immutable log entry, replay is a bug.

### Reading the cache without a binder

Inside a handler you can read the whole cached table through the connection. This is for
UI rebuilds that need more than the last row, not a replacement for binders:

```csharp
foreach (var row in conn.Db.Score.Iter())
{
    if (row.Points > threshold) AddVisual(row.Id);
}
```

`IEnumerable` rows come straight off the generated table field (`conn.Db.Score.Iter()`). Keep
row-event wiring in binders either way.

### Per-caller views

Any Rust `#[view]` whose accessor is `public` generates a normal client table field, so it
subscribes and binds identically to a plain table. The server decides what each caller sees
(AOI filtering, own profile only, etc.) — the client never has to filter for authority, though it
may filter for relevance (e.g. one chest's items out of `NearbyChestItems`).

### Dynamically spawned consumers

`TableBinderComponent` is an ordinary node, so a code-spawned entity can create one in `_Ready`,
pick `TableName` in code (it is a `string` property, not `[Export]`-only), and connect its own
handlers with `+=`:

```csharp
var binder = new TableBinderComponent { TableName = "Score", ReplayExistingRows = true };
AddChild(binder);
binder.RowInserted += OnScoreInserted;   // C# event mirroring the Godot signal
```

Prefer the declared-in-`.tscn` form for scene-owned nodes; use this only for data-driven
spawns.

---

## File reference

### `DatabaseConnector.cs`

| Member | Notes |
|---|---|
| `static DatabaseConnector? Instance` | Set in `_Ready`, cleared in `_ExitTree`. `null` only before the autoload readies or after shutdown. |
| `[Export] Host`, `[Export] DbName`, `[Export] TokenAppend` | Connection target. Defaults are origin-specific; override on the autoload scene. |
| `DbConnection? Conn` | Rebuilt by `Connect()`. Always null-check (`Conn?.Reducers.X(...)`). |
| `Identity? LocalIdentity`, `string Username` | `Username` is kept current via the origin `LocalLobbyPlayer` hook — replace with your own table. |
| `void Connect()` | Builds the connection with `DbConnection.Builder()`, per-host token, and the three handlers. Call again to reconnect. |
| `bool IsLocal(Identity id)` | Identity comparison. |
| `signal Connected` | Emitted once the connection is established; consumers wire callbacks/waves here. |
| `signal Disconnected` | Emitted on an unexpected drop; not emitted during deliberate shutdown. |
| `_Process` | `Conn?.FrameTick()` in try/catch. **If you write your own connection and forget `FrameTick`, no callback ever fires.** |

### `TableSubscriber.cs`

| Member | Notes |
|---|---|
| `static string[] BaseTables` / `LobbyTables` / `GameTables` | The wave contents. **Edit these for your module.** PascalCase generated accessor names. |
| `static IEnumerable<string> AllSubscribedTables` | Distinct union of the three, consumed by `TableBinderComponent`'s dropdown. |
| `SubscribeLobby()` / `UnsubscribeLobby()` | Lobby wave handle. |
| `SubscribeGame()` / `UnsubscribeGame()` | Game wave handle (`OnApplied` logs, `OnError` logs). |
| `event Action? LobbyApplied` | Raised when the lobby wave finishes applying (rows cached). |
| `bool LobbySubActive` | The lobby handle's `IsActive`, safe to read on a re-shown lobby. |
| `static Vector2 LapQ` / `LapR` | **Origin-specific** torus lap vectors from a `MapConfig` row. Remove or adapt. |
| `_ExitTree` | Resets statics, unhooks `Connected`, unsubscribes both waves (skipped when the connection is already dead). |

`OnConnected` subscribes base + lobby. The base wave's `Subscribe(...)` has no applied handler
by design (late binders replay what already exists); only the lobby wave has one.

### `TableBinderComponent.cs`

| Member | Notes |
|---|---|
| `string TableName` | Inspector dropdown from `TableSubscriber.AllSubscribedTables`; declared via `_GetPropertyList`, so it serializes like an export. |
| `[Export] bool ReplayExistingRows` | Re-emit cached rows as `RowInserted` on bind. Persistent tables only. |
| `[Export] bool Verbose` | Log bind + first insert. Debug aid. |
| `object? LastRow` | Inserted row, or the new row after an update. |
| `object? LastOldRow` | The row's pre-update value (update events only). |
| `object? LastDeletedRow` | The removed row. |
| `signal RowInserted` / `RowUpdated` / `RowDeleted` | Arg-less; read the `Last*` properties. |
| Lifecycle | `_Ready` skips registration in the editor (`[Tool]`); `OnEntityReady` binds to the connection (or waits for `Connected`); `_ExitTree` unhooks every table event and the `Connected` hook. |

`OnUpdate`/`OnDelete` do not exist on event tables; the binder hook-up simply skips the missing
events.

### `DatabaseConnector.tscn`

One `Node` with `DatabaseConnector.cs` attached. It is referenced from `project.godot`'s
`[autoload]` section. Keep it minimal — connection configuration belongs in its exports.

---

## Rules (do not break these)

1. **Pump `FrameTick()` exactly once per frame.** The autoload does it; do not add a second pump or a second connection.
2. **No direct `conn.Db.*.OnInsert/OnUpdate/OnDelete` in gameplay code.** Use one `TableBinderComponent` per table per consumer and wire signals in the `.tscn`. Only infrastructure (`TableSubscriber`, `DatabaseConnector`) keeps internal hooks.
3. **`ReplayExistingRows = false` on every event table.** No exceptions.
4. **A table must be named in a wave before any binder can see it.** Add the accessor to `BaseTables`/`LobbyTables`/`GameTables` when you add the table.
5. **Regenerate bindings after every server schema change** and never hand-edit `module_bindings`.
6. **Reducers are the only write path.** Call `Conn?.Reducers.X(...)`; read the resulting rows. No optimistic stat/damage math on the client.
7. **Guard the already-connected case** (`Conn?.IsActive == true`) whenever you hook `Connected` yourself.
8. **Every binder/subscriber needs an `IEntity` ancestor** — without one `ComponentRegistration.Register` logs an error and the component never receives events.
9. **Handlers run on the main thread inside `_Process`.** Keep them cheap; batch large row bursts.
10. **After a reconnect, rebuild the scene.** Handlers stay attached to the dead `DbConnection` otherwise.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Binder logs `'X' is not in TableSubscriber's subscription waves` | Name missing from a wave | Add it to `BaseTables`/`LobbyTables`/`GameTables` |
| Binder logs `no table 'X' in the generated module bindings` | Stale bindings or wrong casing | Regenerate bindings; use the exact generated accessor name |
| Binder never logs `bound X`, no rows | No `IEntity` ancestor, `TableName` unset, or `TableSubscriber` absent from the scene | Fix the scene hierarchy / inspector; check the config warning on the node |
| Signals connected but nothing runs | Signals not wired in the `.tscn`, or `ReplayExistingRows` off for a persistent table | Wire `RowInserted` etc.; enable replay for persistent tables |
| Old events re-fire on scene entry | `ReplayExistingRows` on an event table | Turn it off |
| Nothing at all happens; no subscription logs | Autoload missing, wrong `Host`/`DbName`, or server not running | Check `project.godot` autoload, exports, and `spacetime logs` |
| Reducer called but nothing changes | Reducer rejected (validation) — reducers return no data | Subscribe to `Conn.Reducers.OnYourReducer`, inspect `ctx.Event.Status`; check server logs |
| Callbacks stop after a disconnect | Binder still hooked to the dead `DbConnection` | Reconnect then reload the scene |
| Parallel game instances share a character | Missing `--pN` | Launch with `--pN` (e.g. `--p1`, `--p2`) |
| Client won't compile: `Component` not found | The component framework base classes are missing | Copy them or paste [Appendix A](#appendix-a--minimal-component-framework-shim) |
| Client won't compile: `LocalLobbyPlayer`/`MapConfig` not found | Origin-specific hooks left in place | See [Adapt the copied files](#6-adapt-the-copied-files) |

---

## Appendix A — minimal component framework shim

`TableSubscriber` and `TableBinderComponent` only need these four pieces. Copy the origin
project's real files when available; otherwise paste this into any `.cs` files inside the client
project. Nothing here is SpacetimeDB-specific.

```csharp
#nullable enable
using Godot;
using System;
using System.Collections.Generic;

// === IComponent.cs ===
public interface IComponent
{
    IEntity? Entity { get; }
}

// === IEntity.cs ===
public interface IEntity
{
    void RegisterComponent(IComponent component);
    void UnregisterComponent(IComponent component);
    IComponent? GetComponent(Type type);

    T? GetComponent<T>() where T : IComponent => GetComponent(typeof(T)) is T match ? match : default;
}

// === ComponentRegistration.cs ===
public static class ComponentRegistration
{
    /// Walks ancestors for the nearest IEntity and registers the component there.
    public static IEntity? Register(Node node, IComponent component)
    {
        for (var ancestor = node.GetParent(); ancestor != null; ancestor = ancestor.GetParent())
        {
            if (ancestor is IEntity entity)
            {
                entity.RegisterComponent(component);
                return entity;
            }
        }
        GD.PushError($"[Component] {node.GetType().Name} at '{node.GetPath()}' has no IEntity ancestor.");
        return null;
    }

    public static void ValidateRequired(Node node, IEntity entity, Type[] required)
    {
        foreach (var type in required)
            if (entity.GetComponent(type) == null)
                GD.PushWarning($"[Component] {node.GetType().Name} at '{node.GetPath()}' is missing required sibling component {type.Name}.");
    }
}

// === Component.cs ===
public abstract partial class Component : Node, IComponent
{
    public IEntity? Entity { get; private set; }

    public override void _Ready()
    {
        Entity = ComponentRegistration.Register(this, this);
        if (Entity == null) return;
        OnRegistered();
        CallDeferred(nameof(AfterSiblingsReady)); // deferred so every sibling has finished _Ready
    }

    public override void _ExitTree()
    {
        Entity?.UnregisterComponent(this);
        Entity = null;
    }

    protected virtual void OnRegistered() { }
    protected virtual void OnEntityReady() { }   // TableBinderComponent binds here
    protected virtual Type[] GetRequiredComponents() => Array.Empty<Type>();
    protected T? GetSibling<T>() where T : IComponent => Entity != null ? Entity.GetComponent<T>() : default;

    private void AfterSiblingsReady()
    {
        if (Entity == null) return;
        ComponentRegistration.ValidateRequired(this, Entity, GetRequiredComponents());
        OnEntityReady();
    }
}

// === EntityRegistry.cs ===
public sealed class EntityRegistry
{
    private readonly List<IComponent> components = new();
    private readonly Dictionary<Type, IComponent?> cache = new();

    public void Register(IComponent component) { components.Add(component); cache.Clear(); }
    public void Unregister(IComponent component) { components.Remove(component); cache.Clear(); }

    public IComponent? Get(Type type)
    {
        if (cache.TryGetValue(type, out var cached)) return cached;
        IComponent? found = null;
        foreach (var component in components)
        {
            if (!type.IsInstanceOfType(component)) continue;
            found = component;   // first match wins; lookups are subclass-friendly and order-dependent
            break;
        }
        cache[type] = found;
        return found;
    }
}

// === Entity.cs — convenience for plain-Node scene roots ===
public partial class Entity : Node, IEntity
{
    private readonly EntityRegistry registry = new();
    public void RegisterComponent(IComponent component) => registry.Register(component);
    public void UnregisterComponent(IComponent component) => registry.Unregister(component);
    public IComponent? GetComponent(Type type) => registry.Get(type);
    public T? GetComponent<T>() where T : IComponent => registry.Get(typeof(T)) is T match ? match : default;
}
```

Scene roots that need a native base (`CharacterBody2D`, `Area2D`, …) cannot inherit `Entity`;
implement `IEntity` directly with a `private readonly EntityRegistry registry = new();` field and
forward the four members, exactly like `Entity` does.

**Alternative without the framework:** if the new project does not want it, `sstdbsdk` can be
ported to plain `Node` with two mechanical edits — change `: Component` to `: Node` in
`TableBinderComponent.cs` and `TableSubscriber.cs`, and rename the lifecycle overrides back to
`_Ready`/`_ExitTree` (`OnEntityReady` → the runtime body of `_Ready` with the editor guard;
`OnRegistered` → `_Ready`). This drops `IEntity`, `GetSibling<T>()` and the required-sibling
validation, none of which `sstdbsdk` itself uses beyond registration. The framework route is
recommended: it is what the origin project uses, so the rest of the SDK's idioms line up.
