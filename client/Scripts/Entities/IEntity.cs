#nullable enable
using System;

/// <summary>
/// Implemented by scene roots that own <see cref="Component"/> children (e.g. LocalPlayer).
/// C# has single inheritance, so roots that need a native base class (CharacterBody2D etc.)
/// implement this interface directly with an <see cref="EntityRegistry"/> field instead of
/// deriving from a shared node base. Roots that can derive from plain Node may use the
/// <see cref="Entity"/> convenience class instead.
/// </summary>
public interface IEntity
{
    void RegisterComponent(Component component);
    Component? GetComponent(Type type);

    T? GetComponent<T>() where T : Component => GetComponent(typeof(T)) as T;
}
