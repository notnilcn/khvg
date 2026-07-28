#nullable enable
using Godot;

/// <summary>
/// comedot's Stat, adapted: a clamped integer resource that emits ValueChanged when mutated.
/// Data only — components reference Stats, never the other way around (comedot's resource
/// rule). Values here are mirrors of server rows pushed in by the owning component's
/// SetFromServer, never authoritative client state.
/// </summary>
[GlobalClass]
public partial class Stat : Resource
{
    [Signal] public delegate void ValueChangedEventHandler();

    [Export] public int MinValue { get; set; } = int.MinValue;
    [Export] public int MaxValue { get; set; } = int.MaxValue;

    private int _value;
    [Export]
    public int Value
    {
        get => _value;
        set => SetValue(value);
    }

    public int PreviousValue { get; private set; }

    public float Percent
    {
        get
        {
            double range = (double)MaxValue - MinValue;
            return range > 0 ? (float)((_value - (double)MinValue) / range) : 0f;
        }
    }

    /// Clamps to [MinValue, MaxValue]; emits ValueChanged only on an actual change.
    public void SetValue(int newValue)
    {
        int clamped = Mathf.Clamp(newValue, MinValue, MaxValue);
        if (clamped == _value) return;
        PreviousValue = _value;
        _value = clamped;
        EmitSignal(SignalName.ValueChanged);
    }
}
