#nullable enable
using Godot;

public partial class InventorySlotUI : Control
{
    [Export] public uint SlotIndex { get; set; }
    [Export] public TextureRect Icon { get; set; } = null!;

    private bool inBackpack;

    public override void _Ready()
    {
        for (var node = GetParent(); node != null && node != Owner; node = node.GetParent())
        {
            if (node.Name != "Backpack") continue;
            inBackpack = true;
            break;
        }
        MouseEntered += () => ItemStatsSidebar.Instance?.ShowSlot(SlotIndex);
        MouseExited += () => ItemStatsSidebar.Instance?.Clear();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!inBackpack || Icon.Texture == null) return;
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) return;
        GameManager.Instance.Conn?.Reducers.DropItem(SlotIndex);
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
        GameManager.Instance.Conn?.Reducers.SwapSlots(fromIndex, SlotIndex);
    }
}
