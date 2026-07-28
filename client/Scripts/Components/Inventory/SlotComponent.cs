#nullable enable
using Godot;

/// <summary>
/// A single inventory slot (declared 24 times in inventory_panel.tscn; the node names
/// stay `<Type> - <Index>` because the exported slot-section arrays reference those
/// paths). Drag/drop reports the SwapSlots reducer, right-click in the backpack reports
/// DropItem, and hover shows the sibling ItemSidebarComponent.
/// </summary>
public partial class SlotComponent : ControlComponent
{
    [Export] public uint SlotIndex { get; set; }
    [Export] public TextureRect Icon { get; set; } = null!;

    private bool inBackpack;

    protected override void OnRegistered()
    {
        for (var node = GetParent(); node != null && node != Owner; node = node.GetParent())
        {
            if (node.Name != "Backpack") continue;
            inBackpack = true;
            break;
        }
        MouseEntered += () => GetSibling<ItemSidebarComponent>()?.ShowSlot(SlotIndex);
        MouseExited += () => GetSibling<ItemSidebarComponent>()?.QueueHoverClear();
    }

    public override void _GuiInput(InputEvent @event)
    {
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
