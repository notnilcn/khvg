# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **bullet-hell MMO roguelike** built in **Godot 4.4.1**, targeting RotMG-scale multiplayer (85+ players, 850+ enemies, 2000+ bullets per realm). Combat is dodge-focused: enemies fire telegraphed parametric bullet patterns, players evade. Exported to Web/HTML5.

The codebase currently contains a Vampire Survivors-style template (auto-attacking, enemy waves, weapon progression) that is being rearchitected toward the above vision. Treat existing systems as a starting point, not the target design.

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

## Target Architecture (not yet implemented — priority order)

### Networking Model
- **Listen-server / dedicated server** via WebSocket (required for HTML5 export)
- **Hybrid authority:** client-authoritative player movement, server-authoritative damage and hit detection
- **Tick-rate by entity type:**
  - Players: 20Hz positional sync
  - Enemies: 10Hz (client interpolates between ticks)
  - Bullets: spawn/destroy events only — no positional sync (see Bullet System below)
- **Batched state packets:** one world-state packet per tick per client replacing all per-entity MultiplayerSynchronizers and individual position RPCs

### Area of Interest (AoI)
Server only sends entities within each client's viewport + margin. Critical for scale — without this, broadcasting 850 enemy positions to 85 players every tick is unacceptable bandwidth. Underpins everything; implement first.

### Spatial Grid
Server maintains a uniform spatial hash grid for:
- AoI queries (which entities are near player X?)
- Enemy target selection
- Bullet hit detection (only check bullets against players in the same cell)

Replaces the current O(N×M) per-frame brute-force targeting.

### Bullet System (parametric patterns)
Bullets are **not scene nodes on the server** and **not synced as positions**. Instead:
- Server defines `BulletPattern` types, each with a `get_position(bullet_index, elapsed_time) -> Vector2` function
- On fire: server sends a small spawn event (pattern_id + params) to clients in AoI
- Client reconstructs bullet positions locally using the same deterministic function — no further sync needed
- Server evaluates the same function each tick for swept CCD hit detection
- Patterns outlive the enemy that fired them (enemy death does not cancel in-flight patterns)
- No homing bullets. All patterns are fully parametric (aim direction at fire time is baked into the spawn event as a parameter)
- Client pools visual-only bullet nodes; server tracks patterns as lightweight data structs (no nodes)

### Hit Detection
- **Enemy bullets → player:** server-computed. Each tick the server evaluates pattern functions and runs swept CCD (line segment vs circle) against player hitboxes using the spatial grid.
- **Player attacks → enemy:** client-reports, server validates (existing hybrid model — keep, but tighten damage validation so server checks claimed damage against actual weapon stats).

### Entity Pooling
- Pre-allocate max enemy visual nodes and max bullet visual nodes on clients at scene load
- Avoids per-frame `Node.new()` / `queue_free()` and GC hitches at 2000+ bullets
- Server-side: enemies use `PhysicsServer2D` directly (already done); bullets are plain data structs

### Lag Detection (dead man's switch)
- Client sends a dedicated heartbeat packet every ~250ms
- Server tracks `last_heartbeat[peer_id]`; if gap exceeds ~750ms, marks player `is_lagging = true`
- If player dies while `is_lagging`: respawn at spawn point instead of death
- When lag clears: server sends "respawn at position X" event; client resets health + position
- Clean disconnects (WebSocket close event) handled separately — no heartbeat needed for those

### Known Issues in Current Codebase (fix before shipping)
- **Client can fake damage:** `report_hit()` accepts client-supplied damage amount with no validation against actual weapon stats. Server must look up attacker's weapon and compare.
- **No WebSocket heartbeat:** connections silently die after ~10min idle. Add ping every 30s.
- **No position validation:** server doesn't reject teleport-speed movement updates.
- **Late-join race condition:** player respawn during another client's join sequence can result in the joining client missing the respawned player.

## Key Docs

Full customization guide: `VS-Like/README/DOCS_English.txt`
Licensing: Free for personal & commercial use; no redistribution.
