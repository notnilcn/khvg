# 10 Inventory, Items & Enchantments

## Assumed knowledge

- [[09 Combat & Damage]] — `heal_player` (combat-8, the consumable Heal path), the Strength/Defense scaling formulas (combat-5/combat-7) that `recompute_stats` feeds, and `CombatComponent`'s `EquippedWeapon` read (combat-1).
- [[05 Joining the World]] — join-2's `try_scaffold_profile` (which scaffolds the 24-slot inventory), the subscription waves, and join-5's `EntitySpawnerComponent` binder path that spawns `Drop` nodes.
- [[06 Movement & Position Sync]] — `require_in_world` (move-3, the guard on every reducer here) and the `nearby_indices` AOI machinery (move-5) behind the `nearby_loot_drops` view.
- [[04 Lobby & Profiles]] — the level/XP math (`compute_level`, lobby doc's `xp_for_level`) that base stats and max HP derive from.
- [[03 Boot & Connection]] — boot-4's `init` reducer (item/enchantment seeding, the consumable-effect schedule) and the base subscription wave.
- [[02 The Component Framework]] — entities/components, the ancestor-walk registration that lets the instanced inventory panel belong to the `LocalPlayer` entity, and the `TableBinderComponent` mechanism.
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `equip` and `loot` sections.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

Items are **server-owned rows, client-mirrored**. The static catalog — six seeded `Item`s and five seeded `Enchantment`s — is public world content delivered by three anonymous views and cached client-side by `CatalogComponent`, which every lookup goes through. Each profile's inventory is a single `PlayerInventory` row holding 24 typed slots (weapon, 4 consumable hotbar, 4 accessory, 4 armor, 2 artifact, 8 general, 1 bag); every slot knows which item types it accepts, and the client panel is a fire-and-forget front end: drag-drop calls `swap_slots`, hotbar keys call `use_item`, right-click calls `drop_item`, sidebar buttons call `apply_enchantment`/`remove_enchantment`, and walking over a drop calls `pickup_drop`. Every one of those reducers converges on `recompute_stats`, which folds base stats + equipment modifiers + socketed enchantments + active consumable buffs through one `round((base + flat) × (1 + mult))` formula and writes `PlayerStats`/`PlayerData` back — the client never computes a stat, it repaints when the rows round-trip. Consumable buffs decay on a 1-second scheduled reducer; loot drops are player-created only, AOI-broadcast, and physically picked up.

## Flowcharts

- [[flowcharts/main-items.canvas]] — the composed items flow (client `Inventory`/`Catalog`/`Interaction`/`Data` components, the `Stats` resources, `Drop`, the inventory UI scenes, and the server's `item` module plus the `player` module that mutates it).
![[flowcharts/main-items.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Inventory_subfolder/Inventory_subfolder.canvas]] — deep dive: the five inventory components (`InventoryComponent`, `LocalPlayerInventoryComponent`, `SlotComponent`, `ItemSidebarComponent`, `StatsSidebarComponent`).
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/UI_subfolder/UI_subfolder.canvas]] — deep dive: `inventory_panel.tscn` and `enchantment_row.tscn`, the panel layout the components drive.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/item_subfolder/item_subfolder.canvas]] — deep dive: the server `item` module (tables, admin reducers, seeds, public views).

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

### The catalog: three public views, one dictionary

```sync
![[00 End-to-End Timeline Flowchart#^equip-1{seamless:true,title:false,marker:01.}]]
```

