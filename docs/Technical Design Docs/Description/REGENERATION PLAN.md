## What this project is

A **bullet-hell MMO roguelike**: Godot 4.6 mono C# client (`client/`) + SpacetimeDB Rust server module (`server/spacetimedb/src/`). Repo root is the parent of `docs/`'s parent — all paths below are relative to repo root. The client is **fully compositional** (entity/component architecture modeled on `code_examples/comedot`); the server mirrors that compositionally: one table per concern = a component, ids like `profile_id`/`enemy_id` join a logical entity's rows across tables, and "archetype helpers" bundle the rows of one entity.

The docs you're writing live in `docs/Technical Design Docs/Description/` and are the narrative, learn-from-scratch companion to the terse maintainer references (`CLAUDE.md` at repo root, `client/CLAUDE.md`, `server/CLAUDE.md`). (Confirmed via `git status`: the wiped files were at this exact path — `Description`, not `Description old` — with filenames matching the doc lineup table below.) The docs folder is an Obsidian vault with the `sync-embeds` and `block-link-plus` community plugins installed — the conventions below depend on both.

## Hard rules for every session

1. **The code is the only source of truth.** `CLAUDE.md` files are accurate jump-off points (use their module maps to find files fast), but verify every claim against the actual code before writing it. Where they disagree, code wins.
2. **Never read `client/Scripts/module_bindings/`** — generated SpacetimeDB bindings.
3. **Document what the code does, not what it's planned to do.** Flag TODOs/stubs/known bugs explicitly in the doc's "Known gaps" section and inline where relevant (e.g. "`LobbyGui` is scene navigation only — not yet wired to the `create_profile`/`join_world` reducers").
4. **Don't restate the CLAUDE.md files.** Link to them; these docs are the narrative complement.
5. **Audience**: intermediate programmer, brand new to Godot, C#, Rust, and SpacetimeDB. Explain unfamiliar concepts (reducers, views, subscriptions, node trees, scenes, `.tscn` files) briefly inline or with an analogy. Don't explain general programming ideas (loops, classes, dictionaries).
6. **Style**: dense causal prose — "X does Y *because* Z". One step = one cause-and-effect beat. Every mechanism claim should name the file/function that implements it.
7. Each session updates the **Status** column of the table in `00 Roadmap.md` before finishing.

## Doc lineup & session order

Docs are written **in session order** (numeric). Each session writes its system doc(s) **and** appends its numbered steps to `99 End-to-End Timeline Flowchart.md` in the same session, so the sync-embeds in the system doc resolve.

