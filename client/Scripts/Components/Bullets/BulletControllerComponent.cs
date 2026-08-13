#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Player abilities that manipulate live enemy bullets near a point: DeleteNear, SplitNear
/// and AttractNear (blackhole-style homing). All three share the FindBulletsNear proximity
/// query over the live DirectionalBullets2D instances tracked by BulletSpawnerComponent,
/// which is why they live in one component.
/// Casts are networked by relaying the cast itself: the caster applies the effect locally
/// (optimistic) and the server appends a BulletControlEvent row; every other client applies
/// the same effect on row insert via the child BulletControlEventBinder (own echoes are
/// skipped via cast_by). Each client resolves the proximity query against its own bullets,
/// so edge-of-radius results can differ slightly.
/// Driven by the real ability system: ability items with a DeleteBullets/SplitBullets/
/// AttractBullets AbilityEffect call the public methods below from
/// LocalPlayerInventoryComponent.TryActivateAbility, and activate_ability appends the
/// event server-side (radius from the item, cursor target clamped to cast range).
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

    /// BulletControlEventBinder RowInserted handler (wired in characters.tscn). Replays another
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
