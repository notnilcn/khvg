#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Linq;

/// <summary>
/// The item-details sidebar in inventory_panel.tscn: shows the hovered slot's item
/// name/icon/description plus full composition — base stat modifiers, behavior summary,
/// socketed enchantments (N / MaxEnchantments), and, for enchantable items in equipment
/// slots, applicable enchantments with Socket/Remove buttons calling the
/// ApplyEnchantment/RemoveEnchantment reducers. One enchantment_row.tscn instance per
/// enchantment (data-driven count). Reached by the sibling SlotComponents through
/// GetSibling — no singleton. Refreshes on InventoryChanged and
/// GameManager.EnchantmentsChanged; stays open while the mouse is over it so buttons
/// are clickable.
/// </summary>
public partial class ItemSidebarComponent : ControlComponent
{
    [Export] public Label ItemNameLabel { get; set; } = null!;
    [Export] public TextureRect ItemIcon { get; set; } = null!;
    [Export] public Label DescriptionLabel { get; set; } = null!;
    [Export] public Label EmptyLabel { get; set; } = null!;
    [Export] public ScrollContainer DetailsScroll { get; set; } = null!;
    [Export] public VBoxContainer DetailsList { get; set; } = null!;
    [Export] public PackedScene EnchantmentRowScene { get; set; } = null!;

    private int shownSlot = -1;
    private LocalPlayer? player;

    protected override void OnRegistered()
    {
        MouseExited += QueueHoverClear;
        player = Entity as LocalPlayer;
        if (player != null)
            player.InventoryChanged += RefreshShownSlot;
        GameManager.EnchantmentsChanged += RefreshShownSlot;
        Clear();
    }

    public void ShowSlot(uint slotIndex)
    {
        shownSlot = (int)slotIndex;
        Render();
    }

    public void Clear()
    {
        shownSlot = -1;
        EmptyLabel.Visible = true;
        ItemNameLabel.Visible = false;
        ItemIcon.Visible = false;
        DescriptionLabel.Visible = false;
        DetailsScroll.Visible = false;
    }

    // Called on MouseExited by inventory slots and the sidebar itself. The panel
    // stays up while the mouse moves between slots and the sidebar so the
    // Socket/Remove buttons remain clickable.
    public void QueueHoverClear() => CallDeferred(nameof(ClearIfMouseGone));

    private void ClearIfMouseGone()
    {
        var hovered = GetViewport().GuiGetHoveredControl();
        if (hovered == this || (hovered != null && IsAncestorOf(hovered)) || hovered is SlotComponent) return;
        Clear();
    }

    private void RefreshShownSlot()
    {
        if (shownSlot >= 0) Render();
    }

    private void Render()
    {
        var local = LocalPlayer.Local;
        if (local == null || shownSlot < 0)
        {
            Clear();
            return;
        }
        var resolved = local.ResolveSlotAt(shownSlot);
        if (resolved.IsEmpty)
        {
            Clear();
            return;
        }
        var item = resolved.Item!;
        EmptyLabel.Visible = false;
        ItemNameLabel.Text = item.DisplayName;
        DescriptionLabel.Text = item.Description;
        var resPath = GameManager.GetResPath(item.TextureId);
        ItemIcon.Texture = resPath != null ? GD.Load<Texture2D>(resPath) : null;
        ItemNameLabel.Visible = true;
        ItemIcon.Visible = true;
        DescriptionLabel.Visible = true;
        RenderDetails(resolved, item);
    }

    private void RenderDetails(ResolvedSlot resolved, Item item)
    {
        foreach (var child in DetailsList.GetChildren())
            child.Free();

        if (item.StatModifiers.Count > 0)
            DetailsList.AddChild(MakeLabel(string.Join("\n", item.StatModifiers.Select(FormatModifier))));

        foreach (var behavior in item.Behaviors)
            DetailsList.AddChild(MakeLabel(FormatBehavior(behavior)));

        var slot = resolved.Slot;
        if (slot != null && item.MaxEnchantments > 0)
        {
            int filled = slot.EnchantmentIds.Count;
            bool full = filled >= item.MaxEnchantments;
            bool interactive = LocalPlayer.IsEquipmentSlot(shownSlot);
            DetailsList.AddChild(MakeLabel($"Sockets {filled} / {item.MaxEnchantments}"));

            foreach (var enchantmentId in slot.EnchantmentIds)
            {
                var enchantment = GameManager.GetEnchantment(enchantmentId);
                if (enchantment != null)
                    DetailsList.AddChild(MakeEnchantmentRow(enchantment, socketed: true, disabled: false, interactive));
            }

            if (interactive)
            {
                foreach (var enchantment in GameManager.GetEnchantments())
                {
                    if (!enchantment.AllowedSlots.Contains(item.EquipSlot)) continue;
                    if (slot.EnchantmentIds.Contains(enchantment.EnchantmentId)) continue;
                    DetailsList.AddChild(MakeEnchantmentRow(enchantment, socketed: false, disabled: full, interactive));
                }
            }
        }

        DetailsScroll.Visible = DetailsList.GetChildCount() > 0;
    }

