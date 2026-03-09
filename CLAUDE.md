# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **Vampire Survivors-style roguelike game** built in **Godot 4.4.1**. It's a fully functional template with auto-attacking mechanics, enemy waves, and weapon progression systems. Exported to Web/HTML5.

## Running and Developing

- **Engine:** Godot 4.4.1 — open `project.godot` in the editor to develop
- **Run game:** Press F5 in Godot editor (main scene: `VS-Like/Maps/woods_map.tscn`)
- **Export:** Web/HTML5 preset configured in `export_presets.cfg`
- **No build system** — Godot handles compilation internally

## Architecture

### Core Systems

**Global Singleton** (`VS-Like/Game/Singleton/global.gd`)
Central manager holding the player reference and all 6 weapon resources. Handles weapon acquisition and upgrade tracking.

**Player** (`VS-Like/Characters/Player/player.gd`)
Extends `CharacterBody2D`. Uses a `ResPlayer` custom resource for character data. Manages movement (WASD), weapon inventory, passive stat multipliers (damage, speed, XP), and health. Death reloads the map.

**Enemy** (`VS-Like/Characters/Enemy/enemy.gd`)
Extends `Sprite2D` with physics via `PhysicsServer2D` directly (not CharacterBody2D) for performance. Auto-chases player, despawns when >600 units away. Elite enemies have 10× health, shader effect, and always drop chests.

**Spawner** (`VS-Like/Characters/Enemy/Utilities/Spawner/spawner.gd`)
Max 300 enemies at once. Unlocks new enemy types at 1/2/3-minute marks. Spawn interval decreases over time (1.0s → 0.75s → 0.5s → 0.35s). Supports circle-pattern spawns and elite spawns on timers.

**Weapon System** (`VS-Like/Weapons/`)
6 weapon types: Axe, Arrow, Bible, Boots, Parchment, Ring. Each is a `Weapon` (Node2D) configured by a `ResWeapon` custom resource. Active weapons use a fire timer; passive weapons (Boots, Parchment, Ring) grant stat bonuses without firing. Weapons support 3 upgrade levels.

**Drop System** (`VS-Like/Game/Drops/`)
Three drop types: Points (XP, 75% drop rate), Food (heals, timed spawns), Chest (from elites, opens weapon selection UI). Player has a `DropDetection` Area2D for auto-pickup.

### Data-Driven Design

Game objects use **Custom Resources** (`.tres` files) for configuration:
- Characters: `res_archer.tres`, `res_dwarf.tres`
- Enemies: `res_zombie.tres`, `res_bat.tres`, `res_cyclops.tres`, `res_goblin.tres`
- Weapons: one `.tres` per weapon type

### Collision Layers

| Layer | Purpose |
|-------|---------|
| 1 | World (static obstacles) |
| 2 | Player |
| 3 | Enemy |
| 4 | Drops |

### UI

Health bar, XP bar, weapons container (equipped weapons), weapon selection menu (on level-up, shows 3 random weapons), chest UI, floating damage text.

### Display

- Internal resolution: 640×360
- Window: 1280×720 (2× scale)
- Texture filter: Nearest neighbor (pixel-perfect)
- Renderer: GL Compatibility

## Key Docs

Full customization guide: `VS-Like/README/DOCS_English.txt`
Licensing: Free for personal & commercial use; no redistribution.
