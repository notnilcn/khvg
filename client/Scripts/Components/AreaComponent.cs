#nullable enable
using Godot;
using System;

/// <summary>
/// Component base for components whose scene root is an Area2D (hitboxes/hurtboxes such as
/// DamageReceivingComponent and HitZone) — the comedot rule that a component scene's
/// root is the closest matching builtin type. Registration behavior mirrors Component; the
/// bases share ComponentRegistration because C# allows only one base class.
/// </summary>
public abstract partial class AreaComponent : Area2D, IComponent
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
