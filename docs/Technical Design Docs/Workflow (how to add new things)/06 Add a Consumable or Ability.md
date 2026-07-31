# 06 Add a Consumable or Ability

"Abilities" in the current codebase are consumable items: an `Item` row carrying `ItemBehavior::Consumable(ConsumableBehavior)`, used from the hotbar (slots 1-4) via `use_item` ([[player/reducers.rs##pub fn use_item|use_item]]). Two effect kinds exist — `Heal` (instant HP) and `Buff(...)` (timed stat modifier). Reusing those is **data-only**; a genuinely new *kind* of effect is a small server code change. Background: [[05 Item, Equipment & Enchantment System|05]] (`equip-8`/`equip-9`).

## Path A — a consumable using existing effects (data-only)

Seed an item in `seed_world_items` ([[item/seeds.rs##pub fn seed_world_items|seed_world_items]]), following Bread:

```rust
Item::seed(ctx, Item {
    item_id: "EnergyDrink".to_string(),
    display_name: "Energy Drink".to_string(),
    description: "Move 30% faster for 10 seconds.".to_string(),
    texture_id: "EnergyDrink".to_string(),   // register per [[01 Add a Texture or Sprite]]
    equip_slot: EquipSlot::Consumable,
    stat_modifiers: vec![],
    behaviors: vec![ItemBehavior::Consumable(ConsumableBehavior {
        effect: ConsumableEffect::Buff(ConsumableBuffEffect::Speed(3.0)),
        potency: 0.0,          // ignored for Buff — the effect payload carries the amount
        duration: 10.0,        // seconds; counted down by the 1s scheduled tick
    })],
    max_enchantments: 0,
});
```

The two effects:

- `ConsumableEffect::Heal` — heals `potency` HP instantly via `combat::heal_player` ([[combat/mod.rs##pub fn heal_player|heal_player]]), clamped to max HP. `duration` is irrelevant.
- `ConsumableEffect::Buff(ConsumableBuffEffect::X(amount))` — inserts an `ActiveConsumableEffect` row (`Flat` modifier of `amount` to stat `X`, one variant per stat except `Hp` — see [[item/tables.rs##pub enum ConsumableBuffEffect|ConsumableBuffEffect]]) and calls `recompute_stats` immediately, so the buff is live the instant it's consumed. `tick_active_consumable_effects` (1s schedule) decrements `remaining` and deletes at zero; only then are stats recomputed back down.

Either way, `use_item` **unconditionally consumes the item** — the slot's `item_id` is cleared. There's no cooldown, no multi-use, no non-consuming activatable.

Also works via `upsert_item` at runtime (admin — [[00 Index & The Build Loop|00]]).

## Path B — a new effect *kind* (code change)

Example: a damage-over-time effect, a full heal-to-max, a cleanse. Three touch points, all server-side:

1. **Add the variant** to `ConsumableEffect` in `item/tables.rs` ([[item/tables.rs##pub enum ConsumableEffect|ConsumableEffect]]):

```rust
pub enum ConsumableEffect {
    Heal,
    Buff(ConsumableBuffEffect),
    Regen(f32),   // new: hp per second
}
```

2. **Handle it** in `apply_consumable_effect` ([[player/methods.rs##pub fn apply_consumable_effect|apply_consumable_effect]]) — add a match arm. If the effect persists over time, insert an `ActiveConsumableEffect` row like the `Buff` arm does; note that row only carries a `StatModifier`, so a persistent non-stat effect (like regen) either needs encoding as a `StatKind::Hp` modifier (but `Hp` raises *max* HP — see gotchas) or an extension of the `ActiveConsumableEffect` schema.

3. **If it's a new buff stat**, extend `ConsumableBuffEffect` instead and add the arm to `buff_to_modifier` ([[player/methods.rs##fn buff_to_modifier|buff_to_modifier]]) — that's the simpler sub-case and needs no `apply_consumable_effect` change.

Then `bash build.sh` and use it from any seeded/upserted item. No client changes are needed — effects manifest through `PlayerStats`/`PlayerData` rows the client already mirrors.

## Verify

- Put the item in a hotbar slot (1-4), press the matching hotbar key (`Hotbar1`-`Hotbar4`) — `InventoryComponent` calls `UseItem` directly.
- Heal: watch HP jump (damage yourself first — the debug/admin `spawn_enemy` route works, or walk into a bullet).
- Buff: watch the stats sidebar for the duration, then watch it revert.

## Gotchas

- **No DoT exists today** — `ConsumableEffect` has only `Heal` and `Buff`, despite comments mentioning DoT ([[05 Item, Equipment & Enchantment System|05]] Known gaps). The 1s tick infrastructure is generic enough to extend.
- **`Buff` potency is ignored** — the amount lives inside the `ConsumableBuffEffect` variant. Only `Heal` reads `potency`.
- **All buffs are `Flat`.** `buff_to_modifier` hardcodes `StatMode::Flat`; there is no `Mult`-mode consumable buff in the schema.
- **`use_item` gates on behavior, not slot** — any item with a `Consumable` behavior is usable from anywhere it's sitting, but the client only binds hotbar keys to slots 1-4; using from elsewhere needs the UI (or a reducer call).
- **Buffs don't stack or refresh as identities** — each use inserts a separate `ActiveConsumableEffect` row; two Energy Drinks give two simultaneous +3 Speed rows that expire independently.
