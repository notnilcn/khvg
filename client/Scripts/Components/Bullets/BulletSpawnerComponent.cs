#nullable enable
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Bullet spawning for the BulletManager entity: owns the enemy/player spawner configs
/// (ClassDB.Instantiate of BlastBullets2D *resources* — not nodes), dispatches
/// BulletPatternEvent rows to the per-pattern spawn methods, and fires player bullets
/// for CombatComponent. Enemy bullets spawn via spawn_controllable_directional_bullets and
/// the returned DirectionalBullets2D instances are tracked in LiveEnemyBullets so
/// BulletControllerComponent can find and manipulate live bullets (the factory cannot enumerate
/// them). The deterministic SplitMix64 jitter mirrors the server's hash technique in
/// enemy/methods.rs, seeded by event_id so every client (and the future audit script)
/// derives identical pellet trajectories (logic moved out of BulletManager.cs).
/// </summary>
public partial class BulletSpawnerComponent : Component
{
    private GodotObject EnemyBulletSpawner = null!;
    private GodotObject PlayerBulletSpawner = null!;
    private readonly System.Collections.Generic.Dictionary<string, Texture2D?> bulletTextureCache = [];

    // Live DirectionalBullets2D instances returned by spawn_controllable_directional_bullets.
    // The factory offers no way to enumerate active instances, so they are captured here for
    // BulletControllerComponent. A HashSet dedups instances that auto-pooling hands back out for
    // a later spawn; dead entries are pruned periodically and before each enemy spawn.
    private readonly System.Collections.Generic.HashSet<GodotObject> liveEnemyBullets = [];
    private float pruneTimer;
    private const float PruneInterval = 1f;

    public System.Collections.Generic.IReadOnlyCollection<GodotObject> LiveEnemyBullets => liveEnemyBullets;

    // Arrow.tres art faces up-right (-45 degrees) instead of Vector2.Right, so compensate before BlastBullets2D applies its own trajectory rotation.
    private const float ProjectileTextureAngleOffset = Mathf.Pi / 4f;

    /// The BlastBullets2D factory node, owned by the BulletManager entity root.
    private Node BlastBullets => ((BulletManager)Entity!).BlastBullets;

    protected override void OnRegistered()
    {
        SetupEnemyBullets();
        SetupPlayerBullets();
    }

    public override void _Process(double delta)
    {
        pruneTimer += (float)delta;
        if (pruneTimer >= PruneInterval)
        {
            pruneTimer = 0f;
            PruneLiveEnemyBullets();
        }
    }

    // No per-bullet despawn signal exists, so liveness is checked defensively: an instance is
    // dropped once it is freed or has no enabled bullets left. The bulk get_all_bullets_status
    // array is unreliable for multi-bullet instances (only index 0 reports true), so liveness
    // goes through the per-index is_bullet_status_enabled.
    private void PruneLiveEnemyBullets() =>
        liveEnemyBullets.RemoveWhere(inst =>
        {
            if (!GodotObject.IsInstanceValid(inst)) return true;
            int count = inst.Call("get_amount_bullets").AsInt32();
            for (int i = 0; i < count; i++)
                if (inst.Call("is_bullet_status_enabled", i).AsBool()) return false;
            return true;
        });

    private void SpawnControllable()
    {
        var instance = BlastBullets.Call("spawn_controllable_directional_bullets", EnemyBulletSpawner).AsGodotObject();
        if (instance != null) liveEnemyBullets.Add(instance);
    }

    private void SetupEnemyBullets()
    {
        EnemyBulletSpawner = ClassDB.Instantiate("DirectionalBulletsData2D").AsGodotObject();
        EnemyBulletSpawner.Set("texture_size", new Vector2(8, 8));
        EnemyBulletSpawner.Call("set_collision_layer_from_array", new Godot.Collections.Array { 2 });
        EnemyBulletSpawner.Call("set_collision_mask_from_array", new Godot.Collections.Array { 2 });
        EnemyBulletSpawner.Set("collision_shape_size", new Vector2(4, 4));
    }

    private void SetupPlayerBullets()
    {
        PlayerBulletSpawner = ClassDB.Instantiate("DirectionalBulletsData2D").AsGodotObject();
        PlayerBulletSpawner.Set("texture_size", new Vector2(8, 8));
    }

    private static Transform2D BulletTransform(float angle, Vector2 origin) => new(angle, origin);

    public void SpawnPlayerBullet(Vector2 origin, float angle, float lifetime, float speed, string textureId) =>
        SpawnPlayerBullets(origin, [angle], lifetime, speed, textureId);

