# 01 Roadmap

## What these docs are

This folder is the narrative, learn-from-scratch documentation of the bullet-hell MMO: a Godot 4.7 mono C# client (`client/`) over a SpacetimeDB 2.2 Rust server module (`server/spacetimedb/`). It is the companion to the terse maintainer references — [[AGENTS.md|the root AGENTS.md]], [[client/AGENTS.md]], [[server/AGENTS.md]], and the module-level [[server/spacetimedb/src/enemy/AGENTS.md|enemy]] and [[server/spacetimedb/src/item/AGENTS.md|item]] files — which stay the authoritative jump-off maps; these docs explain the *why* and the end-to-end flow instead of restating them.

The docs are organized around **the server's tables**. Every table has exactly one story worth telling: **who writes it** (which reducer, scheduled tick, lifecycle hook, or seed inserts/updates/deletes rows) and **how the client reads it** (which view or raw-table subscription carries it over the wire, and which binder component consumes the row events). [[00 Table Map]] is the per-table reference spine — one entry per table, written in full — and each system doc (04–13) covers a group of tables, transcluding its table entries from 00 rather than re-typing them.

**Who they're for:** an intermediate programmer who is brand new to Godot, C#, Rust, and SpacetimeDB. Unfamiliar concepts (reducers, views, subscriptions, node trees, scenes, `.tscn` files) are explained briefly inline as they come up; general programming ideas are not. The code is the only source of truth for everything written here — every mechanism claim names the file/function that implements it, and where the AGENTS.md files and the code disagree, the code won.

## Reading order & status

Read [[02 The Component Framework]] first (the client-side architecture every later doc assumes), then [[03 Connection, Subscriptions & Views]] (the read path every table shares — prerequisite for all table docs). Docs 04–13 are the per-system table docs and can be read in any order after 03; [[00 Table Map]] is a reference, not a chapter. Status values: blank = not yet written/verified, `done` = verified against the code in the final pass.

| Doc | Title | Status | Covers |
| --- | ----- | ------ | ------ |
| 00 | [[00 Table Map]] | done | Every server table: writers (reducer/tick/hook/seed per operation) and the client read path (view, subscription wave, binder component) — the reference spine |
| 01 | [[01 Roadmap]] | done | This file — scope, audience, conventions, reading order |
| 02 | [[02 The Component Framework]] | done | The client's entity/component model (component bases, registry, binder pattern) and its server-side mirror: one table per concern, ids joining an entity's rows |
| 03 | [[03 Connection, Subscriptions & Views]] | done | DatabaseConnector, the base/lobby/game subscription waves, the view layer (anonymous global / per-caller / AOI-filtered), TableBinderComponent, the event-table pattern |
| 04 | [[04 Lobby & Identity Tables]] | done | LoggedInPlayer/LoggedOutPlayer/PlayerProfile — connect/disconnect hooks, lobby UI, join/leave flow |
| 05 | [[05 Player Data & Progression Tables]] | done | PlayerData/PlayerStats/PlayerStatAllocation — XP/level math, `recompute_stats`, stat allocation |
| 06 | [[06 Movement & AOI Tables]] | done | PlayerPosition/PlayerRotation/PlayerChunk + the AOI views — movement reporting, displacement clamp, interpolation |
| 07 | [[07 Terrain & World Tables]] | done | MapConfig, textures, terrain rules, world/biome defs, generated tiles/decor — the terrain pipeline and its client rendering |
| 08 | [[08 Enemy Tables]] | done | EnemyTemplate + step defs, the behavior-tree instance tables, the 100 ms tick and spawn schedule |
| 09 | [[09 Combat & Event Tables]] | done | The damage pipeline, the three event tables (bullet patterns, bullet control, spell visuals), hit rate-limiting and the anti-cheat witness flag |
| 10 | [[10 Items, Inventory & Loot Tables]] | done | The Item/Enchantment catalog, PlayerInventorySlot (slots, spans, runtime state), consumables, loot drops |
| 11 | [[11 Status, Zone & Charge Tables]] | done | ActiveStatusEffect/ActiveEnemyStatusEffect/GroundZone/AbilityCharge and the single 1 Hz status tick |
| 12 | [[12 Chest Tables]] | done | Chest/ChestItem/ChestTakeCooldown — chest views, the take flow, chest UI |
| 13 | [[13 Admin & Debug Tables]] | done | Admin reducers (the CLI-only writers of seed/catalog tables), seeds, PlayerPositionDebug + its schedule |

## Visual overview

The generated flowchart map of the whole codebase is at [[flowcharts/main.canvas]] (each system doc links its own `main-<slug>.canvas` plus deep-dive subflowcharts). This link is expected to be unresolved until the flowchart composition runs — it is generated, not hand-written.

## Conventions

- **Wikilinks to code** use the form `[[path/from/repo/root#function name#function index|display text]]` — e.g. `[[server/spacetimedb/src/main/seeds.rs#seed#4|seeds]]`. The function index disambiguates same-name functions in one file (the seven `seed` impls in `main/seeds.rs`); it is omitted when the name is unique. Indices are best-effort and get spot-checked in the final pass.
- **Wikilinks to canvas flowcharts** are ordinary wikilinks including the folder path, never deep-links to a node.
- **Sync-embed blocks** (the sync-embeds plugin) transclude shared content from its single source instead of re-typing it — system-doc table sections embed entries from [[00 Table Map]] by block anchor:

````
```sync
![[00 Table Map#^table-player-position{seamless:true,title:false,marker:01.}]]
```
````

  `marker:NN.` is the entry number *as it should render in the embedding doc*, zero-padded to two digits — system docs number their table sections from `01.` regardless of the entry's position in 00.
- **Block anchors**: every table entry in 00 ends with a `^table-<kebab-case-name>` anchor; docs cross-refer to tables by anchor (`^table-player-chunk`), never by bare table name.
- **Section references between docs** use ordinary Obsidian wikilinks, with display text where helpful.
- The Obsidian vault is the **repo root**, not `docs/` — that's the only way links to `client/`, `server/`, and `flowcharts/` resolve. The docs assume the `sync-embeds`, `block-link-plus`, `advanced-canvas`, and `vscode-editor` community plugins are enabled.

## What stays out

These docs describe **what the code does now**, not what it's planned to do. Aspirational systems described in the Game Design Docs but absent from the code — guild territory PvP, base-building beyond `place_building`/`remove_building`, biomes as gameplay, most of the enchantment breadth, the un-built `BulletDespawnEvent`/`slash_bullet` bullet-despawn design — stay **out**; where a doc touches such an edge it says so explicitly. (There is no `control_bullets` reducer either — the implemented bullet-control protocol is the `BulletControlEvent` event table.) TODOs, stubs, unwired UI, and known bugs in the code are documented per-system in each doc's "Known gaps" section, not silently omitted.
