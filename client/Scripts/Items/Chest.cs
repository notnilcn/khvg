#nullable enable
using Godot;

/// <summary>
/// A server-spawned chest. Entity glue: the Sprite2D child shows the chest texture, the
/// InteractComponent child owns open/close detection (the "interact" action while the local
/// player stands inside), and the NearbyChestItemsBinder child (declared in chest.tscn,
/// replay on) reports content row changes — this root renders the open panel's contents as
/// draggable ChestSlotComponents (drag one onto any player inventory slot → TakeFromChest).
/// EntitySpawnerComponent sets ChestId before AddChild, so _Ready is the forward point
/// (the Drop pattern).
/// </summary>
public partial class Chest : Node2D, IEntity
{
    // IEntity — Chest needs Node2D, so it implements the interface directly with its own
    // registry; same pattern as Drop/LocalPlayer/Enemy.
    private readonly EntityRegistry componentRegistry = new();
    public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
    public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
    public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
    public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

    /// The server row id; set by EntitySpawnerComponent before AddChild.
    public ulong ChestId { get; set; }

    [Export] public PackedScene ChestSlotScene { get; set; } = null!;

    private Control chestPanel = null!;
    private GridContainer itemsGrid = null!;
    private TableBinderComponent itemsBinder = null!;

    public override void _Ready()
    {
        chestPanel = GetNode<Control>("ChestCanvas/ChestPanel");
        itemsGrid = GetNode<GridContainer>("ChestCanvas/ChestPanel/MarginContainer/VBoxContainer/ItemsGrid");
        itemsBinder = GetNode<TableBinderComponent>("NearbyChestItemsBinder");
    }

    public void TogglePanel()
    {
        chestPanel.Visible = !chestPanel.Visible;
        if (chestPanel.Visible)
            RefreshItems();
    }

    public void ClosePanel() => chestPanel.Visible = false;

    // --- NearbyChestItemsBinder signal handlers (wired in chest.tscn) ---

    private void OnChestItemRowInserted()
    {
        if (((SpacetimeDB.Types.ChestItem)itemsBinder.LastRow!).ChestId != ChestId) return;
        if (chestPanel.Visible) RefreshItems();
    }

    private void OnChestItemRowDeleted()
    {
        if (((SpacetimeDB.Types.ChestItem)itemsBinder.LastDeletedRow!).ChestId != ChestId) return;
        if (chestPanel.Visible) RefreshItems();
    }

    /// Rebuilds the panel's slots from the client cache. The slot count is data-driven,
    /// so the ChestSlotComponents are code-instantiated (the declared-in-tscn exception).
    private void RefreshItems()
    {
        foreach (var child in itemsGrid.GetChildren())
            child.QueueFree();
        var conn = GameManager.Conn;
        if (conn == null) return;
        foreach (var row in conn.Db.NearbyChestItems.Iter())
        {
            if (row.ChestId != ChestId) continue;
            var slot = ChestSlotScene.Instantiate<ChestSlotComponent>();
            itemsGrid.AddChild(slot);
            slot.Setup(row.ChestItemId, row.ItemId);
        }
    }
}
