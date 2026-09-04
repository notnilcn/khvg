#nullable enable
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Bullet spawning for the BulletManager entity: owns the enemy/player spawner configs
/// (ClassDB.Instantiate of BlastBullets2D *resources* — not nodes), dispatches
/// BulletPatternEvent rows to the per-pattern spawn methods, and fires player bullets
/// for CombatComponent. Pattern rows arrive through the child BulletPatternEventBinder
/// (declared in bullet_spawner_component.tscn) — ONE binder at BulletManager level,
/// bound before any enemy spawns. The event rows are self-contained (origin_x/y is the
/// enemy's absolute world position at fire time, stamped in enemy/reducers.rs), so
/// dispatch needs no Enemy puppet lookup: the old per-enemy binder fanned every event
/// out to N puppets (N−1 discarded on the id check) AND dropped any volley fired during
/// a puppet's deferred-spawn window — both gone with the single binder.
/// Enemy bullets spawn via spawn_controllable_directional_bullets and
/// the returned DirectionalBullets2D instances are tracked in LiveEnemyBullets so
/// BulletControllerComponent can find and manipulate live bullets (the factory cannot enumerate
/// them). Player bullet instances are tracked the same way in LivePlayerBullets; both
/// dictionaries stamp each instance with a TrackedBulletInfo — its elevation band (the
/// origin hex's level, for the bullet band rule in BulletHitRouterComponent/
/// BulletCliffComponent) plus the projectile texture id (the 3D mirror picks the catalog
/// model from it). The deterministic SplitMix64 jitter mirrors the server's hash technique in
/// enemy/methods.rs, seeded by event_id so every client (and the future audit script)
/// derives identical pellet trajectories (logic moved out of BulletManager.cs).
/// </summary>
public partial class BulletSpawnerComponent : Component
{
    private GodotObject EnemyBulletSpawner = null!;
    private GodotObject PlayerBulletSpawner = null!;
    // Cache hits only — a transient miss must not be cached (see ApplyBulletTexture).
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> bulletTextureCache = [];

    // Live DirectionalBullets2D instances → their elevation band (the origin hex's
    // elevation level at spawn — bullets hit entities within [band − BulletElevationDrop,
    // band + BulletElevationClimb] and die further than BulletElevationClimb above it;
    // see BulletHitRouterComponent/BulletCliffComponent). The factory
    // offers no way to enumerate active instances, so they are captured here for
    // BulletControllerComponent. The dictionary key dedups instances that auto-pooling
    // hands back out for a later spawn (their band is simply re-stamped); dead entries
    // are pruned on the PruneInterval timer and whenever the set grows past
    // PruneThreshold — never per spawn event, or a volley-heavy fight would run an
    // O(live bullets) sweep for every incoming pattern row.
    private readonly System.Collections.Generic.Dictionary<GodotObject, TrackedBulletInfo> liveEnemyBullets = [];
    private readonly System.Collections.Generic.Dictionary<GodotObject, TrackedBulletInfo> livePlayerBullets = [];
    private float pruneTimer;
    private const float PruneInterval = 1f;
    private const int PruneThreshold = 256;

    /// Band + projectile texture stamped onto a live bullet instance; the texture id lets
    /// the 3D mirror pick the catalog model (res_path3d) for the volley.
    public readonly struct TrackedBulletInfo(int band, string textureId)
    {
        public readonly int Band = band;
        public readonly string TextureId = textureId;
    }

    public System.Collections.Generic.IReadOnlyDictionary<GodotObject, TrackedBulletInfo> LiveEnemyBullets => liveEnemyBullets;
    public System.Collections.Generic.IReadOnlyDictionary<GodotObject, TrackedBulletInfo> LivePlayerBullets => livePlayerBullets;

    // Lazily resolved (the facade's static instance isn't set until the game root's
    // _Ready, which runs after this component's OnRegistered).
    private ElevationMapComponent? _elevationMap;
    private ElevationMapComponent? ElevationMap => _elevationMap ??= GameManager.ElevationMap;

    // Arrow.tres art faces up-right (-45 degrees) instead of Vector2.Right, so compensate before BlastBullets2D applies its own trajectory rotation.
    private const float ProjectileTextureAngleOffset = Mathf.Pi / 4f;

    /// The BlastBullets2D factory node, owned by the BulletManager entity root.
    private Node BlastBullets => ((BulletManager)Entity!).BlastBullets;

    private TableBinderComponent bulletPatternEventBinder = null!;

    public override void _Ready()
    {
        base._Ready();
        bulletPatternEventBinder = GetNode<TableBinderComponent>("BulletPatternEventBinder");
    }

