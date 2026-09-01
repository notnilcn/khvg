#nullable enable
using Godot;

/// <summary>
/// One item entry inside a chest's open panel (code-instantiated by Chest.RefreshItems —
/// the chest's contents are data-driven). Drag source only: dragging one onto a player
/// inventory SlotComponent reports the TakeFromChest reducer (see SlotComponent._DropData).
/// Plain Control script, like the other inventory UI pieces.
/// </summary>
public partial class ChestSlotComponent : Control
{
    /// The server ChestItem row id this slot drags out.
    public ulong ChestItemId { get; private set; }

    private TextureRect icon = null!;

    public override void _Ready()
    {
        icon = GetNode<TextureRect>("Icon");
        // The icon sits on top of the slot; it must not become the drag target or
        // _GetDragData on this control is never consulted (the SlotComponent rule).
        icon.MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Setup(ulong chestItemId, string itemId)
    {
        ChestItemId = chestItemId;
        var item = GameManager.GetItem(itemId);
        var resPath = item != null ? GameManager.GetResPath2D(item.TextureId) : null;
        if (resPath != null)
            icon.Texture = GD.Load<Texture2D>(resPath);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (icon.Texture == null) return default;

        SetDragPreview(new TextureRect { Texture = icon.Texture, Size = Size, Modulate = new Color(1, 1, 1, 0.6f) });
        return new Godot.Collections.Dictionary { { "chest_item_id", ChestItemId } };
    }
}
