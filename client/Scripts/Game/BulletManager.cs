#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class BulletManager : Node
{
    public static BulletManager Instance { get; private set; } = null!;

    private Node blastBullets = null!;
    private GodotObject directionalData = null!;
    private GodotObject playerBulletData = null!;
    private readonly System.Collections.Generic.Dictionary<string, Texture2D?> bulletTextureCache = [];

    public override void _Ready()
    {
        Instance = this;
        blastBullets = GetChild(0);
        SetupDirectionalBullets();
        SetupPlayerBulletData();
    }

    private void SetupDirectionalBullets()
    {
        directionalData = ClassDB.Instantiate("DirectionalBulletsData2D").AsGodotObject();
        directionalData.Set("texture_size", new Vector2(8, 8));
        directionalData.Call("set_collision_layer_from_array", new Godot.Collections.Array { 3 });
        directionalData.Call("set_collision_mask_from_array", new Godot.Collections.Array { 2 });
        directionalData.Set("collision_shape_size", new Vector2(4, 4));
        directionalData.Set("max_life_time", 8.0f);
    }

    private void SetupPlayerBulletData()
    {
        playerBulletData = ClassDB.Instantiate("DirectionalBulletsData2D").AsGodotObject();
        playerBulletData.Set("texture_size", new Vector2(8, 8));
        playerBulletData.Call("set_collision_layer_from_array", new Godot.Collections.Array());
        playerBulletData.Call("set_collision_mask_from_array", new Godot.Collections.Array());
        playerBulletData.Set("collision_shape_size", Vector2.Zero);
    }

    public void SpawnPlayerBullet(Vector2 origin, float angle, float lifetime, float speed, string textureId)
    {
        ApplyBulletTexture(playerBulletData, textureId);
        playerBulletData.Set("max_life_time", lifetime);
        playerBulletData.Set("all_bullet_speed_data",
            ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject()
                .Call("generate_random_data", 1, speed, speed, speed, speed, 0, 0));
        playerBulletData.Set("transforms", new Godot.Collections.Array<Transform2D> { new(angle, origin) });
        blastBullets.Call("spawn_directional_bullets", playerBulletData);
    }

    private void ApplyBulletTexture(GodotObject data, string textureId)
    {
        if (!bulletTextureCache.TryGetValue(textureId, out var tex))
        {
            var resPath = GameManager.Instance.GetResPath(textureId);
            tex = resPath != null ? GD.Load<Texture2D>(resPath) : null;
            bulletTextureCache[textureId] = tex;
        }
        if (tex != null)
            data.Call("set_textures", new Godot.Collections.Array<Texture2D> { tex });
    }

    public void Spawn(BulletPatternEvent bulletPattern)
    {
        var origin    = new Vector2(bulletPattern.OriginX, bulletPattern.OriginY) + new Vector2(bulletPattern.OriginOffsetX, bulletPattern.OriginOffsetY);
        var baseAngle = ResolveTargetAngle(origin, bulletPattern.Target) + Mathf.DegToRad(bulletPattern.BaseAngleOffset);

        directionalData.SetMeta("event_id", bulletPattern.EventId);
        directionalData.SetMeta("enemy_id", bulletPattern.EnemyId);
        directionalData.Set("max_life_time", bulletPattern.Lifetime);
        ApplyBulletTexture(directionalData, bulletPattern.TextureId);

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

    public void SpawnDirectional(Vector2 origin, Vector2 direction)
    {
        directionalData.Set("transforms", new Godot.Collections.Array<Transform2D> { new(direction.Angle(), origin) });
        blastBullets.Call("spawn_directional_bullets", directionalData);
    }

    private static float ResolveTargetAngle(Vector2 origin, SpacetimeDB.Identity? target)
    {
        var pos = ResolveTargetPosition(target);
        return pos.HasValue ? (pos.Value - origin).Angle() : 0f;
    }

    private static Vector2? ResolveTargetPosition(SpacetimeDB.Identity? target)
    {
        var conn = GameManager.Instance.Conn;
        if (conn == null || target == null) return null;

        if (GameManager.Instance.IsLocal(target.Value))
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
        directionalData.Set("all_bullet_speed_data",
            ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject()
                .Call("generate_random_data", 1, speed, speed, speed, speed, 0, 0));
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
        directionalData.Set("max_life_time", lifetime);
        SetSpeed(speed);
        directionalData.Set("transforms", new Godot.Collections.Array<Transform2D> { new(angle, origin) });
        blastBullets.Call("spawn_directional_bullets", directionalData);
    }

    private void SpawnRing(Vector2 origin, RingParams p, float baseAngle)
    {
        SetSpeed(p.Speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = Mathf.Tau / p.Count;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(baseAngle + angleStep * i, origin));
        directionalData.Set("transforms", transforms);
        blastBullets.Call("spawn_directional_bullets", directionalData);
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
        float angleStep  = p.Count > 1 ? p.AngleSpan / (p.Count - 1) : 0f;
        float startAngle = baseAngle - p.AngleSpan * 0.5f;
        for (uint i = 0; i < p.Count; i++)
        {
            if (i == p.GapIndex) continue;
            transforms.Add(new Transform2D(startAngle + angleStep * i, origin));
        }
        directionalData.Set("transforms", transforms);
        blastBullets.Call("spawn_directional_bullets", directionalData);
    }

    private void SpawnShotgun(Vector2 origin, ShotgunParams p, float baseAngle, ulong eventId, float lifetime)
    {
        if (p.Count == 0) return;
        float halfSpread = p.Spread * 0.5f;
        float angleStep  = p.Count > 1 ? p.Spread / (p.Count - 1) : 0f;
        for (uint i = 0; i < p.Count; i++)
        {
            float angle          = baseAngle - halfSpread + angleStep * i;
            float speed          = p.Speed + Jitter(eventId, i, 0, p.SpeedVariance);
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
            float angle          = baseAngle + angleStep * i;
            float speed          = p.Speed + Jitter(eventId, i, 0, p.SpeedVariance);
            float pelletLifetime = lifetime + Jitter(eventId, i, 1, p.LifetimeVariance);
            SpawnSingle(origin, angle, speed, pelletLifetime);
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null!;
    }
}
