using Godot;

public partial class SineArrowWeapon : Weapon
{
    private static readonly PackedScene ProjectileScene =
        GD.Load<PackedScene>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/SineArrow/sine_arrow_projectile.tscn");

    private float _amplitudeBonus = 0.0f;

    public override void _Ready()
    {
        base._Ready();
        Fire    += _OnFire;
        Upgraded += _OnUpgraded;
    }

    private void _OnFire()
    {
        var localPlayer = Global.Instance.GetLocalPlayer();
        if (localPlayer == null) return;
        if (!_BelongsToLocalPlayer(localPlayer)) return;

        var cursorWorld = GetGlobalMousePosition();
        var dir = GlobalPosition.DirectionTo(cursorWorld);
        if (dir == Vector2.Zero)
            dir = Vector2.Right;

        var proj = ProjectileScene.Instantiate<SineArrowProjectile>();
        proj.Damage       = Damage;
        proj.FireRate     = Resource.FireRate;
        proj.OwnerPeerId  = Multiplayer.GetUniqueId();
        proj.Direction    = dir;
        proj.Amplitude    = 30.0f + _amplitudeBonus;
        proj.GlobalPosition = GlobalPosition;

        GetTree().CurrentScene.AddChild(proj);
    }

    private void _OnUpgraded()
    {
        _amplitudeBonus += 20.0f;
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
}
