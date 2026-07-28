#nullable enable
using Godot;

/// <summary>
/// comedot's DamageReceivingComponent, adapted for server authority: the victim-side
/// hurtbox (Area2D). Passive — DamageComponents detect it and report the hit; it emits
/// DidReceiveDamage so observer (visual/audio) components can react. It never applies
/// damage locally: the health change arrives as a server row update into HealthComponent.
/// The scene carries no collision shape — each entity adds its own as an instance child.
/// </summary>
public partial class DamageReceivingComponent : AreaComponent
{
    [Signal] public delegate void DidReceiveDamageEventHandler(DamageComponent attacker);

    protected override System.Type[] GetRequiredComponents() => new[] { typeof(FactionComponent) };

    public void ProcessHit(DamageComponent attacker) => EmitSignal(SignalName.DidReceiveDamage, attacker);

    /// Enemy-bullet path: BlastBullets2D reports overlaps through BulletManager rather than
    /// through a DamageComponent, so bullets reach the same reducer via this method.
    public void ProcessBulletHit(BulletData data) =>
        GameManager.Conn?.Reducers.ReportHit(data.SourceStep);
}
