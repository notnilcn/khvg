# 10 Inventory, Items & Enchantments

## Assumed knowledge

- [[02 The Component Framework]] — entities, `*Component` child nodes, and the `TableBinderComponent` row-event-to-signal mechanism every consumer here is fed by.
- [[03 Boot & Connection]] — the subscription waves, especially the base wave (`AllTextures`/`AllItems`/`AllEnchantments`) that delivers the catalogs this doc's client side caches.
- [[05 Joining the World]] — `try_scaffold_profile` creates the inventory rows at first join, and the `LocalPlayer` entity's components initialize from replayed rows.
- [[09 Combat & Damage]] — the effective weapon resolved here is what `CombatComponent` fires and `compute_player_damage` re-derives server-side; bullet-control abilities end up as the `BulletControlEvent` rows that doc owns.

## The 30-second version

Items and enchantments are static catalog rows (`Item`, `Enchantment`) seeded at publish and cached whole by every client; a player's inventory is one `PlayerInventorySlot` row per slot, delivered by the per-caller `LocalPlayerInventory` view. Everything the player does — drag/drop, hotbar use, ability activation, weapon-toggle cycling, socketing enchantments, allocating stat points, dropping and picking up loot — is a reducer call that rewrites slot rows server-side and ends in `recompute_stats`, the single function that folds allocated points, equipped-item modifiers, innate and socketed enchantments, and active consumable buffs into `PlayerStats`/`PlayerData`. The client never computes any of it: it mirrors rows into dictionaries and `Stat` resources, renders them in the inventory panel, and re-resolves its firing weapon through `EffectiveWeaponResolver`, an exact client mirror of the server's `resolve_effective_weapon`. Loot drops are `LootDrop` rows in the world, AOI-subscribed like everything else, spawned as `Drop` nodes and reclaimed through `pickup_drop`.

## Flowcharts

