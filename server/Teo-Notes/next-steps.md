# Next Steps

## M1. Complete Client Combat System

### Current zone system
Zones are spawned in world space at bullet fire time — not following the player. On each fire, `LocalPlayerCombat` computes the bullet origin (`player position + playerRadius` along aim direction) and spawns `ZoneCount` `Area2D` nodes at evenly-spaced positions along the aim direction out to `Range`. Each zone has a `CircleShape2D` with radius `zoneStep * 0.5f` so zones tile continuously with no gaps. Each zone is given a timed delay proportional to its index within the fire period (`(i + 1) * firePeriod / ZoneCount`), scans for overlapping enemies via `GetOverlappingBodies()`, calls `ReportEnemyHit` for each, then frees itself. The visual bullet is spawned via BlastBullets2D with lifetime `Range / BulletSpeed` so it visually reaches the last zone just as it scans. All zone positions and the visual bullet are fixed in world space from fire time — player movement after firing has no effect.

### Bullet patterns
Extend the weapon system to support attack patterns beyond a single straight shot. Patterns are defined per weapon and determine how zones are spatially arranged at fire time:

- **Straight** — current behavior, zones in a line along aim direction
- **Ring** — zones spread evenly in a full circle around the player, aim-direction-independent
- **Burst/Cone** — zones fanned within a spread angle around aim direction
- **Spiral** — zones at incrementally rotated angles, fires over multiple shots
- Additional patterns as needed

Each pattern is just a different function for computing zone center positions at fire time. The fire rate, zone count, and range still apply.

### Aim assist
A player setting (stored in player stats/profile, readable by `LocalPlayerCombat`). When enabled, at fire time the system finds the nearest cached enemy within a small cone around the current aim direction and slightly nudges the zone trajectory toward them. The nudge magnitude is a tunable value. Still fully range-limited — no benefit outside weapon range.

Aim assist is most effective against stationary enemies (guaranteed help) but can hinder against fast-moving enemies (may snap to the wrong target or away from intended aim). This preserves skill expression at higher difficulty levels.

Patterns that are inherently aim-independent (Ring) ignore aim assist since direction is irrelevant.

### Lock-on
A player stat/profile field, or granted by equipping a **lock-on artifact** in a hotbar slot (see Artifacts below). When active, at fire time the system uses the cached nearest enemy as the target and computes zone positions directed from the player toward that target, replacing the mouse aim direction entirely. Guarantees hits on the target as long as it is in range.

Lock-on interacts with patterns:
- **Straight** — zones fire directly at locked target
- **Ring** — no interaction, aim-independent by nature; lock-on has no meaningful effect
- **Burst/Cone** — cone centered on direction to locked target
- **Spiral** — base angle offset toward locked target

If the locked target dies mid-fire, the current shot's zones live out their timers normally. On `NearbyEnemies.OnDelete`, if the deleted enemy matches the cached target, the cache is cleared and the nearest enemy is immediately recomputed (independent of the periodic refresh timer).

### Nearest enemy cache
A periodic timer in `LocalPlayerCombat` (approx. 0.5s, slower than fire rate) scans `NearbyEnemies` and caches the nearest enemy ID. `NearbyEnemies.OnDelete` also triggers an immediate recompute if the cached enemy was the one deleted. Aim assist and lock-on both read from this cache at fire time.

### Artifacts
Rename the `Ability` item type to `Artifact`. Artifacts are rare items that occupy hotbar slots and grant passive or active modifiers to the combat system. Examples:
- **Lock-on artifact** — enables weapon lock-on while equipped
- Speed, damage, zone count modifier artifacts, etc.

In the short term, lock-on and aim assist are toggled via player profile fields edited manually for testing. Long-term, inventory items (artifacts) write to these fields when equipped/unequipped.

### Confirmation
After all patterns and aim modes are working, confirm the full weapon config surface is covered: `range`, `fire_rate`, `zone_count`, `bullet_speed`, `projectile_texture_id`, `pattern`, plus aim assist and lock-on flags from player profile.

---

## M2. Stress Test

With all systems in place, stress test how many enemies a single client can handle before performance degrades. Specific goals:

- **Enemy bullet lifetimes** — currently hardcoded at 8s in `BulletManager`. Move lifetime to enemy seed configuration so each enemy type has a realistic bullet lifetime appropriate to its pattern and range.
- **Client performance** — measure FPS and memory under increasing enemy counts.
- **Server cost** — measure server-side load during stress test. Exact tooling TBD, but some baseline metric should be captured (SpacetimeDB dashboard, logs, or a scheduled diagnostic reducer).

---

## M3. Combat Damage, Player Hits, and Audit Logs

Wire up the full damage loop:

- **Enemies taking damage** — `ReportEnemyHit` is already called by the zone system. Confirm server-side HP reduction, phase transitions, and death handling are correct.
- **Players taking damage** — wire BlastBullets2D `body_entered` signal to detect when enemy bullets hit the local player. Call a `ReportHit(event_id, damage)` reducer. Server validates and reduces player HP.
- **Audit log table** — on each valid `ReportEnemyHit`, append a row to an audit log table containing: player ID, enemy ID, item/weapon ID, timestamp, and weapon `fire_rate`. A scheduled audit reducer runs periodically, checks that the hit frequency per player per weapon is within the possible range given the weapon's fire rate, then deletes processed rows. Catches fire-rate exploit attempts without adding cost to every individual hit.

---

## M4. Enemy Death, Drops, and Rewards

Configure the full death and reward loop:

- **Drop configuration** — drops are defined in enemy seed data (item type, drop chance, quantity range). Not hardcoded.
- **Server-side exclusive drops** — drops are server-authoritative. Not every participating client receives a reward. If 10 players fight a boss, the boss drops a fixed number of items and they are allocated to specific players (e.g. first to hit, most damage, random weighted). This creates meaningful exclusivity and reduces economy exploits.
- **Inventory integration** — dropped items go to the appropriate inventory slot by item type (weapon → slot 0, artifact → hotbar, armor → armor slots, etc.). If the slot is occupied, item goes to general inventory or is dropped as a world pickup depending on preference.
- **Pickup flow** — already partially implemented via `LootDrop` table and `PickupDrop` reducer. Confirm end-to-end: enemy dies → loot row inserted → client picks up → inventory updated → stats recomputed.

---

## M5. Visual Inventory System

Build the client-side inventory UI:

- Display all inventory slots (weapon, hotbar, accessories, armor, general) with item icons and stats.
- Allow moving items between slots with drag-and-drop or slot selection.
- All mutations sync server-side via `UpdateInventory` reducer — UI is a view over server state, not local state.
- Support dropping items from inventory (removes from server, spawns world pickup or destroys).
- Support selling/discarding with confirmation.
- Stats panel reflects current equipped items in real time via `LocalPlayerStats.OnUpdate`.
