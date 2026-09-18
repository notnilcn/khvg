# 06 Chest

## Assumed knowledge

- [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] (`# Chest` section — the spine this doc transcludes from) and [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] (reading order, conventions).
- [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] — reducers (server functions clients invoke by name; transactional, returning no data), views (parameterized server queries clients subscribe to by accessor name), subscription waves (`BaseTables`/`LobbyTables`/`GameTables` in `TableSubscriber.cs`), and binder components (one `TableBinderComponent` child per consumed table, re-exposing row events as editor-wireable signals).
- Maintainer refs: [[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]].

## The 30-second version

The chest module owns three tables: `Chest` (one fixed-position loot container), `ChestItem` (one row per item inside a chest, either consumable or `regenerating`), and the server-only `ChestTakeCooldown` (one row per profile+chest gating regenerating takes). All authorship is admin-only world authoring — [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]], [[server/spacetimedb/src/chest/mod.rs#add_item_to_chest|add_item_to_chest]], [[server/spacetimedb/src/chest/mod.rs#add_all_items_to_chest|add_all_items_to_chest]] — while any in-world player loots through [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]], which range/permission-checks, cooldown-gates regenerating rows, places the item via the shared inventory entry point, and deletes only non-regenerating rows. Clients see chests through the `nearby_chests` / `nearby_chest_items` AOI views in the game wave: one spawns a `Chest` scene node per row, the other rebuilds the open chest panel's draggable slots — and dragging a slot onto any inventory slot reports `TakeFromChest`, letting the server pick the destination.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| Where are chests placed, and who can place them? | Chest | [[#Chest]] |
| How does a chest appear in the world? | Chest | [[#Chest]] (`Readers` → `Client`) |
| What is inside a chest — one-shot or infinite? | ChestItem | [[#ChestItem]] |
| How does the open chest panel fill with items? | ChestItem | [[#ChestItem]] (`Readers` → `Client`) |
| How do I take an item (drag onto inventory)? | ChestItem | [[#ChestItem]] (`Readers` → `Client`) + [[#Cross-table flows]] |
| Why did my take fail — too far, cooldown, full inventory? | ChestItem | [[#ChestItem]] (`Changers`: `take_from_chest` guards) |
| How fast can I loot an infinite (regenerating) chest? | ChestTakeCooldown | [[#ChestTakeCooldown]] |
| Where does a taken item land in my inventory? | — | `^table-player-inventory-slot` (placement via `transfer_item_to_inventory`); see [[#Cross-table flows]] |
| Why can't I loot inside someone's claim? | — | `^table-territory` (permission consult inside `validate_interact`); see [[#Cross-table flows]] |
| Why did a taken item stop rendering as ??? | — | `^table-player-knowledge` (auto-discover on take); see [[#Cross-table flows]] |
| What catalog item does a chest row refer to? | — | `^table-item` (`item_id` validated at insert) |

## Flowcharts

- [[flowcharts/main-chest.canvas]] — the composed chest flow (members in the `chest` flow of `flowcharts/flows.json`: the server `chest_subfolder` aggregate plus the client `Items`, `Interaction`, `Inventory`, `Spawning`, `UI`, and `chest` canvases). Verified 2026-09-08: no composed `main-*.canvas` exists on disk yet — this link resolves once "Regenerate all flowcharts" composes it.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/chest_subfolder/chest_subfolder.canvas]] — server aggregate: tables, reducers, views (verified present).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Items_subfolder/Items_subfolder.canvas]] — client aggregate covering `Chest.cs` + `RefreshItems` (verified present).
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/chest_codefile/chest_codefile.canvas]] — the `chest.tscn` wiring: binder, panel, signal connections (verified present).

## Chest

```sync
![[00 Table Map#^table-chest{seamless:true,title:false,marker:01.}]]
```

### Shape

One row is one loot container at a fixed world position: auto-increment `chest_id` primary key, `x`/`y` world coordinates, and a btree-indexed `chunk_index` stamped at insert from the wrapped coordinate (`world_to_chunk` → `spiral_chunk_index` in [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]], the same stamping `spawn_enemy` uses) — because both AOI views filter on that btree directly instead of scanning positions. The position is write-once: nothing ever updates a chest row, and the parent holds no contents itself (contents are `ChestItem` rows keyed by `chest_id`).

### Changers

- **Insert** in [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]] — admin-only (`is_admin` rejects otherwise); wraps the requested position onto the torus via `wrap_world_pos` with `MapConfig::load(ctx, 1, 1)` and stamps `chunk_index` before inserting, so an admin can pass a raw coordinate and the row still lands on the correct chunk.
- **Never updated, never deleted** — there is no `remove_chest` reducer, so a misplaced chest is permanent (see [[#Known gaps]]).

### Readers

#### Client

Full path to the visible end: the [[server/spacetimedb/src/chest/mod.rs#nearby_chests|nearby_chests]] AOI view → `GameTables` wave ([[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber.GameTables]] lists both `NearbyChests` and `NearbyChestItems`) → `NearbyChestsBinder` on [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#EntitySpawnerComponent|EntitySpawnerComponent]] (`ReplayExistingRows = true`, declared inline in [[client/Scenes/game.tscn|game.tscn]] with `RowInserted`→`OnChestInsert` / `RowDeleted`→`OnChestDelete` wired in the scene) → [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnChestInsert|OnChestInsert]] instantiates `ChestScene` ([[client/Scenes/chest.tscn|chest.tscn]]), stamps `ChestId` and `GlobalPosition` from the row *before* the deferred `AddChild` (the `Drop` pattern — `Chest._Ready` is the forward point), and tracks it in the `chests` dictionary. The visible end is one `Chest` node per nearby row: a `Sprite2D` (texture `Textures/Items/Artifacts/Chest.tres` loaded in code in 2D mode only — hidden in 3D, where the chest is invisible but still interactable), an `InteractComponent` area, the items binder, and the hidden centered `ChestPanel`. `OnChestDelete` exists but is a dead path today because the server never deletes chest rows.

#### Server

[[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] reads the parent row twice: once to resolve the world position for the `validate_interact` range/permission gate, once to confirm the item's chest still exists — because the take names a `chest_item_id`, and the chest row is the only source of ground truth for *where* that item is.

## ChestItem

```sync
![[00 Table Map#^table-chest-item{seamless:true,title:false,marker:02.}]]
```

### Shape

One row is one item sitting in one chest: auto-increment `chest_item_id` key, btree `chest_id` back to the parent, `item_id` naming a row in the item catalog (checked against the `Item` table at insert — a plain string, not a database foreign key), a `regenerating` flag, and a btree `chunk_index` **mirrored from the parent chest at insert** so `nearby_chest_items` can run the same chunk-index OR-chain as `nearby_chests` with no two-query chest-id fan-out (the reason is stated in the table comment). The flag is the whole consumable-vs-infinite design: a `regenerating` row is never consumed by takes — the surviving row *is* the next copy — while a non-regenerating row is deleted on take.

### Changers

- **Insert** in [[server/spacetimedb/src/chest/mod.rs#add_item_to_chest|add_item_to_chest]] — admin-only; rejects unknown chests (`Chest {} not found.`) and unknown catalog ids (`Item '{}' not found.`), then copies the parent's `chunk_index` onto the row.
- **Insert (bulk)** in [[server/spacetimedb/src/chest/mod.rs#add_all_items_to_chest|add_all_items_to_chest]] — admin-only; one `ChestItem` row per catalog `Item` row with a single shared `regenerating` flag. A deliberate separate reducer rather than a magic `item_id` special case (per its doc comment).
- **Delete** in [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] — non-regenerating rows only; regenerating rows survive. The delete runs *after* a successful `transfer_item_to_inventory`, so a failed placement (`No suitable slot available.` on a full inventory) propagates with `?` and the row stays — the item is never lost to a full backpack.
- **Never updated** — takes either delete the row or leave it untouched; the cooldown path restamps `ChestTakeCooldown`, not this table.

`take_from_chest`'s guard chain, in order: `require_in_world` (must be joined) → row lookups (`ChestItem` by id, then `Item` and `Chest` joins) → `validate_interact` (wrap-aware [[server/spacetimedb/src/main/global.rs#INTERACT_RADIUS|INTERACT_RADIUS]] = 128.0 range check plus the territory inventory-permission consult; the generic `Target is too far away.` error is remapped to `Chest is too far away.`) → regenerating-only cooldown gate against `ChestTakeCooldown` ([[server/spacetimedb/src/main/global.rs#CHEST_TAKE_COOLDOWN_SECONDS|CHEST_TAKE_COOLDOWN_SECONDS]] = 1 s, see [[#ChestTakeCooldown]]) → `transfer_item_to_inventory` (ability items auto-equip into the first free span when stat requirements pass, else typed-slot-then-backpack placement) → `discover_ids` auto-discovery → conditional delete.

### Readers

#### Client

Full path to the visible end: the [[server/spacetimedb/src/chest/mod.rs#nearby_chest_items|nearby_chest_items]] AOI view → `GameTables` wave → the `NearbyChestItemsBinder` child of **each** `Chest` node (`ReplayExistingRows = true`, declared in [[client/Scenes/chest.tscn|chest.tscn]] with `RowInserted`→`OnChestItemRowInserted` / `RowDeleted`→`OnChestItemRowDeleted` wired in the scene) → [[client/Scripts/Items/Chest.cs#RefreshItems|Chest.RefreshItems]]. Both handlers first filter by `ChestId` (every chest node hears every nearby row event, so a foreign chest's insert/delete returns early) and rebuild only while the panel is visible. `RefreshItems` clears `ItemsGrid`, iterates the `NearbyChestItems` client cache for this chest's rows, instantiates one `ChestSlotScene` ([[client/Scenes/UI/chest_slot.tscn|chest_slot.tscn]] — code-instantiated because the count is data-driven, the declared-in-`.tscn` exception) per row, and calls `Setup(chest_item_id, item_id)`, which resolves the icon through the catalog (`GameManager.GetItem` → `GetResPath2D`, fed by the `BaseTables` wave). The visible end is the centered `ChestPanel` (`ChestCanvas` layer 2, `PanelContainer` + 6-column `ItemsGrid`, hidden until opened) filled with draggable `ChestSlotComponent` entries.

Opening the panel is proximity + keypress, not a table read: `InteractComponent` ([[client/Scripts/Components/Interaction/InteractComponent.cs#InteractComponent|InteractComponent]], `Area2D` with its 48-radius shape declared in `chest.tscn`) sets `playerInside` on `LocalPlayer` body enter/exit and calls `TogglePanel()` on the `interact` action (Shift+E — `interact` in `project.godot`) while inside; stepping out calls `ClosePanel()`. `TogglePanel` refreshes items on open, so the panel always renders the cache as of open time plus live insert/delete refreshes while open.

Taking is drag-and-drop across two components: `ChestSlotComponent._GetDragData` offers a `{"chest_item_id": …}` Dictionary (drag source only; the `Icon` is `mouse_filter` Ignore so the slot itself receives the drag query — the same rule as `SlotComponent`), and dropping it on any player `SlotComponent` (inventory UI declared inline in `local_player.tscn`) hits `SlotComponent._DropData`'s chest branch, which reports the `TakeFromChest` reducer with that id instead of the `SwapSlots` int path — **the server picks the destination slot** per its own placement rules, so the drop target only matters as a drop surface. The visible confirmation is the inventory slot filling in when the `LocalPlayerInventory` echo row arrives.

#### Server

`take_from_chest` looks the row up by `chest_item_id` and joins the catalog `Item` (for placement) and parent `Chest` (for the range check) — three point lookups, no scans.

## ChestTakeCooldown

```sync
![[00 Table Map#^table-chest-take-cooldown{seamless:true,title:false,marker:03.}]]
```

### Shape

Server-only accumulator (not `public`, so no client can subscribe): one row per (profile, chest) — btree `profile_id`, plain `chest_id`, `last_taken` timestamp — restamped on every regenerating-chest take. It exists because regenerating rows are never consumed, so the take *rate* must be gated instead of the stock.

### Changers

- **Insert/Update** in [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]], regenerating path only: first take inserts the row; later takes compare `seconds_since(ctx, last_taken)` against `CHEST_TAKE_COOLDOWN_SECONDS` (1 s, [[server/spacetimedb/src/main/global.rs#CHEST_TAKE_COOLDOWN_SECONDS|global]]) — too-recent takes are rejected with `This chest was taken too recently.`, otherwise the row is restamped via `..cd` spread update.
- **Never deleted** — rows accumulate one per (profile, chest) forever (see [[#Known gaps]]).

### Readers

#### Client

None — server-only table, no subscription, no visible end. The player perceives it only as the rejection error on rapid re-takes.

#### Server

Only the gate above: a btree-filtered lookup (`profile_id` index, then linear `chest_id` match over that profile's rows) on every regenerating take.

## Files — pointer deep dive

### `mod.rs`

The whole module is this single file (pointer index: [[server/spacetimedb/src/chest/pointers.md|pointers]]), which keeps the table set greppable: three `#[table]` definitions, four reducers, two views. Call direction is inward from clients/admins and outward into three helpers owned elsewhere — the module writes no other module's tables: `is_admin` / `require_in_world` / `transfer_item_to_inventory` / `validate_interact` / `discover_ids` (all `player/methods.rs`), `nearby_indices` (`player/views.rs`), chunk math (`world/hex.rs`, `world/wrap.rs`), catalog reads (`item/tables.rs`), and constants (`main/global.rs`, `main/time.rs`). Guards worth re-stating in one place: the three authoring reducers share the single `is_admin` gate; `take_from_chest` is the only non-admin entry and layers `require_in_world` → existence joins → `validate_interact` → cooldown → placement → discovery → conditional delete, with each failure returning `Err` before any write except the cooldown restamp (which happens before placement — a take that then fails placement still restamps the cooldown, so spamming take on a full inventory stays rate-limited).

## Cross-table flows

Taking from a chest is the module's one cross-table flow, and it all lives inside `take_from_chest`: `Chest` (range-check position) + `ChestItem` (what is taken, consumed or not) + `ChestTakeCooldown` (regenerating rate gate) → `PlayerInventorySlot` in `^table-player-inventory-slot` (via `transfer_item_to_inventory`, which also recomputes stats — the take never touches slots directly) → `PlayerKnowledge` in `^table-player-knowledge` (via `discover_ids`, so taken items stop rendering as ???) — with a `Territory` consult in `^table-territory` tucked inside `validate_interact` (`check_interact` rejects looting inside a claim without inventory permission). See [[#ChestItem]] (`Changers`) for the guard order.

## Known gaps

- **No `remove_chest` reducer.** `Chest` rows are append-only: once placed, a chest (and its items) can never be removed by any caller, and `EntitySpawnerComponent.OnChestDelete` is dead code until a delete path exists.
- **`ChestTakeCooldown` rows are never cleaned up.** One row per (profile, chest) persists forever — including for logged-out or deleted profiles (`teardown_profile` does not touch this table) — so the table grows monotonically with looting.
- **No 3D chest representation.** `Chest._Ready` hides the `Sprite2D` in 3D mode and loads no model, so chests are invisible in 3D while `InteractComponent` still opens the panel — the player loots an unmarked spot.

## Where to go next

Read the Player doc for the destination side of takes (`PlayerInventorySlot` placement, `PlayerKnowledge` discovery), the Claims doc for the territory permission consult inside `validate_interact`, and the Item doc for the catalog rows chest items name. If the wave/binder mechanics above felt rushed, read 03 first — this doc assumes them throughout.

## Phase 2 proposals

- **Flowcharts:** run "Regenerate all flowcharts" to compose `flowcharts/main-chest.canvas` from the `chest` flow in `flowcharts/flows.json`, and delete the stale `flowcharts/main-chests.canvas` (old topic name) if the compose leaves it behind — verified 2026-09-08 that *no* composed `main-*.canvas` exists on disk yet, so this is compose-only, no conflict to resolve.
- **Table set:** none — grepped `#[table(` across `server/spacetimedb/src/`: `chest/mod.rs` defines exactly the three tables in 00's `# Chest` section in the same order (`Chest`, `ChestItem`, `ChestTakeCooldown`), with the same changers and readers. Code matches 00; no anchor change proposed.
