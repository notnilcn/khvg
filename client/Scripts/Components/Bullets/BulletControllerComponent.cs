#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Player abilities that manipulate live enemy bullets near a point: DeleteNear, SplitNear
/// and AttractNear (blackhole-style homing). All four share the FindBulletsNear proximity
/// query over the live DirectionalBullets2D instances tracked by BulletSpawnerComponent,
/// which is why they live in one component.
/// Casts are networked by relaying the cast itself: the caster applies the effect locally
/// (optimistic) and calls the control_bullets reducer, which appends a BulletControlEvent
/// row; every other client applies the same effect on row insert via the child
/// BulletControlEventBinder (own echoes are skipped via cast_by). Each client resolves the
/// proximity query against its own bullets, so edge-of-radius results can differ slightly.
/// Triggered for now by debug hotkeys (ability_delete/split/attract, keys 6-9) polled
/// in _Process; the public methods are the real API for a future ability system.
/// </summary>
public partial class BulletControllerComponent : Component
{
    [Export] public float Radius = 120f;
    [Export] public int SplitCount = 4;
    [Export] public float SplitSpread = Mathf.Pi / 3f;
    [Export] public float SplitLifetime = 3f;
    [Export] public float SplitDefaultSpeed = 150f;
    [Export] public float HomingSmoothing = 5f;

    private BulletSpawnerComponent spawner = null!;
    private TableBinderComponent bulletControlEventBinder = null!;

    protected override Type[] GetRequiredComponents() => [typeof(BulletSpawnerComponent)];

    public override void _Ready()
    {
        base._Ready();
        bulletControlEventBinder = GetNode<TableBinderComponent>("BulletControlEventBinder");
    }

    protected override void OnEntityReady() => spawner = GetSibling<BulletSpawnerComponent>()!;

    public override void _Process(double delta)
    {
        bool delete = Input.IsActionJustPressed("ability_delete");
        bool split = Input.IsActionJustPressed("ability_split");
        bool attract = Input.IsActionJustPressed("ability_attract");
        if (!delete && !split && !attract) return;

        var playerPos = GetLocalPlayerPosition();
        if (playerPos == null) return;
        var point = playerPos.Value;
        var target = ((BulletManager)Entity!).GetGlobalMousePosition();

        // Optimistic local apply; remote clients apply on the BulletControlEvent echo.
        if (delete) DeleteNear(point, Radius);
        if (split) SplitNear(point, Radius);
        if (attract) AttractNear(point, Radius, target);

        var conn = GameManager.Conn;
        if (conn == null) return; // offline debug use stays local-only
        if (delete) conn.Reducers.ControlBullets(BulletControlKind.Delete, point.X, point.Y, Radius, 0f, 0f);
        if (split) conn.Reducers.ControlBullets(BulletControlKind.Split, point.X, point.Y, Radius, 0f, 0f);
        if (attract) conn.Reducers.ControlBullets(BulletControlKind.Attract, point.X, point.Y, Radius, target.X, target.Y);
    }

    /// BulletControlEventBinder RowInserted handler (wired in main.tscn). Replays another
    /// player's cast locally; the caster's own echo is skipped (already applied optimistically).
    private void OnBulletControlEventRow()
    {
        var row = (BulletControlEvent)bulletControlEventBinder.LastRow!;
        if (GameManager.IsLocal(row.CastBy)) return;
        var point = new Vector2(row.X, row.Y);
        if (row.Kind is BulletControlKind.Delete) DeleteNear(point, row.Radius);
        else if (row.Kind is BulletControlKind.Split) SplitNear(point, row.Radius);
        else if (row.Kind is BulletControlKind.Attract)
            AttractNear(point, row.Radius, new Vector2(row.TargetX, row.TargetY));
    }

    private static Vector2? GetLocalPlayerPosition()
    {
        var conn = GameManager.Conn;
        if (conn == null) return null;
        foreach (var row in conn.Db.LocalPlayerPosition.Iter())
            return new Vector2(row.X, row.Y);
        return null;
    }

    /// Every enabled live enemy bullet (instance, index) whose position is within radius of point.
    private IEnumerable<(GodotObject Instance, int Index)> FindBulletsNear(Vector2 point, float radius)
    {
        float radiusSq = radius * radius;
        foreach (var inst in spawner.LiveEnemyBullets)
        {
            if (!GodotObject.IsInstanceValid(inst)) continue;
            int count = inst.Call("get_amount_bullets").AsInt32();
            if (count == 0) continue;
            var transforms = inst.Call("all_bullets_get_transforms").AsGodotArray<Transform2D>();
            for (int i = 0; i < count && i < transforms.Count; i++)
            {
                if (!inst.Call("is_bullet_status_enabled", i).AsBool()) continue;
                if (point.DistanceSquaredTo(transforms[i].Origin) <= radiusSq)
                    yield return (inst, i);
            }
        }
    }

    /// Current speed of one bullet, or null when the plugin's BulletSpeedData2D can't be read.
    private static float? GetBulletSpeed(GodotObject inst, int index)
    {
        var speedData = inst.Call("get_bullet_speed_data", index).AsGodotObject();
        if (speedData == null) return null;
        var value = speedData.Get("speed");
        return value.VariantType == Variant.Type.Nil ? null : value.AsSingle();
    }

    public void DeleteNear(Vector2 point, float radius)
    {
        foreach (var (inst, index) in FindBulletsNear(point, radius))
            inst.Call("disable_bullet", index);
    }

    /// Disables each nearby bullet and respawns it as a fan of SplitCount pellets around its
    /// direction. Hits are collected first because the query must not run while mutating.
    public void SplitNear(Vector2 point, float radius)
    {
        var hits = new List<(GodotObject Inst, int Index)>(FindBulletsNear(point, radius));
        foreach (var (inst, index) in hits)
        {
            if (!GodotObject.IsInstanceValid(inst)) continue;
            if (!inst.Call("is_bullet_status_enabled", index).AsBool()) continue;
            var transform = inst.Call("get_bullet_transform", index).AsTransform2D();
            float speed = GetBulletSpeed(inst, index) ?? SplitDefaultSpeed;
            var customData = inst.Get("bullets_custom_data").As<BulletData>();
            inst.Call("disable_bullet", index);
            spawner.SpawnEnemyBulletFan(transform.Origin, transform.Rotation, SplitCount, SplitSpread, speed, SplitLifetime, customData);
        }
    }

    /// Pushes a global-position homing target (the blackhole) onto every nearby bullet.
    public void AttractNear(Vector2 point, float radius, Vector2 target)
    {
        foreach (var (inst, index) in FindBulletsNear(point, radius))
        {
            inst.Set("homing_smoothing", HomingSmoothing);
            inst.Set("homing_update_interval", 0.0);
            inst.Set("homing_take_control_of_texture_rotation", true);
            inst.Call("bullet_homing_push_back_global_position_target", index, target);
        }
    }
}
