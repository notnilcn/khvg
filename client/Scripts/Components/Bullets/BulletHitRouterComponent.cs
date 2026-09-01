#nullable enable
using Godot;

/// <summary>
/// Routes BlastBullets2D enemy-bullet overlaps to the server: the local player's hurtbox
/// (DamageReceivingComponent) forwards the hit to the ReportHit reducer; a remote player's
/// hurtbox (RemoteHitWitnessComponent) forwards it to the FlagPlayerHit anti-cheat reducer
/// (witness corroboration — damage stays reported by the victim's own client).
/// Anything ELSE on the player collision layer is unknown — it is warned about and ignored,
/// never reported: the old fallback reported a self-hit for any unrecognized area, one new
/// layer-2 area away from phantom self-damage on every bullet flyover
/// (logic moved out of BulletManager.cs).
///
/// Elevation band rule: enemy spawn data is piercing (bullet_max_collision_count = 0 —
/// a bullet must survive overlapping off-band players), so the despawn is manual here.
/// A hit only counts when the victim's hex elevation is within the bullet's hit window
/// [band − BULLET_ELEVATION_DROP, band + BULLET_ELEVATION_CLIMB] around its origin band
/// (tracked by BulletSpawnerComponent); on a match the bullet is disabled via
/// call_deferred (the plugin's README requirement for runtime bullet calls outside
/// attach timers — the rare same-frame second overlap is bounded by the server's
/// INCOMING_HIT_RATE_CAP), outside the window the bullet flies on and nothing is
/// reported.
/// </summary>
public partial class BulletHitRouterComponent : Component
{
    private BulletSpawnerComponent? _spawner;
    private ElevationMapComponent? _elevationMap;

    protected override void OnRegistered()
    {
        _spawner = GetSibling<BulletSpawnerComponent>();
        ((BulletManager)Entity!).BlastBullets.Connect("area_entered",
            Callable.From<GodotObject, GodotObject, int, Resource, Transform2D>(OnEnemyBulletAreaEntered));
    }

    private void OnEnemyBulletAreaEntered(GodotObject hitTargetArea, GodotObject bulletsInstance, int bulletIndex, Resource bulletsCustomData, Transform2D bulletTransform)
    {
        if (bulletsCustomData is not BulletData data) return;
        if (hitTargetArea is not Node2D victim) return;

        // Band check: the victim must stand within the bullet's hit window — at most
        // BULLET_ELEVATION_DROP below or BULLET_ELEVATION_CLIMB above the origin band.
        // Untracked instances (or a missing elevation map) fail open to the old
        // behavior — every tracked enemy bullet is in LiveEnemyBullets by construction.
        _elevationMap ??= GameManager.ElevationMap;
        if (_elevationMap != null &&
            _spawner != null &&
            _spawner.LiveEnemyBullets.TryGetValue(bulletsInstance, out int band))
        {
            int victimElevation = _elevationMap.GetElevationAt(victim.GlobalPosition);
            if (victimElevation < band - ServerConstants.BulletElevationDrop ||
                victimElevation > band + ServerConstants.BulletElevationClimb)
                return; // out-of-window overlap: the bullet flies through, no hit, no despawn
        }

        if (victim is DamageReceivingComponent receiver)
            receiver.ProcessBulletHit(data);
        else if (victim is RemoteHitWitnessComponent witness)
            witness.ProcessBulletHit();
        else
        {
            GD.PushWarning($"[BulletHitRouterComponent] enemy bullet hit an unrecognized layer-2 area '{hitTargetArea}' — ignored (no hit reported)");
            return;
        }

        // Piercing spawn data never despawns on its own — a reported hit disables the
        // bullet manually so every client despawns it deterministically.
        bulletsInstance.CallDeferred("disable_bullet", bulletIndex);
    }
}