    private Control MakeEnchantmentRow(Enchantment enchantment, bool socketed, bool disabled, bool interactive)
    {
        var row = EnchantmentRowScene.Instantiate<HBoxContainer>();

        var icon = row.GetNode<TextureRect>("Icon");
        var resPath = GameManager.GetResPath(enchantment.TextureId);
        if (resPath != null)
            icon.Texture = GD.Load<Texture2D>(resPath);
        else
            icon.Hide();

        var text = row.GetNode<VBoxContainer>("Text");
        text.GetNode<Label>("Name").Text = enchantment.DisplayName;
        var stats = text.GetNode<Label>("Stats");
        if (enchantment.StatModifiers.Count > 0)
            stats.Text = string.Join("\n", enchantment.StatModifiers.Select(FormatModifier));
        else
            stats.Hide();

        var button = row.GetNode<Button>("ActionButton");
        if (interactive)
        {
            button.Text = socketed ? "Remove" : "Socket";
            button.Disabled = disabled;
            uint slotIndex = (uint)shownSlot;
            string enchantmentId = enchantment.EnchantmentId;
            button.Pressed += () =>
            {
                if (socketed)
                    GameManager.Conn?.Reducers.RemoveEnchantment(slotIndex, enchantmentId);
                else
                    GameManager.Conn?.Reducers.ApplyEnchantment(slotIndex, enchantmentId);
            };
        }
        else
        {
            button.Hide();
        }
        return row;
    }

    // Sizes are in the 1200x720 design space of inventory_panel.tscn; the project's
    // canvas_items stretch mode scales them with the window.
    private static Label MakeLabel(string text, int fontSize = 13)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Uppercase = true,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static string FormatModifier(StatModifier modifier)
    {
        string sign = modifier.Amount >= 0 ? "+" : "-";
        float magnitude = Math.Abs(modifier.Amount);
        return modifier.Mode == StatMode.Mult
            ? $"{sign}{magnitude * 100f:0.#}% {modifier.Stat}"
            : $"{sign}{magnitude:0.#} {modifier.Stat}";
    }

    private static string FormatBehavior(ItemBehavior behavior) => behavior switch
    {
        ItemBehavior.Weapon(var weapon) => $"Damage {weapon.Damage} | {weapon.FireRate:0.#}/s | Range {weapon.Range:0}",
        ItemBehavior.Consumable(var consumable) => FormatConsumable(consumable),
        _ => "",
    };

    private static string FormatConsumable(ConsumableBehavior consumable) => consumable.Effect switch
    {
        ConsumableEffect.Heal => $"Heals {consumable.Potency:0.#} HP",
        ConsumableEffect.Buff(var buff) => $"{FormatBuff(buff)} for {consumable.Duration:0.#}s",
        _ => "",
    };

    private static string FormatBuff(ConsumableBuffEffect buff) => buff switch
    {
        ConsumableBuffEffect.Strength(var value) => $"+{value:0.#} Strength",
        ConsumableBuffEffect.Wisdom(var value) => $"+{value:0.#} Wisdom",
        ConsumableBuffEffect.Dexterity(var value) => $"+{value:0.#} Dexterity",
        ConsumableBuffEffect.Defense(var value) => $"+{value:0.#} Defense",
        ConsumableBuffEffect.Vitality(var value) => $"+{value:0.#} Vitality",
        ConsumableBuffEffect.Speed(var value) => $"+{value:0.#} Speed",
        _ => "",
    };

    public override void _ExitTree()
    {
        if (player != null)
            player.InventoryChanged -= RefreshShownSlot;
        GameManager.EnchantmentsChanged -= RefreshShownSlot;
        base._ExitTree();
    }
}
