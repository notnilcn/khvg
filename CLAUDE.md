# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.
## Project Overview

This is a **bullet-hell MMO roguelike** built in **Godot 4.6.1 mono**. Combat is dodge-focused: enemies fire telegraphed parametric bullet patterns, players evade.
## Where to find things

Most tasks span a server module and one or more client scripts. Use this map to jump straight to the relevant files instead of exploring the tree.

| Domain | Server — `server/spacetimedb/src/` | Client — `client/Scripts/` |
|---|---|---|
| Player identity, profiles, lobby, login/logout, inventory, XP/leveling | `player/` | `Game/LobbyGui.cs`, `Game/GameManager.cs`, `Players/Local/*`, `Players/Remote/RemotePlayer.cs` |
| Enemy AI, phases, attack sequences, bullet patterns | `enemy/` | `Players/Enemies/Enemy.cs`, `Game/BulletManager.cs` |
| World/hex grid, buildings, terrain | `world/` | `Components/Camera/HexGridOverlayComponent.cs` (+ `HexGridOverlay3DComponent.cs`), `Components/Terrain/` |
| Admin tools, content seeding, debug overlay, connect/disconnect lifecycle | `main/admin.rs`, `main/seeds.rs`, `main/debug.rs`, `main/lifecycle.rs` | `Game/DebugOverlay.cs` |
| Item/weapon/armor catalog, equipping, inventory UI | `item/` | `Components/Catalog/CatalogComponent.cs` (item cache/lookup, behind the `GameManager` facade), `Players/Local/LocalPlayer.cs` (slot resolution), `Components/Inventory/InventoryComponent.cs`, `Components/Inventory/SlotComponent.cs`, `Components/Inventory/ItemSidebarComponent.cs` |
| Loot pickups | `player/` (`LootDrop`, `pickup_drop`) | `Items/Drop.cs`, `Components/Interaction/PickupComponent.cs` |
| Generated bindings — **do not read** | — | `Scripts/module_bindings/` |

For a per-file breakdown of each server module, see `server/CLAUDE.md`. For a per-file breakdown of the client folders above, see `client/CLAUDE.md`.

## Folders to skip

These don't need exploration for gameplay/feature work:

- `client/Scripts/module_bindings/` — generated, regenerate via `spacetime generate` (see `server/CLAUDE.md`)
- `client/.godot/`, `client/.vscode/` — editor cache/config
- `client/addons/` — third-party plugins (BlastBullets2D, phantom_camera, godot_dotnet_mcp); only relevant when debugging plugin internals
- `client/Assets/`, `client/Textures/` — art assets
- `docs/Technical Design Docs` — technical documentation; only relevant when technical design decisions have been finalized
- `server/spacetimedb/target/` — Rust build output

## Running and Developing

- **Engine:** Godot 4.6.1 mono (`Godot_v4.6.1-stable_mono_win64.exe`) — the mono build is required for C# and SpacetimeDB
- **Run game:** Press F5 in Godot editor (main scene: `Scenes/main.tscn`)
- **Export:** Web/HTML5 preset configured in `export_presets.cfg`
- **No build system** — Godot handles compilation internally
- **⚠️ NuGet warning:** Never use **Project → Tools → C# → Create C# solution** — it regenerates the `.csproj` from scratch and wipes the `SpacetimeDB.ClientSDK` package reference. The solution already exists. If the package reference is ever lost, run `dotnet add package SpacetimeDB.ClientSDK --version 2.1.0` while Godot is closed.