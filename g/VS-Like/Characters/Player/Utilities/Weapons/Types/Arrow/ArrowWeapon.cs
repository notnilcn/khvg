using Godot;

public partial class ArrowWeapon : Weapon
{
    private static readonly PackedScene ProjectileScene =
        GD.Load<PackedScene>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/arrow_projectile.tscn");

    private int _projectileCount = 1;
    private float _spreadAngle   = 15.0f;

    public override void _Ready()
    {
        base._Ready();
        Fire    += _OnFire;
        Upgraded += _OnUpgraded;
    }

    private Vector2 _GetProjectileDirection()
    {
        var localPlayer = Global.Instance.GetLocalPlayer();
        if (localPlayer == null)
            return Vector2.Right;

        var enemies = localPlayer.GetNode<Area2D>("EnemyDetection").GetOverlappingBodies();
        if (enemies.Count > 0)
            return GlobalPosition.DirectionTo(((Node2D)enemies[0]).GlobalPosition);
        return Vector2.Right;
    }

    private void _OnFire()
    {
        var localPlayer = Global.Instance.GetLocalPlayer();
        if (localPlayer == null)
            return;
        if (!_BelongsToLocalPlayer(localPlayer))
            return;

        var baseDirection = _GetProjectileDirection();
        float baseAngle   = baseDirection.Angle();
        float startAngle  = baseAngle - (_projectileCount - 1) * _spreadAngle * 0.5f * Mathf.DegToRad(1);

        for (int i = 0; i < _projectileCount; i++)
        {
            var projectile = ProjectileScene.Instantiate<ArrowProjectile>();
            projectile.Damage       = Damage;
            projectile.FireRate     = Resource.FireRate;
            projectile.OwnerPeerId  = Multiplayer.GetUniqueId();

            float projectileAngle = startAngle + i * _spreadAngle * Mathf.DegToRad(1);
            projectile.Direction  = new Vector2(Mathf.Cos(projectileAngle), Mathf.Sin(projectileAngle)).Normalized();
            projectile.GlobalPosition = GlobalPosition;

            GetTree().CurrentScene.AddChild(projectile);
        }
    }

    private bool _BelongsToLocalPlayer(Player localPlayer)
    {
        Node node = this;
        while (node != null)
        {
            if (node == localPlayer) return true;
            node = node.GetParent();
        }
        return false;
    }

    private void _OnUpgraded()
    {
        _projectileCount += 2;
    }
}
