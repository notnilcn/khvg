#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class LocalPlayerCombat : Node2D
{
    private Weapon? _weapon;
    private float _fireTimer;
    private float _firePeriod;
    private float _zoneStep;
    private float _playerRadius;
    private bool _aimAssist;
    private bool _lockOn;
    private Enemy? _nearestEnemy;
    private float _scanTimer;
    private const float ScanInterval = 0.5f;
    private const float AimConeHalfAngle = 0.52f;

    public override void _Ready()
    {
        var player = GetParent<LocalPlayer>();
        player.InventoryChanged += OnInventoryChanged;
        player.AimSettingsChanged += OnAimSettingsChanged;
        _playerRadius = (player.GetNode<CollisionShape2D>("CollisionShape2D").Shape as CapsuleShape2D)?.Radius ?? 0f;
        OnAimSettingsChanged();

        var conn = GameManager.Instance.Conn;
        if (conn != null)
            conn.Db.NearbyEnemies.OnDelete += OnNearbyEnemyDelete;
    }

    public override void _Process(double delta)
    {
        if (_weapon == null) return;

        _fireTimer = Mathf.Min(_fireTimer + (float)delta, _firePeriod);

        if (_aimAssist || _lockOn)
        {
            _scanTimer += (float)delta;
            if (_scanTimer >= ScanInterval)
            {
                _scanTimer = 0f;
                ScanNearestEnemy();
            }
        }
        else
        {
            _nearestEnemy = null;
            _scanTimer = 0f;
        }

        if (!Input.IsMouseButtonPressed(MouseButton.Left)) return;
        if (_fireTimer < _firePeriod) return;

        _fireTimer = 0f;
        Fire();
    }

    private void OnAimSettingsChanged()
    {
        var conn = GameManager.Instance.Conn;
        if (conn == null) return;
        foreach (var profile in conn.Db.LocalPlayerActiveProfile.Iter())
        {
            _aimAssist = profile.AimAssist;
            _lockOn = profile.LockOn;
            return;
        }
    }

    private void ScanNearestEnemy()
    {
        var conn = GameManager.Instance.Conn;
        if (conn == null || LocalPlayer.Local == null) { _nearestEnemy = null; return; }

        var playerPos = LocalPlayer.Local.GlobalPosition;
        var aimDir = Vector2.Right.Rotated(LocalPlayer.Local.Rotation);

        Enemy? best = null;
        float bestDist = float.MaxValue;

        foreach (var row in conn.Db.NearbyEnemies.Iter())
        {
            var node = GameManager.Instance.GetEnemy(row.EnemyId);
            if (node == null || !IsInstanceValid(node)) continue;

            float dist = playerPos.DistanceTo(node.GlobalPosition);
            if (_weapon == null || dist > _weapon.Range) continue;

            if (_lockOn)
            {
                if (dist < bestDist) { bestDist = dist; best = node; }
            }
            else
            {
                var toEnemy = (node.GlobalPosition - playerPos).Normalized();
                if (Mathf.Abs(aimDir.AngleTo(toEnemy)) <= AimConeHalfAngle && dist < bestDist)
                { bestDist = dist; best = node; }
            }
        }

        _nearestEnemy = best;
    }

    private void OnNearbyEnemyDelete(EventContext _, SpacetimeDB.Types.Enemy enemy)
    {
        if (_nearestEnemy?.EnemyId == enemy.EnemyId)
        {
            _nearestEnemy = null;
            ScanNearestEnemy();
        }
    }

    private Vector2 GetAimDir(LocalPlayer player)
    {
        var defaultDir = Vector2.Right.Rotated(player.Rotation);
        if (_nearestEnemy == null || !IsInstanceValid(_nearestEnemy)) return defaultDir;
        return player.GlobalPosition.DirectionTo(_nearestEnemy.GlobalPosition);
    }

    private void OnInventoryChanged()
    {
        var newWeapon = LocalPlayer.Local?.EquippedWeapon;
        if (newWeapon?.WeaponId == _weapon?.WeaponId) return;
        _weapon = newWeapon;
        if (_weapon == null || _weapon.ZoneCount == 0) { _fireTimer = 0f; return; }
        _fireTimer = _firePeriod;
        _firePeriod = 1f / Mathf.Max(0.001f, _weapon.FireRate);
        _zoneStep = _weapon.Range / _weapon.ZoneCount;
    }

    private void Fire()
    {
        if (_weapon == null || LocalPlayer.Local == null) return;
        var player = LocalPlayer.Local;
        var aimDir = GetAimDir(player);
        var origin = player.GlobalPosition + aimDir * _playerRadius;

        switch (_weapon.Pattern)
        {
            case WeaponPattern.Single:
                FireSingle(origin, aimDir);
                break;
            case WeaponPattern.Triple:
                FireTriple(origin, aimDir);
                break;
            case WeaponPattern.Cluster:
                FireCluster(origin, aimDir);
                break;
        }
    }

    private void FireSingle(Vector2 origin, Vector2 aimDir)
    {
        BulletManager.Instance.SpawnPlayerBullet(origin, aimDir.Angle(), _weapon!.Range / _weapon.BulletSpeed, _weapon.BulletSpeed, _weapon.ProjectileTextureId);
        for (int i = 0; i < (int)_weapon.ZoneCount; i++)
            SpawnZone(origin + aimDir * _zoneStep * (i + 0.5f), _zoneStep * 0.5f, (i + 0.5f) * _zoneStep / _weapon.BulletSpeed);
    }

    private void FireTriple(Vector2 origin, Vector2 aimDir)
    {
        int shotCount = (int)_weapon!.ShotCount;
        float baseAngle = aimDir.Angle();
        float halfSpread = _weapon.SpreadAngle * 0.5f;
        float angleStep = shotCount > 1 ? _weapon.SpreadAngle / (shotCount - 1) : 0f;

        for (int b = 0; b < shotCount; b++)
        {
            float angle = baseAngle + (-halfSpread + angleStep * b);
            SpawnBulletWithZones(origin, angle);
        }
    }

    private void FireCluster(Vector2 origin, Vector2 aimDir)
    {
        int shotCount = (int)_weapon!.ShotCount;
        float baseAngle = aimDir.Angle();
        float halfSpread = _weapon.SpreadAngle * 0.5f;

        for (int b = 0; b < shotCount; b++)
        {
            float angle = baseAngle + (float)GD.RandRange(-halfSpread, halfSpread);
            float pelletRange = (float)GD.RandRange(_weapon.Range * 0.5f, _weapon.Range);
            float pelletSpeed = (float)GD.RandRange(_weapon.BulletSpeed * 0.8f, _weapon.BulletSpeed * 1.1f);
            SpawnBulletWithZones(origin, angle, pelletRange, pelletSpeed);
        }
    }

    private void SpawnBulletWithZones(Vector2 origin, float angle, float range = -1f, float speed = -1f)
    {
        float r = range > 0f ? range : _weapon!.Range;
        float s = speed > 0f ? speed : _weapon!.BulletSpeed;
        int zoneCount = Mathf.Max(1, Mathf.RoundToInt(r / _zoneStep));
        var dir = Vector2.Right.Rotated(angle);
        BulletManager.Instance.SpawnPlayerBullet(origin, angle, r / s, s, _weapon!.ProjectileTextureId);
        for (int i = 0; i < zoneCount; i++)
            SpawnZone(origin + dir * _zoneStep * (i + 0.5f), _zoneStep * 0.5f, (i + 0.5f) * _zoneStep / s);
    }

    private void SpawnZone(Vector2 pos, float radius, float delay)
    {
        var zone = new Area2D { CollisionLayer = 0, CollisionMask = 1, Monitoring = true, Monitorable = false, TopLevel = true };
        zone.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = radius } });
        AddChild(zone);
        zone.GlobalPosition = pos;
        var capturedZone = zone;
        GetTree().CreateTimer(delay).Timeout += () =>
        {
            ScanZone(capturedZone);
            capturedZone.QueueFree();
        };
    }

    private static void ScanZone(Area2D zone)
    {
        if (!IsInstanceValid(zone)) return;
        var conn = GameManager.Instance.Conn;
        if (conn == null) return;
        foreach (var body in zone.GetOverlappingBodies())
        {
            if (body is Enemy enemy)
                conn.Reducers.ReportEnemyHit(enemy.EnemyId);
        }
    }

    public override void _ExitTree()
    {
        if (GetParent() is LocalPlayer player)
        {
            player.InventoryChanged -= OnInventoryChanged;
            player.AimSettingsChanged -= OnAimSettingsChanged;
        }
        var conn = GameManager.Instance.Conn;
        if (conn != null)
            conn.Db.NearbyEnemies.OnDelete -= OnNearbyEnemyDelete;
    }
}
