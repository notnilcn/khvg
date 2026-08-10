# 02 The Component Framework

## Assumed knowledge

- [[01 Roadmap]] — what these docs are, who they're for, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine this doc hangs off of. The framework itself is not a runtime beat, so this doc has no timeline steps of its own.
- The maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

The client has no monolithic "player" or "enemy" classes. Instead, every scene root that needs behavior is an **entity** — a node implementing `IEntity` — and every piece of behavior is a **component**: a child node that, on `_Ready`, walks up its ancestors, finds the nearest entity, and registers itself in that entity's component list. Components then find each other by asking the entity (`GetComponent<T>()`), never by node paths. Because C# allows only one base class, the registration lifecycle is duplicated across one abstract base per Godot root type (`Component`, `AreaComponent`, `Node2DComponent`, `Node3DComponent`, `ControlComponent`, `VisualComponent`) with the shared logic factored into the static `ComponentRegistration` helper. The server mirrors the same idea relationally: one SpacetimeDB table per concern plays the role of a component, an id like `profile_id` or `enemy_id` joins one logical entity's rows across tables, and "archetype helper" functions (`try_scaffold_profile`/`teardown_profile`, `spawn_enemy_archetype`/`despawn_enemy_archetype`) insert or delete the whole row-bundle of one entity at once — the server-side equivalent of instancing or freeing a composed scene.

## Flowcharts
- [[flowcharts/main-framework.canvas]] — the composed framework flow (client component/entity cores, the `sstdbsdk` binding layer, `GameManager`, and the server-side mirror modules).
![[flowcharts/main-framework.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Components_subfolder.canvas]] — deep dive: every component base class and concrete component.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Entities_subfolder/Entities_subfolder.canvas]] — deep dive: `IEntity`, `Entity`, `EntityRegistry`.
- [[flowcharts/Subflowcharts/client_subfolder/sstdbsdk_subfolder/sstdbsdk_subfolder.canvas]] — deep dive: the connection/subscription/binding layer every component consumes rows through.

## Main body

### The Godot concepts this builds on

A Godot game is a tree of **nodes**; nodes have a lifecycle (`_Ready` runs when a node enters the tree, `_ExitTree` when it leaves) and can find each other by path or by walking parents. A **scene** (a `.tscn` file) is a saved subtree that can be instanced inside other scenes — `main.tscn` is the running game's root scene, and `local_player.tscn` is instanced into it per player. The framework uses exactly two tree facts: children can walk *up* to find an ancestor, and `_Ready` runs bottom-up, so children are ready before their parents.

### The contract: `IComponent` and `IEntity`

