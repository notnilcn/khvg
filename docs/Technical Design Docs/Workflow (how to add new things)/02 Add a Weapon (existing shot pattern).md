# 02 Add a Weapon (existing shot pattern)

A weapon is not a special kind of thing — it's an `Item` row whose `behaviors` contains `ItemBehavior::Weapon(WeaponBehavior)` ([[item/tables.rs##pub struct WeaponBehavior|WeaponBehavior]]). If your weapon fires in one of the three existing patterns (`Single`, `Triple`, `Cluster`), this is **data-only: no client code, no server logic changes**. Background: [[05 Item, Equipment & Enchantment System|05]], [[07 Combat & Damage Math|07]].

## 1. Register the textures

You need two: the weapon's **icon** (inventory UI) and its **projectile** (what flies). Follow [[01 Add a Texture or Sprite]] — icon `.tres` under `client/Textures/Items/Weapons/`, projectile under `client/Textures/Attacks/` with a `default` animation, then add both `TextureEntry` seeds in `main/seeds.rs` ([[main/seeds.rs##pub fn seed_default_textures|seed_default_textures]]). Reusing an existing projectile texture (e.g. `"Arrow"`) is fine and common.

## 2. Add the `Item` row

**Seed it:** add an `Item::seed` block to `seed_world_items` in `item/seeds.rs` ([[item/seeds.rs##pub fn seed_world_items|seed_world_items]]), using the Bow ([[item/seeds.rs##item_id: "Bow"|Bow]]) as the template:

```rust
Item::seed(ctx, Item {
    item_id: "Crossbow".to_string(),
    display_name: "Crossbow".to_string(),
    description: "A heavy crossbow. Slow, single powerful bolts.".to_string(),
    texture_id: "Crossbow".to_string(),          // the icon
    equip_slot: EquipSlot::Weapon,
    stat_modifiers: vec![],                       // weapons can also grant stats
    behaviors: vec![ItemBehavior::Weapon(WeaponBehavior {
        damage: 25,
        range: 250.0,
        fire_rate: 1.5,                           // shots per second
        projectile_speed: 400.0,
        shot_count: 1,                            // used by Triple/Cluster
        zone_count: 25,                           // hit-zone probes along the path; 0 disables firing!
        pierce: false,                            // UNUSED — reads nowhere, see gotchas
        projectile_texture_id: "Arrow".to_string(),
        pattern: WeaponPattern::Single,           // Single | Triple | Cluster
        spread_angle: 0.0,                        // radians; used by Triple/Cluster
    })],
    max_enchantments: 2,
});
```

What each `WeaponBehavior` field actually drives (client-side, `CombatComponent` — [[CombatComponent.cs##private void Fire|Fire]]):

- `fire_rate` → fire period `1 / fire_rate` seconds.
- `range` + `zone_count` → the invisible `HitZone` train: `zone_count` probes spaced `range / zone_count` apart, each timed to detonate when the visual bullet would arrive. **`zone_count: 0` silently disables the weapon** (`CombatComponent.cs:139`).
- `damage` → base for `compute_player_damage` ([[combat/mod.rs##pub fn compute_player_damage|compute_player_damage]]): `damage * (1 + strength * 0.002)`, min 1.
- `pattern` → which `Fire*` method runs; `shot_count`/`spread_angle` only matter for `Triple` (even fan) and `Cluster` (random spread + randomized range/speed per pellet).
- `projectile_speed` → visual bullet speed and hit-zone timing.

**Or upsert it at runtime** (admin — [[00 Index & The Build Loop|00]]):

```bash
spacetime call --server local bullethell upsert_item '{"item_id":"Crossbow","display_name":"Crossbow","description":"...","texture_id":"Crossbow","equip_slot":{"weapon":{}},"stat_modifiers":[],"behaviors":[{"weapon":{"damage":25,"range":250.0,"fire_rate":1.5,"projectile_speed":400.0,"shot_count":1,"zone_count":25,"pierce":false,"projectile_texture_id":"Arrow","pattern":{"single":{}},"spread_angle":0.0}}],"max_enchantments":2}'
```

## 3. Verify

1. `bash build.sh` (seed path only; the upsert path is live immediately).
2. Give it to yourself: `spacetime call --server local bullethell give_item <username> <profile_name> Crossbow` (admin).
3. Drag it into **slot 0** in the inventory UI. Left-click fires; the stats sidebar reflects any `stat_modifiers` immediately (`recompute_stats` runs on every equip change).

## Gotchas

- **Only inventory slot 0 fires.** Both the server damage path ([[combat/mod.rs##pub fn compute_player_damage|compute_player_damage]] reads `inv.slots.get(0)`) and the client firing path (`LocalPlayer.EquippedWeapon`) hardcode slot 0. A weapon anywhere else is stat-padding only. The schema *allows* a non-weapon-slot item carrying a `Weapon` behavior (e.g. an offhand artifact that shoots), but nothing fires it today.
- **`pierce` is dead schema.** It's seeded and visible in bindings, but no code reads it ([[07 Combat & Damage Math|07]] Known gaps). Every `HitZone` already hits every enemy overlapping it.
- **Projectile orientation:** all player bullets get the global `π/4` texture offset meant for the arrow sprite — see [[01 Add a Texture or Sprite|01]].
- **Starter loadout:** new profiles always start with the Bow ([[player/methods.rs##pub fn try_scaffold_profile|try_scaffold_profile]]). To change what players start with, edit the hardcoded item ids there — and note `delete_profile` + permadeath both re-run this scaffold.
- **`swap_slots` doesn't carry enchantments** with the item ([[05 Item, Equipment & Enchantment System|05]] Known gaps) — dragging your enchanted weapon between slots silently detaches its sockets. Pick up/drop (`pickup_drop`/`drop_item`) does it correctly.