- [[flowcharts/main-items.canvas]] — this system's composed flow (composed from the `items` manifest in `flowcharts/flows.json`).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/item_subfolder/item_subfolder.canvas]] — the server `item/` module: the catalog tables and behavior enums, the seeds, the `all_items`/`all_enchantments` views, and the admin catalog reducers.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Inventory_subfolder/Inventory_subfolder.canvas]] — the client inventory components: slot mirror, panel, slot widgets, and both sidebars.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]] — the server `player/` module, home of `PlayerInventorySlot`/`ActiveConsumableEffect`/`LootDrop` and of every reducer this doc covers (`swap_slots` … `pickup_drop`) plus `recompute_stats` and the slot helpers.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^equip-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^equip-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^equip-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^equip-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^equip-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^equip-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^equip-7{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^equip-8{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^loot-1{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^loot-2{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^loot-3{seamless:true,title:false,marker:11.}]]
```

## Main body

### The catalog: one `Item` table, two independent axes

```sync
![[00 End-to-End Timeline Flowchart#^equip-1{seamless:true,title:false,marker:01.}]]
```

The whole schema is two tables in [[server/spacetimedb/src/item/tables.rs|item/tables.rs]], and the design decision that matters is what the `Item` row does *not* say. `equip_slot` (an `EquipSlot` enum: Weapon/Armor/Accessory/Bag/Consumable/Ability) answers only "which slot accepts this", while `stat_modifiers` (a `Vec<StatModifier>` — a `StatKind`, an amount, and a `StatMode` of Flat or Mult) and `behaviors` (a `Vec<ItemBehavior>`) answer "what it does". Because the two axes never reference each other, nothing stops an accessory from carrying a `Weapon` behavior or armor from carrying an `Ability` — capability lookups are behavior tests, never slot tests. That is why there are no per-type side tables to join, and why the server exposes three lookup helpers in [[server/spacetimedb/src/player/methods.rs#consumable_behavior#1|consumable_behavior]] / [[server/spacetimedb/src/player/methods.rs#ability_behavior#1|ability_behavior]] / [[server/spacetimedb/src/player/methods.rs#resolve_effective_weapon#1|resolve_effective_weapon]] that scan `behaviors` for the first matching variant instead of inspecting `equip_slot`. (They live in `player/methods.rs`, not the item module, because their only callers are the player reducers beside them — the item module deliberately has no `methods.rs`.)

The behavior payloads:

- `ItemBehavior::Weapon(WeaponBehavior)` — a complete firing pattern: `damage` (**per trigger pull, divided among the bullets** — the per-bullet value is `damage / shot_count`, which is what makes many-weak-bullets trade against few-strong-bullets under flat defense; see [[09 Combat & Damage]]), range, fire rate, projectile speed, shot/zone counts, pierce, texture, a `WeaponPattern` (Single/Triple/Cluster), and spread. One item can carry several — that's the toggle system below.
- `ItemBehavior::Consumable(ConsumableBehavior)` — a `ConsumableEffect` (`Heal` or `Buff(ConsumableBuffEffect)`, one variant per allocatable stat) with potency and duration. Eaten on use.
- `ItemBehavior::Ability(AbilityBehavior)` — an `AbilityEffect` (`Heal`/`Buff` self-effects, `DeleteBullets`/`SplitBullets`/`AttractBullets` radius effects, `SlashBullets(SlashParams)` — a length×width rectangle from the player toward the cursor) plus `cooldown_seconds` and `max_charges` (0 = unlimited). Not consumed on use.

`Enchantment` rows are the same idea one level down: `stat_modifiers` plus `behaviors` of a separate `EnchantmentBehavior` enum — shot/damage modifiers that compose onto the equipped weapon (`AddShots`, `ShotCountMult`, `FlatBulletDamageMod`, `TrueDamageFlat`, `TrueDamagePercent`), an `OnAbilityUseBuff` trigger, and `WeaponToggle(WeaponBehavior)`, which grants the equipped weapon an extra toggle pattern. `allowed_slots` restricts which `EquipSlot`s an enchantment may be socketed into. Two attachment modes exist: **socketed** enchantments live on the player's slot row (`PlayerInventorySlot.enchantment_ids`, capped by the item's `max_enchantments`, removable), while **innate** ones live on the catalog row (`Item.innate_enchantment_ids`) — always applied while the item is equipped, unremovable, and free of socket cost.

`StatKind` deserves its own sentence because it spans both sides: six **allocatable** stats (Strength/Wisdom/Dexterity plus the DamageDealer/Supporter/Artisan temperaments — mechanically identical to attributes) and three **modifier-only** kinds (Hp, Defense, BaseSpeed) that gear and buffs grant but players can never spend points into; they resolve onto `PlayerData` instead of `PlayerStats`. The client duplicates the enum in [[client/Scripts/Resources/Stats/StatKind.cs|StatKind.cs]] (the generated bindings define their own `SpacetimeDB.Types.StatKind` — first-party code qualifies when it needs that one).

All content is seeded at publish by [[server/spacetimedb/src/item/seeds.rs#seed_world_items#1|seed_world_items]] / [[server/spacetimedb/src/item/seeds.rs#seed_world_enchantments#1|seed_world_enchantments]] through the `Seed` trait's upsert, so a republish overwrites rather than duplicates. The seeded set is the entire item economy today: a stat-less two-pattern `Bow` (toggle option 0 a 3-arrow spread, option 1 a single heavy piercer — same 30 damage per pull, deliberately trading per-bullet strength against defense), the `Fan Bow` demonstrating an innate `point_blank` toggle enchantment, starter gear (`Helmet`/`Hat`/`Bag`/`Bread`), and ability items covering the span widths (`Skull` 1 slot, `Ember Scripture` 2, `Tome of Mending` 3) plus the bullet-control pair (`Null Sigil` delete, `Slash Orb` slash). Eight enchantments seed the modifiers, including `splitting` (double the bullets, −3 damage per bullet — the doc-02 tradeoff as content). Two honesty notes: every seeded item uses the all-zero `NO_REQUIREMENTS` (the class-threshold gate is enforced but gates nothing yet — see Known gaps), and the broader enchantment catalog the design docs imagine is aspirational and stays out of scope here, as does the un-built procedural item generator this schema was shaped for.

Server-side exposure is minimal by design: the anonymous views [[server/spacetimedb/src/item/views.rs#all_items#1|all_items]] / [[server/spacetimedb/src/item/views.rs#all_enchantments#1|all_enchantments]] hand the whole tables to every client (catalogs are public knowledge), and the only reducers in `item/reducers.rs` are admin tools — [[server/spacetimedb/src/item/reducers.rs#give_item#1|give_item]] / `remove_item` (targeted by username + profile name, span-aware) and the `upsert_item`/`upsert_enchantment` catalog writers (see [[12 Admin & Debug]]).

Client-side, [[client/Scripts/Components/Catalog/CatalogComponent.cs|CatalogComponent]] (declared inline in `game.tscn` with three child binders — `AllTexturesBinder`/`AllItemsBinder`/`AllEnchantmentsBinder`, all replaying) caches every catalog row into plain dictionaries behind `GetItem`/`GetEnchantment`/`GetEnchantments`/`GetResPath`, and fires `EnchantmentsChanged` on any enchantment-view change so the item sidebar can re-render its socket list. Everything else in the client reaches these through the `GameManager` facade's static pass-throughs ([[client/Scripts/Game/GameManager.cs#GetItem#1|GameManager.GetItem]] and siblings), so a texture or item lookup anywhere — slot icons, drop sprites, sidebar details — is a dictionary read, never a table scan. Note the wiring drift hazard: the standalone `Scenes/Components/Catalog/catalog_component.tscn` still exists on disk and the component's doc comment still names it, but the live wiring is the inline declaration in `game.tscn`.

### Inventory state: one row per slot

```sync
![[00 End-to-End Timeline Flowchart#^equip-2{seamless:true,title:false,marker:02.}]]
```

`PlayerInventorySlot` ([[server/spacetimedb/src/player/tables.rs|player/tables.rs]]) replaced a one-row `Vec<InventorySlot>` design, and the table comment says why: per-slot rows give ability slots a home for runtime state — `cooldown_until`, `charges` (`u32::MAX` = unlimited), `active_toggle`, and span occupancy — that has no business being replicated inside a value type on every swap, and they turn every mutation into a single-row update instead of an eight-reducer wholesale rewrite. Each row carries a `SlotRole` (Weapon/Hotbar/Accessory/Armor/General/Bag/Ability), and a slot's acceptance set is *derived* from its role by [[server/spacetimedb/src/player/methods.rs#slot_allowed#1|slot_allowed]]: every role accepts exactly its matching `EquipSlot`, except General (the backpack), which accepts **everything** — that's how loose ability items can sit in one backpack cell, because `slot_cost` only applies inside the ability region. The 38-row layout with its load-bearing indices (0 weapon, 1–4 hotbar, 5 accessory, 6 armor, 7–30 general, 31 bag, 32–37 ability) is constructed once by [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile#1|try_scaffold_profile]] at first join — including the pre-equipped Skull at 32 and the Tome of Mending spanning 33–35 — and mirrored as constants on the client (`AbilitySlotStart`/`AbilitySlotCount`/`BagSlotIndex` on `LocalPlayerInventoryComponent`, and `ABILITY_SLOT_START`/`ABILITY_SLOT_COUNT` in [[server/spacetimedb/src/main/global.rs|main/global.rs]]; both sides must change together).

**Spans** are how a 3-slot tome occupies 3-slot space: the *head* row holds the item and its runtime state, and the following `slot_cost − 1` rows are *followers* marked `occupied_by = Some(head_index)` — they never hold an `item_id` themselves. Three helpers in `player/methods.rs` do all span surgery: [[server/spacetimedb/src/player/methods.rs#find_free_span#1|find_free_span]] (first contiguous run of free ability cells), [[server/spacetimedb/src/player/methods.rs#place_span#1|place_span]] (writes head + followers, initializing charges from the item's `AbilityBehavior`), and [[server/spacetimedb/src/player/methods.rs#clear_span#1|clear_span]] (resets the whole run to empty). Every reducer that touches an ability item — `swap_slots`, `drop_item`, `pickup_drop`, the admin `remove_item` — resolves followers to their head first, because operating on a follower cell would corrupt the span.

The client mirror is [[client/Scripts/Components/Inventory/LocalPlayerInventoryComponent.cs|LocalPlayerInventoryComponent]], a child of the `LocalPlayer` entity whose `LocalPlayerInventoryBinder` is declared and wired inline in `local_player.tscn` (insert + update → `OnInventoryRow`, delete → `OnInventoryRowDeleted`). It keeps a `Dictionary<int, PlayerInventorySlot>` keyed by slot index — one entry touched per row event, no list replacement — and resolves rows into `ResolvedSlot` pairs (slot row + catalog `Item`) through the `GameManager` cache. It never writes anything back; its only outgoing call is ability activation. The `LocalPlayer` root re-exposes all of it as pass-throughs ([[client/Scripts/Players/Local/LocalPlayer.cs#ResolveSlotAt#1|ResolveSlotAt]] / `GetSlotItemId` / `IsEquipmentSlot`) and re-fires row changes as the entity-level `InventoryChanged` signal via [[client/Scripts/Players/Local/LocalPlayer.cs#RaiseInventoryChanged#1|RaiseInventoryChanged]], so UI readers (`InventoryComponent`, `ItemSidebarComponent`, `CombatComponent`) bind to the entity and survive component refactors untouched.

### Stat resolution: `recompute_stats` is the only place stats exist

```sync
![[00 End-to-End Timeline Flowchart#^equip-3{seamless:true,title:false,marker:03.}]]
```

Read [[server/spacetimedb/src/player/methods.rs#recompute_stats#1|recompute_stats]] as a fold with four sources and one sink. The sources, in order: (1) the `PlayerStatAllocation` row's spent points, folded into the flat accumulator so gear mults scale allocated points like any other flat source; (2) every `StatModifier` on equipped heads — where "equipped" is an explicit role check: Weapon/Accessory/Armor heads at scale 1.0, ability heads scaled by [[server/spacetimedb/src/player/methods.rs#ability_position_multiplier#1|ability_position_multiplier]] (`ABILITY_POSITION_MULTIPLIERS` = `[1.5, 1.25, 1.1, 1.0, 0.9, 0.8]` — "order matters, first ability stronger"), span followers skipped, backpack/hotbar/bag ignored; (3) innate *then* socketed enchantment modifiers on those same items, at the same scale; (4) every `ActiveConsumableEffect` row's modifier at scale 1.0. Each modifier lands in per-`StatKind` `flat`/`mult` accumulators indexed by the `stat_index` helper, with **mults additive, not compounding** — two +10% sources give +20%, never +21%. The sink resolves each allocatable stat as `(BASE_STAT + flat) × (1 + mult)`, rounded, into `PlayerStats` (base stats are just the flat `BASE_STAT` = 10; level-ups grant points, never stats directly), and the three modifier-only kinds onto `PlayerData`: Hp over `compute_base_max_hp(level)` into `max_hp` (with `hp` clamped down if it now exceeds the max), Defense into `defense`, BaseSpeed over `BASE_SPEED` = 100 into the f32 `base_speed` that `report_movement` later clamps against. Every reducer in this doc ends by calling it, which is the invariant that makes the client simple: *any* change to gear, buffs, or allocation arrives as plain `PlayerStats`/`PlayerData` row updates.

Two inputs feed the allocation row. Level-ups: `internal_gain_xp` (called from combat kills) grants `SKILL_POINTS_PER_LEVEL` = 2 unspent points per level gained. Spending: [[server/spacetimedb/src/player/reducers.rs#allocate_stat#1|allocate_stat]] moves unspent points into one of the six allocatable stats and **rejects Hp/Defense/BaseSpeed** — the modifier-only kinds come from gear, not points. On the client, [[client/Scripts/Components/Inventory/StatsSidebarComponent.cs|StatsSidebarComponent]] (declared in `inventory_panel.tscn` under the Tab-toggled `Menu`, with its label and button paths exported) mirrors the `LocalPlayer`'s pass-through stat properties, and its six `+` buttons — one per allocatable stat, visible only while `UnspentPoints > 0` — each call `AllocateStat(stat, 1)`; Defense is displayed but deliberately has no button.

The mirror chain on the client is [[client/Scripts/Components/Data/LocalPlayerDataComponent.cs|LocalPlayerDataComponent]] (three binders wired inline in `local_player.tscn`): `OnDataRow` lifts level/defense/base_speed onto itself and pushes hp/max_hp into the sibling `HealthComponent`; `OnStatsRow` pushes the six stats into the sibling [[client/Scripts/Components/Data/StatsComponent.cs|StatsComponent]]; `OnAllocationRow` caches unspent points; all three raise `StatsChanged` on the entity. `StatsComponent` is comedot's keyed dictionary of [[client/Scripts/Resources/Stats/Stat.cs|Stat]] resources — clamped integer holders that emit `ValueChanged` only on real change — and `LocalPlayerDataComponent.OnEntityReady` registers the *same* `Stat` instance the `HealthComponent` owns under `StatKind.Hp`, so every observer (sidebar, health bars) sees one hp value regardless of which component they ask. Values only ever flow inward via `SetFromServer`; nothing on the client mutates them locally.

### Moving items: `swap_slots` and drag/drop

```sync
![[00 End-to-End Timeline Flowchart#^equip-4{seamless:true,title:false,marker:04.}]]
```

The client side is deliberately dumb. Each [[client/Scripts/Components/Inventory/SlotComponent.cs|SlotComponent]] is a `Panel` with a `SlotIndex` export and an icon whose `MouseFilter` is forced to `Ignore` (otherwise the icon swallows the drag/hover events and Godot's `_GetDragData`/`_CanDropData` on the slot are never consulted). Godot's built-in drag-and-drop does the rest: `_GetDragData` returns the slot index when an icon is present, and [[client/Scripts/Components/Inventory/SlotComponent.cs#_DropData#1|_DropData]] calls `SwapSlots(fromIndex, SlotIndex)` — no local move, no optimistic reorder. The slots themselves are declared across [[client/Scenes/UI/inventory_panel.tscn|inventory_panel.tscn]]: an always-visible `Hotbar` (weapon + 4 consumables) and `Equipment` panel (weapon, six abilities, armor, accessory columns), with the 24-cell backpack grid plus bag slot inside the `Menu` control that [[client/Scripts/Components/Inventory/InventoryComponent.cs#_Input#1|InventoryComponent._Input]] Tab-toggles. Node names stay `<Type> - <Index>` because the component's exported slot-section arrays reference those exact paths.

All rules live server-side in [[server/spacetimedb/src/player/reducers.rs#swap_slots#1|swap_slots]], and the load-bearing insight is that span handling is **region-based, not item-based**: it triggers only when source or target is an Ability-role slot. The reducer first resolves followers to heads on both sides (targeting your *own* span's follower is a nudge — the head should move there — while targeting another span's follower resolves to that span's head), then enforces `slot_allowed` acceptance on both directions, then applies the class-threshold gate: any item moving from loose (non-equip) storage into an equip role must pass [[server/spacetimedb/src/player/methods.rs#check_stat_requirements#1|check_stat_requirements]] against resolved `PlayerStats`. If neither side is in the ability region, the swap is a plain single-cell exchange — even for ability items sitting loose in the backpack, because `slot_cost` means nothing outside the region. If either side *is* in the region, a 1:1 swap is impossible (the receiving side must be empty), and the move re-places the whole span: the run at the target must be contiguous free cells with the source's own cells counting as freed, runtime state (cooldown/charges/toggle) travels with the item — **except** backpack→ability moves, which re-initialize charges from the item's `AbilityBehavior` because the backpack cell's unlimited default is meaningless. Like every mutation here, it ends in `recompute_stats`.

The result comes back as row updates → `InventoryChanged` → [[client/Scripts/Components/Inventory/InventoryComponent.cs#OnInventoryChanged#1|InventoryComponent.OnInventoryChanged]] repainting every exported slot section via `UpdateSlotIcon`: followers display their head's icon dimmed to 0.4 alpha (runtime state lives on the head), and any item whose head's `CooldownUntil` is still in the future dims the same way — the two states a player needs to read at a glance.

### The equipped weapon: resolution, toggles, and the client mirror

```sync
![[00 End-to-End Timeline Flowchart#^equip-5{seamless:true,title:false,marker:05.}]]
```

"The weapon" is a derived value, not a row. Server-side, [[server/spacetimedb/src/player/methods.rs#resolve_effective_weapon#1|resolve_effective_weapon]] takes the weapon head slot's item, builds the **toggle-option list** — the item's own `Weapon` behaviors first, then every `WeaponToggle` grant from equipped enchantments, gathered by [[server/spacetimedb/src/player/methods.rs#equipped_enchantment_behaviors#1|equipped_enchantment_behaviors]] in its deterministic, load-bearing fold order (slot index ascending; innate before socketed per slot; behaviors *not* position-scaled, unlike stat modifiers) — selects `options[active_toggle]`, then folds the shot/damage enchantment behaviors in: `AddShots` sums, `ShotCountMult`s add before applying (the additive-mults rule again), and `FlatBulletDamageMod`/`TrueDamageFlat`/`TrueDamagePercent` ride along on the returned `EffectiveWeapon` for `compute_player_damage` to apply after defense. Effective shot count is `max(1, round(max(1, shot_count + add_shots) × (1 + shot_mult)))`. One edge case is handled by clamping, not erroring: removing a toggle-granting enchantment can leave the stored `active_toggle` pointing past the end of the shrunken list, so the resolver clamps the index (`min(active_toggle, len − 1)`) rather than failing — the player silently falls back to the last pattern. (The `set_slot_toggle` reducer itself is stricter: it *rejects* an out-of-range index after validating the weapon slot has at least two options — step 05 attributes the clamp and the rejection to their respective homes.)

The client runs the identical computation in [[client/Scripts/Components/Weapon/EffectiveWeaponResolver.cs#Resolve#1|EffectiveWeaponResolver.Resolve]] — same equipped-index order (`EquippedIndices`: 0, 5, 6, 32–37), same innate-before-socketed chaining, same clamp, same formula — and the two *must* stay in lockstep because both sides index the same toggle-option list: a divergence means the client fires one pattern while the server bills damage for another. Re-resolution is driven by [[client/Scripts/Components/Weapon/EffectiveWeaponResolver.cs#Fingerprint#1|Fingerprint]], a cache key over every equipped slot's item id, toggle index, and enchantment ids; [[client/Scripts/Components/Weapon/CombatComponent.cs#OnInventoryChanged#1|CombatComponent.OnInventoryChanged]] compares it on every `InventoryChanged` and only re-resolves when the composition actually changed. The toggle key itself (T) runs [[client/Scripts/Components/Weapon/CombatComponent.cs#CycleWeaponToggle#1|CycleWeaponToggle]] → `SetSlotToggle(0, (current + 1) % count)`; the updated slot row returns, changes the fingerprint, and the new pattern takes over. The resolver also copies the selected `WeaponBehavior` with its folded shot count rather than mutating the catalog's shared instance — the cache serves every consumer.

### Enchanting: sockets, innates, and the item sidebar

```sync
![[00 End-to-End Timeline Flowchart#^equip-6{seamless:true,title:false,marker:06.}]]
```

Hovering any slot calls [[client/Scripts/Components/Inventory/ItemSidebarComponent.cs#ShowSlot#1|ItemSidebarComponent.ShowSlot]] (slots and the sidebar find each other with `GetSibling` — no singleton), and the panel stays open while the mouse is over it or another slot so its buttons remain clickable (`QueueHoverClear` defers one frame and checks the actually-hovered control). `RenderDetails` rebuilds the composition from catalog + slot row on every render: stat modifiers, requirements in red while unmet, the behavior summary (for slot 0 it lists the full toggle-option list with the active pattern marked ▶, resolved through the same `EffectiveWeaponResolver.ToggleOptions` the combat component uses), innate enchantments labeled as such, and socketed enchantments as `N / MaxEnchantments`. Interactivity is gated by `LocalPlayer.IsEquipmentSlot` (0, 5, 6, 32–37): only then do applicable enchantments — filtered by `allowed_slots`, excluding already-socketed ones, Socket buttons disabled when full — render with working Socket/Remove buttons, one instanced [[client/Scenes/UI/enchantment_row.tscn|enchantment_row.tscn]] per entry. The sidebar refreshes on `InventoryChanged` and on `GameManager.EnchantmentsChanged`, so an admin `upsert_enchantment` re-renders open panels live.

The buttons call [[server/spacetimedb/src/player/reducers.rs#apply_enchantment#1|apply_enchantment]] / [[server/spacetimedb/src/player/reducers.rs#remove_enchantment#1|remove_enchantment]], which re-check everything server-side — `allowed_slots` membership, `max_enchantments` capacity, no duplicates — before rewriting the slot row's `enchantment_ids` and recomputing stats. Socketing an enchantment with `WeaponToggle` or shot-modifier behaviors therefore changes the resolver fingerprint on the next row echo and transparently re-resolves the firing weapon — the enchantment system and the weapon system meet only inside the two resolvers.

### Consumables: `use_item` and the buff ticker

```sync
![[00 End-to-End Timeline Flowchart#^equip-7{seamless:true,title:false,marker:07.}]]
```

Hotbar keys 1–4 map to slot indices 1–4 in [[client/Scripts/Components/Inventory/InventoryComponent.cs#_Process#1|InventoryComponent._Process]] (only while the hotbar is visible, and only when the slot is non-empty), calling `UseItem(slotIndex)`. The reducer's guard is the behavior test the whole schema is built around: `consumable_behavior(&item)` — the item must *have* a `Consumable` behavior; its `equip_slot` is irrelevant. [[server/spacetimedb/src/player/methods.rs#apply_consumable_effect#1|apply_consumable_effect]] then branches on effect: `Heal` delegates to `combat::heal_player` (healing lives in the combat module even when it comes from bread), `Buff` converts the `ConsumableBuffEffect` to a flat `StatModifier` and inserts an `ActiveConsumableEffect` row with `remaining = duration`. The item is consumed (slot cleared) and stats recomputed, so a buff's effect is visible immediately. Expiry is the scheduled [[server/spacetimedb/src/player/reducers.rs#tick_active_consumable_effects#1|tick_active_consumable_effects]] (the `ConsumableEffectSchedule` row inserted at publish fires it every `CONSUMABLE_EFFECT_TICK_SECONDS` = 1 s): each tick decrements `remaining`, deletes expired rows, and recomputes stats once per affected profile — which is why buffs wear off on their own with no client involvement. Note the tick is also the *only* thing that re-fires recompute for buffs; nothing else polls durations.

### Abilities: `activate_ability`

```sync
![[00 End-to-End Timeline Flowchart#^equip-8{seamless:true,title:false,marker:08.}]]
```

Ability input has two doors into the same function: the six ability actions in `InventoryComponent._Process`, and left-clicking an ability slot in [[client/Scripts/Components/Inventory/SlotComponent.cs#_GuiInput#1|SlotComponent._GuiInput]]. Both call [[client/Scripts/Components/Inventory/LocalPlayerInventoryComponent.cs#TryActivateAbility#1|LocalPlayerInventoryComponent.TryActivateAbility]], which follows span followers to the head, no-ops on passive items (an ability-region item with no `Ability` behavior, like the starter `Skull`), pre-checks charges and cooldown against the mirrored row, and calls `ActivateAbility(head, cursor)` with the cursor's world position. For bullet-control effects it *also* applies the cast optimistically on the local `BulletControllerComponent` — scaled by the same position multiplier the server will apply — because waiting for the event echo would add a full round-trip to a dodge-game reflex; remote clients apply the same cast when the `BulletControlEvent` row arrives, and the caster's own echo is skipped via `cast_by` (the full event protocol is [[09 Combat & Damage]]'s).

Server-side, [[server/spacetimedb/src/player/reducers.rs#activate_ability#1|activate_ability]] enforces what the client pre-checked (head slot only, cooldown, charges), computes `ability_position_multiplier(head_index)`, and scales potency through [[server/spacetimedb/src/player/reducers.rs#scale_ability_effect#1|scale_ability_effect]] — Heal scales through `potency`, but Buff amounts and bullet-control radii live *inside* the effect variants, so those are multiplied in place. Heal/Buff route through the same `apply_consumable_effect` as consumables. The targeted effects clamp the cursor to `MAX_ABILITY_TARGET_RANGE` = 600 around the server's own position row (clients can't cast from positions they claim), then append the `BulletControlEvent` — Delete/Split/Attract as point + radius, Slash as origin + far end + width with a fixed `(1, 0)` direction fallback when the cursor sits on the player, a fallback `TryActivateAbility` mirrors so the optimistic rect matches the echo. Finally it fires every equipped `OnAbilityUseBuff` enchantment trigger (the "using an ability buffs you" hook — the seeded `echoing` armor enchantment), writes `cooldown_until` and the decremented `charges` onto the slot row, and deliberately does **not** consume the item — abilities are equipment with cooldowns, not ammunition. Two mirrored constants must change in lockstep: the client's `AbilityPositionMultipliers` array and the server's `ABILITY_POSITION_MULTIPLIERS`.

Charges interact with spans in one more place: entering the ability region from the backpack re-initializes charges from the item (equip-4 above), so a depleted tome benched to the backpack and re-equipped comes back fresh — a consequence of "backpack cells hold no runtime state", and currently the only charge-refill path.

### Loot drops: `drop_item` → `LootDrop` → `pickup_drop`

```sync
![[00 End-to-End Timeline Flowchart#^loot-1{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^loot-2{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^loot-3{seamless:true,title:false,marker:11.}]]
```

The drop side is [[server/spacetimedb/src/player/reducers.rs#drop_item#1|drop_item]], triggered by right-clicking a backpack slot (`SlotComponent._GuiInput` — only in the `Backpack` subtree, detected by walking parents for the node named `Backpack`). The reducer resolves a dropped follower to its span head (dropping any cell of a span drops the whole item), clears the slot or span, and inserts the `LootDrop` row at the player's *server-side* position row — with the slot's `enchantment_ids` carried along, so a socketed item keeps its enchantments through the drop/pickup round-trip. `dropped_by` records the dropper for the pickup lock, and `expires_at` is stamped `LOOT_DROP_EXPIRY` = 300 s out — but see Known gaps: nothing ever reads it.

Drops are world entities, so they ride the same AOI pipeline as players and enemies: the [[server/spacetimedb/src/player/views.rs#nearby_loot_drops#1|nearby_loot_drops]] view filters `LootDrop` rows to the caller's AOI chunk indices (sentinel-query idiom when the caller has no chunk yet), and inserts/deletes hit the spawner's `NearbyLootDropsBinder`. [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnDropInsert#1|OnDropInsert]] instantiates [[client/Scenes/drop.tscn|drop.tscn]], sets `DropId`/`ItemId`/`DroppedBy`/position *before* `AddChild` (deferred), so the node's `_Ready` — which runs after its component children register — is the forward point; `OnDropDelete` frees the tracked node. The `Drop` root ([[client/Scripts/Items/Drop.cs|Drop.cs]]) is pure entity glue implementing `IEntity` by hand (it needs `Node2D`, so it can't inherit the usual bases): `_Ready` pushes the item id into [[client/Scripts/Components/Visual/DropVisualComponent.cs#SetItem#1|DropVisualComponent.SetItem]] (item texture through the catalog cache) and the row into [[client/Scripts/Components/Interaction/PickupComponent.cs#SetFromServer#1|PickupComponent.SetFromServer]], which starts its 5-second one-shot `PickupLockTimer` (declared in `pickup_component.tscn`) when `GameManager.IsLocal(dropped_by)` — purely a client-side convenience so you don't instantly re-grab your own toss; the server has no lock check.

Pickup is [[client/Scripts/Components/Interaction/PickupComponent.cs#OnBodyEntered#1|OnBodyEntered]] → `PickupDrop(drop_id)`, and the placement logic in [[server/spacetimedb/src/player/reducers.rs#pickup_drop#1|pickup_drop]] mirrors the equip rules: ability items try `find_free_span` auto-equip first — but only if `check_stat_requirements` passes, otherwise they divert to loose backpack storage (unmet requirements never *block* a pickup, they just divert it); everything else prefers a free matching typed slot (also requirements-gated) and falls back to any general cell, with ability-role cells skipped entirely because ability items only enter the region through span placement. The drop's `enchantment_ids` land on the receiving slot row, the `LootDrop` row is deleted — which propagates as the delete that despawns the node on every client — and `recompute_stats` closes out, because an auto-equip may have changed the player's stats.

## Known gaps / stubs

- **Loot expiry is dead code.** `LootDrop.expires_at` is stamped at insert time (`LOOT_DROP_EXPIRY` = 300 s in `main/global.rs`), but nothing ever reads it — there is no expiry sweeper, and the constant is referenced nowhere else in the module (verified by grep across `server/spacetimedb/src/`). Dropped items currently live forever.
- **The Bag does nothing.** The seeded `Bag` item and its dedicated slot 31 exist, but the item has no `stat_modifiers` and no `behaviors`, and no code reads the bag slot's contents — "bag capacity as a behavior" is not built, so the bag is currently a placeholder icon.
- **Stat requirements gate nothing yet.** `check_stat_requirements` is fully wired (equip, span auto-equip, typed-slot pickup) and the sidebar renders requirements in red, but every seeded item uses the all-zero `NO_REQUIREMENTS` constant in `item/seeds.rs` — the class-threshold system is enforced machinery with no gated content.
- **No loot from kills.** `LootDrop` rows are created only by `drop_item`; killing an enemy awards XP (combat-4) but drops nothing. Item acquisition beyond the starter kit currently requires admin `give_item`.
- **Aspirational and out of scope:** the procedural item generator the slot/behavior split was shaped for, and any enchantment breadth beyond the eight seeded enchantments, are design-doc material with no code behind them.

## Where to go next

The effective weapon this doc resolves is fired and billed in [[09 Combat & Damage]], which also owns the `BulletControlEvent` protocol abilities cast into and the damage formulas the enchantment modifiers feed. Item administration (`give_item`, catalog upserts) is covered with the rest of the admin tooling in [[12 Admin & Debug]]; what happens to all these rows on death or logout is [[13 Disconnect & Teardown]].
