#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Cliff-face culling for live bullets, child of the BulletManager entity (declared
/// inline in game.tscn): on a slow timer, every tracked bullet instance (enemy and
/// player, band-stamped by BulletSpawnerComponent) is swept — a bullet over a hex with
/// elevation MORE THAN BULLET_ELEVATION_CLIMB above its origin band is disabled (it can
/// climb a little, and flies over lower ground freely). The hit-level half of the band
/// rule lives in BulletHitRouterComponent; this is the physical cleanup that keeps
/// bullets from crossing cliff faces.
/// Throttled to 0.15 s because each tick costs a full transform-array alloc per instance
/// (the plugin's all_bullets_get_transforms offers no caller-buffer overload — same
/// throttling rationale as EnemyBullets3DComponent's 30 Hz sync). disable_bullet goes
/// through call_deferred per the plugin README's runtime-call rule.
/// </summary>
public partial class BulletCliffComponent : Component
{
    /// Sweep period in seconds.
    [Export] public float TickSeconds { get; set; } = 0.15f;

    private BulletSpawnerComponent? _spawner;
    private ElevationMapComponent? _elevationMap;
    private float _timer;

    public override void _Process(double delta)
    {
        _timer += (float)delta;
        if (_timer < TickSeconds) return;
        _timer = 0f;

        // Lazy resolution: the GameManager facade's static instance isn't set until the
        // game root's _Ready, which runs after this component's own registration.
        _spawner ??= GetSibling<BulletSpawnerComponent>();
        _elevationMap ??= GameManager.ElevationMap;
        if (_spawner == null || _elevationMap == null) return;

        CullAboveBand(_spawner.LiveEnemyBullets);
        CullAboveBand(_spawner.LivePlayerBullets);
    }

    private void CullAboveBand(IReadOnlyDictionary<GodotObject, int> live)
    {
        foreach (var (inst, band) in live)
        {
            if (!GodotObject.IsInstanceValid(inst)) continue;
            int count = inst.Call("get_amount_bullets").AsInt32();
            if (count == 0) continue;
            var transforms = inst.Call("all_bullets_get_transforms").AsGodotArray<Transform2D>();
            for (int i = 0; i < count && i < transforms.Count; i++)
            {
                // Per-index liveness: the bulk get_all_bullets_status array is buggy in
                // the plugin (only slot 0 is ever written), same as everywhere else.
                if (!inst.Call("is_bullet_status_enabled", i).AsBool()) continue;
                if (_elevationMap!.GetElevationAt(transforms[i].Origin) > band + ServerConstants.BulletElevationClimb)
                    inst.CallDeferred("disable_bullet", i);
            }
        }
    }
}
