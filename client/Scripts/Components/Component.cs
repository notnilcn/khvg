#nullable enable
using Godot;
using System;

/// <summary>
/// Base class for components with plain Node roots: nodes that provide one piece of behavior
/// for the entity (IEntity-implementing scene root) above them. On _Ready the component walks
/// up its ancestors to find the nearest IEntity, registers itself there, runs OnRegistered(),
/// and then (deferred, so sibling components have had their own _Ready) runs OnEntityReady()
/// and warns about any missing required sibling components from GetRequiredComponents().
/// For other native root types see AreaComponent (Area2D), Node2DComponent (Node2D),
/// ControlComponent (Control), and VisualComponent (AnimatedSprite2D).
/// </summary>
public abstract partial class Component : Node, IComponent
{
    /// The owning entity; null before _Ready registration and after _ExitTree.
    public IEntity? Entity { get; private set; }

    public override void _Ready()
    {
        Entity = ComponentRegistration.Register(this, this);
        if (Entity == null) return;
        OnRegistered();
        CallDeferred(nameof(AfterSiblingsReady));
    }

    public override void _ExitTree()
    {
        Entity?.UnregisterComponent(this);
        Entity = null;
    }

    protected virtual void OnRegistered() { }

    /// Called deferred after _Ready, once all sibling components have registered themselves.
    protected virtual void OnEntityReady() { }

    protected virtual Type[] GetRequiredComponents() => Array.Empty<Type>();

    protected T? GetSibling<T>() where T : IComponent => Entity != null ? Entity.GetComponent<T>() : default;

    private void AfterSiblingsReady()
    {
        if (Entity == null) return;
        ComponentRegistration.ValidateRequired(this, Entity, GetRequiredComponents());
        OnEntityReady();
    }
}
