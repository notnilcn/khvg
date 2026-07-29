# 05 Item, Equipment & Enchantment System

## Assumed knowledge

[[01 Architecture & Sync Model]] — tables, reducers, subscriptions, views, and the base/game subscription waves are used throughout without re-explaining them. [[02 Entity & Component Framework]] — component registration, sibling/ancestor lookup (`GetSibling<T>()`, `GetAncestor<T>()`), and the client's compositional pattern this doc's UI components follow. [[04 Player System|04]] — the login/join flow, and specifically `join-2`'s starter loadout and the 24-slot layout (weapon/hotbar/accessory/armor/artifact/general/bag index ranges) this doc assumes rather than re-derives; also `recompute_stats`'s *base* numbers (`compute_base_stats`, `compute_base_max_hp`), which this doc's fold sits on top of.

## The 30-second version

Every item — weapon, armor, accessory, artifact, bag, or consumable — is one row in a single `Item` table: `equip_slot` says where it can go, `stat_modifiers` says what it adds to the wearer's stats, and `behaviors` is an open list of composable effects (`Weapon`, `Consumable` today) that says what it *does*, independent of its slot. `Enchantment` rows are the same idea restricted to stats only — no behaviors — socketed into an item's `enchantment_ids` up to that item's own `max_enchantments` cap. `PlayerInventory` is 24 `InventorySlot`s, each restricted to one or more `EquipSlot` types; `swap_slots`, `pickup_drop`, `drop_item`, and `use_item` all move `item_id`s (and, inconsistently, `enchantment_ids`) between those slots and the world's `LootDrop` table. Every one of those reducers ends by calling `recompute_stats`, which folds every equipped item's and socketed enchantment's `stat_modifiers` — plus any timed consumable buffs — on top of the level-based *base* stats [[04 Player System|04]] established, using additive-percentage stacking for `Mult` modifiers and a special case for `Hp`, which resolves into `PlayerData.max_hp` rather than a `PlayerStats` column. Client-side, `InventoryComponent`/`SlotComponent`/`ItemSidebarComponent`/`StatsSidebarComponent` render all of this reactively off `LocalPlayer.InventoryChanged`/`StatsChanged`, and `CombatComponent` ([[07 Combat & Damage Math|07]]'s territory) resolves whatever's equipped in the weapon slot into what actually fires.

## System flowchart

```sync
![[99 End-to-End Timeline Flowchart#^equip-1{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-2{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-3{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-4{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-5{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-6{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-7{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-8{seamless:true,title:false,marker:08.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-9{seamless:true,title:false,marker:09.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^equip-10{seamless:true,title:false,marker:10.}]]
```

## Main body

### The unified item catalog

`Item` (`item/tables.rs`) is a single table for every equippable and consumable thing in the game — no per-type side tables. `equip_slot: EquipSlot` (Weapon/Artifact/Armor/Accessory/Bag/Consumable) says only where it goes; `stat_modifiers: Vec<StatModifier>` says what it adds to the wearer; `behaviors: Vec<ItemBehavior>` is an open list of what it *does*. Because slot and behavior are independent fields rather than one column dispatching on the other, an item's location and its function can vary independently — an accessory that fires bullets like a weapon is a legal row (`equip_slot: Accessory, behaviors: [Weapon(..)]`), even though nothing in the seeded catalog currently does this. Two `ItemBehavior` payloads exist today: `WeaponBehavior` (damage/range/fire_rate/pattern/etc. — the shape `CombatComponent` reads to decide how a weapon fires, full mechanics [[07 Combat & Damage Math|07]]'s territory) and `ConsumableBehavior` (`effect`/`potency`/`duration`, resolved in `equip-8`/`equip-9` above). [[item/tables.rs##pub struct Item|Item]] · [[item/tables.rs##pub struct StatModifier|StatModifier]] · [[item/tables.rs##pub enum ItemBehavior|ItemBehavior]]

`Enchantment` (same file) is the same "data, not code" idea narrowed to stats only: `stat_modifiers` plus `allowed_slots: Vec<EquipSlot>` restricting which item types can hold it. There's no `ItemBehavior`-equivalent field on `Enchantment` — an enchantment can never grant a new behavior, only adjust numbers, which is why the enchantment system stays firmly inside this doc's "stat modifier" scope rather than needing its own behavior-dispatch story.

Both tables are seeded once, in `boot-2`: `seed_world_items` inserts six items (Helmet, Hat, Skull, Bag, Bread, Bow — the same six `join-2` places into the starter loadout) and `seed_world_enchantments` inserts five (`bear`, `swift`, `eagle_eye`, `iron_skin`, `vampiric`), each with `allowed_slots` restricting where it can socket — `swift` on everything, `vampiric` on artifacts only. [[item/seeds.rs##pub fn seed_world_items|seed_world_items]] · [[item/seeds.rs##pub fn seed_world_enchantments|seed_world_enchantments]]

`all_items`/`all_enchantments` (`item/views.rs`) are unfiltered, anonymous views — every client sees the same full catalog, subscribed once in the base wave ([[01 Architecture & Sync Model|01]]'s `conn-5`) and cached client-side by `CatalogComponent` (`conn-7`). Nothing in this doc re-subscribes or re-caches them; every lookup below (`GameManager.GetItem`, `GetEnchantment(s)`) reads that same warm cache. [[item/views.rs##fn all_items|all_items]] · [[CatalogComponent.cs##public Item? GetItem|GetItem]]

**Admin catalog tooling**, colocated in the same file rather than `main/admin.rs`: `give_item`/`remove_item` place or strip an item from a target player's general slots by username/profile-name lookup, and `upsert_item`/`upsert_enchantment` insert-or-update a whole catalog row. All four are `is_admin`-gated and run outside the ordinary player session — the same "meanwhile, independently" category [[09 Admin, Debug & World Lifecycle|09]] covers for `main/admin.rs`'s reducers, just structurally filed under `item/` because they mutate item-catalog tables. [[item/reducers.rs##pub fn give_item|give_item]] · [[item/reducers.rs##pub fn upsert_item|upsert_item]]

### Inventory slots and the allowed-types gate

`InventorySlot` (`player/tables.rs`) is `{ allowed_slots: Vec<EquipSlot>, item_id: Option<String>, enchantment_ids: Vec<String> }` — [[04 Player System|04]]'s per-type constructors (`weapon_slot`, `armor_slot`, …) each build one with a single-element `allowed_slots`, except `general_slot` (`[Weapon, Artifact, Armor, Accessory, Consumable]` — note `Bag` is deliberately absent even from the general fallback, so a second `Bag`-type item with the bag slot already occupied has nowhere to land; a real if narrow edge case). Every reducer below that moves an item checks the *destination* slot's `allowed_slots` against the item's `equip_slot` before allowing the move — that single check is the entire equip-legality system; there's no separate "can this class wear this" gate anywhere. [[player/tables.rs##pub struct InventorySlot|InventorySlot]]

### Swapping and equipping

`equip-1`/`equip-2` above cover the reducer itself; the noteworthy part is what it *doesn't* validate client-side (nothing — the server is the sole gate) and what it *doesn't* move server-side: `swap_slots` only reassigns `item_id`, leaving each slot's `enchantment_ids` where they were. That's inconsistent with `pickup_drop`/`drop_item` (`equip-5`/`equip-6`), both of which correctly carry `enchantment_ids` alongside `item_id` — see Known gaps below. Every legal swap ends in a `recompute_stats` call, same as every other inventory-mutating reducer in this doc.

### `recompute_stats`: folding gear on top of the base

[[04 Player System|04]] established the *base*: `compute_base_stats(level)` and `compute_base_max_hp(level)`, both flat `level - 1` scaling. `recompute_stats` (`player/methods.rs`) is what turns that base into the number a player actually sees, and it's the deferred half of doc04's story:

- **Which slots count as "equipped."** The test is `slot.allowed_slots.len() != 1 { continue; }` — any slot restricted to exactly one `EquipSlot` type folds its item's modifiers in. That includes the obvious worn-gear slots (weapon, armor ×4, accessory ×4, artifact ×2) but *also* the four hotbar/consumable slots and the single bag slot, none of which a player would call "equipped" in the intuitive sense. It deliberately excludes the eight general/backpack slots (multi-type, so `len() != 1`). This is a different, broader test than `LocalPlayer.IsEquipmentSlot` (client-side, gates only the enchant-socketing UI to slot 0 and 5-14) — see Known gaps.
- **What folds in, per qualifying slot.** The slot's own item's `stat_modifiers`, then every enchantment named in `slot.enchantment_ids`, looked up by id and its `stat_modifiers` folded in too. Independently of any slot, every `ActiveConsumableEffect` row for the profile (timed buffs from `equip-8`) folds in as well.
- **How it accumulates.** One `[f32; 7]` pair (six `StatKind`s plus `Hp`) — `flat` and `mult` kept separate — via a `fold_modifier` closure shared across items, enchantments, and buffs alike; there's no special-casing by source, only by `StatMode`.
- **How it resolves.** `(base + flat) * (1 + mult)`, rounded once at the end. This is additive percentage stacking, not compounding: two `Mult` sources of `0.10` each give `+20%` total, not `+21%`. The seeded catalog only exercises one `Mult` source today (`vampiric`, `Vitality +5%`), so this only becomes visible once a player stacks two or more `Mult` modifiers on the same stat.
- **The `Hp` special case.** `StatKind::Hp` has no `PlayerStats` column — its resolved value becomes the new `PlayerData.max_hp` (baseline `compute_base_max_hp(level)`), and current `hp` is clamped down if it now exceeds that cap. This is why the Helmet's `+15 Hp` modifier (`stat_modifiers`, seeded) actually changes a player's max HP rather than sitting inert.

[[player/methods.rs##pub fn recompute_stats|recompute_stats]] is called from `try_scaffold_profile` once (a profile's first join, [[04 Player System|04]]'s `join-2`) and, per-mutation, from `swap_slots`, `pickup_drop`, `drop_item`, `use_item`, `apply_enchantment`, `remove_enchantment`, and `tick_active_consumable_effects` — anywhere an item, enchantment, or buff could have entered or left a stat-folding slot.

### Loot: picking up and dropping

`equip-5`-`equip-7` cover the mechanism: `pickup_drop` prefers a type-specific empty slot over a general one, carries `enchantment_ids` with the item (unlike `swap_slots`), and deletes the `LootDrop` row on success. `drop_item` is the precise inverse — it clears both fields from the slot together and creates a `LootDrop` row stamped with `expires_at`, a column nothing currently reads (Known gaps). `nearby_loot_drops` reuses the exact AOI chunk-filtering mechanism [[03 World & Hex Grid|03]]'s `move-1`-`move-3` established for terrain, applied here to `LootDrop.chunk_index` — no bespoke radius, just the shared `nearby_indices` wrapper. Client-side, a `Drop` node is pure glue (`Drop.cs` wires its own two children in `_Ready` and owns nothing else): `DropVisualComponent` resolves a texture through the same catalog/texture cache every other textured entity uses, and `PickupComponent` owns both the pickup trigger (`Area2D.BodyEntered`) and a self-pickup lock (a `Timer` started only for the identity that dropped the item, via `GameManager.IsLocal`) so a player doesn't instantly re-collect what they just threw away by standing on it.

### Consumables and timed buffs

`use_item` (`equip-8`) is gated on `consumable_behavior` finding a `Consumable` entry in the item's `behaviors` — a behavior test, not an `equip_slot` test, in keeping with the catalog's design intent that slot and function are independent (nothing in the seeded catalog currently has a non-`Consumable`-slot item carrying a `Consumable` behavior, but the reducer doesn't forbid it). `apply_consumable_effect` dispatches on the behavior's `effect`: `Heal` calls straight into `combat::heal_player` ([[07 Combat & Damage Math|07]]'s territory — this doc's job stops at "a heal was requested"), `Buff` inserts an `ActiveConsumableEffect` row carrying a `StatModifier` derived from the `ConsumableBuffEffect` payload (always `Flat` mode — there's no `Mult`-mode consumable buff in the schema today) and calls `recompute_stats` immediately, so a buff is live the instant it's drunk/eaten, not on the next tick. Either branch consumes the item — `use_item` unconditionally clears the slot's `item_id`.

`tick_active_consumable_effects` (`equip-9`) is the third scheduled reducer named back in `boot-1`, ticking every `CONSUMABLE_EFFECT_TICK_SECONDS` (1s). It decrements every active effect's `remaining`, deletes the ones that hit zero, and calls `recompute_stats` only for profiles that actually lost an effect that tick — a small but deliberate optimization, since most ticks most profiles have nothing expiring. [[player/reducers.rs##pub fn tick_active_consumable_effects|tick_active_consumable_effects]]

### Enchantment socketing

`apply_enchantment`/`remove_enchantment` (`equip-10`) are a small, symmetric pair: applying checks the enchantment's own `allowed_slots` against the *item's* `equip_slot` (not the inventory slot index the item happens to sit in) and the item's `max_enchantments` cap against the slot's current `enchantment_ids` length, then pushes the id; removing retains everything except the given id. Both recompute stats afterward, since a socketed enchantment's `stat_modifiers` fold in exactly like the item's own (`equip-3`). The per-item caps are small and deliberate — Bow and Helmet both cap at 2, Skull at 3, Hat at 1, Bag and Bread at 0 (unenchantable by design, not oversight: a bag or a piece of bread has nothing to socket).

Client-side, `ItemSidebarComponent.RenderDetails` always lists every already-socketed enchantment (with a Remove button, unconditionally), but only offers un-socketed candidate rows (Socket buttons) when `LocalPlayer.IsEquipmentSlot(shownSlot)` is true — slot 0 or 5-through-14. That's narrower than what the server reducer actually permits (any slot whose current item satisfies the enchantment's `allowed_slots`), so the socket-adding UI simply never appears for hotbar/general/bag slots even though nothing server-side would reject it for an item that happened to qualify. In practice this is inert — no consumable or bag item has `max_enchantments > 0` to begin with — but it's a UI restriction layered on top of, not derived from, the server's actual rule.

### From equipped weapon to fired weapon

`CombatComponent` (`Components/Weapon/`, [[07 Combat & Damage Math|07]]'s territory in full) listens for `LocalPlayer.InventoryChanged` and re-resolves `LocalPlayer.EquippedWeapon`'s `WeaponBehavior` from its `behaviors` list — the same `weapon_behavior` lookup `player/methods.rs` uses server-side for damage — refiring only when the equipped item's id actually changed. Everything about how that behavior turns into bullets, hit zones, and damage is out of scope here; this doc's contribution stops at "slot 0 holds an `Item`, and that item may or may not carry a `WeaponBehavior`."

### Client-side inventory UI

`InventoryComponent` (`ControlComponent`, `Scenes/UI/inventory_panel.tscn` inside `local_player.tscn`) toggles the Menu/Hotbar containers on Tab, listens for `LocalPlayer.InventoryChanged`, and on each change re-textures every `SlotComponent` across six exported arrays (Hotbar/Armor/Accessory/Artifact/General/InventoryHotbar) by resolving each slot's item id through the catalog cache. Hotbar key presses (`Hotbar1`-`Hotbar4`) call `UseItem` directly rather than going through drag/drop. [[InventoryComponent.cs##private void OnInventoryChanged|OnInventoryChanged]]

`SlotComponent` is one slot, declared 24 times: drag source and drop target for `swap_slots` (`equip-1`), right-click-to-drop *only* inside the Backpack container (`inBackpack`, determined once at registration by walking ancestors for a node literally named `"Backpack"`), and hover-in/hover-out wiring to the sibling `ItemSidebarComponent`.

`ItemSidebarComponent` is the single richest UI piece this doc covers: on hover it renders the resolved item's name/icon/description, then `RenderDetails` builds a free-form list — stat modifiers (`StatModifier` formatted as `+N Stat` or `+N% Stat` depending on `StatMode`), a one-line behavior summary (`FormatBehavior`, weapon dps-shape or consumable effect text), and — only for items with `max_enchantments > 0` — a socket count plus one row per socketed enchantment and (when `IsEquipmentSlot`) one row per un-socketed candidate. It deliberately stays open while the mouse moves from the slot onto the sidebar itself (`QueueHoverClear`/`ClearIfMouseGone` check `GuiGetHoveredControl` before actually clearing), which is what keeps the Socket/Remove buttons clickable at all. [[ItemSidebarComponent.cs##private void RenderDetails|RenderDetails]]

`StatsSidebarComponent` is the simplest of the four: a pass-through display of `LocalPlayer.Level`/`Hp`/`MaxHp`/six stat properties, refreshed on `StatsChanged` — which both `ApplyData` and `ApplyStats` fire, so every `recompute_stats`-triggered row update in this doc eventually redraws it.

## Known gaps / stubs

- **`swap_slots` doesn't move `enchantment_ids`.** Only `item_id` is reassigned between the two slots; any enchantments socketed on the moved item stay bound to the vacated slot index instead of following the item. `pickup_drop` and `drop_item` both get this right (carrying `enchantment_ids` alongside `item_id`), which makes `swap_slots`'s omission look like an oversight rather than a deliberate design choice. Practical effect: dragging an enchanted item to a different slot silently detaches its enchantments.
- **`LootDrop.expires_at` is dead.** `drop_item` stamps every drop with `now + LOOT_DROP_EXPIRY` (300s), but no reducer, scheduled or otherwise, ever reads that column back — there's no expiry sweep, so dropped loot persists on the ground indefinitely regardless of the timestamp.
- **The stat-folding "equipped" test and the enchant-UI "equipment slot" test are different tests that happen to mostly agree.** `recompute_stats` folds any single-allowed-type slot (weapon/armor/accessory/artifact/bag/hotbar); `LocalPlayer.IsEquipmentSlot` (client, enchant-socket UI gating) covers only slot 0 and 5-14. They diverge on the hotbar (1-4) and bag (23) slots — currently invisible because no consumable or bag item in the seeded catalog carries `stat_modifiers`, but a future one would silently affect stats while never showing an enchant-socket option in the UI.
- **`ConsumableEffect` has no damage-over-time variant**, despite `main/global.rs`'s `CONSUMABLE_EFFECT_TICK_SECONDS` comment ("DoT/regen") and `99`'s `boot-1` step describing the scheduled tick as covering "buff/DoT ticks." The tick infrastructure (`ConsumableEffectSchedule`, `tick_active_consumable_effects`) is generic enough to support one, but `ConsumableEffect` today only has `Heal` and `Buff(ConsumableBuffEffect)` — no DoT exists in code.
- **`general_slot`'s `allowed_slots` omits `EquipSlot::Bag`.** A second `Bag`-type item, picked up while the one bag slot (23) is already occupied, has no fallback slot at all and fails `pickup_drop` with "No suitable slot available" — every other item type falls back to the general range, `Bag` doesn't.
- **Profile creation still has no texture picker** ([[04 Player System|04]]'s flagged gap) — irrelevant to items directly, but worth noting `PlayerProfile.texture_id` and `Item.texture_id` are resolved through the exact same `CatalogComponent`/`AllTextures` cache this doc relies on for item icons.

## Where to go next

[[06 Enemy AI & Bullet Patterns]] picks up the other reason `BulletPatternEvent`s exist — enemy attacks, not the player's own weapon fire. [[07 Combat & Damage Math]] is where the `WeaponBehavior` this doc introduces actually turns into damage (`compute_player_damage`), where `heal_player` (called from `equip-8`'s `Heal` branch) lives, and where `CombatComponent`'s firing logic is documented in full.
