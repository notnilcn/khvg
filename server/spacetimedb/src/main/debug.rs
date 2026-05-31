use spacetimedb::{table, ReducerContext, Table, Identity, ScheduleAt, TimeDuration};
use std::time::Duration;
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::player::methods::*;

#[spacetimedb::reducer]
pub fn tick_player_position_debug(ctx: &ReducerContext, _job: PlayerPositionDebugSchedule) {
    let cfg = ctx.db.map_config().id().find(&0u32);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let mut active_ids = std::collections::HashSet::new();
    for pos in ctx.db.player_position().iter() {
        let (hex_q, hex_r) = world_to_hex(pos.x, pos.y, hex_outer_radius);
        let row = PlayerPositionDebug {
            player_id: pos.player_id, x: pos.x, y: pos.y, rotation: pos.rotation,
            chunk_index: pos.chunk_index, hex_q, hex_r,
        };
        if ctx.db.player_position_debug().player_id().find(&pos.player_id).is_some() {
            ctx.db.player_position_debug().player_id().update(row);
        } else {
            ctx.db.player_position_debug().insert(row);
        }
        active_ids.insert(pos.player_id);
    }
    let stale: Vec<Identity> = ctx.db.player_position_debug()
        .iter().filter(|d| !active_ids.contains(&d.player_id)).map(|d| d.player_id).collect();
    for id in stale { ctx.db.player_position_debug().player_id().delete(&id); }
}

#[spacetimedb::reducer]
pub fn toggle_debug(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let is_active = ctx.db.player_position_debug_schedule().iter().next().is_some();
    if is_active {
        for row in ctx.db.player_position_debug_schedule().iter() {
            ctx.db.player_position_debug_schedule().scheduled_id().delete(&row.scheduled_id);
        }
        for row in ctx.db.player_position_debug().iter() {
            ctx.db.player_position_debug().player_id().delete(&row.player_id);
        }
        log::info!("toggle_debug: position debug disabled.");
    } else {
        ctx.db.player_position_debug_schedule().insert(PlayerPositionDebugSchedule {
            scheduled_id: 0,
            scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_secs(1))),
        });
        log::info!("toggle_debug: position debug enabled.");
    }
    Ok(())
}

#[table(accessor = player_position_debug)]
pub struct PlayerPositionDebug {
    #[primary_key]
    pub player_id: Identity,
    pub x: f32,
    pub y: f32,
    pub rotation: f32,
    pub chunk_index: i64,
    pub hex_q: i32,
    pub hex_r: i32,
}

#[spacetimedb::table(accessor = player_position_debug_schedule, scheduled(tick_player_position_debug))]
pub struct PlayerPositionDebugSchedule {
    #[primary_key] #[auto_inc]
    pub scheduled_id: u64,
    pub scheduled_at: ScheduleAt,
}
