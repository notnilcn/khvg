use spacetimedb::ReducerContext;
use crate::world::tables::*;
use crate::player::methods::*;

#[spacetimedb::reducer]
pub fn place_building(ctx: &ReducerContext, hex_q: i32, hex_r: i32, building_type: String) -> Result<(), String> {
    require_logged_in(ctx)?;
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
