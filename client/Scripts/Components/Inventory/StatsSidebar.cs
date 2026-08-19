#nullable enable
using Godot;

/// <summary>
/// The player-stats sidebar (level/hp/attributes) in inventory_panel.tscn. Mirrors the
/// owning LocalPlayer's pass-through stat properties and refreshes on StatsChanged.
/// The six allocatable stats (str/wis/dex/dps/sup/art) each get a "+" Button, visible
/// only while the player has unspent skill points, calling the AllocateStat reducer.
/// Defense is modifier-only (gear/buffs) — shown, but not allocatable.
/// Reaches the owning LocalPlayer through the ancestor walk (across the instanced
/// inventory_panel.tscn boundary).
/// </summary>
public partial class StatsSidebar : Control
{
    [Export] public Label LevelLabel { get; set; } = null!;
    [Export] public Label HpLabel { get; set; } = null!;
    [Export] public Label StrengthLabel { get; set; } = null!;
    [Export] public Label WisdomLabel { get; set; } = null!;
    [Export] public Label DexterityLabel { get; set; } = null!;
    [Export] public Label DamageDealerLabel { get; set; } = null!;
    [Export] public Label SupporterLabel { get; set; } = null!;
    [Export] public Label ArtisanLabel { get; set; } = null!;
    [Export] public Label DefenseLabel { get; set; } = null!;
    [Export] public Label UnspentLabel { get; set; } = null!;
    // One "+" button per allocatable stat, in Allocatable order (wired in the .tscn).
    [Export] public Godot.Collections.Array<Button> AllocateButtons { get; set; } = [];

    // The generated bindings' StatKind, in AllocateButtons order.
    private static readonly SpacetimeDB.Types.StatKind[] Allocatable =
    [
        SpacetimeDB.Types.StatKind.Strength,
        SpacetimeDB.Types.StatKind.Wisdom,
        SpacetimeDB.Types.StatKind.Dexterity,
        SpacetimeDB.Types.StatKind.DamageDealer,
        SpacetimeDB.Types.StatKind.Supporter,
        SpacetimeDB.Types.StatKind.Artisan,
    ];

    private LocalPlayer? player;

    public override void _Ready()
    {
        player = this.GetAncestor<LocalPlayer>();
        if (player == null) return;
        player.StatsChanged += HandleStatsChanged;
        for (int i = 0; i < AllocateButtons.Count && i < Allocatable.Length; i++)
        {
            var stat = Allocatable[i];
            AllocateButtons[i].Pressed += () => GameManager.Conn?.Reducers.AllocateStat(stat, 1);
        }
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
        DamageDealerLabel.Text = $"DPS: {player.DamageDealer}";
        SupporterLabel.Text = $"SUP: {player.Supporter}";
        ArtisanLabel.Text = $"ART: {player.Artisan}";
        DefenseLabel.Text = $"DEF: {player.Defense}";
        UnspentLabel.Text = $"Points: {player.UnspentPoints}";
        foreach (var button in AllocateButtons)
            button.Visible = player.UnspentPoints > 0;
    }

    public override void _ExitTree()
    {
        if (player != null)
            player.StatsChanged -= HandleStatsChanged;
    }
}
