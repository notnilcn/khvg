# Item system: split stats from behavior

> **Status: implemented.** This was `server/spacetimedb/src/CLAUDE.md` — a plan doc that lived where it
> auto-loaded on every server edit long after it shipped. The schema it describes is live
> (`item/tables.rs`, `player/methods.rs::recompute_stats`); `server/CLAUDE.md` carries the durable
> design rules. Kept here for the *why* behind the shape, and for the follow-ups at the bottom that
> are still open. Details drifted since: `recompute_stats` now also folds socketed enchantment
> modifiers, and `ConsumableBuffEffect` survived as a seed-time input converted by `buff_to_modifier`.

## Context

`server/spacetimedb/src/CLAUDE.md` documents a target design for the item system that has never been
implemented. Today an item is a row in `Item` plus a row in one of five parallel side tables
(`Weapon`, `Armor`, `Artifact`, `Accessory`, `Consumable`), joined by a shared string id and
dispatched on `Item.item_type`. That structure is closed: what an item *does* is welded to which
slot it occupies, so an accessory that fires projectiles is not expressible without hacking it into
the `Weapon` table. The goal is procedurally generated items, which requires slot and behavior to be
independent axes.

The fix, per the design doc, is one row per item carrying two generic vectors — `stat_modifiers:
Vec<StatModifier>` and `behaviors: Vec<ItemBehavior>` — with `equip_slot` describing only where it
goes. "Accessory that works like a weapon" becomes `equip_slot: Accessory, behaviors: [Weapon(..)]`.
This is the same `Vec<enum>`-on-one-row shape already used by `EnemyTemplate.phases`
(`enemy/def_tables.rs`) and `PlayerInventory.slots` (`player/tables.rs`).

Decisions taken with the user: full cut through server *and* client (nothing left half-migrated);
implement both `Flat` and `Mult` modes; add `Hp` to `StatKind` and actually wire it up; no
procedural generator in this change — schema only.

`bullet build` wipes data on every publish, so this is a hard cut with no dual-write or migration
path.

## Changes

### 1. `server/spacetimedb/src/item/tables.rs` — the new schema

Delete the `Weapon`, `Armor`, `Artifact`, `Accessory`, `Consumable` tables and the `ArtifactEffect`
enum. Keep `WeaponPattern`, `ConsumableEffect`, `ConsumableBuffEffect` as behavior payloads.

Rename `ItemType` → `EquipSlot` (same variants: `Weapon`, `Artifact`, `Armor`, `Accessory`, `Bag`,
`Consumable`). The name now means "where it goes", which is the whole point of the split — leaving it
called `ItemType` would preserve exactly the type/behavior conflation being removed.

```rust
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum StatKind { Strength, Wisdom, Dexterity, Defense, Vitality, Speed, Hp }

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum StatMode { Flat, Mult }

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct StatModifier { pub stat: StatKind, pub amount: f32, pub mode: StatMode }

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct WeaponBehavior {
    pub damage: u32, pub range: f32, pub fire_rate: f32, pub projectile_speed: f32,
    pub shot_count: u32, pub zone_count: u32, pub pierce: bool,
    pub projectile_texture_id: String, pub pattern: WeaponPattern, pub spread_angle: f32,
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct ConsumableBehavior { pub effect: ConsumableEffect, pub potency: f32, pub duration: f32 }

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ItemBehavior { Weapon(WeaponBehavior), Consumable(ConsumableBehavior) }

#[table(accessor = item, public)]
pub struct Item {
    #[primary_key] pub item_id: String,
    pub display_name: String,
    pub description: String,
    pub texture_id: String,
    pub equip_slot: EquipSlot,
    pub stat_modifiers: Vec<StatModifier>,
    pub behaviors: Vec<ItemBehavior>,
}
```

Note `ArtifactEffect` disappears entirely — it was `Strength(f32) | Wisdom(f32) | ...`, which is
exactly `StatModifier` with `mode: Flat`. It was a stat, not a behavior, so it does not become an
`ItemBehavior` variant. The doc's example listing `ArtifactEffect(..)` as a behavior predates that
realization; collapsing it into `stat_modifiers` is the same design carried one step further, and is
what makes the Skull and the Hat the same kind of thing.

### 2. `item/seeds.rs` — one seed function

Delete `seed_weapon`/`seed_armor`/`seed_artifact`/`seed_accessory`/`seed_consumable` and the five
`impl Seed for <SideTable>` blocks. Keep `impl Seed for Item`. `seed_world_items` builds `Item` rows
directly — with `Vec` fields the per-type helpers no longer save anything.

Preserve current values exactly (they are the regression baseline):

