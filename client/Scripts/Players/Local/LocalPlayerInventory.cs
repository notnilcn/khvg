#nullable enable
using Godot;

public partial class LocalPlayerInventory : Control
{
    private static readonly string[] HotbarActions = ["Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4"];

    [Export] public Godot.Collections.Array<InventorySlotUI> HotbarSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<InventorySlotUI> ArmorSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<InventorySlotUI> AccessorySlots { get; set; } = [];
    [Export] public Godot.Collections.Array<InventorySlotUI> ArtifactSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<InventorySlotUI> GeneralSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<InventorySlotUI> InventoryHotbarSlots { get; set; } = [];

    private Control menuLayout = null!;
    private Control hotbar = null!;

    public override void _Ready()
    {
        menuLayout = GetNode<Control>("Menu");
        hotbar = GetNode<Control>("Hotbar");
        menuLayout.Visible = false;

        if (Owner is not LocalPlayer player) return;
        player.InventoryChanged += OnInventoryChanged;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || key.PhysicalKeycode != Key.Tab) return;

        menuLayout.Visible = !menuLayout.Visible;
        hotbar.Visible = !hotbar.Visible;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (Owner is not LocalPlayer player || !hotbar.Visible) return;

        for (int i = 0; i < HotbarActions.Length; i++)
        {
            if (!Input.IsActionJustPressed(HotbarActions[i])) continue;
            int slotIndex = i + 1;
            if (player.GetSlotItemId(slotIndex) == null) continue;
            GameManager.Instance.Conn?.Reducers.UseItem((uint)slotIndex);
        }
    }

    private void OnInventoryChanged()
    {
        if (Owner is not LocalPlayer player) return;

        UpdateSection(player, HotbarSlots);
        UpdateSection(player, ArmorSlots);
        UpdateSection(player, AccessorySlots);
        UpdateSection(player, ArtifactSlots);
        UpdateSection(player, GeneralSlots);
        UpdateSection(player, InventoryHotbarSlots);
    }

    private static void UpdateSection(LocalPlayer player, Godot.Collections.Array<InventorySlotUI> slots)
    {
        foreach (var slot in slots)
            SetSlotTexture(slot.Icon, player.GetSlotItemId((int)slot.SlotIndex));
    }

    private static void SetSlotTexture(TextureRect rect, string? itemId)
    {
        var textureId = itemId != null ? GameManager.Instance.GetItem(itemId)?.TextureId : null;
        var resPath = textureId != null ? GameManager.Instance.GetResPath(textureId) : null;
        rect.Texture = resPath != null ? GD.Load<Texture2D>(resPath) : null;
    }

    public override void _ExitTree()
    {
        if (Owner is LocalPlayer player)
            player.InventoryChanged -= OnInventoryChanged;
    }
}
