#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class BulletManager : Node
{
    public static BulletManager Instance { get; private set; } = null!;

    [Export] public Texture2D[] BulletTextures { get; set; } = System.Array.Empty<Texture2D>();

    private Node blastBullets = null!;
    private GodotObject directionalData = null!;
    private readonly System.Collections.Generic.Dictionary<string, Texture2D?> bulletTextureCache = new();

    public override void _Ready()
    {
        Instance = this;
        blastBullets = GetChild(0);
        SetupDirectionalBullets();
    }

    private void SetupDirectionalBullets()
    {
        directionalData = ClassDB.Instantiate("DirectionalBulletsData2D").AsGodotObject();
        directionalData.Call("set_textures", new Godot.Collections.Array<Texture2D>(BulletTextures));
        directionalData.Set("texture_size", new Vector2(8, 8));
        directionalData.Call("set_collision_layer_from_array", new Godot.Collections.Array { 3 });
        directionalData.Call("set_collision_mask_from_array", new Godot.Collections.Array { 2 });
        directionalData.Set("collision_shape_size", new Vector2(4, 4));
        directionalData.Set("max_life_time", 8.0f);
    }

    private void ApplyBulletTexture(string textureId)
    {
        if (!bulletTextureCache.TryGetValue(textureId, out var tex))
        {
            var resPath = GameManager.Instance.GetResPath(textureId);
            tex = resPath != null ? GD.Load<Texture2D>(resPath) : null;
            bulletTextureCache[textureId] = tex;
        }
        if (tex != null)
            directionalData.Call("set_textures", new Godot.Collections.Array<Texture2D> { tex });
    }

    public void Spawn(BulletPatternEvent bulletPattern)
    {
        var origin    = new Vector2(bulletPattern.OriginX, bulletPattern.OriginY) + new Vector2(bulletPattern.OriginOffsetX, bulletPattern.OriginOffsetY);
        var baseAngle = ResolveTargetAngle(origin, bulletPattern.Target) + Mathf.DegToRad(bulletPattern.BaseAngleOffset);

        directionalData.SetMeta("event_id", bulletPattern.EventId);
        directionalData.SetMeta("enemy_id", bulletPattern.EnemyId);
        ApplyBulletTexture(bulletPattern.TextureId);

        if (bulletPattern.PatternType is PatternType.Straight(var straight))
            SpawnStraight(origin, straight, baseAngle);
        else if (bulletPattern.PatternType is PatternType.Ring(var ring))
            SpawnRing(origin, ring, baseAngle);
        else if (bulletPattern.PatternType is PatternType.Sine(var sine))
            SpawnSine(origin, sine, baseAngle);
        else if (bulletPattern.PatternType is PatternType.Burst(var burst))
            SpawnBurst(origin, burst, baseAngle);
        else if (bulletPattern.PatternType is PatternType.Spiral(var spiral))
            SpawnSpiral(origin, spiral, baseAngle);
        else if (bulletPattern.PatternType is PatternType.Homing(var homing))
            SpawnHoming(origin, homing, baseAngle);
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

    private void SpawnStraight(Vector2 origin, StraightParams p, float baseAngle)
    {
        SetSpeed(p.Speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(baseAngle, origin));
        directionalData.Set("transforms", transforms);
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

    private void SpawnSine(Vector2 origin, SineParams p, float baseAngle)
    {
        SetSpeed(p.Speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = p.Count > 1 ? Mathf.Tau / p.Count : 0f;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(baseAngle + angleStep * i, origin));
        directionalData.Set("transforms", transforms);
        blastBullets.Call("spawn_directional_bullets", directionalData);
    }

    private void SpawnBurst(Vector2 origin, BurstParams p, float baseAngle)
    {
        SetSpeed(p.Speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        float halfSpread = p.Spread * 0.5f;
        float angleStep  = p.Count > 1 ? p.Spread / (p.Count - 1) : 0f;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(baseAngle - halfSpread + angleStep * i, origin));
        directionalData.Set("transforms", transforms);
        blastBullets.Call("spawn_directional_bullets", directionalData);
    }

    private void SpawnSpiral(Vector2 origin, SpiralParams p, float baseAngle)
    {
        SetSpeed(p.Speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = Mathf.Tau / p.Arms;
        for (int i = 0; i < p.Arms; i++)
            transforms.Add(new Transform2D(baseAngle + angleStep * i, origin));
        directionalData.Set("transforms", transforms);
        blastBullets.Call("spawn_directional_bullets", directionalData);
    }

    private void SpawnHoming(Vector2 origin, HomingParams p, float baseAngle)
    {
        SetSpeed(p.Speed);
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = p.Count > 1 ? Mathf.Tau / p.Count : 0f;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(baseAngle + angleStep * i, origin));
        directionalData.Set("transforms", transforms);
        blastBullets.Call("spawn_directional_bullets", directionalData);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null!;
    }
}
