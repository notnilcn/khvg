# 08 Item Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for the mirror/report split and `recompute_stats`, [[03 Connection, Subscriptions & Views]] for the Base-wave catalog subscription. The ability-authoring recipe (seed-only vs new effect type) below is the narrative companion to [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]]. For the rows abilities read and write at cast time, see `10 Player Tables` (slots, cooldowns, charges) and `07 Enemy Tables` (the event tables casts append).

## The 30-second version

The catalog is two static tables — `Item` (one unified row per item: where it goes, what it does, what it requires) and `Enchantment` (socketable/innate behavior + stat grants) — seeded at publish (every publish wipes the DB, so unseeded items don't exist) and cached wholesale by every client from the Base wave. Slot and behavior are independent axes: `equip_slot` says only where an item goes, `stat_modifiers` + `behaviors` say what it does — which is what makes "an accessory that fires projectiles" expressible. Weapons compose at use time through `resolve_effective_weapon` (toggle-selected pattern + enchantment fold, mirrored exactly by the client's `EffectiveWeaponResolver`), and abilities dispatch through the shared slot validation/dispatch/spend in the player module.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do I add a new ability item? | `Item` | [[#Item]] — reuse an `AbilityEffect`: seed-only; new effect type: + dispatch + client mirror |
| Where does an item go vs what does it do? | `Item` | [[#Item]] — `equip_slot` vs `behaviors` independence |
| How is the fired weapon composed? | `Item` | [[#Item]] — `resolve_effective_weapon`: toggle options + enchantment fold |
| What are toggle options / `active_toggle`? | `Item` | [[#Item]] — own `Weapon` behaviors + `WeaponToggle` grants, indexed by the slot |
| What do enchantments do? | `Enchantment` | [[#Enchantment]] — stat mods + behaviors; innate vs socketed |
| What are the class gates? | `Item` | [[#Item]] — `REQ_*` thresholds (primary ≥ 15, secondary ≥ 12) |
| Which abilities are hold-to-charge? | `Item` | [[#Item]] — `ability_is_charge_capable`: charge-scaled Teleport/Spell |
| How do I hand a player an item for testing? | `Item` | [[#Item]] — admin `give_item`; pre-equip via scaffold, not new machinery |

## Flowcharts

- [[flowcharts/main-item.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/item_subfolder/item_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Catalog_subfolder/Catalog_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Inventory_subfolder/Inventory_subfolder.canvas]]

## Table changes

```sync
![[00 Table Map#^table-item{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-enchantment{seamless:true,title:false,marker:02.}]]
```

(The entries above are the complete changer/reader reference. The sections below add Shape plus the composition mechanics the entries summarize.)

## Item

### Shape

One unified catalog row in [[server/spacetimedb/src/item/tables.rs#Item|tables.rs]]: string `item_id` key, display/description/texture, `equip_slot` (`Weapon` / `Armor` / `Accessory` / `Bag` / `Consumable` / `Ability` — artifacts are 1-cost abilities, there is no Artifact variant), `slot_cost` (1 for everything except multi-slot ability items: shield 6, tome 3, spell 2), `stat_modifiers`, `behaviors` (`Weapon` / `Consumable` / `Ability` — the same `Vec<enum>`-on-one-row shape as `EnemyTemplate.phases`), `max_enchantments`, `innate_enchantment_ids` (always applied while equipped, don't eat sockets, can't be removed), and `stat_requirements` (flat per-stat minimums, all-zero = none).

**Slot and behavior are deliberately independent axes.** Capability lookups are behavior-based — `ability_behavior` / `consumable_behavior` in [[server/spacetimedb/src/item/methods.rs#ability_behavior|methods.rs]] — never `equip_slot`-based, which is why `use_item` guards on *having a `Consumable` behavior* (plus the Hotbar role, matching the client UI) and rejects items carrying an `Ability` behavior, so a span head can never be consumed out from under its followers. There are no per-type side tables and no `ArtifactEffect` — an artifact "effect" is just a flat `StatModifier`.

### Changers

`seed_world_items` seeds the whole catalog (entry 01 above): starter gear (`Bow`, `Hat`, `Helmet`, `Bag`, `Bread`), the innate-toggle `Fan Bow` and two-pattern `Bow`, the P6 class-gated bullet-master toggle weapons (`Marksman Bow` / `Warbow`), the bullet-delete abilities (`Null Sigil` / `Delete Orb`), and the status/zone/movement/spell arsenal through the `ability_item` helper (single-effect, 1-slot, unlimited-charge seeds with the class gate as its last parameter). `Item::seed` upserts by key, so reseeding is idempotent. Runtime edits go through `upsert_item`; `remove_item` deletes a player's item (admin, `target_username` + `profile_name` resolved via `find_profile_by_name`); `give_item` hands items out the same way — all three route placement through the shared slot helpers (`get_slot` / `update_slot` / `clear_span`).

**Adding abilities:** reusing an existing `AbilityEffect` variant is seed-only (catalog entry + a `TextureEntry` if the texture is new). A new effect type additionally needs the variant on `AbilityEffect` (unit or single-field only — multi-field tuples fail with a misleading Serialize cascade), arms in `scale_ability_effect` (position-multiply every scalable dim) and `apply_ability_effect` (the shared dispatch both `activate_ability` and the charge release call), a `BulletControlKind` if bullet-controlling (prefer encoding geometry into existing event fields), the charge-capability decision in `ability_is_charge_capable`, and the client mirror (query/visual in `BulletControllerComponent`, echo + optimistic arms, `ItemSidebar` display line) with server/client fallbacks matching exactly.

### Readers

- ***Client*** — via the `all_items` view in the Base wave → `CatalogComponent` (`GetItem`) → sidebar composition, `EffectiveWeaponResolver` / `CombatComponent` firing, drop/chest sprites. The resolver mirrors `resolve_effective_weapon` exactly — toggle options (own `Weapon` behaviors first, then `WeaponToggle` grants from equipped enchantments), `active_toggle` indexing with end-clamp (removing a toggle-granting enchantment can strand the index), `AddShots`/`ShotCountMult` folded as `(shots + flat, min 1) × (1 + summed mults)` rounded min 1, true-damage/flat mods summed — in deterministic fold order (slot index ascending, innate before socketed), which is load-bearing on both sides.
- ***Server*** — `resolve_effective_weapon` feeds `compute_player_damage` (combat) and the `report_enemy_hit` rate cap; `weapon_toggle_options` validates `set_slot_toggle`; `check_stat_requirements` gates any item entering an equipped role from loose storage against the P6 `REQ_*` consts (primary ≥ 15, secondary ≥ 12 — modest above-`BASE_STAT`-10 thresholds; Paladin mirrors witch-doctor, Trickster ordering approximated); `ability_is_charge_capable` (charge-scaled `Teleport`/`Spell` only) routes casts to begin/release instead of `activate_ability`, mirrored by the client's `IsChargeCapable`.

## Enchantment

### Shape

One catalog row in [[server/spacetimedb/src/item/tables.rs#Enchantment|tables.rs]]: string key, display/description/texture, `stat_modifiers`, `behaviors` (`AddShots` / `ShotCountMult` / `FlatBulletDamageMod` / `TrueDamageFlat` / `TrueDamagePercent` / `OnAbilityUseBuff` / `WeaponToggle`), and `allowed_slots`. Mults are additive per project rule (two +10%s give +20%, never compounding); enchantment *behaviors* (unlike stat modifiers) are never ability-position-scaled.

### Changers

`seed_world_enchantments` (entry 02 above: `splitting` shot-doubler, `echoing` on-ability buff, innate-only `point_blank` / `scatter_fan`; seeded via `impl Seed for Enchantment` in `main/seeds.rs`) plus `upsert_enchantment`. No delete reducer — sockets are removed player-side via `remove_enchantment`. Runtime socket state lives on the slot row (`enchantment_ids`), never here: the catalog is shared and static.

### Readers

- ***Client*** — via the `all_enchantments` view in the Base wave → `CatalogComponent` (`GetEnchantment(s)`, `EnchantmentsChanged`, `CatalogVersion` bump on any row change) → socket/remove buttons and full composition lines in `ItemSidebar`; toggle grants fold into `EffectiveWeaponResolver` in the same deterministic order as the server.
- ***Server*** — `equipped_enchantment_behaviors` folds socketed **and innate** enchantments (Weapon/Accessory/Armor/Ability heads only — backpack/hotbar don't count) into `recompute_stats` and weapon resolution; `spend_ability` fires equipped `OnAbilityUseBuff` triggers on every cast.

## Files — pointer deep dive

Entry points: [[server/spacetimedb/src/item/pointers.md|item pointers]].

### methods.rs

Behavior lookups + weapon composition: `consumable_behavior` / `ability_behavior`, `ability_is_charge_capable`, `equipped_head_slots`, `equipped_enchantment_behaviors`, `weapon_toggle_options`, `resolve_effective_weapon` → `EffectiveWeapon`. Pure catalog-side reading (equipped slots only). Consumed by combat (`resolve_effective_weapon`), enemy reducers (hit-rate cap), player methods/reducers (capability checks, toggle validation), and status reducers (charge routing).

### mod.rs

Module declarations only.

### reducers.rs

Admin catalog reducers: `give_item` / `remove_item` (player-targeted, via `find_profile_by_username` + shared slot helpers) and `upsert_item` / `upsert_enchantment` (catalog edits). No client calls these except through admin flows.

### seeds.rs

`seed_world_items` / `seed_world_enchantments` plus the `REQ_*` class gates and the `ability_item` helper (see [[#Item]]). Called from `init` in `main/lifecycle.rs`. Content = seeds: anything not seeded doesn't exist after the publish wipe.

### tables.rs

`Item` / `Enchantment` plus the full behavior/param vocabulary (`WeaponBehavior`, `ConsumableBehavior`, `AbilityBehavior` + every `AbilityEffect` payload struct, `EnchantmentBehavior`, `StatKind` / `StatMode` / `StatModifier`, `StatRequirements`). Read across the module boundary by chest (item validation), player methods/reducers (every dispatch arm), and player tables (`StatModifier` on consumable rows).

### views.rs

`all_items` / `all_enchantments`: global views (no per-caller state) in the Base wave, bound by `CatalogComponent`'s replaying binders. Adding a view is how catalog data is exposed — the client never writes SQL.

## Cross-table flows

The cast pipeline spans three modules but each link lives on its own entry: slot validation + cooldown/charges (^table-player-inventory-slot) → position-scaled dispatch (`apply_ability_effect`) → bullet-control appends (^table-bullet-control-event), spell damage through the combat sink + visual relay (^table-ability-visual-event), self effects through `apply_consumable_effect` (^table-active-consumable-effect / ^table-active-status-effect), zone placement (^table-ground-zone), charge routing (^table-ability-charge), spend (`spend_ability` + enchantment triggers). No separate telling needed.

## Known gaps / stubs

- **Procedural item generation is not built.** The slot/behavior split is its prerequisite (and the rationale for the schema — see the implemented-design note in `server/AGENTS.md`), but no generator exists; all content is hand-seeded.
- **`Bag` capacity is not a behavior yet.** The `Bag` slot role exists and `slot_allowed` accepts it, but capacity-as-behavior (the designed extension point) is unimplemented.

## Where to go next

[[09 Main Tables]] covers the lifecycle/admin/seeds machinery that writes these catalog rows. For the slots that hold catalog items at runtime, read `10 Player Tables`.
