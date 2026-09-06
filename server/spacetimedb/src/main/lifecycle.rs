use spacetimedb::{ReducerContext, Table, ScheduleAt, TimeDuration};
use std::time::Duration;
use crate::main::global::*;
use crate::world::terrain::internal_generate_world_proc;
use crate::player::tables::*;
use crate::enemy::instance_tables::*;
use super::seeds::{seed_default_textures, seed_default_animations, seed_layering_rules, seed_adjacency_rules, seed_decor_ground_rules, seed_default_enemies, seed_world_defs};
use super::admin::internal_add_chunks;
use crate::item::seeds::{seed_world_items, seed_world_enchantments};

#[spacetimedb::reducer(init)]
pub fn init(ctx: &ReducerContext) {
    ctx.db.enemy_behavior_schedule().insert(EnemyBehaviorSchedule {
        scheduled_id: 0,
        scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_millis(100))),
    });
    // Biome-region enemy spawning starts disarmed for debugging: no EnemySpawnSchedule
    // job row is inserted here. An admin re-arms the 2s tick with toggle_enemy_spawning(true).
    ctx.db.consumable_effect_schedule().insert(ConsumableEffectSchedule {
        scheduled_id: 0,
        scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_secs_f32(CONSUMABLE_EFFECT_TICK_SECONDS))),
    });
    seed_default_textures(ctx);
    seed_default_animations(ctx);
    seed_layering_rules(ctx);
    seed_adjacency_rules(ctx);
    seed_decor_ground_rules(ctx);
    seed_default_enemies(ctx);
    crate::enemy::defs::gorgon_boss::seed(ctx);
    seed_world_items(ctx);
    seed_world_enchantments(ctx);
    seed_world_defs(ctx);
    let _ = internal_add_chunks(ctx, DEFAULT_CHUNK_HEX_RADIUS, DEFAULT_CHUNK_COLS, DEFAULT_CHUNK_ROWS, DEFAULT_HEX_OUTER_RADIUS);
    let _ = internal_generate_world_proc(ctx, "Earth".to_string(), 0);
}

#[spacetimedb::reducer(client_connected)]
pub fn client_connected(ctx: &ReducerContext) {
    let in_world = ctx.db.logged_in_player().player_id().find(ctx.sender()).is_some();
    let in_lobby = ctx.db.logged_out_player().player_id().find(ctx.sender()).is_some();
    if in_world {
        log::error!("Player {:?} connected while already in world — this is a bug.", ctx.sender());
        if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
            // Same runtime-row purge as leave_world/client_disconnected — the recovery
            // path must not leave statuses ticking or zones live for a player being
            // bounced back to the lobby.
            crate::status::methods::purge_profile_statuses(ctx, p.profile_id);
            crate::status::methods::purge_profile_zones(ctx, p.profile_id);
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
            aim_assist: false, lock_on: false,
        });
        log::info!("New player: {:?}", ctx.sender());
    }
}

#[spacetimedb::reducer(client_disconnected)]
pub fn client_disconnected(ctx: &ReducerContext) {
    if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        // Same runtime-row purge as leave_world — no status may kill or tick a logged-out
        // player, and a caster's zones die with them.
        crate::status::methods::purge_profile_statuses(ctx, p.profile_id);
        crate::status::methods::purge_profile_zones(ctx, p.profile_id);
        ctx.db.logged_in_player().player_id().delete(ctx.sender());
        ctx.db.logged_out_player().insert(LoggedOutPlayer { player_id: p.player_id, username: p.username, is_admin: p.is_admin });
    }
    log::info!("Player disconnected: {:?}", ctx.sender());
}