The entire component-side contract is one member: [[IComponent.cs##public interface IComponent|IComponent]] exposes an `Entity` back-reference to the owning entity, null before registration and after `_ExitTree`. That single property is what lets the registry hold components regardless of whether their Godot root is a `Node`, `Area2D`, or `AnimatedSprite2D`.

The entity side is [[IEntity.cs##public interface IEntity|IEntity]]: `RegisterComponent`/`UnregisterComponent`/`GetComponent(Type)`, plus a default-implemented generic `GetComponent<T>()`. It exists as an interface — not a base class — because entity roots need their own native Godot base: [[LocalPlayer.cs##public partial class LocalPlayer : CharacterBody2D, IEntity|LocalPlayer]] and `Enemy` must be `CharacterBody2D` for physics, so they implement `IEntity` directly with an `EntityRegistry` field, while roots that can be plain `Node`s get the same thing for free by deriving from [[Entity.cs##public partial class Entity : Node, IEntity|Entity]].

### The registration lifecycle

All six component base classes run the identical four-beat lifecycle, shown here for [[Component.cs##public abstract partial class Component : Node, IComponent|Component]]:

1. **`_Ready`** — the component calls [[ComponentRegistration.cs##public static IEntity? Register(Node node, IComponent component)|ComponentRegistration.Register]], which walks `GetParent()` upward to the nearest `IEntity` ancestor and adds the component to its registry. Walking ancestors (rather than requiring direct parentage) is what lets binder nodes and shapes sit between a component and its entity. If no entity ancestor exists, `Register` logs a `PushError` and returns null — a component outside an entity is inert, not crashing.
2. **`OnRegistered()`** — the first override point, run immediately after registration, for setup that only needs the entity back-reference.
3. **Deferred second pass** — `_Ready` ends with `CallDeferred(nameof(AfterSiblingsReady))` because sibling `_Ready` order is not guaranteed to match dependency order; deferring guarantees every sibling has registered before anyone checks for them.
4. **`AfterSiblingsReady`** — validates the component's `GetRequiredComponents()` list through [[ComponentRegistration.cs##public static void ValidateRequired|ComponentRegistration.ValidateRequired]] (one `PushWarning` per missing sibling — a warning, not an error, so a half-assembled entity still runs in the editor) and then calls the second override point, `OnEntityReady()`.

`_ExitTree` unregisters the component from the entity, so dynamically spawned components (a `HitZone` that frees itself when its lifetime timer expires) don't leak stale entries in the registry. Components reach each other through `GetSibling<T>()`, which is just `Entity.GetComponent<T>()` with a null guard.

The sibling bases exist purely to give components the right native Godot root — the Comedot rule that a component scene's root is the closest matching builtin type: [[AreaComponent.cs##public abstract partial class AreaComponent : Area2D, IComponent|AreaComponent]] for hitboxes/hurtboxes (`Area2D` gives physics overlap signals), `Node2DComponent` for positioned 2D nodes, [[Node3DComponent.cs##public abstract partial class Node3DComponent : Node3D, IComponent|Node3DComponent]] for the components inside `world_3d.tscn` (which register with the `GameManager` entity through the `SubViewport` ancestor walk), `ControlComponent` for UI, and `VisualComponent` for `AnimatedSprite2D` sprites. C#'s single inheritance means the lifecycle code is repeated in each base; the genuinely shared parts — the ancestor walk and the required-components check — live once in [[ComponentRegistration.cs##public static class ComponentRegistration|ComponentRegistration]], the same role `IEntity`/`EntityRegistry` play on the entity side.

One utility rounds out the client core: [[NodeExtensions.cs##public static T? GetAncestor<T>(this Node node)|NodeExtensions.GetAncestor<T>()]], the same parent-walk as a typed extension method. It exists because `Node.Owner` stops at instanced sub-scene boundaries, and nodes inside an instanced sub-scene sometimes need a node from the parent scene.

### `EntityRegistry`: lookup semantics that matter

[[EntityRegistry.cs##public sealed class EntityRegistry|EntityRegistry]] is deliberately a flat `List<IComponent>`, not a dictionary. [[EntityRegistry.cs##public IComponent? Get(Type type)|Get]] returns the first registered component the requested type `IsInstanceOfType` — two consequences worth knowing before you build on it:

- **Subclass queries work.** Asking for `DamageComponent` returns a `HitZone`, because `HitZone : DamageComponent`. This is the intended way to query by abstraction.
- **Lookup is order-dependent.** When two registered components share a base, you get whichever registered first (registration order = scene-tree `_Ready` order). Don't put two components of the same type on one entity.

### `GameManager`: the scene-level entity and static facade

[[GameManager.cs##public partial class GameManager : Node2D, IEntity|GameManager]] is the root node of `main.tscn` and the `IEntity` that all scene-level components register with — `TableSubscriber`, `CatalogComponent`, `EntitySpawnerComponent`, `LobbyComponent`, and the camera/overlay components are its children. It holds no logic of its own; it exists because spawned entities and UI panels can't see those scene-level components through their own entity's registry.

The solution is the static facade on the same class: `_Ready` stashes the instance in a static field, and static members like [[GameManager.cs##public static DbConnection? Conn|Conn]], `GetEnemy(enemyId)`, `GetItem(itemId)`, and `LapQ`/`LapR` delegate through `Get<T>()` to the registered components (or to the `DatabaseConnector` autoload) so any code anywhere can reach the connection, catalogs, and spawner lookups without a node-path walk. Every accessor is null-safe against the instance being gone, because `main.tscn` is unloaded when the game returns to the lobby. Even the `EnchantmentsChanged` event is forwarded subscribe/unsubscribe through the facade, so observers never hold a direct reference to the component.

### `TableBinderComponent`: server rows enter the component model

SpacetimeDB in one paragraph: the server module defines **tables** (typed rows replicated to subscribed clients) and **reducers** (the only way to mutate them, called remotely). The generated C# bindings expose each table's row events as C# events (`OnInsert`/`OnUpdate`/`OnDelete`). Those events are unusable in the Godot editor — you can't wire a C# event to a method in a `.tscn` — so [[TableBinderComponent.cs##public partial class TableBinderComponent : Component|TableBinderComponent]] exists to bridge them.

One binder is placed as a child of the consuming component per table it cares about. In [[TableBinderComponent.cs##private void Bind(DbConnection conn)|Bind]], the binder looks up the named table on the generated `RemoteTables` by reflection and hooks its row events through [[TableBinderComponent.cs##private void Hook(string eventName, string handlerMethod)|Hook]], which closes the generic `HandleInsert<TRow>`/`HandleUpdate<TRow>`/`HandleDelete<TRow>` methods over the row type recovered from the event's own delegate signature — this is why one binder class works for every table without codegen. Each hook re-emits the event as an editor-wireable Godot signal (`RowInserted`/`RowUpdated`/`RowDeleted`). The signals carry no arguments because SpacetimeDB rows are plain C# classes and can't cross Godot's `Variant` signal boundary; handlers instead read `LastRow`, `LastOldRow` (updates), or `LastDeletedRow` and cast. `ReplayExistingRows` fires `RowInserted` once per already-cached row on bind, which replaces manual `Iter()` replay loops for late-binding components.

Three smaller design points, all in the same file: the binder is a `Component` itself, so it rides the standard lifecycle — it binds in `OnEntityReady` (waiting for the `DatabaseConnector` autoload's `Connected` signal if the connection isn't up yet) and unhooks every event in `_ExitTree` so freed components don't keep receiving rows. The `TableName` property is declared through `_GetPropertyList` so the inspector shows a dropdown populated from `TableSubscriber.AllSubscribedTables` — picking an unsubscribed table earns a configuration warning because such a binder can never receive rows. And the class is `[Tool]` purely so that dropdown works in the editor; the `_Ready`/`OnEntityReady` editor-hint guards keep it from attempting registration or connections there. (Event tables like `BulletPatternEvent` have no update/delete semantics, so `Hook` silently skips missing events by design.)

### The server mirror: tables as components, archetype helpers as scene instancing

The server applies the same composition rule relationally: one table per concern is one component, and a shared id joins a logical entity's rows across tables the way the entity root joins its component children. A player profile's "components" are the rows keyed by `profile_id`: [[server/spacetimedb/src/player/tables.rs##pub struct PlayerData|PlayerData]] (level/xp/hp), `PlayerStats`, `PlayerInventory`, `PlayerPosition`, `PlayerChunk`, `ActiveConsumableEffect`.

[[server/spacetimedb/src/player/methods.rs##pub fn try_scaffold_profile|try_scaffold_profile]] is the server-side equivalent of instancing a composed scene: called on `join_world`, it inserts each missing per-profile row — `PlayerData` at level 1, a `PlayerInventory` whose fixed 24-slot layout (weapon, hotbar, accessories, armor, artifacts, general, bag, with starter items pre-slotted) is load-bearing on both sides of the wire, lazily computed `PlayerStats` via `recompute_stats`, and `PlayerPosition`/`PlayerChunk` at the world origin with the chunk derived through `world_to_chunk`/`spiral_chunk_index`. Every insert is guarded by an `is_none()` check, so re-joining never clobbers an existing profile — the helper *scaffolds*, it doesn't reset.

[[server/spacetimedb/src/player/methods.rs##pub fn teardown_profile|teardown_profile]] is the exact inverse — `queue_free()` for the row-bundle: it deletes the data, stats, inventory, position, and chunk rows plus any active consumable effects. Its docstring marks it as the *one* shared cleanup list used by both combat death and `delete_profile` (the two paths previously disagreed about which tables to clean), while identity-level rows (`PlayerProfile`, login state) are left to the callers because death and profile deletion handle them differently.

Enemies show the same pattern with a deeper tree. [[server/spacetimedb/src/enemy/methods.rs##pub fn spawn_enemy_archetype|spawn_enemy_archetype]] first calls `build_enemy_behavior`, which expands an `EnemyTemplate` into a behavior *tree* — an `EnemyBehavior` row with child `EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance` rows joined by `behavior_id`/`attack_id` — and then inserts the flat `Enemy` row (position, hp, phase) pointing at it. The code's own comment names the mapping: the behavior tree is the enemy's component tree, with `behavior_id`/`enemy_id` as the entity id joining it; the `Enemy` row stays flat by deliberate design. [[server/spacetimedb/src/enemy/methods.rs##pub fn despawn_enemy_archetype|despawn_enemy_archetype]] deletes the whole behavior tree via `delete_enemy_behavior` *before* deleting the `Enemy` row, so no orphan component rows outlive the entity — the relational equivalent of freeing a node freeing its children.

## Known gaps / stubs

- **Nine standalone component scenes are unreferenced duplicates** — drift hazards, not wiring sites. Each duplicates node declarations that the live scenes (`main.tscn`, etc.) declare inline; no `.tscn` instances them and no code loads them (verified by grep: the only text matches are self-referential comments, below). Do not edit them expecting any effect: `Scenes/Components/Catalog/catalog_component.tscn`, `Scenes/Components/Spawning/entity_spawner_component.tscn`, `Scenes/Components/Terrain/terrain_component.tscn`, `Scenes/Components/Subscription/subscription_component.tscn`, `Scenes/Components/Camera/camera_rig_component.tscn`, `Scenes/Components/Camera/camera_2d_presenter_component.tscn`, `Scenes/Components/Camera/hex_grid_overlay_component.tscn`, `Scenes/UI/debug_overlay.tscn`, and `Scenes/Components/Combat/damage_component.tscn`.
- **Stale comments point at those duplicate scenes.** `CatalogComponent.cs`, `EntitySpawnerComponent.cs`, `TerrainComponent.cs`, and `HexGridOverlayComponent.cs` each say their binder signals are "wired in `<name>.tscn`" — that wiring actually lives inline in `main.tscn` now (e.g. the `CatalogComponent` node and its binders are declared directly under `Main`). Read the comments as history, not truth.
- **`DamageComponent` has no live scene of its own.** Its only real use is as [[HitZone.cs##public partial class HitZone : DamageComponent|HitZone]]'s base class ([[DamageComponent.cs##public partial class DamageComponent : AreaComponent|DamageComponent]] itself is just the attacker-side hitbox base); `damage_component.tscn` is one of the unreferenced duplicates above.

## Where to go next

With the framework in place, continue to [[03 Boot & Connection]]: how the app starts, how the `DatabaseConnector` autoload establishes the SpacetimeDB connection that every `TableBinderComponent` waits on, and how the server seeds the world those tables describe.
