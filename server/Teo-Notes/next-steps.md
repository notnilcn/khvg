# Next Steps

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
