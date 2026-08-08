## What this project is

A **bullet-hell MMO roguelike**: Godot 4.6 mono C# client (`client/`) + SpacetimeDB Rust server module (`server/spacetimedb/src/`). Repo root is the parent of `docs/`'s parent — all paths below are relative to repo root. The client is **fully compositional** (entity/component architecture modeled on `code_examples/comedot`); the server mirrors that compositionally: one table per concern = a component, ids like `profile_id`/`enemy_id` join a logical entity's rows across tables, and "archetype helpers" bundle the rows of one entity.

The docs you're writing live in `docs/Technical Design Docs/Description/` and are the narrative, learn-from-scratch companion to the terse maintainer references (`CLAUDE.md` at repo root, `client/CLAUDE.md`, `server/CLAUDE.md`). The docs folder is an Obsidian vault with the `sync-embeds` and `block-link-plus` community plugins installed — the conventions below depend on both.

## Hard rules for every session

1. **The code is the only source of truth.** `CLAUDE.md` files are accurate jump-off points (use their module maps to find files fast), but verify every claim against the actual code before writing it. Where they disagree, code wins.
2. **Never read `client/Scripts/module_bindings/`** — generated SpacetimeDB bindings.
3. **Document what the code does, not what it's planned to do.** Flag TODOs/stubs/known bugs explicitly in the doc's "Known gaps" section and inline where relevant (e.g. "`LobbyGui` is scene navigation only — not yet wired to the `create_profile`/`join_world` reducers").
4. **Don't restate the CLAUDE.md files.** Link to them; these docs are the narrative complement.
5. **Audience**: intermediate programmer, brand new to Godot, C#, Rust, and SpacetimeDB. Explain unfamiliar concepts (reducers, views, subscriptions, node trees, scenes, `.tscn` files) briefly inline or with an analogy. Don't explain general programming ideas (loops, classes, dictionaries).
6. **Style**: dense causal prose — "X does Y *because* Z". One step = one cause-and-effect beat. Every mechanism claim should name the file/function that implements it.
7. Each session updates the **Status** column of the table in `01 Roadmap.md` before finishing.

## Doc lineup & session order

Docs are written **in session order** (numeric). Each session writes its system doc(s) **and** appends its numbered steps to `00 End-to-End Timeline Flowchart.md` in the same session, so the sync-embeds in the system doc resolve.

| Session | Writes                                                                      | Appends to 00 | Code to read |
| ------- | --------------------------------------------------------------------------- | ------------- | ------------ |
| 1       | `00 End-to-end Timeline Flowchart` scaffold (headers only), `01 Roadmap.md` |               |              |

Aspirational systems described in the Game Design Docs but absent from the code (guild territory PvP, base-building beyond `place_building`/`remove_building`, biomes as gameplay, most of enchantment breadth) stay **out** of these docs; where a doc touches such an edge it says so explicitly.

## System doc schema (docs 01–nn)

Every system doc has exactly these sections, in order:

1. **Assumed knowledge** — wikilinks to the prerequisite docs (e.g. every doc assumes `[[01 Architecture & Sync Model]]`; docs 03–09 assume `[[02 Entity & Component Framework]]`).
2. **The 30-second version** — a short paragraph: what this system is and how it works, no detail.
3. **System flowchart** — the doc's own numbered steps, rendered as sync-embed blocks transcluded from `00 End-to-End Timeline Flowchart.md` (see mechanical conventions). Numbering restarts at `01.` within each doc regardless of the anchor's own number in 00.
   - **Exception — doc 02** is atemporal (framework structure, not a runtime sequence). Its third section is instead a **composition map**: for each logical entity (local player, remote player, enemy, drop, bullet manager, game manager), the client scene root + its component children, and the server tables joined by its entity id + the archetype helper that bundles them.
1. **Main body** — the full documentation, covering everything in the doc's lineup row above. Where content duplicates a timeline step, sync-embed it from 00 instead of re-typing; free prose is for everything the timeline doesn't say (why, invariants, edge cases, formulas).
2. **Known gaps / stubs** — explicit list of TODOs, stubs, unwired UI, and known bugs in this system's code. If none, omit the section.
3. **Where to go next** — 1–3 sentences pointing to the next doc(s) to read.

## `01 Roadmap.md` (session 1)

Contains: what these docs are and who they're for; the conventions section (copy §"Mechanical conventions" below into it, adapted as observer-facing guidance); the reading-order/status table (doc #, title, status, one-line coverage summary — mirror the lineup table above, statuses all start blank and get filled in by later sessions); and the note about aspirational systems staying out. Status values: blank / `done`.

## `00 End-to-End Timeline Flowchart.md` conventions

Session 1 writes the scaffold with these exact headers (content gets appended by the sessions per the lineup table):



Step format within each section:

