# 02 The Component Framework

## Assumed knowledge

None — this is the foundational doc; everything else in the series builds on the vocabulary defined here. [[00 End-to-End Timeline Flowchart]] is the runtime map this framework plugs into, and [[01 Roadmap]] explains the doc set's conventions. When a section needs deeper context it links the system doc that owns it ([[03 Boot & Connection]] for the connection layer, [[05 Joining the World]] for spawning, [[09 Combat & Damage]] for why components never compute damage).

## The 30-second version

The client has no god-objects. Every gameplay thing — the local player, a remote puppet, an enemy, a drop, even the `game.tscn` scene root — is an **entity**: a scene root implementing [[client/Scripts/Entities/IEntity.cs|IEntity]] whose children are single-purpose **components** implementing [[client/Scripts/Components/IComponent.cs|IComponent]]. On `_Ready` each component walks up its ancestors, registers with the *nearest* entity, and gets lifecycle hooks (`OnRegistered`, then a deferred `OnEntityReady` once all siblings exist). Server rows reach components through one bridge — a [[client/sstdbsdk/TableBinderComponent.cs|TableBinderComponent]] child per subscribed table, re-firing row events as editor-wireable Godot signals. The server mirrors the same shape: one table per concern is a component, ids like `profile_id`/`enemy_id` are the entity id, and "archetype helpers" (`try_scaffold_profile`/`teardown_profile`, `spawn_enemy_archetype`/`despawn_enemy_archetype`) insert or delete one entity's whole row set at once.

## Flowcharts

- [[flowcharts/main-framework.canvas]] — the composed flow for this doc (composed later from `flowcharts/flows.json`'s `framework` entry; expected to be unresolved until then).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Components_subfolder.canvas]] — the component base classes plus every concrete component, one canvas over `Scripts/Components/`.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Entities_subfolder/Entities_subfolder.canvas]] — `IEntity` / `Entity` / `EntityRegistry`, the entity-side trio.
- [[flowcharts/Subflowcharts/client_subfolder/sstdbsdk_subfolder/TableBinderComponent_codefile/TableBinderComponent_codefile.canvas]] — deep dive on the table-to-signal bridge.

## Main body

### Why a component framework at all

A Godot game is a tree of **nodes** declared in `.tscn` scene files, where each node runs at most one attached script. The naive shape for an MMO client is one enormous `Player.cs` that moves, renders, tracks health, owns the inventory, and talks to the network — which is exactly what this project's pre-refactor client had (`LocalPlayerCombat`, `LocalPlayerInventory`, `TerrainManager`, `CameraController2D`, `CameraRig` — all since deleted). The refactor, modeled on the Comedot framework, replaced each of those monoliths with an entity root plus a fan of `<Role>Component` children. The payoffs that show up everywhere else in these docs: a new concern is a new child node instead of new code in a shared class; the `.tscn` file *is* the wiring diagram (node name = class name = file name, per `client/AGENTS.md`'s naming convention); and the server-authoritative rule is enforceable per component — each mirror component exposes `SetFromServer(...)` and nothing that mutates state locally.

### The contract: `IComponent` and `IEntity`

[[client/Scripts/Components/IComponent.cs|IComponent]] is deliberately tiny — one member, an `Entity` back-reference to the owning `IEntity` (null before registration and after `_ExitTree`). That is the entire contract a component must satisfy, which is what lets one registry hold an `Area2D` hitbox, a `Control` inventory panel, and a plain `Node` logic component side by side.

[[client/Scripts/Entities/IEntity.cs|IEntity]] is the other side: `RegisterComponent` / `UnregisterComponent` / `GetComponent(Type)`, plus a default-implemented generic `GetComponent<T>()` that just forwards to the `Type` version. C# has single inheritance, so a root that needs a native Godot base — `LocalPlayer` and `Enemy` need `CharacterBody2D` physics, `GameManager`/`RemotePlayer`/`Drop`/`BulletManager`/`TileComponent` need `Node2D` — cannot also derive from a shared "entity node" class. Instead each root implements `IEntity` directly and forwards the three methods to a private [[client/Scripts/Entities/EntityRegistry.cs|EntityRegistry]] field. `EntityRegistry` itself is a flat `List<IComponent>` with one subtlety in [[client/Scripts/Entities/EntityRegistry.cs#Get#1|EntityRegistry.Get]]: lookup returns the first registered component for which `type.IsInstanceOfType(component)` is true — so a query for a base type (say `TerrainLayerComponent`) matches subclasses, but when two siblings share a base the winner depends on registration order. The [[client/Scripts/Entities/Entity.cs|Entity]] convenience class (a plain-`Node` root with the registry baked in) exists for roots without native-base needs, though every entity in the codebase today needs one and implements `IEntity` directly.

