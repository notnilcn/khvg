# Don't send individual player bullets, only send the enemy that the player is targeting
Every time a player toggles auto-fire they send their timestamp modulo (modulus is equal to server tick rate).

When players land hits on an enemy, they only send the enemy id.

# (stupid) i$\omega$aginary numbers for the hex chunks
![](https://www.youtube.com/watch?v=d6agN416onM&t=492s)

# Claude analysis on what to do:
I read the design doc, the six server modules, and the client's item/inventory/combat/bullet paths. Here's what I'd change.

The short version

The item schema refactor (see `(implemented) Item system - split stats from behavior.md`) was the right move — `Vec<StatModifier>` + `Vec<ItemBehavior>` on one row is genuinely the shape this doc
needs, and behaviors being a `Vec already makes weapon toggles almost free. But the doc's central claim — "classes are defined by gear + stat requirements"
— has zero representation in the schema right now, and three of the ability categories (zones, statuses, cooldowns) have no subsystem at all.

---
1. Blocking gaps — the doc's premise doesn't compile today

Stats aren't allocatable. compute_base_stats (player/methods.rs:43) returns 10 + level-1 for all six stats. Every character at a given level is
stat-identical, so no distribution exists to sort players into classes. Everything else in the doc is downstream of fixing this: you need level-granted
points plus an allocate_stat reducer, and PlayerStats should split primary (str/wis/dex — player-assigned) from derived (defense/vitality/speed — gear-
and formula-driven). Right now they're one flat row (player/tables.rs:52) that treats them identically.

Temperament doesn't exist. dps/sup/art aren't in StatKind (item/tables.rs:21) or PlayerStats. Mechanically identical to normal stats, so this is cheap —
but it has to land before item requirements, since the requirements are what make temperament mean anything.

Items have no stat requirements. This is the entire class-gating mechanism and there's no field for it. Add requirements: `Vec<StatRequirement>` to Item and
enforce in swap_slots (player/reducers.rs:186-197), which today only checks equip_slot.

▎ ⚠️ Design trap worth deciding now: gear grants stats, and stats gate gear. If requirements are checked against final stats you get equip-order
▎ dependence and unequip cascades (remove the Skull, and the sword you needed +5 str for is suddenly illegal — does it pop off? does that cascade?). RotMG
▎ checks against base stats only. I'd do the same: check compute_base_stats + allocated, never gear-inclusive. Otherwise recompute_stats (methods.rs:90)
▎ has to become a fixpoint loop.

No item instances. "Borderlands item system" means per-instance rolled modifiers. Today an item is its catalog row — Item.item_id: String — and the only
per-instance state anywhere is enchantment_ids on the slot (player/tables.rs:9) and on LootDrop (:122). A weapon that rolled 12 damage instead of 10 is
inexpressible. You need an ItemInstance { instance_id, item_id, rolled_modifiers, innate_enchantment_ids, socketed_enchantment_ids } table, with slots and
drops holding instance_id.

This is the widest-blast-radius change in the list — recompute_stats, swap_slots, use_item, pickup_drop, drop_item, compute_player_damage, plus client
LocalPlayer.ResolveSlotAt, InventoryComponent, SlotComponent, Drop.cs. Do it before building anything else on top of item_id, because every feature below
adds another item_id reader.

---
2. Refactors that unblock the ability system

PlayerInventory.slots should stop being one `Vec in one row. Six reducers do inv.slots.clone() and rewrite the whole `Vector. More importantly, ability
slots need per-slot runtime state — cooldowns, charge, active toggle index, multi-slot occupancy spans — and none of that belongs in a value type
replicated wholesale on every swap. Promote to PlayerInventorySlot keyed (profile_id, slot_index) with a btree index.

That also kills this:

// player/methods.rs:112
if slot.allowed_slots.len() != 1 { continue; }

That's "is this slot equipped?" inferred from `Vector length. It's already fragile, and it silently breaks the first time a slot accepts two kinds — which
ability slots will. Replace with an explicit role: SlotRole field.

Ability slots don't exist. EquipSlot (item/tables.rs:11) has no Ability variant. Beyond adding one, the multi-slot rule (shield 6, tome 3, spell 2) needs
Item.slot_cost: u32 and a contiguous-span placement algorithm in swap_slots — that's new logic, not a tweak, and it interacts with pickup_drop's
auto-equip search (reducers.rs:130-137). The "order matters, first ability stronger" rule wants a position→multiplier curve as a global.rs constant.

No activation path. There's no activate_ability reducer, no cooldown state, no resource. use_item (reducers.rs:225) consumes the item and clears the slot
— that's consumables, not abilities. Another reason for the per-slot table.

---
3. Weapon toggles are nearly free — do this early

Item.behaviors is already `Vec<ItemBehavior>`, so two Weapon(...) variants on one item is already legal today. What's missing is only:

┌───────────────────────────────────────┬────────────────────────────────────────────────────────────────────────┐
│                 Where                 │                                 Change                                 │
├───────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ player/methods.rs:76                  │ weapon_behavior returns the first Weapon match — take an index instead │
├───────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ client/.../CombatComponent.cs:134-138 │ same first-match-and-break — same fix                                  │
├───────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ per-slot state                        │ active_behavior_index (needs §2's slot table)                          │
├───────────────────────────────────────┼────────────────────────────────────────────────────────────────────────┤
│ new reducer                           │ set_weapon_toggle(slot_index, behavior_index)                          │
└───────────────────────────────────────┴────────────────────────────────────────────────────────────────────────┘

That's the doc's headline mechanic (the swap-out replacement) for maybe a day of work, and it's the cheapest thing on this list relative to design value.

---
4. Merge player shot patterns into the enemy pattern system

This one I'd push hardest on. You have two parallel bullet systems:

- Enemies: PatternType (enemy/def_tables.rs:21) — parameterized Ring/Volley/Curtain/Shotgun/Explosion, fully data-driven, with a complete client
dispatcher at BulletSpawnerComponent.cs:85-94.
- Players: WeaponPattern { Single, Triple, Cluster } (item/tables.rs:4) — a closed 3-variant enum with a hardcoded switch at CombatComponent.cs:152-163
and bespoke FireTriple/FireCluster methods.

The doc's flagship weapon — S.T.A.F.F., "10 bullets in a circle" — is literally Ring, and you already implemented it, for the wrong faction. Replace
WeaponPattern + shot_count + spread_angle with PatternType and route player fire through the same spawn methods (they're already parameterized; only the
spawner config object and collision layer differ). Move PatternType out of enemy/def_tables.rs into something shared like combat/patterns.rs since it
stops being enemy-specific.

Payoff: new weapon patterns become data, addable via upsert_item with no client update. That's precisely the property the doc says it wants for classes
("players wouldn't have to update their clients"). Under the current design, every new class weapon is a client patch.

---
5. Damage model can't express the doc's core tension

Two of three stats do nothing. compute_player_damage (combat/mod.rs:35-46) is weapon.damage * (1 + strength * 0.002). Wisdom and dexterity are inert — a
wizard and a warrior with the same staff deal identical damage. Give WeaponBehavior a scaling: `Vec<(StatKind, f32)>` so staves scale off wis and guns off
dex. Dexterity should also feed fire_rate, which is currently weapon-only and client-side (CombatComponent.cs:141). Also: 0.002 and 0.002 (:43, :51)
belong in global.rs with every other tunable.

Enemies have no defense. deal_damage_to_enemy (combat/mod.rs:75) subtracts raw damage; EnemyTemplate has no defense field. Consequence: "higher defense
disproportionately punishes low-bullet-count builds" — the stated reason the toggle meta exists — is currently false in your game. High-count and
low-count builds are mathematically identical, so the toggle from §3 is a decision with no correct answer. Armor break, expose, and the entire tank kit
are also gated on this. Mirror compute_incoming_damage's mitigation on the enemy side.

Also worth noting: compute_player_damage hardcodes inv.slots.get(0) as the weapon slot. Once slot roles exist (§2), that should be a role lookup.

---
6. Status effects need to become a pipeline, not a stat bag

Acti`VeconsumableEffect (player/tables.rs:94) is a StatModifier + a float remaining, decremented at 1 Hz. Now scan the doc's ability list: invulnerability,
immunity to the next single instance of damage, convert incoming damage to bleed, store pre-mitigated damage and release it as a smite, redirect party
damage to self, armor break, stagger bar, "can't drop below 1 HP". Almost none of these are stat modifiers.

Two changes:

1. Generalize to a StatusEffect table with an EffectKind enum, source_profile_id (needed for redirect/distribute), and a stacking rule. Use expires_at:
Timestamp checked on read rather than a ticked remaining — 1 Hz is far too coarse for "invulnerable for 2 seconds" in a bullet hell.
2. Turn deal_damage_to_player into an ordered pipeline. It's currently straight-line mitigate→subtract (combat/mod.rs:56-70). "Store all pre-mitigated
damage" and "convert incoming damage to bleed" intercept at different points, so you need explicit stages: pre-mitigation hooks → mitigation →
post-mitigation hooks → apply → death hooks.

The good news is combat/mod.rs is already the correct single chokepoint — it just needs to become a pipeline. Retrofitting this after abilities ship means
rewriting every caller.

---
7. Missing subsystem: world zones

Roughly half the support kit is "a circle on the ground that does X" — heal-over-time, burst heal, damage amp, slow, damage-over-time, no-death zone,
damage redistribution, mushroom-tome spire, wizard spire, decoy, shield wall. Nothing in the schema represents a persistent world-space effect volume.

You need a WorldZone table (position, radius, chunk_index for AOI, owner, effect, expires_at), a scheduled tick applying effects to entities inside, and a
client component to render it. It follows the existing LootDrop pattern closely (AOI-filtered, expiring, chunk-indexed), so the shape is familiar.

This is mostly independent of the item refactor and can be built in parallel — it's the largest missing subsystem after abilities themselves.

---
8. Enchantments are too weak, and too coarsely scoped

Enchantment (item/tables.rs:71-80) is stat modifiers + allowed_slots. The doc wants enchantments that flip shot patterns and grant status effects.

- Give Enchantment a behaviors: `Vec<ItemBehavior>` — same field as Item. Behavior resolution then folds both sources, exactly as recompute_stats already
folds both stat sources (methods.rs:115-124).
- Innate enchantments that don't eat a slot are per-instance, so they land on ItemInstance (§1) as a separate `Vec not counted against max_enchantments
(reducers.rs:261).
- allowed_slots: `Vec<EquipSlot>` is too coarse. A gun-only enchantment is unexpressible because there's no weapon subtype — EquipSlot::Weapon covers
swords, staves, wands, and guns alike. Add Item.tags: `Vec<String>` (or an ItemCategory enum) and let enchantments require tags. Tags also give you the
per-class weapon taxonomy the doc's class writeups assume — without a schema change per class, which is the doc's stated goal.

---
9. The inventory layout is hardcoded in three places

- Server: try_scaffold_profile (player/methods.rs:174-188) + GENERAL_SLOT_START = 15 (global.rs:50)
- Client: LocalPlayer's index-range properties and IsEquipmentSlot (LocalPlayer.cs:67-85)
- Client: InventoryComponent's exported slot arrays and per-slot SlotIndex baked into inventory_panel.tscn

Adding 6 ability slots shifts every index after 14 and requires editing all three plus the scene. Before adding ability slots, make the client read
role/allowed_slots off the server rows instead of hardcoded ranges. Otherwise every future layout change ships as a client update — again, the exact thing
the doc is trying to avoid.

---
10. Small cleanups

- ConsumableBuffEffect (item/tables.rs:37-44) is a redundant parallel of StatKind, already mechanically converted by buff_to_modifier (methods.rs:64).
Delete it; put StatModifier directly in ConsumableEffect::Buff.
- StatKind exists in three places (server enum, client Resources/Stats/StatKind.cs, generated bindings). Adding temperaments touches all three —
unavoidable, just budget for it.
- Player bullets are client-spawned with server-side damage on report_enemy_hit. Making patterns data-driven (§4) widens what a modified client can claim,
but doesn't change the trust model you already accepted. Worth a conscious decision rather than a drift.
- Artisan temperament has no implementation surface whatsoever (no crafting, gathering, or economy tables). Fine as out-of-scope, but the doc treats it as
one of three co-equal temperaments, so it'll need its own pass.

---
Suggested order

1. Item instances (§1) — widest blast radius, everything else adds readers of item_id
2. Per-slot table + slot roles (§2) — unblocks abilities, toggles, cooldowns
3. Stat allocation + temperaments + item requirements (§1) — makes the class system actually exist
4. Weapon toggles (§3) — cheap, high design value, validates §1+§2
5. Enemy defense + stat scaling (§5) — makes §4's toggle a real decision
6. Merge shot patterns (§4) — turns weapons into data
7. Status effect pipeline (§6) — do before writing any ability, not after
8. World zones (§7) — parallelizable with 1–6
9. Data-driven client inventory layout (§9) — must precede adding ability slots to the UI

Items 1, 2, and 6 are the ones where doing them later costs materially more than doing them now.

# To confirm: Run Time Authoring.
Run time authoring is defining enemies after the server has been launched (i.e. outside of seeds).


# Near miss detection
