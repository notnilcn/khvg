#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class CombatComponent : Component
{
    [Export] public PackedScene HitZoneScene { get; set; } = null!;

    private WeaponBehavior? _weapon;
    private string? _weaponItemId;
    private float _fireTimer;
    private float _firePeriod;
    private float _zoneStep;
    private float _playerRadius;
    private bool _aimAssist;
    private bool _lockOn;
    private Enemy? _nearestEnemy;
    private float _scanTimer;
    private LocalPlayer player = null!;
    private TableBinderComponent nearbyEnemiesBinder = null!;
    private const float ScanInterval = 0.5f;
    private const float AimConeHalfAngle = 0.52f;

    public override void _Ready()
    {
        base._Ready();
        nearbyEnemiesBinder = GetNode<TableBinderComponent>("NearbyEnemiesBinder");
    }

    protected override void OnRegistered()
    {
        player = (LocalPlayer)Entity!;
        player.InventoryChanged += OnInventoryChanged;
        player.AimSettingsChanged += OnAimSettingsChanged;
        _playerRadius = (player.GetNode<CollisionShape2D>("Collider").Shape as CapsuleShape2D)?.Radius ?? 0f;
        OnAimSettingsChanged();
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

        if (!Input.IsActionPressed("fire")) return;
        if (_fireTimer < _firePeriod) return;

        _fireTimer = 0f;
        Fire();
    }

    private void OnAimSettingsChanged()
    {
        var conn = GameManager.Conn;
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
        var conn = GameManager.Conn;
        if (conn == null || LocalPlayer.Local == null) { _nearestEnemy = null; return; }

        var playerPos = LocalPlayer.Local.GlobalPosition;
        var aimDir = Vector2.Right.Rotated(LocalPlayer.Local.Rotation);

        Enemy? best = null;
        float bestDist = float.MaxValue;

        foreach (var row in conn.Db.NearbyEnemies.Iter())
        {
            var node = GameManager.GetEnemy(row.EnemyId);
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

    // --- TableBinderComponent signal handlers (wired in local_player.tscn) ---

    private void OnNearbyEnemyDeletedRow()
    {
        var enemy = (SpacetimeDB.Types.Enemy)nearbyEnemiesBinder.LastDeletedRow!;
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
        var newWeaponItem = LocalPlayer.Local?.EquippedWeapon;
        if (newWeaponItem?.ItemId == _weaponItemId) return;
        _weaponItemId = newWeaponItem?.ItemId;
        _weapon = null;
        if (newWeaponItem != null)
        {
            foreach (var behavior in newWeaponItem.Behaviors)
            {
                if (behavior is ItemBehavior.Weapon(var weapon)) { _weapon = weapon; break; }
            }
        }
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
        BulletManager.Instance.SpawnPlayerBullet(origin, aimDir.Angle(), _weapon!.Range / _weapon.ProjectileSpeed, _weapon.ProjectileSpeed, _weapon.ProjectileTextureId);
        SpawnZonesAlong(origin, aimDir.Angle(), _weapon.Range, _weapon.ProjectileSpeed);
    }

    private void FireTriple(Vector2 origin, Vector2 aimDir)
    {
        int shotCount = (int)_weapon!.ShotCount;
        float baseAngle = aimDir.Angle();
        float halfSpread = _weapon.SpreadAngle * 0.5f;
        float angleStep = shotCount > 1 ? _weapon.SpreadAngle / (shotCount - 1) : 0f;

        var angles = new float[shotCount];
        for (int b = 0; b < shotCount; b++)
            angles[b] = baseAngle + (-halfSpread + angleStep * b);

        BulletManager.Instance.SpawnPlayerBullets(origin, angles, _weapon.Range / _weapon.ProjectileSpeed, _weapon.ProjectileSpeed, _weapon.ProjectileTextureId);
        foreach (var angle in angles)
            SpawnZonesAlong(origin, angle, _weapon.Range, _weapon.ProjectileSpeed);
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
            float pelletSpeed = (float)GD.RandRange(_weapon.ProjectileSpeed * 0.8f, _weapon.ProjectileSpeed * 1.1f);
            SpawnBulletWithZones(origin, angle, pelletRange, pelletSpeed);
        }
    }

    private void SpawnBulletWithZones(Vector2 origin, float angle, float range = -1f, float speed = -1f)
    {
        float r = range > 0f ? range : _weapon!.Range;
        float s = speed > 0f ? speed : _weapon!.ProjectileSpeed;
        BulletManager.Instance.SpawnPlayerBullet(origin, angle, r / s, s, _weapon!.ProjectileTextureId);
        SpawnZonesAlong(origin, angle, r, s);
    }

    private void SpawnZonesAlong(Vector2 origin, float angle, float range, float speed)
    {
        int zoneCount = Mathf.Max(1, Mathf.RoundToInt(range / _zoneStep));
        var dir = Vector2.Right.Rotated(angle);
        for (int i = 0; i < zoneCount; i++)
            SpawnZone(origin + dir * _zoneStep * (i + 0.5f), _zoneStep * 0.5f, (i + 0.5f) * _zoneStep / speed);
    }

    private void SpawnZone(Vector2 pos, float radius, float delay)
    {
        var zone = HitZoneScene.Instantiate<HitZone>();
        AddChild(zone);
        zone.GlobalPosition = pos;
        zone.Launch(radius, delay);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (player != null)
        {
            player.InventoryChanged -= OnInventoryChanged;
            player.AimSettingsChanged -= OnAimSettingsChanged;
        }
    }
}
