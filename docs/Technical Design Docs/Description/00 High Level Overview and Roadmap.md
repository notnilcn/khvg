# 00 High Level Overview and Roadmap
## What these docs are

A narrative, learn-from-scratch companion to the terse maintainer references — root `CLAUDE.md`, `client/CLAUDE.md`, `server/CLAUDE.md`. Those files are dense module maps for someone who already knows the codebase; these docs are for someone who doesn't. Where the two disagree, the `CLAUDE.md` files and the code win — these docs link out to them rather than restate them.
## Who these docs are for

An intermediate programmer who is brand new to Godot, C#, Rust, and SpacetimeDB, but who already knows general programming concepts (loops, classes, dictionaries, event handlers). Godot- and SpacetimeDB-specific ideas — reducers, subscriptions, node trees, scenes, `.tscn` files — get a brief inline explanation or analogy the first time they matter.
## High Level Overview

### The game in one paragraph

This is a **bullet-hell MMO roguelike**: players move around a shared hex-grid world in real time, enemies fire dense telegraphed bullet patterns, and combat is about dodging rather than tanking. It's made of exactly two programs that talk over the network:

- **`client/`** — a Godot 4.6 (mono, C#) game. Everything the player sees and touches.
- **`server/`** — a SpacetimeDB module written in Rust. The single source of truth for all game state: every player's position, every enemy, every item.

The server is **authoritative**. Clients never decide outcomes — they *report intentions* ("I moved here", "my shot hit that enemy") and *mirror* whatever the server says the world looks like now. Keep that one rule in mind and most design choices in both codebases follow from it.

### Godot for Python programmers

Godot is a game engine plus editor; the `client/` folder is one Godot project. Four ideas cover most of what you'll meet:

- **Everything on screen is a `Node` in a tree** — the "scene tree". A sprite, a camera, a hitbox, a UI panel are all nodes; parents own children. Think of it as the DOM, but for a game.
- **A scene (a `.tscn` file) is a saved subtree** — a declarative text file listing nodes and their properties, roughly like a Kivy `.kv` or Qt `.ui` file. Scenes are the unit of reuse: `drop.tscn` is instanced once per loot drop on the ground, `local_player.tscn` builds your character. Instantiating a scene in code ≈ calling a constructor that builds the whole subtree.
- **Behavior lives in C# scripts attached to nodes.** A script is a class extending a node type (`Node2D`, `CharacterBody2D` for physics bodies, `Control` for UI…). Two lifecycle methods recur everywhere: `_Ready()` runs once when the node enters the tree (≈ `__init__`, deferred until the object is fully wired into the tree), and `_Process(delta)` runs every frame (≈ the body of the game loop; `delta` is seconds since the last frame). The client is 100% C# — if you've read any Java or TypeScript it will read fine.
- **Signals are the pub-sub.** A node emits a signal (e.g. `HealthDidZero`), other nodes subscribe to it — like PyQt signals or a tiny in-process event bus. The client uses signals wherever one object needs to notify strangers, instead of reaching across the tree with direct method calls.

One project-specific convention makes the whole client navigable: an entity (player, enemy, drop) is a root node whose children are **components** named `<Role>Component` — `HealthComponent`, `CombatComponent`, and so on — each holding exactly one concern. Node name = class name = file name, so the scene tree in the editor is literally a map of the classes. [[02 Entity & Component Framework|02]] covers the pattern in full.

### SpacetimeDB for Python programmers

SpacetimeDB replaces the usual "database + web framework + websocket layer" stack with a single thing: you write a **module** (Rust code defining tables and functions), publish it, and SpacetimeDB hosts it as a live server. Five ideas cover what you'll meet constantly:

- **Tables ≈ Django models.** Rust structs annotated `#[table]`. `public` tables are visible to clients; private ones are server-internal bookkeeping.
- **Reducers ≈ POST endpoints that can't return anything.** They are the *only* way data changes. A reducer runs as an atomic database transaction: it validates, mutates tables, and either succeeds or returns an error string. Because reducers return nothing, clients learn what happened by watching tables (next bullet), not from a response body.
- **Subscriptions ≈ live queries.** The client registers a query ("all enemies in the chunks near me") and SpacetimeDB keeps a local mirror of the matching rows continuously in sync, firing `OnInsert`/`OnUpdate`/`OnDelete` callbacks as rows change. No polling, no hand-rolled cache invalidation — this is the part that replaces a bespoke websocket protocol.
- **Scheduled tables ≈ cron rows.** A table can schedule a reducer to fire at a time or on an interval; this is how enemies keep moving and attacking even with zero players connected.
- **Generated bindings.** `spacetime generate` produces `client/Scripts/module_bindings/` — C# classes for every table and reducer — so the client works with typed objects instead of raw protocol messages. That folder is generated: never read it line-by-line, never edit it.

[[01 Architecture & Sync Model|01]] builds all of this up properly; this section is just the survival kit.

### The sync loop — how a click becomes pixels

Almost every interaction in the game follows the same four-beat loop:

1. The client **calls a reducer** — e.g. movement sends `ReportMovement` with the player's new position.
2. The server runs it as a transaction and **updates its tables** (here, the player's position row).
3. SpacetimeDB **pushes the changed rows to every client subscribed to them** — and thanks to area-of-interest (AOI) filtering, "subscribed" means "players near enough to actually see it".
4. Each client **mirrors the new row into its scene tree** (the entity's components copy the values, signals fire) and the next frame renders it.

Reducers in, subscriptions out. There is no REST API and no request/response gameplay path anywhere in the codebase — [[01 Architecture & Sync Model|01]] walks the full bootstrap-to-disconnect sequence this loop lives inside.

### The client at a glance

All hand-written code is in `client/Scripts/`:

| Folder | What lives there | Deep dive |
|---|---|---|
| `Game/` | The root scene's components: the connection ([[ConnectionComponent.cs##public void Connect\|Connect]]), subscription waves, item catalog cache, entity spawner, lobby UI, debug overlay | [[01 Architecture & Sync Model\|01]], [[09 Admin, Debug & World Lifecycle\|09]] |
| `Components/` + `Entities/` | The entity/component framework itself, plus one subfolder per concern: `Combat/`, `Terrain/`, `Camera/`, `Inventory/`, `Movement/`… | [[02 Entity & Component Framework\|02]] |
| `Players/` | `LocalPlayer.cs` (your character: input, inventory state), `RemotePlayer.cs` (other players, interpolating toward server positions), `Enemy.cs` (a server-driven puppet) | [[04 Player System\|04]], [[06 Enemy AI & Bullet Patterns\|06]] |
| `Components/Terrain/` + `World/` | Rendering the hex world streamed from the server; torus-wrap math | [[03 World & Hex Grid\|03]] |
| `Components/Camera/` | The 2D camera rig and the 3D backdrop viewport | [[08 Client Rendering & Camera\|08]] |
| `Components/Combat/` + `Components/Weapon/` | Hitboxes/hurtboxes that *report* hits to the server — they never compute damage locally | [[07 Combat & Damage Math\|07]] |
| `Components/Inventory/` + `Items/` | Hotbar/backpack/equipment UI, enchantment socketing, loot drops on the ground | [[05 Item, Equipment & Enchantment System\|05]] |
| `module_bindings/` | Generated SpacetimeDB C# bindings — do not read, do not edit | — |

`GameManager.cs` is the root node of the main scene and the front door: its child components own the connection and subscriptions, and its static facade is how the rest of the client reaches them.

### The server at a glance

`server/spacetimedb/src/lib.rs` is the whole module's table of contents — six `mod` declarations and nothing else:

| Module    | What lives there                                                                    | Deep dive                                                                                       |
| --------- | ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| `main/`   | Bootstrap ([[lifecycle.rs##pub fn init                                              \| init]] runs once at first publish), admin tools, content seeding, debug, every tunable constant | [[09 Admin, Debug & World Lifecycle \| 09]] |
| `world/`  | Hex-grid and torus math, deterministic procedural terrain generation, buildings     | [[03 World & Hex Grid                                                                           \| 03]]                                |
| `player/` | Login state, profiles, movement, inventory slots, XP/leveling, loot drops           | [[04 Player System                                                                              \| 04]]                                |
| `enemy/`  | Enemy templates, behavior ticks, phases and attack sequences, bullet-pattern events | [[06 Enemy AI & Bullet Patterns                                                                 \| 06]]                                |
| `item/`   | The unified item catalog and enchantments; admin catalog reducers                   | [[05 Item, Equipment & Enchantment System                                                       \| 05]]                                |
| `combat/` | The single damage pipeline — the *only* place damage is computed and applied        | [[07 Combat & Damage Math                                                                       \| 07]]                                |

Two structural notes make the server readable:

- **It mirrors the client's entity/component idea in relational form**: one table per concern, joined by ids like `profile_id` / `enemy_id`. An "enemy" is not one row but a bundle of rows across tables — the same logical entity the client builds out of component nodes. [[02 Entity & Component Framework|02]] gives the full composition map.
- **Gameplay modules share a four-file shape**: `tables.rs` (schema), `reducers.rs` (the callable entry points), `methods.rs` (pure helpers), `views.rs` (the subscription queries clients can ask for). Once you've read one module, the rest look familiar.

### Where to go next

The reading-order table below is the full path; [[01 Architecture & Sync Model|01]] is the real starting point. If you only read three docs, read [[01 Architecture & Sync Model|01]] (the sync model everything runs on), [[02 Entity & Component Framework|02]] (the shared entity pattern), and whichever of 03–09 covers the system you're about to touch.

## Reading order & status

| # | Doc | Status | Covers |
|---|---|---|---|
| 00 | Roadmap | done | This doc — the big picture, Godot/SpacetimeDB primers, the client & server tour, conventions, reading order |
| 01 | [[01 Architecture & Sync Model]] | done | SpacetimeDB primer, server module layout, server bootstrap, a client connecting, subscription waves, session end |
| 02 | [[02 Entity & Component Framework]] | done | The compositional entity/component pattern shared by client and server; composition map per logical entity |
| 03 | [[03 World & Hex Grid]] | done | Torus hex grid math, chunking, procedural terrain generation, buildings |
| 04 | [[04 Player System]] | done | Login/lobby/profile flow, joining the world, movement & AOI, XP/leveling |
| 05 | [[05 Item, Equipment & Enchantment System]] | done | The unified item catalog, inventory/equip reducers, enchantment socketing |
| 06 | [[06 Enemy AI & Bullet Patterns]] | done | Enemy templates, phases, attack sequences, bullet pattern events |
| 07 | [[07 Combat & Damage Math]] | done | The single damage pipeline: outgoing/incoming formulas, death and kill handling |
| 08 | [[08 Client Rendering & Camera]] | done | Camera rig, 2D presenter, the 3D backdrop viewport |
| 09 | [[09 Admin, Debug & World Lifecycle]] | done | Admin tools, content seeding, debug overlay, known admin-reducer bugs |
| 10 | End-to-End Flowchart | done | `99 End-to-End Flowchart.canvas` spatial arrangement + final cross-doc verification pass |

Status values: blank (not yet written) or `done`.

## Conventions used across these docs

**Wikilinks to code** use the `block-link-plus` plugin's line-search syntax:

```
[[filename##text on the target line|display text]]
```

Bare filename, no path — the plugin searches the vault (e.g. `[[lifecycle.rs##pub fn init|init]]`, `[[GameManager.cs##public override void _Ready|_Ready]]`). The search text is the function/struct signature line, kept unique within that file. Every step in the timeline and every claim in a doc's main body links to the code that implements it wherever possible — if you follow a link and the text isn't there anymore, the code has moved and the doc is stale.

**Sync-embedded steps**: the systems all run through one shared, chronologically-ordered narrative — `99 End-to-End Timeline Flowchart.md`. Individual system docs don't retype those steps; they transclude them via the `sync-embeds` plugin:

````
```sync
![[99 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:01.}]]
```
````

so a step's prose lives in exactly one place. Each system doc renumbers the steps it embeds starting from `01.` regardless of the step's own number in `99` (`99` numbers within its own named sections, e.g. `move-7` might render as `03.` inside `04 Player System`). Free prose around an embed is for what the timeline doesn't say: why a mechanism works that way, invariants, edge cases, formulas.

**Cross-doc references** use plain Obsidian wikilinks: `[[03 Player System]]`, or with custom display text `[[07 Combat & Damage Math|07]]`.

**Step anchors**: within `99`, every numbered step ends in a block anchor `^prefix-N` (e.g. `^boot-3`, `^move-7`). Each prefix (`boot`, `conn`, `lobby`, `join`, `move`, `equip`, `enemy`, `combat`, `camera`, `end`, `admin`) restarts its own count at 1. Steps refer to each other by anchor name in prose ("the subscription opened in `conn-5`"), never by a bare number, since numbers are only stable within a prefix.

**The canvas** (`99 End-to-End Flowchart.canvas`, doc 10): a spatial arrangement of the same timeline, built last. Its nodes reference `99`'s anchors directly — it adds layout, not new prose.

## Aspirational systems

Anything in the Game Design Docs (`docs/01`–`05`, `99 (outdated)...`) that has no corresponding code — guild territory PvP, base-building beyond placing/removing a single building tile, biomes as a gameplay mechanic rather than a terrain-generation input, most enchantment breadth beyond the current socket/remove pair — is not documented here as if it existed. Docs that touch the edge of one of these say so explicitly rather than filling the gap with design-doc content.
