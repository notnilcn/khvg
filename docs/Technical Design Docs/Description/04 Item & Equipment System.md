# 04 Item & Equipment System

## Assumed knowledge

- [[00 Roadmap]] for how these docs are structured and the conventions used below.
- [[01 Architecture & Sync Model]] for the tables/reducers/views vocabulary — this doc's `Item` catalog and inventory reducers are just another instance of that pattern.
- [[03 Player System]] for `PlayerData`/`PlayerStats`, the base-stat-from-level formula (`compute_base_stats`/`compute_base_max_hp`), and `try_scaffold_profile` (the lazy-backfill reducer this doc's starting loadout comes from) — this doc picks up exactly where that one stopped, at `recompute_stats`'s item-modifier folding.

## The 30-second version

Every item in the game — weapon, armor, accessory, artifact, bag, or consumable — is one row in a single `Item` table, not five parallel side-tables joined by id. Two generic `Vec` fields carry everything an item *does*: `stat_modifiers` (flat/percent bonuses to the six player stats, plus HP) and `behaviors` (currently `Weapon` or `Consumable` payloads), while `equip_slot` only says *where it goes*. Slot and behavior are deliberately independent axes — an item can have `equip_slot: Accessory` and still carry a `Weapon` behavior, which the old schema (side-tables keyed by slot) couldn't express. A profile's inventory is a fixed 24-`InventorySlot` array baked in at scaffold time (weapon/hotbar/accessory/armor/artifact/general/bag), each slot carrying its own `allowed_slots: Vec<EquipSlot>` whitelist rather than the array position alone meaning anything. `swap_slots`/`drop_item`/`pickup_drop`/`use_item` are the only ways the array changes shape, and all four end by calling `recompute_stats` — the single function that walks every equipped slot and every timed buff, folds their `StatModifier`s (flat summed, then a shared percent multiplier applied once), and writes the result to `PlayerStats`/`PlayerData.max_hp`. Loot drops are a flat `LootDrop` table read through the same chunk-ring AOI view as terrain/enemies; today the only thing that creates one is a player manually dropping an item — nothing drops loot on enemy death yet, and a drop's `expires_at` timestamp is written but never read, so dropped items never actually expire.

## System flowchart

Ordering below is a scoped excerpt of [[99 End-to-End Timeline Flowchart]] — just the "Equip and inventory" fork of the parallel in-world loop. It picks up right after the profile's `PlayerInventory` row (scaffolded in [[03 Player System]]'s "Joining the world" section) first reaches the client.

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

