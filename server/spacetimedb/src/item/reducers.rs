use spacetimedb::{ReducerContext, Table};
use crate::item::tables::*;
use crate::player::tables::*;
use crate::player::methods::{is_admin, find_profile_by_name};
use crate::main::admin::find_player_by_username;
use crate::main::global::{GENERAL_SLOT_START, GENERAL_SLOT_COUNT};

#[spacetimedb::reducer]
pub fn give_item(ctx: &ReducerContext, target_username: String, profile_name: String, item_id: String) -> Result<(), String> 
{
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let profile = find_profile_by_name(ctx, target_id, &profile_name)
        .ok_or_else(|| format!("Profile '{}' not found.", profile_name))?;
    let inv = ctx.db.player_inventory().profile_id().find(&profile.profile_id).ok_or("Inventory not found.")?;
    let empty_slot = inv.slots.iter().enumerate()
        .skip(GENERAL_SLOT_START).take(GENERAL_SLOT_COUNT)
        .find(|(_, s)| s.item_id.is_none())
        .map(|(i, _)| i)
        .ok_or("No empty general slots available.")?;
    let mut slots = inv.slots.clone();
    slots[empty_slot].item_id = Some(item_id);
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_item(ctx: &ReducerContext, target_username: String, profile_name: String, item_id: String) -> Result<(), String> 
{
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let profile = find_profile_by_name(ctx, target_id, &profile_name)
        .ok_or_else(|| format!("Profile '{}' not found.", profile_name))?;
    let inv = ctx.db.player_inventory().profile_id().find(&profile.profile_id).ok_or("Inventory not found.")?;
    let slots: Vec<InventorySlot> = inv.slots.iter().map(|s| {
        if s.item_id.as_deref() == Some(item_id.as_str()) {
            InventorySlot { item_id: None, ..s.clone() }
        } else { s.clone() }
    }).collect();
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_item(ctx: &ReducerContext, item: Item) -> Result<(), String> 
{
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.item().item_id().find(&item.item_id).is_some() {
        ctx.db.item().item_id().update(item);
    } else {
        ctx.db.item().insert(item);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_enchantment(ctx: &ReducerContext, enchantment: Enchantment) -> Result<(), String> 
{
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.enchantment().enchantment_id().find(&enchantment.enchantment_id).is_some() {
        ctx.db.enchantment().enchantment_id().update(enchantment);
    } else {
        ctx.db.enchantment().insert(enchantment);
    }
    Ok(())
}
