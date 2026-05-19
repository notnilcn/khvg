use spacetimedb::AnonymousViewContext;
use spacetimedb::{ViewContext, Query, Table, Identity};
use super::tables::*;
use super::methods::*;

fn nearby_indices(ctx: &ViewContext) -> Option<Vec<i64>> {
    let pos = ctx.db.player_position().player_id().find(ctx.sender())?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let chunk_rows       = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(1);
    let (cq, cr) = world_to_chunk(pos.x, pos.y, chunk_hex_radius, hex_outer_radius);
    Some(surrounding_chunk_indices(cq, cr, chunk_cols, chunk_rows, None))
}

// ── Local player views ────────────────────────────────────────────────────────

#[spacetimedb::view(accessor = local_player, public)]
fn local_player(ctx: &ViewContext) -> impl Query<Player> {
    let sender = ctx.sender();
    ctx.from.logged_in_player().r#where(move |p| p.player_id.eq(sender)).build()
}

#[spacetimedb::view(accessor = local_player_data, public)]
fn local_player_data(ctx: &ViewContext) -> impl Query<PlayerData> {
    let sender = ctx.sender();
    ctx.from.player_data().r#where(move |p| p.player_id.eq(sender)).build()
}

#[spacetimedb::view(accessor = local_player_stats, public)]
fn local_player_stats(ctx: &ViewContext) -> impl Query<PlayerStats> {
    let sender = ctx.sender();
    ctx.from.player_stats().r#where(move |p| p.player_id.eq(sender)).build()
}

#[spacetimedb::view(accessor = local_player_inventory, public)]
fn local_player_inventory(ctx: &ViewContext) -> impl Query<PlayerInventory> {
    let sender = ctx.sender();
    ctx.from.player_inventory().r#where(move |p| p.player_id.eq(sender)).build()
}

#[spacetimedb::view(accessor = local_player_position, public)]
fn local_player_position(ctx: &ViewContext) -> impl Query<PlayerPosition> {
    let sender = ctx.sender();
    ctx.from.player_position().r#where(move |p| p.player_id.eq(sender)).build()
}

// ── Remote player views ───────────────────────────────────────────────────────

#[spacetimedb::view(accessor = nearby_remote_players, public)]
fn nearby_remote_players(ctx: &ViewContext) -> impl Query<PlayerPosition> {
    let empty = ctx.from.player_position().r#where(|p| p.player_id.eq(Identity::default())).build();
    let indices = match nearby_indices(ctx) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.player_position()
        .r#where(move |p| {
            let mut cond = p.chunk_index.eq(indices[0]);
            for &idx in &indices[1..] {
                cond = cond.or(p.chunk_index.eq(idx));
            }
            cond
        })
        .left_semijoin(
            ctx.from.logged_in_player(),
            |pos, player| pos.player_id.eq(player.player_id),
        )
        .build()
}

// ── Enemy views ───────────────────────────────────────────────────────────────

#[spacetimedb::view(accessor = nearby_enemies, public)]
fn nearby_enemies(ctx: &ViewContext) -> impl Query<Enemy> {
    let empty = ctx.from.enemy().r#where(|e| e.enemy_id.eq(u64::MAX)).build();
    let indices = match nearby_indices(ctx) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.enemy().r#where(move |e| {
        let mut cond = e.chunk_index.eq(indices[0]);
        for &idx in &indices[1..] {
            cond = cond.or(e.chunk_index.eq(idx));
        }
        cond
    }).build()
}

#[spacetimedb::view(accessor = enemy_templates, public)]
fn enemy_templates(ctx: &AnonymousViewContext) -> impl Query<EnemyTemplate> {
    ctx.from.enemy_template().build()
}
