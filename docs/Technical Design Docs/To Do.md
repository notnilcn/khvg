# Don't send individual player bullets, only send the enemy that the player is targeting
Every time a player toggles auto-fire they send their timestamp modulo (modulus is equal to server tick rate).

When players land hits on an enemy, they only send the enemy id.
# To confirm: Run Time Authoring.
Run time authoring is defining enemies after the server has been launched (i.e. outside of seeds).

# Near miss detection


# Item & Ability System — Current State vs Design Doc 03

Analysis of `docs/Game Design Docs/03 Class system, Item system, and Equipment system.md` against the implementation in `server/spacetimedb/src/` and `client/Scripts/`.

---

## 1. How it works today

### Server — catalog (`item/`)

- One unified `Item` table (`item/tables.rs:64`): `item_id`, `equip_slot` (Weapon/Armor/Accessory/Bag/Consumable/Ability), `slot_cost`, `stat_modifiers: Vec<StatModifier>`, `behaviors: Vec<ItemBehavior>`, `max_enchantments`. No per-type side tables.
- **Slot and behavior are independent axes**: `equip_slot` says where it goes; `behaviors` says what it does. Behaviors: `Weapon(WeaponBehavior)` (damage/range/fire_rate/projectile_speed/shot_count/zone_count/pierce/pattern/spread), `Consumable(ConsumableBehavior)` (Heal or Buff + potency + duration), `Ability(AbilityBehavior)` — which **reuses the consumable Heal/Buff shapes** plus `cooldown_seconds` and `max_charges`.
- `Enchantment` table: stat modifiers + `allowed_slots` only. **Enchantments are pure stats** — no behavior axis, no innate enchantments.
- Catalog is seed data (`item/seeds.rs`); admin upsert reducers mutate it at runtime.

### Server — inventory & stats (`player/`)

- `PlayerInventorySlot`: one row per slot, 38 slots per profile: `0` weapon, `1–4` hotbar, `5` accessory, `6` armor, `7–30` backpack, `31` bag, `32–37` ability. A slot's acceptance set derives from its `SlotRole` (`slot_allowed`); backpack accepts everything.
- **Multi-slot abilities**: an ability item with `slot_cost` N occupies a head row (holds the item + runtime state) and N−1 follower rows marked `occupied_by = head`. Runtime state per slot: `cooldown_until`, `charges`, `active_toggle`. `pickup_drop` auto-equips ability items into the first free span; `swap_slots` has region-based span handling; dropping a follower drops the whole span.
- **`recompute_stats`** (`player/methods.rs:90`) is the single stat-resolution point: folds item + socketed-enchantment modifiers from equipped heads (Weapon/Accessory/Armor at 1.0; **Ability heads scaled by `ABILITY_POSITION_MULTIPLIERS` [1.5, 1.25, 1.1, 1.0, 0.9, 0.8]** — "first ability stronger" is implemented) plus active consumable buffs, resolving `(base + flat) * (1 + mult)` with additive mults.
- `activate_ability`: validates cooldown + charges, applies the Heal/Buff effect (potency scaled by head position), sets cooldown, decrements charges. Item not consumed. `use_item`: consumes a consumable, same effect application. `apply_enchantment`/`remove_enchantment`: socket management with `allowed_slots` + `max_enchantments` checks. `set_slot_toggle`: writes `active_toggle`.

### Server — combat (`combat/mod.rs`)

- Outgoing: `compute_player_damage` = weapon `damage` × (1 + strength × 0.002), looked up from the equipped weapon head. **Each client `report_enemy_hit` applies the full weapon damage** — no division by shot_count, no per-bullet math, **enemies have no defense stat**, no rate/range validation on hit reports.
- Incoming: `compute_incoming_damage` = base × (1 − defense × 0.002), min 1. `deal_damage_to_player` handles death (teardown → lobby).
- **Stat usage: only Strength and Defense appear in any formula.** Vitality, Wisdom, Dexterity are computed but unused. Speed is mirrored to the client but never used server-side (movement is client-authoritative; `report_movement` accepts any x,y).

### Client

