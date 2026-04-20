using Godot;

public partial class BibleWeapon : Weapon
{
    [Export] public float RotationSpeed { get; set; } = 5.0f;

    private Timer _durationTimer;
    private CollisionShape2D _firstCollShape;
    private CollisionShape2D _secondCollShape;

    private bool _hasSecondBible = false;
    private bool _isFiring       = false;

    public override void _Ready()
    {
        base._Ready();
        _durationTimer   = GetNode<Timer>("DurationTimer");
        _firstCollShape  = GetNode<CollisionShape2D>("%FirstCollShape");
        _secondCollShape = GetNode<CollisionShape2D>("%SecondCollShape");
        Fire    += _OnFire;
        Upgraded += _OnUpgraded;
    }

    public override void _Process(double delta)
    {
        if (_isFiring)
            RotationDegrees += RotationSpeed;
    }

    private void _OnFire()
    {
        Visible = true;
        _firstCollShape.Disabled = false;
        if (_hasSecondBible)
            _secondCollShape.Disabled = false;
        _isFiring = true;
        _durationTimer.Start();
    }

    private void _OnDurationTimerTimeout()
    {
        Visible = false;
        _firstCollShape.Disabled = true;
        if (_hasSecondBible)
            _secondCollShape.Disabled = true;
        _isFiring = false;
    }

    private void _OnAreaBodyEntered(Node2D body)
    {
        var enemy = _GetEnemyFromBody(body);
        if (enemy == null) return;

        var localPlayer = Global.Instance.GetLocalPlayer();
        if (localPlayer == null) return;
        if (!_BelongsToLocalPlayer(localPlayer)) return;

        float finalDamage = Damage * localPlayer.ExtraDamage;
        enemy.Rpc(nameof(Enemy.ShowFloatingText), Mathf.RoundToInt(finalDamage));

        if (Multiplayer.IsServer())
            enemy.ReportHit(finalDamage, Resource.FireRate);
        else
            enemy.RpcId(1, nameof(Enemy.ReportHit), finalDamage, Resource.FireRate);
    }

    private static Enemy _GetEnemyFromBody(Node2D body)
    {
        if (body is Enemy e) return e;
        if (body.GetParent() is Enemy pe) return pe;
        return null;
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
        if (UpgradeLevel == 2)
        {
            _hasSecondBible = true;
            GetNode<Node2D>("SecondBible").Visible = true;
        }
        else if (UpgradeLevel == 3)
        {
            RotationSpeed += 2.5f;
        }
    }
}
