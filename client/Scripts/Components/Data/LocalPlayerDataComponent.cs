#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;

/// <summary>
/// Feeds the local player's LocalPlayerData/LocalPlayerStats/LocalPlayerStatAllocation
/// rows into the sibling HealthComponent/StatsComponent mirrors, which own the actual
/// values (LocalPlayer's Hp/MaxHp/Strength… pass-throughs read from them, keeping UI
/// readers stable). Rows arrive via the child binders declared in local_player.tscn
/// (signals wired in the editor; ReplayExistingRows replaces the old Iter() loops in
/// LocalPlayer._Ready). Defense is modifier-only (gear/buffs) and lives on the data row,
/// same as max_hp.
/// </summary>
public partial class LocalPlayerDataComponent : Component
{
    public uint Level { get; private set; }
    public int Defense { get; private set; }
    public uint UnspentPoints { get; private set; }

    private TableBinderComponent dataBinder = null!;
    private TableBinderComponent statsBinder = null!;
    private TableBinderComponent allocationBinder = null!;

    private HealthComponent? HealthComponent => GetSibling<HealthComponent>();
    private StatsComponent? StatsComponent => GetSibling<StatsComponent>();

    public override void _Ready()
    {
        base._Ready();
        dataBinder = GetNode<TableBinderComponent>("LocalPlayerDataBinder");
        statsBinder = GetNode<TableBinderComponent>("LocalPlayerStatsBinder");
        allocationBinder = GetNode<TableBinderComponent>("LocalPlayerStatAllocationBinder");
    }

    protected override Type[] GetRequiredComponents() => [typeof(HealthComponent), typeof(StatsComponent)];

    protected override void OnEntityReady()
    {
        // comedot's shared-Stat pattern: the Stats component lists the same Stat instance
        // the Health component owns, so all observers see one hp value.
        if (HealthComponent != null && StatsComponent != null)
            StatsComponent.RegisterStat(StatKind.Hp, HealthComponent.Health);
    }

    // --- TableBinderComponent signal handlers (wired in local_player.tscn) ---

    private void OnDataRow()
    {
        var data = (PlayerData)dataBinder.LastRow!;
        Level = data.Level;
        Defense = data.Defense;
        HealthComponent?.SetFromServer(data.Hp, data.MaxHp);
        (Entity as LocalPlayer)?.RaiseStatsChanged();
    }

    private void OnStatsRow()
    {
        var stats = (PlayerStats)statsBinder.LastRow!;
        StatsComponent?.SetFromServer(StatKind.Strength, stats.Strength);
        StatsComponent?.SetFromServer(StatKind.Wisdom, stats.Wisdom);
        StatsComponent?.SetFromServer(StatKind.Dexterity, stats.Dexterity);
        StatsComponent?.SetFromServer(StatKind.DamageDealer, stats.DamageDealer);
        StatsComponent?.SetFromServer(StatKind.Supporter, stats.Supporter);
        StatsComponent?.SetFromServer(StatKind.Artisan, stats.Artisan);
        (Entity as LocalPlayer)?.RaiseStatsChanged();
    }

    private void OnAllocationRow()
    {
        var allocation = (PlayerStatAllocation)allocationBinder.LastRow!;
        UnspentPoints = allocation.UnspentPoints;
        (Entity as LocalPlayer)?.RaiseStatsChanged();
    }
}
