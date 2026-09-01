use spacetimedb::ReducerContext;
use crate::world::tables::*;
use crate::player::methods::*;

/// Elevation level of one hex, default 0 when no tile row exists (i.e. before add_chunks).
/// Same hex_q-btree + hex_r-find lookup place_building uses.
pub fn hex_elevation(ctx: &ReducerContext, hex_q: i32, hex_r: i32) -> i16 {
    ctx.db.building_tile().hex_q().filter(&hex_q)
        .find(|t| t.hex_r == hex_r)
        .map(|t| t.elevation)
        .unwrap_or(0)
}

#[spacetimedb::reducer]
pub fn place_building(ctx: &ReducerContext, hex_q: i32, hex_r: i32, building_type: String) -> Result<(), String> {
    // Buildings aren't a designed feature yet — no client UI calls this reducer — so
    // placement is admin-only world authoring, like add_chest/add_item_to_chest.
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if building_type.is_empty() { return Err("building_type cannot be empty.".to_string()); }

    let tile = ctx.db.building_tile().hex_q().filter(&hex_q)
        .find(|t| t.hex_r == hex_r)
        .ok_or("Tile not found — has add_chunks been called?")?;

    if !tile.building_type.is_empty() { return Err("Tile already occupied.".to_string()); }

    ctx.db.building_tile().tile_id().update(BuildingTile { building_type, owner_id: Some(ctx.sender()), ..tile });
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_building(ctx: &ReducerContext, hex_q: i32, hex_r: i32) -> Result<(), String> {
    require_logged_in(ctx)?;
    let tile = ctx.db.building_tile().hex_q().filter(&hex_q)
        .find(|t| t.hex_r == hex_r)
        .ok_or("Tile not found.")?;

    if tile.building_type.is_empty() { return Ok(()); }

    if tile.owner_id != Some(ctx.sender()) && !is_admin(ctx) {
        return Err("Not the owner.".to_string());
    }

    ctx.db.building_tile().tile_id().update(BuildingTile { building_type: String::new(), owner_id: None, ..tile });
    Ok(())
}
