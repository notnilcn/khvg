#nullable enable
using Godot;
using System;

/// <summary>
/// Convenience base for entity roots whose scene root can be a plain Node. Roots that need a
/// different native base (e.g. LocalPlayer : CharacterBody2D) can't derive from this — they
/// implement <see cref="IEntity"/> directly with their own <see cref="EntityRegistry"/> field.
/// </summary>
public partial class Entity : Node, IEntity
{
    private readonly EntityRegistry registry = new();

    public void RegisterComponent(IComponent component) => registry.Register(component);
    public void UnregisterComponent(IComponent component) => registry.Unregister(component);
    public IComponent? GetComponent(Type type) => registry.Get(type);
}
