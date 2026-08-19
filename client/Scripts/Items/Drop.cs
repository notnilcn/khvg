#nullable enable
using Godot;
using SpacetimeDB;

/// <summary>
/// A server-spawned loot drop. Entity glue only: the Sprite2D child shows the item
/// texture and the PickupComponent owns pickup detection + the pickup lock; this root
/// just mirrors the LootDrop row into them. GameManager sets the row properties before
/// AddChild, so _Ready (after the component children have registered) is the forward point.
/// </summary>
public partial class Drop : Node2D, IEntity
{
    // IEntity — Drop needs Node2D, so it implements the interface directly with its own
    // registry; same pattern as LocalPlayer/Enemy.
    private readonly EntityRegistry componentRegistry = new();
    public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
    public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
    public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
    public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

    public ulong DropId { get; set; }
    public string ItemId { get; set; } = "";
    public Identity? DroppedBy { get; set; }

    public bool PickupLocked => GetComponent<PickupComponent>()?.PickupLocked ?? false;

    public override void _Ready()
    {
        var item = GameManager.GetItem(ItemId);
        var resPath = item != null ? GameManager.GetResPath(item.TextureId) : null;
        if (resPath != null)
            GetNode<Sprite2D>("Sprite2D").Texture = GD.Load<Texture2D>(resPath);
        GetComponent<PickupComponent>()?.SetFromServer(DropId, DroppedBy);
    }
}
