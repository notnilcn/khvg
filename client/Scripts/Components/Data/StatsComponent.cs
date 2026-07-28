#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// comedot's StatsComponent: the entity's set of Stats, keyed by StatKind. Values are
/// mirrors of server rows (PlayerStats/PlayerData for the local player, EnemyTemplate +
/// NearbyEnemies for enemies) pushed in via SetFromServer by the entity root; observers
/// bind to StatChanged or to an individual Stat's ValueChanged.
/// </summary>
public partial class StatsComponent : Component
{
    [Signal] public delegate void StatChangedEventHandler(int kind);

    private readonly Dictionary<StatKind, Stat> stats = new();

    /// comedot's shared-Stat pattern: the same Stat instance can be listed here while being
    /// owned by another component (e.g. HealthComponent's Health), so all observers see one value.
    public void RegisterStat(StatKind kind, Stat stat) => stats[kind] = stat;

    public Stat GetStat(StatKind kind)
    {
        if (!stats.TryGetValue(kind, out var stat))
        {
            stat = new Stat();
            stats[kind] = stat;
        }
        return stat;
    }

    public int GetValue(StatKind kind) => GetStat(kind).Value;

    public void SetFromServer(StatKind kind, int value)
    {
        var stat = GetStat(kind);
        if (stat.Value == value) return;
        stat.SetValue(value);
        EmitSignal(SignalName.StatChanged, (int)kind);
    }
}
