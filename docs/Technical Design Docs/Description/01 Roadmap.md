# 01 Roadmap

## What these docs are, and who they are for

The Description docs are the narrative, learn-from-scratch companion to the terse maintainer references ([[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]], plus the module-level [[server/spacetimedb/src/enemy/AGENTS.md|enemy]] and [[server/spacetimedb/src/item/AGENTS.md|item]] refs). They are written for an intermediate programmer brand new to Godot, C#, Rust, and SpacetimeDB — comfortable with code, unfamiliar with this stack.

They are centered around **how the tables are viewed and changed** — not features, flows, or code organization in the abstract. Every table in `server/spacetimedb/src/` has exactly one story worth telling: **how it is changed** (which reducer, scheduled tick, lifecycle hook, or seed inserts/updates/deletes rows) and **how it is viewed** (which view or raw-table subscription carries it over the wire and which binder component consumes the row events on the client, plus any server-side reads that matter). [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] is the per-table spine they all transclude from. If a mechanism doesn't change a table or explain how a table is viewed, it doesn't get its own section — it belongs as a sentence inside the Changers/Readers prose of the table(s) it touches.

## Conventions

How to read (and write) these docs:

- **Wikilinks to code** look like `[[filename#function name#function index|display text]]` — for example, `[[server/spacetimedb/src/main/seeds.rs#seed#4|seeds]]`. Every table entry and every mechanism claim links to the code that implements it. For table definitions the bare `#TableName` anchor with no index is the accepted form, e.g. `[[server/spacetimedb/src/player/tables.rs#PlayerPosition|definition]]`.
- **Wikilinks to canvas flowcharts** are ordinary wikilinks that include the folder path, e.g. `[[flowcharts/main-combat.canvas]]`. Canvases are linked by **file path only** — never deep-link to a node inside a canvas.
- **Sync-embed blocks** (sync-embeds plugin) transclude duplicated content from its single source instead of re-typing it:

````
```sync
![[00 Table Map#^table-player-position{seamless:true,title:false,marker:01.}]]
```
````

`marker:NN.` is the entry number *as it should render in the embedding doc*, zero-padded to two digits. System-doc table sections number their entries from `01.` regardless of the entry's position in 00.

- **Section references between docs** use Obsidian wikilinks, with display text where it reads better.
- **Table anchors** are `^table-<kebab-slug>` (e.g. `^table-bullet-pattern-event`), defined once in 00 and referenced by anchor name everywhere else — never by table name alone when an anchor exists.

## Reading order and status

Read 00–03 first, then any module doc in any order. Status values: blank / `done` (filled in by the phase-2 pass).

| # | Title | Status | Coverage |
|---|-------|--------|----------|
| 00 | Table Map | done | Per-table spine: every table's Changers / Client readers / Server readers plus `^table-*` anchors |
| 01 | Roadmap (this doc) | done | What/whom, conventions, reading order, flowchart overview link |
| 02 | The Component Framework | done | Component/entity base classes; the server-side mirror (one table per concern) via the profile/enemy archetype helpers |
| 03 | Connection, Subscriptions & Views | done | Connector, subscriber waves, binder, the three view flavors; `main_menu.tscn` + `game.tscn` inline subscriber; the event-table subscription pattern |
| 04 | Anticheat | done | `PlayerHitFlag` witness flags and `MoveValidationStrikes` strike windows — how reports are corroborated and clamped |
| 05 | Chat | done | Channels, membership mirror, messages, reports, rate windows, retention sweep |
| 06 | Chest | done | Chest containers, regenerating vs consumable items, per-player take cooldowns |
| 07 | Claims | done | Territories, permission bits, upkeep/dissolve, interact/build consults |
| 08 | Combat | done | The shared damage pipeline: pure math vs sinks, mitigation policies, death ownership split |
| 09 | Enemy | done | Template/step defs, behavior-tree runtime rows, the 100 ms sim, event tables and attestations |
| 10 | Item | done | Unified catalog, enchantments, staging, knowledge gates |
| 11 | Main | done | Lifecycle, admin reducers, seeds, agent switch, position-debug tooling |
| 12 | Player | done | Identity/lobby rows, profiles, stats/allocation, position/rotation/chunk, inventory slots, drops, validation state, knowledge, survival |
| 13 | Status | done | Status effects, enemy debuffs, stagger, ground zones, ability charges, channeled actions |
| 14 | Trade | done | Two-party sessions, offer pockets, atomic swap, timeout/combat/range sweep |
| 15 | World | done | Map config, building tiles, texture/animation catalogs, layering/adjacency/decor rules, world/biome defs, generated hexes/decor, buildings/sites |

