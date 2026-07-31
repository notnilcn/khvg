# 02 Entity & Component Framework

## Assumed knowledge

[[01 Architecture & Sync Model]] — this doc assumes you know what a table, a reducer, and a subscription are, and how the client's static facade (`GameManager.Conn`, `.GetItem(...)`) resolves through registered components.

## The 30-second version

Both sides of this project are **compositional**: instead of one big `Player` class or one big `Player` table, a logical entity — a local player, an enemy, a loot drop — is a bag of narrow, single-purpose pieces wired together by a shared id. On the client, a scene root implements `IEntity` and its child nodes are `IComponent`s that find that root by walking up the scene tree and register themselves with it; anything that needs a sibling's data asks the entity for it by type instead of holding a direct reference. On the server, the same idea shows up as one table per concern — `PlayerData`, `PlayerStats`, `PlayerInventory` are separate tables, not columns of one giant `Player` row — joined by an id (`profile_id`, `enemy_id`) that plays the same role `GetComponent<T>()` plays on the client. Because an entity's rows/nodes are scattered across many tables/children, both sides need a single place that knows how to create and destroy *all* of them atomically — the client's answer is table `OnInsert`/`OnDelete` callbacks driving `Instantiate`/`QueueFree`; the server's answer is the **archetype helpers** this doc covers (`try_scaffold_profile`/`teardown_profile`, `spawn_enemy_archetype`/`despawn_enemy_archetype`).

## Composition map

*(Doc 02 is atemporal — framework structure, not a runtime sequence — so per the schema exception this section replaces the usual sync-embedded System flowchart. No steps are appended to `99` for this doc.)*

For each logical entity: the client scene root and its component children, then the server tables joined by that entity's id and the archetype helper (if any) that bundles them.

### Local player

