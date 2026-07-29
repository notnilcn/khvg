# 01 Architecture & Sync Model

## Assumed knowledge

None — this is the entry point. Start here before any other numbered doc; see [[00 Roadmap]] for the reading order and the conventions the rest of this set follows.

## The 30-second version

The server is a **SpacetimeDB module** — Rust code that defines database tables plus **reducers** (server functions a client calls, run as atomic transactions) — published once and then running headless, ticking a handful of scheduled reducers even with nobody connected. The client is a **Godot 4.6 mono** app that opens one `DbConnection` to that module and **subscribes** to specific tables — live queries SpacetimeDB keeps continuously in sync, pushing row inserts/updates/deletes as they happen, instead of the client polling for state. Nothing in this game has a traditional request/response API: the client calls a reducer to *change* something and reads the result back off its subscribed tables, not off a return value (reducers don't return data at all). The rest of this doc covers the server's module layout, the client's top-level connection/subscription machinery, and the bootstrap and connect/disconnect sequences that everything else in this doc set builds on.

## System flowchart

```sync
![[99 End-to-End Timeline Flowchart#^boot-1{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^boot-2{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^boot-3{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^boot-4{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^boot-5{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^conn-1{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^conn-2{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^conn-3{seamless:true,title:false,marker:08.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^conn-4{seamless:true,title:false,marker:09.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^conn-5{seamless:true,title:false,marker:10.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^conn-6{seamless:true,title:false,marker:11.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^conn-7{seamless:true,title:false,marker:12.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^end-1{seamless:true,title:false,marker:13.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^end-2{seamless:true,title:false,marker:14.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^end-3{seamless:true,title:false,marker:15.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^end-4{seamless:true,title:false,marker:16.}]]
```

## Main body

### SpacetimeDB in three ideas

If you've never used SpacetimeDB, three ideas cover what you need for this doc set:

- **Tables** are ordinary Rust structs annotated `#[table(...)]`. `public` tables are the ones a client can subscribe to; private tables exist only for server-internal bookkeeping.
- **Reducers** (`#[reducer]` functions) are the *only* way any data changes. They run as atomic transactions against `ctx.db`, take a `&ReducerContext` (never mutable — determinism is enforced by the type system, not convention), and either succeed or return `Err(String)`; there is no other error channel. A few reducers are **lifecycle hooks** SpacetimeDB itself invokes rather than a client: `init` (once, at first publish), `client_connected`/`client_disconnected` (per connection), and scheduled reducers (see below).
- **Subscriptions** are SQL-like queries (`q.From.SomeTable()`) a connected client registers; SpacetimeDB keeps the client's local mirror of the matched rows continuously in sync and fires `OnInsert`/`OnUpdate`/`OnDelete` callbacks as rows change. Subscribing is not a one-off fetch — the query stays live until explicitly unsubscribed or the connection drops.

One more mechanism this project leans on heavily: **scheduled tables**. A table declared `scheduled(reducer = X)` holds rows whose `scheduled_at: ScheduleAt` field is a time or a repeating interval; SpacetimeDB invokes `X` automatically when a row comes due, and (for `Interval` rows) reschedules it. This is how enemy behavior/spawn ticks and consumable-effect ticks run continuously without any client driving them — see `^boot-1` above and [[06 Enemy AI & Bullet Patterns|06]] / [[05 Item, Equipment & Enchantment System|05]] for what those specific ticks do.

### Server module layout

`server/spacetimedb/src/lib.rs` is the whole module's table of contents — six `mod` declarations, nothing else:

```
mod main;    // bootstrap, admin, seeding, debug, global constants
mod world;   // hex grid, terrain gen, buildings
mod player;  // identity, profiles, lobby, movement, inventory, XP
mod enemy;   // templates, phases, attack sequences, bullet events
mod item;    // unified item/enchantment catalog
mod combat;  // the one place damage is computed and applied
```

[[lib.rs##mod main;|lib.rs]]

Each gameplay module (`world`, `player`, `enemy`, `item`) follows the same internal split — `tables.rs`/`def_tables.rs`/`instance_tables.rs` for schema, `methods.rs` for pure helpers and math, `reducers.rs` for the client-callable entry points, `views.rs` for subscription-facing filtered queries — except `world`, whose generation logic is large enough to live in its own `terrain/` sub-pipeline. `combat` is the odd one out structurally: it's a single `mod.rs` because it's deliberately *not* split by table — it's the one shared pipeline every damage-dealing reducer (`report_hit` in `player/`, `report_enemy_hit` in `enemy/`) delegates into, so that damage math has exactly one implementation regardless of which side got hit. Full per-file breakdowns: `server/CLAUDE.md`.

Every module's tunable constant lives in one place, `main/global.rs` — chunk/hex sizing, AOI subscription radii, player stat baselines, inventory slot layout, the enemy simulation tick rate, world timers. There's no per-module scattering of magic numbers; if you're chasing a "why is this value X" question anywhere in the docs that follow, `global.rs` is the first place to check.

### Client top-level structure

`client/Scripts/Game/GameManager.cs` is the root node ("Main") of `Scenes/main.tscn` and implements `IEntity` directly (it needs a `Node2D` native base, so it can't derive from the plain-`Node` `Entity` base class — same reason `LocalPlayer`/`Enemy` implement `IEntity` directly rather than inheriting it). It holds essentially no logic of its own. Instead it's a thin **static facade** over sibling components registered under it — `ConnectionComponent`, `SubscriptionComponent`, `CatalogComponent`, `EntitySpawnerComponent`, `LobbyComponent`, plus the camera/overlay components covered in [[08 Client Rendering & Camera|08]]. Static properties like `GameManager.Conn`, `.Username`, `.GetItem(...)` all resolve through `GetComponent<T>()` against the currently-registered `instance`, which is how code with no path back to Main's node tree — spawned entities, UI panels, debug overlays — reaches the connection or the item cache without a node-path walk. [[GameManager.cs##public partial class GameManager|GameManager]]

The full entity/component registration mechanism (how a component finds its owning entity, the `OnRegistered`/`OnEntityReady` lifecycle, sibling lookups) is [[02 Entity & Component Framework|02]]'s subject — this doc only needs the piece that matters for bootstrapping: `ConnectionComponent` reacts the instant it registers (`OnRegistered` immediately opens the connection), and every other component that needs the connection waits for its `Connected` signal instead of assuming a connection order.

### The three connection-side components

- **`ConnectionComponent`** owns the single `DbConnection` for the whole client. It builds the connection on registration, pumps `Conn.FrameTick()` every `_Process` frame (this is what actually delivers queued table updates and fires callbacks — without it, nothing arrives no matter how long the client waits), and disconnects cleanly in `_ExitTree`. It also tracks `LocalIdentity` and `Username`, and exposes `IsLocal(Identity)` — the check every remote-vs-local rendering/logic branch in the client uses.
- **`SubscriptionComponent`** owns every subscription *wave* — named groups of queries opened/closed together. Today there are three: the always-on **base wave** (`AllTextures`/`AllItems`/`AllEnchantments`, opened once and never torn down), the **lobby wave** (`SubscribeLobby()`/`UnsubscribeLobby()`, active while the player is picking/creating a profile), and the **game wave** (`SubscribeGame()`/`UnsubscribeGame()`, active once in-world — everything AOI-scoped: nearby players/enemies/loot/terrain, plus the local player's own data/stats/inventory/position rows). It also mirrors `MapConfig`'s `LapQ`/`LapR` torus-wrap vectors, exposed for anything doing wrapped-distance math ([[03 World & Hex Grid|03]]). Why waves at all, rather than one subscription for everything: a player sitting in the lobby has no in-world position yet, so AOI-scoped queries have nothing meaningful to filter by — splitting the waves keeps the lobby's subscription footprint to just what the lobby UI needs, and defers every AOI query until `join_world` gives it something to be scoped around ([[04 Player System|04]]).
- **`CatalogComponent`** turns the base wave's three tables into O(1)-lookup dictionaries (`textureCache`/`itemCache`/`enchantmentCache`) and fires `EnchantmentsChanged` so UI (the enchantment socket/remove panel in [[05 Item, Equipment & Enchantment System|05]]) can refresh reactively instead of re-querying. This is the layer `GameManager`'s `GetItem`/`GetEnchantment(s)`/`GetResPath` static methods actually read from.

All three follow the identical pattern: `OnRegistered` hooks `ConnectionComponent.Connected`, and the real work happens in an `OnConnected` handler once that signal fires — never in `OnRegistered` itself, since the connection doesn't exist yet at that point (`conn-4` through `conn-7` above). This is the pattern to expect from any future sibling component that needs the connection: hook `Connected`, don't assume registration order.

## Known gaps / stubs

- **No persistent login.** `ConnectionComponent.Connect` always calls `WithToken(null)`; the `AuthToken.Init`/`AuthToken.SaveToken`/`AuthToken.Token` calls that would load/persist a saved identity token are present but commented out (`ConnectionComponent.cs` lines 31, 35, 46). Every connection today is a fresh anonymous identity — closing and reopening the client does not resume the previous identity's session.
- **Reconnect-while-in-world is a logged bug, not a supported flow.** `client_connected` treats finding an existing `LoggedInPlayer` row for the connecting identity as an error case (`log::error!`) and recovers by force-moving it to `LoggedOutPlayer` rather than resuming the in-world session — see `conn-3`. This can only happen with the (currently unused) persistent-token path above, so it's latent rather than reachable today.

## Where to go next

Read [[02 Entity & Component Framework]] next — it covers the composition pattern (`IComponent`/`IEntity`/registries on the client, archetype helpers on the server) that every later doc assumes. After that, [[04 Player System]] picks up exactly where `conn-6` leaves off: the lobby wave's data feeding profile creation and `join_world`.
