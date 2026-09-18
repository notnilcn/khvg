# AGENTS.md — Regeneration plan for the Description docs

This file is the working plan for (re)generating the system documentation in this folder (`docs/Technical Design Docs/Description/`). It assumes the reader already has the repo's maintainer references at hand — the root `AGENTS.md`, `client/AGENTS.md`, `server/AGENTS.md`, and the module-level `server/spacetimedb/src/enemy/AGENTS.md` and `server/spacetimedb/src/item/AGENTS.md`.

## What these docs are

These docs are centered around **how the tables are viewed and changed** — not around features, flows, or code organization in the abstract. Every table in `server/spacetimedb/src/` has exactly one story worth telling: **how it is changed** (which reducer, scheduled tick, lifecycle hook, or seed inserts/updates/deletes rows) and **how it is viewed** (which view or raw-table subscription carries it over the wire and which binder component consumes the row events on the client, plus any server-side reads that matter). Each system doc is a group of tables told through those two lenses; `00 Table Map.md` is the per-table spine they all transclude from. If a mechanism doesn't change a table or explain how a table is viewed, it doesn't get its own section — it belongs as a sentence inside the Changers/Readers prose of the table(s) it touches.

The docs you're writing live in this folder. **The Obsidian vault is the repo root** (`docs/`'s parent), not `docs/` — that's the only way the docs' wikilinks to code files (`client/`, `server/`) and to the canvases (`flowcharts/`) can resolve. All paths below are relative to repo root. The vault needs the `sync-embeds` and `block-link-plus` community plugins enabled — the conventions below depend on both — plus `advanced-canvas` (hard dependency of every flowchart command) and the local `vscode-editor` fork, whose "Generate flowcharts from code" feature produces the `.canvas` flowcharts under `flowcharts/` and whose "Generate pointers in subfolders" feature produces the `pointers.md` file indexes (see §"Pointer files"). (A nested `docs/.obsidian` also exists; ignore it — it can't see the code or the flowcharts.)

The docs are the narrative, learn-from-scratch companion to the terse maintainer references listed above.

## Hard rules for every session

1. **The code is the only source of truth.** The `AGENTS.md` files are accurate jump-off points (use their module maps to find files fast), but verify every claim against the actual code before writing it. Where they disagree, code wins.
2. **Never read `flowcharts/Subflowcharts/` canvas contents wholesale.** Use the doc's main canvas (`flowcharts/main-<name>.canvas`) plus at most a handful of targeted subflowchart canvases. *Listing* directory paths is fine — that's how you seed and curate `flows.json`. Link canvases by **file path**, never deep-link to a node. (Node ids are deterministic content hashes since the flows.json update, so they no longer re-roll — but path links remain the robust convention.)
3. **Document what the code does, not what it's planned to do.** Flag TODOs/stubs/known bugs explicitly in the doc's "Known gaps" section and inline where relevant.
4. **Don't restate the AGENTS.md files.** Link to them; these docs are the narrative complement.
5. **Audience**: intermediate programmer, brand new to Godot, C#, Rust, and SpacetimeDB. Explain unfamiliar concepts (reducers, views, subscriptions, node trees, scenes, `.tscn` files) briefly inline or with an analogy. Don't explain general programming ideas (loops, classes, dictionaries).
6. **Style**: dense causal prose — "X does Y *because* Z". One step = one cause-and-effect beat. Every mechanism claim should name the file/function that implements it. For table content specifically: a **changer** claim names the exact reducer/method that writes; a **client reader** claim names the view accessor, the subscription wave, the binder component, and the client-side consumer it feeds (the UI element or spawned node the row ends up driving).
7. The **phase-2 pass** updates the **Status** column of the table in `01 Roadmap.md` (parallel doc authors leave it alone — see §"Doc lineup & phases").

## Doc lineup & phases

Docs are written in **three phases**, not strict session order — the per-doc work is independent; only the shared artifacts serialize:

- **Phase 0 (one agent):** writes `00 Table Map.md` **in full** (all sections, all table entries and `^table-*` anchors — a shallow read of every module's pointer file plus its `tables.rs`/`def_tables.rs`/`instance_tables.rs`, their writers and client read paths, not just headers), writes `01 Roadmap.md`, regenerates all pointer files (§"Pointer files" — the re-run must produce no `git diff` before phase 1 starts), seeds `flowcharts/flows.json` by hand (it currently exists but is emptied), and runs the first full flowchart regeneration. Because 00 is complete before any doc starts, every sync-embed resolves from the start. Phase 0 also fixes the 04+ doc set: list the folders in `server/spacetimedb/src/` at phase-0 time — one 04+ doc per folder, numbered 04 upward in that listing order.
- **Phase 1 (one agent per doc — 02, 03, plus one 04+ module doc per folder in `server/spacetimedb/src/`):** each agent does exactly what its lineup row says — docs 02/03 follow their Scope rows, each 04+ doc follows the generic 04+ row applied to its module folder — deep code read starting from the pointer files matching that Scope (read through all `pointers.md` under the §"Pointer files" roots and pick entry points by Scope), prose, sync-embeds pointing at the pre-written anchors, Flowcharts-section links. Parallel agents **never edit** `00`, `01`, or `flows.json`, and **never run plugin commands** (no flowchart or pointer generation from a parallel session — see §"Flowchart integration" and §"Pointer files"). If a doc author finds a 00 entry that needs changing, it leaves a `**Phase 2 proposal:** …` note at the end of its doc instead of editing 00 directly. If a doc author finds a pointer file stale (a new file not listed), it leaves a `**Phase 2 proposal:** regenerate pointers` note instead of running the command.
- **Phase 2 (one agent):** applies any `**Phase 2 proposal:**` anchor changes (stripping the notes), re-runs pointer generation on all four roots, curates every doc's flow membership (batched), runs the final "Regenerate all flowcharts", then the shared verification items (pointers fresh, anchors exist, canvas links resolve, `missingCanvases` empty) and fills in the `01 Roadmap.md` status table.

## Pointer files (generated — the agnostic entry points)

Per-doc file lists rot: every rename silently invalidates them, and new folders appear as the game grows. So **no lineup row below names code files or pointer files to read — this plan is never updated just because a module or folder was added**. Instead, each code folder carries a generated `pointers.md` — a wikilink index of every code file under it, grouped by relative directory. Read through all the pointer files under a root for the entry points matching your doc's Scope, then write the documentation. The lineup's "Scope" column names only the stable scope (tables, reducers, views, scenes); bare file paths appear only for composition-root scenes, which are stable entry points.

Which pointer files to use (all paths vault-relative; regenerate, never hand-edit — discover by listing, never assume any folder list is complete):

- Server: read through all the pointer files in `server/spacetimedb/src/` for the entry points — whatever `<module>/pointers.md` files exist at the time. New modules added later are covered automatically.
- Client: same rule — read through all the pointer files under `client/Scripts/`, `client/sstdbsdk/`, and `client/Scenes/` for the entry points (`client/Scripts/module_bindings/` is excluded, never read it). New areas added later are covered automatically.

Regenerate with the `vscode-editor` fork: right-click the parent folder (`server/spacetimedb/src`, `client/Scripts`, `client/sstdbsdk`, `client/Scenes`) → "Generate pointers in subfolders" (count-suffixed when it will write more than one file), or the palette command "Generate pointers in subfolders…", or headless `plugin.flowchart.generatePointers(folder)`. Symbol-level entry points (`try_scaffold_profile`, `recompute_stats`, …) are deliberately NOT listed in the pointers — open the file, then use the `[[file#` symbol suggest popover to jump to them.

| Doc | Writes                                                                                                                                                                       | 00 sections                  | flows.json flow                           | Scope (match against all pointers.md — see §"Pointer files")                                                                                                                                                                                                                                                             |
| --- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------- | ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 1   | `00 Table Map.md` **in full** (all table entries/anchors, not just headers — phase 0), `01 Roadmap.md` (phase 0), **seed `flowcharts/flows.json`** with the full flow set    | —                            | —                                         | All `pointers.md` files, shallow pass (file lists only), plus the maintainer refs linked at the top of this file                                                                                                                                                                                                         |
| 2   | `02 The Component Framework.md`                                                                                                                                              | —                            | `main` + `framework` (+ seed all others)  | Scope: component/entity base classes; the server-side mirror (one table per concern) via the profile/enemy archetype helpers — find them with `[[file#` suggest                                                                                                                                                          |
| 3   | `03 Connection, Subscriptions & Views.md`                                                                                                                                    | —                            | `subscriptions`                           | Scope: connector, subscriber waves, binder, the three view flavors; `[[client/Scenes/main_menu.tscn]]` + `[[client/Scenes/game.tscn]]` for the inline subscriber; the event-table subscription pattern (find it via the server pointers' views sections)                                                                 |
| 4+  | `04+ <Module> Tables.md` — one doc per folder in `server/spacetimedb/src/` (numbered 04 upward in that phase's listing order; title `<Module>` is the humanized folder name) | the doc's own module section | `<folder-name>` (one flow per module doc) | Scope: that folder's tables — how each is viewed and changed — plus a per-file deep dive expanding that folder's `pointers.md`: the pointers say *what* each file uses; the doc explains *how* (call sites, call direction, guards, data passed). Trace client read paths to their visible end where the tables surface. |

Docs 02 and 03 have no table entries because they cover machinery, not tables — the framework itself (02) and the read path every table share (03). They omit **Tables** (schema §5), replacing it with a main body organized one section per piece of machinery (component base classes for 02; connector, subscriber, binder, view flavors for 03). They keep **Find it fast** (schema §3) — it indexes their machinery sections instead of table sections. Doc 03 in particular is the prerequisite for every later doc: it explains views, subscription waves, and binder components once, so the 04+ module docs can say "read via the `nearby_enemies` view in the game wave" without re-teaching the mechanism.

Aspirational systems described in the Game Design Docs but absent from the code (guild territory PvP, base-building beyond `place_building`/`remove_building`, biomes as gameplay, most of enchantment breadth, the un-built `BulletDespawnEvent`/`slash_bullet` bullet-despawn design) stay **out** of these docs; where a doc touches such an edge it says so explicitly. (There is no `control_bullets` reducer — the implemented bullet-control protocol is the `BulletControlEvent` event table; see `enemy/AGENTS.md`.)

## System doc schema (docs 02–nn)

Every system doc is organized **around how its tables are viewed and changed** — a reader asking "how does X manifest in the client?" or "what writes X?" should be able to land on the exact table section that answers it without skimming prose. The Tables body (§6) and the Table changes embeds (§5) are the doc; everything else exists only to index or illustrate those view/change paths. Every system doc has exactly these sections, in order:

1. **Assumed knowledge** — wikilinks to the prerequisite docs.
2. **The 30-second version** — a short paragraph: which tables this system owns and the one-sentence shape of their write/read paths, no detail.
3. **Find it fast** — the doc's navigation index: a lookup table mapping the questions a reader actually asks ("how do enchantments show up on the client?", "where is damage computed?", "what happens when I die?") to the place that answers each. Columns: *Question / concept* → *Table* → *Where* — an anchor link into this doc's table section, or a link to another doc / a `^table-*` entry in 00 when the answer lives elsewhere. Cover every table the doc owns plus the handful of cross-doc questions this system most often raises. Keep questions phrased the way a newcomer would ask them, not in code identifiers.
4. **Flowcharts** — wikilinks to the generated `.canvas` flowcharts for this system: the doc's own main canvas (`flowcharts/main-<doc-slug>.canvas`) plus 1–3 deep-dive subflowchart canvases under `flowcharts/Subflowcharts/`, all linked by path (never by node id).
5. **Tables** — the main body. **One `## <TableName>` section per table the doc owns**, in the order the tables appear in 00 — no free-floating prose blobs. Each table section has exactly these subsections, in order:
	- **Shape** — what a row *means* and why the table is shaped this way (key columns, constraints, relations to other tables). Only as much as the next two subsections need; this is not a schema dump.
	- **Changers** — every path that inserts, updates, or deletes rows: the reducer, scheduled tick, lifecycle hook, or seed, told as dense causal prose with its guards, write invariants, and formulas, each claim naming the file/function. If an operation never happens (append-only, never deleted), say so — that is information.
	- **Readers** — how rows are consumed, split into ***Client*** and ***Server***:
		- ***Client*** — the full read path *to its visible end*: view accessor or raw-table subscription → subscription wave → binder component → row-event handler → **the concrete thing on screen or in the scene it ends up driving** (which UI panel, which spawned node, which visual). This subsection is where "how does X manifest in the client?" gets answered — never stop at the binder; trace the row to its manifestation.
		- ***Server*** — guards every reducer runs, AOI semijoins, full scans worth warning about. Omit when unremarkable.
   Where content duplicates a table entry, sync-embed it from 00 instead of re-typing; free prose is for everything the entry doesn't say.
7. **Files — pointer deep dive** (04+ module docs only) — one `### <file>` subsection per file (or tight file group) in the module folder, in the order that folder's `pointers.md` lists them. The pointers say *what* each file uses; this section says *how*: call sites with file/function names, call direction, guards/preconditions, and what data is passed. Keep each subsection to the file's role in viewing/changing this module's tables — this is not a line-by-line walkthrough.
8. **Cross-table flows** (optional) — the few mechanisms that genuinely span several tables and can't be told under any one of them (e.g. the damage pipeline, joining the world). Keep short; link into the per-table sections rather than re-telling them. Omit when every mechanism fits under a single table.
9. **Known gaps / stubs** — explicit list of TODOs, stubs, unwired UI, and known bugs in this system's code (see the verified gap lists in §"Known gaps" below — each gap names its target doc). If none, omit the section.
10. **Where to go next** — 1–3 sentences pointing to the next doc(s) to read.

## `01 Roadmap.md` (phase 0)

Contains: what these docs are and who they're for; the conventions section (copy §"Mechanical conventions" below into it, adapted as observer-facing guidance); the reading-order/status table (doc #, title, status, one-line coverage summary — mirror the lineup table above, statuses all start blank and get filled in by the phase-2 pass); a link to the global `flowcharts/main.canvas` as the visual overview (**this link stays unresolved until phase 0 seeds `flows.json` and composes — that's expected, don't "fix" it by hand-creating a canvas**); and the note about aspirational systems staying out. Status values: blank / `done`.

## `00 Table Map.md` conventions

Phase 0 writes the file **in full** — every section header plus all table entries and anchors (a shallow read of every module's pointer file plus its table files) — with one `# <Module>` section per folder in `server/spacetimedb/src/`, in that phase's listing order. Phase 0 discovers the folders by listing `server/spacetimedb/src/` at phase-0 time — never assume any folder list is complete; a new folder means a new 00 section and a new 04+ doc. Each section holds every table defined in that folder.

Entry format within each section:

- One entry per table defined in that folder, in definition order. Scheduled-job tables get their own entry (their only "changer" is whoever arms/disarms the job row).
- The entry opens with the bold table name and a wikilink to its definition (`**PlayerPosition** — [[…tables.rs#PlayerPosition|definition]]`), followed by three bullet groups — these mirror the **Changers** / **Readers** subsections of the system docs, so a doc author can lift the entry's prose into its per-table section unchanged:
  - **Changers** — every insert / update / delete site, each with a code wikilink. Group by operation; name the reducer, scheduled tick, lifecycle hook, or seed. If an operation never happens (e.g. no update path, append-only event table, no delete anywhere), say so explicitly — "never updated" is information.
  - **Client readers** — the exact path to the client *and to its visible end*: the view accessor or raw public table, the subscription wave (`BaseTables`/`LobbyTables`/`GameTables` in `TableSubscriber.cs`), the binder component / script that consumes row events (with `ReplayExistingRows` on/off where it matters, e.g. the event tables), and the concrete UI element or scene node the row ends up driving. If the client never sees the table (server-only, or `public` but unsubscribed), say so — that is a first-class outcome, not an omission.
  - **Server readers** — only where the read pattern matters (guards every reducer runs, AOI semijoins, full scans worth warning about). Omit the group when unremarkable.
- Each entry ends with a `^table-<slug>` block anchor; the slug is the kebab-case table name (`^table-player-position`, `^table-bullet-pattern-event`). Table names are unique across the module, so anchors need no section prefixes.
- Entries cross-refer to other entries by anchor name ("the chunk row in ^table-player-chunk"), never by table name alone when an anchor exists.
- Where an entry's details belong to another doc, say so and link it.

## Flowchart integration (the `vscode-editor` fork's canvas flowcharts)

The plugin's flowchart feature (right-click a folder in Obsidian's file explorer, the palette commands, or drive it headlessly via `app.plugins.plugins['vscode-editor'].flowchart.*`) generates a deterministic tree of `.canvas` files under `flowcharts/Subflowcharts/` at three granularity levels (`_symbol`, `_codefile`, `_subfolder`), mirroring the `client/` and `server/` folders, with the root aggregate at `flowcharts/Subflowcharts/root_subfolder.canvas`. **Named main flowcharts** are defined in **`flowcharts/flows.json`** — a manifest mapping flow names to lists of subflowchart canvas paths:

```json
{ "flows": { "main": ["flowcharts/Subflowcharts/root_subfolder.canvas"], "combat": ["…", "…"] } }
```

**Current state: `flows.json` exists but is empty** (`{ "flows": {} }`), and all composed `main*.canvas` files have been deleted — `flowcharts/` contains only `Subflowcharts/` plus the empty manifest. Phase 0 re-seeds it **by hand, as part of that phase** with the full pre-defined flow set (see below) — this is a deliberate step the agent performs, not a prerequisite to assume done. Writing it before the first compose run also pre-empts the plugin's one-time legacy migration, which would otherwise seed a `"main"` flow from any `includeInMainFlowchart` canvas tags and write the manifest itself.

**Exact palette command names**: "Regenerate all flowcharts" (full pipeline: code model → subflowcharts → tag-driven aggregates → compose every flow in flows.json, strictly serialized per discovered tree), "Generate flowcharts from code…" (subflowcharts only), "Generate aggregate flowchart from subflowchart metadata…" (aggregates + compose all flows; no code parse needed). Composition output: the `"main"` flow → `flowcharts/main.canvas`, each other flow `<name>` → `flowcharts/main-<name>.canvas` (flat in `flowcharts/`, no subfolder). One invocation composes **all** flows at once. Membership lives in the manifest, not in canvas frontmatter, so it survives code regeneration untouched. Key mechanics:

- **Hard dependency: the Advanced Canvas plugin must be enabled** — every flowchart command aborts with a Notice otherwise.
- **Stable across regeneration**: canvas file **paths**, node ids (deterministic content hashes), and manually adjusted node positions (carried over on overwrite by matching id — but a canvas *renamed* by the collision ladder breaks the id match and loses its positions). Manually added text nodes in aggregate canvases are also preserved, and composed `main*.canvas` files keep any existing frontmatter.
- **Stale pruning is automatic**: a generation run trashes (**vault trash** `.trash/`, recoverable — not system trash) any previously generated, granularity-tagged `Subflowcharts/` canvas it didn't rewrite — controlled by the "Prune stale flowcharts" setting (default on). Canvases outside `Subflowcharts/` (mains, `flows.json`) are never pruned — and a **stale `main-<name>.canvas` is never cleaned up** if its flow is renamed or emptied; delete it manually.
- **Headless API** (`plugin.flowchart.*`, all return structured JSON and stash it on `plugin.flowchart.lastResult`): `generate(folder, extensions)` (subflowcharts only), `regenerate(scope)` (aggregates + compose), `regenerateAll()` (everything, every tree), `setFlowMembership(files, flowName, include)`, `setAggregateIncluded(Many)`, `generatePointers(folder, extensions?)` (pointer indexes — see §"Pointer files").
- **No run-overlap protection in the plugin** — concurrent runs race on file creation. Drive runs strictly serially, and note long async eval calls can return empty while still running: poll `lastResult` (or stash a global) rather than assuming completion.
- **`regenerateAll` regenerates with all configured extensions** (it ignores per-folder extension selection) and discovers only folders literally named `flowcharts` that have a `Subflowcharts/` child or a `flows.json` — the vault-root `flowcharts/` qualifies.
- **`flows.json` is hand-editable** — compose re-reads it fresh every run and reads are tolerant (missing/malformed → treated as empty). But API edits (`setFlowMembership`, the context menus) rewrite the whole file and **drop empty flows**, so every pre-defined flow needs at least one real member canvas. Keep members within their own tree — cross-tree members render as isolated nodes.
- **Dangling entries**: a renamed or pruned canvas leaves a dangling manifest entry — skipped at compose time and reported as `missingCanvases` in the result (a flow whose members are *all* missing produces no canvas at all). The manifest is deliberately not auto-edited; keep entries in sync when canvases move.
- **Two curation layers — don't confuse them**: the canvas tag `includeInAggregateFlowchart` gates which nodes appear *inside* codefile/subfolder aggregate canvases (auto-added on create, survives regeneration); `flows.json` membership gates the `main*.canvas` compositions. These docs only curate the second.

### Per-phase flowchart steps

**Phase 0 (one agent):**

1. **Regenerate everything** with the palette command "Regenerate all flowcharts" (or `plugin.flowchart.regenerateAll()` via eval — strictly serialized; poll `lastResult`). This regenerates both code roots' subflowcharts, prunes stale ones, rebuilds aggregates, and recomposes every flow in `flows.json`.
2. **Seed `flows.json`:** after the regenerate, list the canvas paths under `flowcharts/Subflowcharts/` (listing paths is fine; rule 2 only bans reading canvas contents wholesale) and write `flowcharts/flows.json` by hand with the full flow set from the lineup table: `"main"` (root/client/server top-level subfolder canvases) plus one flow per doc (`framework`, `subscriptions`, and one per 04+ module doc with slug = its module folder name), each seeded with the subflowchart canvases that obviously cover that system — **at least one member per flow** (empty flows are dropped on the next API write). The manifest currently exists but is emptied (`{ "flows": {} }`), so this is a from-scratch rewrite the agent performs itself. Then re-run "Regenerate all flowcharts" and verify every `main*.canvas` appears and `lastResult.missingCanvases` is empty.

**Phase 1 (parallel doc authors):** the only flowchart step is filling in the doc's **Flowcharts** section (schema §4) linking `flowcharts/main-<doc-slug>.canvas` plus 1–3 deep-dive subflowcharts (`01 Roadmap.md` links the global `flowcharts/main.canvas`). **Never run the plugin's flowchart or pointer commands or edit `flows.json` from a parallel session** — the plugin has no run-overlap protection, and API writes rewrite the whole manifest. If a doc's flow coverage looks wrong, leave a `**Phase 2 proposal:** …` note at the end of the doc instead.

**Phase 2 (one agent):**

3. **Curate each doc's flow** (batched; only where coverage changed or a phase-1 author proposed it): add/remove member canvases via right-click → "Include in flow ▸ …" / "Exclude from flow ▸ …" (or `setFlowMembership` via eval).
4. Re-run "Regenerate all flowcharts" and verify every `main*.canvas` appears fresh and `lastResult.missingCanvases` is empty.

## Mechanical conventions (exact syntax — follow precisely)

**Wikilinks to code**:
```
[[filename#function name#function index|display text]]
```

- Example: `[[server/spacetimedb/src/main/seeds.rs#seed#4|seeds]]`.
- Every table entry and every flowchart entry links to code wherever possible.

**Wikilinks to canvas flowcharts** are ordinary wikilinks including the folder path.

**Sync-embed blocks** (sync-embeds plugin) — for any content duplicated between docs, transclude from the single source (the table map) instead of re-typing:

````
```sync
![[00 Table Map#^table-player-position{seamless:true,title:false,marker:01.}]]
```
````

- `marker:NN.` — the entry number *as it should render in the embedding doc*, zero-padded to two digits. System-doc table sections number their entries from `01.` regardless of the entry's position in 00.

**Section references between docs** use Obsidian wikilinks, or with display text.

## Verification checklist

Items 2–5 are per-doc — each phase-1 author runs them before finishing its doc. Items 1, 6, 7, 8 touch shared artifacts — the phase-2 pass runs them across the whole set.

1. Every `^table-*` anchor referenced by any doc's sync-embeds exists in `00 Table Map.md` (phase 2 — also the moment to apply any `**Phase 2 proposal:**` anchor changes and strip those notes).
2. Spot-check every `[[file name#function name#function index|…]]` code link: the search text appears (uniquely) in that code file.
3. Every class, table, reducer, view, and function named in the doc exists in the code today — grep for it. (The pre-refactor client had classes like `TerrainManager`, `CameraController2D`, `CameraRig`, `LocalPlayerCombat`, `LocalPlayerInventory` — these **no longer exist**; their successors are the `*Component` classes in `client/Scripts/Components/`. Never cite the old names except to say they were replaced.)
4. **Cite the live wiring site.** Binder/signal wiring lives inline in `game.tscn`, `local_player.tscn`, `default_enemy.tscn`, `non_local_player.tscn`, and `world_3d.tscn` (the `DebugOverlay` is also declared inline in `game.tscn`, not instanced from a scene). All standalone duplicate component scenes from the earlier drift-hazard list have since been deleted (`entity_spawner_component.tscn`, `terrain_component.tscn`, `camera_rig_component.tscn`, `camera_2d_presenter_component.tscn`, `hex_grid_overlay_component.tscn`, `debug_overlay.tscn`, `damage_component.tscn`, `catalog_component.tscn`, `subscription_component.tscn`) — and `DamageComponent` itself is gone from the client (`HitZone` derives from `AreaComponent`). The rule stands for any future duplicates: cite the live scene, never a standalone component scene.
5. **Structure check (per doc):** docs 02–03 cover their Scope; each 04+ module doc gives every table defined in its module folder its own `## <TableName>` section in the same order as 00, with **Shape** / **Changers** / **Readers** subsections, plus the **Files — pointer deep dive** section (§7); every ***Client*** reader subsection traces the row to a concrete on-screen or in-scene manifestation (not just a binder); the **Find it fast** index covers every owned table and its links resolve to real headers/anchors.
6. Each doc's **Flowcharts** section links resolve; every linked canvas was regenerated in the final compose (phase 2); each doc's flow exists in `flows.json` with at least one member; and the last compose result's `missingCanvases` was empty.
7. `01 Roadmap.md` status table updated for every doc (phase 2).
8. Pointer files fresh: re-running "Generate pointers in subfolders" on all four roots (`server/spacetimedb/src`, `client/Scripts`, `client/sstdbsdk`, `client/Scenes`) produces no `git diff` (phase 2).

## Known gaps to document (verified against the code — each names its target doc's "Known gaps" section; server gaps name the 04+ module doc owning the table — resolve the doc via `00 Table Map.md` at phase-1 time, never hardcode a doc number)

**Server:**
- **→ module doc owning the lobby tables:** `client_connected` mishandles the already-in-world case: it purges the player's statuses/zones, deletes the `LoggedInPlayer` row and inserts a `LoggedOutPlayer` row (drops the player back to the lobby) instead of rejecting — flagged as a bug only in a `log::error!` message, not a comment.
- **→ module doc owning `PlayerPosition`/`PlayerChunk`:** `PlayerPosition`/`PlayerChunk` rows persist while logged out ("ghosts"); enemy sim guards against them via `logged_in_player` checks rather than cleaning up (cleanup happens only in `teardown_profile`, i.e. on death or `delete_profile`).
- **→ module doc owning `BuildingTile`:** `BuildingTile` is `public` but the client never subscribes to it — buildings aren't a designed feature yet, so the table is write-only world authoring for now.
- **→ module doc owning `PlayerPositionDebug`:** `PlayerPositionDebug` has no client reader at all (server-only table, inspect via `spacetime sql`) — `client/AGENTS.md`'s claim that `DebugOverlay.cs` is "paired with server main/debug.rs" is stale: `DebugOverlay` is a purely local perf HUD (FPS/memory/enemy count) and never touches the table.

**Client:**
- Leftover debug `GD.Print`s in `LobbyComponent.cs` (**→ module doc owning the lobby tables**) and `PositionSyncComponent.cs` (a `[Desync]` print — **→ module doc owning `PlayerPosition`**). The `TableBinderComponent.Bind/HandleInsert` prints are now gated behind its `[Export] Verbose` flag (off by default — **→ 03** documents them as opt-in diagnostics; the unconditional leftovers are `TableSubscriber.OnLobbySubApplied`/`OnGameSubApplied`).
- **→ module doc owning the lobby tables:** `LobbyGui` is scene navigation only — not yet wired to the `create_profile`/`join_world` reducers (those calls live in `LobbyComponent`). Its ServerList/Settings panels are also show/hide only.
**Resolved since the last doc pass — do not re-add these as gaps:**
- The seven unreferenced duplicate component scenes formerly listed in checklist item 4 (and `DamageComponent` itself) have all been deleted — the drift hazard is gone. `HitZone` now derives from `AreaComponent`.
- The `TerrainComponent._Ready/OnTileRowInserted` and `EntitySpawnerComponent.OnDropInsert` debug `GD.Print`s are gone.
- `LootDrop.expires_at` **is** enforced now: `tick_status_effects` sweeps expired drops (1 Hz), `LOOT_DROP_EXPIRY` = 10 s in `main/global.rs`.
- `nearby_remote_players_profiles` **is** AOI-filtered now: it right-semijoins `PlayerProfile` over the same chunk-filtered `PlayerPosition` scan as `nearby_remote_players` (see the comment at `player/views.rs`).
- The deleted `src/plan.md` design note (`BulletDespawnEvent`/`slash_bullet`) is no longer referenced anywhere in the code. The `PlayerDamageOutcome`/`EnemyDamageOutcome` dead-code enums are gone from `combat/mod.rs`.
- The old `hex_grid_overlay.gd` stale comment in `world/hex.rs` is gone.
- The camera/presentation doc from the previous lineup is dropped: it owns no tables. Its table-touching content (the `MapConfig` binders on the hex-grid overlays) moved into the terrain/world module doc; the purely client-side presentation notes (disabled legacy `Camera2D`, hidden overlays that still run) were pruned with it.