    protected override void OnRegistered()
    {
        SetupEnemyBullets();
        SetupPlayerBullets();
    }

    public override void _Process(double delta)
    {
        pruneTimer += (float)delta;
        if (pruneTimer >= PruneInterval || liveEnemyBullets.Count + livePlayerBullets.Count > PruneThreshold)
        {
            pruneTimer = 0f;
            PruneLiveBullets(liveEnemyBullets);
            PruneLiveBullets(livePlayerBullets);
        }
    }

    // --- TableBinderComponent signal handler (wired in bullet_spawner_component.tscn) ---
    // The binder MUST keep ReplayExistingRows = false: there is no last-seen-id tracking,
    // so replaying would re-spawn every bullet pattern in the table's history each time
    // the scene is entered.

    private void OnBulletPatternRowInserted() =>
        SpawnEnemyBullet((BulletPatternEvent)bulletPatternEventBinder.LastRow!);

    // No per-bullet despawn signal exists, so liveness is checked defensively: an instance is
    // dropped once it is freed or has no enabled bullets left. The bulk get_all_bullets_status
    // array is unreliable for multi-bullet instances (a plugin bug — its index is never
    // incremented, so only slot 0 is ever written), so liveness goes through the per-index
    // is_bullet_status_enabled. Shared by the enemy and player tracking dictionaries.
    private static void PruneLiveBullets<T>(System.Collections.Generic.Dictionary<GodotObject, T> live)
    {
        _pruneBuffer.Clear();
        foreach (var inst in live.Keys)
        {
            if (!GodotObject.IsInstanceValid(inst)) { _pruneBuffer.Add(inst); continue; }
            int count = inst.Call("get_amount_bullets").AsInt32();
            bool anyEnabled = false;
            for (int i = 0; i < count; i++)
                if (inst.Call("is_bullet_status_enabled", i).AsBool()) { anyEnabled = true; break; }
            if (!anyEnabled) _pruneBuffer.Add(inst);
        }
        foreach (var inst in _pruneBuffer)
            live.Remove(inst);
    }

    private static readonly System.Collections.Generic.List<GodotObject> _pruneBuffer = [];

    // Elevation band + projectile texture stamped onto every instance the current spawn
    // produces (set by SpawnEnemyBullet/SpawnEnemyBulletFan/SpawnPlayerBullets before the
    // spawn calls, like currentSpeedAcceleration — the spawner resource and this state
    // are shared across spawns). The fan keeps whatever texture the last pattern event
    // applied to the shared enemy spawner, so it leaves currentTextureId untouched.
    private int currentBandElevation;
    private string currentTextureId = "";

    private void SpawnControllable()
    {
        var instance = BlastBullets.Call("spawn_controllable_directional_bullets", EnemyBulletSpawner).AsGodotObject();
        if (instance != null)
        {
            Hide2DIn3DMode(instance);
            liveEnemyBullets[instance] = new TrackedBulletInfo(currentBandElevation, currentTextureId);
        }
    }

    // Hiding the BulletManager node does NOT reach the plugin's pooled bullet renderers
    // (they stay visible as untextured white squares in 3D mode), so each spawned
    // instance is hidden directly; the 3D mirror components take over rendering while
    // the 2D simulation (collision/hit reporting) keeps running.
    private static void Hide2DIn3DMode(GodotObject instance)
    {
        if (GameVisuals.Is3D)
            instance.Set("visible", false);
    }

    private void SetupEnemyBullets()
    {
        EnemyBulletSpawner = ClassDB.Instantiate("DirectionalBulletsData2D").AsGodotObject();
        EnemyBulletSpawner.Set("texture_size", new Vector2(8, 8));
        EnemyBulletSpawner.Call("set_collision_layer_from_array", new Godot.Collections.Array { 2 });
        EnemyBulletSpawner.Call("set_collision_mask_from_array", new Godot.Collections.Array { 2 });
        EnemyBulletSpawner.Set("collision_shape_size", new Vector2(4, 4));
        // Piercing (never auto-despawned on collision): a bullet must survive overlapping
        // off-band players to reach victims on its own elevation band, so the plugin's
        // collision counting can no longer be the hit gate. Single-hit enforcement moved
        // to BulletHitRouterComponent's manual disable_bullet on a band-matched hit.
        EnemyBulletSpawner.Set("bullet_max_collision_count", 0);
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
        // spawn_controllable_directional_bullets (NOT spawn_directional_bullets — only the
        // controllable variant returns the instance) so player bullets are band-tracked
        // exactly like enemy bullets for the cliff cull in BulletCliffComponent.
        var instance = BlastBullets.Call("spawn_controllable_directional_bullets", PlayerBulletSpawner).AsGodotObject();
        if (instance != null)
        {
            Hide2DIn3DMode(instance);
            livePlayerBullets[instance] = new TrackedBulletInfo(ElevationMap?.GetElevationAt(origin) ?? 0, textureId);
        }
    }

