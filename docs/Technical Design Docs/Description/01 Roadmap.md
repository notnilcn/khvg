# 01 Roadmap

## What these docs are

This folder is the narrative, learn-from-scratch companion to the terse maintainer references ([[AGENTS.md]] at the repo root, [[client/AGENTS.md]], [[server/AGENTS.md]]). It documents the bullet-hell MMO roguelike as its code behaves **today**: a Godot 4.7 mono C# client (`client/`) over a SpacetimeDB 2.2 Rust module (`server/spacetimedb/src/`), with the server authoritative — the client mirrors subscribed rows and reports intent through reducers; it never computes damage or stats locally.

**Audience:** an intermediate programmer who is brand new to Godot, C#, Rust, and SpacetimeDB. Unfamiliar engine/framework concepts (reducers, views, subscriptions, node trees, scenes, `.tscn` files) are explained briefly inline where they first appear; general programming ideas are not.

**How to read:** start with [[00 End-to-End Timeline Flowchart]] — the whole game as one causal timeline, with every step anchored (`^prefix-N`) and linked to the code that implements it. Then read the system docs 02–13 in order; each one transcludes its own steps from 00 and adds the depth the timeline skips. The visual overview is the composed flowchart [[flowcharts/main.canvas]] — *(this link is intentionally unresolved until the flowchart pipeline composes it; do not hand-create the canvas)* — and each system doc links its own `flowcharts/main-<name>.canvas` plus deep-dive subflowcharts.

## Conventions used in these docs

- **Code links** use the vault-relative wikilink form `[[path#function name#function index|display]]`, e.g. `[[server/spacetimedb/src/main/seeds.rs#seed#4|seeds]]`. The vault root is the repo root, so links reach `client/` and `server/` directly. Every mechanism claim names the file/function that implements it, and every name cited was verified to exist in the code when written.
- **Duplicated content is transcluded, never re-typed.** Steps shared between 00 and a system doc appear as sync-embed blocks (the `sync-embeds` plugin) referencing 00's block anchors, e.g. `![[00 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:01.}]]`; `marker:NN.` is the step number as it renders in the embedding doc, zero-padded, restarting at `01.` in each system doc.
- **Flowchart links** are ordinary wikilinks to the canvas *file path* (never deep-links to a canvas node).
- **Cross-doc section references** use ordinary wikilinks, optionally with display text.
- **Known gaps** (TODOs, stubs, unwired UI, dead rows, stale comments) are flagged inline and collected in each doc's "Known gaps / stubs" section — the docs describe what the code does, not what it plans to do.
- The **Status** column below is filled in by the final verification pass; authors leave it alone.

## Reading order & status

| #   | Doc                                                                              | Status | Covers (one line)                                                                                                  |
| --- | -------------------------------------------------------------------------------- | ------ | ------------------------------------------------------------------------------------------------------------------ |
| 00  | [[00 End-to-End Timeline Flowchart]]                                             | done | The whole game as one causal timeline; every step anchored and code-linked (complete since phase 0)                |
| 01  | [[01 Roadmap]]                                                                   | done | This file — scope, conventions, reading order                                                                      |
| 02  | [[02 The Component Framework]]                                                   | done | Entity/component architecture client-side + its server mirror (one table per concern, archetype helpers)           |
| 03  | [[03 Boot & Connection]]                                                         | done | Server `init`/seeds/world build; `DatabaseConnector` autoload, auth, subscription waves, binder mechanics          |
| 04  | [[04 Lobby & Profiles]]                                                          | done | Menu scene, character slots, profile create/delete, login-state tables and lobby views                             |
| 05  | [[05 Joining the World]]                                                         | done | `join_world`, profile scaffolding, game subscription wave, entity spawning from rows                               |
| 06  | [[06 Movement & Position Sync]]                                                  | done | Movement reporting (angle+speed), torus wrap, chunk/AOI, interpolation & dead reckoning                            |
| 07  | [[07 Terrain & World Streaming]]                                                 | done | Hex/chunk grid, world-gen pipeline, terrain views, TileComponent pooling and MultiMesh batching                    |
| 08  | [[08 Enemies & AI]]                                                              | done | Templates/phases/sequences, spawn & behavior ticks, BulletPatternEvent, client bullet spawning                     |
| 09  | [[09 Combat & Damage]]                                                           | done | The single damage pipeline in `combat/mod.rs`, hit reporting, death, anti-cheat witness flags                      |
| 10  | [[10 Inventory, Items & Enchantments]]                                           | done | Item/enchantment catalogs, slot rows & spans, `recompute_stats`, consumables, abilities, loot drops                |
| 11  | [[11 Camera & Presentation]]                                                     | done | CameraRig + 2D/3D presenters, phantom-camera handoff, 3D backdrop, overlays, sprite/texture resolution             |
| 12  | [[12 Admin & Debug]]                                                             | done | Admin slot & the 18 admin reducers, item admin, client DebugOverlay, server position-debug feed                    |
| 13  | [[13 Disconnect & Teardown]]                                                     | done | `leave_world`, death teardown, ghost rows, disconnect handling, client-side despawn paths                          |

Status values: blank = not yet verified complete; `done` = verified in the final pass.

## Aspirational systems stay out

Systems described in the Game Design Docs but **absent from the code** — guild territory PvP, base-building beyond `place_building`/`remove_building`, biomes as gameplay, most of the enchantment breadth, and the un-built `BulletDespawnEvent`/`slash_bullet` bullet-control variant (its design note `server/spacetimedb/src/plan.md` has been deleted; the implemented protocol is `BulletControlEvent` + `control_bullets`) — are deliberately **not** documented here. Where a doc touches such an edge, it says so explicitly instead of documenting the aspiration.
