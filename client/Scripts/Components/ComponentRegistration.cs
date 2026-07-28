#nullable enable
using Godot;
using System;

/// <summary>
/// Shared registration logic for the component base classes (Component for Node roots,
/// AreaComponent for Area2D roots, Node2DComponent for Node2D, ControlComponent for
/// Control, VisualComponent for AnimatedSprite2D). C# single inheritance prevents a common
/// node base class, so the behavior lives here — the same role IEntity/EntityRegistry
/// play for entity roots.
/// </summary>
public static class ComponentRegistration
{
    /// Walks ancestors for the nearest IEntity and registers the component there.
    /// Returns the entity, or null (with an error logged) if there is no IEntity ancestor.
    public static IEntity? Register(Node node, IComponent component)
    {
        for (var ancestor = node.GetParent(); ancestor != null; ancestor = ancestor.GetParent())
        {
            if (ancestor is IEntity entity)
            {
                entity.RegisterComponent(component);
                return entity;
            }
        }
        GD.PushError($"[Component] {node.GetType().Name} at '{node.GetPath()}' has no IEntity ancestor.");
        return null;
    }

    public static void ValidateRequired(Node node, IEntity entity, Type[] required)
    {
        foreach (var type in required)
        {
            if (entity.GetComponent(type) == null)
                GD.PushWarning($"[Component] {node.GetType().Name} at '{node.GetPath()}' is missing required sibling component {type.Name}.");
        }
    }
}
