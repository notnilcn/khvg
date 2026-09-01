# 12 Chest Tables

## Assumed knowledge

- [[02 The Component Framework]] — the client's entity/component model: `Chest` is an `IEntity` root whose children (`InteractComponent`, the `NearbyChestItemsBinder`) are components, and `TableBinderComponent` is how every row event reaches them.
- [[03 Connection, Subscriptions & Views]] — views, the `BaseTables`/`LobbyTables`/`GameTables` subscription waves, binder replay semantics. Both chest views ride the `GameTables` wave; this doc does not re-teach the mechanism.
- [[06 Movement & AOI Tables]] — `PlayerChunk` and the shared `nearby_indices` AOI helper the chest views filter through, and the wrap-aware distance math `take_from_chest` reuses.
- [[10 Items, Inventory & Loot Tables]] — the inventory slot model `store_item_in_inventory` places into, and the `LootDrop`/pickup flow the chest flow is contrasted against.

## The 30-second version

Chests are admin-authored loot containers at fixed world positions. The server keeps three tables in `chest/mod.rs`: `Chest` (one row per container: position + chunk), `ChestItem` (one row per item in a chest, with a `regenerating` flag that makes the row unconsumable), and the server-only `ChestTakeCooldown` (per-player take-rate gate for regenerating chests). Admins write the first two through `add_chest`/`add_item_to_chest`; players only ever call `take_from_chest`, which range-checks, gates regenerating takes on the cooldown, and routes placement through the shared `store_item_in_inventory` helper. Clients see chests through two AOI-filtered views (`nearby_chests`, `nearby_chest_items`) in the game wave: the first spawns `Chest` puppets via EntitySpawnerComponent, the second feeds the open chest panel, whose slots drag onto player inventory slots to report `take_from_chest`.

## Flowcharts

- [[flowcharts/main-chests.canvas]] — this system's composed flow.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/chest_subfolder/chest_subfolder.canvas]] — the whole server module: tables, the three reducers, both views.
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/chest_codefile/chest_codefile.canvas]] — `chest.tscn` + `Chest.cs`: the puppet, its binder, and the panel UI.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Interaction_subfolder/Interaction_subfolder.canvas]] — `InteractComponent` (chest open/close) alongside `PickupComponent` (the drop counterpart).

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

## Main body

### Why the tables are shaped this way