    public void SpawnPlayerBullets(Vector2 origin, float[] angles, float lifetime, float speed, string textureId)
    {
        ApplyBulletTexture(PlayerBulletSpawner, textureId);
        PlayerBulletSpawner.Set("max_life_time", lifetime);
        PlayerBulletSpawner.Set("all_bullet_speed_data", ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject().Call("generate_random_data", 1, speed, speed, speed, speed, 0, 0));
        var transforms = new Godot.Collections.Array<Transform2D>();
        foreach (var angle in angles)
            transforms.Add(BulletTransform(angle, origin));
        PlayerBulletSpawner.Set("transforms", transforms);
        BlastBullets.Call("spawn_directional_bullets", PlayerBulletSpawner);
    }

    private void ApplyBulletTexture(GodotObject data, string textureId)
    {
        if (!bulletTextureCache.TryGetValue(textureId, out var tex))
        {
            var resPath = GameManager.GetResPath(textureId);
            var frame = resPath != null ? GD.Load<SpriteFrames>(resPath)?.GetFrameTexture("default", 0) : null;
            tex = frame != null ? ImageTexture.CreateFromImage(frame.GetImage()) : null;
            bulletTextureCache[textureId] = tex;
        }
        if (tex != null)
            data.Call("set_textures", new Godot.Collections.Array<Texture2D> { tex });
    }

    public void SpawnEnemyBullet(BulletPatternEvent bulletPattern)
    {
        PruneLiveEnemyBullets();
        var origin = new Vector2(bulletPattern.OriginX, bulletPattern.OriginY) + new Vector2(bulletPattern.OriginOffsetX, bulletPattern.OriginOffsetY);
        var baseAngle = ResolveTargetAngle(origin, bulletPattern.Target) + Mathf.DegToRad(bulletPattern.BaseAngleOffset);

        EnemyBulletSpawner.Set("bullets_custom_data", new BulletData { SourceStep = bulletPattern.SourceStep });
        EnemyBulletSpawner.Set("max_life_time", bulletPattern.Lifetime);
        ApplyBulletTexture(EnemyBulletSpawner, bulletPattern.TextureId);

        if (bulletPattern.PatternType is PatternType.Ring(var ring))
            SpawnRing(origin, ring, baseAngle);
        else if (bulletPattern.PatternType is PatternType.Volley(var volley))
            SpawnVolley(origin, volley, baseAngle, bulletPattern.EventId, bulletPattern.Lifetime);
        else if (bulletPattern.PatternType is PatternType.Curtain(var curtain))
            SpawnCurtain(origin, curtain, baseAngle);
        else if (bulletPattern.PatternType is PatternType.Shotgun(var shotgun))
            SpawnShotgun(origin, shotgun, baseAngle, bulletPattern.EventId, bulletPattern.Lifetime);
        else if (bulletPattern.PatternType is PatternType.Explosion(var explosion))
            SpawnExplosion(origin, explosion, baseAngle, bulletPattern.EventId, bulletPattern.Lifetime);
    }

    private static float ResolveTargetAngle(Vector2 origin, SpacetimeDB.Identity? target)
    {
        var pos = ResolveTargetPosition(target);
        return pos.HasValue ? (pos.Value - origin).Angle() : 0f;
    }

    private static Vector2? ResolveTargetPosition(SpacetimeDB.Identity? target)
    {
        var conn = GameManager.Conn;
        if (conn == null || target == null) return null;

        if (GameManager.IsLocal(target.Value))
        {
            foreach (var row in conn.Db.LocalPlayerPosition.Iter())
                return new Vector2(row.X, row.Y);
            return null;
        }

        foreach (var row in conn.Db.NearbyRemotePlayers.Iter())
        {
            if (row.PlayerId == target.Value)
                return new Vector2(row.X, row.Y);
        }
        return null;
    }

    private void SetSpeed(float speed)
    {
        EnemyBulletSpawner.Set("all_bullet_speed_data", ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject().Call("generate_random_data", 1, speed, speed, speed, speed, 0, 0));
    }

    // Deterministic PRNG (SplitMix64) mirroring the server's hash technique in enemy/methods.rs,
    // seeded by event_id so every client (and the future audit script) derives identical pellet trajectories.
    private static ulong SplitMix64(ulong seed)
    {
        ulong h = (seed ^ (seed >> 30)) * 0xbf58476d1ce4e5b9UL;
        h = (h ^ (h >> 27)) * 0x94d049bb133111ebUL;
        return h ^ (h >> 31);
    }

    private static float HashToUnit(ulong h) => (h >> 11) / (float)(1UL << 53);