- Numbered prose steps starting from 1 within each section.
- Each step ends with a `^prefix-N` block anchor. Example prefixes: `boot`, `conn`, `lobby`, `join`, `move`, `equip`, `enemy`, `combat`, `camera`, `end`, `admin` — each prefix restarts its own count at 1 (e.g. `^boot-1`, `^boot-2`, `^conn-1`…).
- Steps cross-refer to other steps by anchor name ("the subscription opened in join-1"), never by bare number.
- Where a step's details belong to another doc, say so and link it ("the damage formula is [[07 Combat & Damage Math|07's]] concern").

## Mechanical conventions (exact syntax — follow precisely)

**Wikilinks to code** (block-link-plus line-search syntax):

```
[[filename##text on the target line|display text]]
```

- Bare filename, no path — the plugin searches the vault (e.g. `[[lifecycle.rs##pub fn init|init]]`, `[[GameManager.cs##public override void _Ready|_Ready]]`).
- Search text must be unique within that file — use the function/struct signature line.
- Every timeline step and every flowchart entry links to code wherever possible.

**Sync-embed blocks** (sync-embeds plugin) — for any content duplicated between docs, transclude from the single source (the timeline doc) instead of re-typing:

````
```sync
![[00 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:01.}]]
```
````

- `marker:NN.` — the step number *as it should render in the embedding doc*, zero-padded to two digits. System-doc flowcharts number from `01.` regardless of the source step's number in 00.

**Section references between docs** use Obsidian wikilinks: `[[03 Player System]]`, or with display text `[[07 Combat & Damage Math|07]]`.

## Per-session verification checklist (run before marking a doc done)

1. Every `^anchor` referenced by the session's sync-embeds exists in `00 End-to-End Timeline Flowchart.md`.
2. Spot-check every `[[file##text|…]]` code link: the search text appears (uniquely) in that code file.
3. Every class, table, reducer, view, and function named in the doc exists in the code today — grep for it. (The pre-refactor client had classes like `TerrainManager`, `CameraController2D`, `CameraRig`, `LocalPlayerCombat`, `LocalPlayerInventory` — these **no longer exist**; their successors are the `*Component` classes in `client/Scripts/Components/`. Never cite the old names except to say they were replaced.)
4. `01 Roadmap.md` status table updated for the doc(s) written this session.

## Background: what changed since the wiped docs (so you don't resurrect stale claims)

- **Client**: refactored to a fully compositional entity/component architecture. `GameManager` is now thin glue plus a static facade delegating to child components (`TableSubscriber`, `CatalogComponent`, `EntitySpawnerComponent`, `LobbyComponent`) **and to the `DatabaseConnector` autoload** — connection ownership moved out of any scene-tree component entirely (see below). Camera static singletons are gone — components are reached via `GameManager.GetComponent<T>()`. `TerrainManager.cs` was replaced by the `Components/Terrain/` stack (`TerrainComponent` + pooled `TileComponent`s + four layer components). Old camera/player-combat/inventory classes became `CameraRigComponent`, `Camera2DPresenterComponent`, `World3DComponent`, `CombatComponent`, `InventoryComponent`, `SlotComponent`, `ItemSidebarComponent`, `StatsSidebarComponent`.
- **A later refactor moved connection/subscription ownership into a new `client/addons/sstdbsdk/` addon** — not present when this plan was first drafted, and not covered by root `CLAUDE.md`'s "Folders to skip" note (which still describes `client/addons/` as third-party-only). It has three pieces: `DatabaseConnector.cs`, an **autoload singleton** (`project.godot`, `DatabaseConnector.Instance`) owning the `DbConnection` and per-frame `FrameTick()`; `TableSubscriber.cs`, a `Component` owning the subscription waves (`BaseTables`/`LobbyTables`/`GameTables`) and the `MapConfig` lap-vector mirror; and `TableBinderComponent.cs`, a reflection-based `Component` that binds one subscribed table and re-exposes its row events as `RowInserted`/`RowUpdated`/`RowDeleted` Godot signals — now the standard way almost every component consumes table rows, wired per-instance in `.tscn` files rather than hand-hooked in code. This folder is gitignored (own code, not vendored), so it's invisible to `git status`/`git ls-files` — read it from disk. Docs 01 and 02 need to cover this; several later sessions' file lists have been updated above to cite the specific `Local*Component` children this pattern produced on `LocalPlayer` (`LocalPositionSyncComponent`, `LocalPlayerDataComponent`, `LocalPlayerInventoryComponent`, `LocalPlayerProfileComponent`).
- **Server**: new `combat/` module owns the whole damage pipeline; `report_hit`/`report_enemy_hit` just resolve base damage and delegate. New `Enchantment` table + `apply_enchantment`/`remove_enchantment` reducers + enchantment UI in `ItemSidebarComponent`. `world/`'s old `methods.rs` split into focused files (`prng`/`noise`/`hex`/`wrap`/`aoi`) plus a composable `terrain/` generation pipeline. Archetype helpers (`try_scaffold_profile`/`teardown_profile`, `spawn_enemy_archetype`/`despawn_enemy_archetype`) bundle each entity's rows. `main/global.rs` centralizes tunable constants. Every `views.rs` is now implemented (no empty placeholders).
