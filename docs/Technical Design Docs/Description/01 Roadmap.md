# 01 Roadmap

## What these docs are

This folder is the narrative, learn-from-scratch companion to the codebase: a bullet-hell MMO roguelike built from a Godot 4.7 mono C# client (`client/`) and a SpacetimeDB Rust server module (`server/spacetimedb/src/`). The client is fully compositional — every entity is assembled from components — and the server mirrors that compositionally: one table per concern acts like a component, ids like `profile_id`/`enemy_id` join a logical entity's rows across tables, and "archetype helpers" bundle the rows of one entity.

These docs are the prose complement to the terse maintainer references ([[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`). Those tell a maintainer where things are; these explain how and why the pieces fit together, one system at a time, in reading order.

**Audience:** an intermediate programmer who is brand new to Godot, C#, Rust, and SpacetimeDB. Unfamiliar concepts — reducers, views, subscriptions, node trees, scenes, `.tscn` files — are explained briefly inline as they come up. General programming ideas (loops, classes, dictionaries) are not.

**Scope rule:** the code is the only source of truth. These docs describe what the code does today, not what it is planned to do. TODOs, stubs, and known bugs are flagged explicitly in each doc's "Known gaps" section. Aspirational systems described in the Game Design Docs but absent from the code — guild territory PvP, base-building beyond `place_building`/`remove_building`, biomes as gameplay, most of the enchantment breadth, the slash/bullet-despawn protocol in `server/spacetimedb/src/plan.md` — stay **out** of these docs; where a doc touches such an edge it says so explicitly.

## How to read these docs (conventions)

The Obsidian vault is the **repo root**, not `docs/` — that is the only way the wikilinks below can resolve to code files (`client/`, `server/`) and to the flowchart canvases (`flowcharts/`). The vault expects the `sync-embeds`, `block-link-plus`, `advanced-canvas`, and `vscode-editor` (local fork) community plugins enabled; several conventions below depend on them.

- **Code links** use block-link-plus line-search syntax: `[[filename##unique text on target line|display text]]`. The filename is bare (no path) — the plugin searches the vault — and the search text is a line (usually a function or struct signature) that is unique within that file. Clicking one jumps straight to that line of code, e.g. [[lifecycle.rs##pub fn init|init]]. Every timeline step and flowchart entry links to code wherever possible, so any claim can be checked at its source.
- **Flowchart canvas links** are ordinary wikilinks that include the folder path (e.g. [[flowcharts/main.canvas]]). Canvases are linked by file path, never deep-linked to an individual node.
- **Sync-embed blocks** (the sync-embeds plugin) transclude content from its single source instead of duplicating it. Numbered steps live once in [[00 End-to-End Timeline Flowchart]] and are embedded into the system docs. If a sync block renders as raw text, the sync-embeds plugin is not enabled.
- **Timeline anchors**: each step in the timeline doc ends with a `^prefix-N` block anchor (e.g. `^move-2`). Cross-references between steps use those anchor names, never bare numbers. Step numbering restarts at 1 within each section of the timeline and within each system doc's flowchart.
- **Section references** between docs are ordinary Obsidian wikilinks, sometimes with display text.

## Reading order & status

Docs are meant to be read in order; each system doc opens with wikilinks to its prerequisites. [[00 End-to-End Timeline Flowchart]] is the spine: a single numbered timeline of the game's runtime from boot to teardown, from which the system docs transclude their steps. The **visual overview** of the whole codebase is the generated flowchart at [[flowcharts/main.canvas]], with a per-system canvas linked from each doc's "Flowcharts" section.

| #   | Doc                                                                                          | Status | Coverage                                                                                                   |
| --- | -------------------------------------------------------------------------------------------- | ------ | ---------------------------------------------------------------------------------------------------------- |
| 00  | [[00 End-to-End Timeline Flowchart]]                                                          | done   | The end-to-end runtime timeline; single source of the numbered steps transcluded into every system doc.     |
| 01  | [[01 Roadmap]]                                                                                | done   | This doc — purpose, audience, conventions, reading order.                                                   |
| 02  | [[02 The Component Framework]]                                                                | done   | The client's entity/component architecture and its server-side mirror (one table per concern, archetype helpers). |
| 03  | [[03 Boot & Connection]]                                                                      | done   | App startup, autoloads and main scene, SpacetimeDB connection/auth, server init and world seeding.          |
| 04  | [[04 Lobby & Profiles]]                                                                       | done   | Login flow, lobby UI, profile creation/selection/deletion, lobby reducers and views.                        |
| 05  | [[05 Joining the World]]                                                                      | done   | `join_world`/`leave_world`, subscription waves, entity spawning on the client.                              |
| 06  | [[06 Movement & Position Sync]]                                                               | done   | Local movement, `report_movement`, position interpolation, AOI-filtered views, torus world wrapping.        |
| 07  | [[07 Terrain & World Streaming]]                                                              | done   | Hex chunks, procedural world generation, terrain tables/views, building placement.                          |
| 08  | [[08 Enemies & AI]]                                                                           | done   | Enemy archetypes, spawn/scheduling, bullet patterns, client enemy presentation.                             |
| 09  | [[09 Combat & Damage]]                                                                        | done   | Hit reporting, the server damage pipeline, health and death, XP/leveling.                                   |
| 10  | [[10 Inventory, Items & Enchantments]]                                                        | done   | Inventory and equipment, consumables, enchantments, loot drops, stat recomputation.                         |
| 11  | [[11 Camera & Presentation]]                                                                  | done   | Phantom-camera setup, the 3D backdrop, local/remote visual components.                                      |
| 12  | [[12 Admin & Debug]]                                                                          | done   | Admin reducers, the debug overlay, seed data.                                                               |
| 13  | [[13 Disconnect & Teardown]]                                                                  | done   | Disconnect handling, leaving the world, profile teardown, lingering "ghost" rows.                           |

Status values: blank = not yet written, `done` = written and verified against the code.
