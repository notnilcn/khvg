#nullable enable
using Godot;

/// <summary>
/// Routes BlastBullets2D enemy-bullet overlaps to the server: the local player's hurtbox
/// (DamageReceivingComponent) forwards the hit to the ReportHit reducer; a remote player's
/// hurtbox (RemoteHitWitnessComponent) forwards it to the FlagPlayerHit anti-cheat reducer
/// (witness corroboration — the bullet despawn itself is the plugin's collision counting);
/// the fallback covers bullets hitting anything else on the player collision layer
/// (logic moved out of BulletManager.cs).
/// </summary>
public partial class BulletHitRouterComponent : Component
{
    protected override void OnRegistered()
    {
        ((BulletManager)Entity!).BlastBullets.Connect("area_entered",
            Callable.From<GodotObject, GodotObject, int, Resource, Transform2D>(OnEnemyBulletAreaEntered));
    }

    private void OnEnemyBulletAreaEntered(GodotObject hitTargetArea, GodotObject bulletsInstance, int bulletIndex, Resource bulletsCustomData, Transform2D bulletTransform)
    {
        if (bulletsCustomData is not BulletData data) return;
        if (hitTargetArea is DamageReceivingComponent receiver)
            receiver.ProcessBulletHit(data);
        else if (hitTargetArea is RemoteHitWitnessComponent witness)
            witness.ProcessBulletHit();
        else
            GameManager.Conn?.Reducers.ReportHit(data.SourceStep);
    }
}
