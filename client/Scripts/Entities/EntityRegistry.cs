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

    // Memoized Get() results by query type, cleared whenever the list mutates — repeated
    // lookups (e.g. the GameManager facade's Get<T>()) stay O(1) while the subclass-friendly
    // first-match semantics below remain exact.
    private readonly Dictionary<Type, IComponent?> getCache = new();

    public void Register(IComponent component)
    {
        components.Add(component);
        getCache.Clear();
    }

    public void Unregister(IComponent component)
    {
        components.Remove(component);
        getCache.Clear();
    }

    public IComponent? Get(Type type)
    {
        if (getCache.TryGetValue(type, out var cached))
            return cached;
        foreach (var component in components)
            if (type.IsInstanceOfType(component))
            {
                getCache[type] = component;
                return component;
            }
        getCache[type] = null;
        return null;
    }
}
