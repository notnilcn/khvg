#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The item-details sidebar in inventory_panel.tscn: shows the hovered slot's item
/// name/icon/description plus full composition — base stat modifiers, stat requirements
/// (red when unmet), behavior summary (weapon items show their toggle-pattern list with
/// the active one marked; abilities show effect/cooldown/charges), innate enchantments
/// (unremovable, no socket cost), socketed enchantments (N / MaxEnchantments), and — for
/// enchantable items in equipment slots — applicable enchantments with Socket/Remove
/// buttons calling the ApplyEnchantment/RemoveEnchantment reducers. One
/// enchantment_row.tscn instance per enchantment (data-driven count). Reached by the
/// sibling SlotComponents through GetSibling — no singleton. Refreshes on
/// InventoryChanged and GameManager.EnchantmentsChanged; stays open while the mouse is
/// over it so buttons are clickable.
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

        RenderRequirements(item);

        // Weapon toggle patterns: the equipped weapon lists every option (its own
        // behaviors + enchantment grants) with the active one marked; elsewhere the
        // item's own behaviors are listed plain.
        var ownWeapons = new List<WeaponBehavior>();
        foreach (var behavior in item.Behaviors)
            if (behavior is ItemBehavior.Weapon(var weapon)) ownWeapons.Add(weapon);
        if (shownSlot == 0 && LocalPlayer.Local != null)
        {
            var options = EffectiveWeaponResolver.ToggleOptions(LocalPlayer.Local);
            uint active = resolved.Slot?.ActiveToggle ?? 0;
            for (int i = 0; i < options.Count; i++)
                DetailsList.AddChild(MakeLabel($"{(i == (int)active ? "▶ " : "  ")}{FormatWeaponOption(options[i])}"));
        }
        else
        {
            foreach (var weapon in ownWeapons)
                DetailsList.AddChild(MakeLabel(FormatWeaponOption(weapon)));
        }

        foreach (var behavior in item.Behaviors)
        {
            var text = behavior switch
            {
                ItemBehavior.Weapon => null, // rendered above
                ItemBehavior.Consumable(var consumable) => FormatConsumable(consumable),
                ItemBehavior.Ability(var ability) => FormatAbility(ability),
                _ => null,
            };
            if (!string.IsNullOrEmpty(text))
                DetailsList.AddChild(MakeLabel(text));
        }

        if (item.InnateEnchantmentIds.Count > 0)
        {
            DetailsList.AddChild(MakeLabel("Innate:"));
            foreach (var enchantmentId in item.InnateEnchantmentIds)
            {
                var enchantment = GameManager.GetEnchantment(enchantmentId);
                if (enchantment != null)
                    DetailsList.AddChild(MakeEnchantmentRow(enchantment, socketed: true, disabled: false, interactive: false));
            }
        }

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

    // Stat requirements (doc 03 class thresholds) — only shown when the item gates on
    // anything; red while the player's resolved stats fall short.
    private void RenderRequirements(Item item)
    {
        var req = item.StatRequirements;
        var parts = new List<string>();
        if (req.Strength > 0) parts.Add($"STR {req.Strength}");
        if (req.Wisdom > 0) parts.Add($"WIS {req.Wisdom}");
        if (req.Dexterity > 0) parts.Add($"DEX {req.Dexterity}");
        if (req.DamageDealer > 0) parts.Add($"DPS {req.DamageDealer}");
        if (req.Supporter > 0) parts.Add($"SUP {req.Supporter}");
        if (req.Artisan > 0) parts.Add($"ART {req.Artisan}");
        if (parts.Count == 0) return;

        var label = MakeLabel("Requires " + string.Join(", ", parts));
        var local = LocalPlayer.Local;
        bool unmet = local != null && (
            local.Strength < req.Strength || local.Wisdom < req.Wisdom || local.Dexterity < req.Dexterity ||
            local.DamageDealer < req.DamageDealer || local.Supporter < req.Supporter || local.Artisan < req.Artisan);
        if (unmet)
            label.Modulate = new Color(1f, 0.4f, 0.4f);
        DetailsList.AddChild(label);
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
        var lines = enchantment.StatModifiers.Select(FormatModifier)
            .Concat(enchantment.Behaviors.Select(FormatEnchantmentBehavior)).ToList();
        if (lines.Count > 0)
            stats.Text = string.Join("\n", lines);
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

    // Per-bullet damage is Damage / ShotCount (server divides the per-trigger damage
    // among the bullets) — shown so the shot-count tradeoff is visible.
    private static string FormatWeaponOption(WeaponBehavior weapon) =>
        $"{weapon.Pattern} — {(float)weapon.Damage / Math.Max(1u, weapon.ShotCount):0.#} dmg × {weapon.ShotCount} | {weapon.FireRate:0.#}/s | Range {weapon.Range:0}";

    private static string FormatConsumable(ConsumableBehavior consumable) => consumable.Effect switch
    {
        ConsumableEffect.Heal => $"Heals {consumable.Potency:0.#} HP",
        ConsumableEffect.Buff(var buff) => $"{FormatBuff(buff)} for {consumable.Duration:0.#}s",
        _ => "",
    };

    private static string FormatAbility(AbilityBehavior ability)
    {
        string effect = ability.Effect switch
        {
            AbilityEffect.Heal => $"Heals {ability.Potency:0.#} HP",
            AbilityEffect.Buff(var buff) => $"{FormatBuff(buff)} for {ability.Duration:0.#}s",
            AbilityEffect.DeleteBullets(var radius) => $"Erases enemy bullets within {radius:0} of the cursor",
            AbilityEffect.SplitBullets(var radius) => $"Splits enemy bullets within {radius:0} of the cursor",
            AbilityEffect.AttractBullets(var radius) => $"Drags enemy bullets within {radius:0} toward the cursor",
            _ => "",
        };
        string charges = ability.MaxCharges > 0 ? $" | {ability.MaxCharges} charges" : "";
        return $"{effect} | {ability.CooldownSeconds:0.#}s CD{charges}";
    }

    private static string FormatBuff(ConsumableBuffEffect buff) => buff switch
    {
        ConsumableBuffEffect.Strength(var value) => $"+{value:0.#} Strength",
        ConsumableBuffEffect.Wisdom(var value) => $"+{value:0.#} Wisdom",
        ConsumableBuffEffect.Dexterity(var value) => $"+{value:0.#} Dexterity",
        ConsumableBuffEffect.DamageDealer(var value) => $"+{value:0.#} DamageDealer",
        ConsumableBuffEffect.Supporter(var value) => $"+{value:0.#} Supporter",
        ConsumableBuffEffect.Artisan(var value) => $"+{value:0.#} Artisan",
        _ => "",
    };

    private static string FormatEnchantmentBehavior(EnchantmentBehavior behavior) => behavior switch
    {
        EnchantmentBehavior.AddShots(var n) => $"{(n >= 0 ? "+" : "")}{n} bullets",
        EnchantmentBehavior.ShotCountMult(var m) => $"×{1f + m:0.#} bullets",
        EnchantmentBehavior.FlatBulletDamageMod(var v) => $"{(v >= 0 ? "+" : "")}{v:0.#} bullet damage",
        EnchantmentBehavior.TrueDamageFlat(var v) => $"+{v:0.#} true damage",
        EnchantmentBehavior.TrueDamagePercent(var v) => $"+{v * 100f:0.#}% true damage",
        EnchantmentBehavior.OnAbilityUseBuff(var p) => $"On ability use: {FormatBuff(p.Buff)} for {p.Duration:0.#}s",
        EnchantmentBehavior.WeaponToggle(var w) => $"Toggle: {FormatWeaponOption(w)}",
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
