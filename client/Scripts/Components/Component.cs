#nullable enable
using Godot;
using System;

/// <summary>
/// Base class for components: nodes that provide one piece of behavior for the entity
/// (IEntity-implementing scene root) above them. On _Ready the component walks up its
/// ancestors to find the nearest IEntity, registers itself there, runs OnRegistered(), and
/// then (deferred, so sibling components have had their own _Ready) warns about any missing
/// required sibling components from GetRequiredComponents().
/// </summary>
public abstract partial class Component : Node
{
    protected IEntity Entity { get; private set; } = null!;

    public override void _Ready()
    {
        for (var node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is IEntity entity)
            {
                Entity = entity;
                break;
            }
        }

        if (Entity == null)
        {
            GD.PushError($"[Component] {GetType().Name} at '{GetPath()}' has no IEntity ancestor.");
            return;
        }

        Entity.RegisterComponent(this);
        OnRegistered();

        if (GetRequiredComponents().Length > 0)
            CallDeferred(nameof(ValidateRequiredComponents));
    }

    protected virtual void OnRegistered() { }

    protected virtual Type[] GetRequiredComponents() => Array.Empty<Type>();

    protected T? GetSibling<T>() where T : Component => Entity?.GetComponent<T>();

    private void ValidateRequiredComponents()
    {
        foreach (var type in GetRequiredComponents())
        {
            if (Entity.GetComponent(type) == null)
                GD.PushWarning($"[Component] {GetType().Name} at '{GetPath()}' is missing required sibling component {type.Name}.");
        }
    }
}
