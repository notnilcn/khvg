using Godot;

public partial class ArrowProjectile : Area2D
{
    [Export] public int Speed { get; set; } = 700;

    public Vector2 Direction    { get; set; }
    public float Damage         { get; set; }
    public float FireRate       { get; set; } = 1.0f;
    public int OwnerPeerId      { get; set; } = 1;

    public override void _Ready()
    {
        Rotation = Direction.Angle();
    }

    public override void _PhysicsProcess(double delta)
    {
        Position += Direction * Speed * (float)delta;
    }

    private void _OnBodyEntered(Node2D body)
    {
        var enemy = _GetEnemyFromBody(body);
        if (enemy == null)
        {
            QueueFree();
            return;
        }

        var shooter = Player.Local;
        if (shooter == null)
        {
            QueueFree();
            return;
        }

        float finalDamage = Damage * shooter.ExtraDamage;
        enemy.ShowFloatingText(Mathf.RoundToInt(finalDamage));
        enemy.ReportHit(finalDamage, FireRate);

        QueueFree();
    }

    private static Enemy _GetEnemyFromBody(Node2D body)
    {
        if (body is Enemy e) return e;
        if (body.GetParent() is Enemy pe) return pe;
        return null;
    }
}
