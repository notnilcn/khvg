#nullable enable
using Godot;

/// <summary>
/// A single inventory slot (declared across inventory_panel.tscn for the hotbar, the
/// always-visible Equipment panel, and the Backpack grid; the node names stay
/// `<Type> - <Index>` because the exported slot-section arrays reference those paths).
/// Drag/drop reports the SwapSlots reducer, left-click on an ability slot reports
/// ActivateAbility, right-click in the backpack reports DropItem, and hover shows the
/// scene-unique ItemSidebar.
/// </summary>
public partial class SlotComponent : ControlComponent
{
    [Export] public uint SlotIndex { get; set; }
    [Export] public TextureRect Icon { get; set; } = null!;

    private bool inBackpack;
    private ItemSidebar _sidebar = null!;

    protected override void OnRegistered()
    {
        // The icon sits on top of the slot; it must not become the drag/hover target or
        // _GetDragData/_CanDropData on this control are never consulted.
        Icon.MouseFilter = MouseFilterEnum.Ignore;

        for (var node = GetParent(); node != null && node != Owner; node = node.GetParent())
        {
            if (node.Name != "Backpack") continue;
            inBackpack = true;
            break;
        }
        _sidebar = GetNode<ItemSidebar>("%ItemSidebar");
        MouseEntered += () => _sidebar.ShowSlot(SlotIndex);
        MouseExited += () => _sidebar.QueueHoverClear();
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Left-click on an ability slot activates it (span followers activate their head).
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            && LocalPlayerInventoryComponent.IsAbilitySlot((int)SlotIndex))
        {
            var slot = LocalPlayer.Local?.ResolveSlotAt((int)SlotIndex).Slot;
            if (slot != null && (slot.ItemId != null || slot.OccupiedBy != null) && LocalPlayer.Local != null)
                LocalPlayerInventoryComponent.TryActivateAbility(LocalPlayer.Local, (int)SlotIndex);
            return;
        }

        if (!inBackpack || Icon.Texture == null) return;
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) return;
        GameManager.Conn?.Reducers.DropItem(SlotIndex);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Icon.Texture == null) return default;

        SetDragPreview(new TextureRect { Texture = Icon.Texture, Size = Size, Modulate = new Color(1, 1, 1, 0.6f) });
        return SlotIndex;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => data.VariantType == Variant.Type.Int;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var fromIndex = data.AsUInt32();
        if (fromIndex == SlotIndex) return;
        GameManager.Conn?.Reducers.SwapSlots(fromIndex, SlotIndex);
    }
}
