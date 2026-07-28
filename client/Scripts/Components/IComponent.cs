#nullable enable

/// <summary>
/// Implemented by every entity component — Component (plain Node roots), AreaComponent
/// (Area2D roots such as hitboxes and hurtboxes), Node2DComponent (Node2D roots),
/// ControlComponent (Control roots), and VisualComponent (AnimatedSprite2D roots).
/// Lets the entity registry hold and query components regardless of their native node
/// base class.
/// </summary>
public interface IComponent
{
    /// The entity this component registered with during _Ready (null before registration and after _ExitTree).
    IEntity? Entity { get; }
}
