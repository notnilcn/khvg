#nullable enable
using System;

/// <summary>
/// Implemented by scene roots that own <see cref="IComponent"/> children (e.g. LocalPlayer).
/// C# has single inheritance, so roots that need a native base class (CharacterBody2D etc.)
/// implement this interface directly with an <see cref="EntityRegistry"/> field instead of
/// deriving from a shared node base. Roots that can derive from plain Node may use the
/// <see cref="Entity"/> convenience class instead.
/// </summary>
public interface IEntity
{
    void RegisterComponent(IComponent component);
    void UnregisterComponent(IComponent component);
    IComponent? GetComponent(Type type);

    T? GetComponent<T>() where T : IComponent => GetComponent(typeof(T)) is T match ? match : default;
}
