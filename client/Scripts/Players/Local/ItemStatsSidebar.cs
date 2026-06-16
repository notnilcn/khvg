#nullable enable
using Godot;

public partial class ItemStatsSidebar : Control
{
    public static ItemStatsSidebar? Instance { get; private set; }

    [Export] public Label ItemNameLabel { get; set; } = null!;
    [Export] public TextureRect ItemIcon { get; set; } = null!;
    [Export] public Label DescriptionLabel { get; set; } = null!;
    [Export] public Label EmptyLabel { get; set; } = null!;

    public override void _Ready()
    {
        Instance = this;
        Clear();
    }

    public void ShowSlot(uint slotIndex)
    {
        var player = LocalPlayer.Local;
        if (player == null) return;
        var resolved = player.ResolveSlotAt((int)slotIndex);
        if (resolved.IsEmpty)
        {
            Clear();
            return;
        }
        var item = resolved.Item!;
        EmptyLabel.Visible = false;
        ItemNameLabel.Text = item.DisplayName;
        DescriptionLabel.Text = item.Description;
        var resPath = GameManager.Instance.GetResPath(item.TextureId);
        ItemIcon.Texture = resPath != null ? GD.Load<Texture2D>(resPath) : null;
        ItemNameLabel.Visible = true;
        ItemIcon.Visible = true;
        DescriptionLabel.Visible = true;
    }

    public void Clear()
    {
        EmptyLabel.Visible = true;
        ItemNameLabel.Visible = false;
        ItemIcon.Visible = false;
        DescriptionLabel.Visible = false;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
}
