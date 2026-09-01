#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The item-details sidebar in the inventory UI declared inline in local_player.tscn:
/// shows the hovered slot's item
/// name/icon/description plus full composition — base stat modifiers, stat requirements
/// (red when unmet), behavior summary (weapon items show their toggle-pattern list with
/// the active one marked; abilities show effect/cooldown/charges), innate enchantments
/// (unremovable, no socket cost), socketed enchantments (N / MaxEnchantments), and — for
/// enchantable items in equipment slots — applicable enchantments with Socket/Remove
/// buttons calling the ApplyEnchantment/RemoveEnchantment reducers. One
/// enchantment_row.tscn instance per enchantment (data-driven count). Reached by the
/// SlotComponents through the scene-unique %ItemSidebar lookup — no singleton. Refreshes on
/// InventoryChanged and GameManager.EnchantmentsChanged; stays open while the mouse is
/// over it so buttons are clickable.
/// </summary>
public partial class ItemSidebar : Control
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

    public override void _Ready()
    {
        MouseExited += QueueHoverClear;
        player = this.GetAncestor<LocalPlayer>();
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
        var local = player;
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
        var resPath = GameManager.GetResPath2D(item.TextureId);
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
        if (shownSlot == 0 && player != null)
        {
            var options = EffectiveWeaponResolver.ToggleOptions(player);
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
        var local = player;
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
        var resPath = GameManager.GetResPath2D(enchantment.TextureId);
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

    // Sizes are in the 1200x720 design space of the inventory UI in local_player.tscn;
    // the project's canvas_items stretch mode scales them with the window.
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
            AbilityEffect.SplitBullets(var p) => $"Splits enemy bullets in two in a {p.Length:0}×{p.Width:0} rectangle in front of you",
            AbilityEffect.AttractBullets(var radius) => ability.Duration > 0
                ? $"Summons a black hole at the cursor for {ability.Duration:0.#}s, dragging in enemy bullets within {radius:0}"
                : $"Drags enemy bullets within {radius:0} toward the cursor",
            AbilityEffect.DeleteBulletsInRect(var p) => $"Deletes enemy bullets in a {p.Length:0}×{p.Width:0} rectangle in front of you",
            AbilityEffect.MarkBleed(var p) => (p.AllyRadius > 0 ? $"For you and allies within {p.AllyRadius:0}: " : "")
                + $"mark the enemy near the cursor for {ability.Duration:0.#}s — {p.Conversion * 100f:0}% of its hits bleed over ~{p.PoolSeconds:0.#}s"
                + (p.Outgoing ? ", and your hits on it become a bleed on the enemy" : "")
                + $" (sheds: {FormatPurge(p.Purge)})",
            AbilityEffect.MarkStore(var p) => (p.AllyRadius > 0 ? $"For you and allies within {p.AllyRadius:0}: " : "")
                + $"mark the enemy near the cursor for {ability.Duration:0.#}s — your damage to it is stored, then released ({FormatWager(p)})",
            AbilityEffect.StoreIncoming(var p) => p.Release == SpacetimeDB.Types.ReleaseKind.Smite
                ? $"Store the damage you take for {ability.Duration:0.#}s, then unleash it within {p.ReleaseRadius:0} — recast to release early at the cursor"
                : $"Store the damage you take for {ability.Duration:0.#}s; your next shield bash unleashes it",
            AbilityEffect.Invulnerable(var radius) => radius > 0
                ? $"You and allies within {radius:0} are invulnerable for {ability.Duration:0.#}s"
                : $"Invulnerable for {ability.Duration:0.#}s",
            AbilityEffect.DamageShield(var radius) => radius > 0
                ? $"You and allies within {radius:0} absorb your next hit within {ability.Duration:0.#}s"
                : $"Absorbs your next hit within {ability.Duration:0.#}s",
            AbilityEffect.ReduceDamage(var p) => (p.AllyRadius > 0 ? $"For you and allies within {p.AllyRadius:0}: " : "")
                + $"{p.Pct * 100f:0}% reduced damage taken for {ability.Duration:0.#}s",
            AbilityEffect.RedirectToCaster(var radius) => $"Hits on allies within {radius:0} are redirected to you for {ability.Duration:0.#}s",
            AbilityEffect.RegenAura(var p) => (p.AllyRadius > 0 ? $"You and allies within {p.AllyRadius:0} regenerate" : "Regenerate")
                + $" {p.PerSecond:0.#} HP/s for {ability.Duration:0.#}s",
            AbilityEffect.Curse(var p) => $"Curses the enemy near the cursor for {ability.Duration:0.#}s: {FormatEnemyStatus(p.Kind, p.Magnitude)}",
            AbilityEffect.Bash(var p) => $"Bashes enemies within {p.Range:0} for weapon damage and {p.Stagger:0} stagger; a full stagger bar stuns for {p.StunSeconds:0.#}s"
                + (p.ArmorBreak ? " and breaks their armor" : ""),
            AbilityEffect.Slash(var p) => $"Erases enemy bullets dealing {p.MaxBulletDamage} damage or less in a {p.Length:0}×{p.Width:0} slash in front of you",
            AbilityEffect.Wall(var p) => $"Raises a {p.Length:0}×{p.Width:0} wall in front of you for {ability.Duration:0.#}s, erasing enemy bullets that cross it",
            AbilityEffect.CastZone(var p) => $"Draws a {FormatZoneKind(p.Kind, p.Magnitude)} at the cursor (radius {p.Radius:0}) for {ability.Duration:0.#}s",
            AbilityEffect.Teleport(var p) => p.ChargeScaled
                ? $"Hold to charge (slowed, can't fire), release to teleport toward the cursor — up to {p.Range:0} at full charge"
                : $"Teleport up to {p.Range:0} toward the cursor",
            AbilityEffect.Grapple(var range) => $"Pull yourself up to {range:0} toward the cursor",
            AbilityEffect.Spell(var p) => FormatSpell(p),
            _ => "",
        };
        string charges = ability.MaxCharges > 0 ? $" | {ability.MaxCharges} charges" : "";
        return $"{effect} | {ability.CooldownSeconds:0.#}s CD{charges}";
    }

    private static string FormatSpell(SpellParams p)
    {
        string shape = p.Shape switch
        {
            SpellShape.Circle(var c) => $"a circle (radius {c.Radius:0}) at the cursor",
            SpellShape.Line(var l) => $"a {l.Length:0}×{l.Width:0} line in front of you",
            SpellShape.Cone(var c) => $"a cone ({c.Length:0} long, {c.Spread:0.##} rad spread) in front of you",
            _ => "an area",
        };
        string hits = p.Hits > 1 ? $" in {p.Hits} hits of {p.Damage / p.Hits:0.#}" : "";
        string charge = p.ChargeScaled ? " — hold to charge (slowed, can't fire)" : "";
        return $"Deals {p.Damage:0} damage{hits} in {shape}, scaling with {p.Scaling}{charge}";
    }

    private static string FormatWager(SpacetimeDB.Types.MarkStoreParams p) => p.Wager switch
    {
        SpacetimeDB.Types.WagerKind.NoHits => $"2× at 0 hits taken, 0× at {p.MaxHits + 1}+",
        SpacetimeDB.Types.WagerKind.Radius => $"2× if you never leave {p.R1:0}, 0× if you ever leave {p.R2:0}",
        SpacetimeDB.Types.WagerKind.Damage => $"2× at {p.X:0}+ stored, 0× below {p.Z:0}",
        _ => "",
    };

    private static string FormatEnemyStatus(SpacetimeDB.Types.EnemyStatusKind kind, float magnitude) => kind switch
    {
        SpacetimeDB.Types.EnemyStatusKind.DoT => $"{magnitude:0.#} damage/s",
        SpacetimeDB.Types.EnemyStatusKind.Slow => $"slowed {magnitude * 100f:0}%",
        SpacetimeDB.Types.EnemyStatusKind.Vulnerability => $"takes +{magnitude * 100f:0}% damage",
        SpacetimeDB.Types.EnemyStatusKind.ArmorBreak => "defense ignored",
        SpacetimeDB.Types.EnemyStatusKind.Stunned => "stunned",
        _ => "",
    };

    // Ground zones (P3) — the witch doctor's circles and the wizard's spire.
    private static string FormatZoneKind(SpacetimeDB.Types.ZoneKind kind, float magnitude) => kind switch
    {
        SpacetimeDB.Types.ZoneKind.DebuffImmunity => "warding circle: allies inside are debuff-immune",
        SpacetimeDB.Types.ZoneKind.DamageReduction => $"aegis circle: allies inside take {magnitude * 100f:0}% reduced damage",
        SpacetimeDB.Types.ZoneKind.HpFloor => "sanctuary circle: allies inside cannot drop below 1 HP",
        SpacetimeDB.Types.ZoneKind.HealOverTime => $"mending circle: allies inside regenerate {magnitude:0.#} HP/s",
        SpacetimeDB.Types.ZoneKind.BurstHeal => $"surging circle: detonates at expiry, healing allies inside for {magnitude:0.#} HP",
        SpacetimeDB.Types.ZoneKind.DamageBuff => $"empowering circle: allies inside deal {(magnitude - 1f) * 100f:0}% more damage",
        SpacetimeDB.Types.ZoneKind.EnemyDot => $"withering circle: enemies inside bleed for {magnitude:0.#} damage/s",
        SpacetimeDB.Types.ZoneKind.EnemyBurst => $"ruin circle: detonates at expiry for {magnitude:0.#} damage to enemies inside",
        SpacetimeDB.Types.ZoneKind.EnemySlow => $"hindering circle: enemies inside are slowed {magnitude * 100f:0}%",
        SpacetimeDB.Types.ZoneKind.EnemyVulnerability => $"exposing circle: enemies inside take +{magnitude * 100f:0}% damage",
        SpacetimeDB.Types.ZoneKind.DamageShare => "communion circle: hits on anyone inside are split across everyone inside",
        _ => "circle",
    };

    private static string FormatPurge(SpacetimeDB.Types.PurgeKind purge) => purge switch    {
        SpacetimeDB.Types.PurgeKind.NoDamageTaken => "2s without taking a hit",
        SpacetimeDB.Types.PurgeKind.StayClose => "staying near the mark for 5s",
        SpacetimeDB.Types.PurgeKind.DealDamage => "damaging the mark",
        SpacetimeDB.Types.PurgeKind.OnKill => "the mark dies",
        _ => "",
    };

    private static string FormatBuff(ConsumableBuffEffect buff) => buff switch    {
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
    }
}
