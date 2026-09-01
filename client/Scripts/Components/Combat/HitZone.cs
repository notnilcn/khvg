#nullable enable
using Godot;

/// <summary>
/// Self-contained melee/projectile hit zone (Scenes/Components/hit_zone.tscn). Spawned by
/// CombatComponent along a bullet's path; after the given delay it reports every enemy
/// hurtbox (DamageReceivingComponent) standing inside it and frees itself via its in-scene
/// LifetimeTimer. An AreaComponent hitbox with a fuse — damage itself is computed server-side:
/// it *reports* the hits and the victim's HealthComponent mirror updates when the row comes back.
/// </summary>
public partial class HitZone : AreaComponent
{
    [Signal] public delegate void DidHitReceiverEventHandler(DamageReceivingComponent receiver);

    private CircleShape2D shape = null!;
    private Timer lifetimeTimer = null!;

    /// Elevation level this zone was fired from (stamped by CombatComponent.SpawnZone);
    /// ReportHits only connects with receivers within the bullet hit window around it.
    public int Elevation { get; set; }

    public override void _Ready()
    {
        base._Ready();
        shape = (CircleShape2D)GetNode<CollisionShape2D>("CollisionShape2D").Shape;
        lifetimeTimer = GetNode<Timer>("LifetimeTimer");
        lifetimeTimer.Timeout += OnLifetimeTimeout;
    }

    /// <summary>Call after the zone is inside the tree. Sets the radius and starts the fuse.</summary>
    public void Launch(float radius, float delay)
    {
        shape.Radius = radius;
        lifetimeTimer.Start(delay);
    }

    private void OnLifetimeTimeout()
    {
        ReportHits();
        QueueFree();
    }

    /// Reports every DamageReceivingComponent currently overlapping this hitbox.
    /// Returns the number of receivers hit.
    public int ReportHits()
    {
        int hits = 0;
        var elevationMap = GameManager.ElevationMap;
        foreach (var area in GetOverlappingAreas())
        {
            if (area is not DamageReceivingComponent receiver || !CanDamage(receiver)) continue;
            // Elevation window rule: player attacks connect with receivers at most
            // BULLET_ELEVATION_DROP below or BULLET_ELEVATION_CLIMB above the level
            // they were fired from (stamped by CombatComponent.SpawnZone).
            if (elevationMap != null)
            {
                int receiverElevation = elevationMap.GetElevationAt(receiver.GlobalPosition);
                if (receiverElevation < Elevation - ServerConstants.BulletElevationDrop ||
                    receiverElevation > Elevation + ServerConstants.BulletElevationClimb)
                    continue;
            }
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
