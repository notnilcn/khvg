#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class BulletManager : Node
{
    public static BulletManager Instance { get; private set; } = null!;

    [Export] public Texture2D[] BulletTextures { get; set; } = System.Array.Empty<Texture2D>();

    private Node _blastBullets = null!;

    private GodotObject _directionalData = null!;

    public override void _Ready()
    {
        Instance = this;
        _blastBullets = GetParent();
        _SetupDirectionalBullets();
    }

    private void _SetupDirectionalBullets()
    {
        _directionalData = ClassDB.Instantiate("DirectionalBulletsData2D").AsGodotObject();
        _directionalData.Call("set_textures", BulletTextures);
        _directionalData.Set("texture_size", new Vector2(64, 64));
        _directionalData.Call("set_collision_layer_from_array", new Godot.Collections.Array { 3 });
        _directionalData.Call("set_collision_mask_from_array", new Godot.Collections.Array { 2 });
        _directionalData.Set("collision_shape_size", new Vector2(16, 16));
        _directionalData.Set("all_bullet_speed_data",
            ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject()
                .Call("generate_random_data", 1, 200, 200, 300, 300, 0, 0));
    }

    public void Spawn(BulletPatternEvent evt)
    {
        var origin = new Vector2(evt.OriginX, evt.OriginY);
        var pattern = evt.PatternType;

        if (pattern is PatternType.Straight(var straight))
            _SpawnStraight(origin, straight);
        else if (pattern is PatternType.Ring(var ring))
            _SpawnRing(origin, ring);
        else if (pattern is PatternType.Sine(var sine))
            _SpawnSine(origin, sine);
        else if (pattern is PatternType.Burst(var burst))
            _SpawnBurst(origin, burst);
        else if (pattern is PatternType.Spiral(var spiral))
            _SpawnSpiral(origin, spiral);
        else if (pattern is PatternType.Homing(var homing))
            _SpawnHoming(origin, homing);
    }

    public void SpawnDirectional(Vector2 origin, Vector2 direction)
    {
        var transforms = new Godot.Collections.Array<Transform2D>();
        var angle = direction.Angle();
        transforms.Add(new Transform2D(angle, origin));
        _directionalData.Set("transforms", transforms);
        _blastBullets.Call("spawn_directional_bullets", _directionalData);
    }

    private void _SpawnStraight(Vector2 origin, StraightParams p)
    {
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = p.Count > 1 ? Mathf.Tau / p.Count : 0f;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(angleStep * i, origin));
        _directionalData.Set("transforms", transforms);
        _blastBullets.Call("spawn_directional_bullets", _directionalData);
    }

    private void _SpawnRing(Vector2 origin, RingParams p)
    {
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = Mathf.Tau / p.Count;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(angleStep * i, origin));
        _directionalData.Set("transforms", transforms);
        _blastBullets.Call("spawn_directional_bullets", _directionalData);
    }

    private void _SpawnSine(Vector2 origin, SineParams p)
    {
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = p.Count > 1 ? Mathf.Tau / p.Count : 0f;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(angleStep * i, origin));
        _directionalData.Set("transforms", transforms);
        _blastBullets.Call("spawn_directional_bullets", _directionalData);
    }

    private void _SpawnBurst(Vector2 origin, BurstParams p)
    {
        var transforms = new Godot.Collections.Array<Transform2D>();
        float halfSpread = p.Spread * 0.5f;
        float angleStep = p.Count > 1 ? p.Spread / (p.Count - 1) : 0f;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(-halfSpread + angleStep * i, origin));
        _directionalData.Set("transforms", transforms);
        _blastBullets.Call("spawn_directional_bullets", _directionalData);
    }

    private void _SpawnSpiral(Vector2 origin, SpiralParams p)
    {
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = Mathf.Tau / p.Arms;
        for (int i = 0; i < p.Arms; i++)
            transforms.Add(new Transform2D(angleStep * i, origin));
        _directionalData.Set("transforms", transforms);
        _blastBullets.Call("spawn_directional_bullets", _directionalData);
    }

    private void _SpawnHoming(Vector2 origin, HomingParams p)
    {
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = p.Count > 1 ? Mathf.Tau / p.Count : 0f;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(new Transform2D(angleStep * i, origin));
        _directionalData.Set("transforms", transforms);
        _blastBullets.Call("spawn_directional_bullets", _directionalData);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null!;
    }
}
