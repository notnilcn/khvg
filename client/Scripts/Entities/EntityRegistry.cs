#nullable enable
using System;
using System.Collections.Generic;

/// <summary>
/// Reusable storage behind <see cref="IEntity"/>: entity roots keep one of these as a field
/// and forward their interface methods to it. Lookup returns the first registered component
/// assignable to the requested type, so subclass queries (comedot's findSubclasses) work.
/// </summary>
public sealed class EntityRegistry
{
    private readonly List<IComponent> components = new();

    public void Register(IComponent component) => components.Add(component);

    public void Unregister(IComponent component) => components.Remove(component);

    public IComponent? Get(Type type)
    {
        foreach (var component in components)
            if (type.IsInstanceOfType(component))
                return component;
        return null;
    }
}
