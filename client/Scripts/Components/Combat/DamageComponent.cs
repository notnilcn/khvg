#nullable enable
using Godot;

/// <summary>
/// comedot's DamageComponent, adapted for server authority: the attacker-side hitbox
/// (Area2D). It finds DamageReceivingComponents in contact and *reports* the hits to the
/// server — it never computes or applies damage locally. The server recomputes damage and
/// the victim's HealthComponent mirror updates when the row comes back.
/// </summary>
public partial class DamageComponent : AreaComponent
{
    [Signal] public delegate void DidHitReceiverEventHandler(DamageReceivingComponent receiver);

    /// Reports every DamageReceivingComponent currently overlapping this hitbox.
    /// Returns the number of receivers hit.
    public int ReportHits()
    {
        int hits = 0;
        foreach (var area in GetOverlappingAreas())
        {
            if (area is not DamageReceivingComponent receiver || !CanDamage(receiver)) continue;
            receiver.ProcessHit(this);
            EmitSignal(SignalName.DidHitReceiver, receiver);
            // ReportEnemyHit needs the victim's id — the server is authoritative for damage.
            if (receiver.Entity is Enemy enemy)
                GameManager.Conn?.Reducers.ReportEnemyHit(enemy.EnemyId);
            hits++;
        }
        return hits;
    }

    /// Faction opposition filter (comedot's rule: a missing FactionComponent on either side
    /// falls back to Neutral, which is opposed to everything).
    public bool CanDamage(DamageReceivingComponent receiver)
    {
        var mine = Entity?.GetComponent<FactionComponent>()?.Factions ?? Factions.Neutral;
        var theirs = receiver.Entity?.GetComponent<FactionComponent>()?.Factions ?? Factions.Neutral;
        return FactionComponent.CheckOpposition(mine, theirs);
    }
}
