#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class BulletData : Resource
{
    public SequenceStepRef SourceStep = null!;
    /// The enemy whose BulletPatternEvent spawned this bullet (stamped per spawn batch).
    /// report_hit sends it back so the server can validate the hit's source — a
    /// SequenceStepRef identifies the step def, not the enemy.
    public ulong EnemyId;
    /// Per-bullet damage, stamped from BulletPatternEvent.Damage (resolved server-side).
    /// Lets damage-aware bullet controllers (SlashRect) compare without the step-def tables.
    public uint Damage;
}