## Visual overview

[[flowcharts/main.canvas]] is the global aggregate flowchart of the whole codebase. **This link is expected to stay unresolved until the phase-0 flowchart recompose runs** — do not "fix" it by hand-creating a canvas. Each system doc links its own `flowcharts/main-<doc-slug>.canvas` plus 1–3 deep-dive subflowcharts; the one naming exception is doc 11, whose flow is `main-module` (see Phase-0 notes) and whose canvas is `flowcharts/main-main-module.canvas`.

## Aspirational systems stay out

Systems described in the Game Design Docs but absent from the code — guild territory PvP, base-building beyond site placement/demolition, biomes as gameplay, most of enchantment breadth, the un-built bullet-despawn designs — stay **out** of these docs. Where a doc touches such an edge it says so explicitly. (There is no `control_bullets` reducer — the implemented bullet-control protocol is the `BulletControlEvent` event table.)

## Phase-0 notes

Recorded at spine time; the phase-2 pass resolves or re-runs them.

(a) **Flow-name collision — `main` vs `main/`.** The global aggregate flow and the Main-module doc both wanted the name `main`. The module doc's flow is named **`main-module`** instead (its canvas composes to `flowcharts/main-main-module.canvas`); the global aggregate keeps `main`. Doc 11 and its Flowcharts section must use the `main-module` name.

(b) **chat/claims/trade subflowcharts verified on disk (phase-2 check 2026-09-08).** At seed time `flowcharts/Subflowcharts/` held aggregates for anticheat/chest/combat/enemy/item/main/player/status/world but none for the three new modules, so their flows were first seeded with the `src_subfolder.canvas` placeholder. Before phase 0 closed, a running Godot instance's regeneration created the dedicated `chat_subfolder`/`claims_subfolder`/`trade_subfolder` aggregates, and the three flows were repointed at them (plus the nearest client canvas each) — the phase-2 pass verified all three aggregate canvases exist on disk. What remains is only the Obsidian "Regenerate all flowcharts" compose (with a `missingCanvases`-empty check), which should also regenerate the retired topic canvases away: the old topic flows (lobby, player-data, movement, terrain, enemies, items, status, chests, admin) were merged into the module flows, so their stale `flowcharts/main-<topic>.canvas` files should be deleted by hand if the compose leaves them behind (a stale `main-<name>.canvas` is never cleaned up automatically).

(c) **Pointer files verified present, plugin re-run pending Obsidian.** All 24 server pointer files (`anticheat`/`chat`/`chest`/`claims`/`combat`/`enemy`/`item`/`main`/`player`/`status`/`trade`/`world` × `pointers.md`, plus `pointers-symbols.md` in some) and all 11 client pointer files (under `client/Scripts/` ×7, `client/sstdbsdk/` ×1, `client/Scenes/` ×3) were listed and used as entry points, but the "Generate pointers in subfolders" freshness check (re-run produces no `git diff`) needs Obsidian and is deferred to phase 2. Two further observations for that pass: the generated C# bindings still contain a `TriangleTile` table with no server-side definition (stale binding — server grep finds no such table), and `flowcharts/` was mid-regeneration by a running Godot instance while this doc was written, so canvas membership should be re-verified against the settled tree.

## Phase-2 notes

Recorded by the phase-2 pass (2026-09-08). No Obsidian runs in this environment — everything below that needs a plugin command is recorded as pending, not claimed done.

