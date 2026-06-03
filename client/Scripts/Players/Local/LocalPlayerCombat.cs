#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class LocalPlayerCombat : Node2D
{
    private Weapon? _weapon;
    private bool _wasFiring;
    private float _fireTimer;
    private float _firePeriod;
    private float _zoneInterval;
    private float _zoneStep;
    private float _playerRadius;

    public override void _Ready()
    {
        var player = GetParent<LocalPlayer>();
        player.InventoryChanged += OnInventoryChanged;
        _playerRadius = (player.GetNode<CollisionShape2D>("CollisionShape2D").Shape as CapsuleShape2D)?.Radius ?? 0f;
    }

    public override void _Process(double delta)
    {
        if (_weapon == null) return;

        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _wasFiring = false;
            _fireTimer = 0f;
            return;
        }

        if (!_wasFiring)
        {
            _wasFiring = true;
            _fireTimer = _firePeriod;
        }

        _fireTimer += (float)delta;
        if (_fireTimer < _firePeriod) return;
        _fireTimer = 0f;
        Fire();
    }

    private void OnInventoryChanged()
    {
        var newWeapon = LocalPlayer.Local?.EquippedWeapon;
        if (newWeapon?.WeaponId == _weapon?.WeaponId) return;
        _weapon = newWeapon;
        _fireTimer = 0f;
        _wasFiring = false;
        if (_weapon == null || _weapon.ZoneCount == 0) return;
        _firePeriod = 1f / Mathf.Max(0.001f, _weapon.FireRate);
        _zoneInterval = _firePeriod / _weapon.ZoneCount;
        _zoneStep = _weapon.Range / _weapon.ZoneCount;
    }

    private void Fire()
    {
        if (_weapon == null || LocalPlayer.Local == null) return;
        var player = LocalPlayer.Local;
        var aimDir = Vector2.Right.Rotated(player.Rotation);
        var bulletOrigin = player.GlobalPosition + aimDir * _playerRadius;

        BulletManager.Instance.SpawnPlayerBullet(
            bulletOrigin, player.Rotation,
            _weapon.Range / _weapon.BulletSpeed,
            _weapon.BulletSpeed,
            _weapon.ProjectileTextureId);

        for (int i = 0; i < (int)_weapon.ZoneCount; i++)
        {
            var zone = new Area2D { CollisionLayer = 0, CollisionMask = 1, Monitoring = true, Monitorable = false, TopLevel = true };
            zone.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = _zoneStep * 0.5f } });
            AddChild(zone);
            zone.GlobalPosition = bulletOrigin + aimDir * _zoneStep * (i + 0.5f);

            var capturedZone = zone;
            var capturedIndex = i;
            GetTree().CreateTimer((capturedIndex + 1) * _zoneInterval).Timeout += () =>
            {
                ScanZone(capturedZone, capturedIndex);
                capturedZone.QueueFree();
            };
        }
    }

    private static void ScanZone(Area2D zone, int zoneNumber)
    {
        if (!IsInstanceValid(zone)) return;
        var conn = GameManager.Instance.Conn;
        if (conn == null) return;

        foreach (var body in zone.GetOverlappingBodies())
        {
            if (body is Enemy enemy)
            {
                conn.Reducers.ReportEnemyHit(enemy.EnemyId);
                GD.Print($"Enemy {enemy.EnemyId} Hit In Zone {zoneNumber + 1}");
            }
        }
    }

    public override void _ExitTree()
    {
        if (GetParent() is LocalPlayer player)
            player.InventoryChanged -= OnInventoryChanged;
    }
}
