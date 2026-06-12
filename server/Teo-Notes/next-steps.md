# Next Steps

## 3. Inventory UI — Other Sections + Toggle Menu

Done so far:

- Hotbar rendering (weapon slot 0 + consumable slots 1–5) via `InventorySlotUI` (`SlotIndex` + `Icon`) and `LocalPlayerInventory.UpdateSection`, driven by `LocalPlayer.GetSlotItemId`.
- Hotbar keys 1–5 (`Hotbar1`–`Hotbar5` input actions) call `use_item(slot_index)` when the slot isn't empty.

Remaining:

- Build UI for the other sections (Accessory 6–9, Armor 10–13, Artifact 14–15, General 16–22, Bag 23) using the same `InventorySlotUI` + `UpdateSection` pattern — just needs nodes laid out and assigned in the inspector.
- Inventory becomes a toggle menu: a keybind (TBD) opens/closes a `Control` containing all the non-hotbar sections.
- While the menu is open, the standalone screen-space hotbar hides and a second "menu hotbar" (visually integrated with the rest of the menu) becomes the active one. Both sets of `InventorySlotUI` nodes reference slot indices 1–5 — only one set is visible/interactive at a time.
- Drag-and-drop between slots calls `swap_slots(from_index, to_index)` using each `InventorySlotUI.SlotIndex`.

---

## 4. Drop Item + Loot Despawn + Pickup Lock

- Right-click on the hovered slot (in the open menu) calls `drop_item(slot_index)`.
- New scheduled reducer to expire `LootDrop` rows past `expires_at` (the field already exists from step 2). Client already reacts to `LootDrop.OnDelete` (`GameManager.OnDropDelete` frees the `Drop` node), so this is purely a server-side scheduled cleanup.
- Pickup lock: the player who dropped an item shouldn't instantly re-pick it up. Add `dropped_by: Option<Identity>` to `LootDrop`, set by `drop_item`. Client-side in `Drop.cs`: if `DroppedBy == LocalIdentity`, run a 5s timer on spawn during which `OnBodyEntered` ignores the local player.

---

## 5. Item Stats Sidebar

- Hovering/clicking a slot in the open menu shows its stats in a sidebar.
- Derive on hover rather than storing on `InventorySlotUI`: resolve `GetSlotItemId(slotIndex)` → `Item` → typed data (`Weapon`/`Armor`/`Accessory`/`Artifact`/`Consumable`) via the existing `GameManager` caches — same resolution `LocalPlayer.ResolveSlotAt` already does for `GeneralSlots`.
- Decide per-item-type display format (weapon stats vs. armor bonuses vs. consumable effect description) once the sidebar UI exists.

---

## 6. More Test Items + Loot Drop/Pickup Integration

- Seed more `Item`/`Weapon`/`Armor`/`Accessory`/`Artifact`/`Consumable` entries for variety across all slot types.
- Define drop tables per `EnemyTemplate` (item type, drop chance, quantity range) in seed data — not hardcoded.
- On enemy death, insert `LootDrop` row(s); server-authoritative allocation if multiple players are involved (not every participant guaranteed a drop).
- Confirm end-to-end: enemy dies → `LootDrop` inserted → client spawns `Drop` → player walks in → `pickup_drop` → inventory updated → stats recomputed.

---

## 7. World Zones + Dynamic Enemy Spawning + Procedural World Gen

- Replace the current hardcoded single-enemy spawn with a world zone configuration system (`EnemyZone` table already exists — wire it up properly).
- Enemy spawning driven by zone config across the whole world, not a fixed test spot.
- Procedural world generation tied to actual building/world rules (hex grid, claimed zones, etc.) and real world assets — test assets available.