- `CatalogComponent` caches `AllItems`/`AllEnchantments`/`AllTextures`; everything resolves items through it.
- `LocalPlayerInventoryComponent` mirrors slot rows into a dictionary, exposes typed slot groups, raises `InventoryChanged`.
- `inventory_panel.tscn` + `SlotComponent` (drag/drop → `SwapSlots`, right-click backpack → `DropItem`, click ability slot → `ActivateAbility`), `InventoryComponent` (hotbar keys 1–4 → `UseItem`, **ability keys → `ActivateAbility` with span-head resolution**), `ItemSidebarComponent` (item details, socket/unsocket → `ApplyEnchantment`/`RemoveEnchantment`).
- `CombatComponent` fires the equipped weapon's `WeaponBehavior` (Single/Triple/Cluster patterns), spawning BlastBullets visuals + `HitZone`s along each bullet path; zone contact → `ReportEnemyHit`. **It never reads `ActiveToggle`.**
- `BulletControllerComponent` (delete/split/attract enemy bullets) is fully networked (optimistic local + `ControlBullets` reducer → `BulletControlEvent` fanout) but bound to **debug hotkeys "until a real ability system exists"**.

### What's genuinely good

- The slot/behavior split is the right foundation — it already makes "an accessory that fires projectiles" expressible and is the stated prerequisite for procedural generation.
- Multi-slot ability spans (the doc's integer-composition n=6 design) are implemented faithfully, including order-matters position scaling in both stats and potency.
- Per-slot rows with runtime state are clean; enchantments survive drop/pickup round-trips.

---

## 2. Gaps vs the design doc

| Doc promise                                                                                                      | Status                                                                                                                                                                                                                       |
| ---------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Classes emerge from gear locked behind **stat thresholds**                                                       | **Missing** — `Item` has no requirement field; nothing gates equipping. Base stats are +1-all per level with no allocation, so requirements can't bind yet.                                                                  |
| **Weapon toggles** replace the swap-out meta (the doc's headline mechanic)                                       | **Dead scaffold** — `active_toggle` + `set_slot_toggle` exist, but weapons have a single `WeaponBehavior`, no client ever calls `SetSlotToggle`, and neither `CombatComponent` nor server damage read the toggle.            |
| Toggle tradeoff: low-count/high-damage vs high-count/low-damage vs enemy defense                                 | **Can't exist** — enemies have no defense; every reported bullet hit deals full weapon damage, so more bullets is strictly better. There is no tradeoff to toggle between.                                                   |
| Enchantments that modify shot patterns / ability activation ("ring doubles bullets", armor buffs on ability use) | **Missing** — enchantments are stat-only.                                                                                                                                                                                    |
| **Innate** enchantments (no slot cost, unremovable — e.g. Extreme-Prejudice toggle)                              | **Missing** from schema.                                                                                                                                                                                                     |
| Rich abilities: spires, beams, meteors, zones, teleports, decoys, shields, bullet destruction                    | **Placeholder** — abilities are only self-targeted Heal/Buff. `activate_ability` takes no target. The one real spatial ability that exists (bullet control) sits on debug keys.                                              |
| Stats define the class space                                                                                     | **Weak** — Wisdom/Dexterity/Vitality are decorative; Speed is client-side only (and unenforced). Note: the doc lists only Dex/Str/Wis + temperaments; the schema has 6 stats and no temperament axis — the two have drifted. |
| Temperament stats (dps/supporter/artisan)                                                                        | **Absent** — the doc itself never says where temperament points come from; mechanically unspecified.                                                                                                                         |
| Warrior tattoos as consumables that overwrite an equipped tattoo                                                 | Not modeled (consumables can't target equipment).                                                                                                                                                                            |
| Artisan/crafting endgame loop                                                                                    | Entirely absent (expected at this stage).                                                                                                                                                                                    |
| Doc: tomes/scriptures cost 3, spells cost 2                                                                      | Seed drift: "Ember Scripture" has `slot_cost: 2`. Trivial.                                                                                                                                                                   |

## 3. Bugs / exploits found (independent of the doc)

1. **Charge-refill exploit**: charges deplete permanently, but swapping an ability to the backpack and back re-initializes them (`swap_slots` re-init on region entry). Either recharge over time or preserve charges across re-equips.
2. **Cooldown UI never recovers**: `InventoryComponent.UpdateSlotIcon` dims on cooldown only when an inventory row event fires; expiry produces no event, so icons stay dim until an unrelated inventory change. No charge counter or toggle indicator either.
3. `use_item` accepts any slot index — a client can eat consumables straight from the backpack (hotbar gating is client-side only).
4. `apply_enchantment` doesn't require an equipment slot — you can socket enchantments onto backpack items where they silently do nothing.
5. Clicking a passive ability item (e.g. the seeded Skull, which has no `Ability` behavior) fires `ActivateAbility` and eats a reducer error; the client should check for the behavior first.
6. `LocalPlayer.SpeedPerStat = 10f` vs `RemotePlayer.SpeedPerStat = 4f` — remote puppets extrapolate at the wrong speed (masked by interpolation, still wrong).
7. `report_enemy_hit` has no rate/range/LoS validation — full weapon damage per call, callable on any enemy from anywhere. Known-early posture, but it caps how much the damage formula can matter before anti-cheat catches up.

---

## 4. What I'd change, in order

**Tier 1 — make the built systems actually function (small, high value):**

1. **Wire weapon toggles end-to-end.** Minimal path that reuses the existing schema: an item's `behaviors` Vec can already hold multiple `Weapon(...)` entries — treat `active_toggle` as the index. Server: `set_slot_toggle` validates `value < count`; `compute_player_damage` reads the toggle-indexed behavior. Client: a toggle key calls `SetSlotToggle`; `CombatComponent.OnInventoryChanged` also keys off `ActiveToggle` and fires the indexed behavior. Seed one two-pattern bow (3-shot spread ↔ 1-shot heavy) to prove it.
2. **Give stats teeth** (prerequisite for stat-gated classes meaning anything): Dexterity → fire-rate multiplier in `CombatComponent` + server-side fire validation later; Vitality → folds into max HP; Wisdom → ability potency/cooldown scale. And enforce Speed in `report_movement` (max-displacement check per 10 Hz tick) so the client-authoritative movement has a server bound.
3. **Enemy defense + per-bullet damage** so the toggle tradeoff can exist at all: `EnemyTemplate` gains `defense`; `compute_player_damage` divides by the pattern's `shot_count` and subtracts defense per bullet hit (RotMG model, min 1). This one formula change creates the entire high-count/low-damage vs low-count/high-damage axis the doc revolves around.

**Tier 2 — the doc's item depth (schema work):**

4. **Behavior enchantments + innate enchantments**: `Enchantment` gains a `behaviors` axis (shot-pattern modifiers like shot-count multiplier, on-ability-use triggers, toggle grants); `Item` gains `innate_enchantment_ids` (always on, no slot, unremovable). `CombatComponent` composes pattern modifiers when firing; `activate_ability` fires triggers. This is the Extreme-Prejudice fantasy and the "lower-tier beats higher-tier" goal.
5. **A real `AbilityEffect` enum** instead of reusing `ConsumableEffect`: keep Heal/Buff, add targeted spatial effects with a `target` param on `activate_ability` (cursor world pos, server-validated). First concrete effects: wire the **already-networked** bullet delete/split/attract (`BulletControllerComponent` + `ControlBullets`) as genuine ability behaviors — cheapest path from placeholder to real.
6. **Stat requirements on items**: `Item.stat_requirements: Vec<StatModifier>` (reuse the shape), enforced in `swap_slots`/`pickup_drop` equip paths, shown in the sidebar. This is the class system.

**Tier 3 — hardening & UX:** fix the charge exploit (recharge over time or persist), role-guard `use_item`/`apply_enchantment`, cooldown sweep + charge count + toggle indicator in the slot UI, passive-ability click guard, align the two `SpeedPerStat` constants, scripture `slot_cost` 3 per doc.

**Open questions for you (not implemented either way):**
- Where do temperament points (dps/sup/artisan) come from — allocated at level-up, derived from play, or pure flavor? The doc says "work exactly like normal stats" but never defines the source.
- Is stat *allocation* a thing (doc says "optimal builds and stat allocations") or is gear the only build choice? Today stats are level-derived, identical for everyone.
- The doc says the stats are Dex/Str/Wis; the schema has six. Which list is canonical?

---

## 5. Proposed execution scope (pick at approval)

- **Option A — Analysis only.** No code changes; take the list and decide later.
- **Option B — Tier 1.** Weapon toggles end-to-end (multi-`Weapon`-behavior indexing, server + client + seed), stat teeth (Dex/Vit/Wis/Speed enforcement), enemy defense + per-bullet damage. Server: `cargo check`; client: `dotnet build`; you verify in-game.
- **Option C — Tier 1 + Tier 2.** Adds behavior/innate enchantments, the `AbilityEffect` rework with bullet-control as the first real effects, and stat requirements. Bigger schema cut (remember: every publish wipes the DB anyway, so hard cuts are free).
