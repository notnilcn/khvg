# 02 The Component Framework

## Assumed knowledge

[[01 Roadmap]] for how these docs are organized. This doc covers machinery, not tables — it has no table entries of its own. For the read path every table shares (subscriptions, views, binders), read [[03 Connection, Subscriptions & Views]] next.

## The 30-second version

The client is 100% C# (Godot 4.7, .NET 8) and fully compositional, modeled on the **Comedot** framework: every high-level node is an **entity** carrying small **components** named `<Role>Component`, declared in `.tscn` scene files rather than built in code. Components never compute damage or mutate stats locally — they *mirror* server rows and *report* intent through reducers, because the server is authoritative. The server mirrors the same shape back: **one table per concern = a component**, with ids like `profile_id`/`enemy_id` acting as entity ids joining a logical entity's rows across tables, and **archetype helpers** (`try_scaffold_profile`/`teardown_profile`, `spawn_enemy_archetype`/`despawn_enemy_archetype`) that create and destroy a whole entity's row-set as one unit — the server-side counterpart of spawning a scene.

## Find it fast

| Question / concept | Where |
|---|---|
| How do I add behavior to an entity? | [[#Component base classes]] — pick the base matching the node's native type, override `OnRegistered` / `OnEntityReady` |
| How does a component find its siblings? | [[#Component base classes]] — `GetSibling<T>()` after the deferred ready pass |
| How is an entity defined, and where does the registry live? | [[#Entities and the registry]] |
| Why is node name = class name = file name? | [[#Naming and scene composition]] |
| How do I consume a table row in a component? | [[03 Connection, Subscriptions & Views]] — binder children wired in the `.tscn` |
| How does a profile's full row-set get created / destroyed? | [[#The server-side mirror — archetype helpers]] — `try_scaffold_profile` / `teardown_profile` |
| How does an enemy's full row-set get created / destroyed? | [[#The server-side mirror — archetype helpers]] — `spawn_enemy_archetype` / `despawn_enemy_archetype` |
| Where is damage actually computed? | Nowhere on the client — the combat pipeline (`06 Combat Tables`) on the server; components only mirror hp and report hits |
| What are the six stats vs the modifier-only ones? | [[#The server-side mirror — archetype helpers]] — `PlayerStats` vs `PlayerData.defense`/`base_speed`/`max_hp` |

## Flowcharts

- [[flowcharts/main-framework.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Components_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Entities_subfolder/Entities_subfolder.canvas]]

## Component base classes

A **component** is a node that attaches to an entity and does one job (health mirroring, movement syncing, inventory state). A **scene** (`.tscn` file) is Godot's prefab format — a saved node tree with exported properties and signal connections; the game instantiates scenes like `local_player.tscn` at runtime instead of building node trees in code.

C# has single inheritance, so the framework provides **one base class per native root type** — you pick the base that matches what your node *is*:

- [[client/Scripts/Components/Component.cs#Component|Component]] (`Node`) — the default for logic-only components (`CombatComponent`, `ElevationMapComponent`).
- [[client/Scripts/Components/AreaComponent.cs#AreaComponent|AreaComponent]] (`Area2D`) — hitboxes and hurtboxes (`DamageReceivingComponent`, `HitZone`, `PickupComponent`, `RemoteHitWitnessComponent`).
- [[client/Scripts/Components/Node2DComponent.cs#Node2DComponent|Node2DComponent]] (`Node2D`) — 2D spatial components (`TerrainComponent`, `EnemyDebuffIndicatorComponent`).
- [[client/Scripts/Components/Node3DComponent.cs#Node3DComponent|Node3DComponent]] (`Node3D`) — 3D spatial components (`TerrainComponent3D`).
- [[client/Scripts/Components/ControlComponent.cs#ControlComponent|ControlComponent]] (`Control`) — UI components that participate in the entity (`SlotComponent`, `StatusBarComponent`).
- [[client/Scripts/Components/VisualComponent.cs#VisualComponent|VisualComponent]] (`AnimatedSprite2D`) — animated-sprite components.

The whole component contract in [[client/Scripts/Components/IComponent.cs#IComponent|IComponent]] is one member: an `Entity` back-reference to the owning `IEntity` (null before `_Ready`, null again after `_ExitTree` — Godot's enter-tree/exit-tree callbacks, the constructor/destructor equivalents for nodes).

The lifecycle lives in the base classes, not in registration: each base's `_Ready` registers with the nearest `IEntity` ancestor, runs `OnRegistered()`, then `CallDeferred`s a second pass that validates `GetRequiredComponents()` and runs `OnEntityReady()` — deferred so every sibling has finished its own `_Ready` first, which is why sibling access is only safe in `OnEntityReady`, via `GetSibling<T>()`. `_ExitTree` unregisters, so dynamically spawned components (e.g. `HitZone`) don't leak in the registry. You override `OnRegistered`, `OnEntityReady`, and `GetRequiredComponents` — and almost nothing else.

[[client/Scripts/Components/ComponentRegistration.cs#Register|ComponentRegistration]] holds only the two static helpers those bases share: `Register(node, component)` walks ancestors for the nearest `IEntity` (using `GetAncestor<T>()` from [[client/Scripts/Components/NodeExtensions.cs#GetAncestor|NodeExtensions]], which works across instanced sub-scene boundaries, unlike `Node.Owner`) and `PushError`s if there is none; `ValidateRequired(node, entity, types)` emits a `PushWarning` per missing sibling.

## Entities and the registry

The entity side is [[client/Scripts/Entities/IEntity.cs#IEntity|IEntity]] (`RegisterComponent` / `UnregisterComponent` / `GetComponent(Type)` plus a default-implemented generic `GetComponent<T>()`), implemented two ways: roots that need a native base (`LocalPlayer` / `Enemy`, both `CharacterBody2D` — Godot's physics-body node) implement `IEntity` directly with an `EntityRegistry` field, while plain-`Node` roots derive from [[client/Scripts/Entities/Entity.cs#Entity|Entity]].

[[client/Scripts/Entities/EntityRegistry.cs#Get|EntityRegistry]] itself is deliberately simple — a flat `List<IComponent>` whose `Get(Type)` returns the first component the type `IsInstanceOfType`. That makes lookups subclass-friendly but **order-dependent** when two components share a base, and results are memoized per query type in a cache cleared on Register/Unregister, so repeated `Get` calls are O(1) with identical semantics.

## Naming and scene composition

Every high-level parent node's children are components named `<Role>Component`, and **node name = class name = file name** — the `HealthComponent` node in a `.tscn` is an instance of `HealthComponent.cs`. The rule has exactly three exception families: pure data nodes with zero behavior (`CollisionShape2D` shapes inside Area components, `SpriteFrames`, the BlastBullets factory node, phantom_camera addon nodes); `TableBinderComponent` children, whose node name is `<Table>Binder` (e.g. `NearbyEnemiesBinder`) because there is one per consumed table and the table name is the useful identifier; and code-instantiated scenes whose count is data-driven (server-row entities, `HitZone`s, enchantment rows, profile panels, terrain batch leaves, the chunk pool).

The bias is always toward declaring nodes in the `.tscn` and wiring signals there, not building trees in code — which is why the live wiring sites are scene files: binder/signal wiring lives inline in [[client/Scenes/game.tscn|game.tscn]], [[client/Scenes/local_player.tscn|local_player.tscn]], [[client/Scenes/default_enemy.tscn|default_enemy.tscn]], [[client/Scenes/non_local_player.tscn|non_local_player.tscn]], and [[client/Scenes/world_3d.tscn|world_3d.tscn]]. Cite the live scene, never a standalone component scene.

## The server-side mirror — archetype helpers

The server is authoritative, so the client never computes damage or mutates stats locally: components *mirror* rows via `SetFromServer(...)` (e.g. `HealthComponent.SetFromServer(hp, maxHp)`, which emits `HealthDidDecrease` / `HealthDidIncrease` / `HealthDidZero` for observers) and *report* hits via reducers — the updated row then flows back into the mirror. The server's schema mirrors the composition back as **one table per concern = a component**: `PlayerPosition`, `PlayerData`, `PlayerStats`, `PlayerInventorySlot`, `Enemy`, and so on are each one concern of a logical entity, joined by `profile_id` / `enemy_id` as the entity id. An enemy's flat row (position/hp/phase on `Enemy` itself) is deliberate — behavior-tree tick state lives on the `EnemyBehavior` / `EnemyPhase` / `EnemyAttack` / `EnemySequenceStep` / `RepeatStepInstance` side tables instead.

Because an entity's rows span many tables, creating or destroying one is always done through **archetype helpers** that bundle the whole row-set, so no orphan rows ever exist:

- [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]] / [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] — scaffold deals the full profile row-set (data, stats, allocation, all 39 inventory-slot rows, position, rotation, chunk, movement state); teardown runs one union cleanup list used by BOTH `kill_player` death cleanup and `delete_profile` (allocation and rotation rows included).
- [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype|spawn_enemy_archetype]] / [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype|despawn_enemy_archetype]] — spawn builds the behavior tree (`EnemyBehavior` → `EnemyPhase` → `EnemyAttack` → `EnemySequenceStep` → per-enemy `RepeatStepInstance` copies) under the `Enemy` row; despawn removes the whole tree. Admin `spawn_enemy` / `despawn_enemy` route through these (despawn additionally calls `on_enemy_killed` so no status rows reference the dead enemy), as do the scheduled spawn tick and combat kills.

Two resolution rules live at this layer and everything else obeys them. **`recompute_stats` is the one place stats resolve**: allocated skill points, every `StatModifier` from equipped items (socketed *and innate* enchantments included), and active consumable buffs fold into per-`StatKind` `(flat, mult)` accumulators, then resolve as `(base + flat) * (1 + mult)` — mults are additive, never compounding. And **"equipped" is an explicit `SlotRole` check**: only Weapon/Accessory/Armor heads contribute at scale 1.0, Ability heads contribute position-scaled, span followers never contribute; `StatKind::Hp` / `Defense` / `BaseSpeed` are modifier-only (no `PlayerStats` field, not allocatable) and resolve into `PlayerData.max_hp` / `defense` / `base_speed` instead.

## Where to go next

[[03 Connection, Subscriptions & Views]] explains how rows travel from these tables to these components — the read path every table in `00 Table Map.md` shares. Then [[04 Anticheat Tables]] begins the module docs with the smallest one.
