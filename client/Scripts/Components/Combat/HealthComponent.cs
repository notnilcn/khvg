#nullable enable
using Godot;

/// <summary>
/// comedot's HealthComponent, adapted for server authority: it owns the entity's Health
/// Stat as a read-only *mirror* of the server's hp/max_hp and emits signals when the server
/// value changes, so UI/visual components can react. It deliberately has no Damage()/Heal()
/// methods — damage is computed by the server; new values arrive via SetFromServer().
/// </summary>
public partial class HealthComponent : Component
{
    [Signal] public delegate void HealthDidDecreaseEventHandler(int difference);
    [Signal] public delegate void HealthDidIncreaseEventHandler(int difference);
    [Signal] public delegate void HealthDidZeroEventHandler();

    /// Shared with the entity's StatsComponent (comedot's shared-Stat pattern).
    public Stat Health { get; } = new() { MinValue = 0 };

    public int Hp => Health.Value;
    public int MaxHp => Health.MaxValue;

    public void SetFromServer(uint hp, uint maxHp)
    {
        int old = Health.Value;
        Health.MaxValue = (int)maxHp;
        Health.SetValue((int)hp);
        int difference = Health.Value - old;
        if (difference < 0) EmitSignal(SignalName.HealthDidDecrease, -difference);
        else if (difference > 0) EmitSignal(SignalName.HealthDidIncrease, difference);
        if (Health.Value == 0 && old != 0) EmitSignal(SignalName.HealthDidZero);
    }
}