The catalog views are `public` and *anonymous* — [[server/spacetimedb/src/item/views.rs##fn all_items|all_items]] takes an `AnonymousViewContext`, meaning the server evaluates one query for everyone, with no per-caller filtering — because item and enchantment definitions are world content, identical for every client, like the terrain defs of [[07 Terrain & World Streaming]]. That is also why they ride the **base wave** ([[TableSubscriber.cs##public static readonly string[] BaseTables|BaseTables]]): the catalog is needed in the lobby-adjacent UI and the game alike, so it is subscribed at connect and never unsubscribed (only the lobby and game waves have `Unsubscribe*` paths). Seeding is publish-time and idempotent — the [[server/spacetimedb/src/main/seeds.rs##pub trait Seed|`Seed` trait]] upserts by primary key, so republishing the module refreshes the content in place rather than duplicating it.

[[CatalogComponent.cs##public partial class CatalogComponent : Component|CatalogComponent]] is a pure cache: three `Dictionary` fields keyed by id, fed by insert/update/delete handlers, with `ReplayExistingRows` on the binders so rows already in the client cache still arrive through the same insert path (the binder mechanism is [[02 The Component Framework]]). The only event it emits is [[CatalogComponent.cs##public event Action? EnchantmentsChanged|EnchantmentsChanged]] — item and texture rows are static in practice, but enchantments drive the sidebar's Socket list, so [[ItemSidebarComponent.cs##protected override void OnRegistered|ItemSidebarComponent]] subscribes and re-renders on change. Everything else in the client reaches the cache through the [[GameManager.cs##public static Item? GetItem(string itemId)|GameManager facade]] — `GetItem`/`GetEnchantment`/`GetEnchantments`/`GetResPath` — so no consumer needs to know where the component lives in the scene tree. One drift note: the component's docstring says its binders are "declared in catalog_component.tscn", but that scene is one of the nine unreferenced duplicate component scenes — the live declaration is inline in `main.tscn` (equip-1), with the nine binder `[connection]` entries at the bottom of that file.

### One row, 24 typed slots

```sync
![[00 End-to-End Timeline Flowchart#^equip-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^equip-3{seamless:true,title:false,marker:03.}]]
```

The inventory is deliberately *one row with a `Vec`* rather than 24 slot rows: every mutation the game allows is a whole-list rewrite (`slots.clone()`, edit, `update`), so a swap is atomic inside the reducer's transaction and the client gets exactly one `RowUpdated` per change — [[LocalPlayerInventoryComponent.cs##private void OnInventoryRow()|OnInventoryRow]] can serve insert and update with the same handler because both replace the entire list. The slot's type constraint is *data on the slot* ([[server/spacetimedb/src/player/tables.rs##pub struct InventorySlot|`allowed_slots: Vec<EquipSlot>`]]), not a positional convention — that is what lets `swap_slots` validate moves generically and what `recompute_stats` later exploits to decide which slots grant stats.

The price of the one-row design is duplicated layout knowledge on the client. [[LocalPlayerInventoryComponent.cs##public partial class LocalPlayerInventoryComponent|LocalPlayerInventoryComponent]] hardcodes the section ranges the server builds from factories (slots 1–4 hotbar, 5–8 accessories, 9–12 armor, 13–14 artifacts, 15–22 general) and the server alone owns the general-slot bounds as constants ([[server/spacetimedb/src/main/global.rs##pub const GENERAL_SLOT_START|GENERAL_SLOT_START]] = 15, `GENERAL_SLOT_COUNT` = 8) — change one side and the other's typed accessors silently mislabel. [[LocalPlayerInventoryComponent.cs##public static bool IsEquipmentSlot|IsEquipmentSlot]] encodes a second, subtler rule: worn-and-enchantable means slot 0 or 5–14 — the general slots *and* the bag slot (23) are excluded, so a Bag can never be socketed even though its slot is single-purpose. `ResolveSlotAt` pairs the raw row slot with its catalog `Item` into a [[LocalPlayerInventoryComponent.cs##public class ResolvedSlot|ResolvedSlot]], the unit the sidebar renders.

### The panel: 29 panels for 24 slots

```sync
![[00 End-to-End Timeline Flowchart#^equip-4{seamless:true,title:false,marker:04.}]]
```

The 29-vs-24 arithmetic: the Backpack menu shows all 24 slots, and five of them (weapon + four consumables) are *echoed* in the always-visible Hotbar — the `HotbarSlots` and `InventoryHotbarSlots` export arrays of [[inventory_panel.tscn##[node name="InventoryComponent" type="Control" parent="."|the InventoryComponent node]] point at the two copies, and [[InventoryComponent.cs##private void OnInventoryChanged()|OnInventoryChanged]] repaints every registered panel on each `InventoryChanged`. Icon resolution is a two-hop through the catalog: slot → `item_id` → `Item.texture_id` → `res://` path → `GD.Load<Texture2D>` ([[InventoryComponent.cs##private static void SetSlotTexture|SetSlotTexture]]). The panel scene has **no `[connection]` entries at all** — every interaction is wired in code: hover via `MouseEntered`/`MouseExited` lambdas in [[SlotComponent.cs##protected override void OnRegistered|SlotComponent.OnRegistered]], drag via Godot's built-in drag-drop virtuals (`_GetDragData` returns the slot index as the drag payload, with a semi-transparent `TextureRect` preview), right-click via `_GuiInput`.

The sidebar's hover lifecycle is the one piece of non-obvious UI logic. Slots and the sidebar both call [[ItemSidebarComponent.cs##public void QueueHoverClear|QueueHoverClear]] on mouse-exit, which *defers* the clear by a frame and then checks `GuiGetHoveredControl`: if the mouse landed on the sidebar itself, one of its children, or another slot, the panel stays up — that deferred re-check is what makes the Socket/Remove buttons reachable at all, since moving the mouse from slot to button briefly leaves the slot. The sidebar finds its data without any scene wiring: `GetSibling<ItemSidebarComponent>()` works because the framework's registration walk crosses the instanced-scene boundary, so the panel's components register to the `LocalPlayer` entity even though they live in a different `.tscn` ([[02 The Component Framework]]). And the whole panel obeys the inventory's fire-and-forget convention: no click ever mutates a local slot — the UI sends a reducer and repaints only when equip-3's row update round-trips, so the client can never display a state the server rejected.

### Moving items: `swap_slots` is the only door

```sync
![[00 End-to-End Timeline Flowchart#^equip-5{seamless:true,title:false,marker:05.}]]
```

There is no "move to empty slot" special case — moving is swapping with an empty occupant, which is why the validation is symmetric: if either slot holds an item, that item's `equip_slot` must appear in the *other* slot's `allowed_slots`. An empty slot passes trivially (`item_id` is `None`, no check runs), so equipping is "swap backpack slot with weapon slot" and unequipping is the reverse. Same-index drops are discarded client-side ([[SlotComponent.cs##public override void _DropData(Vector2 atPosition, Variant data)|_DropData]] early-returns) and again server-side (`from_index == to_index` → `Ok`). The trailing `recompute_stats` is load-bearing: moving a Helmet from an armor slot (single-purpose, stats count) to a general slot (multi-purpose, stats don't count — see the next section) is a real stat change even though no item was created or destroyed.

### Consumables: use, tick, decay

```sync
![[00 End-to-End Timeline Flowchart#^equip-6{seamless:true,title:false,marker:06.}]]
```

The decay mechanism is a SpacetimeDB **scheduled table**: [[server/spacetimedb/src/player/tables.rs##pub struct ConsumableEffectSchedule|ConsumableEffectSchedule]] declares `scheduled(tick_active_consumable_effects)`, so the single row `init` inserts with `ScheduleAt::Interval(1s)` re-invokes the reducer every second, forever — a cron job expressed as a table row. Two consequences of the implementation are worth knowing. First, buffs are **flat-only**: [[server/spacetimedb/src/player/methods.rs##fn buff_to_modifier|buff_to_modifier]] wraps every `ConsumableBuffEffect` in `StatMode::Flat`, so a buff can never multiply. Second, buffs **stack additively by row** — each `use_item` inserts its own `ActiveConsumableEffect` row with its own countdown, so two Strength potions give two modifiers that expire independently; there is no refresh-or-replace dedup.

Edge cases the code accepts silently: `use_item` consumes the item even when the effect is wasted (a Heal at full HP — [[server/spacetimedb/src/combat/mod.rs##pub fn heal_player|heal_player]] just clamps), and although the client only offers hotbar slots 1–4, the reducer accepts *any* slot holding a consumable-behavior item — the hotbar restriction is UI convention, not server rule. And while `ActiveConsumableEffect` is a `public` table, no client subscription includes it (it is in none of [[TableSubscriber.cs##public static readonly string[] GameTables|the wave lists]]), so buff names and remaining time are invisible in the UI — you see only the resulting stat change in the sidebar.

### Enchantments: sockets as slot data

```sync
![[00 End-to-End Timeline Flowchart#^equip-7{seamless:true,title:false,marker:07.}]]
```

An [[server/spacetimedb/src/item/tables.rs##pub struct Enchantment|Enchantment]] is exactly two things: a `stat_modifiers` list and an `allowed_slots` list — no behaviors, no procs, no triggered effects. The three server checks (slot type legal, under `max_enchantments`, not already socketed) are mirrored on the client as *presentation*: the sidebar only lists enchantments whose `allowed_slots` contains the item's `equip_slot`, greys out Socket when the item is full, and only offers the buttons at all for `IsEquipmentSlot` slots — but the reducer re-checks everything, because the client is advisory by design (the same trust split as combat-4). The Socket/Remove buttons themselves are built per render in [[ItemSidebarComponent.cs##private Control MakeEnchantmentRow|MakeEnchantmentRow]] from the script-less [[enchantment_row.tscn##[node name="EnchantmentRow" type="HBoxContainer"|enchantment_row.tscn]] layout, with the reducer call captured in a button lambda.

The one design fact to internalize: **enchantments live on the slot, not the item** (`InventorySlot.enchantment_ids`). `drop_item` and `pickup_drop` deliberately carry the ids with the item into and out of the `LootDrop` row — but `swap_slots` doesn't move them, so dragging a socketed item detaches its enchantments (Known gaps). Worth stating explicitly at this edge: the Game Design Docs describe a much broader enchantment system (named effects, procs, build-defining modifiers); none of that exists in the code, and per this documentation project's rule it stays out of this doc — what is implemented is exactly the stat-modifier sockets above.

### `recompute_stats`: the single stat doorway

```sync
![[00 End-to-End Timeline Flowchart#^equip-8{seamless:true,title:false,marker:08.}]]
```

The `allowed_slots.len() != 1` skip in [[server/spacetimedb/src/player/methods.rs##pub fn recompute_stats|recompute_stats]] is the entire equipment/storage distinction, expressed without any slot-index constants: weapon, hotbar, accessory, armor, artifact, and bag slots each accept exactly one `EquipSlot` and therefore contribute; general slots accept five and are storage. (Yes, this means a stat-bearing Bag *would* count — the seeded Bag has no modifiers, so the rule is dormant there.) Nine call sites funnel through this one function: the six inventory reducers, the consumable tick (only for profiles whose buff expired — the decrement itself doesn't recompute, because `remaining` isn't a stat), `internal_gain_xp` on level-up (lobby doc), and `try_scaffold_profile`'s first build (join-2).

A worked example with the starter loadout at level 1: bases are 10 in all six stats (`compute_base_stats`), `max_hp` base 100 (`compute_base_max_hp`: 100 + 5 per level past 1). The Helmet contributes +5 Defense, +3 Vitality, +15 HP (all flat); the Hat +2 Wisdom/Dexterity/Speed; the Skull +5 Strength — so the row reads STR 15, WIS 12, DEX 12, DEF 15, VIT 13, SPD 12, `max_hp` 115. If you then socket the seeded "vampiric" enchantment (+0.05 Vitality, `Mult`) onto the Skull, Vitality resolves as `round((10 + 3) × (1 + 0.05)) = round(13.65) = 14` — flats sum first, then the multiplier applies to the *sum*, so mult enchantments scale with your flats. The `PlayerData` write is conditional (`max_hp` changed or `hp` over the new cap), so a pure Strength swap doesn't churn the HP row — and when the cap *drops* (unequipping the Helmet), `hp` is clamped down, never up.

On the client the numbers land in the mirror stack the reader met in [[09 Combat & Damage]]: [[StatsComponent.cs##public partial class StatsComponent|StatsComponent]] holds one [[Stat.cs##public partial class Stat : Resource|Stat]] resource per [[StatKind.cs##public enum StatKind|StatKind]] — a clamped, observable integer that only `SetFromServer` mutates — and [[LocalPlayerDataComponent.cs##protected override void OnEntityReady|LocalPlayerDataComponent.OnEntityReady]] registers the *same* `Stat` instance the `HealthComponent` owns under `StatKind.Hp`, so HP observers and stat observers literally watch one object (comedot's shared-Stat pattern). The client enum's docstring flags the naming collision: the generated bindings also define a `SpacetimeDB.Types.StatKind`, and `Hp` exists in both enums only for parity with item modifiers — live HP never flows through `StatsComponent.SetFromServer`.

### Loot drops

```sync
![[00 End-to-End Timeline Flowchart#^loot-1{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^loot-2{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^loot-3{seamless:true,title:false,marker:11.}]]
```

A `LootDrop` is a world object in the same sense enemies are: an AOI-broadcast row ([[server/spacetimedb/src/player/views.rs##fn nearby_loot_drops|nearby_loot_drops]] filters by the caller's surrounding chunk indices, so a drop is only visible within two chunk rings) with a client entity mirrored onto it. The [[drop.tscn##[node name="Drop" type="Node2D"|drop.tscn]] root is entity glue in the exact sense of [[02 The Component Framework]] — it implements `IEntity` by hand because it needs a `Node2D` base, the same pattern as `Enemy` — and its two components split presentation from interaction: [[DropVisualComponent.cs##public partial class DropVisualComponent|DropVisualComponent]] is a `Sprite2D` that implements `IComponent` directly (a component base class can't be a sprite), while [[PickupComponent.cs##public partial class PickupComponent : AreaComponent|PickupComponent]] is the instanced `pickup_component.tscn` `Area2D` carrying the collision shape and the lock timer as children.

Two lifecycle facts fall out of the row model. Drops **outlive their dropper**: `teardown_profile` (combat-8) deletes the profile's data/stats/inventory/position/chunk rows and active effects, but not `loot_drop` rows — dying or logging out leaves your thrown items on the ground for others. And the **pickup lock is a client courtesy, not a rule**: `dropped_by` is only ever read by [[EntitySpawnerComponent.cs##private void OnDropInsert()|OnDropInsert]] → `SetFromServer` to start the local 5-second timer, and `expires_at` is only ever *written* (Known gaps). The server's `pickup_drop` trusts the caller's assertion of contact, exactly the client-detected/server-decided split of [[09 Combat & Damage]] — a modified client could vacuum any drop it can see by id.

The admin reducers behind the item tables — `give_item`/`remove_item` (target a player's profile by name, land in the first free general slot) and the `upsert_item`/`upsert_enchantment`/`upsert_texture_entry` content editors — are all `is_admin`-gated and covered in [[12 Admin & Debug]].

## Known gaps / stubs

- **`LootDrop.expires_at` is written but never enforced.** [[server/spacetimedb/src/player/reducers.rs##pub fn drop_item|drop_item]] stamps `ctx.timestamp + LOOT_DROP_EXPIRY` (300s, [[server/spacetimedb/src/main/global.rs##pub const LOOT_DROP_EXPIRY|global.rs]]), but nothing ever reads `expires_at` — grep finds the field only at its declaration and that insert, no sweeper reducer or schedule exists, and `LOOT_DROP_EXPIRY` is referenced nowhere else. Drops currently live until picked up.
- **`SlotComponent`'s docstring undercounts its declarations.** The docstring says "declared 24 times in inventory_panel.tscn"; the scene actually attaches the script 29 times (24 menu slots + 5 hotbar echoes of slots 0–4).
- **Scene bug: the `ArmorSlots` export array lists `Armor - 11` twice and omits `Armor - 12`** ([[inventory_panel.tscn##ArmorSlots = [NodePath|inventory_panel.tscn]]). The `Armor - 12` panel itself has the correct `SlotIndex = 12`, so drag/drop, right-click, and hover all work on it — but `UpdateSection` only repaints panels reachable through the export arrays, so armor slot 12's icon never refreshes.
- **Scene bug (adjacent, verified this session): the `Bag - 23` panel is in *no* export array** — the six arrays cover indices 0–22 (with 11 duplicated), so the bag slot's icon likewise never repaints even though the starter Bag sits there from the first join.
- **`swap_slots` detaches enchantments from their item.** It exchanges only `item_id` ([[server/spacetimedb/src/player/reducers.rs##pub fn swap_slots|swap_slots]]), leaving `enchantment_ids` in the source slot — unlike `drop_item`/`pickup_drop`, which carry the ids with the item. After moving a socketed item, the old slot keeps live enchantment ids (applied to whatever item lands there next, since `recompute_stats` reads slot + item together) and the moved item renders as `Sockets 0 / N`.
- **Enemies drop no loot.** `drop_item` is the only producer of `LootDrop` rows in the module (verified by grep over `server/spacetimedb/src`); a kill awards XP only. The whole `Drop`-spawning client path currently only ever shows player-discarded items.
- **Pickup is client-asserted.** `pickup_drop` validates no distance and ignores `dropped_by`; the 5-second re-pickup lock exists only in the client's `PickupLockTimer`. Same accepted trust boundary as the hit reducers ([[09 Combat & Damage]]), documented so it isn't mistaken for an oversight.
- **The admin item reducers don't recompute.** `give_item` and `remove_item` update the inventory row without calling `recompute_stats` — harmless for `give_item` (it targets stat-less general slots), but `remove_item` can strip an equipped item and leave stale `PlayerStats` until the next mutation.
- **Enchantment breadth is aspirational.** The Game Design Docs' richer enchantment designs (named effects, procs, build modifiers) have no code presence — implemented enchantments are stat-modifier sockets only, per this project's rule that aspirational systems stay out.
- **Docstring drift:** `CatalogComponent.cs` cites `catalog_component.tscn` as its wiring site; that scene is one of the nine unreferenced duplicate component scenes, and the live declaration is inline in `main.tscn`.

## Where to go next

The camera and presentation layer that renders the world this panel floats over is [[11 Camera & Presentation]]. The admin side of items — `give_item`, `upsert_item`, `upsert_enchantment`, texture management — is [[12 Admin & Debug]], and what happens to these rows (and the drops they leave behind) on disconnect and death is [[13 Disconnect & Teardown]].
