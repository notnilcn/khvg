#nullable enable
using Godot;
using System;

[Flags]
public enum Factions
{
    Neutral = 1 << 0,
    Players = 1 << 1,
    Enemies = 1 << 2,
}

/// <summary>
/// comedot's FactionComponent: a pure data marker used to decide whether two entities are
/// opposed (and may therefore damage each other). Set statically per scene — faction is
/// implicit in entity kind on the server, so there is nothing to sync.
/// </summary>
public partial class FactionComponent : Component
{
    [Export] public Factions Factions { get; set; } = Factions.Neutral;

    /// Opposition = no shared faction flag (comedot's rule: entities without a common
    /// faction may damage each other, so a missing component falls back to Neutral and
    /// is opposed to everything).
    public static bool CheckOpposition(Factions a, Factions b) => (a & b) == 0;
}
