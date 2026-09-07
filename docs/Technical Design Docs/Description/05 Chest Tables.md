# 05 Chest Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for entities and the mirror/report split, [[03 Connection, Subscriptions & Views]] for waves, binders, and replay rules. The take path lands items through the single acquire entry documented in `10 Player Tables` (`transfer_item_to_inventory`); the range check reads the position row documented there.

## The 30-second version

Chests are admin-placed loot containers at fixed world positions: one `Chest` row for the box, one `ChestItem` row per item inside it (mirroring the parent's `chunk_index` so the contents view filters directly), plus a server-only `ChestTakeCooldown` row per (profile, chest) gating takes from regenerating chests. Both visible tables stream through AOI views in the Game wave — chests spawn puppets via `EntitySpawnerComponent`, contents render in the chest panel — and taking an item calls `take_from_chest`, which range-checks, enforces the cooldown, routes placement through the shared inventory acquire path, and deletes (or keeps, for regenerating rows) the item row.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do chests appear in the world? | `Chest` | [[#Chest]] — `nearby_chests` view → `EntitySpawnerComponent` → `chest.tscn` puppet |
| How do I see what's inside a chest? | `ChestItem` | [[#ChestItem]] — open panel rebuilds `ChestSlotComponent` rows from `nearby_chest_items` |
| How do I take an item from a chest? | `ChestItem` | [[#ChestItem]] — drag onto a player slot → `TakeFromChest` reducer |
| What does "regenerating" mean? | `ChestItem` | [[#ChestItem]] — the surviving row IS the next copy; takes are cooldown-gated instead |
| Why was my regenerating take rejected? | `ChestTakeCooldown` | [[#ChestTakeCooldown]] — one take per player per chest per `CHEST_TAKE_COOLDOWN_SECONDS` |
| How are chests created / stocked? | `Chest` | [[#Chest]] — admin-only `add_chest` / `add_item_to_chest` / `add_all_items_to_chest` |

## Flowcharts

- [[flowcharts/main-chest.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/chest_codefile/chest_codefile.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/chest_subfolder/chest_subfolder.canvas]]

## Table changes

```sync
![[00 Table Map#^table-chest{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-chest-item{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-chest-take-cooldown{seamless:true,title:false,marker:03.}]]
```

## Chest

### Shape

One row per loot container in [[server/spacetimedb/src/chest/mod.rs#Chest|chest/mod.rs]]: auto-inc `chest_id`, fixed world `x`/`y`, and the btree-indexed `chunk_index` stamped from the wrapped coordinate at placement (the same `world_to_chunk` + `spiral_chunk_index` every spawner uses). The row carries no contents — those are ^table-chest-item rows keyed by `chest_id` — because contents need per-item regenerating flags and the parent needs to stay a stable spawn anchor.

### Changers

Inserts come only from [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]], which is admin-only world authoring (like `place_building`: no client UI calls it): it wraps the position onto the torus with `wrap_world_pos` and stamps the chunk index before inserting. Nothing ever updates a chest (fixed position) and nothing ever deletes one — chests are permanent once placed.

### Readers

- ***Client*** — via the `nearby_chests` view (the `nearby_loot_drops` AOI chunk-index OR-chain over `nearby_indices()`) in the Game wave → `EntitySpawnerComponent`'s replaying `NearbyChests` binder (declared inline in [[client/Scenes/game.tscn|game.tscn]]) → spawns/despawns the [[client/Scenes/chest.tscn|chest.tscn]] puppet (`Items/Chest.cs`, 2D sprite loaded from code, hidden in 3D mode). The puppet carries the `InteractComponent` Area2D zone, which tracks the local player inside and toggles the center-screen chest panel on the `interact` action (Shift+E), closing it when the player steps out.
- ***Server*** — `take_from_chest` reads the row twice: the wrap-aware `INTERACT_RADIUS` range check (`wrapped_distance_sq` against the caller's ^table-player-position row — no remote looting) and the parent `chunk_index` (already mirrored onto the item row, so no second lookup is needed in the view path).

## ChestItem

### Shape

One row per item in a chest: auto-inc `chest_item_id`, btree-indexed `chest_id` back to the parent, the catalog `item_id` (validated against ^table-item at insert), the `regenerating` flag, and the parent's mirrored `chunk_index` — mirrored so `nearby_chest_items` filters on the chunk btree directly instead of a two-query chest-id fan-out. A `regenerating` row is never consumed by takes: the surviving row IS the next copy, so the chest regenerates by never running out.

### Changers

Inserts come from the two admin-only stocking reducers: [[server/spacetimedb/src/chest/mod.rs#add_item_to_chest|add_item_to_chest]] (single item, validates the catalog id) and [[server/spacetimedb/src/chest/mod.rs#add_all_items_to_chest|add_all_items_to_chest]] (bulk fill — one row per catalog item with one shared `regenerating` flag; its own reducer rather than a magic item_id special case). Nothing ever updates an item row. Deletes come only from [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] on non-regenerating takes: any in-world player within range takes the item, placement routes through the single `transfer_item_to_inventory` entry (shared with `pickup_drop` — stat requirements and span placement behave identically), and the row is deleted. Regenerating takes keep their row and instead stamp ^table-chest-take-cooldown.

### Readers

- ***Client*** — via the `nearby_chest_items` view (same AOI filter over the row's own mirrored `chunk_index`) in the Game wave → the replaying `NearbyChestItemsBinder` in [[client/Scenes/chest.tscn|chest.tscn]] → the open chest panel rebuilds one draggable `ChestSlotComponent` per cached row (`Scenes/UI/chest_slot.tscn`, code-instantiated because the count is data-driven). Dragging a chest slot onto any player `SlotComponent` carries a `{"chest_item_id": …}` Dictionary — `SlotComponent._DropData` branches on it before the `SwapSlots` int path — and reports `TakeFromChest`. Visible end: the chest's item grid, emptying (or not, for regenerating rows) as items move into the backpack.
- ***Server*** — `take_from_chest` guards on the row's `regenerating` flag to pick the consume path versus the cooldown path.

## ChestTakeCooldown

### Shape

One row per (profile, chest) in [[server/spacetimedb/src/chest/mod.rs#ChestTakeCooldown|chest/mod.rs]]: auto-inc `id`, btree-indexed `profile_id`, `chest_id`, `last_taken`. It exists because regenerating rows are never consumed, so the take *rate* must be gated instead of the stock.

### Changers

[[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] inserts the row on a profile's first regenerating take from a chest and restamps `last_taken` on every further take; takes within `CHEST_TAKE_COOLDOWN_SECONDS` (1 s) of the stamp are rejected. Rows are never deleted — one per (profile, chest), restamped forever. Never updated except for the restamp, never read by anyone but `take_from_chest`.

### Readers

- ***Client*** — none. Server-only table (not `public`): the rejection surfaces only as the `TakeFromChest` reducer error on the spammed take.
- ***Server*** — the cooldown check described above; nothing else.

## Files — pointer deep dive

Entry points: [[server/spacetimedb/src/chest/pointers.md|chest pointers]]. The module is a single file (the `combat/` precedent), so this section follows its internal order: tables, reducers, views.

### mod.rs — tables

`Chest` / `ChestItem` / `ChestTakeCooldown` plus their doc comments (the regenerating-row and mirror rationale). Read from `player/methods.rs` (`transfer_item_to_inventory` consumes the item on take) and `player/tables.rs` (`PlayerPosition` feeds the range check); written only by this file's own reducers.

### mod.rs — reducers

`add_chest` (admin gate → torus wrap → chunk stamp → insert), `add_item_to_chest` / `add_all_items_to_chest` (admin gate → parent lookup → catalog validation → chunk-mirrored insert), `take_from_chest` (the full take pipeline: `require_in_world` → item/chest/position lookups → wrap-aware range reject → regenerating-cooldown branch → `transfer_item_to_inventory` → consume-or-keep). Called from exactly one client site: `SlotComponent`'s chest-drag branch in [[client/Scenes/local_player.tscn|local_player.tscn]].

### mod.rs — views

`nearby_chests` / `nearby_chest_items`: the same `nearby_indices()` chunk OR-chain with the sentinel-empty pattern (see [[03 Connection, Subscriptions & Views]]), over `Chest.chunk_index` and the mirrored `ChestItem.chunk_index` respectively. Subscribed in the Game wave (`TableSubscriber`), bound by `EntitySpawnerComponent` (`NearbyChests`) and the chest puppet (`NearbyChestItemsBinder`).

## Cross-table flows

The take flow is the module's one multi-table mechanism, and it fits in one pass: range check reads ^table-chest + ^table-player-position → cooldown branch reads/writes ^table-chest-take-cooldown → placement writes ^table-player-inventory-slot through `transfer_item_to_inventory` (then `recompute_stats`) → consume branch deletes ^table-chest-item. The reducer is transactional, so a failed placement never consumes the item.

## Where to go next

[[06 Combat Tables]] covers the damage pipeline that `transfer_item_to_inventory`'s callers (kills, drops) feed into. For the inventory rows takes land in, read `10 Player Tables`.