A chest is two concerns that change at different rates, so it is two tables (the one-table-per-concern rule from [[02 The Component Framework]]). `Chest` ([[server/spacetimedb/src/chest/mod.rs#Chest|definition]]) is near-static world authoring: an auto-inc `chest_id`, a world position, and a btree-indexed `chunk_index` for AOI filtering. `ChestItem` ([[server/spacetimedb/src/chest/mod.rs#ChestItem|definition]]) is the mutable part — one row per contained item, joined to its chest by a btree-indexed `chest_id` — so taking an item is a row delete, not an update to a serialized contents list. Splitting contents out of the chest row is also what lets the client subscribe to contents independently of positions: a player walking past a chest gets the `Chest` row (and the puppet) whether or not they ever look inside.

The `regenerating` flag on `ChestItem` replaces what would otherwise be a respawn timer: a regenerating row is *never consumed* by a take, so the surviving row IS the next copy — the chest "regenerates" the item by never running out of it. The consequence is that regeneration needs no scheduled job and no extra table; the only thing left to gate is the take rate, which is what `ChestTakeCooldown` ([[server/spacetimedb/src/chest/mod.rs#ChestTakeCooldown|definition]]) does — one row per (profile, chest), restamped per take. It is deliberately not `public`: the client never needs to render cooldown state, so the table stays server-only and never crosses the wire.

### The write path: authoring reducers

Both authoring reducers are admin-only world building (guarded by [[server/spacetimedb/src/player/methods.rs#is_admin|is_admin]]), in the same category as `place_building` — nothing in gameplay spawns a chest.

- [[server/spacetimedb/src/chest/mod.rs#add_chest|add_chest]] wraps the requested position onto the torus via [[server/spacetimedb/src/world/wrap.rs#wrap_world_pos|wrap_world_pos]], derives the chunk with [[server/spacetimedb/src/world/hex.rs#world_to_chunk|world_to_chunk]], and stamps `chunk_index` with [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index|spiral_chunk_index]] — the identical stamping `spawn_enemy` uses, so chests participate in the same chunk-indexed AOI as every other world entity.
- [[server/spacetimedb/src/chest/mod.rs#add_item_to_chest|add_item_to_chest]] validates the chest exists and the `item_id` resolves against the catalog (`Item` table, see ^table-item), then inserts one `ChestItem` row. Passing the `every_single_item` sentinel ([[server/spacetimedb/src/chest/mod.rs#EVERY_SINGLE_ITEM|EVERY_SINGLE_ITEM]]) short-circuits to inserting one row per catalog item — the "debug chest with everything" case.

### The write path: `take_from_chest`

[[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] is the only player-facing chest reducer, and it is a chain of guards *because* the server is authoritative — the client reports intent (a `chest_item_id`), never the outcome:

1. **In-world guard.** [[server/spacetimedb/src/player/methods.rs#require_in_world|require_in_world]] resolves the caller's `LoggedInPlayer` row or rejects; lobby players have no position and no inventory context, so they can't take.
2. **Resolution.** The `ChestItem`, its catalog `Item`, and its parent `Chest` are all looked up by index; any missing row rejects.
3. **Range check.** The caller's `PlayerPosition` row (see ^table-player-position) must be within `INTERACT_RADIUS` (128 px, [[server/spacetimedb/src/main/global.rs#INTERACT_RADIUS|INTERACT_RADIUS]]) of the chest, measured with [[server/spacetimedb/src/world/wrap.rs#wrapped_distance_sq|wrapped_distance_sq]] so a chest just across the torus seam is still reachable — no remote looting. Note the deliberate asymmetry with the client: the *panel* opens at 48 px (the `CircleShape2D` radius in `chest.tscn`), but the server honors takes out to 128 px, so a take is never rejected while the UI says the chest is open.
4. **Cooldown gate (regenerating rows only).** If the row is regenerating, the reducer looks up the caller's `ChestTakeCooldown` row for this chest: a take inside `CHEST_TAKE_COOLDOWN_SECONDS` (1 s, [[server/spacetimedb/src/main/global.rs#CHEST_TAKE_COOLDOWN_SECONDS|CHEST_TAKE_COOLDOWN_SECONDS]]) is rejected; otherwise the row is inserted or restamped with `ctx.timestamp`. Elapsed time comes from [[server/spacetimedb/src/enemy/methods.rs#seconds_since|seconds_since]] (microsecond timestamps → float seconds). The cooldown is keyed per **(profile, chest)**, not per item — one regenerating take locks out *every* regenerating item in that chest for the window.
5. **Placement.** [[server/spacetimedb/src/player/methods.rs#store_item_in_inventory|store_item_in_inventory]] — the same helper `pickup_drop` uses — picks the slot: ability items auto-equip into a free span when stat requirements pass, otherwise the item lands in the first free role-matching slot, falling back to a general (backpack) slot; a full inventory rejects the whole reducer, so the `ChestItem` row is not consumed on failure. The take passes no carried state (`vec![]` enchantments, `None` cooldown, `u32::MAX` charges = unlimited) because a chest item is fresh, unlike a picked-up drop which restores the state stamped on its `LootDrop` row.
6. **Consumption.** Only a *non-regenerating* row is deleted — this delete is the entire item-consumption mechanism. A regenerating row survives, which is the regeneration (see above).
7. **Stat recompute.** [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] runs at the end, the same tail `pickup_drop` has, so an auto-equipped ability item's stats apply immediately.

### The read path: two AOI views

Both views are per-caller filters built on the shared [[server/spacetimedb/src/player/views.rs#nearby_indices|nearby_indices]] helper, which reads the caller's `PlayerChunk` row (see ^table-player-chunk) and expands it to the surrounding chunk indices at `DEFAULT_AOI_CHUNK_RADIUS` (2 rings). They live in the `GameTables` wave ([[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber]]), so they only stream while the player is in the world.

- [[server/spacetimedb/src/chest/mod.rs#nearby_chests|nearby_chests]] is the `nearby_loot_drops` filter verbatim: an OR-chain of `chunk_index` equality over the caller's nearby chunk set (the view-builder has no `IN`), with the deliberately-unmatchable `chest_id == u64::MAX` sentinel as the empty result when the caller has no chunk row yet. This view drives chest *spawning*.
- [[server/spacetimedb/src/chest/mod.rs#nearby_chest_items|nearby_chest_items]] filters contents *through* the chests, because `ChestItem` has no `chunk_index` of its own: the view first collects the nearby chest ids imperatively (`ctx.db.chest().chunk_index().filter(idx)` per chunk — `ViewContext` allows this alongside the query builder), then OR-chains `chest_id` equality over them. Stamping a redundant `chunk_index` onto every `ChestItem` would denormalize data the chest row already owns; the two-step read is the price of keeping contents keyed only by `chest_id`.

### The client: spawn, open, take

**Spawning.** The `NearbyChestsBinder` child of `EntitySpawnerComponent` — declared inline in [[client/Scenes/game.tscn|game.tscn]] with `ReplayExistingRows = true`, signals wired there to `OnChestInsert`/`OnChestDelete` — drives [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnChestInsert|OnChestInsert]]: it instantiates `chest.tscn`, stamps `ChestId` and `GlobalPosition` *before* the deferred `AddChild` (the Drop pattern — `_Ready` is the forward point), and tracks the puppet in the `chests` dictionary so [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#OnChestDelete|OnChestDelete]] can despawn it. Replay-on is correct here because `nearby_chests` is a persistent view: a late joiner must see chests that already exist (the opposite of the event-table rule in [[03 Connection, Subscriptions & Views]]). Deletes only fire on AOI exit — the server never deletes `Chest` rows — so in practice a chest despawns only when the player walks away.

**The puppet.** [[client/Scripts/Items/Chest.cs|Chest]] is a `Node2D, IEntity` root with the same EntityRegistry boilerplate as `Drop`/`Enemy` (it needs the `Node2D` base, so it implements `IEntity` directly rather than deriving `Entity`). [[client/Scenes/chest.tscn|chest.tscn]] declares its four children: a plain `Sprite2D`, an `InteractComponent` (`Area2D`) with a 48 px `CircleShape2D` child (the DamageReceivingComponent pattern — the area carries no shape of its own), the `NearbyChestItemsBinder` (a `TableBinderComponent` bound to `NearbyChestItems`, replay on), and the `ChestCanvas`/`ChestPanel` UI — a center-screen `PanelContainer` with a 6-column `GridContainer`, hidden by default.

**Opening.** [[client/Scripts/Components/Interaction/InteractComponent.cs|InteractComponent]] tracks whether the local player's body is inside its area (`BodyEntered`/`BodyExited` filtered to `LocalPlayer`); on the `interact` input action while inside, its `_Process` calls `Chest.TogglePanel()`, and stepping out calls `Chest.ClosePanel()`. (The action is bound to the **V** key in `client/project.godot`; [[client/AGENTS.md]]'s "Shift+E" note is stale.) This is the deliberate contrast with drops: [[client/Scripts/Components/Interaction/PickupComponent.cs|PickupComponent]] fires the `PickupDrop` reducer on body-entered because a drop is a single walk-over grab, while a chest is browse-and-take — opening a panel is a UI act, not a reducer call, so no server round-trip happens until an actual take.

**Rendering contents.** The binder's `RowInserted`/`RowDeleted` signals are wired in `chest.tscn` to [[client/Scripts/Items/Chest.cs#OnChestItemRowInserted|OnChestItemRowInserted]]/`OnChestItemRowDeleted`, which filter the row to this chest's `ChestId` (the binder replays *all* nearby chests' items, not just this puppet's) and re-run [[client/Scripts/Items/Chest.cs#RefreshItems|RefreshItems]] while the panel is open. `RefreshItems` rebuilds the grid from the client cache (`conn.Db.NearbyChestItems.Iter()`), code-instantiating one `ChestSlotComponent` per row (the declared-in-tscn exception — the slot count is data-driven).

**Taking.** [[client/Scripts/Components/Inventory/ChestSlotComponent.cs#_GetDragData|ChestSlotComponent._GetDragData]] is a drag *source only*: it returns a `{"chest_item_id": …}` Dictionary instead of a slot index. [[client/Scripts/Components/Inventory/SlotComponent.cs#_DropData|SlotComponent._DropData]] branches on that shape (via `IsChestDrag`) *before* its normal `SwapSlots` int path and reports the `TakeFromChest` reducer with just the row id — which slot the item lands in is the server's decision (step 5 above), so the drop *target* slot is only a gesture surface. Every inventory slot accepts a chest drag because `_CanDropData` allows the Dictionary shape on all of them. The server's row delete (non-regenerating) or silence (regenerating) then flows back through `nearby_chest_items` into the open panel — the client renders the echo, never an optimistic local removal.

## Known gaps / stubs

- **No removal or editing path.** There is no `remove_chest` or `remove_item_from_chest` reducer: `Chest` rows are never updated or deleted, and a `ChestItem` row leaves only by being taken. A non-regenerating chest therefore depletes permanently and remains in the world as an empty container; a misplaced chest can only be removed by wiping the database (every publish does — see `server/build.sh`).
- **Authoring is CLI-only.** `add_chest`/`add_item_to_chest` are admin-gated and no client UI calls them; chests exist in the world only because someone ran the reducers by hand (`spacetime call`). There is no seed that places chests, so a freshly published world has none.

## Where to go next

The inventory the taken item lands in — slot roles, spans, and the `SwapSlots`/`DropItem` reducers that share `SlotComponent`'s drag surface — is [[10 Items, Inventory & Loot Tables]]. The walk-over counterpart to the chest panel (the `LootDrop` table and `PickupComponent`) is in the same doc; the AOI chunk machinery both views filter through is [[06 Movement & AOI Tables]].
