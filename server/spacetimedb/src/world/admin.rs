use spacetimedb::{ReducerContext, Identity, Table};
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::player::methods::*;
use crate::enemy::tables::*;
use crate::enemy::methods::build_enemy_behavior;

#[spacetimedb::reducer]
pub fn claim_admin(ctx: &ReducerContext) -> Result<(), String> {
    let player = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .ok_or("You must connect first so a Player row exists.")?;
    if player.is_admin { return Ok(()); }
    let any_admin = ctx.db.logged_in_player().iter().any(|p| p.is_admin)
        || ctx.db.logged_out_player().iter().any(|p| p.is_admin);
    if any_admin { return Err("An admin already exists. Use release_admin to remove them first.".to_string()); }
    ctx.db.logged_in_player().player_id().update(Player { is_admin: true, ..player });
    log::info!("claim_admin: {:?} is now admin.", ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn release_admin(ctx: &ReducerContext) -> Result<(), String> {
    let player = ctx.db.logged_in_player().player_id().find(ctx.sender()).ok_or("No Player row found.")?;
    if !player.is_admin { return Err("You are not an admin.".to_string()); }
    ctx.db.logged_in_player().player_id().update(Player { is_admin: false, ..player });
    log::info!("release_admin: {:?} released admin.", ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn change_stats(ctx: &ReducerContext, target_username: String, strength: i32, wisdom: i32, dexterity: i32, defense: i32, vitality: i32, speed: i32) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let stats = ctx.db.player_stats().player_id().find(target_id).ok_or("Stats not found.")?;
    ctx.db.player_stats().player_id().update(PlayerStats { strength, wisdom, dexterity, defense, vitality, speed, ..stats });
    Ok(())
}

#[spacetimedb::reducer]
pub fn give_item(ctx: &ReducerContext, target_username: String, item_id: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let inv = ctx.db.player_inventory().player_id().find(target_id).ok_or("Inventory not found.")?;
    let next_index = inv.slots.iter()
        .filter(|s| matches!(s.kind, SlotKind::Backpack))
        .map(|s| s.index).max().map(|i| i + 1).unwrap_or(0);
    let mut slots = inv.slots.clone();
    slots.push(InventorySlot { kind: SlotKind::Backpack, index: next_index, item_id });
    ctx.db.player_inventory().player_id().update(PlayerInventory { slots, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_item(ctx: &ReducerContext, target_username: String, item_id: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let inv = ctx.db.player_inventory().player_id().find(target_id).ok_or("Inventory not found.")?;
    let slots: Vec<InventorySlot> = inv.slots.iter().filter(|s| s.item_id != item_id).cloned().collect();
    ctx.db.player_inventory().player_id().update(PlayerInventory { slots, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_item_stats(ctx: &ReducerContext, stats: ItemStats) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.item_stats().item_id().find(&stats.item_id).is_some() {
        ctx.db.item_stats().item_id().update(stats);
    } else {
        ctx.db.item_stats().insert(stats);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_enemy_template(ctx: &ReducerContext, template: EnemyTemplate) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.enemy_template().template_id().find(&template.template_id).is_some() {
        ctx.db.enemy_template().template_id().update(template);
    } else {
        ctx.db.enemy_template().insert(template);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn spawn_enemy(ctx: &ReducerContext, template_id: String, x: f32, y: f32, is_elite: bool, immortal: bool) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let template = ctx.db.enemy_template().template_id().find(&template_id)
        .ok_or_else(|| format!("Template '{}' not found.", template_id))?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let chunk_rows = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(1);
    let (x, y) = wrap_world_pos(x, y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    let inserted_behavior = ctx.db.enemy_behavior().insert(build_enemy_behavior(ctx, &template));
    ctx.db.enemy().insert(Enemy {
        enemy_id: 0, template_id, behavior_id: inserted_behavior.behavior_id,
        x, y, spawn_time: ctx.timestamp, hp: template.max_hp,
        phase: 0, is_elite, immortal, chunk_index,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn despawn_enemy(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    ctx.db.enemy().enemy_id().delete(&enemy_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn add_chunks(ctx: &ReducerContext, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if !(1..=200).contains(&chunk_cols) { return Err("chunk_cols must be 1–200.".to_string()); }
    if !(1..=200).contains(&chunk_rows) { return Err("chunk_rows must be 1–200.".to_string()); }
    if hex_outer_radius <= 0.0 { return Err("hex_outer_radius must be positive.".to_string()); }
    let area = hex_area(chunk_hex_radius);
    let shift = hex_shift(chunk_hex_radius);
    for cr in 0..chunk_rows {
        for cq in 0..chunk_cols {
            let cidx = spiral_chunk_index(cq, cr);
            let (cx, cz) = chunk_center_hex(cq, cr, chunk_hex_radius);
            for m in 0..area {
                let (rx, rz) = inv_hexmod(m, chunk_hex_radius, shift);
                ctx.db.building_tile().insert(BuildingTile {
                    tile_id: 0, hex_q: cx + rx, hex_r: cz + rz,
                    chunk_q: cq, chunk_r: cr, local_index: m, chunk_index: cidx,
                    building_type: String::new(), owner_id: None,
                });
            }
        }
    }
    if ctx.db.map_config().id().find(&0u32).is_some() {
        ctx.db.map_config().id().update(MapConfig { id: 0, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius });
    } else {
        ctx.db.map_config().insert(MapConfig { id: 0, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius });
    }
    log::info!("add_chunks: {} tiles inserted ({} chunks, area {})", chunk_cols * chunk_rows * area, chunk_cols * chunk_rows, area);
    Ok(())
}

#[spacetimedb::reducer]
pub fn clear_chunks(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let count = ctx.db.building_tile().iter().count();
    for tile in ctx.db.building_tile().iter() {
        ctx.db.building_tile().tile_id().delete(&tile.tile_id);
    }
    log::info!("clear_chunks: {} tiles deleted.", count);
    Ok(())
}

pub fn find_player_by_username(ctx: &ReducerContext, lookup: &str) -> Option<Identity> {
    let lower = lookup.to_lowercase();
    let hex = lower.trim_start_matches("0x");
    for p in ctx.db.logged_in_player().iter().chain(ctx.db.logged_out_player().iter()) {
        let id_hex = p.player_id.to_hex().to_string().to_lowercase();
        if (!p.username.is_empty() && p.username.to_lowercase() == lower)
            || id_hex == hex
            || (!hex.is_empty() && id_hex.starts_with(hex))
        {
            return Some(p.player_id);
        }
    }
    None
}
