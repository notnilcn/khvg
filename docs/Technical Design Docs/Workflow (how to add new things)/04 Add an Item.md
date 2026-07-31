# 04 Add an Item (armor / accessory / artifact / bag)

Non-weapon, non-consumable items are pure stat sticks and socket holders — one `Item` row with `stat_modifiers`, an `equip_slot`, and (usually) an empty `behaviors` list. **Data-only.** For weapons see [[02 Add a Weapon (existing shot pattern)|02]]; for consumables see [[06 Add a Consumable or Ability|06]]. Background: [[05 Item, Equipment & Enchantment System|05]].

## 1. Register the icon texture

Follow [[01 Add a Texture or Sprite]] — `.tres` under the matching `client/Textures/Items/` subfolder (`Armor/`, `Accesories` *(sic — the seeded folder name is misspelled)*, `Artifacts/`, `Bags/`), plus a `TextureEntry` seed.

## 2. Add the `Item` row

**Seed it** in `seed_world_items` ([[item/seeds.rs##pub fn seed_world_items|seed_world_items]]), following the Helmet:

```rust
Item::seed(ctx, Item {
    item_id: "Shield".to_string(),
    display_name: "Shield".to_string(),
    description: "A tower shield. Greatly increases defense.".to_string(),
    texture_id: "Shield".to_string(),
    equip_slot: EquipSlot::Armor,      // where it's allowed to sit
    stat_modifiers: vec![
        StatModifier { stat: StatKind::Defense, amount: 8.0, mode: StatMode::Flat },
        StatModifier { stat: StatKind::Hp, amount: 25.0, mode: StatMode::Flat },
    ],
    behaviors: vec![],
    max_enchantments: 2,               // 0 = can never hold enchantments
});
```

**Or upsert at runtime** (admin, see [[00 Index & The Build Loop|00]]): `spacetime call --server local bullethell upsert_item '{...}'` with the same fields.

Field notes:

- `stat_modifiers` — `Flat` adds raw points; `Mult` adds a fraction (`0.05` = +5%), stacked additively with other `Mult` sources (`(base + flat) * (1 + mult)` in [[player/methods.rs##pub fn recompute_stats|recompute_stats]]). `StatKind::Hp` raises *max HP*, not a visible stat column.
- `equip_slot` — one of `Weapon/Artifact/Armor/Accessory/Bag/Consumable`. This is the *only* equip-legality rule in the game: reducers check the item's `equip_slot` against the destination inventory slot's `allowed_slots`. There is no class/level gate.
- `behaviors` — leave empty for a stat stick. The slot/behavior split means an armor piece *could* also carry a `Weapon` behavior and the schema wouldn't blink — but nothing fires non-slot-0 weapons today ([[02 Add a Weapon (existing shot pattern)|02]] gotchas).

## 3. Verify

1. `bash build.sh` for seeds (immediate for upserts).
2. `spacetime call --server local bullethell give_item <username> <profile_name> Shield` — lands in a type-specific free slot if one exists, else a general slot.
3. Equip it (drag to an armor slot, 9-12). The stats sidebar updates immediately — `recompute_stats` runs on every inventory mutation.

## Where items are allowed to live

The 24-slot layout is fixed and duplicated in two places — change it in both or they desync ([[04 Player System|04]] Known gaps):

| Slots | Contents | Server constructor | Client mirror |
|---|---|---|---|
| 0 | weapon | `weapon_slot` | `EquippedWeapon` |
| 1-4 | consumables (hotbar) | `hotbar_slot` | `HotbarSlots` |
| 5-8 | accessories | `accessory_slot` | `AccessorySlots` |
| 9-12 | armor | `armor_slot` | `ArmorSlots` |
| 13-14 | artifacts | `artifact_slot` | `ArtifactSlots` |
| 15-22 | general (any except Bag) | `general_slot` | `GeneralSlots` |
| 23 | bag | `bag_slot` | — |

Server: [[player/methods.rs##fn weapon_slot|slot constructors]] + [[player/methods.rs##pub fn try_scaffold_profile|try_scaffold_profile]]. Client: [[LocalPlayer.cs##public Item? EquippedWeapon|LocalPlayer.cs:66-85]].

## Gotchas

- **General slots reject `Bag`-type items** ([[player/methods.rs##fn general_slot|general_slot]] omits `EquipSlot::Bag`). A second bag with slot 23 occupied has nowhere to go and `pickup_drop` fails with "No suitable slot available".
- **Stat-folding vs. enchant-UI tests differ.** `recompute_stats` folds modifiers from *any* slot with exactly one allowed type (includes hotbar 1-4 and bag 23); the enchant-socketing UI only appears for slot 0 and 5-14 (`LocalPlayer.IsEquipmentSlot`). An item with `stat_modifiers` sitting in the hotbar still affects stats but shows no socket UI ([[05 Item, Equipment & Enchantment System|05]] Known gaps).
- **`max_enchantments: 0` is the design choice for bags/consumables**, not an oversight — nothing to socket into a loaf of bread.
- **Adding a new `EquipSlot` variant** is a code change, not data: the enum in [[item/tables.rs##pub enum EquipSlot|EquipSlot]], a new slot constructor + scaffold layout in `player/methods.rs`, the mirrored ranges in `LocalPlayer.cs`, and the inventory UI's exported slot arrays in `inventory_panel.tscn`. Keep the two slot-layout mirrors in sync.
