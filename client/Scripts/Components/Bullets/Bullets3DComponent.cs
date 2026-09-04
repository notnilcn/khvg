#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// 3D mirror of ALL live bullets (enemy and player), child in world_3d.tscn (registers
/// with the GameManager entity through the SubViewport ancestor walk, like
/// HexGridOverlay3DComponent). On each sync tick (SyncHz — throttled off the per-frame
/// path: every tick costs an untyped interop Call per bullet plus a full transform-array
/// alloc per instance, scaling with exactly the quantity a bullet-hell maximizes) it
/// enumerates the enabled bullets of every DirectionalBullets2D instance tracked by
/// BulletSpawnerComponent.LiveEnemyBullets/LivePlayerBullets and lays pooled sets of
/// bullet instances over their positions — the BlastBullets2D factory renders only into
/// the 2D world, so without this the 3D viewport shows no fire. Bullet transforms are
/// read through untyped Call(…) (the factory is a GDExtension node — no compile-time
/// checking, same as BulletSpawnerComponent/BulletControllerComponent). The plugin's
/// all_bullets_get_transforms always allocates a fresh array (no caller-buffer overload
/// exists), so the alloc per instance per tick is unavoidable.
/// Every volley's model is catalog-driven: the instance's stamped projectile texture id
/// (TrackedBulletInfo.TextureId) resolves to the texture's res_path3d (arrow_bow.fbx for
/// the "Arrow" texture), with the exported BulletScene (hex_bullet.tscn) as the fallback
/// when the texture has no 3D path. Each model scene gets its own pool — volleys of
/// different weapons/patterns stay on their own models with no cross-pool contamination.
/// A bullet's Y is its instance's elevation band (the firing entity's origin hex level,
/// band-stamped by BulletSpawnerComponent) × WorldHeightPerLevel + BulletHeight.
/// </summary>
public partial class Bullets3DComponent : Node3DComponent
{
    /// Fallback bullet scene (hex_bullet.tscn) for projectile textures with no res_path3d.
    [Export] public PackedScene? BulletScene { get; set; }

    /// Uniform scale applied to fallback-scene instances (the hex mesh's native outer radius is ~1 unit).
    [Export] public float BulletScale { get; set; } = 8f;

    /// Uniform scale applied to catalog model instances (KayKit FBX imports are near-unit-sized).
    [Export] public float ModelScale { get; set; } = 30f;

    /// Extra yaw (radians) aligning a catalog model's long axis with the travel direction.
    [Export] public float ModelYawOffset { get; set; }

    /// Height above the firing entity's elevation band the bullets hover at.
    [Export] public float BulletHeight { get; set; } = 20f;

    /// Sync rate in Hz — 30 Hz is visually indistinguishable from per-frame for the 3D
    /// backdrop and halves the untyped interop cost of the mirror.
    [Export] public float SyncHz { get; set; } = 30f;

    // One flat pool PER MODEL SCENE, reused every tick: after Reset the first Next() calls
    // track live bullets, the rest stay hidden on End. Grows on demand, never shrinks
    // (bullet counts spike and settle). Keying pools by scene means a weapon swap (or two
    // enemy templates with different projectile textures) never forces a rebuild — the
    // old single-pool design QueueFree'd and rebuilt mid-tick whenever two live volleys
    // had different models, which stranded one volley on the fallback scene.
    private sealed class NodePool(Node3D parent, PackedScene scene, float scale)
    {
        private readonly List<Node3D> _nodes = [];
        private int _used;

        public void Reset() => _used = 0;

        public Node3D Next()
        {
            if (_used == _nodes.Count)
            {
                var node = scene.Instantiate<Node3D>();
                node.Scale = Vector3.One * scale;
                parent.AddChild(node);
                _nodes.Add(node);
            }
            var next = _nodes[_used++];
            next.Visible = true;
            return next;
        }

        public void End()
        {
            for (int i = _used; i < _nodes.Count; i++)
                _nodes[i].Visible = false;
        }
    }

    private readonly Dictionary<PackedScene, NodePool> _pools = [];
    private float _syncTimer;

    // Projectile models are catalog-driven (the texture's res_path3d), cached per texture
    // id. Transient misses (the texture row hasn't arrived) are NOT cached — the fallback
    // scene is used until the row lands (the ApplyBulletTexture rule).
    private readonly Dictionary<string, PackedScene> _modelCache = [];

    // Cached at first sight (BulletManager readies after this component) — the per-frame
    // GetComponent lookup this replaces was part of the hot path.
    private BulletSpawnerComponent? _spawner;

    public override void _Process(double delta)
    {
        _syncTimer += (float)delta;
        if (_syncTimer < 1f / SyncHz) return;
        _syncTimer = 0f;

        _spawner ??= BulletScene != null ? BulletManager.Instance?.GetComponent<BulletSpawnerComponent>() : null;
        if (_spawner == null) return;

        foreach (var pool in _pools.Values)
            pool.Reset();
        foreach (var (inst, info) in _spawner.LiveEnemyBullets)
            SyncInstance(inst, info);
        foreach (var (inst, info) in _spawner.LivePlayerBullets)
            SyncInstance(inst, info);
        foreach (var pool in _pools.Values)
            pool.End();
    }

    /// The projectile texture's catalog 3D model; null when it has none (or the row
    /// hasn't arrived yet) — the caller falls back to BulletScene.
    private PackedScene? ModelFor(string textureId)
    {
        if (_modelCache.TryGetValue(textureId, out var cached))
            return cached;
        var resPath = GameManager.GetResPath3D(textureId);
        if (resPath == null)
            return null;
        var scene = GD.Load<PackedScene>(resPath);
        if (scene != null)
            _modelCache[textureId] = scene;
        return scene;
    }

    private void SyncInstance(GodotObject inst, BulletSpawnerComponent.TrackedBulletInfo info)
    {
        if (!GodotObject.IsInstanceValid(inst)) return;
        int count = inst.Call("get_amount_bullets").AsInt32();
        if (count == 0) return;

        var model = ModelFor(info.TextureId);
        var scene = model ?? BulletScene!;
        if (!_pools.TryGetValue(scene, out var pool))
        {
            pool = new NodePool(this, scene, model != null ? ModelScale : BulletScale);
            _pools[scene] = pool;
        }
        float yawOffset = model != null ? ModelYawOffset : 0f;
        float y = info.Band * ElevationMapComponent.WorldHeightPerLevel + BulletHeight;

        // NOTE: the bulk get_all_bullets_status array is unreliable for multi-bullet
        // instances (a plugin bug — its index is never incremented, so only slot 0 is ever
        // written) — liveness must go through the per-index is_bullet_status_enabled, same
        // as BulletControllerComponent.
        var transforms = inst.Call("all_bullets_get_transforms").AsGodotArray<Transform2D>();
        for (int i = 0; i < count && i < transforms.Count; i++)
        {
            if (!inst.Call("is_bullet_status_enabled", i).AsBool()) continue;
            var node = pool.Next();
            var t = transforms[i];
            node.GlobalPosition = new Vector3(t.Origin.X, y, t.Origin.Y);
            // 2D rotation is clockwise in a y-down space; 3D yaw about +Y is counter-clockwise.
            node.Rotation = new Vector3(0f, -t.Rotation + yawOffset, 0f);
        }
    }
}
