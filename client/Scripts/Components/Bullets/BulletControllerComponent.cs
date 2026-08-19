#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Player abilities that manipulate live enemy bullets near a point: DeleteNear, SplitNear,
/// AttractNear (blackhole-style homing) and Slash (a rectangle from the caster toward the
/// cursor that deletes every bullet inside it). All four share the proximity queries over
/// the live DirectionalBullets2D instances tracked by BulletSpawnerComponent, which is why
/// they live in one component.
/// Casts are networked by relaying the cast itself: the caster applies the effect locally
/// (optimistic) and the server appends a BulletControlEvent row; every other client applies
/// the same effect on row insert via the child BulletControlEventBinder (own echoes are
/// skipped via cast_by). Each client resolves the proximity query against its own bullets,
/// so edge-of-radius results can differ slightly. Slash events encode the rectangle as
/// origin (x, y) + far end (target_x, target_y) + width (radius); every client also draws
/// the black slash rectangle, so the visual replicates with the cast.
/// Driven by the real ability system: ability items with a DeleteBullets/SplitBullets/
/// AttractBullets/SlashBullets AbilityEffect call the public methods below from
/// LocalPlayerInventoryComponent.TryActivateAbility, and activate_ability appends the
/// event server-side (radius/dims from the item, cursor target clamped to cast range).
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

    /// BulletControlEventBinder RowInserted handler (wired in game.tscn). Replays another
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
        else if (row.Kind is BulletControlKind.Slash)
            Slash(point, new Vector2(row.TargetX, row.TargetY), row.Radius);
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

    /// Every enabled live enemy bullet (instance, index) inside the rectangle from origin
    /// to end with the given full width (the slash's hitbox). Same torus-naive iteration
    /// shape as FindBulletsNear.
    private IEnumerable<(GodotObject Instance, int Index)> FindBulletsInRect(Vector2 origin, Vector2 end, float width)
    {
        var axis = end - origin;
        float length = axis.Length();
        if (length < 0.001f) yield break;
        var dir = axis / length;
        float halfWidth = width * 0.5f;
        foreach (var inst in spawner.LiveEnemyBullets)
        {
            if (!GodotObject.IsInstanceValid(inst)) continue;
            int count = inst.Call("get_amount_bullets").AsInt32();
            if (count == 0) continue;
            var transforms = inst.Call("all_bullets_get_transforms").AsGodotArray<Transform2D>();
            for (int i = 0; i < count && i < transforms.Count; i++)
            {
                if (!inst.Call("is_bullet_status_enabled", i).AsBool()) continue;
                var rel = transforms[i].Origin - origin;
                float along = rel.Dot(dir);
                if (along < 0f || along > length) continue;
                float across = Mathf.Abs(rel.Dot(dir.Orthogonal()));
                if (across <= halfWidth)
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

    /// Deletes every bullet inside the slash rectangle and draws it. Runs on the caster
    /// (optimistic) and on every other client via the BulletControlEvent echo, so the
    /// black rectangle replicates with the cast.
    public void Slash(Vector2 origin, Vector2 end, float width)
    {
        foreach (var (inst, index) in FindBulletsInRect(origin, end, width))
            inst.Call("disable_bullet", index);
        SpawnSlashVisual(origin, end, width);
    }

    /// The black slash rectangle, drawn for everyone who sees the cast: a code-created
    /// Polygon2D (data-driven count, like HitZones) under the parent BulletManager
    /// (z_index 1 keeps it above entities), flashed briefly then freed.
    private void SpawnSlashVisual(Vector2 origin, Vector2 end, float width)
    {
        var axis = end - origin;
        if (axis.Length() < 0.001f) return;
        var offset = axis.Orthogonal().Normalized() * (width * 0.5f);
        var rect = new Polygon2D
        {
            Polygon = [origin - offset, origin + offset, end + offset, end - offset],
            Color = new Color(0f, 0f, 0f, 0.85f),
        };
        GetParent().AddChild(rect);
        var tween = rect.CreateTween();
        tween.TweenProperty(rect, "modulate:a", 0f, 0.2);
        tween.TweenCallback(Callable.From(rect.QueueFree));
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
