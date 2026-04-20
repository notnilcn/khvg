using Godot;

public partial class AxeWeapon : Weapon
{
    private CollisionShape2D _firstCollShape;
    private CollisionShape2D _secondCollShape;

    private bool _hasSecondAxe = false;
    private float _tweenSpeed  = 0.5f;

    public override void _Ready()
    {
        base._Ready();
        _firstCollShape  = GetNode<CollisionShape2D>("FirstAxe/FirstCollShape");
        _secondCollShape = GetNode<CollisionShape2D>("SecondAxe/SecondCollShape");
        Fire    += _OnFire;
        Upgraded += _OnUpgraded;
    }

    private async void _RotationTween()
    {
        var tween = GetTree().CreateTween();
        tween.TweenProperty(this, "rotation_degrees", 90.0f, _tweenSpeed);
        await ToSignal(tween, Tween.SignalName.Finished);
        Visible = false;
        _firstCollShape.Disabled = true;
        if (_hasSecondAxe)
            _secondCollShape.Disabled = true;
        Rotation = 0.0f;
    }

    private void _OnFire()
    {
        Visible = true;
        _firstCollShape.Disabled = false;
        if (_hasSecondAxe)
            _secondCollShape.Disabled = false;
        _RotationTween();
    }

    private void _OnAreaBodyEntered(Node2D body)
    {
        if (!Visible)
            return;

        var enemy = _GetEnemyFromBody(body);
        if (enemy == null)
            return;

        var localPlayer = Global.Instance.GetLocalPlayer();
        if (localPlayer == null)
            return;
        if (!_BelongsToLocalPlayer(localPlayer))
            return;

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
            _hasSecondAxe = true;
            GetNode<Node2D>("SecondAxe").Visible = true;
        }
        else if (UpgradeLevel == 3)
        {
            _tweenSpeed = 0.25f;
        }
    }
}
