#nullable enable
using Godot;

/// <summary>
/// Hurtbox for remote player puppets (declared in non_local_player.tscn with a capsule
/// shape child). Enemy bullets use collision layer/mask 2, so this Area2D sits on layer 2
/// (mask 0 — it detects nothing itself) and bullets register collisions against other
/// players on every client. The BlastBullets2D plugin counts each collision toward
/// bullet_max_collision_count and auto-disables the bullet once reached (0 = piercing,
/// never disabled), so a bullet seen hitting another player despawns locally everywhere.
/// Also the anti-cheat witness path: BulletHitRouterComponent forwards bullet overlaps
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
