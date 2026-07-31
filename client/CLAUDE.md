# client/CLAUDE.md

Guidance for Claude Code when working inside the Godot client (`client/`). See the root `CLAUDE.md` for the project-wide domain map and architecture rules.

The client is 100% C# (Godot 4.6 mono) and fully compositional, modeled on `code_examples/comedot`.

## Do not read

`Scripts/module_bindings/` — generated SpacetimeDB bindings.

## Naming convention

Every high-level parent node's children are **components named `<Role>Component`** — node name = class name = file name (the `HealthComponent` node in a `.tscn` is an instance of `HealthComponent.cs`). Exceptions:

- Pure data nodes with zero behavior: `CollisionShape2D` inside Area components, `SpriteFrames`, the BlastBullets GDExtension node, phantom_camera addon nodes.
- Code-instantiated scenes whose count is data-driven: server-row entities, `HitZone`s, enchantment rows, profile panels, terrain MultiMesh batch leaves + the chunk pool.

Nodes are declared in the `.tscn`, not built in code, wherever feasible.

## Module Map (`Scripts/`)

### `Components/` + `Entities/` (entity/component framework, modeled on comedot)

Server-authoritative rule: components never mutate health/stats locally. They *mirror* server rows (`SetFromServer(...)`) and emit signals for observers, and they *report* hits via reducers — the server computes damage and the updated row flows back into the mirror.