    private void ApplyBulletTexture(GodotObject data, string textureId)
    {
        // 3D mode never renders the 2D bullets (BulletManager is hidden by GameManager);
        // don't load their textures. Physics/collision are unaffected.
        if (GameVisuals.Is3D) return;
        if (bulletTextureCache.TryGetValue(textureId, out var tex))
        {
            data.Call("set_textures", new Godot.Collections.Array<Texture2D> { tex });
            return;
        }
        var resPath = GameManager.GetResPath2D(textureId);
        var frame = resPath != null ? GD.Load<SpriteFrames>(resPath)?.GetFrameTexture("default", 0) : null;
        tex = frame != null ? ImageTexture.CreateFromImage(frame.GetImage()) : null;
        // A transient miss (e.g. the texture row hasn't arrived) must NOT be cached — a
        // cached null would poison the id for the rest of the process. Retry next spawn
        // instead (TerrainComponent.GetTexture does the same).
        if (tex == null)
        {
            GD.PushWarning($"[BulletSpawnerComponent] bullet texture '{textureId}' unresolved — bullets spawn with the previous texture; will retry on next spawn");
            return;
        }
        bulletTextureCache[textureId] = tex;
        data.Call("set_textures", new Godot.Collections.Array<Texture2D> { tex });
    }

    public void SpawnEnemyBullet(BulletPatternEvent bulletPattern)
    {
        var origin = new Vector2(bulletPattern.OriginX, bulletPattern.OriginY) + new Vector2(bulletPattern.OriginOffsetX, bulletPattern.OriginOffsetY);
        var baseAngle = ResolveTargetAngle(origin, bulletPattern.Target) + Mathf.DegToRad(bulletPattern.BaseAngleOffset);

        // The volley's elevation band: the origin hex's level, stamped onto every tracked
        // instance this event spawns (shared spawn state, like currentSpeedAcceleration).
        currentBandElevation = ElevationMap?.GetElevationAt(origin) ?? 0;
        currentTextureId = bulletPattern.TextureId;
        EnemyBulletSpawner.Set("bullets_custom_data", new BulletData { SourceStep = bulletPattern.SourceStep, EnemyId = bulletPattern.EnemyId, Damage = bulletPattern.Damage });
        EnemyBulletSpawner.Set("max_life_time", bulletPattern.Lifetime);
        ApplyBulletTexture(EnemyBulletSpawner, bulletPattern.TextureId);
        ApplyMotion(bulletPattern.AngularSpeed, bulletPattern.SpeedAcceleration, bulletPattern.Lifetime);

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

    // Curving for enemy bullets (server-baked per event; 0 = off): BlastBullets2D rotation
    // data + adjust_direction_based_on_rotation makes each bullet re-derive its velocity from
    // its rotating transform every physics frame. Both are re-set on every event because the
    // spawner resource is shared across spawns — the size-0 array is the plugin's
    // "rotation disabled" path, so a straight event never inherits a curving one's data.
    // Acceleration rides along in SetSpeed.
    private void ApplyMotion(float angularSpeedDeg, float speedAcceleration, float lifetime)
    {
        float angularSpeed = Mathf.DegToRad(angularSpeedDeg);
        EnemyBulletSpawner.Set("adjust_direction_based_on_rotation", angularSpeed != 0f);
        if (angularSpeed != 0f)
        {
            var rotationData = ClassDB.Instantiate("BulletRotationData2D").AsGodotObject()
                .Call("generate_random_data", 1, angularSpeed, angularSpeed, Mathf.Abs(angularSpeed), Mathf.Abs(angularSpeed), 0, 0);
            EnemyBulletSpawner.Set("all_bullet_rotation_data", rotationData);
        }
        else
        {
            EnemyBulletSpawner.Set("all_bullet_rotation_data", new Godot.Collections.Array());
        }
        // Stashed for SetSpeed, which each Spawn* method calls with its own base speed.
        currentSpeedAcceleration = speedAcceleration;
        currentMaxSpeedBonus = speedAcceleration > 0f ? speedAcceleration * lifetime : 0f;
    }

    private float currentSpeedAcceleration;
    private float currentMaxSpeedBonus;

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
        // The plugin clamps speed with a plain min() against max_speed, so acceleration gets
        // an explicit ceiling that never binds early; deceleration (accel < 0) never binds.
        float maxSpeed = speed + currentMaxSpeedBonus;
        EnemyBulletSpawner.Set("all_bullet_speed_data", ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject().Call("generate_random_data", 1, speed, speed, maxSpeed, maxSpeed, currentSpeedAcceleration, currentSpeedAcceleration));
    }

