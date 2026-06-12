#nullable enable
using Godot;

public partial class InventorySlotUI : Control
{
    [Export] public uint SlotIndex { get; set; }
    [Export] public TextureRect Icon { get; set; } = null!;
}