[[Drop.cs##public override void _Ready|`_Ready`]]

## The unified `Item` catalog: slot and behavior as independent axes

`Item` (`item/tables.rs`) is one `public` table: `item_id` (primary key, a string like `"Bow"` or `"Helmet"`), `display_name`, `description`, `texture_id`, `equip_slot: EquipSlot`, `stat_modifiers: Vec<StatModifier>`, `behaviors: Vec<ItemBehavior>`. `EquipSlot` (`Weapon`/`Artifact`/`Armor`/`Accessory`/`Bag`/`Consumable`) is purely about *where an item can sit* — it has no bearing on what the item does once it's there. What it does lives entirely in the other two fields:

- `StatModifier { stat: StatKind, amount: f32, mode: StatMode }` — `StatKind` covers the six `PlayerStats` fields plus a seventh, `Hp`, that doesn't correspond to a `PlayerStats` field at all (see "Equipping and stat recompute" below for where that goes instead). `StatMode` is `Flat` or `Mult`. An item can carry any number of these — the Helmet seed (`item/seeds.rs`) carries three (`Defense +5`, `Vitality +3`, `Hp +15`), all `Flat`.
- `ItemBehavior::Weapon(WeaponBehavior)` / `ItemBehavior::Consumable(ConsumableBehavior)` — `WeaponBehavior` is fire-rate/range/projectile/pattern data `LocalPlayerCombat` reads to drive shooting (covered below); `ConsumableBehavior` is `{ effect: ConsumableEffect, potency, duration }` where `ConsumableEffect` is `Heal` or `Buff(ConsumableBuffEffect)`.

The reason this is two independent `Vec`s instead of one `item_type` enum branching into a side-table (`Weapon`, `Armor`, `Artifact`, ... each with its own columns) is procedural generation: a slot-keyed schema can't express "an accessory that shoots," because *what an item does* is welded to *which table its row lives in*. With slot and behavior split, `equip_slot: Accessory, behaviors: [Weapon(..)]` is exactly as valid a row as any seeded item — nothing in `recompute_stats`, `compute_player_damage`, or the client's behavior lookups cares which slot an item is sitting in, only whether it's *equipped* (see below) and what its `behaviors` contain. No procedural generator exists yet — today every `Item` row is hand-seeded in `seed_world_items` (Bow, Bread, Hat, Helmet, Skull, Bag) or written by an admin via `upsert_item` — but the schema is already shaped for one. See [[tables.rs##pub struct Item|Item]].

`all_items` (`item/views.rs`) is the one client-facing view onto this table — unfiltered, since the item catalog is the same for every connected client regardless of login state (the same category [[01 Architecture & Sync Model]] puts `all_textures` in). `GameManager` caches it in a single `Dictionary<string, Item>` (`itemCache`, keyed by `item_id`) behind `GetItem(itemId)` — there's no per-item-type cache to keep in sync, matching the server's single-table design. See [[GameManager.cs##public Item\? GetItem|GetItem]].

## Inventory: 24 fixed slots, typed by whitelist

`PlayerInventory.slots` is a `Vec<InventorySlot>` of a fixed length (24), and each `InventorySlot { allowed_slots: Vec<EquipSlot>, item_id: Option<String> }` carries its own whitelist rather than the array index alone meaning anything — a slot is "the weapon slot" only because `try_scaffold_profile` built it with `allowed_slots: vec![EquipSlot::Weapon]`, not because it's index 0 in some hardcoded sense the rest of the code re-derives. The layout `try_scaffold_profile` writes ([[03 Player System]] covers scaffolding generally; this is what it does specifically for inventory):

| Indices | Slot helper | `allowed_slots` | Starting item |
|---|---|---|---|
| 0 | `weapon_slot` | `[Weapon]` | Bow |
| 1–4 | `hotbar_slot` ×4 | `[Consumable]` | Bread in slot 1 |
| 5–8 | `accessory_slot` ×4 | `[Accessory]` | Hat in slot 5 |
| 9–12 | `armor_slot` ×4 | `[Armor]` | Helmet in slot 9 |
| 13–14 | `artifact_slot` ×2 | `[Artifact]` | Skull in slot 13 |
| 15–22 | `general_slot` ×8 | `[Weapon, Artifact, Armor, Accessory, Consumable]` | (empty) |
| 23 | `bag_slot` | `[Bag]` | Bag |

The 8 general slots (`GENERAL_SLOT_START = 15`, `GENERAL_SLOT_COUNT = 8`, `main/global.rs`) are the only ones whose whitelist names more than one `EquipSlot` — everywhere else in the code, "is this slot equipped" is tested as `allowed_slots.len() == 1` (see `recompute_stats` below), so a general slot holding an item is deliberately never treated as "equipped" for stat purposes even though the item physically sits in the inventory array. See [[methods.rs##fn weapon_slot|weapon_slot]] and [[reducers.rs##pub fn try_scaffold_profile|try_scaffold_profile]] (in [[03 Player System]]'s scope).

Three reducers mutate this array, all under `require_in_world` and all ending with `recompute_stats`:

- **`swap_slots(from, to)`** — the drag-and-drop path. Whichever of the two slots holds an item has that item's `equip_slot` checked against the *other* slot's `allowed_slots` — checked in both directions, since a swap (unlike a one-way move) can carry an item either way. Rejecting, say, dragging the Skull artifact into an armor slot is this check, not a client-side filter — `InventorySlotUI`'s drag/drop (`_GetDragData`/`_CanDropData`/`_DropData`) accepts any drag and lets the server's `Err` be the actual gate. See [[reducers.rs##pub fn swap_slots|swap_slots]].
- **`drop_item(slot_index)`** — right-click in the backpack (`InventorySlotUI._GuiInput`, gated to `inBackpack` so hotbar/equipment slots outside the backpack panel don't respond to right-click). Clears the slot and inserts a `LootDrop`; see "Loot drops" below.
- **`pickup_drop(drop_id)`** / **`use_item(slot_index)`** — covered in their own sections below, since both also touch `Item.behaviors` rather than just the inventory array.

Two admin-only reducers round out `item/reducers.rs` but aren't part of the moment-to-moment loop: `give_item`/`remove_item` (find a target player's profile by username, insert/clear an `item_id` in an empty general slot or matching slot) and `upsert_item` (insert-or-update a whole `Item` row — this is how a hand-authored item, including the "accessory that behaves like a weapon" case above, gets into the catalog today, absent a generator). See [[reducers.rs##pub fn give_item|give_item]].

## Equipping and stat recompute

`recompute_stats(ctx, profile_id)` (`player/methods.rs`) is where an item sitting in a slot actually becomes a number. It:

1. Iterates `inv.slots`, skipping any slot whose `allowed_slots` doesn't name exactly one `EquipSlot` — this is the "is this slot equipped, and not a general/bag slot" test — resolves the held `item_id` against the `Item` table, and folds every entry in `item.stat_modifiers` into a per-`StatKind` `(flat, mult)` accumulator (7 entries: the six `PlayerStats` fields plus `Hp`).
2. Folds every one of the profile's `ActiveConsumableEffect.modifier` rows into the same accumulator — an equipped Helmet and a timed Strength potion are the same kind of input to this fold, not two separate code paths.
3. Resolves each stat as `(base + flat) * (1 + mult)`, where `base` comes from [[03 Player System]]'s level formula (`compute_base_stats`/`compute_base_max_hp`) — so two `+10% Mult` sources add to `+20%`, not `1.1 × 1.1 = +21%`. Nothing seeded today actually uses `Mult` (every seed is `Flat`), but the accumulation already treats it as additive-percentage rather than compounding, which is the harder-to-retrofit choice to get right early.
4. Writes the six resolved values to `PlayerStats` (insert-or-update), and separately resolves the `Hp` accumulator against `compute_base_max_hp` to get the new `max_hp` — clamping current `hp` down to it if the cap just shrank (unequipping a +HP item mid-session, or a buff expiring). This is the only place `StatKind::Hp` goes; it was seeded on the Helmet (`+15`) years before `recompute_stats` had anywhere to put it, so the value only became live once this fold existed.

Every inventory-mutating reducer above (`swap_slots`, `drop_item`, `pickup_drop`, `use_item`) calls this at the end, and so does `tick_active_consumable_effects` (below) whenever a buff actually expires — there's no separate "stats are stale" flag; a slot or buff change is always immediately followed by a full recompute rather than a partial patch. See [[methods.rs##pub fn recompute_stats|recompute_stats]].

Combat math reads the same catalog independently of `recompute_stats`: `compute_player_damage` pulls whatever's in slot 0 and looks for a `Weapon` behavior via the small `weapon_behavior(item)` helper (a `behaviors.iter().find_map`, not a dedicated weapon table lookup), then scales its `damage` by the resolved `strength` stat. `compute_incoming_damage` reads `defense` the same way to scale down an attack's base damage. Both are [[06 Combat & Damage Math|06's]] concern for the formulas themselves; what matters here is that they're reading `Item.behaviors`/`PlayerStats`, the same two things this doc's recompute produces. See [[methods.rs##pub fn compute_player_damage|compute_player_damage]].

## Weapon and consumable behaviors, client-side

The client never re-implements `recompute_stats` or the damage formulas — it only mirrors the *behavior lookup* pattern where it needs the same shape of data:

- **`LocalPlayerCombat`** resolves the equipped weapon once per `InventoryChanged` signal (not once per shot): it reads `LocalPlayer.EquippedWeapon` (slot 0's `Item`), scans `Behaviors` for `ItemBehavior.Weapon(var weapon)`, and caches the resulting `WeaponBehavior` — comparing the new item's `ItemId` against the previously-cached one first, so swapping an unrelated accessory doesn't reset the weapon's fire-rate timer (`_fireTimer`) or firing pattern. `WeaponBehavior.Pattern` (`Single`/`Triple`/`Cluster`) picks between three firing methods that build angle spreads and hand bullets off to `BulletManager`; `zone_count`/`range` separately drive melee-style "damage zone" `Area2D`s spaced along each shot's path, which is what `ReportEnemyHit` actually keys off rather than the bullet visual itself. See [[LocalPlayerCombat.cs##private void OnInventoryChanged|OnInventoryChanged]].
- **`use_item`**'s only gate, server-side, is whether the held item has a `Consumable` behavior at all — not whether it's sitting in a `Consumable`-typed slot. That's a deliberate widening consistent with the slot/behavior split above: any item carrying a `Consumable` behavior is usable from wherever it sits. Today only `Bread` exercises this (seeded with a `Heal` behavior, `equip_slot: Consumable`), so the widening isn't yet visibly different from a slot check, but the guard is written as a behavior test on purpose. See [[reducers.rs##pub fn use_item|use_item]].

## Loot drops

`LootDrop` (`player/tables.rs`) is a flat, `public` table — `drop_id`, `item_id`, `x`/`y`/`chunk_index`, `expires_at`, `dropped_by`. The only reducer that inserts one today is `drop_item` (above); nothing on the enemy side inserts a `LootDrop` on a kill yet, so "enemies drop loot" is aspirational, not implemented — [[05 Enemy AI & Bullet Patterns|05's]] concern once that exists.

Clients never subscribe to `LootDrop` directly — `nearby_loot_drops` (`player/views.rs`) filters it through the exact same `nearby_indices`/chunk-ring AOI helper [[02 World & Hex Grid]] documents for terrain and [[03 Player System]] documents for remote players, so a drop only becomes visible once it's within `DEFAULT_AOI_CHUNK_RADIUS` chunks. `GameManager.OnDropInsert`/`OnDropDelete` spawn or free one `Drop` node per row. `Drop._Ready` textures itself via the same `GetItem`→`GetResPath` chain everything else in this doc uses, and — purely client-side, with no server backing — refuses pickup collisions from its own dropper for `PickupLockDuration` (5s) so the same body standing on the tile it just dropped from doesn't immediately re-trigger `pickup_drop`. See [[Drop.cs##public override void _Ready|_Ready]].

`pickup_drop` prefers an empty slot whose `allowed_slots` matches the item's `equip_slot` over the general slots, falling back to the first empty general slot only if no typed slot is free — so picking up a second helmet with all four armor slots full still lands it in a general slot rather than failing outright, as long as one of the 8 general slots is open. See [[reducers.rs##pub fn pickup_drop|pickup_drop]].

## Known gaps and stubs

- **Loot drops never expire.** `drop_item` stamps every `LootDrop` with `expires_at = now + LOOT_DROP_EXPIRY` (300s), but no scheduled reducer anywhere reads that field or deletes expired rows — unlike `ActiveConsumableEffect`, which has `tick_active_consumable_effects` doing exactly this. A dropped item sits on the ground indefinitely today.
- **No enemy-death loot.** `LootDrop` and `pickup_drop` are fully wired for player-initiated drops, but nothing in `enemy/reducers.rs` inserts a `LootDrop` on a kill — the table and pickup flow are ready for it, but "enemies drop loot when they die" doesn't exist in the code yet.
- **`Bag` has no behavior.** It occupies its own equip slot and its own scaffolded slot (23), but carries no `stat_modifiers` or `behaviors` — there's no bag-capacity mechanic (e.g. extra general slots) implemented; equipping a different bag today would be cosmetically inert.
- **No procedural item generator.** The `stat_modifiers`/`behaviors` split exists specifically to make one possible (see "The unified `Item` catalog" above), but every `Item` row today is either hand-seeded (`seed_world_items`) or hand-authored via the admin-only `upsert_item` reducer.
- **`server/spacetimedb/src/CLAUDE.md` describes a migration that has already shipped.** That file frames the `stat_modifiers`/`behaviors` split documented above as a not-yet-implemented target design, with a step-by-step migration plan off an older five-side-table schema (`Weapon`/`Armor`/`Artifact`/`Accessory`/`Consumable` tables joined by `item_type`). Reading the actual `item/tables.rs`, `player/methods.rs`, and the client's `GameManager`/`LocalPlayer` confirms the migration is complete — single `Item` table, single `itemCache`, `ResolvedSlot { Slot, Item }` on the client. Treat that file as stale planning history, not current state.
- **`server/CLAUDE.md`'s reducer list for `player/` names `update_inventory` and `gain_xp`, neither of which exists as a reducer today.** Inventory mutation happens through `swap_slots`/`drop_item`/`pickup_drop`/`use_item` instead; XP gain is `internal_gain_xp`, a plain function in `player/methods.rs` called from `enemy/reducers.rs` on a kill; there is no `gain_xp` reducer a client calls directly. Same caution [[01 Architecture & Sync Model]] already flags for that file's `views.rs` claim — the per-file breakdown drifts from the code faster than this narrative doc will.

## Where to go next

[[05 Enemy AI & Bullet Patterns]] is the natural next read — it's the other side of `WeaponBehavior`'s `PatternType`-shaped counterpart on the enemy side, and the doc that will need to answer whether/how enemy deaths ever populate `LootDrop`. [[06 Combat & Damage Math]] picks up `compute_player_damage`/`compute_incoming_damage` where this doc left them (reading `Item.behaviors` and `PlayerStats`) and covers the actual damage formulas and `report_hit`/`report_enemy_hit` flow this doc only named in passing.
