#nullable enable
using Godot;

public partial class PlayerStatsSidebar : Control
{
    [Export] public Label LevelLabel { get; set; } = null!;
    [Export] public Label HpLabel { get; set; } = null!;
    [Export] public Label StrengthLabel { get; set; } = null!;
    [Export] public Label WisdomLabel { get; set; } = null!;
    [Export] public Label DexterityLabel { get; set; } = null!;
    [Export] public Label DefenseLabel { get; set; } = null!;
    [Export] public Label VitalityLabel { get; set; } = null!;
    [Export] public Label SpeedLabel { get; set; } = null!;

    public override void _Ready()
    {
        if (this.GetAncestor<LocalPlayer>() is not { } player) return;
        player.StatsChanged += HandleStatsChanged;
        HandleStatsChanged();
    }

    private void HandleStatsChanged()
    {
        if (this.GetAncestor<LocalPlayer>() is not { } player) return;
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
        if (this.GetAncestor<LocalPlayer>() is { } player)
            player.StatsChanged -= HandleStatsChanged;
    }
}