    private static ulong PelletSeed(ulong eventId, uint pelletIndex, uint stream)
    {
        unchecked
        {
            return SplitMix64(eventId + pelletIndex * 0x9E3779B97F4A7C15UL + stream * 0xBF58476D1CE4E5B9UL);
        }
    }

    private static float Jitter(ulong eventId, uint pelletIndex, uint stream, float magnitude) =>
        (HashToUnit(PelletSeed(eventId, pelletIndex, stream)) * 2f - 1f) * magnitude;

    private void SpawnSingle(Vector2 origin, float angle, float speed, float lifetime)
    {
        EnemyBulletSpawner.Set("max_life_time", lifetime);
        SetSpeed(speed);
        EnemyBulletSpawner.Set("transforms", new Godot.Collections.Array<Transform2D> { BulletTransform(angle, origin) });
        SpawnControllable();
    }

    private void SpawnRing(Vector2 origin, RingParams p, float baseAngle)
    {
        SetSpeed(p.Speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = Mathf.Tau / p.Count;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(BulletTransform(baseAngle + angleStep * i, origin));
        EnemyBulletSpawner.Set("transforms", transforms);
        SpawnControllable();
    }

    private void SpawnVolley(Vector2 origin, VolleyParams p, float baseAngle, ulong eventId, float lifetime)
    {
        for (uint i = 0; i < p.Count; i++)
        {
            float speed = p.Speed + Jitter(eventId, i, 0, p.SpeedVariance);
            float angle = baseAngle + Jitter(eventId, i, 1, p.AngleJitter);
            SpawnSingle(origin, angle, speed, lifetime);
        }
    }

    private void SpawnCurtain(Vector2 origin, CurtainParams p, float baseAngle)
    {
        if (p.Count == 0) return;
        SetSpeed(p.Speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = p.Count > 1 ? p.AngleSpan / (p.Count - 1) : 0f;
        float startAngle = baseAngle - p.AngleSpan * 0.5f;
        for (uint i = 0; i < p.Count; i++)
        {
            if (i == p.GapIndex) continue;
            transforms.Add(BulletTransform(startAngle + angleStep * i, origin));
        }
        EnemyBulletSpawner.Set("transforms", transforms);
        SpawnControllable();
    }

    private void SpawnShotgun(Vector2 origin, ShotgunParams p, float baseAngle, ulong eventId, float lifetime)
    {
        if (p.Count == 0) return;
        float halfSpread = p.Spread * 0.5f;
        float angleStep = p.Count > 1 ? p.Spread / (p.Count - 1) : 0f;
        for (uint i = 0; i < p.Count; i++)
        {
            float angle = baseAngle - halfSpread + angleStep * i;
            float speed = p.Speed + Jitter(eventId, i, 0, p.SpeedVariance);
            float pelletLifetime = lifetime + Jitter(eventId, i, 1, p.LifetimeVariance);
            SpawnSingle(origin, angle, speed, pelletLifetime);
        }
    }

    private void SpawnExplosion(Vector2 origin, ExplosionParams p, float baseAngle, ulong eventId, float lifetime)
    {
        if (p.Count == 0) return;
        float angleStep = Mathf.Tau / p.Count;
        for (uint i = 0; i < p.Count; i++)
        {
            float angle = baseAngle + angleStep * i;
            float speed = p.Speed + Jitter(eventId, i, 0, p.SpeedVariance);
            float pelletLifetime = lifetime + Jitter(eventId, i, 1, p.LifetimeVariance);
            SpawnSingle(origin, angle, speed, pelletLifetime);
        }
    }

    /// Fan of enemy bullets for BulletControllerComponent's split effect. Keeps the source
    /// bullet's BulletData so split pellets still report the original SourceStep on hit, and
    /// whatever texture was last applied to the shared enemy spawner.
    public void SpawnEnemyBulletFan(Vector2 origin, float baseAngle, int count, float spread, float speed, float lifetime, BulletData? customData)
    {
        if (count == 0) return;
        if (customData != null)
            EnemyBulletSpawner.Set("bullets_custom_data", customData);
        EnemyBulletSpawner.Set("max_life_time", lifetime);
        SetSpeed(speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        float halfSpread = spread * 0.5f;
        float angleStep = count > 1 ? spread / (count - 1) : 0f;
        for (int i = 0; i < count; i++)
            transforms.Add(BulletTransform(baseAngle - halfSpread + angleStep * i, origin));
        EnemyBulletSpawner.Set("transforms", transforms);
        SpawnControllable();
    }
}
