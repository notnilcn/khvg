# Next Steps

## 1. Seed system (init reducer + builder traits)

Goal: cleanly define enemies, items, etc. in code once without re-doing on every republish.

`init` reducer runs once when the module is first published. Each type gets a `create` associated function that upserts (safe to re-run):

```rust
#[reducer(init)]
pub fn init(ctx: &ReducerContext) {
    EnemyTemplate::seed(ctx, "goblin", "Goblin", 100, vec![
        PhaseDef { phase_index: 0, hp_threshold: 1.0, attacks: vec![
            AttackDef { pattern_type: PatternType::Straight, fire_rate: 2.0, params: "speed=150".into() },
        ]},
        PhaseDef { phase_index: 1, hp_threshold: 0.5, attacks: vec![
            AttackDef { pattern_type: PatternType::Ring, fire_rate: 1.5, params: "speed=150,count=8".into() },
        ]},
    ]);

    ItemStats::seed(ctx, ItemStats { item_id: "sword".into(), base_damage: 20, ... });
}
```

Implement `seed()` as an associated method on each type (upsert pattern — insert if not exists, update if exists). This means republishing the module safely re-seeds without duplicating data.

## 2. CLI-callable admin reducers

Reducers callable via `spacetime call` for manual testing:

```bash
spacetime call spawn_enemy '["goblin", "res://...", 0.0, 0.0, false, false]'
spacetime call despawn_enemy '[42]'
spacetime call give_item '["Teo", "sword"]'
spacetime call change_stats '["Teo", 10, 10, 10, 10, 10, 25]'
```

These are already defined — just need to test the CLI invocation format. Can also add convenience reducers like `spawn_enemy_at_player(template_id)` that looks up an online player's position.

## 3. AoI chunk subscriptions (client-side)

Replace `SubscribeToAllTables()` in `SpacetimeDBManager.cs` with parameterised chunk queries:

```csharp
conn.SubscriptionBuilder()
    .OnApplied(OnSubscriptionReady)
    .Subscribe(new[] {
        // Own player data — no chunk filter
        "SELECT * FROM player WHERE player_id = @id",
        "SELECT * FROM player_data WHERE player_id = @id",
        "SELECT * FROM player_stats WHERE player_id = @id",
        "SELECT * FROM player_inventory WHERE player_id = @id",
        // Reference data — no chunk filter
        "SELECT * FROM enemy_template",
        "SELECT * FROM item_stats",
        // AoI — chunk window
        $"SELECT * FROM player_position WHERE chunk_x BETWEEN {minX} AND {maxX} AND chunk_y BETWEEN {minY} AND {maxY}",
        $"SELECT * FROM enemy WHERE chunk_x BETWEEN {minX} AND {maxX} AND chunk_y BETWEEN {minY} AND {maxY}",
        $"SELECT * FROM loot_drop WHERE chunk_x BETWEEN {minX} AND {maxX} AND chunk_y BETWEEN {minY} AND {maxY}",
    });
```

Re-subscribe when local player crosses a chunk boundary. AoI window = 1 chunk radius (3x3 grid of chunks around player).

## 4. Client refactor

See `system-overview.md` for the full architecture. Key points:

- `SpacetimeDBManager` — connection + subscription manager only
- `PlayerSync` autoload — owns PlayerPosition table callbacks, spawns/despawns remote player nodes
- `EnemySync` autoload — owns Enemy table callbacks, spawns/despawns enemy nodes; each enemy node simulates itself from EnemyTemplate data
- `DropSync` autoload — owns LootDrop table callbacks, spawns/despawns loot nodes
- `LocalPlayer` — calls report_movement timer, report_enemy_hit on weapon collision, pickup_drop on loot
- Split `Player.cs` into: Player (orchestrator), PlayerMovement, PlayerCombat, PlayerEquipment

Remove: `StdbBulletSync.cs`, `ElevatedClient.cs`, dead-reckoning code in `StdbPlayerSync.cs`

## 5. Bullet simulation integration (BlastBullets2D)

Each enemy script runs its own bullet simulation using BlastBullets2D plugin:
- On spawn: read `EnemyTemplate` for current phase's `AttackDef` list
- Each frame: track elapsed time from `spawn_time`, fire bullets at `fire_rate` intervals
- `pattern_type` + `params` drive the BlastBullets2D configuration
- Player collision detected locally → apply damage to local player HP

No server involvement. No sync. Fully self-contained per enemy script.
