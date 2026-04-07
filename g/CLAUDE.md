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
Central manager holding the player registry (`players: Dictionary`) and the full item pool (`RES_ITEMS`). Key helpers: `pickup_item(res)` — equips directly if the relevant slot is empty, otherwise sends to inventory; `get_available_items()` — returns the full pool for chest rolls; `add_weapon()` kept as a legacy alias for `pickup_item()`.

**Player** (`VS-Like/Characters/Player/player.gd`)
Extends `CharacterBody2D`. Uses a `ResPlayer` custom resource for character data. Manages movement (WASD), equipment slots, inventory, RPG stats, and health. Death reloads the map.

- **RPG stats:** `strength`, `wisdom`, `dexterity`, `defense`, `vitality` (all `int`). Each level-up awards +1 to all stats and +5 max HP.
- **Equipment slots:** `equipped_weapon` (Weapon node), `equipped_ability` (Weapon node), `equipped_armor` (placeholder), `equipped_accessories: Array[Weapon]`. Slot count for accessories scales with Wisdom: 1 slot base, 2 at 500 WIS, 3 at 1000 WIS. `equip_item(res)` handles swapping — the displaced item goes to inventory.
- **Inventory:** `inventory: Array[ResWeapon]` (default 6 slots, max 12 via backpack). `consumables: Array[ResWeapon]` (default 2 slots, max 8).
- **Passive stat multipliers:** `extra_exp`, `extra_damage`, `extra_speed` — modified by equipped accessories in their `_ready()` / `remove_passive_effects()`.
- **Inventory UI:** press **I** to toggle. Created programmatically at runtime (`InventoryUI`); added to the HUD CanvasLayer.

**Enemy** (`VS-Like/Characters/Enemy/enemy.gd`)
Extends `Sprite2D` with physics via `PhysicsServer2D` directly (not CharacterBody2D) for performance. Auto-chases player, despawns when >600 units away. Elite enemies have 10× health, shader effect, and always drop chests.

**Spawner** (`VS-Like/Game/World/spawner.gd`)
Max 300 enemies at once. Unlocks new enemy types at 1/2/3-minute marks. Spawn interval decreases over time (1.0s → 0.75s → 0.5s → 0.35s). Supports circle-pattern spawns and elite spawns on timers.

Admin chat commands (press T; requires admin except `/stats` and `/inv`):

| Command | Description |
|---------|-------------|
| `/stats` | Print your RPG stats and HP to chat (no admin needed) |
| `/inv` | Print equipped items and inventory contents to chat (no admin needed) |
| `/chest [x y]` | Spawn an elite enemy (guaranteed chest drop) near you or at x,y |
| `/spawn [x y]` | Spawn an immortal dummy at x,y (default 0,0) |
| `/give <item> [peer]` | Give a weapon/item to yourself or another peer |
| `/remove <item> [peer]` | Remove a weapon/item from yourself or another peer |
| `/spawner on\|off` | Enable or disable the automatic enemy spawner |
| `/admin <peer>` | Grant admin (host only) |
| `/unadmin <peer>` | Revoke admin (host only) |
| `/setpass [pw]` | Set or clear the admin password (host only) |
| `/adminpass <pw>` | Claim admin via password (any player) |

**Item / Weapon System** (`VS-Like/Characters/Player/Utilities/Weapons/`)
All loot items use the single `ResItem` resource class (`res_item.gd`). Fields are grouped by category; unused fields default to 0 and are ignored at runtime. `ResWeapon` (`res_weapon.gd`) is a backward-compatibility shim that `extends ResItem`.

The `item_type` enum determines the equipment slot:

| `ItemType` | Value | Slot | Examples |
|-----------|-------|------|---------|
| `WEAPON` | 0 | Weapon | Axe, Arrow, Sine Bow |
| `ABILITY` | 1 | Ability | Bible |
| `ARMOR` | 2 | Armor | *(not yet implemented)* |
| `ACCESSORY` | 3 | Accessory (×1–3) | Boots, Parchment, Ring |

**Passive stat bonuses** for accessories/armor are stored directly in the `.tres` file (`bonus_speed_mult`, `bonus_exp_mult`, `bonus_damage_mult`, `bonus_defense`, `bonus_max_health`) and applied by the generic `PassiveItemWeapon` base class. Adding a new accessory requires only a `.tres` file — no new GDScript. Boots/Parchment/Ring each have a one-line `.gd` that just `extends PassiveItemWeapon`.

Weapons are obtained **only from chests** (dropped by elite enemies). Level-up no longer shows a weapon selection menu — it grants stats instead. The `WeaponSelection` scene has been removed.

**Drop System** (`VS-Like/Characters/Enemy/Utilities/Drop/`)
Three drop types: Points (XP), Food (heals), Chest (from elites). Chest UI shows up to 3 random items as clickable buttons; player picks **one** to pick up (or presses Done to skip). Picked-up items equip directly if the slot is empty, otherwise go to inventory.

**Inventory UI** (`VS-Like/Characters/Player/Utilities/UI/InventoryUI/inventory_ui.gd`)
Fully programmatic (no `.tscn`). Instantiated in `player._init_player()` and added to the HUD CanvasLayer. Shows: equipment row, inventory grid (12 slots, first 6 active by default), consumable row (8 slots, first 2 active), and stats panel. Click an equipped slot to unequip back to inventory; click an inventory slot to equip. Locked slots (beyond current backpack size / wisdom level) are tinted dark. Runs with `PROCESS_MODE_WHEN_PAUSED` and handles KEY_I itself so closing works even while the game is paused.

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

Health bar, XP bar, weapons container (shows currently equipped items by slot), chest UI (item pick from elite drops), inventory panel (I key — equipment + inventory + stats), floating damage text.

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