    // One exact (min == max, so the "random" range is a constant) BulletSpeedData2D entry
    // for a batched multi-bullet spawn: the plugin consumes all_bullet_speed_data per bullet
    // index when the array length matches the bullet count, letting each pellet of a single
    // instance keep its own jittered speed. Same acceleration ceiling as SetSpeed.
    private GodotObject MakeSpeedDataEntry(float speed)
    {
        float maxSpeed = speed + currentMaxSpeedBonus;
        return ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject()
            .Call("generate_random_data", 1, speed, speed, maxSpeed, maxSpeed, currentSpeedAcceleration, currentSpeedAcceleration)
            .AsGodotArray<GodotObject>()[0];
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

    // One instance per pellet: Shotgun/Explosion jitter each pellet's lifetime and the
    // plugin's max_life_time is a single scalar per instance (no per-bullet lifetime data
    // exists), so these pellets cannot share an instance without the attach-timer +
    // disable_bullet workaround — not worth the complexity next to the volley batching.
    private void SpawnSingle(Vector2 origin, float angle, float speed, float lifetime)
    {
        EnemyBulletSpawner.Set("max_life_time", lifetime);
        SetSpeed(speed);
        EnemyBulletSpawner.Set("transforms", new Godot.Collections.Array<Transform2D> { BulletTransform(angle, origin) });
        SpawnControllable();
    }

    private void SpawnRing(Vector2 origin, RingParams p, float baseAngle)
    {
        var transforms = new Godot.Collections.Array<Transform2D>();
        float angleStep = Mathf.Tau / p.Count;
        for (int i = 0; i < p.Count; i++)
            transforms.Add(BulletTransform(baseAngle + angleStep * i, origin));
        // DanmakU Line composition: stack copy j flies at speed + step * (j + 1), stringing
        // the copies out into a line as they travel. One spawn call per stack level.
        int line = System.Math.Max(1, (int)p.Line);
        for (int j = 1; j <= line; j++)
        {
            SetSpeed(p.Speed + p.LineSpeedStep * j);
            EnemyBulletSpawner.Set("transforms", transforms);
            SpawnControllable();
        }
    }

    // Each Line stack level is ONE instance of Count bullets: per-pellet angle jitter lives
    // in the per-index transforms and per-pellet speed jitter in the per-index speed data
    // (exact values, so trajectories are identical to the old one-instance-per-pellet
    // spawn), and every pellet in the event shares one lifetime. A 20-pellet ×3-line volley
    // is 3 tracked instances instead of 60 — every prune, ability query and 3D-mirror pass
    // scales with the instance count.
    private void SpawnVolley(Vector2 origin, VolleyParams p, float baseAngle, ulong eventId, float lifetime)
    {
        if (p.Count == 0) return;
        EnemyBulletSpawner.Set("max_life_time", lifetime);
        int line = System.Math.Max(1, (int)p.Line);
        for (int j = 1; j <= line; j++)
        {
            var transforms = new Godot.Collections.Array<Transform2D>();
            var speedData = new Godot.Collections.Array<GodotObject>();
            for (uint i = 0; i < p.Count; i++)
            {
                transforms.Add(BulletTransform(baseAngle + Jitter(eventId, i, 1, p.AngleJitter), origin));
                speedData.Add(MakeSpeedDataEntry(p.Speed + Jitter(eventId, i, 0, p.SpeedVariance) + p.LineSpeedStep * j));
            }
            EnemyBulletSpawner.Set("transforms", transforms);
            EnemyBulletSpawner.Set("all_bullet_speed_data", speedData);
            SpawnControllable();
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
        int line = System.Math.Max(1, (int)p.Line);
        for (uint i = 0; i < p.Count; i++)
        {
            float angle = baseAngle - halfSpread + angleStep * i;
            float speed = p.Speed + Jitter(eventId, i, 0, p.SpeedVariance);
            float pelletLifetime = lifetime + Jitter(eventId, i, 1, p.LifetimeVariance);
            for (int j = 1; j <= line; j++)
                SpawnSingle(origin, angle, speed + p.LineSpeedStep * j, pelletLifetime);
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
        // Player-ability spawn, not a server pattern event — no motion modifiers. The
        // band is the fan's own origin hex, same rule as a pattern event's origin.
        currentSpeedAcceleration = 0f;
        currentMaxSpeedBonus = 0f;
        currentBandElevation = ElevationMap?.GetElevationAt(origin) ?? 0;
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