- **Helmet** — `Armor`, mods `[Defense +5, Vitality +3, Hp +15]` (all Flat)
- **Hat** — `Accessory`, mods `[Wisdom +2, Dexterity +2, Speed +2]` (strength_bonus was 0, drop it)
- **Skull** — `Artifact`, mods `[Strength +5]`
- **Bag** — `Bag`, no mods, no behaviors
- **Bread** — `Consumable`, behaviors `[Consumable(Heal, potency 25, duration 0)]`
- **Bow** — `Weapon`, behaviors `[Weapon(dmg 10, range 150, rate 4, speed 300, shots 3, zones 25,
  pierce false, "Arrow", Triple, 0.35)]`

### 3. `item/views.rs` — six views become one

Delete `all_weapons`/`all_armor`/`all_artifacts`/`all_accessories`/`all_consumables`. Keep
`all_items`. The join they existed to serve no longer exists.

### 4. `item/reducers.rs`

`upsert_item` already takes a whole `Item` and needs no change. `give_item`/`remove_item` touch only
`item_id` strings — no change.

### 5. `player/tables.rs`

- `InventorySlot.allowed_types: Vec<ItemType>` → `allowed_slots: Vec<EquipSlot>`.
- `ActiveConsumableEffect.effect: ConsumableBuffEffect` → `modifier: StatModifier`. This is the same
  unification: an active buff and an equipped artifact are both "a stat modifier that applies right
  now", and `recompute_stats` should not have two code paths summing the same six stats.
- Update the import on line 2.

### 6. `player/methods.rs` — the real work

`recompute_stats` (lines 48–122) currently has three near-duplicate blocks (armor / accessory /
artifact) plus a fourth for active buffs. Collapse to one accumulation over `StatModifier`:

- Iterate equipped slots, resolve `Item`, and fold `item.stat_modifiers` into a per-`StatKind`
  `(flat, mult)` accumulator. Chain `ActiveConsumableEffect.modifier` into the same fold.
- Resolve as `base + flat`, then `* (1 + sum_of_mults)` — additive percentages, not compounding
  products, so two `+10%` sources give `+20%` rather than `+21%`. Nothing uses `Mult` yet; this
  fixes the stacking rule before anything depends on it.
- `StatKind::Hp` does **not** belong in `PlayerStats` (no field for it). Write its resolved value
  into `PlayerData.max_hp` as `BASE_MAX_HP + hp_flat`, scaled by mult, and clamp `hp` to the new
  `max_hp` so a player cannot sit above their cap after unequipping. **This is a live bug fix** —
  Helmet's `+15` HP is seeded today and read by nothing.

Two behavior changes to be aware of, both intended:

- The "is this an equipped slot" test is `allowed_types.len() != 1 { continue; }` (line 62). Keep the
  same rule against `allowed_slots`, but note it now means **any** equipped item contributes its
  modifiers, including the weapon in slot 0 and the bag in slot 23. Neither has modifiers today, so
  no value changes — but a weapon with stat mods will now work, which is the point.
- Artifact effects were `f32` truncated via `v as i32`. Accumulating in `f32` and rounding once at
  the end is the same for current whole-number seeds.

Then:

- `weapon_slot`/`hotbar_slot`/`accessory_slot`/... (lines 124–130) — mechanical rename of
  `allowed_types` → `allowed_slots`, `ItemType::X` → `EquipSlot::X`.
- `compute_player_damage` (lines 179–190) — replace the `ctx.db.weapon().weapon_id().find(..)` join
  with a scan of `item.behaviors` for `ItemBehavior::Weapon(w)`. Add a small helper
  (`fn weapon_behavior(item: &Item) -> Option<&WeaponBehavior>`) since the client mirrors this lookup
  and `use_consumable` needs the same shape for consumables.
- `apply_consumable_effect` (lines 197–212) — take `&ConsumableBehavior` instead of `&Consumable`;
  the `Buff` arm inserts a `StatModifier` rather than a `ConsumableBuffEffect`.

### 7. `player/reducers.rs`

- `use_consumable` (~line 250) — the `item.item_type != ItemType::Consumable` guard plus the
  `ctx.db.consumable().find(..)` join collapse into a single "does this item have a `Consumable`
  behavior?" check. **Deliberate widening:** anything carrying a consumable behavior is usable,
  regardless of `equip_slot`. That is the "accessory that acts like a weapon" requirement applied to
  consumables, and it is what makes the guard a behavior test rather than a slot test.
- `pickup_drop` (~line 160) and `update_inventory` (~line 212) — `s.allowed_types.contains(&item.item_type)`
  → `s.allowed_slots.contains(&item.equip_slot)`.
- `tick_active_consumable_effects` — no logic change; field is `modifier` now.

### 8. Regenerate bindings

```
spacetime generate --lang csharp --out-dir client/Scripts/module_bindings --module-path server/spacetimedb
```

Do not hand-edit the output.

### 9. Client

