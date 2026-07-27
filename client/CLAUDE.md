# client/CLAUDE.md

Guidance for Claude Code when working inside the Godot client (`client/`). See the root `CLAUDE.md` for the project-wide domain map and architecture rules.

The client is 100% C# (Godot 4.6 mono).

## Do not read

`Scripts/module_bindings/` — generated SpacetimeDB bindings.

## Module Map (`Scripts/`)

### `Components/` + `Entities/` (lean entity/component framework)
- `Component.cs` — abstract `Node` base for behavior components. `_Ready` walks ancestors for the nearest `IEntity`, registers via `RegisterComponent`, calls `OnRegistered()`, then warns (deferred) about missing sibling components from `GetRequiredComponents()`. `GetSibling<T>()` for cross-component access.
- `IEntity.cs` — implemented by entity scene roots. C# single inheritance means roots needing a native base (e.g. `LocalPlayer : CharacterBody2D`) implement it directly with an `EntityRegistry` field; plain-`Node` roots can derive from `Entity.cs` instead.
- `EntityRegistry.cs` — component storage behind `IEntity`.
- `NodeExtensions.cs` — `GetAncestor<T>()` (works across instanced sub-scene boundaries, unlike `Node.Owner`).
- `HitZone.cs` — self-reporting/self-freeing combat hit zone (`Scenes/Components/hit_zone.tscn`); spawned by `LocalPlayerCombat`.

### `Game/`
- `GameManager.cs` — root singleton (`GameManager.Instance`), root node of `Scenes/main.tscn`. Owns the `DbConnection`, the subscription builder, and instantiates/tracks player/enemy/drop/bullet nodes. Connection flow: "Join World" → `Connect()` → `OnConnected()` → subscription builder → `OnSubscriptionApplied()`. Also caches the `AllItems` (`GetItem(itemId)`) and `AllEnchantments` (`GetEnchantment(enchantmentId)`, `GetEnchantments()`, `EnchantmentsChanged` signal) subscription views, and resolves item textures (`GetResPath`).
- `BulletManager.cs` — bridges `BulletPatternEvent` rows to the **BlastBullets2D** GDExtension plugin (`BulletFactory2D`), dispatching per `PatternType`.
- `LobbyGui.cs` — lobby/menu UI (`Scenes/lobby_gui.tscn`). Currently scene navigation only — not yet wired to `create_profile`/`join_world` reducers.
- `DebugOverlay.cs` — debug HUD (`Scenes/UI/debug_overlay.tscn`, instanced in `main.tscn`); paired with server `main/debug.rs`.

### `Players/Local/`
- `LocalPlayer.cs` — `CharacterBody2D, IEntity`; WASD movement, mouse-aim rotation, sends `ReportMovement`, subscribes to local player data/stats views. Also owns inventory slot state: `ResolveSlotAt`/`GetSlotItemId`, fires `InventoryChanged` signal.
- `LocalPlayerCombat.cs` — `Component`; subscribes to `NearbyEnemies`, calls `ReportEnemyHit`. Resolves the player via the `IEntity` owner mechanism; spawns `HitZone` scenes (exported `HitZoneScene`) along bullet paths.
- `LocalPlayerInventory.cs` — `Control`; hotbar/backpack/equipment slot UI, listens for `LocalPlayer.InventoryChanged`, calls `UseItem` reducer on hotbar key press.
- `InventorySlotUI.cs` — `Control`; single inventory slot — drag/drop (`SwapSlots` reducer), right-click drop (`DropItem` reducer), hover shows `ItemStatsSidebar`.
- `ItemStatsSidebar.cs` — `Control`; singleton sidebar panel showing the hovered slot's item name/icon/description plus full composition: base stat modifiers, behavior summary, socketed enchantments (`N / MaxEnchantments`), and — for enchantable items in equipment slots — applicable enchantments with Socket/Remove buttons calling the `ApplyEnchantment`/`RemoveEnchantment` reducers. Refreshes on `InventoryChanged` and `GameManager.EnchantmentsChanged`; stays open while the mouse is over it so buttons are clickable.
- The whole inventory UI (InventoryCanvas: hotbar, 24 slot panels, both sidebars) lives in `Scenes/UI/inventory_panel.tscn`, instanced into `local_player.tscn`. Scripts inside it reach the player via `GetAncestor<LocalPlayer>()`, not `Owner`.

### `Players/Remote/`
- `RemotePlayer.cs` — `Node2D`; lerps position/rotation toward values from remote player position subscriptions.

### `Players/Enemies/`
- `Enemy.cs` — `CharacterBody2D`; lerps toward server-authoritative position from `NearbyEnemies`. Listens for `BulletPatternEvent` rows and forwards matching events to `BulletManager`.

### `Items/`
- `Drop.cs` — `Area2D`; calls `PickupDrop(drop_id)` reducer when the local player enters its collision area.

### `World/`
- `HexGridOverlay2D.cs` — `Node2D`; hex grid debug overlay, reads `MapConfig`.
- `TerrainManager.cs` — `Node2D`; declared as a child of `Scenes/main.tscn`. Subscribes to `NearbyTerrainTiles`/`NearbyHexDecor` and renders them via MultiMeshInstance2D batches built in code (performance — the batches intentionally stay code-built).

## Setup notes
- No autoloads — `GameManager` is instantiated as the root node of `Scenes/main.tscn`.
- `VampireSurvivorsTemplate.csproj` (project root) has the `SpacetimeDB.ClientSDK` package reference — see the NuGet warning in the root `CLAUDE.md` if it's ever lost.
- **BlastBullets2D** GDExtension plugin (`client/addons/blastbullets2d`) must be present; `BulletManager` calls `ClassDB.Instantiate("DirectionalBulletsData2D")` and `ClassDB.Instantiate("BulletSpeedData2D")` at runtime.
