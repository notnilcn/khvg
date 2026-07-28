#nullable enable
using Godot;

/// <summary>
/// The local player's inventory UI root (the InventoryComponent node in
/// inventory_panel.tscn, instanced under LocalPlayer): hotbar/backpack visibility
/// toggle, hotbar key presses (UseItem reducer), and slot icon refresh on
/// InventoryChanged. Registers with the LocalPlayer entity through the ancestor walk,
/// so Entity IS the LocalPlayer. The Hotbar/Backpack containers stay plain nodes — the
/// slot-section wiring crosses both containers, so splitting them into their own
/// components would be artificial.
/// </summary>
public partial class InventoryComponent : ControlComponent
{
    private static readonly string[] HotbarActions = ["Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4"];

    [Export] public Godot.Collections.Array<SlotComponent> HotbarSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> ArmorSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> AccessorySlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> ArtifactSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> GeneralSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> InventoryHotbarSlots { get; set; } = [];

    private Control menuLayout = null!;
    private Control hotbar = null!;
    private LocalPlayer? player;

    protected override void OnRegistered()
    {
        menuLayout = GetNode<Control>("Menu");
        hotbar = GetNode<Control>("Hotbar");
        menuLayout.Visible = false;

        player = Entity as LocalPlayer;
        if (player != null)
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
        if (player == null || !hotbar.Visible) return;

        for (int i = 0; i < HotbarActions.Length; i++)
        {
            if (!Input.IsActionJustPressed(HotbarActions[i])) continue;
            int slotIndex = i + 1;
            if (player.GetSlotItemId(slotIndex) == null) continue;
            GameManager.Conn?.Reducers.UseItem((uint)slotIndex);
        }
    }

    private void OnInventoryChanged()
    {
        if (player == null) return;

        UpdateSection(player, HotbarSlots);
        UpdateSection(player, ArmorSlots);
        UpdateSection(player, AccessorySlots);
        UpdateSection(player, ArtifactSlots);
        UpdateSection(player, GeneralSlots);
        UpdateSection(player, InventoryHotbarSlots);
    }

    private static void UpdateSection(LocalPlayer player, Godot.Collections.Array<SlotComponent> slots)
    {
        foreach (var slot in slots)
            SetSlotTexture(slot.Icon, player.GetSlotItemId((int)slot.SlotIndex));
    }

    private static void SetSlotTexture(TextureRect rect, string? itemId)
    {
        var textureId = itemId != null ? GameManager.GetItem(itemId)?.TextureId : null;
        var resPath = textureId != null ? GameManager.GetResPath(textureId) : null;
        rect.Texture = resPath != null ? GD.Load<Texture2D>(resPath) : null;
    }

    public override void _ExitTree()
    {
        if (player != null)
            player.InventoryChanged -= OnInventoryChanged;
        base._ExitTree();
    }
}
