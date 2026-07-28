#nullable enable
using Godot;

/// <summary>
/// The player-stats sidebar (level/hp/attributes) in inventory_panel.tscn. Mirrors the
/// owning LocalPlayer's pass-through stat properties and refreshes on StatsChanged.
/// Entity IS the LocalPlayer (registration walks ancestors across the instanced
/// inventory_panel.tscn boundary).
/// </summary>
public partial class StatsSidebarComponent : ControlComponent
{
    [Export] public Label LevelLabel { get; set; } = null!;
    [Export] public Label HpLabel { get; set; } = null!;
    [Export] public Label StrengthLabel { get; set; } = null!;
    [Export] public Label WisdomLabel { get; set; } = null!;
    [Export] public Label DexterityLabel { get; set; } = null!;
    [Export] public Label DefenseLabel { get; set; } = null!;
    [Export] public Label VitalityLabel { get; set; } = null!;
    [Export] public Label SpeedLabel { get; set; } = null!;

    private LocalPlayer? player;

    protected override void OnRegistered()
    {
        player = Entity as LocalPlayer;
        if (player == null) return;
        player.StatsChanged += HandleStatsChanged;
        HandleStatsChanged();
    }

    private void HandleStatsChanged()
    {
        if (player == null) return;
        LevelLabel.Text = $"Lv. {player.Level}";
        HpLabel.Text = $"HP: {player.Hp}/{player.MaxHp}";
        StrengthLabel.Text = $"STR: {player.Strength}";
        WisdomLabel.Text = $"WIS: {player.Wisdom}";
        DexterityLabel.Text = $"DEX: {player.Dexterity}";
        DefenseLabel.Text = $"DEF: {player.Defense}";
        VitalityLabel.Text = $"VIT: {player.Vitality}";
        SpeedLabel.Text = $"SPD: {player.Speed}";
    }

    public override void _ExitTree()
    {
        if (player != null)
            player.StatsChanged -= HandleStatsChanged;
        base._ExitTree();
    }
}
