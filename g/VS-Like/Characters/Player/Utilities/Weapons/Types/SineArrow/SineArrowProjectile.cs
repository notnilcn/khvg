using Godot;

public partial class SineArrowProjectile : Area2D
{
    private const float Lifetime = 4.0f;

    [Export] public float Speed     { get; set; } = 500.0f;
    [Export] public float Amplitude { get; set; } = 30.0f;
    [Export] public float Frequency { get; set; } = 2.0f;

    public Vector2 Direction    { get; set; }
    public float Damage         { get; set; }
    public float FireRate       { get; set; } = 1.0f;
    public int OwnerPeerId      { get; set; } = 1;

    private Vector2 _spawnPos;
    private Vector2 _perp;
    private float _elapsed = 0.0f;

    public override void _Ready()
    {
        _spawnPos = GlobalPosition;
        _perp     = new Vector2(-Direction.Y, Direction.X);
        Rotation  = Direction.Angle();
    }

    public override void _PhysicsProcess(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed > Lifetime)
        {
            QueueFree();
            return;
        }

        var prevPos = GlobalPosition;
        GlobalPosition = _spawnPos
            + Direction * Speed * _elapsed
            + _perp     * Amplitude * Mathf.Sin(Frequency * Mathf.Tau * _elapsed);

        var travel = GlobalPosition - prevPos;
        if (travel.LengthSquared() > 0.0f)
            Rotation = travel.Angle();
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
