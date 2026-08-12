use spacetimedb::{Identity, ReducerContext, Table};
use crate::anticheat::tables::*;
use crate::player::methods::require_in_world;

/// Witness report: the caller saw `hit_player` get hit by a bullet. Self-reports are
/// silently ignored — the victim's own client reports damage through `report_hit`; this
/// table only collects independent corroboration.
#[spacetimedb::reducer]
pub fn flag_player_hit(ctx: &ReducerContext, hit_player: Identity) -> Result<(), String> {
    require_in_world(ctx)?;
    if hit_player == ctx.sender() {
        return Ok(());
    }
    ctx.db.player_hit_flag().insert(PlayerHitFlag {
        flag_id: 0,
        hit_player,
        reported_by: ctx.sender(),
        reported_at: ctx.timestamp,
    });
    Ok(())
}
