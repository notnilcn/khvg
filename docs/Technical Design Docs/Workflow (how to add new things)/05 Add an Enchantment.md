# 05 Add an Enchantment

Enchantments are `stat_modifiers` with an `allowed_slots` restriction — nothing more. They can never grant behaviors, only adjust numbers ([[05 Item, Equipment & Enchantment System|05]]). **Data-only.**

## 1. (Optional) icon

Enchantments have a `texture_id` like items, but no seeded enchantment textures exist yet — the UI degrades fine without one. If you want an icon, follow [[01 Add a Texture or Sprite]] and seed a `TextureEntry` whose id matches the `enchantment_id`.

## 2. Add the `Enchantment` row

**Seed it** in `seed_world_enchantments` ([[item/seeds.rs##pub fn seed_world_enchantments|seed_world_enchantments]]), following `vampiric`:

```rust
Enchantment::seed(ctx, Enchantment {
    enchantment_id: "molten".to_string(),
    display_name: "Molten".to_string(),
    description: "Imbues with volcanic heat.".to_string(),
    texture_id: "molten".to_string(),
    stat_modifiers: vec![
        StatModifier { stat: StatKind::Strength, amount: 4.0, mode: StatMode::Flat },
        StatModifier { stat: StatKind::Defense, amount: 0.05, mode: StatMode::Mult },  // +5%
    ],
    allowed_slots: vec![EquipSlot::Weapon, EquipSlot::Artifact],
});
```

**Or upsert at runtime** (admin, [[00 Index & The Build Loop|00]]):

```bash
spacetime call --server local bullethell upsert_enchantment '{"enchantment_id":"molten","display_name":"Molten","description":"...","texture_id":"molten","stat_modifiers":[{"stat":{"strength":{}},"amount":4.0,"mode":{"flat":{}}}],"allowed_slots":[{"weapon":{}},{"artifact":{}}]}'
```

Field notes:

- `allowed_slots` — matched against the **item's** `equip_slot`, not the inventory slot index it happens to sit in ([[player/reducers.rs##pub fn apply_enchantment|apply_enchantment]]). `"swift"` allows everything; `"vampiric"` is artifact-only.
- `mode: Mult` values stack additively with other `Mult` sources on the same stat: two +5% give +10%, not +10.25% ([[player/methods.rs##pub fn recompute_stats|recompute_stats]]).

## 3. Verify

1. `bash build.sh` for seeds (immediate for upserts). The enchantment catalog is part of the always-on base subscription wave, so every client's `CatalogComponent` cache sees the new row at once and fires `EnchantmentsChanged` — open socket UIs refresh reactively.
2. In game: hover an equipment item (slot 0 or 5-14) whose `equip_slot` is in your enchantment's `allowed_slots` and that has a free socket (`max_enchantments` not reached) — the item sidebar lists your enchantment with a Socket button.
3. Socket it; the stats sidebar reflects the modifier immediately (`apply_enchantment` ends in `recompute_stats`).

## Gotchas

- **UI gating is narrower than server rules.** The Socket button only appears when the item sits in slot 0 or 5-14 (`LocalPlayer.IsEquipmentSlot`), even though the server reducer would allow any slot whose item qualifies. Inert today (only equipment items have `max_enchantments > 0`), but surprising if you make an enchantable consumable ([[05 Item, Equipment & Enchantment System|05]]).
- **Sockets are per inventory slot, not per item**, and `swap_slots` doesn't move `enchantment_ids` with the item — dragging an enchanted item detaches its enchantments onto the vacated slot ([[05 Item, Equipment & Enchantment System|05]] Known gaps).
- **Duplicates are rejected** per slot, and the cap is the item's own `max_enchantments` — the enchantment row has no say in either; both are checked server-side in `apply_enchantment`.
- No level-scaling, no rarity, no cost — an enchantment's entire effect is its `stat_modifiers` list. Anything richer (e.g. on-hit effects) would need the `ItemBehavior` mechanism enchantments deliberately don't have.
