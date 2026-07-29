# 00 Roadmap

## What these docs are

A narrative, learn-from-scratch companion to the terse maintainer references — root `CLAUDE.md`, `client/CLAUDE.md`, `server/CLAUDE.md`. Those files are dense module maps for someone who already knows the codebase; these docs are for someone who doesn't. Where the two disagree, the `CLAUDE.md` files and the code win — these docs link out to them rather than restate them.

## Who they're for

An intermediate programmer who is brand new to Godot, C#, Rust, and SpacetimeDB, but who already knows general programming concepts (loops, classes, dictionaries, event handlers). Godot- and SpacetimeDB-specific ideas — reducers, subscriptions, node trees, scenes, `.tscn` files — get a brief inline explanation or analogy the first time they matter.

## The code is the source of truth

Every claim in these docs is checked against the current code, not against what a design doc says should exist. Where the code has a TODO, a stub, or a known bug, the doc says so explicitly — in that doc's "Known gaps / stubs" section and inline where it's relevant to the step being described. Aspirational systems described in the Game Design Docs but absent from the code (guild territory PvP, base-building beyond `place_building`/`remove_building`, biomes as gameplay, most of enchantment breadth) are out of scope here; where a doc's subject matter brushes against one of these, it says so rather than describing something that doesn't run.

## Reading order & status

| # | Doc | Status | Covers |
|---|---|---|---|
| 00 | Roadmap | done | This doc — what these are, who they're for, conventions, reading order |
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
