#nullable enable
using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Player abilities that manipulate live enemy bullets near a point: DeleteNear, SlowNear,
/// SplitNear and AttractNear (blackhole-style homing). All four share the FindBulletsNear
/// proximity query over the live DirectionalBullets2D instances tracked by
/// BulletSpawnerComponent, which is why they live in one component. Everything here is
/// client-local — no reducers — consistent with the self-reported report_hit trust model.
/// Triggered for now by debug hotkeys (ability_delete/slow/split/attract, keys 6-9) polled
/// in _Process; the public methods are the real API for a future ability system.
/// </summary>
public partial class BulletAbilityComponent : Component
{
    private const float Radius = 120f;
    private const float SlowFactor = 0.35f;
    private const float DefaultSlowSpeed = 100f;
    private const int SplitCount = 4;
    private const float SplitSpread = Mathf.Pi / 3f;
    private const float SplitLifetime = 3f;
    private const float SplitDefaultSpeed = 150f;
    private const float HomingSmoothing = 5f;

    private BulletSpawnerComponent spawner = null!;

    protected override Type[] GetRequiredComponents() => [typeof(BulletSpawnerComponent)];

    protected override void OnEntityReady() => spawner = GetSibling<BulletSpawnerComponent>()!;

    public override void _Process(double delta)
    {
        bool delete = Input.IsActionJustPressed("ability_delete");
        bool slow = Input.IsActionJustPressed("ability_slow");
        bool split = Input.IsActionJustPressed("ability_split");
        bool attract = Input.IsActionJustPressed("ability_attract");
        if (!delete && !slow && !split && !attract) return;

        var playerPos = GetLocalPlayerPosition();
        if (playerPos == null) return;

        if (delete) DeleteNear(playerPos.Value, Radius);
        if (slow) SlowNear(playerPos.Value, Radius, SlowFactor);
        if (split) SplitNear(playerPos.Value, Radius);
        if (attract) AttractNear(playerPos.Value, Radius, ((BulletManager)Entity!).GetGlobalMousePosition());
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

    /// Permanently rescales the speed of nearby bullets (no restore timer in v1).
    public void SlowNear(Vector2 point, float radius, float factor)
    {
        foreach (var (inst, index) in FindBulletsNear(point, radius))
        {
            float speed = GetBulletSpeed(inst, index) ?? DefaultSlowSpeed;
            var slowed = ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject()
                .Call("generate_random_data", 1, speed * factor, speed * factor, speed * factor, speed * factor, 0, 0);
            inst.Call("set_bullet_speed_data", index, slowed);
        }
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
