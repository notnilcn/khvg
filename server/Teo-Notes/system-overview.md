# System Overview
# System Overview

SpacetimeDB is the database AND the server. Clients connect, subscribe to table subsets, and call reducers. The server stores state and broadcasts changes. No separate game server process.

---

## Tables

### Persistent (survive disconnects, across sessions)

| Table | What it stores | Who writes |
|-------|---------------|------------|
| `Player` | Identity, username, is_admin | Server on first connect |
| `PlayerData` | Level, XP, HP, max_hp | Server (gain_xp, report_enemy_hit) |
| `PlayerStats` | Computed STR/WIS/DEX/DEF/VIT/SPD | Server recomputes on level-up or inventory change |
| `PlayerInventory` | Equipment + backpack as Vec<InventorySlot> | Client via update_inventory |
| `ItemStats` | Item definitions, damage/stat formulas | Admin via upsert_item_stats or init |
| `EnemyTemplate` | Enemy type definitions: phases, attack patterns | Admin via upsert_enemy_template or init |

### Session (exist only while game is running)

| Table | What it stores | Who writes |
|-------|---------------|------------|
| `PlayerPosition` | x, y, rotation, chunk | Client via report_movement (~5 Hz) |
| `Enemy` | Live enemy instances: spawn info, hp, phase | Admin spawns/despawns; server updates hp+phase |
| `LootDrop` | Items on the ground | Server on enemy death |

---

## Reducers

### Any connected client

| Reducer | Signature | What it does |
|---------|-----------|-------------|
| `set_username` | `(username: String)` | Updates Player.username |
| `report_movement` | `(x, y, rotation: f32)` | Updates PlayerPosition. Call at ~5 Hz. |
| `report_enemy_hit` | `(enemy_id: u64)` | Server computes damage from caller's stats + weapon, updates Enemy.hp, triggers phase change or death |
| `pickup_drop` | `(drop_id: u64)` | Validates drop exists, adds item to PlayerInventory, deletes drop row |
| `update_inventory` | `(slots: Vec<InventorySlot>)` | Replaces PlayerInventory.slots, triggers recompute_stats |
| `gain_xp` | `(amount: u32)` | Adds XP, levels up if threshold crossed, updates PlayerData, recomputes stats |

### Admin only (`Player.is_admin == true`)

| Reducer | What it does |
|---------|-------------|
| `spawn_enemy(template_id, res_path, x, y, is_elite, immortal)` | Inserts Enemy row |
| `despawn_enemy(enemy_id)` | Deletes Enemy row |
| `upsert_enemy_template(template)` | Creates or updates enemy type definition |
| `upsert_item_stats(stats)` | Creates or updates item definition |
| `give_item(target_username, item_id)` | Adds item to target's backpack |
| `remove_item(target_username, item_id)` | Removes item from target's inventory |
| `change_stats(target_username, ...)` | Overrides target's PlayerStats directly |

---

## Client Subscriptions (AoI model)

Rather than `subscribe_to_all_tables()`, clients use parameterised SQL chunk queries. Each client knows its current chunk and re-subscribes when crossing a chunk boundary.

```sql
-- Other players nearby
SELECT * FROM player_position
WHERE chunk_x BETWEEN @min_x AND @max_x
  AND chunk_y BETWEEN @min_y AND @max_y

-- Enemies nearby
SELECT * FROM enemy
WHERE chunk_x BETWEEN @min_x AND @max_x
  AND chunk_y BETWEEN @min_y AND @max_y

-- Loot nearby
SELECT * FROM loot_drop
WHERE chunk_x BETWEEN @min_x AND @max_x
  AND chunk_y BETWEEN @min_y AND @max_y
```

**Always subscribed (no AoI filter):**
- `player` — all usernames (small, needed for display)
- `player_data`, `player_stats`, `player_inventory` — filtered to own player_id
- `enemy_template` — all templates (small reference data, read-only)
- `item_stats` — all item definitions (small reference data, read-only)

Chunk size is 320 units. AoI radius = 1 chunk in each direction (3x3 chunk window). No server-side views needed — the SQL condition in the subscription handles it per client.

---

## Client Simulation Model

### Player position (other players)
- Server relays position at ~5 Hz via PlayerPosition table updates
- Client receives `OnUpdate` → lerps visual node to new x/y
- Rotation updated directly (no lerp needed for camera direction)

### Enemy lifecycle
```
Admin calls spawn_enemy()
  → Enemy row inserted with spawn_x, spawn_y, spawn_time, hp=max_hp, phase=0
  → All clients in AoI receive OnInsert
  → Each client: instantiate scene from res_path, set position to spawn_x/y
  → Each client: start deterministic AI simulation from spawn_time

Server receives report_enemy_hit()
  → Computes damage, updates Enemy.hp + Enemy.phase
  → All clients receive OnUpdate → update health bar, switch phase behavior if phase changed

Admin calls despawn_enemy() OR server kills enemy (hp=0)
  → Enemy row deleted
  → All clients receive OnDelete → remove visual node, play death FX
```

### Enemy AI simulation (fully client-side)
- Each client reads `EnemyTemplate` for its `res_path` to get phase defs and attack patterns
- Enemy script computes elapsed = `Time.now - spawn_time`
- Determines current behavior from `EnemyTemplate.phases[Enemy.phase].attacks`
- Moves and fires bullets locally — no server involvement
- When `Enemy.phase` updates via OnUpdate: enemy script switches attack set immediately

### Bullet simulation (fully client-side, no server rows)
- Enemy script reads `AttackDef.fire_rate` + `AttackDef.pattern_type` + `AttackDef.params`
- Fires bullets at the right intervals based on elapsed time from spawn_time
- All clients run the same deterministic logic → bullets appear at the same time on all clients
- Local player collision detection → client applies damage locally (and optionally reports to server)
- No `BulletPattern` table. No server rows. Zero network traffic for bullets.

### Loot drops
```
Enemy dies (report_enemy_hit reduces hp to 0)
  → Server inserts LootDrop at enemy.spawn_x/y
  → All clients in AoI receive OnInsert → spawn loot visual

Client walks over loot, calls pickup_drop(drop_id)
  → Server validates, adds to PlayerInventory, deletes LootDrop row
  → Client receives OnDelete → remove loot visual
  → Client receives PlayerInventory OnUpdate → show new item
```

---

## Known Gaps (to address in future tasks)

- **Player taking damage from bullets** — currently no reducer for this. Need `report_player_hit(damage)` that any client can call on themselves, or trust client to apply locally and only sync HP on death/session.
- **Chunk AoI subscriptions** — currently using `subscribe_to_all_tables()`. Chunk subscriptions are the next networking milestone.
- **Torus world wrapping** — chunk queries need two-range queries near edges. Deferred until world size is finalized.
- **LootDrop item content** — currently spawns with empty item_id as placeholder. Needs drop table per enemy template.
