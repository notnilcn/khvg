#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;

/// <summary>
/// The local player's inventory UI root (the InventoryComponent node in
/// inventory_panel.tscn, instanced under LocalPlayer): hotbar/backpack visibility
/// toggle, hotbar key presses (UseItem reducer), ability key presses (ActivateAbility
/// reducer), and slot icon refresh on InventoryChanged. Registers with the LocalPlayer
/// entity through the ancestor walk, so Entity IS the LocalPlayer. The always-visible
/// Equipment panel (weapon / abilities / armor / accessory columns) sits next to the
/// Hotbar on the HUD; the Backpack (general storage) stays in the Tab-toggled Menu.
/// </summary>
public partial class InventoryComponent : ControlComponent
{
    private static readonly string[] HotbarActions = ["Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4"];
    private static readonly string[] AbilityActions = ["Ability1", "Ability2", "Ability3", "Ability4", "Ability5", "Ability6"];

    [Export] public Godot.Collections.Array<SlotComponent> HotbarSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> EquipmentWeaponSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> AbilitySlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> EquipmentArmorSlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> EquipmentAccessorySlots { get; set; } = [];
    [Export] public Godot.Collections.Array<SlotComponent> GeneralSlots { get; set; } = [];

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
        if (player == null) return;

        if (hotbar.Visible)
        {
            for (int i = 0; i < HotbarActions.Length; i++)
            {
                if (!Input.IsActionJustPressed(HotbarActions[i])) continue;
                int slotIndex = i + 1;
                if (player.GetSlotItemId(slotIndex) == null) continue;
                GameManager.Conn?.Reducers.UseItem((uint)slotIndex);
            }
        }

        for (int i = 0; i < AbilityActions.Length; i++)
        {
            if (!Input.IsActionJustPressed(AbilityActions[i])) continue;
            LocalPlayerInventoryComponent.TryActivateAbility(player, LocalPlayerInventoryComponent.AbilitySlotStart + i);
        }
    }

    private void OnInventoryChanged()
    {
        if (player == null) return;

        UpdateSection(player, HotbarSlots);
        UpdateSection(player, EquipmentWeaponSlots);
        UpdateSection(player, AbilitySlots);
        UpdateSection(player, EquipmentArmorSlots);
        UpdateSection(player, EquipmentAccessorySlots);
        UpdateSection(player, GeneralSlots);
    }

    private static void UpdateSection(LocalPlayer player, Godot.Collections.Array<SlotComponent> slots)
    {
        foreach (var slot in slots)
            UpdateSlotIcon(player, slot);
    }

    private static void UpdateSlotIcon(LocalPlayer player, SlotComponent slot)
    {
        var own = player.ResolveSlotAt((int)slot.SlotIndex).Slot;
        // Span followers show their head's icon; runtime state (cooldown) lives on the head.
        var head = own?.OccupiedBy is uint h ? player.ResolveSlotAt((int)h).Slot : own;
        var itemId = own?.ItemId ?? head?.ItemId;

        SetSlotTexture(slot.Icon, itemId);

        float alpha = 1f;
        if (itemId != null && own?.OccupiedBy != null) alpha = 0.4f; // follower cell
        if (itemId != null && head?.CooldownUntil is SpacetimeDB.Timestamp until && IsOnCooldown(until)) alpha = 0.4f;
        slot.Icon.Modulate = new Color(1, 1, 1, alpha);
    }

    private static bool IsOnCooldown(SpacetimeDB.Timestamp until) =>
        until.MicrosecondsSinceUnixEpoch > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

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
