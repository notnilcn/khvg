#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class CombatComponent : Component
{
    [Export] public PackedScene HitZoneScene { get; set; } = null!;

    private EffectiveWeapon? _weapon;
    private string _weaponFingerprint = "";
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
        if (Input.IsActionJustPressed("weapon_toggle"))
            CycleWeaponToggle();

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

    /// Cycles the weapon slot's shot-pattern toggle (doc 03's swap-out-meta replacement).
    /// The server validates the index against the same toggle-option list we mirror here.
    private void CycleWeaponToggle()
    {
        var local = LocalPlayer.Local;
        var conn = GameManager.Conn;
        if (local == null || conn == null) return;
        int count = EffectiveWeaponResolver.ToggleOptions(local).Count;
        if (count < 2) return;
        uint current = local.ResolveSlotAt(0).Slot?.ActiveToggle ?? 0;
        conn.Reducers.SetSlotToggle(0, (current + 1) % (uint)count);
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
            if (_weapon == null || dist > _weapon.Behavior.Range) continue;

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
        var local = LocalPlayer.Local;
        if (local == null) return;
        // The fingerprint covers the weapon item, its ActiveToggle, and every equipped
        // slot's enchantments — anything that changes the effective weapon re-resolves it.
        var fingerprint = EffectiveWeaponResolver.Fingerprint(local);
        if (fingerprint == _weaponFingerprint) return;
        _weaponFingerprint = fingerprint;

        _weapon = EffectiveWeaponResolver.Resolve(local);
        if (_weapon == null || _weapon.Behavior.ZoneCount == 0) { _fireTimer = 0f; return; }
        _fireTimer = _firePeriod;
        _firePeriod = 1f / Mathf.Max(0.001f, _weapon.Behavior.FireRate);
        _zoneStep = _weapon.Behavior.Range / _weapon.Behavior.ZoneCount;
    }

    private void Fire()
    {
        if (_weapon == null || LocalPlayer.Local == null) return;
        var player = LocalPlayer.Local;
        var aimDir = GetAimDir(player);
        var origin = player.GlobalPosition + aimDir * _playerRadius;

        switch (_weapon.Behavior.Pattern)
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
        var behavior = _weapon!.Behavior;
        BulletManager.Instance.SpawnPlayerBullet(origin, aimDir.Angle(), behavior.Range / behavior.ProjectileSpeed, behavior.ProjectileSpeed, behavior.ProjectileTextureId);
        SpawnZonesAlong(origin, aimDir.Angle(), behavior.Range, behavior.ProjectileSpeed);
    }

    private void FireTriple(Vector2 origin, Vector2 aimDir)
    {
        var behavior = _weapon!.Behavior;
        int shotCount = (int)behavior.ShotCount;
        float baseAngle = aimDir.Angle();
        float halfSpread = behavior.SpreadAngle * 0.5f;
        float angleStep = shotCount > 1 ? behavior.SpreadAngle / (shotCount - 1) : 0f;

        var angles = new float[shotCount];
        for (int b = 0; b < shotCount; b++)
            angles[b] = baseAngle + (-halfSpread + angleStep * b);

        BulletManager.Instance.SpawnPlayerBullets(origin, angles, behavior.Range / behavior.ProjectileSpeed, behavior.ProjectileSpeed, behavior.ProjectileTextureId);
        foreach (var angle in angles)
            SpawnZonesAlong(origin, angle, behavior.Range, behavior.ProjectileSpeed);
    }

    private void FireCluster(Vector2 origin, Vector2 aimDir)
    {
        var behavior = _weapon!.Behavior;
        int shotCount = (int)behavior.ShotCount;
        float baseAngle = aimDir.Angle();
        float halfSpread = behavior.SpreadAngle * 0.5f;

        for (int b = 0; b < shotCount; b++)
        {
            float angle = baseAngle + (float)GD.RandRange(-halfSpread, halfSpread);
            float pelletRange = (float)GD.RandRange(behavior.Range * 0.5f, behavior.Range);
            float pelletSpeed = (float)GD.RandRange(behavior.ProjectileSpeed * 0.8f, behavior.ProjectileSpeed * 1.1f);
            SpawnBulletWithZones(origin, angle, pelletRange, pelletSpeed);
        }
    }

    private void SpawnBulletWithZones(Vector2 origin, float angle, float range = -1f, float speed = -1f)
    {
        var behavior = _weapon!.Behavior;
        float r = range > 0f ? range : behavior.Range;
        float s = speed > 0f ? speed : behavior.ProjectileSpeed;
        BulletManager.Instance.SpawnPlayerBullet(origin, angle, r / s, s, behavior.ProjectileTextureId);
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