`client/Scripts/Game/GameManager.cs` — delete `weaponCache`/`armorCache`/`artifactCache`/
`accessoryCache`/`consumableCache` (lines 38–42), their twelve `OnInsert`/`OnUpdate`/`OnDelete`
handlers (174–192), the five `.AddQuery(q => q.From.AllWeapons())`-style subscriptions (197–201), and
the five `GetWeapon`/`GetArmor`/... accessors (378–382). `itemCache`, `AllItems`, and `GetItem`
survive — one cache, one subscription, one lookup. (The surviving caches have since moved to
`client/Scripts/Components/Catalog/CatalogComponent.cs`; `GameManager` is now a facade over it.)

`client/Scripts/Players/Local/LocalPlayer.cs` — `ResolvedSlot` (lines 7–17) currently holds five
mutually-exclusive nullable side-table refs of which at most one is ever non-null. It collapses to
`{ InventorySlot? Slot; Item? Item; }`, and `ResolveSlotAt` (204–220) to a single `GetItem` call —
the five conditional resolutions were the join, and the join is gone.

The typed slot properties (`EquippedWeapon`, `HotbarSlots`, `AccessorySlots`, `ArmorSlots`,
`ArtifactSlots`, lines 52–64) return side-table types that no longer exist. Since consumers only ever
want "the `Item` in slot N", replace `GetSlotItem`/`BuildTypedSlots` with `Item?`-returning
equivalents and make `EquippedWeapon` an `Item?`. Delete the commented-out `Bag` line (69) rather
than porting it.

`client/Scripts/Components/Weapon/CombatComponent.cs` (was `Players/Local/LocalPlayerCombat.cs`) —
`_weapon` becomes the `WeaponBehavior` pulled
out of the equipped `Item`'s `behaviors` (mirroring the server helper from step 6). `OnInventoryChanged`
(122–131) compares `WeaponId` for change detection; use the equipped `Item.ItemId` instead. Field
reads inside `Fire`/`FireSingle`/etc. (133–158) are unchanged once `_weapon` is a `WeaponBehavior` —
the field names carry over.

`client/Scripts/Components/Inventory/ItemSidebarComponent.cs` (was `Players/Local/ItemStatsSidebar.cs`) —
reads only `resolved.Item`, so it compiles
unchanged. It is the natural place to later render `stat_modifiers`, but that is not this change.

`Components/Inventory/InventoryComponent.cs` (was `LocalPlayerInventory.cs`) and `Items/Drop.cs` reference slots/ids only — no change.

## Verification

1. **Compiles clean:** `cargo check --manifest-path server/spacetimedb/Cargo.toml`. The `mod item;`
   mismatch means this fails *before* any of the above — confirm it passes once the rename lands, so
   later failures are attributable to the redesign.
2. **Publish with a wipe:** `spacetime publish bullethell --clear-database -y --module-path server/spacetimedb`,
   then `spacetime logs bullethell` for seed errors.
3. **Regenerate bindings and build the client** in Godot — a clean build is what proves step 9 found
   every consumer of the deleted types.
4. **Run (F5, `Scenes/main.tscn`) and check the baseline held.** A fresh profile spawns with Bow /
   Bread / Hat / Helmet / Skull / Bag. Against pre-change values at level 1 (base 10 across the
   board), the sidebar should read **Strength 15** (Skull +5), **Wisdom 12** / **Dexterity 12** /
   **Speed 12** (Hat), **Defense 15** (Helmet +5), **Vitality 13** (Helmet +3). All six identical to
   before — the redesign must be stat-neutral.
5. **The one intended difference:** `max_hp` should now be `BASE_MAX_HP + 15` with the Helmet
   equipped, and drop back to `BASE_MAX_HP` when it is unequipped, with current `hp` clamped down.
   That is the `hp_bonus` bug being fixed; it is the only number expected to move.
6. **Exercise the paths that changed shape:** fire the Bow (weapon behavior lookup), eat the Bread
   (consumable behavior + heal), drag items between slots (`allowed_slots` filter — verify the Skull
   is still rejected by an armor slot), and pick up a drop (`pickup_drop` slot matching).
7. **Prove the point of the exercise:** via `upsert_item`, define an item with
   `equip_slot: Accessory` and `behaviors: [Weapon(..)]`, equip it in an accessory slot, and confirm
   `compute_player_damage` picks it up. This is the requirement the old schema could not express, and
   it is worth confirming before adding a generator on top.

## Follow-ups (explicitly not in this change)

- The procedural generator reducer — the schema above is what makes it writable.
- `Bag` capacity as a `BagBehavior { extra_slots }` variant; `Bag` is currently a slot with no
  behavior, and `PlayerInventory.slots` is a fixed 24.
- Rendering `stat_modifiers` in `ItemSidebarComponent` (was `ItemStatsSidebar`), which now has generic data to display.
- Per-stat btree-indexed tables if an auction-house-style query ever lands — the doc's caveat.