### The six component bases and the registration lifecycle

Components have the same single-inheritance problem in reverse: a hitbox *is* an `Area2D`, a sprite *is* an `AnimatedSprite2D`, so there is one abstract base per native root type — [[client/Scripts/Components/Component.cs|Component]] (`Node`), [[client/Scripts/Components/AreaComponent.cs|AreaComponent]] (`Area2D`, hitboxes/hurtboxes), [[client/Scripts/Components/Node2DComponent.cs|Node2DComponent]] (`Node2D`), [[client/Scripts/Components/Node3DComponent.cs|Node3DComponent]] (`Node3D`), [[client/Scripts/Components/ControlComponent.cs|ControlComponent]] (`Control` UI), and [[client/Scripts/Components/VisualComponent.cs|VisualComponent]] (`AnimatedSprite2D`). All six contain identical lifecycle code because they cannot share a base; the shared logic that *can* be static lives in [[client/Scripts/Components/ComponentRegistration.cs|ComponentRegistration]].

The lifecycle, from [[client/Scripts/Components/Component.cs#_Ready#1|Component._Ready]]:

1. **Register.** [[client/Scripts/Components/ComponentRegistration.cs#Register#1|ComponentRegistration.Register]] walks `GetParent()` ancestors and registers the component with the *nearest* `IEntity`, logging a `PushError` if there is none. Nearest-wins is the load-bearing rule: a `HitZone` added under `CombatComponent` under `LocalPlayer` under the `game` root passes two entities on the way up and correctly binds to `LocalPlayer`, not `GameManager`.
2. **`OnRegistered()`** runs immediately — the component may now use `GetSibling<T>()` for siblings that registered before it, but sibling order in the scene is not a guarantee, which is why anything order-sensitive waits for step 3.
3. **Deferred finish.** `CallDeferred(nameof(AfterSiblingsReady))` schedules the second half after every sibling's own `_Ready` has run: [[client/Scripts/Components/ComponentRegistration.cs#ValidateRequired#1|ComponentRegistration.ValidateRequired]] `PushWarning`s for each type in `GetRequiredComponents()` that has no registered sibling, then `OnEntityReady()` runs. The declared-requirement checks in the codebase are real dependency statements — `DamageReceivingComponent` requires `FactionComponent` (it can't filter hits without a faction), `LocalPlayerDataComponent` requires `HealthComponent` + `StatsComponent` (it exists to feed those mirrors), `BulletControllerComponent` requires `BulletSpawnerComponent` (it manipulates the spawner's live bullet list).
4. **Unregister on exit.** `_ExitTree` unregisters from the entity, so dynamically spawned components — `HitZone`s `QueueFree`d after their fuse, despawned puppets — never leak stale entries in a registry.

Two components sit outside the six bases and prove the contract is the interface, not the inheritance: [[client/Scripts/Components/Visual/DropVisualComponent.cs|DropVisualComponent]] (a `Sprite2D`) implements `IComponent` directly and calls `ComponentRegistration.Register` itself, skipping the deferred/`GetRequiredComponents` machinery it doesn't need. And [[client/sstdbsdk/TableBinderComponent.cs|TableBinderComponent]] extends `Component` but is `[Tool]` (it runs in the editor to render its inspector dropdown), so its `_Ready` skips registration under `Engine.IsEditorHint()` — in the editor there is no `IEntity` ancestor because the entity scripts aren't tool-enabled.

For reaching *up* instead of across, [[client/Scripts/Components/NodeExtensions.cs#GetAncestor#1|GetAncestor<T>()]] walks `GetParent()` for the first ancestor of a type — unlike `Node.Owner` it crosses instanced sub-scene boundaries. Its live users (`Enemy`, `LocalPlayer`, `LobbyComponent`) all call `GetAncestor<GameManager>()` to reach the scene-level entity above them. Components rarely need it for their own entity, since registration already gives them `Entity` and `GetSibling<T>()` — e.g. `SlotComponent` (deep inside the instanced `inventory_panel.tscn`, whose nearest `IEntity` ancestor is the `LocalPlayer`) opens its item sidebar with `GetSibling<ItemSidebarComponent>()`, no node paths involved.

### The entity census

Seven scene roots are entities today, and the pattern for reading any of them is "root holds glue + identity, children hold behavior":

- `Scenes/game.tscn` — root node `game` (a `Node2D`) running [[client/Scripts/Game/GameManager.cs|GameManager]], the scene-level entity: `TableSubscriber`, `CatalogComponent`, `EntitySpawnerComponent`, the camera/terrain/overlay components all register here. (The root's node name is `game` — both GameManager's own doc comment, which says "Main", and the stale "Characters" in older notes are wrong; the `.tscn` wins.)
- `Scenes/local_player.tscn` — `LocalPlayer : CharacterBody2D, IEntity`; its per-table concerns (`PositionSyncComponent`, `LocalPlayerDataComponent`, `LocalPlayerInventoryComponent`, `LocalPlayerProfileComponent`) are child components fed by binders wired in that scene.
- `Scenes/non_local_player.tscn` — `RemotePlayer : Node2D, IEntity`; the work lives in `InterpolationComponent` + `RemoteVisualComponent`.
- `Scenes/default_enemy.tscn` — `Enemy : CharacterBody2D, IEntity`, a server-driven puppet whose two binders (`NearbyEnemiesBinder`, `BulletPatternEventBinder`) are declared at its root.
- `Scenes/drop.tscn` — `Drop : Node2D, IEntity` with `DropVisualComponent` + `PickupComponent`.
- `Scenes/bullet_manager.tscn` — `BulletManager : Node2D, IEntity`, instanced directly in `game.tscn` (not row-driven); its bullet components register with it.
- `Scenes/Components/Terrain/tile_component.tscn` — `TileComponent : Node2D, IEntity`; its four terrain *layer* children are components, which is why a per-chunk tile can be pooled and cleared as one unit.

The 3D backdrop exercises the ancestor walk's most surprising hop: the `Node3DComponent`s inside `Scenes/world_3d.tscn` live under a `SubViewport` in `game.tscn`, and their parent walk crosses the viewport boundary to register with `GameManager` — the `Node3DComponent` doc comment calls this out explicitly, and it works because `SubViewport` is itself a node in the same tree.

### GameManager: the facade over the scene-level entity

```sync
![[00 End-to-End Timeline Flowchart#^lobby-2{seamless:true,title:false,marker:01.}]]
```

What the timeline step doesn't spell out is *why* the static facade exists. A component reaches its own entity's siblings through `Entity.GetComponent<T>()`, but a spawned entity (a `RemotePlayer` puppet, a `Drop`) registers with *itself* — its registry can't see the scene-level components that live under the `game` root. Node-path walks (`GetNode("/root/game/...")`) would hard-code scene layout into every consumer. So [[client/Scripts/Game/GameManager.cs|GameManager]] keeps a private static `instance` (set in `_Ready`, cleared in `_ExitTree`) and re-exposes its registered components through static members: `LapQ`/`LapR` delegate to `Get<TableSubscriber>()`, `GetItem`/`GetEnchantment(s)`/`GetResPath`/`EnchantmentsChanged` to `Get<CatalogComponent>()`, `GetEnemy`/`EnemyCount` to `Get<EntitySpawnerComponent>()`, and `Conn`/`Username`/`IsLocal` straight to the `DatabaseConnector` autoload. Every accessor is null-safe (`Get<T>()` returns null once the instance is freed), because UI and debug code can outlive the scene that owned the components. For anything not on the facade, the instance method `GetComponent<T>()` is the escape hatch — the old pre-refactor static singletons (`CameraRig` and friends) are gone.

### TableBinderComponent: the table-to-signal bridge

```sync
![[00 End-to-End Timeline Flowchart#^conn-6{seamless:true,title:false,marker:02.}]]
```

The step above is the what; here is the how, all in [[client/sstdbsdk/TableBinderComponent.cs|TableBinderComponent.cs]]. Godot **signals** are the engine's editor-wireable event mechanism — a `[Signal]`-declared event one node's handler can subscribe to in the `.tscn` file itself — but SpacetimeDB row objects are plain C# classes, not Godot `Variant`s, so they cannot ride inside a signal's arguments. The binder resolves that mismatch by keeping the row on itself: `HandleInsert`/`HandleUpdate`/`HandleDelete` stash the row in `LastRow` / `LastOldRow` / `LastDeletedRow` and then emit the argument-less `RowInserted` / `RowUpdated` / `RowDeleted`, and the handler (wired in the scene) reads and casts the property. The hookup is reflection over the generated bindings: [[client/sstdbsdk/TableBinderComponent.cs#Bind#1|Bind]] looks up the `TableName` string as a field on `RemoteTables`, and `Hook` recovers each event's row type from its delegate signature to close the generic `Handle*<TRow>` methods — which is also why event tables like `BulletPatternEvent`, which have no `OnUpdate`/`OnDelete`, bind without error (a missing event just skips that hook).

Three design details carry most of the system's ergonomics. First, `ReplayExistingRows`: after binding, the binder iterates the already-cached rows and re-fires them through the insert path, so a component that binds late (or a puppet spawned after the rows arrived) initializes through the exact same code path as live inserts — no separate `Iter()` bootstrap loops. Second, the inspector integration: `[Tool]` plus `_GetPropertyList` turns `TableName` into a dropdown fed by `TableSubscriber.AllSubscribedTables`, and `_GetConfigurationWarnings` puts a warning triangle on the node if the bound table isn't in any subscription wave — a misspelled or unsubscribed table fails in the editor, not silently at runtime. Third, `_ExitTree` removes every table event handler it added, so a freed puppet can never leave a callback pointing at a disposed node. Binder children are named `<Table>Binder` (`NearbyEnemiesBinder`, `LocalPlayerBinder`, …) rather than the class name — the one sanctioned exception to node-name-equals-class-name — and the wiring is all in the live scenes: see the `[connection signal="RowInserted" from="EntitySpawnerComponent/NearbyEnemiesBinder" ...]` lines in [[client/Scenes/game.tscn|game.tscn]] or the two root-level binder connections in [[client/Scenes/default_enemy.tscn|default_enemy.tscn]].

### The mirror rule: components never write back

The framework has one behavioral law on top of the mechanics, stated in `client/AGENTS.md` and visible in every mirror component: server state flows *into* components through `SetFromServer(...)`-style methods (`HealthComponent.SetFromServer`, `StatsComponent.SetFromServer`, `PickupComponent.SetFromServer`), and the only way *out* is a reducer call (`ReportEnemyHit`, `ReportMovement`, `SwapSlots`). `HealthComponent` deliberately has no `Damage()` or `Heal()`. This is what makes the server-authoritative model survivable in a compositional codebase: with dozens of components touching the same conceptual state, any local mutation would desync silently; with the rule, a bug in one component corrupts only that component's mirror, and the next row update heals it. The full damage round-trip — report, server compute, row echo — is [[09 Combat & Damage]]'s story.

### The server-side mirror: tables as components, helpers as archetypes

The composition model is not client-only. On the server there are no objects at all — a SpacetimeDB module stores everything in tables — so the mapping is structural: **one table per concern is a component**, and an id column (`profile_id`, `enemy_id`, `behavior_id`) is the entity id that joins one logical entity's rows across tables. A "player character" is a `PlayerData` row + `PlayerStats` + `PlayerStatAllocation` + 38 `PlayerInventorySlot` rows + `PlayerPosition`/`PlayerRotation`/`PlayerChunk`, exactly as its client puppet is a `LocalPlayer` root plus its data/movement/inventory components. And where the client builds an entity by instancing a scene, the server builds one through an **archetype helper** — one function that inserts the whole row set, with a matching helper that deletes it:

```sync
![[00 End-to-End Timeline Flowchart#^join-2{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^enemy-3{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^end-2{seamless:true,title:false,marker:05.}]]
```

The free-prose details the steps compress:

- **Scaffolding is idempotent by construction.** Every block in [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile#1|try_scaffold_profile]] is guarded by an existence check (`if … is_none()`), so re-joining after `leave_world` — where profile rows deliberately survive — reuses the existing rows, while re-joining after death (which ran the teardown) rebuilds from scratch. The function never needs to know which case it's in.
- **The teardown list is the single source of truth for "what belongs to a profile".** [[server/spacetimedb/src/player/methods.rs#teardown_profile#1|teardown_profile]] is called by both combat death and `delete_profile`, so adding a new per-profile table means adding it in exactly one place; its doc comment says as much. Callers handle the identity-level rows themselves (death keeps `PlayerProfile` and moves the identity to `LoggedOutPlayer`; `delete_profile` removes the profile while the player sits in the lobby) — the helper owns components, the caller owns the entity id. Known hole: it does not delete the `PlayerRotation` row (see Known gaps in [[13 Disconnect & Teardown]]).
- **The enemy tree is two levels deep, and the helpers wrap the pair.** [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype#1|spawn_enemy_archetype]] = [[server/spacetimedb/src/enemy/methods.rs#build_enemy_behavior#1|build_enemy_behavior]] + the flat `Enemy` row pointing at it by `behavior_id`; [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype#1|despawn_enemy_archetype]] = [[server/spacetimedb/src/enemy/methods.rs#delete_enemy_behavior#1|delete_enemy_behavior]] + deleting the flat row. The flat row carrying position/hp/phase is deliberate — the 10 Hz sim updates and the AOI view touch one row, not a tree. Note the per-enemy *instance* rows inside the tree: a `Repeat` step's shared `RepeatStepDef` is copied into a private `RepeatStepInstance` at spawn so repeat counters advance per enemy — instance data vs. shared def data is the server equivalent of "component state lives on the component, not the class".
- **Every caller routes through the helpers.** `join_world`, combat kills, the admin `spawn_enemy`/`despawn_enemy` reducers — no call site inserts or deletes archetype rows directly, which is why orphan behavior rows and half-scaffolded profiles don't occur. When you add an entity kind server-side, the helpers are the interface you write first.

## Known gaps / stubs

- **Seven unreferenced duplicate component scenes** — drift hazards, not wiring sites: `Scenes/Components/Spawning/entity_spawner_component.tscn`, `Scenes/Components/Terrain/terrain_component.tscn`, `Scenes/Components/Camera/camera_rig_component.tscn`, `Scenes/Components/Camera/camera_2d_presenter_component.tscn`, `Scenes/Components/Camera/hex_grid_overlay_component.tscn`, `Scenes/UI/debug_overlay.tscn`, and `Scenes/Components/Combat/damage_component.tscn` (`DamageComponent`'s only live use is as `HitZone`'s base class). Each duplicates wiring the live scenes now declare inline (`game.tscn`, `local_player.tscn`, `default_enemy.tscn`, `non_local_player.tscn`, `world_3d.tscn`); verified unreferenced — no `.tscn` references any of them. Don't instance, edit, or cite them as wiring sites. (The earlier list of nine also named `catalog_component.tscn` and `subscription_component.tscn`; those two have since been deleted.)
- **Stale identity of the `game.tscn` root.** The root node is named `game`, but `GameManager.cs`'s own doc comment says "Main" and `client/AGENTS.md` says "Characters" — both stale. Harmless, but confusing when cross-referencing the scene.
- **`Entity` (the plain-`Node` convenience base) has no users today** — every current entity root needs a native base and implements `IEntity` directly with an `EntityRegistry` field. It's infrastructure awaiting a plain-`Node` entity, not dead code with a bug.
- Leftover `GD.Print` DEBUG lines in `TableBinderComponent.Bind`/`HandleInsert` are owned by [[03 Boot & Connection]]'s gap list; mentioned here only because the binder is this doc's subject.

## Where to go next

The framework exists to deliver server rows to behavior, so the natural next read is [[03 Boot & Connection]] — how the connection, autoload, and subscription waves that feed every binder come up. After that, [[05 Joining the World]] shows the framework at full stretch: row inserts becoming spawned entities, each assembling itself from replayed rows through the lifecycle hooks defined here.
