use log::Log;
use spacetimedb::{ReducerContext, Table};
use std::time::Duration;
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::player::methods::*;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;

#[spacetimedb::reducer]
pub fn set_username(ctx: &ReducerContext, username: String) -> Result<(), String> {
    if username.trim().is_empty() || username.len() > 20 {
        return Err("Username must be 1-20 non-whitespace characters.".to_string());
    }
    if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        ctx.db.logged_in_player().player_id().update(LoggedInPlayer { username, ..p });
    } else if let Some(p) = ctx.db.logged_out_player().player_id().find(ctx.sender()) {
        ctx.db.logged_out_player().player_id().update(LoggedOutPlayer { username, ..p });
    } else {
        return Err("Player not found.".to_string());
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn create_profile(ctx: &ReducerContext, profile_name: String, texture_id: String) -> Result<(), String> {
    let _ = require_in_lobby(ctx)?;
    if profile_name.trim().is_empty() || profile_name.len() > 20 {
        return Err("Profile name must be 1-20 non-whitespace characters.".to_string());
    }
    let existing: Vec<_> = ctx.db.player_profile().player_id().filter(&ctx.sender()).collect();
    if existing.len() >= 3 {
        return Err("Maximum 3 profiles allowed.".to_string());
    }
    let lower = profile_name.to_lowercase();
    if existing.iter().any(|p| p.profile_name.to_lowercase() == lower) {
        return Err(format!("Profile '{}' already exists.", profile_name));
    }
    ctx.db.player_profile().insert(PlayerProfile {
        profile_id: 0, player_id: ctx.sender(), profile_name, texture_id, aim_assist: false, lock_on: false,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn delete_profile(ctx: &ReducerContext, profile_id: u64) -> Result<(), String> {
    let _ = require_in_lobby(ctx)?;
    let profile = ctx.db.player_profile().profile_id().find(&profile_id)
        .ok_or("Profile not found.")?;
    if profile.player_id != ctx.sender() {
        return Err("Not your profile.".to_string());
    }
    ctx.db.player_data().profile_id().delete(&profile_id);
    ctx.db.player_stats().profile_id().delete(&profile_id);
    ctx.db.player_inventory().profile_id().delete(&profile_id);
    ctx.db.player_profile().profile_id().delete(&profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn join_world(ctx: &ReducerContext, profile_id: u64) -> Result<(), String> {
    let lobby = require_in_lobby(ctx)?;
    let profile = ctx.db.player_profile().profile_id().find(&profile_id).ok_or("Profile not found.")?;
    if profile.player_id != ctx.sender() { return Err("Not your profile.".to_string()); }

    ctx.db.logged_out_player().player_id().delete(ctx.sender());
    ctx.db.logged_in_player().insert(LoggedInPlayer {player_id: lobby.player_id, username: lobby.username, is_admin: lobby.is_admin, profile_id});
    try_scaffold_profile(ctx, profile_id);
    
    log::info!("Player {:?} joined world with profile {}", ctx.sender(), profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn leave_world(ctx: &ReducerContext) -> Result<(), String> {
    let player = ctx.db.logged_in_player().player_id().find(ctx.sender()).ok_or("Not in world.")?;
    ctx.db.logged_in_player().player_id().delete(ctx.sender());
    ctx.db.logged_out_player().insert(LoggedOutPlayer { player_id: player.player_id, username: player.username, is_admin: player.is_admin});
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_movement(ctx: &ReducerContext, x: f32, y: f32, rotation: f32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let pos = ctx.db.player_position().profile_id().find(&profile_id)
        .ok_or_else(|| format!("Position not found for profile {}", profile_id))?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(0);
    let chunk_rows = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(0);
    let (x, y) = wrap_world_pos(x, y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    ctx.db.player_position().profile_id().update(PlayerPosition { x, y, rotation, chunk_index, ..pos });
    Ok(())
}

#[spacetimedb::reducer]
pub fn update_inventory(ctx: &ReducerContext, slots: Vec<InventorySlot>) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let inv = ctx.db.player_inventory().profile_id().find(&profile_id).ok_or("Inventory not found.")?;
    if slots.len() != inv.slots.len() {
        return Err("Inventory size cannot be changed this way.".to_string());
    }
    for (i, (new_slot, existing_slot)) in slots.iter().zip(inv.slots.iter()).enumerate() {
        if new_slot.allowed_types != existing_slot.allowed_types {
            return Err(format!("Slot {} allowed types cannot be changed.", i));
        }
        if let Some(item_id) = &new_slot.item_id {
            let item = ctx.db.item().item_id().find(item_id)
                .ok_or_else(|| format!("Item '{}' not found.", item_id))?;
            if !new_slot.allowed_types.contains(&item.item_type) {
                return Err(format!("Item '{}' is not allowed in slot {}.", item_id, i));
            }
        }
    }
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_hit(ctx: &ReducerContext, attack: SequenceStepRef) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let base_damage = match attack {
        SequenceStepRef::Single(def_id) => ctx.db.single_step_def().def_id().find(&def_id).map(|s| s.damage),
        SequenceStepRef::Repeat(repeat_id) => ctx.db.repeat_step_instance().repeat_id().find(&repeat_id).map(|r| r.damage),
        SequenceStepRef::Multi(multi_ref) => ctx.db.multi_step_def().def_id().find(&multi_ref.def_id).and_then(|m| m.shots.get(multi_ref.shot_index as usize).map(|s| s.damage)),
    }.ok_or("Attack step not found.")?;

    let damage = compute_incoming_damage(ctx, profile_id, base_damage);
    let data = ctx.db.player_data().profile_id().find(&profile_id).ok_or("Player data not found.")?;
    let new_hp = data.hp.saturating_sub(damage);
    match new_hp {
        0 => {
            ctx.db.player_data().profile_id().delete(profile_id);
            ctx.db.player_inventory().profile_id().delete(profile_id);
            ctx.db.player_stats().profile_id().delete(profile_id);
            ctx.db.player_position().profile_id().delete(profile_id);

            ctx.db.logged_in_player().player_id().delete(ctx.sender());
            ctx.db.logged_out_player().insert(LoggedOutPlayer {player_id: ctx.sender(), username: player.username, is_admin: player.is_admin});
        },
        _ =>  { ctx.db.player_data().profile_id().update(PlayerData { hp: new_hp, ..data }); }
            
    }

    Ok(()) 
}

#[spacetimedb::reducer]
pub fn pickup_drop(ctx: &ReducerContext, drop_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let drop = ctx.db.loot_drop().drop_id().find(&drop_id).ok_or("Drop not found.")?;
    if drop.item_id.is_empty() { return Ok(()); }
    let drop_item_id = drop.item_id.clone();
    let inv = ctx.db.player_inventory().profile_id().find(&profile_id).ok_or("Inventory not found.")?;
    let empty_slot = inv.slots.iter().enumerate()
        .skip(14).take(9)
        .find(|(_, s)| s.item_id.is_none())
        .map(|(i, _)| i)
        .ok_or("No empty slots available.")?;
    let mut slots = inv.slots.clone();
    slots[empty_slot].item_id = Some(drop_item_id);
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    ctx.db.loot_drop().drop_id().delete(&drop_id);
    Ok(())
}
