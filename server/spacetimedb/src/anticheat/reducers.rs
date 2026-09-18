use spacetimedb::{Identity, ReducerContext, Table, Timestamp};
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

/// Record a move-validation failure for a profile (BitCraft `validation_strike`).
/// Prunes timestamps outside STRIKE_WINDOW_SECS, appends now, and returns the
/// live strike count. Server-side only — called from `report_movement`.
pub fn record_move_strike(ctx: &ReducerContext, profile_id: u64) -> u32 {
    let now_micros = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    let window_micros = (crate::main::global::STRIKE_WINDOW_SECS * 1_000_000.0) as u128;
    let mut stamps: Vec<Timestamp> = ctx.db.move_validation_strikes().profile_id().find(&profile_id)
        .map(|r| r.failure_timestamps)
        .unwrap_or_default()
        .into_iter()
        .filter(|t| {
            let then = t.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
            now_micros.saturating_sub(then) <= window_micros
        })
        .collect();
    stamps.push(ctx.timestamp);
    let count = stamps.len() as u32;
    match ctx.db.move_validation_strikes().profile_id().find(&profile_id) {
        Some(row) => {
            ctx.db.move_validation_strikes().profile_id().update(MoveValidationStrikes { failure_timestamps: stamps, ..row });
        }
        None => {
            ctx.db.move_validation_strikes().insert(MoveValidationStrikes { profile_id, failure_timestamps: stamps });
        }
    }
    log::warn!("move validation strike for profile {} ({}/{})", profile_id, count, crate::main::global::STRIKES_BEFORE_REJECT);
    count
}

/// Live strike count (pruned to the window, no write).
pub fn live_strike_count(ctx: &ReducerContext, profile_id: u64) -> u32 {
    let now_micros = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    let window_micros = (crate::main::global::STRIKE_WINDOW_SECS * 1_000_000.0) as u128;
    ctx.db.move_validation_strikes().profile_id().find(&profile_id)
        .map(|r| r.failure_timestamps.into_iter().filter(|t| {
            let then = t.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
            now_micros.saturating_sub(then) <= window_micros
        }).count() as u32)
        .unwrap_or(0)
}

/// Sweep expired strike rows (called from the cleanup pass so the table cannot grow
/// unbounded for players who stop moving).
pub fn sweep_strikes(ctx: &ReducerContext) {
    for row in ctx.db.move_validation_strikes().iter().collect::<Vec<_>>() {
        if live_strike_count(ctx, row.profile_id) == 0 {
            ctx.db.move_validation_strikes().profile_id().delete(&row.profile_id);
        }
    }
}
