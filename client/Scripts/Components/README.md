# `Components/` — the six component base classes

**Read this before "cleaning up" the top-level base classes.** The six files at the top of this
folder are the inheritance roots of the client's entity/component framework. They are
intentionally near-identical copies of one lifecycle — one copy per Godot native root type:

| File | Native root | Typical consumers |
|---|---|---|
| `Component.cs` | `Node` | `HealthComponent`, `CombatComponent`, `ElevationMapComponent`, `TableSubscriber`, `TableBinderComponent` |
| `AreaComponent.cs` | `Area2D` | `DamageReceivingComponent`, `HitZone`, `PickupComponent`, `InteractComponent`, `RemoteHitWitnessComponent` |
| `Node2DComponent.cs` | `Node2D` | `TerrainComponent`, `Camera2DPresenterComponent`, `HexGridOverlayComponent`, `TerrainLayerComponent` (abstract) |
| `Node3DComponent.cs` | `Node3D` | `World3DComponent`, `TerrainComponent3D`, `TerrainRoadsComponent3D`, `ExploredMapComponent3D`, `WaterComponent3D`, `Bullets3DComponent`, `AbilityPreview3DComponent`, `HexGridOverlay3DComponent` |
| `ControlComponent.cs` | `Control` | `LobbyComponent`, `SlotComponent`, `StatusBarComponent`, `ConnectionLostComponent` |
| `VisualComponent.cs` | `AnimatedSprite2D` | `RemoteVisualComponent` |

## The lifecycle (identical in all six)

```csharp
// Component.cs shown; the other five are the same body with Node/Node2D/Node3D/Control/AnimatedSprite2D
public abstract partial class Component : Node, IComponent
{
    public IEntity? Entity { get; private set; }

    public override void _Ready()
    {
        Entity = ComponentRegistration.Register(this, this); // nearest IEntity ancestor, PushError if none
        if (Entity == null) return;
        OnRegistered();
        CallDeferred(nameof(AfterSiblingsReady));            // deferred: runs after every sibling's _Ready
    }

    public override void _ExitTree()
    {
        Entity?.UnregisterComponent(this);
        Entity = null;
    }

    protected virtual void OnRegistered() { }                // sync, immediately after registration
    protected virtual void OnEntityReady() { }               // deferred, all siblings registered
    protected virtual Type[] GetRequiredComponents() => Array.Empty<Type>();
    protected T? GetSibling<T>() where T : IComponent => Entity != null ? Entity.GetComponent<T>() : default;

    private void AfterSiblingsReady()
    {
        if (Entity == null) return;
        ComponentRegistration.ValidateRequired(this, Entity, GetRequiredComponents());
        OnEntityReady();
    }
}
```

`IComponent` (the `Entity` back-reference) and `ComponentRegistration` (the ancestor walk + the
required-sibling warning) are the only parts that need no virtual dispatch, so they are the only
parts shared by composition. Everything else is per-native-root by necessity.

## Why the duplication is deliberate

1. **C# cannot share it.** There is no `Component<T> : T` ("cannot derive from type parameter"),
   and Godot's native roots are real classes, so the lifecycle has no common slot to live in.
   One copy per root is the language-mandated shape.

2. **The compiler enforces the lifecycle — this is the whole point.** Inheriting from a base means
   `_Ready` always registers and `_ExitTree` always unregisters. You cannot forget. A registration
   that can be forgotten will be forgotten, and its failure mode is silent: `Entity` stays null,
   `GetSibling` returns null, `GetComponent<T>()` never finds the component, and the only symptom
   is gameplay quietly going dead. In a server-authoritative game where every mirror depends on
   this registry, a compile-time-impossible failure mode is worth six duplicated files.

3. **The virtual hooks are the public API.** `OnRegistered` (sync, right after registration),
   `OnEntityReady` (deferred, after all siblings' `_Ready` — the guarantee that lets a parent wire
   handlers before a child replays cached rows), `GetRequiredComponents`, and `GetSibling<T>()`
   only exist through inheritance. Inline helpers turn those into conventions each class must
   re-derive correctly; the base makes them the same for every component regardless of root type.

4. **The duplication is bounded and frozen.** Six files, ~45 lines each, copies by construction,
   not independently evolving code. It is paid once. The alternatives shift boilerplate into every
   component class — a per-class, open-ended cost with a new chance to get the lifecycle wrong in
   each one.

5. **Uniformity beats DRY here.** Every component, whatever its native root, has exactly the same
   registration shape. A reader who has seen one base have seen all six, and a reviewer can trust
   the lifecycle without checking each class.

**The friction is a feature, not a bug.** The accepted cost is six near-identical files that must
be edited together. The purchased guarantee is that no component can silently drop out of the
registry.

## The static helper alternative (considered, rejected)

This is the design to implement **if** the enforcement argument is ever deliberately overruled.
It replaces inheritance with an opt-in helper plus an extension method:

