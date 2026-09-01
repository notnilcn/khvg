#nullable enable
using Godot;

/// <summary>
/// Hurtbox for remote player puppets (declared in non_local_player.tscn with a capsule
/// shape child). Enemy bullets use collision layer/mask 2, so this Area2D sits on layer 2
/// (mask 0 — it detects nothing itself) and bullets register collisions against other
/// players on every client. Enemy spawn data is piercing (bullet_max_collision_count = 0,
/// never auto-disabled — a bullet must survive overlapping off-band players), so
/// BulletHitRouterComponent applies the elevation band check before forwarding here and
/// disables the bullet manually on a reported hit, keeping despawns deterministic across
/// clients. Also the anti-cheat witness path: the router forwards bullet overlaps
/// here and this flags the hit to the server — witnesses only corroborate; damage stays
/// reported exclusively by the victim's own client through ReportHit.
/// </summary>
public partial class RemoteHitWitnessComponent : AreaComponent
{
    /// Enemy-bullet path, forwarded by BulletHitRouterComponent. Flags the owning remote
    /// player as hit; never reports damage.
    public void ProcessBulletHit()
    {
        if (Entity is RemotePlayer player)
            GameManager.Conn?.Reducers.FlagPlayerHit(player.PlayerId);
    }
}
