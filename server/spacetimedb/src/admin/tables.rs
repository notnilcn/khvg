use spacetimedb::{table, Identity, ScheduleAt};
use super::admin::tick_player_position_debug;

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
