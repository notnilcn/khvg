use spacetimedb::{ViewContext, Query};
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;

pub(crate) fn nearby_indices(ctx: &ViewContext) -> Option<Vec<i64>> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())?.profile_id;
    let pos = ctx.db.player_position().profile_id().find(&profile_id)?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let chunk_rows = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(1);
    let (cq, cr) = world_to_chunk(pos.x, pos.y, chunk_hex_radius, hex_outer_radius);
    Some(surrounding_chunk_indices(cq, cr, chunk_cols, chunk_rows, None))
}

#[spacetimedb::view(accessor = local_player, public)]
fn local_player(ctx: &ViewContext) -> impl Query<LoggedInPlayer> {
    let sender = ctx.sender();
    ctx.from.logged_in_player().r#where(move |p| p.player_id.eq(sender)).build()
}

#[spacetimedb::view(accessor = local_lobby_player, public)]
fn local_lobby_player(ctx: &ViewContext) -> impl Query<LoggedOutPlayer> {
    let sender = ctx.sender();
    ctx.from.logged_out_player().r#where(move |p| p.player_id.eq(sender)).build()
}

#[spacetimedb::view(accessor = local_player_profiles, public)]
fn local_player_profiles(ctx: &ViewContext) -> impl Query<PlayerProfile> {
    let sender = ctx.sender();
    ctx.from.player_profile().r#where(move |p| p.player_id.eq(sender)).build()
}

#[spacetimedb::view(accessor = local_player_active_profile, public)]
fn local_player_active_profile(ctx: &ViewContext) -> impl Query<PlayerProfile> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);
    ctx.from.player_profile().r#where(move |p| p.profile_id.eq(profile_id)).build()
}

#[spacetimedb::view(accessor = local_player_data, public)]
fn local_player_data(ctx: &ViewContext) -> impl Query<PlayerData> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);
    ctx.from.player_data().r#where(move |d| d.profile_id.eq(profile_id)).build()
}

#[spacetimedb::view(accessor = local_player_stats, public)]
fn local_player_stats(ctx: &ViewContext) -> impl Query<PlayerStats> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);
    ctx.from.player_stats().r#where(move |s| s.profile_id.eq(profile_id)).build()
}

#[spacetimedb::view(accessor = local_player_inventory, public)]
fn local_player_inventory(ctx: &ViewContext) -> impl Query<PlayerInventory> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);
    ctx.from.player_inventory().r#where(move |i| i.profile_id.eq(profile_id)).build()
}

#[spacetimedb::view(accessor = local_player_position, public)]
fn local_player_position(ctx: &ViewContext) -> impl Query<PlayerPosition> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);
    ctx.from.player_position().r#where(move |p| p.profile_id.eq(profile_id)).build()
}

#[spacetimedb::view(accessor = nearby_remote_players, public)]
fn nearby_remote_players(ctx: &ViewContext) -> impl Query<PlayerPosition> {
    let empty = ctx.from.player_position().r#where(|p| p.profile_id.eq(u64::MAX)).build();
    let indices = match nearby_indices(ctx) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.player_position()
        .r#where(move |p| {
            let mut cond = p.chunk_index.eq(indices[0]);
            for &idx in &indices[1..] { cond = cond.or(p.chunk_index.eq(idx)); }
            cond
        })
        .left_semijoin(ctx.from.logged_in_player(), |pos, player| pos.profile_id.eq(player.profile_id))
        .build()
}

#[spacetimedb::view(accessor = nearby_remote_players_profiles, public)]
fn nearby_remote_players_profiles(ctx: &ViewContext) -> impl Query<PlayerProfile> {
    ctx.from.player_profile()
        .left_semijoin(ctx.from.logged_in_player(), |profile, player| profile.profile_id.eq(player.profile_id))
        .build()
}