- `IComponent.cs` / `ComponentRegistration.cs` — the component contract (`Entity` back-reference to the owning `IEntity`) plus the static self-registration logic: `_Ready` registers with the nearest `IEntity` ancestor, calls `OnRegistered()`, then (deferred) `OnEntityReady()` + required-sibling validation. `_ExitTree` unregisters, so dynamically spawned components (e.g. `HitZone`) don't leak in the registry. Sibling access via `GetSibling<T>()` / `Entity.GetComponent<T>()`.
- One base per native root type (C# has single inheritance): `Component.cs` (`Node`), `AreaComponent.cs` (`Area2D` — hitboxes/hurtboxes), `Node2DComponent.cs` (`Node2D`), `Node3DComponent.cs` (`Node3D`), `ControlComponent.cs` (`Control`), `VisualComponent.cs` (`AnimatedSprite2D`). All delegate to `ComponentRegistration`.
- `Entities/IEntity.cs` / `Entity.cs` / `EntityRegistry.cs` — the entity side. Roots needing a native base (`LocalPlayer`/`Enemy : CharacterBody2D`) implement `IEntity` directly with an `EntityRegistry` field; plain-`Node` roots can derive from `Entity`. Registry lookups return the first component assignable to the requested type (subclass-friendly).
- `NodeExtensions.cs` — `GetAncestor<T>()` (works across instanced sub-scene boundaries, unlike `Node.Owner`).

### `Components/Combat/` (scenes in `Scenes/Components/Combat/`)
- `HealthComponent.cs` (`health_component.tscn`) — owns the entity's `Health` Stat as a mirror of server hp/max_hp; `SetFromServer(hp, maxHp)` emits `HealthDidDecrease`/`HealthDidIncrease`/`HealthDidZero`. Deliberately no `Damage()`/`Heal()`.
- `FactionComponent.cs` — `[Flags] Factions { Neutral, Players, Enemies }`; `CheckOpposition` = no shared flag (missing component ⇒ Neutral ⇒ opposed to everything). Static per scene; not server-synced.
- `DamageComponent.cs` (`damage_component.tscn`) — attacker hitbox (`Area2D`). `ReportHits()` finds faction-opposed `DamageReceivingComponent`s in contact and reports them (`ReportEnemyHit` for enemy victims); never computes damage.
- `DamageReceivingComponent.cs` (`damage_receiving_component.tscn`) — victim hurtbox (`Area2D`, no in-scene shape; each entity adds its own shape child). `ProcessHit` emits `DidReceiveDamage`; `ProcessBulletHit` routes BlastBullets2D overlaps to `ReportHit` (via `BulletHitRouterComponent`).
- `HitZone.cs` (`Scenes/Components/hit_zone.tscn`) — `DamageComponent` with a lifetime fuse; spawned by `CombatComponent` along bullet paths.

### `Components/Data/` + `Resources/Stats/`
- `StatsComponent.cs` (`Scenes/Components/Data/stats_component.tscn`) — `Stat` set keyed by `StatKind`; `SetFromServer(kind, value)`, `StatChanged` signal. Shared-Stat pattern: the entity registers `HealthComponent.Health` under `StatKind.Hp` so all observers see one instance.
- `Resources/Stats/Stat.cs` — `[GlobalClass] Resource`: clamped `MinValue`/`MaxValue`/`Value`, `ValueChanged` signal. Data only (comedot's resource rule).
- `Resources/Stats/StatKind.cs` — mirrors the server's `StatKind`. Note the generated bindings also define `SpacetimeDB.Types.StatKind` — qualify when you need that one.

### `Components/Weapon/`
- `CombatComponent.cs` (was `LocalPlayerCombat.cs`) — `Component`; weapon firing from the equipped item's `WeaponBehavior` (fire rate/pattern/range), aim assist/lock-on from `LocalPlayerActiveProfile`, spawns `HitZone`s along bullet paths.

### `Game/` — GameManager + its decomposition (component scenes in `Scenes/Components/`)
- `GameManager.cs` — `Node2D, IEntity`, root node ("Main") of `Scenes/main.tscn`. Now thin entity glue plus a **static facade** delegating to its child components: `Conn`, `Username`, `IsLocal` → `ConnectionComponent`; `LapQ`/`LapR` → `SubscriptionComponent`; `GetItem`, `GetEnchantment(s)`, `EnchantmentsChanged`, `GetResPath` → `CatalogComponent`; `GetEnemy`, `EnemyCount` → `EntitySpawnerComponent`.
- `Components/Connection/ConnectionComponent.cs` (`connection_component.tscn`) — owns the `DbConnection`, pumps `FrameTick()` every frame, `IsLocal` identity check. Connection flow: "Join World" → `Connect()` → `OnConnected()` → subscription builder → `OnSubscriptionApplied()`.
- `Components/Subscription/SubscriptionComponent.cs` (`subscription_component.tscn`) — subscription waves; `LapQ`/`LapR` torus lap vectors from `MapConfig`.
- `Components/Catalog/CatalogComponent.cs` (`catalog_component.tscn`) — the `AllItems`/`AllEnchantments`/texture subscription views as caches (`GetItem`, `GetEnchantment(s)`, `GetResPath`, `EnchantmentsChanged`).
- `Components/Spawning/EntitySpawnerComponent.cs` (`entity_spawner_component.tscn`) — table `OnInsert`/`OnDelete` → spawn/despawn of `LocalPlayer`/`RemotePlayer`/`Enemy`/`Drop`/`BulletManager`, plus the tracking dictionaries (`GetEnemy`, `EnemyCount`).
- `Components/Lobby/LobbyComponent.cs` (`lobby_component.tscn`) — CharSlots UI (moved out of `main.tscn`) and profile panels.
- `BulletManager.cs` — `Node2D, IEntity`, root of `Scenes/bullet_manager.tscn`. Children: `BlastBullets` (plain GDExtension child reached via `[Export]` NodePath, replacing `GetChild(0)`), `Components/Bullets/BulletSpawnerComponent.cs` (`BulletPatternEvent` → per-`PatternType` dispatch to the BlastBullets2D `BulletFactory2D`), `Components/Bullets/BulletHitRouterComponent.cs` (overlap → `ReportHit` routing through the victim's `DamageReceivingComponent`). Keeps a static `Instance` + spawn pass-throughs (`CombatComponent`/`Enemy` have no registry path to it). `BulletData.cs` — small `Resource` carried through the bullet pipeline.
- `LobbyGui.cs` — lobby/menu UI (`Scenes/lobby_gui.tscn`). Currently scene navigation only — not yet wired to `create_profile`/`join_world` reducers.
- `DebugOverlay.cs` — debug HUD (`Scenes/UI/debug_overlay.tscn`, instanced in `main.tscn`); paired with server `main/debug.rs`.

### `Components/Camera/` (scenes in `Scenes/Components/Camera/` + `Scenes/world_3d.tscn`)
- `CameraRigComponent.cs` (was `World/CameraRig.cs`) and `Camera2DPresenterComponent.cs` (was `CameraController2D.cs`) — 2D camera rig/presenter, children of `main.tscn`.
- `World3DComponent.cs` (was `WorldRenderer3D.cs`) — root of `world_3d.tscn` (the 3D backdrop viewport).
- `HexGridOverlayComponent.cs` (was `HexGridOverlay2D.cs`) — hex grid debug overlay, reads `MapConfig`; `HexGridOverlay3DComponent.cs` (was `HexGridOverlay3D.cs`) — its 3D sibling, declared in `world_3d.tscn`.
- The old camera static singletons are gone — reach these via `GameManager.GetComponent<T>()`.

### `Components/Terrain/` (scenes in `Scenes/Components/Terrain/`) — replaces `World/TerrainManager.cs` (deleted)
- `TerrainComponent.cs` (`terrain_component.tscn`, `Node2DComponent`, child of Main) — subscribes to `NearbyTerrainTiles`/`NearbyHexDecor`, stores rows, dirty-flag one-rebuild-per-frame, shared wedge/decor mesh + texture caches, per-hex torus wrap + camera cull. Owns a pool of `TileComponent` instances pre-warmed to the AOI ring count (`1+3R(R+1)`; `[Export] AoiChunkRadius = 2` matching the server's `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`), growing on demand.
- `TileComponent.cs` (`tile_component.tscn`, `Node2D, IEntity`) — per-chunk scaffolding with four declared layer children: `GroundComponent` (z=0), `OverlayComponent` (z=1, centroid-scaled), `DecorShadowComponent` (z=2), `DecorComponent` (z=3). `Populate(chunkIndex, rows…)` fills the layers per frame; `Clear()` parks it back in the pool.
- `TerrainLayerComponent.cs` — base for the four layers; each owns pooled `MultiMeshInstance2D` batch leaves. The leaves stay code-created (data-driven count, deliberate perf design); z-order/scaffolding is declared in the `.tscn`. `DecorLayerComponent.cs` — shared base for the two decor layers (per-texture batches sorted by hex row).
- Rendering semantics unchanged from the old `TerrainManager` (same meshes, transforms, z-order, fallback modulate, decor HexR sort).

### `Components/Movement/` + `Components/Visual/` + `Components/Interaction/`
- `Movement/InterpolationComponent.cs` (`interpolation_component.tscn`) — shared position/rotation lerp toward server targets, torus-aware via `TorusMath.NearestCandidate`; exposes `Moving`. Used by `RemotePlayer` + `Enemy`.
- `Visual/RemoteVisualComponent.cs` — remote player sprite/texture; Walk/Idle driven by `InterpolationComponent.Moving`.
- `Visual/DropVisualComponent.cs` — drop item texture.
- `Interaction/PickupComponent.cs` (`pickup_component.tscn`) — `AreaComponent` with declared `CollisionShape2D` + `PickupLockTimer` children; body-entered → `PickupDrop` reducer.

### `Components/Inventory/` (UI scene: `Scenes/UI/inventory_panel.tscn`)
- `InventoryComponent.cs` (was `Players/Local/LocalPlayerInventory.cs`) — `ControlComponent`; hotbar/backpack/equipment slot UI, listens for `LocalPlayer.InventoryChanged`, calls `UseItem` reducer on hotbar key press. Hotbar/Backpack stay plain containers (zero behavior) inside `inventory_panel.tscn`.
- `SlotComponent.cs` (was `InventorySlotUI.cs`) — single inventory slot — drag/drop (`SwapSlots` reducer), right-click drop (`DropItem` reducer), hover shows `ItemSidebarComponent`.
- `StatsSidebarComponent.cs` (was `PlayerStatsSidebar.cs`) — player stat panel.
- `ItemSidebarComponent.cs` (was `ItemStatsSidebar.cs`; singleton removed) — hovered slot's item name/icon/description plus full composition: base stat modifiers, behavior summary, socketed enchantments (`N / MaxEnchantments`), and — for enchantable items in equipment slots — applicable enchantments with Socket/Remove buttons calling the `ApplyEnchantment`/`RemoveEnchantment` reducers. Enchantment rows are instantiated from `Scenes/UI/enchantment_row.tscn` instead of code-built. Refreshes on `InventoryChanged` and `EnchantmentsChanged`; stays open while the mouse is over it.
- The whole inventory UI (hotbar, 24 slot panels, both sidebars) lives in `Scenes/UI/inventory_panel.tscn`, instanced into `local_player.tscn`. Scripts inside it reach the player via `GetAncestor<LocalPlayer>()`, not `Owner`.

### `Players/Local/`
- `LocalPlayer.cs` — `CharacterBody2D, IEntity`; WASD movement, sends `ReportMovement`, owns inventory slot state (`ResolveSlotAt`/`GetSlotItemId`, fires `InventoryChanged`). Composition root of `local_player.tscn`: children literally named `StatsComponent`, `HealthComponent`, `FactionComponent`, `DamageReceivingComponent` (was "Hurtbox"), `CombatComponent` (was "Combat") hold the mirrored stat/hp values — `LocalPlayer` feeds them from `LocalPlayerData`/`LocalPlayerStats` rows and exposes pass-through properties (`Hp`, `MaxHp`, `Strength`…) so UI readers (e.g. `StatsSidebarComponent`) stay stable. Combat logic lives in `Components/Weapon/CombatComponent.cs`.

### `Players/Remote/`
- `RemotePlayer.cs` — `Node2D, IEntity` root of `non_local_player.tscn` (EntityRegistry boilerplate + thin glue). The work lives in its children: `InterpolationComponent` (lerp toward subscribed positions) and `RemoteVisualComponent` (texture + Walk/Idle).

### `Players/Enemies/`
- `Enemy.cs` — `CharacterBody2D, IEntity`; server-driven puppet. Feeds row hp + `EnemyTemplate` max_hp into its `HealthComponent`/`StatsComponent` and forwards matching `BulletPatternEvent` rows to `BulletManager`. `default_enemy.tscn` children: `StatsComponent`, `HealthComponent`, `FactionComponent` (Enemies), `DamageReceivingComponent` (Enemy collision layer — what `HitZone`s detect instead of the physics body), `InterpolationComponent` (drives the lerp toward `NearbyEnemies` positions).

### `Items/`
- `Drop.cs` — `Node2D, IEntity` root of `drop.tscn` (EntityRegistry boilerplate only). Children: `DropVisualComponent` (item texture) + `PickupComponent` (the `Area2D` that calls the `PickupDrop(drop_id)` reducer when the local player enters).

### `World/`
- `TorusMath.cs` — torus wrap / nearest-candidate math (`NearestCandidate`). The only file left here — everything else moved to `Components/`.

## Setup notes
- No autoloads — `GameManager` is instantiated as the root node ("Main") of `Scenes/main.tscn`; reach cross-cutting state through its static facade or `GetComponent<T>()` on it.
- `khvg.csproj` (project root) has the `SpacetimeDB.ClientSDK` package reference — see the NuGet warning in the root `CLAUDE.md` if it's ever lost.
- **BlastBullets2D** GDExtension plugin (`client/addons/blastbullets2d`) must be present; `BulletManager` dispatches patterns through its `BlastBullets` factory child node.
