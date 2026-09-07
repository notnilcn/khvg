# 01 Roadmap — what these docs are, and in what order to read them

These docs are the narrative, learn-from-scratch companion to the terse maintainer refs ([[AGENTS.md|root AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]]). They are written for an **intermediate programmer brand new to Godot, C#, Rust, and SpacetimeDB**: unfamiliar concepts (reducers, views, subscriptions, node trees, scenes, `.tscn` files) are explained briefly inline; general programming ideas are not.

Every system doc is organized around **how its tables are viewed and changed** — not around features or code layout in the abstract. Each table has exactly one story: **how it is changed** (which reducer, scheduled tick, lifecycle hook, or seed inserts/updates/deletes rows) and **how it is viewed** (which view or raw-table subscription carries it over the wire and which binder component consumes the row events on the client, plus any server-side reads that matter). If a mechanism doesn't change a table or explain how a table is viewed, it lives as a sentence inside the Changers/Readers prose of the table(s) it touches — never as its own section. `00 Table Map.md` is the per-table spine they all transclude from; docs `02`–`12` never re-type a table entry.

Aspirational systems from the Game Design Docs that have no code behind them stay **out** of these docs: guild territory PvP, base-building beyond `place_building`/`remove_building`, biomes as gameplay, most of enchantment breadth, the un-built `BulletDespawnEvent`/`slash_bullet` bullet-despawn design. Where a doc touches such an edge it says so explicitly. (There is no `control_bullets` reducer — the implemented bullet-control protocol is the `BulletControlEvent` event table.)

## Conventions (how to read these docs)

**Wikilinks to code** name the file and the symbol, so every mechanism claim points at its implementation:

```
[[filename#function name|display text]]
```

- Example: `[[server/spacetimedb/src/main/seeds.rs#seed_default_enemies|seed_default_enemies]]`.
- The vault root is the repo root (not `docs/`), so these links resolve to `client/`, `server/`, and `flowcharts/` alike.

**Wikilinks to canvas flowcharts** are ordinary wikilinks including the folder path, e.g. `[[flowcharts/main-combat.canvas]]` — linked by path, never by node id.

**Sync-embed blocks** (sync-embeds plugin) transclude table entries from the single source instead of re-typing them:

````
```sync
![[00 Table Map#^table-player-position{seamless:true,title:false,marker:01.}]]
```
````

- `marker:NN.` is the entry number *as it renders in the embedding doc*, zero-padded to two digits. System-doc table sections number their entries from `01.` regardless of the entry's position in 00.

**Section references between docs** are ordinary Obsidian wikilinks, with display text where it reads better.

**Table anchors** (`^table-<kebab-case-table-name>`) live in `00 Table Map.md` and nowhere else. Entries cross-refer by anchor name, never by table name alone.

## Reading order and status

Read top-down: machinery first (the framework, then the read path every table shares), then one module doc per folder in `server/spacetimedb/src/` (alphabetical directory-listing order — the phase-0 order).

| # | Title | Status | Coverage |
|---|-------|--------|----------|
| 00 | Table Map | done | Every table in the module: changers, client readers to their visible end, server readers that matter |
| 01 | Roadmap (this doc) | done | What the docs are, conventions, reading order, flowchart overview link |
| 02 | The Component Framework | done | Component/entity base classes; the server-side mirror (one table per concern) via the profile/enemy archetype helpers |
| 03 | Connection, Subscriptions & Views | done | Connector, subscriber waves, binder, the three view flavors; `main_menu.tscn` + `game.tscn` inline subscriber; the event-table subscription pattern |
| 04 | Anticheat Tables | done | `PlayerHitFlag`: witness corroboration for damage claims and wager settlements |
| 05 | Chest Tables | done | `Chest`, `ChestItem`, server-only `ChestTakeCooldown`; AOI chest views; the chest panel |
| 06 | Combat Tables | done | Owns no tables — the shared damage pipeline (calc + sinks + policy types) as a cross-table flow |
| 07 | Enemy Tables | done | Template defs, behavior-tree instances, event tables (`BulletPatternEvent`, `BulletControlEvent`, `AbilityVisualEvent`), `BulletFireAttestation`, schedules |
| 08 | Item Tables | done | Unified `Item` catalog + `Enchantment`: slot/behavior independence, toggle options, seeds |
| 09 | Main Tables | done | `PlayerPositionDebug` + schedule; lifecycle/admin/seeds as changers of other modules' tables |
| 10 | Player Tables | done | Login rows, profiles, data/stats/allocation, position/rotation/chunk, inventory slots, consumable effects, loot drops, server-only validation tables |
| 11 | Status Tables | done | `ActiveStatusEffect`, `ActiveEnemyStatusEffect`, `GroundZone`, `AbilityCharge`: intercepts, wagers, zones, charges on the single 1 Hz tick |
| 12 | World Tables | done | `MapConfig`, `BuildingTile` elevation, texture/animation catalogs, generation rules/defs, generated tiles/decor |

Status values: blank / `done`. Docs 00–12 were written in the first pass; the phase-2 pass still owns the shared verification (anchor sweep, pointer freshness, final flowchart compose) before this table is final.

## Visual overview

The aggregate flowchart for the whole system is [[flowcharts/main.canvas]] — the `"main"` flow composed from the root/client/server subfolder canvases. Per-system canvases (`flowcharts/main-<doc-slug>.canvas` plus 1–3 deep-dive subflowcharts) are linked from each doc's **Flowcharts** section.

> Note on current state: `flowcharts/flows.json` is pre-seeded with the previous flow set (`framework`, `subscriptions`, plus the older per-system flows `lobby`, `player-data`, `movement`, `terrain`, `enemies`, `combat`, `items`, `status`, `chests`, `admin`). The phase-2 pass curates it to the lineup above (one flow per `04+` module doc, slug = module folder name: `anticheat`, `chest`, `combat`, `enemy`, `item`, `main`, `player`, `status`, `world`) and re-runs the full "Regenerate all flowcharts" compose, which is when every `main*.canvas` link above is guaranteed fresh and `missingCanvases` is verified empty.