- **Client** — `LocalPlayer` (`CharacterBody2D, IEntity`), root of `local_player.tscn`. Implements `IEntity` directly (needs the `CharacterBody2D` native base, so it can't derive from the plain-`Node` `Entity` convenience class) with its own `EntityRegistry` field. [[LocalPlayer.cs##private readonly EntityRegistry componentRegistry|LocalPlayer.cs]] Children: `Sprite`/`Collider`/`Camera` (plain nodes, no behavior), `CombatComponent` (weapon firing), `StatsComponent`, `HealthComponent`, `FactionComponent` (`Players` flag), `DamageReceivingComponent` (hurtbox, its own `CollisionShape2D` child), `InventoryCanvas` (the `inventory_panel.tscn` UI), `LocalPlayerPhantomCamera2D` (camera addon node, follows the hurtbox).
- **Server** — entity id `profile_id`. Rows: `PlayerData` (level/xp/hp), `PlayerStats` (six stats), `PlayerInventory` (24 `InventorySlot`s), `PlayerPosition`, `PlayerChunk`. Archetype helper: `try_scaffold_profile`/`teardown_profile` in `player/methods.rs` — both lazily-idempotent (`try_`) on creation, and a flat delete-all on teardown. `PlayerProfile` itself (the identity-level `profile_name`/`texture_id` row, keyed by `profile_id` but owned by `player_id`) sits outside this helper — it's created by `create_profile` and only removed by `delete_profile` in the lobby, not by this scaffold/teardown pair ([[04 Player System|04]]).

### Remote player

- **Client** — `RemotePlayer` (`Node2D, IEntity`), root of `non_local_player.tscn`. Children: `RemoteVisualComponent` (`AnimatedSprite2D`, texture + Walk/Idle) and `InterpolationComponent` (lerp toward subscribed positions).
- **Server** — no separate tables. A remote player is the *same* `PlayerPosition`/`PlayerProfile` rows a local player has, seen through another connection's AOI-filtered views (`nearby_remote_players`, `nearby_remote_players_profiles` in `player/views.rs`) instead of the unfiltered `local_player_*` views. [[player/views.rs##fn nearby_remote_players(ctx: &ViewContext)|nearby_remote_players]] There's exactly one archetype per profile server-side; "local" vs "remote" is a client-side distinction about *which view a given connection subscribes to*, not a different set of rows.

### Enemy

- **Client** — `Enemy` (`CharacterBody2D, IEntity`), root of `default_enemy.tscn`. Same "can't derive from `Entity`" reasoning as `LocalPlayer`. Children: `AnimatedSprite2D`/`CollisionShape2D` (plain), `StatsComponent`, `HealthComponent`, `FactionComponent` (`Enemies` flag), `DamageReceivingComponent` (its own collision layer — what `HitZone`s detect instead of the physics body), `InterpolationComponent`.
- **Server** — entity id `enemy_id` (plus `behavior_id`, the join from `Enemy` into its behavior tree). Rows: `Enemy` itself (flat — position/hp/phase live directly on this row, a deliberate exception to "one table per concern" since those three fields change every tick and splitting them out would just add join overhead for no isolation benefit), `EnemyBehavior`, `EnemyPhase`, `EnemyAttack`, `EnemySequenceStep`, `RepeatStepInstance`. Archetype helper: `spawn_enemy_archetype`/`despawn_enemy_archetype` in `enemy/methods.rs`, which wrap `build_enemy_behavior`/`delete_enemy_behavior` — the part that actually walks and (re)creates the whole phase→attack→step tree, so no orphan behavior rows outlive the `Enemy` row. [[enemy/methods.rs##pub fn spawn_enemy_archetype|spawn_enemy_archetype]] · [[enemy/methods.rs##pub fn despawn_enemy_archetype|despawn_enemy_archetype]] Static template data (`EnemyTemplate`, phase/attack/step *defs*) is not per-instance and isn't part of this archetype — it's read-only content the archetype helper copies from. Full detail: [[06 Enemy AI & Bullet Patterns|06]].

### Drop

- **Client** — `Drop` (`Node2D, IEntity`), root of `drop.tscn`. Children: `DropVisualComponent` (`Sprite2D`, item texture) and `PickupComponent` (an `AreaComponent` — `Area2D` root — that calls the `PickupDrop` reducer on body-enter).
- **Server** — entity id `drop_id`. A single table, `LootDrop` (item id, position, chunk index, expiry, dropped-by identity, socketed enchantment ids) — one row *is* the whole entity, so there's no archetype helper to bundle: create is one `insert`, destroy is one `delete` (`pickup_drop`/expiry). Contrast with the profile helpers above — a drop simply doesn't need one.

### Bullet manager

- **Client** — `BulletManager` (`Node2D, IEntity`), root of `bullet_manager.tscn`, one instance for the whole client (not per-bullet). Children: `BlastBullets` (the BlastBullets2D GDExtension factory node — a plain child reached by `[Export] NodePath`, not an `IComponent`, since it's third-party plugin surface), `BulletSpawnerComponent` (dispatches `BulletPatternEvent` rows to the factory), `BulletHitRouterComponent` (routes overlaps to `ReportHit`).
- **Server** — `BulletPatternEvent` is declared `#[table(..., event)]`, SpacetimeDB's ephemeral **event table** kind: rows are delivered to subscribers as a fire-once notification and are not persisted for querying afterward. It doesn't carry a stable entity id the way `profile_id`/`enemy_id` do and needs no archetype helper — there's nothing to scaffold or tear down, only a message to route. Full detail: [[06 Enemy AI & Bullet Patterns|06]].

### Game manager

- **Client** — `GameManager` (`Node2D, IEntity`), root ("Main") of `Scenes/main.tscn`. Implements `IEntity` directly for the same native-base reason as the others. [[GameManager.cs##public partial class GameManager|GameManager.cs]] Children registered as components: `ConnectionComponent`, `SubscriptionComponent`, `CatalogComponent`, `EntitySpawnerComponent` (the table `OnInsert`/`OnDelete` → `Instantiate`/`QueueFree` glue that spawns every entity above except itself — [[EntitySpawnerComponent.cs##public partial class EntitySpawnerComponent : Component|EntitySpawnerComponent.cs]]), `CameraRigComponent`, `Camera2DPresenterComponent`, `HexGridOverlayComponent`, `TerrainComponent`, and — nested under the `3D/SubViewportContainer/SubViewport` branch — `World3DComponent` (a `Node3DComponent`; the ancestor walk in `ComponentRegistration.Register` crosses the `SubViewport` boundary the same as any other `GetParent()` chain, so it still finds `Main`). `LobbyComponent` is nested under the `UI` `CanvasLayer` but registers the same way. One exception: `DebugOverlay`, also a direct child of Main, is a bare `CanvasLayer` — not an `IComponent` — so it never registers; it reaches state through `GameManager`'s static facade instead (paired with server `main/debug.rs`, [[09 Admin, Debug & World Lifecycle|09]]).
- **Server** — no table backs `GameManager`; it's purely client-side composition root for the connection/subscription/catalog machinery covered in [[01 Architecture & Sync Model|01]]. There's no server-side equivalent because nothing about "being connected" is itself a per-entity row — it's the `client_connected`/`client_disconnected` lifecycle hooks instead.

## Main body

### The component contract: `IComponent` + `ComponentRegistration`

`IComponent` is a one-property interface — `IEntity? Entity { get; }`, the owning entity, null before registration and after `_ExitTree` — that lets an `IEntity`'s registry hold and query components regardless of which native Godot node type they're built on. [[IComponent.cs##public interface IComponent|IComponent.cs]]

The actual registration logic — walk `GetParent()` until an `IEntity` is found, call `RegisterComponent` on it, then validate required siblings — lives once, in the static helper `ComponentRegistration`, rather than being copy-pasted into every component base class. [[ComponentRegistration.cs##public static IEntity|Register]] If no `IEntity` ancestor exists, it logs an error and returns null rather than throwing, so a misconfigured scene fails loud (in the log) but doesn't crash the tree.

### Six bases, one pattern, because C# has single inheritance

A component's scene root is always the *closest matching builtin type* to what it needs (the comedot convention this framework is modeled on) — a hitbox is an `Area2D`, a stat panel is a `Control`, an entity sprite is an `AnimatedSprite2D`. C# only allows one base class, so there can't be one `Component : IComponent` base shared by all of them; instead there are six near-identical bases, one per native root type, each independently implementing the same `_Ready`/`_ExitTree`/`OnRegistered`/`OnEntityReady` cycle by delegating to `ComponentRegistration`:

| Base | Native root | Used for |
|---|---|---|
| `Component` | `Node` | Behavior with no visual/physics footprint (`CombatComponent`, `EntitySpawnerComponent`) |
| `AreaComponent` | `Area2D` | Hitboxes/hurtboxes (`DamageComponent`, `DamageReceivingComponent`, `PickupComponent`) |
| `Node2DComponent` | `Node2D` | 2D-positioned components (`TerrainComponent`, `Camera2DPresenterComponent`) |
| `Node3DComponent` | `Node3D` | Components inside the 3D backdrop viewport (`World3DComponent`) |
| `ControlComponent` | `Control` | UI panels (`InventoryComponent`, `LobbyComponent`) |
| `VisualComponent` | `AnimatedSprite2D` | (base exists for sprite-rooted components; current sprite components mostly attach the script directly to a scene-declared `AnimatedSprite2D` node instead) |

[[Component.cs##public abstract partial class Component : Node, IComponent|Component.cs]] · [[AreaComponent.cs##public abstract partial class AreaComponent : Area2D, IComponent|AreaComponent.cs]] · [[Node2DComponent.cs##public abstract partial class Node2DComponent : Node2D, IComponent|Node2DComponent.cs]] · [[Node3DComponent.cs##public abstract partial class Node3DComponent : Node3D, IComponent|Node3DComponent.cs]] · [[ControlComponent.cs##public abstract partial class ControlComponent : Control, IComponent|ControlComponent.cs]] · [[VisualComponent.cs##public abstract partial class VisualComponent : AnimatedSprite2D, IComponent|VisualComponent.cs]]

All six run the identical sequence on `_Ready`:

1. `Entity = ComponentRegistration.Register(this, this)` — find and register with the nearest `IEntity` ancestor. If this returns null (no ancestor found), the method returns early — no `OnRegistered` fires.
2. `OnRegistered()` runs *synchronously*, in the same `_Ready` call. This is the hook to use for anything that must happen the instant the component exists — [[01 Architecture & Sync Model|01]]'s `ConnectionComponent.OnRegistered` opening the connection is the prototypical example, precisely because nothing else can be assumed ready yet.
3. `CallDeferred(nameof(AfterSiblingsReady))` queues the second half for *after* Godot finishes the current `_Ready` pass across the whole scene tree — which means after every sibling component has also run step 1 and registered itself.
4. `AfterSiblingsReady` (once deferred execution reaches it) runs `ComponentRegistration.ValidateRequired` (logs a warning per required-but-missing sibling type from `GetRequiredComponents()`) and then `OnEntityReady()` — the hook to use for anything that reads a sibling component, since by this point every sibling is guaranteed registered regardless of node order in the `.tscn`.

`_ExitTree` unregisters from the entity and clears `Entity` to null — this is what lets dynamically spawned components (a `HitZone` fired mid-fight, an inventory slot panel) leave cleanly without leaking a stale entry in the registry.

`GetSibling<T>()` is a small protected convenience on every base — `Entity?.GetComponent<T>()` — so component code reads `GetSibling<HealthComponent>()` instead of repeating the null-conditional chain. [[Component.cs##GetSibling<T>|GetSibling]]

### `NodeExtensions.GetAncestor<T>()`

A component finds its *entity* through `IEntity`'s registry, but code sometimes needs an ancestor of a specific concrete type instead — e.g. a slot panel inside `inventory_panel.tscn` reaching the owning `LocalPlayer`. Godot's built-in `Node.Owner` doesn't work for this once a scene is instanced as a child of another scene (the `Owner` of nodes inside an instanced sub-scene stays the sub-scene's own root, not whatever it was instanced into). `GetAncestor<T>()` just walks `GetParent()` until it finds a node assignable to `T`, which works across those instancing boundaries. [[NodeExtensions.cs##public static T|GetAncestor]]

### The entity side: `IEntity` / `Entity` / `EntityRegistry`

`IEntity` is the other half of the contract: `RegisterComponent`/`UnregisterComponent`/`GetComponent(Type)`, plus a default-interface-method generic wrapper `GetComponent<T>()` built on top of the non-generic one. [[IEntity.cs##public interface IEntity|IEntity.cs]] There are two ways to *be* an entity:

- **Derive `Entity`** — a thin `Node`-rooted convenience class that owns an `EntityRegistry` field and forwards the three `IEntity` methods to it. Use this when the scene root doesn't need a more specific native base. [[Entity.cs##public partial class Entity : Node, IEntity|Entity.cs]]
- **Implement `IEntity` directly** — every entity in the composition map above (`LocalPlayer`, `RemotePlayer`, `Enemy`, `Drop`, `BulletManager`, `GameManager`) does this instead, because each needs a native base other than plain `Node` (`CharacterBody2D` for the two that move under physics, `Node2D` for the rest) and C# single inheritance rules out also deriving `Entity`. Each carries the identical three-line forwarding boilerplate to its own private `EntityRegistry` field — the same duplication-for-native-base-reasons pattern as the six component bases above. [[LocalPlayer.cs##private readonly EntityRegistry componentRegistry|LocalPlayer.cs]]

`EntityRegistry` itself is a flat `List<IComponent>` behind `Register`/`Unregister`/`Get`. `Get(Type)` returns the *first* component assignable to the requested type — not requiring an exact type match — which is what makes subclass-based lookups work (comedot's "find subclasses" idea): code asking for a base component type transparently finds a more specific subclass instance if that's what's registered. [[EntityRegistry.cs##public sealed class EntityRegistry|EntityRegistry.cs]]

### The server side: tables-as-components, ids-as-entities

The server has no interface or base class doing this — the composition is structural, not enforced by a type system feature. "One table per concern" plus a shared id column *is* the pattern: `PlayerData`, `PlayerStats`, `PlayerInventory`, `PlayerPosition`, `PlayerChunk` are five separate tables, each `#[primary_key]`-ed on the same `profile_id`, and reading "everything about this profile" means five separate `.find(&profile_id)` calls rather than one row read — the SQL-side equivalent of a client entity's `GetComponent<T>()` calls across its registered children. The same shape repeats for enemies keyed on `enemy_id`/`behavior_id`. Every gameplay module (`world`, `player`, `enemy`, `item`) follows this; full per-file breakdown is `server/CLAUDE.md`, which this doc doesn't restate.

### Why archetype helpers exist

Because an entity's data is deliberately scattered across tables, *creating* or *destroying* one is not a single-row operation — it's N inserts or N deletes that all need to happen together, or not at all, with nothing left half-done. That's what the archetype helpers guarantee:

- `try_scaffold_profile` (creation) checks each of the five per-profile tables independently with `is_none()` before inserting — the `try_` prefix names that idempotency, so calling it twice (e.g. once from `join_world`, once from a future reconnect path) never double-inserts a row that's already there.
- `teardown_profile` (destruction) is an unconditional delete across the same five tables plus any live `ActiveConsumableEffect` rows, called from two different places that both need "erase every trace of this profile's in-world state" — combat's zero-HP death cleanup, and the lobby's `delete_profile` — which is exactly why it's a shared helper instead of two call sites drifting out of sync on which tables they remember to clean up. [[player/methods.rs##pub fn try_scaffold_profile|try_scaffold_profile]] · [[player/methods.rs##pub fn teardown_profile|teardown_profile]]
- `spawn_enemy_archetype`/`despawn_enemy_archetype` do the equivalent for enemies: spawning walks the enemy's whole phase→attack→step tree structure (`build_enemy_behavior`) and inserts one row per node in that tree before inserting the flat `Enemy` row itself; despawning (`delete_enemy_behavior`) walks the same tree in reverse, deleting every phase/attack/step/repeat-instance row before the `Enemy` row goes. [[enemy/methods.rs##pub fn spawn_enemy_archetype|spawn_enemy_archetype]] · [[enemy/methods.rs##pub fn despawn_enemy_archetype|despawn_enemy_archetype]]

On the client, the equivalent guarantee is simpler because there's no cross-table transaction to worry about — `EntitySpawnerComponent` just instantiates the whole scene (with all its declared component children) in one `AddChild` call keyed off a single row's `OnInsert`, and `QueueFree`s the whole subtree on that same row's `OnDelete`. A `LocalPlayer` scene's `HealthComponent`/`StatsComponent`/etc. all come into existence together because they're declared once in `local_player.tscn`, not assembled piecemeal — the client's "archetype" is just the `.tscn` file. [[EntitySpawnerComponent.cs##public partial class EntitySpawnerComponent : Component|EntitySpawnerComponent.cs]]

## Known gaps / stubs

- **`VisualComponent` (`AnimatedSprite2D` base) has no current instantiation** — the sprite-rooted components that exist today (`RemoteVisualComponent`, on `non_local_player.tscn`'s `AnimatedSprite2D`) attach their script directly to a scene-declared node rather than deriving this base. The base exists for the pattern's completeness, not because something currently uses it.

## Where to go next

This composition pattern — component registration, archetype helpers — is assumed by every doc from here on without re-explaining it. Read [[03 World & Hex Grid]] or [[04 Player System]] next; both lean on the server-side half (per-chunk/per-profile tables joined by id) immediately.
