use std::time::Duration;
use spacetimedb::{ReducerContext, Table};
use crate::main::global::*;
use crate::world::hex::{spiral_chunk_index, world_to_chunk};
use crate::world::wrap::wrap_world_pos;
use crate::world::tables::MapConfig;
use crate::player::tables::*;
use crate::player::methods::*;
use crate::combat::deal_damage_to_player;
use crate::item::tables::*;
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
    teardown_profile(ctx, profile_id);
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
    let cfg = MapConfig::load(ctx, 0, 0);
    let (x, y) = wrap_world_pos(x, y, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, cfg.chunk_hex_radius, cfg.hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    ctx.db.player_position().profile_id().update(PlayerPosition { x, y, rotation, chunk_index, ..pos });

    if let Some(chunk) = ctx.db.player_chunk().profile_id().find(&profile_id) {
        if (chunk.chunk_q, chunk.chunk_r) != (cq, cr) {
            ctx.db.player_chunk().profile_id().update(PlayerChunk { chunk_q: cq, chunk_r: cr, ..chunk });
        }
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_hit(ctx: &ReducerContext, attack: SequenceStepRef) -> Result<(), String> {
    let player = require_in_world(ctx)?;

    let base_damage = match attack {
        SequenceStepRef::Single(def_id) => ctx.db.single_step_def().def_id().find(&def_id).map(|s| s.damage),
        SequenceStepRef::Repeat(repeat_id) => ctx.db.repeat_step_instance().repeat_id().find(&repeat_id).map(|r| r.damage),
        SequenceStepRef::Multi(multi_ref) => ctx.db.multi_step_def().def_id().find(&multi_ref.def_id).and_then(|m| m.shots.get(multi_ref.shot_index as usize).map(|s| s.damage)),
    }.ok_or("Attack step not found.")?;

    deal_damage_to_player(ctx, &player, base_damage)?;
    Ok(())
}

#[spacetimedb::reducer]
pub fn pickup_drop(ctx: &ReducerContext, drop_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let drop = ctx.db.loot_drop().drop_id().find(&drop_id).ok_or("Drop not found.")?;
    if drop.item_id.is_empty() { return Ok(()); }
    let item = ctx.db.item().item_id().find(&drop.item_id).ok_or("Item not found.")?;
    let inv = ctx.db.player_inventory().profile_id().find(&profile_id).ok_or("Inventory not found.")?;

    let general_range = GENERAL_SLOT_START..GENERAL_SLOT_START + GENERAL_SLOT_COUNT;
    let target = inv.slots.iter().enumerate()
        .filter(|(i, _)| !general_range.contains(i))
        .find(|(_, s)| s.item_id.is_none() && s.allowed_slots.contains(&item.equip_slot))
        .or_else(|| inv.slots.iter().enumerate()
            .skip(GENERAL_SLOT_START).take(GENERAL_SLOT_COUNT)
            .find(|(_, s)| s.item_id.is_none() && s.allowed_slots.contains(&item.equip_slot)))
        .map(|(i, _)| i)
        .ok_or("No suitable slot available.")?;

    let mut slots = inv.slots.clone();
    slots[target].item_id = Some(drop.item_id.clone());
    slots[target].enchantment_ids = drop.enchantment_ids.clone();
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    ctx.db.loot_drop().drop_id().delete(&drop_id);
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn drop_item(ctx: &ReducerContext, slot_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;
    let slot_index = slot_index as usize;

    let inv = ctx.db.player_inventory().profile_id().find(&profile_id).ok_or("Inventory not found.")?;
    let slot = inv.slots.get(slot_index).ok_or("Invalid slot index.")?;
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let enchantment_ids = slot.enchantment_ids.clone();
    let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;

    let mut slots = inv.slots.clone();
    slots[slot_index].item_id = None;
    slots[slot_index].enchantment_ids = vec![];
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });

    ctx.db.loot_drop().insert(LootDrop {
        drop_id: 0, item_id, x: pos.x, y: pos.y, chunk_index: pos.chunk_index,
        expires_at: ctx.timestamp + Duration::from_secs(LOOT_DROP_EXPIRY),
        dropped_by: Some(ctx.sender()),
        enchantment_ids,
    });
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn swap_slots(ctx: &ReducerContext, from_index: u32, to_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;
    let (from_index, to_index) = (from_index as usize, to_index as usize);

    let inv = ctx.db.player_inventory().profile_id().find(&profile_id).ok_or("Inventory not found.")?;
    if from_index == to_index { return Ok(()); }
    let from = inv.slots.get(from_index).ok_or("Invalid slot index.")?.clone();
    let to = inv.slots.get(to_index).ok_or("Invalid slot index.")?.clone();

    if let Some(item_id) = &from.item_id {
        let item = ctx.db.item().item_id().find(item_id).ok_or("Item not found.")?;
        if !to.allowed_slots.contains(&item.equip_slot) {
            return Err(format!("Item '{}' is not allowed in slot {}.", item_id, to_index));
        }
    }
    if let Some(item_id) = &to.item_id {
        let item = ctx.db.item().item_id().find(item_id).ok_or("Item not found.")?;
        if !from.allowed_slots.contains(&item.equip_slot) {
            return Err(format!("Item '{}' is not allowed in slot {}.", item_id, from_index));
        }
    }

    let mut slots = inv.slots.clone();
    slots[from_index].item_id = to.item_id;
    slots[to_index].item_id = from.item_id;
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn tick_active_consumable_effects(ctx: &ReducerContext, _job: ConsumableEffectSchedule) {
    let mut affected_profiles = Vec::new();
    for active in ctx.db.active_consumable_effect().iter() {
        let remaining = active.remaining - CONSUMABLE_EFFECT_TICK_SECONDS;
        if remaining <= 0.0 {
            ctx.db.active_consumable_effect().effect_id().delete(&active.effect_id);
            affected_profiles.push(active.profile_id);
        } else {
            ctx.db.active_consumable_effect().effect_id().update(ActiveConsumableEffect { remaining, ..active });
        }
    }
    for profile_id in affected_profiles {
        recompute_stats(ctx, profile_id);
    }
}

#[spacetimedb::reducer]
pub fn use_item(ctx: &ReducerContext, slot_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;
    let slot_index = slot_index as usize;

    let inv = ctx.db.player_inventory().profile_id().find(&profile_id).ok_or("Inventory not found.")?;
    let slot = inv.slots.get(slot_index).ok_or("Invalid slot index.")?;
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    let consumable = consumable_behavior(&item)
        .ok_or("Only consumable items can be used.")?
        .clone();
    apply_consumable_effect(ctx, profile_id, &consumable);

    let mut slots = inv.slots.clone();
    slots[slot_index].item_id = None;
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn apply_enchantment(ctx: &ReducerContext, slot_index: u32, enchantment_id: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;
    let slot_index = slot_index as usize;

    let inv = ctx.db.player_inventory().profile_id().find(&profile_id).ok_or("Inventory not found.")?;
    let slot = inv.slots.get(slot_index).ok_or("Invalid slot index.")?;
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    let enchantment = ctx.db.enchantment().enchantment_id().find(&enchantment_id)
        .ok_or_else(|| format!("Enchantment '{}' not found.", enchantment_id))?;
    if !enchantment.allowed_slots.contains(&item.equip_slot) {
        return Err(format!("Enchantment '{}' cannot be applied to '{}'.", enchantment_id, item_id));
    }
    if slot.enchantment_ids.len() as u32 >= item.max_enchantments {
        return Err(format!("Item '{}' cannot hold more enchantments.", item_id));
    }
    if slot.enchantment_ids.contains(&enchantment_id) {
        return Err(format!("Enchantment '{}' is already applied to this item.", enchantment_id));
    }

    let mut slots = inv.slots.clone();
    slots[slot_index].enchantment_ids.push(enchantment_id);
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_enchantment(ctx: &ReducerContext, slot_index: u32, enchantment_id: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;
    let slot_index = slot_index as usize;

    let inv = ctx.db.player_inventory().profile_id().find(&profile_id).ok_or("Inventory not found.")?;
    let slot = inv.slots.get(slot_index).ok_or("Invalid slot index.")?;
    if !slot.enchantment_ids.contains(&enchantment_id) {
        return Err(format!("Enchantment '{}' is not applied to this item.", enchantment_id));
    }

    let mut slots = inv.slots.clone();
    slots[slot_index].enchantment_ids.retain(|id| id != &enchantment_id);
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    recompute_stats(ctx, profile_id);
    Ok(())
}
