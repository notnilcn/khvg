use spacetimedb::{ReducerContext, Table};
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::player::methods::*;

#[spacetimedb::reducer(client_connected)]
pub fn client_connected(ctx: &ReducerContext) {
    let is_new = ctx.db.logged_in_player().player_id().find(ctx.sender()).is_none()
        && ctx.db.logged_out_player().player_id().find(ctx.sender()).is_none();

    if is_new {
        seed_new_player(ctx, ctx.sender());
        log::info!("New player: {:?}", ctx.sender());
    } else if let Some(player) = ctx.db.logged_out_player().player_id().find(ctx.sender()) {
        ctx.db.logged_out_player().player_id().delete(ctx.sender());
        ctx.db.logged_in_player().insert(player);
        log::info!("Returning player: {:?}", ctx.sender());
    }
}

#[spacetimedb::reducer(client_disconnected)]
pub fn client_disconnected(ctx: &ReducerContext) {
    if let Some(player) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        ctx.db.logged_in_player().player_id().delete(ctx.sender());
        ctx.db.logged_out_player().insert(player);
    }
    log::info!("Player disconnected: {:?}", ctx.sender());
}

#[spacetimedb::reducer]
pub fn set_username(ctx: &ReducerContext, username: String) -> Result<(), String> {
    if username.trim().is_empty() || username.len() > 20 {
        return Err("Username must be 1-20 non-whitespace characters.".to_string());
    }
    let player = ctx.db.logged_in_player().player_id().find(ctx.sender()).ok_or("Player not found.")?;
    ctx.db.logged_in_player().player_id().update(Player { username, ..player });
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_movement(ctx: &ReducerContext, x: f32, y: f32, rotation: f32) -> Result<(), String> {
    require_logged_in(ctx)?;
    let pos = ctx.db.player_position().player_id().find(ctx.sender())
        .ok_or_else(|| format!("Position not found for {:?}", ctx.sender()))?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(0);
    let chunk_rows = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(0);
    let (x, y) = wrap_world_pos(x, y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    ctx.db.player_position().player_id().update(PlayerPosition { x, y, rotation, chunk_index, ..pos });
    Ok(())
}

#[spacetimedb::reducer]
pub fn gain_xp(ctx: &ReducerContext, amount: u32) -> Result<(), String> {
    require_logged_in(ctx)?;
    const MAX_XP_PER_GAIN: u32 = 500;
    if amount == 0 { return Ok(()); }
    if amount > MAX_XP_PER_GAIN {
        return Err(format!("XP gain {} exceeds per-call maximum of {}.", amount, MAX_XP_PER_GAIN));
    }
    let data = ctx.db.player_data().player_id().find(ctx.sender()).ok_or("Player data not found.")?;
    let old_level = data.level;
    let new_xp = data.xp + amount;
    let new_level = compute_level(new_xp);
    let new_max_hp = (BASE_MAX_HP + (new_level as i32 - 1) * HP_PER_LEVEL) as u32;
    let heal = (new_level - old_level) * HP_PER_LEVEL as u32;
    let new_hp = (data.hp + heal).min(new_max_hp);
    ctx.db.player_data().player_id().update(PlayerData { level: new_level, xp: new_xp, hp: new_hp, max_hp: new_max_hp, ..data });
    if new_level != old_level {
        recompute_stats(ctx, ctx.sender());
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn update_inventory(ctx: &ReducerContext, slots: Vec<InventorySlot>) -> Result<(), String> {
    require_logged_in(ctx)?;
    if slots.iter().filter(|s| matches!(s.kind, SlotKind::Backpack)).count() > 12 {
        return Err("Too many backpack slots.".to_string());
    }
    if slots.iter().filter(|s| matches!(s.kind, SlotKind::Accessory)).count() > 3 {
        return Err("Too many accessory slots.".to_string());
    }
    if slots.iter().filter(|s| matches!(s.kind, SlotKind::Consumable)).count() > 8 {
        return Err("Too many consumable slots.".to_string());
    }
    let inv = ctx.db.player_inventory().player_id().find(ctx.sender()).ok_or("Inventory not found.")?;
    ctx.db.player_inventory().player_id().update(PlayerInventory { slots, ..inv });
    recompute_stats(ctx, ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn pickup_drop(ctx: &ReducerContext, drop_id: u64) -> Result<(), String> {
    require_logged_in(ctx)?;
    let drop = ctx.db.loot_drop().drop_id().find(&drop_id).ok_or("Drop not found.")?;
    if drop.item_id.is_empty() { return Ok(()); }
    let inv = ctx.db.player_inventory().player_id().find(ctx.sender()).ok_or("Inventory not found.")?;
    if inv.slots.iter().filter(|s| matches!(s.kind, SlotKind::Backpack)).count() >= 12 {
        return Err("Backpack full.".to_string());
    }
    let next_index = inv.slots.iter()
        .filter(|s| matches!(s.kind, SlotKind::Backpack))
        .map(|s| s.index)
        .max()
        .map(|i| i + 1)
        .unwrap_or(0);
    let mut slots = inv.slots.clone();
    slots.push(InventorySlot { kind: SlotKind::Backpack, index: next_index, item_id: drop.item_id });
    ctx.db.player_inventory().player_id().update(PlayerInventory { slots, ..inv });
    ctx.db.loot_drop().drop_id().delete(&drop_id);
    Ok(())
}
