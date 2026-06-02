use spacetimedb::{ReducerContext, Table, ScheduleAt, TimeDuration};
use std::time::Duration;
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::enemy::tables::*;
use super::seeds::{seed_default_textures, seed_default_enemies, seed_stress_enemies, seed_default_items};
use super::admin::internal_add_chunks;

#[spacetimedb::reducer(init)]
pub fn init(ctx: &ReducerContext) {
    ctx.db.enemy_behavior_schedule().insert(EnemyBehaviorSchedule {
        scheduled_id: 0,
        scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_millis(100))),
    });
    ctx.db.enemy_spawn_schedule().insert(EnemySpawnSchedule {
        scheduled_id: 0,
        scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_secs(2))),
    });
    seed_default_textures(ctx);
    seed_default_enemies(ctx);
    seed_stress_enemies(ctx);
    seed_default_items(ctx);
    let _ = internal_add_chunks(ctx, DEFAULT_CHUNK_HEX_RADIUS, DEFAULT_CHUNK_COLS, DEFAULT_CHUNK_ROWS, DEFAULT_HEX_OUTER_RADIUS);
}

#[spacetimedb::reducer(client_connected)]
pub fn client_connected(ctx: &ReducerContext) {
    let in_world = ctx.db.logged_in_player().player_id().find(ctx.sender()).is_some();
    let in_lobby = ctx.db.logged_out_player().player_id().find(ctx.sender()).is_some();
    if in_world {
        log::error!("Player {:?} connected while already in world — this is a bug.", ctx.sender());
        if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
            ctx.db.logged_in_player().player_id().delete(ctx.sender());
            ctx.db.logged_out_player().insert(LoggedOutPlayer { player_id: p.player_id, username: p.username, is_admin: p.is_admin });
        }
    } else if !in_lobby {
        ctx.db.logged_out_player().insert(LoggedOutPlayer {
            player_id: ctx.sender(), username: String::new(), is_admin: false,
        });
        ctx.db.player_profile().insert(PlayerProfile {
            profile_id: 0, player_id: ctx.sender(),
            profile_name: "Knight".to_string(),
            texture_id: "Knight".to_string(),
        });
        log::info!("New player: {:?}", ctx.sender());
    }
}

#[spacetimedb::reducer(client_disconnected)]
pub fn client_disconnected(ctx: &ReducerContext) {
    if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        ctx.db.logged_in_player().player_id().delete(ctx.sender());
        ctx.db.logged_out_player().insert(LoggedOutPlayer { player_id: p.player_id, username: p.username, is_admin: p.is_admin });
    }
    log::info!("Player disconnected: {:?}", ctx.sender());
}
