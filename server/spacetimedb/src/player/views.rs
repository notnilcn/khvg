use spacetimedb::{ViewContext, Query};
use crate::main::global::*;
use crate::world::aoi::surrounding_chunk_indices;
use crate::world::tables::*;
use crate::player::tables::*;

// Keys off PlayerChunk instead of PlayerPosition — PlayerChunk is only written when the
// player's chunk actually changes, so views built on this only recompute on real chunk
// crossings instead of on every report_movement call (10Hz).
pub(crate) fn nearby_indices_from_chunk(ctx: &ViewContext, rings: i32) -> Option<Vec<i64>> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())?.profile_id;
    let chunk = ctx.db.player_chunk().profile_id().find(&profile_id)?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_cols = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let chunk_rows = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(1);
    Some(surrounding_chunk_indices(chunk.chunk_q, chunk.chunk_r, chunk_cols, chunk_rows, Some(rings)))
}

pub(crate) fn nearby_indices(ctx: &ViewContext) -> Option<Vec<i64>> {
    nearby_indices_from_chunk(ctx, DEFAULT_AOI_CHUNK_RADIUS)
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

#[spacetimedb::view(accessor = local_player_stat_allocation, public)]
fn local_player_stat_allocation(ctx: &ViewContext) -> impl Query<PlayerStatAllocation> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);
    ctx.from.player_stat_allocation().r#where(move |a| a.profile_id.eq(profile_id)).build()
}

#[spacetimedb::view(accessor = local_player_inventory, public)]
fn local_player_inventory(ctx: &ViewContext) -> impl Query<PlayerInventorySlot> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);
    ctx.from.player_inventory_slot().r#where(move |s| s.profile_id.eq(profile_id)).build()
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

// Same AOI filter as nearby_remote_players, over the separate rotation table — rotation
// is reported on its own (faster) cadence than movement.
#[spacetimedb::view(accessor = nearby_remote_player_rotations, public)]
fn nearby_remote_player_rotations(ctx: &ViewContext) -> impl Query<PlayerRotation> {
    let empty = ctx.from.player_rotation().r#where(|p| p.profile_id.eq(u64::MAX)).build();
    let indices = match nearby_indices(ctx) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.player_rotation()
        .r#where(move |p| {
            let mut cond = p.chunk_index.eq(indices[0]);
            for &idx in &indices[1..] { cond = cond.or(p.chunk_index.eq(idx)); }
            cond
        })
        .left_semijoin(ctx.from.logged_in_player(), |rot, player| rot.profile_id.eq(player.profile_id))
        .build()
}

#[spacetimedb::view(accessor = nearby_remote_players_profiles, public)]
fn nearby_remote_players_profiles(ctx: &ViewContext) -> impl Query<PlayerProfile> {
    ctx.from.player_profile()
        .left_semijoin(ctx.from.logged_in_player(), |profile, player| profile.profile_id.eq(player.profile_id))
        .build()
}

#[spacetimedb::view(accessor = nearby_loot_drops, public)]
fn nearby_loot_drops(ctx: &ViewContext) -> impl Query<LootDrop> {
    let empty = ctx.from.loot_drop().r#where(|d| d.drop_id.eq(u64::MAX)).build();
    let indices = match nearby_indices(ctx) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.loot_drop().r#where(move |d| {
        let mut cond = d.chunk_index.eq(indices[0]);
        for &idx in &indices[1..] { cond = cond.or(d.chunk_index.eq(idx)); }
        cond
    }).build()
}
