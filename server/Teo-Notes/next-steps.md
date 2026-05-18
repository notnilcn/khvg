# Next Steps

## 1. Client refactor

Split client responsibilities so each entity owns its own behavior. No god objects. No IsOwner checks cluttering shared scripts.

**SpacetimeDBManager** — connection + subscription setup only. Subscribes to views on connect. No game logic.

**Game manager** — handles `OnInsert` / `OnDelete` for nearby entities (enemies, loot). Spawns and despawns nodes. Does not update state after spawn.

**Local player** — owns input, calls server reducers (`report_movement`, `report_enemy_hit`, `pickup_drop`). Subscribes to `local_player`, `local_player_data`, `local_player_stats`, `local_player_inventory`, `local_player_position` views.

**Remote player** — separate script from local player (no IsOwner branches). Subscribes to `remote_players` and `nearby_player_positions`. Listens to `OnUpdate` for its own player_id, lerps position.

**Enemy node** — spawned by game manager on `nearby_enemies` OnInsert. After spawn, handles its own `OnUpdate` from the same view. Reads `EnemyTemplate` for phase/attack data. Game manager handles `OnDelete` (despawn).

**Bullet factory node** — receives `BulletPatternEvent` table subscription (direct table sub, not a view — event tables don't support views yet). On `OnInsert`, unpacks `PatternType` variant and feeds into the bullet spawner algorithm.

**Drop node** — spawned by game manager on loot OnInsert. Handles its own pickup collision. Game manager handles OnDelete.

Split `Player.cs` into: `PlayerController` (orchestrator), `PlayerMovement`, `PlayerCombat`, `PlayerEquipment`. Remove any remaining dead-reckoning code and elevated client references.

---

## 2. Bullet factory node integration

Wire the server-side bullet pattern events to the client-side bullet spawner (BlastBullets2D or equivalent).

**Flow:**
```
Server tick_enemy_behavior decides enemy fires
  → Inserts BulletPatternEvent (ephemeral event broadcast)
  → Client BulletFactory receives OnInsert
  → Unpacks PatternType variant (Straight, Ring, Spiral, etc.)
  → Reads typed params (speed, count, spread, etc.) — no string parsing
  → Feeds into bullet spawner algorithm
  → Spawner handles pooling, trajectory math, collision
```

The bullet factory is a standalone node — it has no knowledge of which enemy fired or player state. It just takes a pattern + params and runs the spawner. Clean interface.

Bullet hit detection is client-side. Player collision → apply damage locally → call `report_player_hit` (reducer TBD).

---

## 3. World expansion + enemy behavior (TBD)

After bullet factory is wired, the systems are in place to build features on top of:

- **Enemy behavior logic** — fill in `tick_enemy_behavior` with actual per-template AI (movement toward target, phase transitions, attack timing based on `last_attack_time` + `fire_rate`)
- **Spawn zones** — scheduled reducer checks if players are near a spawn zone; spawns enemies only if needed; despawns when players leave
- **AoI resubscription** — client re-subscribes to nearby views when crossing a chunk boundary
- **World events** — loot, enemies, and other world state grouped by chunk. Currently blocked on view params (not yet in SpacetimeDB). Once view params land, views can be parameterised by chunk index list.
- **Torus world** — chunk wrapping for all position and subscription logic
- **Loot drop content** — drop table per enemy template instead of empty item_id placeholder
