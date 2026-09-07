#nullable enable
using Godot;

public partial class BulletData : Resource
{
    /// The server-stamped BulletPatternEvent that spawned this bullet. report_hit
    /// sends it back so the server can attest the hit against a volley the enemy
    /// actually fired — a step def id is not accepted on its own.
    public ulong EventId;
    /// Per-bullet damage, stamped from BulletPatternEvent.Damage (resolved server-side).
    /// Lets damage-aware bullet controllers (SlashRect) compare without the step-def tables.
    public uint Damage;
}
