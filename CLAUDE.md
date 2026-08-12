# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository. ^e7e4af

## Project Overview

A **bullet-hell MMO roguelike**: **Godot 4.7 mono** C# client (`client/`) over a **SpacetimeDB 2.2** Rust module (`server/spacetimedb/`). Combat is dodge-focused — enemies fire telegraphed parametric bullet patterns, players evade.

**The server is authoritative.** The client mirrors subscribed rows and reports intent through reducers; it never computes damage or mutates stats locally. Any feature spanning both sides needs all five steps: table → reducer → client subscription → **client calls the reducer** → client renders. Forgetting step 4 is the recurring mistake.

## Where to find things

Most tasks span a server module and one or more client scripts. Use this map to jump straight to the relevant files instead of exploring the tree; the two child files below have the per-file detail.

| Domain | Server — `server/spacetimedb/src/` | Client — `client/Scripts/` |
|---|---|---|
| Player identity, profiles, lobby, login/logout, XP/leveling | `player/` | `Game/LobbyGui.cs`, `Game/GameManager.cs`, `Players/Local/`, `Players/Remote/` |
| Enemy AI, phases, attack sequences, bullet patterns | `enemy/` | `Players/Enemies/Enemy.cs`, `Game/BulletManager.cs` |
| Damage & healing math (the only place damage is computed) | `combat/mod.rs` | `Components/Combat/`, `Components/Weapon/` |
| World/hex grid, terrain generation, buildings | `world/` | `Components/Terrain/`, `Components/Camera/HexGridOverlay*Component.cs` |
| Item/enchantment catalog, equipping, inventory UI | `item/` | `Components/Catalog/`, `Components/Inventory/` |
| Loot drops and pickups | `player/` (`LootDrop`, `pickup_drop`) | `Items/Drop.cs`, `Components/Interaction/PickupComponent.cs` |
| Admin tools, seeding, debug overlay, connect/disconnect | `main/` | `Game/DebugOverlay.cs` |
| Connection, subscriptions, table→signal binding | — | `client/sstdbsdk/` |

- `server/CLAUDE.md` — per-module server breakdown **plus the SpacetimeDB Rust rules**. Read it before writing any Rust here.
- `client/CLAUDE.md` — per-file client breakdown plus the component naming convention.

## Folders to skip

- `client/Scripts/module_bindings/` — generated bindings. **Never read or hand-edit**; regenerate with `server/build.sh`.
- `client/addons/` — third-party plugins (`blastbullets2d`, `phantom_camera`, `open_godot_mcp`), gitignored; only relevant when debugging plugin internals. **`client/sstdbsdk/` is not in here** — it is first-party code every component depends on, and reads like any other client script.
- `client/.godot/`, `client/.vscode/` — editor cache/config. `client/Assets/`, `client/Textures/` — art.
- `server/spacetimedb/target/` — Rust build output.
- `docs/` — an Obsidian vault of design and ideation notes, **not a spec**; nothing in it is guaranteed to be implemented (see `docs/edualc.md`). Consult `docs/Technical Design Docs/` only once a design decision has been finalized.
- `flowcharts/` — Obsidian canvas diagrams of the tree.

## Running and Developing

- **Engine:** `Godot/Godot-stable_mono_win64.exe` — the mono build is required for C# and SpacetimeDB.
- **Run:** F5 in the editor. The project's main scene is `client/Scenes/lobby_gui.tscn`; gameplay lives in `Scenes/main.tscn`, entered from the lobby. Same entry point when launching via open-godot-mcp.
- **Client build:** no build system — Godot compiles `client/khvg.csproj` internally.
- **Server publish:** `server/build.sh` regenerates the C# bindings into `client/Scripts/module_bindings/` and publishes the `bullethell` module to the `local` server with `--delete-data`. **Every publish wipes the database**, so schema changes are hard cuts — no migrations, no dual-write; seeds re-run from `main/seeds.rs`. Logs: `spacetime logs bullethell`.
- **Export:** Web/HTML5 preset in `client/export_presets.cfg`.
- **⚠️ Never use Project → Tools → C# → Create C# solution** — it regenerates `khvg.csproj` from scratch and wipes the `SpacetimeDB.ClientSDK.Godot` package reference. The solution already exists. If the reference is ever lost, restore it with Godot closed: `dotnet add package SpacetimeDB.ClientSDK.Godot --version 2.7.1`.

## Verification

Automated checks stop at compilation: `cargo check` for the server, `dotnet build client/khvg.csproj` for the client. Anything beyond that — running the game, multiplayer behavior, visuals — is done **by the user**. When a change needs runtime verification, describe the manual test steps and ask the user to run them; do not launch the editor or game yourself.
