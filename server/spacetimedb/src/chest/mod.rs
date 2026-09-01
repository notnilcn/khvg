use spacetimedb::{table, Query, ReducerContext, Table, Timestamp, ViewContext};
use crate::main::global::*;
use crate::item::tables::item;
use crate::player::methods::{is_admin, recompute_stats, require_in_world, store_item_in_inventory};
use crate::player::tables::player_position;
use crate::player::views::nearby_indices;
use crate::world::hex::{spiral_chunk_index, world_to_chunk};
use crate::world::tables::MapConfig;
use crate::world::wrap::{wrap_world_pos, wrapped_distance_sq};

/// The add_item_to_chest special case: passing this as item_id inserts one ChestItem row
/// per catalog item (the `regenerating` flag applies to all of them).
pub const EVERY_SINGLE_ITEM: &str = "every_single_item";

// A loot container at a fixed world position. Contents are ChestItem rows keyed by
// chest_id; players take items out with take_from_chest.
#[table(accessor = chest, public)]
pub struct Chest {
    #[primary_key] #[auto_inc]
    pub chest_id: u64,
    pub x: f32,
    pub y: f32,
    #[index(btree)]
    pub chunk_index: i64,
}

// One row per item in a chest. A `regenerating` row is not consumed by take_from_chest —
// the row stays, so the chest always holds another copy of the item.
#[table(accessor = chest_item, public)]
pub struct ChestItem {
    #[primary_key] #[auto_inc]
    pub chest_item_id: u64,
    #[index(btree)]
    pub chest_id: u64,
    pub item_id: String,
    pub regenerating: bool,
}

/// Server-only per-player take cooldown for REGENERATING chests (those rows are never
/// consumed, so the take rate is gated instead): one row per (profile, chest), restamped
/// on every take; takes within CHEST_TAKE_COOLDOWN_SECONDS are rejected.
#[table(accessor = chest_take_cooldown)]
pub struct ChestTakeCooldown {
    #[primary_key] #[auto_inc]
    pub id: u64,
    #[index(btree)]
    pub profile_id: u64,
    pub chest_id: u64,
    pub last_taken: Timestamp,
}

#[spacetimedb::reducer]
pub fn add_chest(ctx: &ReducerContext, x: f32, y: f32) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let cfg = MapConfig::load(ctx, 1, 1);
    let (x, y) = wrap_world_pos(x, y, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, cfg.chunk_hex_radius, cfg.hex_outer_radius);
    ctx.db.chest().insert(Chest { chest_id: 0, x, y, chunk_index: spiral_chunk_index(cq, cr) });
    Ok(())
}

#[spacetimedb::reducer]
pub fn add_item_to_chest(ctx: &ReducerContext, chest_id: u64, item_id: String, regenerating: bool) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    ctx.db.chest().chest_id().find(&chest_id)
        .ok_or_else(|| format!("Chest {} not found.", chest_id))?;
    if item_id == EVERY_SINGLE_ITEM {
        let item_ids: Vec<String> = ctx.db.item().iter().map(|i| i.item_id).collect();
        for id in item_ids {
            ctx.db.chest_item().insert(ChestItem { chest_item_id: 0, chest_id, item_id: id, regenerating });
        }
        return Ok(());
    }
    ctx.db.item().item_id().find(&item_id)
        .ok_or_else(|| format!("Item '{}' not found.", item_id))?;
    ctx.db.chest_item().insert(ChestItem { chest_item_id: 0, chest_id, item_id, regenerating });
    Ok(())
}

#[spacetimedb::reducer]
pub fn take_from_chest(ctx: &ReducerContext, chest_item_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;
    let chest_item = ctx.db.chest_item().chest_item_id().find(&chest_item_id)
        .ok_or_else(|| format!("Chest item {} not found.", chest_item_id))?;
    let item = ctx.db.item().item_id().find(&chest_item.item_id).ok_or("Item not found.")?;
    let chest = ctx.db.chest().chest_id().find(&chest_item.chest_id).ok_or("Chest not found.")?;

    // Wrap-aware range check against the caller's position row — no remote looting.
    let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
    let cfg = MapConfig::load(ctx, 0, 0);
    let dist_sq = wrapped_distance_sq(pos.x, pos.y, chest.x, chest.y, (cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));
    if dist_sq > INTERACT_RADIUS * INTERACT_RADIUS {
        return Err("Chest is too far away.".to_string());
    }

    // Regenerating rows are never consumed, so gate the take rate instead: one take per
    // player per chest per CHEST_TAKE_COOLDOWN_SECONDS.
    if chest_item.regenerating {
        match ctx.db.chest_take_cooldown().profile_id().filter(&profile_id).find(|c| c.chest_id == chest.chest_id) {
            Some(cd) => {
                if crate::enemy::methods::seconds_since(ctx, cd.last_taken) < CHEST_TAKE_COOLDOWN_SECONDS as f32 {
                    return Err("This chest was taken too recently.".to_string());
                }
                ctx.db.chest_take_cooldown().id().update(ChestTakeCooldown { last_taken: ctx.timestamp, ..cd });
            }
            None => {
                ctx.db.chest_take_cooldown().insert(ChestTakeCooldown {
                    id: 0, profile_id, chest_id: chest.chest_id, last_taken: ctx.timestamp,
                });
            }
        }
    }

    store_item_in_inventory(ctx, profile_id, &item, vec![], None, u32::MAX)?;

    // Non-regenerating items are consumed; regenerating ones keep their row, which IS the
    // next copy — the chest regenerates the item by never running out of it.
    if !chest_item.regenerating {
        ctx.db.chest_item().chest_item_id().delete(&chest_item_id);
    }
    recompute_stats(ctx, profile_id);
    Ok(())
}

// ===== Views =====

// Same AOI chunk filter as nearby_loot_drops (player/views.rs).
#[spacetimedb::view(accessor = nearby_chests, public)]
fn nearby_chests(ctx: &ViewContext) -> impl Query<Chest> {
    let empty = ctx.from.chest().r#where(|c| c.chest_id.eq(u64::MAX)).build();
    let indices = match nearby_indices(ctx) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.chest().r#where(move |c| {
        let mut cond = c.chunk_index.eq(indices[0]);
        for &idx in &indices[1..] { cond = cond.or(c.chunk_index.eq(idx)); }
        cond
    }).build()
}

// Contents of the chests inside the caller's AOI — ChestItem has no chunk_index of its
// own, so the filter is an OR-chain over the nearby chest ids (the same chunk filter as
// nearby_chests, read imperatively).
#[spacetimedb::view(accessor = nearby_chest_items, public)]
fn nearby_chest_items(ctx: &ViewContext) -> impl Query<ChestItem> {
    let empty = ctx.from.chest_item().r#where(|i| i.chest_item_id.eq(u64::MAX)).build();
    let indices = match nearby_indices(ctx) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    let mut chest_ids: Vec<u64> = Vec::new();
    for idx in &indices {
        chest_ids.extend(ctx.db.chest().chunk_index().filter(idx).map(|c| c.chest_id));
    }
    if chest_ids.is_empty() { return empty; }
    ctx.from.chest_item().r#where(move |i| {
        let mut cond = i.chest_id.eq(chest_ids[0]);
        for &id in &chest_ids[1..] { cond = cond.or(i.chest_id.eq(id)); }
        cond
    }).build()
}
