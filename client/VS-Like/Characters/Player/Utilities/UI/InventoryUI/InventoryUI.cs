using Godot;
using System.Collections.Generic;

public partial class InventoryUI : Control
{
    private static readonly Color ColorPanel   = new(0.18f, 0.10f, 0.44f, 0.97f);
    private static readonly Color ColorSlot    = new(0.10f, 0.06f, 0.26f, 0.90f);
    private static readonly Color ColorSlotHvr = new(0.28f, 0.18f, 0.58f, 1.00f);
    private static readonly Color ColorBorder  = new(0.45f, 0.32f, 0.80f, 0.90f);
    private static readonly Color ColorLocked  = new(0.08f, 0.05f, 0.18f, 0.70f);
    private static readonly Vector2 SlotSize   = new(52, 52);

    private Player _player;
    private readonly Dictionary<string, Button> _equipSlots = new();
    private readonly List<Button> _invSlots = new();
    private readonly List<Button> _conSlots = new();
    private readonly Dictionary<string, Label> _statLabels = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.WhenPaused;
        Visible     = false;
        MouseFilter = MouseFilterEnum.Stop;
        _BuildUi();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.I)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void _BuildUi()
    {
        var overlay = new ColorRect();
        overlay.Color = new Color(0, 0, 0, 0.55f);
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.MouseFilter = MouseFilterEnum.Stop;
        AddChild(overlay);
        overlay.GuiInput += (ev) =>
        {
            if (ev is InputEventMouseButton mb && mb.Pressed)
                Close();
        };

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left",   160);
        margin.AddThemeConstantOverride("margin_top",     30);
        margin.AddThemeConstantOverride("margin_right",  160);
        margin.AddThemeConstantOverride("margin_bottom",  30);
        AddChild(margin);

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = ColorPanel;
        panelStyle.SetCornerRadiusAll(8);
        panelStyle.SetBorderWidthAll(2);
        panelStyle.BorderColor = ColorBorder;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        panel.MouseFilter = MouseFilterEnum.Stop;
        margin.AddChild(panel);

        var rootMargin = new MarginContainer();
        rootMargin.AddThemeConstantOverride("margin_left",   16);
        rootMargin.AddThemeConstantOverride("margin_top",    12);
        rootMargin.AddThemeConstantOverride("margin_right",  16);
        rootMargin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(rootMargin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        rootMargin.AddChild(vbox);

        var title = new Label();
        title.Text = "INVENTORY     [I] close";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());
        _BuildEquipmentSection(vbox);
        vbox.AddChild(new HSeparator());
        _BuildInventorySection(vbox);
        vbox.AddChild(new HSeparator());
        _BuildStatsSection(vbox);
    }

    private void _BuildEquipmentSection(VBoxContainer parent)
    {
        var lbl = new Label(); lbl.Text = "EQUIPMENT"; parent.AddChild(lbl);
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);
        parent.AddChild(hbox);

        var slots = new[] {
            ("weapon",  "WEAPON"),  ("ability", "ABILITY"), ("armor",   "ARMOR"),
            ("acc_0",   "ACCESSORY"), ("acc_1", "ACCESSORY"), ("acc_2", "ACCESSORY"),
        };

        foreach (var (key, tip) in slots)
        {
            var btn = _MakeSlotButton(tip);
            hbox.AddChild(btn);
            _equipSlots[key] = btn;
            var capturedKey = key;
            btn.Pressed += () => _OnEquipSlotPressed(capturedKey);
        }
    }

    private void _BuildInventorySection(VBoxContainer parent)
    {
        var lbl = new Label(); lbl.Text = "INVENTORY"; parent.AddChild(lbl);
        var grid = new GridContainer();
        grid.Columns = 6;
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        parent.AddChild(grid);

        for (int i = 0; i < 12; i++)
        {
            var btn = _MakeSlotButton("");
            grid.AddChild(btn);
            _invSlots.Add(btn);
            var idx = i;
            btn.Pressed += () => _OnInvSlotPressed(idx);
        }

        var conLbl = new Label(); conLbl.Text = "CONSUMABLES"; parent.AddChild(conLbl);
        var conHbox = new HBoxContainer();
        conHbox.AddThemeConstantOverride("separation", 6);
        parent.AddChild(conHbox);

        for (int i = 0; i < 8; i++)
        {
            var btn = _MakeSlotButton("");
            conHbox.AddChild(btn);
            _conSlots.Add(btn);
        }
    }

    private void _BuildStatsSection(VBoxContainer parent)
    {
        var lbl = new Label(); lbl.Text = "STATS"; parent.AddChild(lbl);
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 24);
        parent.AddChild(hbox);

        foreach (var stat in new[] { "STR", "WIS", "DEX", "DEF", "VIT" })
        {
            var vb = new VBoxContainer();
            vb.AddThemeConstantOverride("separation", 2);
            hbox.AddChild(vb);

            var nameLbl = new Label(); nameLbl.Text = stat;
            nameLbl.HorizontalAlignment = HorizontalAlignment.Center;
            vb.AddChild(nameLbl);

            var valLbl = new Label(); valLbl.Text = "0";
            valLbl.HorizontalAlignment = HorizontalAlignment.Center;
            vb.AddChild(valLbl);

            _statLabels[stat] = valLbl;
        }
    }

    private Button _MakeSlotButton(string placeholder)
    {
        var btn = new Button();
        btn.CustomMinimumSize      = SlotSize;
        btn.ClipContents           = true;
        btn.ExpandIcon             = true;
        btn.IconAlignment          = HorizontalAlignment.Center;
        btn.VerticalIconAlignment  = VerticalAlignment.Center;
        btn.Text = placeholder;

        var sn = new StyleBoxFlat();
        sn.BgColor = ColorSlot; sn.SetBorderWidthAll(2); sn.BorderColor = ColorBorder; sn.SetCornerRadiusAll(4);
        btn.AddThemeStyleboxOverride("normal", sn);

        var sh = new StyleBoxFlat();
        sh.BgColor = ColorSlotHvr; sh.SetBorderWidthAll(2); sh.BorderColor = new Color(0.70f, 0.55f, 1.0f, 1.0f); sh.SetCornerRadiusAll(4);
        btn.AddThemeStyleboxOverride("hover", sh);

        var sp = new StyleBoxFlat();
        sp.BgColor = new Color(0.35f, 0.22f, 0.70f, 1.0f); sp.SetBorderWidthAll(2); sp.BorderColor = new Color(0.70f, 0.55f, 1.0f, 1.0f); sp.SetCornerRadiusAll(4);
        btn.AddThemeStyleboxOverride("pressed", sp);

        return btn;
    }

    public void Open(Player player)
    {
        _player = player;
        Refresh();
        Visible = true;
        GetTree().Paused = true;
    }

    public void Close()
    {
        Visible = false;
        GetTree().Paused = false;
    }

    public void Refresh()
    {
        if (_player == null) return;
        _RefreshEquipment();
        _RefreshInventory();
        _RefreshStats();
    }

    private void _RefreshEquipment()
    {
        _SetSlot(_equipSlots.GetValueOrDefault("weapon"),  _WeaponRes(_player.EquippedWeapon), "WPN");
        _SetSlot(_equipSlots.GetValueOrDefault("ability"), _WeaponRes(_player.EquippedAbility), "ABL");
        _SetSlot(_equipSlots.GetValueOrDefault("armor"),   null, "ARM");

        int accCount = _player.GetAccessorySlotCount();
        for (int i = 0; i < 3; i++)
        {
            var slot = _equipSlots.GetValueOrDefault($"acc_{i}");
            if (slot == null) continue;
            if (i < accCount)
            {
                var res = (i < _player.EquippedAccessories.Count) ? _WeaponRes(_player.EquippedAccessories[i]) : null;
                _SetSlot(slot, res, "ACC");
                slot.Disabled = false;
            }
            else
            {
                slot.Icon         = null;
                slot.Text         = "???";
                slot.Disabled     = true;
                slot.TooltipText  = $"Requires {500 * i} Wisdom";
            }
        }
    }

    private void _RefreshInventory()
    {
        int maxInv = _player.InventoryMax;
        int maxCon = _player.ConsumablesMax;

        for (int i = 0; i < _invSlots.Count; i++)
        {
            var slot = _invSlots[i];
            if (i >= maxInv)
            {
                slot.Disabled = true; slot.Icon = null; slot.Text = "";
                _TintLocked(slot); continue;
            }
            slot.Disabled = false;
            _RestoreSlotStyle(slot);
            _SetSlot(slot, i < _player.Inventory.Count ? _player.Inventory[i] : null, "");
        }

        for (int i = 0; i < _conSlots.Count; i++)
        {
            var slot = _conSlots[i];
            if (i >= maxCon)
            {
                slot.Disabled = true; slot.Icon = null; slot.Text = "";
                _TintLocked(slot); continue;
            }
            slot.Disabled = false;
            _RestoreSlotStyle(slot);
            _SetSlot(slot, i < _player.Consumables.Count ? _player.Consumables[i] : null, "");
        }
    }

    private void _RefreshStats()
    {
        var values = new Dictionary<string, int> {
            { "STR", _player.Strength }, { "WIS", _player.Wisdom },
            { "DEX", _player.Dexterity }, { "DEF", _player.Defense }, { "VIT", _player.Vitality },
        };
        foreach (var kvp in values)
            if (_statLabels.TryGetValue(kvp.Key, out var lbl))
                lbl.Text = kvp.Value.ToString();
    }

    private static void _SetSlot(Button slot, ResItem res, string placeholder)
    {
        if (slot == null) return;
        if (res != null && res.Sprite != null)
        {
            slot.Icon = res.Sprite; slot.Text = "";
            slot.TooltipText = res.ItemName + "\n" + res.ItemDescription;
        }
        else
        {
            slot.Icon = null; slot.Text = placeholder; slot.TooltipText = "";
        }
    }

    private void _TintLocked(Button slot)
    {
        var s = new StyleBoxFlat();
        s.BgColor = ColorLocked; s.SetBorderWidthAll(2); s.BorderColor = new Color(0.2f, 0.15f, 0.35f, 0.5f); s.SetCornerRadiusAll(4);
        slot.AddThemeStyleboxOverride("normal", s);
        slot.AddThemeStyleboxOverride("hover",  s);
    }

    private void _RestoreSlotStyle(Button slot)
    {
        var sn = new StyleBoxFlat();
        sn.BgColor = ColorSlot; sn.SetBorderWidthAll(2); sn.BorderColor = ColorBorder; sn.SetCornerRadiusAll(4);
        slot.AddThemeStyleboxOverride("normal", sn);

        var sh = new StyleBoxFlat();
        sh.BgColor = ColorSlotHvr; sh.SetBorderWidthAll(2); sh.BorderColor = new Color(0.70f, 0.55f, 1.0f, 1.0f); sh.SetCornerRadiusAll(4);
        slot.AddThemeStyleboxOverride("hover", sh);
    }

    private static ResItem _WeaponRes(Weapon weapon) => weapon?.Resource;

    private void _OnEquipSlotPressed(string key)
    {
        if (_player == null) return;

        if (key == "weapon" && _player.EquippedWeapon != null)
        {
            _player._UnequipToInventory(_player.EquippedWeapon);
            _player.EquippedWeapon = null;
            _player.GetNode<WeaponContainerUI>("%WeaponsContainerUI").UpdateEquipment(_player);
        }
        else if (key == "ability" && _player.EquippedAbility != null)
        {
            _player._UnequipToInventory(_player.EquippedAbility);
            _player.EquippedAbility = null;
            _player.GetNode<WeaponContainerUI>("%WeaponsContainerUI").UpdateEquipment(_player);
        }
        else if (key.StartsWith("acc_"))
        {
            int idx = int.Parse(key.Substring(4));
            if (idx < _player.EquippedAccessories.Count)
            {
                var acc = _player.EquippedAccessories[idx];
                _player._UnequipToInventory(acc);
                _player.EquippedAccessories.RemoveAt(idx);
                _player.GetNode<WeaponContainerUI>("%WeaponsContainerUI").UpdateEquipment(_player);
            }
        }

        Refresh();
    }

    private void _OnInvSlotPressed(int idx)
    {
        if (_player == null) return;
        if (idx >= _player.Inventory.Count) return;

        var res = _player.Inventory[idx];
        _player.Inventory.RemoveAt(idx);

        if (!_player.EquipItem(res))
            _player.Inventory.Insert(idx, res);

        Refresh();
    }
}
