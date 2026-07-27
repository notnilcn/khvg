#nullable enable
using Godot;

/// <summary>
/// Self-contained melee/projectile hit zone (Scenes/Components/hit_zone.tscn). Spawned by
/// LocalPlayerCombat along a bullet's path; after the given delay it reports every enemy
/// standing inside it and frees itself via its in-scene LifetimeTimer.
/// </summary>
public partial class HitZone : Area2D
{
    private CircleShape2D shape = null!;
    private Timer lifetimeTimer = null!;

    public override void _Ready()
    {
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
        var conn = GameManager.Instance.Conn;
        if (conn != null)
        {
            foreach (var body in GetOverlappingBodies())
            {
                if (body is Enemy enemy)
                    conn.Reducers.ReportEnemyHit(enemy.EnemyId);
            }
        }
        QueueFree();
    }
}
