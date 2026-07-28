#nullable enable
using Godot;

/// <summary>
/// The Drop's item sprite: resolves the item's texture through GameManager's item/texture
/// caches (logic moved out of Drop.cs). Sprite2D root, so it implements IComponent
/// directly — the registration boilerplate mirrors the shared component bases.
/// </summary>
public partial class DropVisualComponent : Sprite2D, IComponent
{
    /// The owning entity; null before _Ready registration and after _ExitTree.
    public IEntity? Entity { get; private set; }

    public override void _Ready()
    {
        Entity = ComponentRegistration.Register(this, this);
    }

    public override void _ExitTree()
    {
        Entity?.UnregisterComponent(this);
        Entity = null;
    }

    /// Loads the texture of the server-supplied item id.
    public void SetItem(string itemId)
    {
        var item = GameManager.GetItem(itemId);
        var resPath = item != null ? GameManager.GetResPath(item.TextureId) : null;
        if (resPath != null)
            Texture = GD.Load<Texture2D>(resPath);
    }
}