| Session | Writes | Appends to 99 | Code to read |
|---|---|---|---|
| 1 | `00 Roadmap.md`, `99` scaffold (headers only), `01 Architecture & Sync Model.md` | §01 Server bootstrap (`boot-N`), §02 A client connects (`conn-N`), §06 Session end (`end-N`) | `server/spacetimedb/src/lib.rs`, `main/lifecycle.rs`, `main/global.rs`; `client/Scripts/Game/GameManager.cs` (thin entity glue + static facade), `Components/Connection/ConnectionComponent.cs`, `Components/Subscription/SubscriptionComponent.cs`, `Components/Catalog/CatalogComponent.cs`; bootstrap commands (`spacetime start`/`publish`/`generate`, F5) from root and `server` CLAUDE.md |
| 2 | `02 Entity & Component Framework.md` | none — atemporal doc (see schema exception) | `client/Scripts/Components/IComponent.cs`, `ComponentRegistration.cs`, `Component.cs`, `AreaComponent.cs`, `Node2DComponent.cs`, `Node3DComponent.cs`, `ControlComponent.cs`, `VisualComponent.cs`, `NodeExtensions.cs`; `Entities/IEntity.cs`, `Entity.cs`, `EntityRegistry.cs`; server-side composition mapping + archetype helpers `try_scaffold_profile`/`teardown_profile` (`server/spacetimedb/src/player/methods.rs`), `spawn_enemy_archetype`/`despawn_enemy_archetype` (`enemy/methods.rs`) |
| 3 | `03 World & Hex Grid.md` | world-gen cross-links from `boot-2`; AOI steps shared with doc 04 inside §05 (`move-N`) | `server/spacetimedb/src/world/`: `hex.rs`, `wrap.rs`, `aoi.rs`, `prng.rs`, `noise.rs`, `terrain/` pipeline (`mod.rs` orchestrator, `rules.rs`, `voronoi.rs`, `ground.rs`, `overlay.rs`, `decor.rs`), `tables.rs`, `def_tables.rs`, `instance_tables.rs`, `views.rs`, `reducers.rs` (`place_building`/`remove_building`); client `World/TorusMath.cs`, `Components/Terrain/` (`TerrainComponent`, `TileComponent`, `TerrainLayerComponent`, `GroundComponent`, `OverlayComponent`, `DecorComponent`, `DecorShadowComponent`, `DecorLayerComponent`), `Components/Camera/HexGridOverlayComponent.cs`, `HexGridOverlay3DComponent.cs` |
| 4 | `04 Player System.md` | §03 Lobby and profile creation (`lobby-N`), §04 Joining the world (`join-N`), movement/remote-player steps of §05 (`move-N`) | `server/spacetimedb/src/player/` (all of it: login-state tables, profiles, `PlayerData`/`PlayerStats`, XP/level math, `report_movement`, views incl. shared `nearby_indices` AOI helpers); client `Players/Local/LocalPlayer.cs`, `Players/Remote/RemotePlayer.cs`, `Components/Movement/InterpolationComponent.cs`, `Components/Visual/RemoteVisualComponent.cs`, `Components/Lobby/LobbyComponent.cs`, `Game/LobbyGui.cs` (flag: not yet wired to reducers), `Components/Data/StatsComponent.cs` + `Resources/Stats/` (server-row mirroring) |
| 5 | `05 Item, Equipment & Enchantment System.md` | §05 equip/inventory steps (`equip-N`), including enchantment socketing | `server/spacetimedb/src/item/` (unified `Item` table, `Enchantment` table, `EquipSlot`, `StatModifier`, `ItemBehavior`, seeds, views `all_items`/`all_enchantments`, admin catalog reducers) + inventory reducers in `player/reducers.rs` (`swap_slots`, `drop_item`, `pickup_drop`, `use_item`, `apply_enchantment`, `remove_enchantment`) and `recompute_stats`/`apply_consumable_effect` in `player/methods.rs`; client `Components/Inventory/` (`InventoryComponent`, `SlotComponent`, `ItemSidebarComponent`, `StatsSidebarComponent`), `Components/Catalog/CatalogComponent.cs`, `Items/Drop.cs`, `Components/Interaction/PickupComponent.cs`, `Components/Visual/DropVisualComponent.cs` |
| 6 | `06 Enemy AI & Bullet Patterns.md` | §05 enemy steps (`enemy-N`) | `server/spacetimedb/src/enemy/` (def tables: `EnemyTemplate`, phases/attack sequences/step defs; instance tables: `Enemy`, `EnemyBehavior`, `EnemyPhase`, `EnemyAttack`, `EnemySequenceStep`, `RepeatStepInstance`, `BulletPatternEvent`, schedules; scheduled ticks; behavior-tree build, phase/sequence ticking, movement, aggro, biome-region spawning; views); client `Players/Enemies/Enemy.cs`, `Game/BulletManager.cs`, `Components/Bullets/BulletSpawnerComponent.cs`, `Game/BulletData.cs`, BlastBullets2D bridge (`client/addons/blastbullets2d` — interface level only, don't document plugin internals) |
| 7 | `07 Combat & Damage Math.md` | §05 combat steps (`combat-N`) | `server/spacetimedb/src/combat/mod.rs` (the only place damage is computed/applied: `compute_player_damage`, `compute_incoming_damage`, `deal_damage_to_player`, `deal_damage_to_enemy`, `heal_player`) + entry-point reducers `report_hit` (`player/reducers.rs`) / `report_enemy_hit` (`enemy/reducers.rs`); client `Components/Combat/` (`HealthComponent`, `FactionComponent`, `DamageComponent`, `DamageReceivingComponent`, `HitZone.cs`), `Components/Weapon/CombatComponent.cs`, `Components/Bullets/BulletHitRouterComponent.cs` |
| 8 | `08 Client Rendering & Camera.md` | §05 camera steps (`camera-N`) | `client/Scripts/Components/Camera/` (`CameraRigComponent`, `Camera2DPresenterComponent`, `World3DComponent`), `Players/CharacterModel3D.cs`, `Scenes/world_3d.tscn`, phantom_camera addon usage, the 3D `SubViewport` setup in `Scenes/main.tscn` |
| 9 | `09 Admin, Debug & World Lifecycle.md` | §07 Meanwhile, independently (`admin-N`) | `server/spacetimedb/src/main/admin.rs` (single global admin slot; **known pre-existing bugs to flag**: `spawn_enemy` double-inserts the behavior row; `despawn_enemy` leaks the behavior tree), `main/debug.rs`, `main/seeds.rs`, `main/lifecycle.rs` (`init` seeding + world gen); client `Game/DebugOverlay.cs` |
| 10 | `99 End-to-End Flowchart.canvas` + final verification pass | — | Build canvas nodes that reference `99`'s `^anchor` block ids only (no prose duplication — the canvas adds spatial arrangement, nothing else). Then run the full verification checklist below across all docs. |

Aspirational systems described in the Game Design Docs but absent from the code (guild territory PvP, base-building beyond `place_building`/`remove_building`, biomes as gameplay, most of enchantment breadth) stay **out** of these docs; where a doc touches such an edge it says so explicitly.

## System doc schema (docs 01–09)

Every system doc has exactly these sections, in order:

1. **Assumed knowledge** — wikilinks to the prerequisite docs (e.g. every doc assumes `[[01 Architecture & Sync Model]]`; docs 03–09 assume `[[02 Entity & Component Framework]]`).
2. **The 30-second version** — a short paragraph: what this system is and how it works, no detail.
3. **System flowchart** — the doc's own numbered steps, rendered as sync-embed blocks transcluded from `99 End-to-End Timeline Flowchart.md` (see mechanical conventions). Numbering restarts at `01.` within each doc regardless of the anchor's own number in 99.
   - **Exception — doc 02** is atemporal (framework structure, not a runtime sequence). Its third section is instead a **composition map**: for each logical entity (local player, remote player, enemy, drop, bullet manager, game manager), the client scene root + its component children, and the server tables joined by its entity id + the archetype helper that bundles them.
4. **Main body** — the full documentation, covering everything in the doc's lineup row above. Where content duplicates a timeline step, sync-embed it from 99 instead of re-typing; free prose is for everything the timeline doesn't say (why, invariants, edge cases, formulas).
5. **Known gaps / stubs** — explicit list of TODOs, stubs, unwired UI, and known bugs in this system's code. If none, omit the section.
6. **Where to go next** — 1–3 sentences pointing to the next doc(s) to read.

## `00 Roadmap.md` (session 1)

Contains: what these docs are and who they're for; the conventions section (copy §"Mechanical conventions" below into it, adapted as observer-facing guidance); the reading-order/status table (doc #, title, status, one-line coverage summary — mirror the lineup table above, statuses all start blank and get filled in by later sessions); and the note about aspirational systems staying out. Status values: blank / `done`.

## `99 End-to-End Timeline Flowchart.md` conventions

Session 1 writes the scaffold with these exact headers (content gets appended by the sessions per the lineup table):

- `## 01 Server bootstrap`
- `## 02 A client connects`
- `## 03 Lobby and profile creation`
- `## 04 Joining the world`
- `## 05 The parallel in-world loop` — with a short intro ("everything from here to Session end is independent systems running at once, not a sequence") and these `###` subsections: `Movement and AOI`, `Equip and inventory`, `Enemy spawning and behavior`, `Combat, both directions`, `Camera and rendering, spectating all of it`
- `## 06 Session end`
- `## 07 Meanwhile, independently` — intro noting this is admin/out-of-band activity, not the ordinary player flow
- `## 08 Reading this alongside the canvas` — relationship to the `.canvas` sibling (built last; canvas nodes reference anchors, no prose duplication)

Step format within each section:

- Numbered prose steps starting from 1 within each section.
- Each step ends with a `^prefix-N` block anchor. Prefixes: `boot`, `conn`, `lobby`, `join`, `move`, `equip`, `enemy`, `combat`, `camera`, `end`, `admin` — each prefix restarts its own count at 1 (e.g. `^boot-1`, `^boot-2`, `^conn-1`…).
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
![[99 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:01.}]]
```
````

- `marker:NN.` — the step number *as it should render in the embedding doc*, zero-padded to two digits. System-doc flowcharts number from `01.` regardless of the source step's number in 99.

**Section references between docs** use Obsidian wikilinks: `[[03 Player System]]`, or with display text `[[07 Combat & Damage Math|07]]`.

## Per-session verification checklist (run before marking a doc done)

1. Every `^anchor` referenced by the session's sync-embeds exists in `99 End-to-End Timeline Flowchart.md`.
2. Spot-check every `[[file##text|…]]` code link: the search text appears (uniquely) in that code file.
3. Every class, table, reducer, view, and function named in the doc exists in the code today — grep for it. (The pre-refactor client had classes like `TerrainManager`, `CameraController2D`, `CameraRig`, `LocalPlayerCombat`, `LocalPlayerInventory` — these **no longer exist**; their successors are the `*Component` classes in `client/Scripts/Components/`. Never cite the old names except to say they were replaced.)
4. `00 Roadmap.md` status table updated for the doc(s) written this session.

## Background: what changed since the wiped docs (so you don't resurrect stale claims)

- **Client**: refactored to a fully compositional entity/component architecture. `GameManager` is now thin glue plus a static facade delegating to child components (`ConnectionComponent`, `SubscriptionComponent`, `CatalogComponent`, `EntitySpawnerComponent`, `LobbyComponent`). Camera static singletons are gone — components are reached via `GameManager.GetComponent<T>()`. `TerrainManager.cs` was replaced by the `Components/Terrain/` stack (`TerrainComponent` + pooled `TileComponent`s + four layer components). Old camera/player-combat/inventory classes became `CameraRigComponent`, `Camera2DPresenterComponent`, `World3DComponent`, `CombatComponent`, `InventoryComponent`, `SlotComponent`, `ItemSidebarComponent`, `StatsSidebarComponent`.
- **Server**: new `combat/` module owns the whole damage pipeline; `report_hit`/`report_enemy_hit` just resolve base damage and delegate. New `Enchantment` table + `apply_enchantment`/`remove_enchantment` reducers + enchantment UI in `ItemSidebarComponent`. `world/`'s old `methods.rs` split into focused files (`prng`/`noise`/`hex`/`wrap`/`aoi`) plus a composable `terrain/` generation pipeline. Archetype helpers (`try_scaffold_profile`/`teardown_profile`, `spawn_enemy_archetype`/`despawn_enemy_archetype`) bundle each entity's rows. `main/global.rs` centralizes tunable constants. Every `views.rs` is now implemented (no empty placeholders).
