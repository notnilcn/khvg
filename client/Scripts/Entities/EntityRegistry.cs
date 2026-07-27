#nullable enable
using System;
using System.Collections.Generic;

/// <summary>
/// Reusable storage behind <see cref="IEntity"/>: entity roots keep one of these as a field
/// and forward their two interface methods to it.
/// </summary>
public sealed class EntityRegistry
{
    private readonly List<Component> components = new();

    public void Register(Component component) => components.Add(component);

    public Component? Get(Type type)
    {
        foreach (var component in components)
            if (type.IsInstanceOfType(component))
                return component;
        return null;
    }
}
