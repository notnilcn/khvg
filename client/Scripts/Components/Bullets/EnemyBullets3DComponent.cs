#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// 3D mirror of the live enemy bullets, child in world_3d.tscn (registers with the
/// GameManager entity through the SubViewport ancestor walk, like HexGridOverlay3DComponent).
/// Each frame it enumerates the enabled bullets of every DirectionalBullets2D instance
/// tracked by BulletSpawnerComponent.LiveEnemyBullets and lays a pooled set of BulletScene
/// instances over their positions — the BlastBullets2D factory renders only into the 2D
/// world, so without this the 3D viewport shows no enemy fire. Bullet transforms are read
/// through untyped Call(…) (the factory is a GDExtension node — no compile-time checking,
/// same as BulletSpawnerComponent/BulletControllerComponent).
/// </summary>
public partial class EnemyBullets3DComponent : Node3DComponent
{
    /// Scene instanced per live bullet (hex_bullet.tscn).
    [Export] public PackedScene? BulletScene { get; set; }

    /// Uniform scale applied to each bullet instance (the hex mesh's native outer radius is ~1 unit).
    [Export] public float BulletScale { get; set; } = 8f;

    /// Height above the ground plane the bullets hover at.
    [Export] public float BulletHeight { get; set; } = 20f;

    // Flat pool reused every frame: the first _used entries track live bullets, the rest
    // stay hidden. Grows on demand, never shrinks (bullet counts spike and settle).
    private readonly List<Node3D> _pool = [];
    private int _used;

    public override void _Process(double delta)
    {
        _used = 0;
        var spawner = BulletScene != null ? BulletManager.Instance?.GetComponent<BulletSpawnerComponent>() : null;
        if (spawner != null)
            foreach (var inst in spawner.LiveEnemyBullets)
                SyncInstance(inst);
        HideUnused();
    }

    private void SyncInstance(GodotObject inst)
    {
        if (!GodotObject.IsInstanceValid(inst)) return;
        int count = inst.Call("get_amount_bullets").AsInt32();
        if (count == 0) return;
        // NOTE: the bulk get_all_bullets_status array is unreliable for multi-bullet
        // instances (only index 0 reports true) — liveness must go through the per-index
        // is_bullet_status_enabled, same as BulletControllerComponent.
        var transforms = inst.Call("all_bullets_get_transforms").AsGodotArray<Transform2D>();
        for (int i = 0; i < count && i < transforms.Count; i++)
        {
            if (!inst.Call("is_bullet_status_enabled", i).AsBool()) continue;
            var node = NextNode();
            var t = transforms[i];
            node.GlobalPosition = new Vector3(t.Origin.X, BulletHeight, t.Origin.Y);
            // 2D rotation is clockwise in a y-down space; 3D yaw about +Y is counter-clockwise.
            node.Rotation = new Vector3(0f, -t.Rotation, 0f);
        }
    }

    private Node3D NextNode()
    {
        if (_used == _pool.Count)
        {
            var node = BulletScene!.Instantiate<Node3D>();
            node.Scale = Vector3.One * BulletScale;
            AddChild(node);
            _pool.Add(node);
        }
        var next = _pool[_used++];
        next.Visible = true;
        return next;
    }

    private void HideUnused()
    {
        for (int i = _used; i < _pool.Count; i++)
            _pool[i].Visible = false;
    }
}
