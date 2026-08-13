use spacetimedb::{ReducerContext, Table};
use crate::item::tables::*;
use crate::player::tables::*;
use crate::player::methods::{is_admin, find_profile_by_name, update_slot, clear_span};
use crate::main::admin::find_player_by_username;

#[spacetimedb::reducer]
pub fn give_item(ctx: &ReducerContext, target_username: String, profile_name: String, item_id: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let profile = find_profile_by_name(ctx, target_id, &profile_name)
        .ok_or_else(|| format!("Profile '{}' not found.", profile_name))?;
    let empty_slot = ctx.db.player_inventory_slot().profile_id().filter(&profile.profile_id)
        .find(|s| s.role == SlotRole::General && s.item_id.is_none() && s.occupied_by.is_none())
        .ok_or("No empty general slots available.")?;
    update_slot(ctx, PlayerInventorySlot { item_id: Some(item_id), ..empty_slot });
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_item(ctx: &ReducerContext, target_username: String, profile_name: String, item_id: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let profile = find_profile_by_name(ctx, target_id, &profile_name)
        .ok_or_else(|| format!("Profile '{}' not found.", profile_name))?;
    let matches: Vec<PlayerInventorySlot> = ctx.db.player_inventory_slot().profile_id().filter(&profile.profile_id)
        .filter(|s| s.item_id.as_deref() == Some(item_id.as_str()))
        .collect();
    for slot in matches {
        if slot.role == SlotRole::Ability {
            let cost = ctx.db.item().item_id().find(&item_id).map(|i| i.slot_cost).unwrap_or(1);
            clear_span(ctx, profile.profile_id, slot.slot_index, cost);
        } else {
            update_slot(ctx, PlayerInventorySlot { item_id: None, ..slot });
        }
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_item(ctx: &ReducerContext, item: Item) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.item().item_id().find(&item.item_id).is_some() {
        ctx.db.item().item_id().update(item);
    } else {
        ctx.db.item().insert(item);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_enchantment(ctx: &ReducerContext, enchantment: Enchantment) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.enchantment().enchantment_id().find(&enchantment.enchantment_id).is_some() {
        ctx.db.enchantment().enchantment_id().update(enchantment);
    } else {
        ctx.db.enchantment().insert(enchantment);
    }
    Ok(())
}
