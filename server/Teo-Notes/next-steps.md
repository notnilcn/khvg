# Next Steps

## 1. System Overview Refresh + Server Cleanup

A lot of server-side systems have changed since `system-overview.md` was last written (profile-based player model, item schema split into Item/Weapon/Armor/Accessory/Artifact/Consumable, views, etc.).

- Rewrite `system-overview.md` to reflect the current tables, reducers, and views on both server and client.
- Audit server logic for leftover functionality from before the schema redesign that no longer fits (dead code, stale fields, mismatched assumptions).
- Update any logic that doesn't integrate cleanly with current systems.
- Add code comments to existing server logic where it helps future readability (only where the WHY is non-obvious, per global no-comments-by-default rule).

---

## 2. Inventory Server Logic

New slot layout (24 slots total):

| Indices | Zone | Allowed types | Behavior |
|---|---|---|---|
| 0 | Weapon | Weapon | equip (recompute) |
| 1–5 | Hotbar | Consumable only | use (`use_item`) |
| 6–9 | Accessory | Accessory | equip (recompute) |
| 10–13 | Armor | Armor | equip (recompute) |
| 14–15 | Artifact | Ability | equip (recompute, no-op for now) |
| 16–22 | General | everything except Bag | storage |
| 23 | Bag | Bag | deferred (no logic yet) |

New/changed reducers (replacing `update_inventory` entirely):

- **`pickup_drop(drop_id)`** — rewrite: scan general slots (16–22) first for an empty type-compatible slot, then fall back to scanning all other slots in index order. If nothing fits, `Err` and the drop stays on the ground. Calls `recompute_stats`.
- **`drop_item(slot_index)`** — new: removes item from slot, inserts a `LootDrop` at the player's current position/chunk with a new `LOOT_DROP_EXPIRY` constant, calls `recompute_stats`.
- **`swap_slots(from_index, to_index)`** — new: bidirectional type-compatibility check (each occupied side's item must be allowed by the other slot; empty side trivially passes). Either check fails → `Err`, atomic reject, nothing changes. Calls `recompute_stats`.
- **`use_item(slot_index)`** — new: only `ItemType::Consumable` is usable (else `Err`). Applies `Consumable` effect, clears the slot (no stacking), calls `recompute_stats`.
- **Remove `update_inventory`** — unused by client, had a multiset-validation hole; the four reducers above become the only mutation paths.

Schema changes:

- `Consumable.effect: String` → typed `ConsumableEffect` enum (matches the no-string-parsing convention used for `PatternType`/`WeaponPattern`). v1: `Heal` variant restores HP via `potency`, capped at `max_hp`.
- `recompute_stats` keeps its `_ => {}` fallback for `ItemType::Ability` (artifact) — extension point for passive on-equip artifact effects once real artifacts are designed; no new infra needed yet.
- New `LOOT_DROP_EXPIRY` constant in `world/methods.rs`.
- Updated slot-builder helpers in `player/methods.rs` (`artifact_slot()`, consumable-only hotbar slot, trimmed general count).

Note: this changes inventory shape, so existing player rows need a `--clear-database` republish.

---

## 3. Client Inventory/Hotbar UI

- Build UI to view all inventory zones (weapon, hotbar/consumables, accessory, armor, artifact, general, bag) with item icons and stats.
- Wire UI actions to the new reducers: drag/swap → `swap_slots`, drop → `drop_item`, use → `use_item`.
- Stats panel reflects `LocalPlayerStats.OnUpdate` in real time.
- Find temporary art assets for item icons and test the full flow with real items equipped/swapped/used.

---

## 4. Loot Pickups + Enemy Reward Drops

Depends on 1 and 2 being done first.

- Define drop tables per enemy template (item type, drop chance, quantity range) in seed data — not hardcoded.
- On enemy death, insert `LootDrop` row(s); server-authoritative allocation if multiple players are involved (not every participant guaranteed a drop).
- Confirm end-to-end: enemy dies → loot row inserted → client picks up via `pickup_drop` → inventory updated → stats recomputed.

---

## 5. World Zones + Dynamic Enemy Spawning + Procedural World Gen

- Replace the current hardcoded single-enemy spawn with a world zone configuration system (`EnemyZone` table already exists — wire it up properly).
- Enemy spawning driven by zone config across the whole world, not a fixed test spot.
- Procedural world generation tied to actual building/world rules (hex grid, claimed zones, etc.) and real world assets — test assets available.