**Proposals applied to `00 Table Map.md`** (each re-verified against code before editing; proposal notes stripped from docs 04/05/07/09/10/13/15, Obsidian-only notes left in place):
- (a) `^table-player-hit-flag` Server readers now names the `NoHits` arm of `settle_store_mark` (`status/methods.rs`), not `intercept_incoming` (which never touches the table).
- (b) `^table-move-validation-strikes` Server readers now names both call sites: `report_movement` consumes `record_move_strike`'s returned count vs `STRIKES_BEFORE_REJECT`; read-only `live_strike_count` serves `sweep_strikes`.
- (c) `^table-chat-rate`: "1-second window" → "5-second window" (`CHAT_RATE_WINDOW_SECS` = 5.0, `CHAT_RATE_MAX_POSTS` = 5).
- (d) `^table-territory` Server readers now lists `pickup_drop` alongside `take_from_chest` (shared `validate_interact` → `check_interact` gate).
- (e) `^table-enemy-spawn-schedule` Changers now admin-arm-only (`toggle_enemy_spawning(true)`; `init` starts disarmed).
- (f) `^table-enemy-phase` Changers no longer claims "movement-def swaps" (`movement_def_id` fixed at build; only wait/loop-timer/cycle-anchor fields update).
- (g) `^table-staged-item` / `^table-staged-enchantment`: imports do not delete staged rows; only `clear_staged_*` deletes.
- (h) `^table-channeled-action`: `continue_channeled_action` is keep-alive (no progress write, expiry delete only; progress advances in the `tick_channeled` sweep); `complete_channeled` dispatches to `heal_player` only.
- (i) `^table-hex-tile` carries the one-line `TriangleTile` binding-staleness note (see below).
- (j) `^table-map-config` no longer cites the dead `MapConfig::seed` changer (zero callers; only `internal_add_chunks` writes).

**Verification (disk-checkable part):**
- Anchors: 78 distinct `^table-*` anchors referenced across docs 00–15, all 78 present in 00 — 0 missing.
- `flows.json`: all 15 flows present with at least one member each (anticheat 2, chat 2, chest 7, claims 2, combat 12, enemy 6, framework 6, item 12, main 3, main-module 5, player 22, status 6, subscriptions 10, trade 2, world 8 — 105 members total), every member verified to exist on disk — 0 dangling, so `flows.json` was left untouched. No composed `main*.canvas` exists yet (all pending the compose; do not hand-create).
- Pointer freshness (compared against directory listings, `module_bindings/` excluded; `pointers.md` never hand-edited): `server/spacetimedb/src` 60/61 `.rs` files indexed (only `src/lib.rs` — 12 bare `mod` lines — unindexed); `client/Scripts` 82/82 `.cs` covered; `client/sstdbsdk` 4/4 covered; `client/Scenes` 12/12 subfolder scenes covered while the 9 root-level composition scenes (`game`, `main_menu`, `local_player`, `non_local_player`, `default_enemy`, `chest`, `drop`, `profile_panel`, `world_3d`) have no root `pointers.md` by generator design and are cited directly as stable entry points. The doc-04 pointer-regen note (anticheat cross-refs) is still open and needs the Obsidian re-run to confirm.
- Status table above: all docs 00–15 → `done`.

**`TriangleTile` binding staleness (code work still pending, not docs):** the server defines only `HexTile`, but the generated C# bindings still expose `TriangleTile`, and `TerrainComponent` / `WaterComponent3D` cast `nearby_terrain_tiles` rows to it. Fix = `server/build.sh` regen + migrate the two components (plus the `TileComponent.cs` / `TerrainComponent3D.cs` comments) to `HexTile`.

**Subscribed-but-unwired view set (no binder consumes; no visible end):** `all_chat_channels`, `local_chat_messages`, `all_territories`, `local_trade_session`, `local_player_knowledge`, and the raw `ChanneledAction` table. In AGENTS.md terms these stop at step 3 (subscription) with step 4 (client calls the reducer / binds the view) unwired.

**Exact pending Obsidian steps (run with Obsidian + the `vscode-editor` fork, strictly serially):**
1. "Generate pointers in subfolders" on all four roots (`server/spacetimedb/src`, `client/Scripts`, `client/sstdbsdk`, `client/Scenes`) — confirm no `git diff`, resolving the open doc-04 pointer note.
2. Curate flow membership only where coverage changed (none proposed beyond the settled `flows.json`).
3. "Regenerate all flowcharts" — confirm every `main*.canvas` appears fresh and `lastResult.missingCanvases` is empty.
4. Hand-delete any stale `main-<topic>.canvas` the compose leaves behind (`main-lobby`, `main-player-data`, `main-movement`, `main-terrain`, `main-enemies`/`main-items`/`main-chests` plurals, `main-admin`) — stale mains are never pruned automatically. None exists on disk today, so this is compose-fallout-only.