```csharp
public static class ComponentLifecycle
{
    /// Registers with the nearest IEntity ancestor and runs onRegistered synchronously.
    public static IEntity? Attach(Node node, IComponent component, Action? onRegistered = null)
    {
        var entity = ComponentRegistration.Register(node, component);
        if (entity == null) return null;
        onRegistered?.Invoke();
        return entity;
    }

    /// The deferred post-sibling pass. Same message-queue phase as CallDeferred(nameof(...)),
    /// plus an IsInstanceValid guard because a Callable wrapping a C# delegate is not
    /// object-ID-validated by the message queue the way a named deferred call is.
    public static void ScheduleEntityReady(Node node, Type[] required, Action onEntityReady)
    {
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(node) || node is not IComponent component) return;
            if (component.Entity is not IEntity entity) return;
            ComponentRegistration.ValidateRequired(node, entity, required);
            onEntityReady();
        }).CallDeferred();
    }

    /// Call from _ExitTree, then null the class's own Entity property.
    public static void Detach(IComponent component)
        => component.Entity?.UnregisterComponent(component);
}

public static class ComponentExtensions
{
    /// The old protected GetSibling<T>(), as an extension on any IComponent.
    public static T? GetSibling<T>(this IComponent component) where T : IComponent
        => component.Entity != null ? component.Entity.GetComponent<T>() : default;
}
```

A component would look like this (all six bases deleted):

```csharp
public partial class WaterComponent3D : Node3D, IComponent
{
    public IEntity? Entity { get; private set; }

    public override void _Ready()
    {
        Entity = ComponentLifecycle.Attach(this, this);
        // ...existing _Ready body
    }

    public override void _ExitTree()
    {
        ComponentLifecycle.Detach(this);
        Entity = null;
    }
}
```

Components with hooks inline them: `onRegistered:` callback on `Attach`, and
`ScheduleEntityReady(this, required, () => { /* former OnEntityReady body */ })` — keeping the
deferred pass is load-bearing for consumers like `TableBinderComponent`, whose
`ReplayExistingRows` emits must not fire before the parent's `_Ready` has wired handlers
(children ready before parents in Godot).

### What it would change

- Delete the six base classes; add `ComponentLifecycle` (+ `ComponentExtensions`).
- `IComponent`, `IEntity`, `EntityRegistry`, `ComponentRegistration`, `Entity`, and every `.tscn`
  are unchanged (scene roots are native types in both designs).
- Every component class is edited: base becomes its native type, add the `Entity` property,
  `Attach` in `_Ready`, `Detach` + null in `_ExitTree`, hook bodies inline.
- `sstdbsdk`'s two classes drop `: Component`, shrinking its dependency set to
  `IComponent` + `IEntity` + `ComponentRegistration` + `EntityRegistry`.

### Why it was rejected

- **It loses compiler enforcement.** This is the decisive difference. Forget `Attach` and the
  component silently never registers (no error at all, unlike the base's `PushError` path).
  Forget `Detach` and the registry holds a stale reference. Every mitigation is a convention
  (review, CI grep), not a guarantee.
- **It is roughly line-neutral, not a cleanup.** Six bases (~270 lines) become ~6 lines per
  component class plus the helper — about a wash in this project, and it scales per component
  instead of staying fixed.
- **It trades named extension points for lambdas**, adding delegate indirection in stack traces
  and requiring the `IsInstanceValid` guard noted above.
- **It churns every component file once**, for no behavioral gain.

If the duplication is ever worth removing for real, the only path that keeps the guarantee is a
**source generator** that emits the base classes (or the lifecycle half of each `partial class`)
from an attribute, preserving inherited registration and virtual hooks. That is a tooling
decision; the static helper is not that decision.

## Rules for editing these files

- **Change all six together.** Anything that is not virtual-dispatch-dependent belongs in
  `ComponentRegistration`; anything else must be copied, character for character, to every base
  with only the native type and class name changed.
- **Do not add behavior to one base only.** If a distinction between roots is ever needed, it is a
  new decision, not a drive-by edit.
- **A new native root costs a seventh copy** (e.g. a `CharacterBody2DComponent`). Copy an existing
  base; do not invent a generic mechanism.
- **Do not demote a subclass to its native type on the grounds that it "uses no hooks."**
  Registration itself is the hook — it is what makes the component visible to
  `GetComponent<T>()`/`GetSibling<T>()`. Prove nothing fetches the type first.
- **The bases are game framework, not SDK.** `client/sstdbsdk/` depends only on `Component` and
  its interface/registry files; the other five bases exist for game components and are not part
  of a portable sstdbsdk copy.

## See also

- `client/AGENTS.md` — the framework's place in the client map and the per-domain component list.
- `client/Entities/` — `IEntity`, `Entity`, `EntityRegistry` (the other half of the contract).
- `client/sstdbsdk/AGENTS.md` — what the SDK layer needs from this framework.
