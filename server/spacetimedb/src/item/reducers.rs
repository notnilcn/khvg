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

// ===== Static-data staging (Phase 0B) =====

/// Stage an item revision without touching the live catalog row.
#[spacetimedb::reducer]
pub fn stage_item(ctx: &ReducerContext, item: Item) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if item.item_id.trim().is_empty() { return Err("item_id cannot be empty.".to_string()); }
    if item.slot_cost == 0 { return Err("slot_cost must be >= 1.".to_string()); }
    let staged = StagedItem {
        item_id: item.item_id.clone(), display_name: item.display_name.clone(),
        description: item.description.clone(), texture_id: item.texture_id.clone(),
        equip_slot: item.equip_slot, slot_cost: item.slot_cost,
        stat_modifiers: item.stat_modifiers.clone(), behaviors: item.behaviors.clone(),
        max_enchantments: item.max_enchantments,
        innate_enchantment_ids: item.innate_enchantment_ids.clone(),
        stat_requirements: item.stat_requirements,
        staged_by: ctx.sender().to_hex().to_string(),
    };
    if ctx.db.staged_item().item_id().find(&staged.item_id).is_some() {
        ctx.db.staged_item().item_id().update(staged);
    } else {
        ctx.db.staged_item().insert(staged);
    }
    Ok(())
}

/// Drop all staged item rows.
#[spacetimedb::reducer]
pub fn clear_staged_items(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    for row in ctx.db.staged_item().iter().collect::<Vec<_>>() {
        ctx.db.staged_item().item_id().delete(&row.item_id);
    }
    Ok(())
}

/// Validate staged items (stat-requirement/range checks mirroring `Item::seed`).
/// Bad rows are reported by id; live rows are untouched. Returns Err listing the
/// offenders so the caller knows what to fix before import.
#[spacetimedb::reducer]
pub fn validate_staged_items(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let mut bad: Vec<String> = vec![];
    for row in ctx.db.staged_item().iter() {
        if row.item_id.trim().is_empty() || row.slot_cost == 0 || row.slot_cost > 6 {
            bad.push(row.item_id.clone());
            continue;
        }
        for id in row.innate_enchantment_ids.iter() {
            if ctx.db.enchantment().enchantment_id().find(id).is_none()
                && ctx.db.staged_enchantment().enchantment_id().find(id).is_none() {
                bad.push(row.item_id.clone());
                break;
            }
        }
    }
    if bad.is_empty() { Ok(()) } else { Err(format!("Invalid staged items: {}", bad.join(", "))) }
}

/// Import (upsert) all staged items into the live catalog by primary key.
#[spacetimedb::reducer]
pub fn import_staged_items(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    for row in ctx.db.staged_item().iter().collect::<Vec<_>>() {
        let item = Item {
            item_id: row.item_id.clone(), display_name: row.display_name.clone(),
            description: row.description.clone(), texture_id: row.texture_id.clone(),
            equip_slot: row.equip_slot, slot_cost: row.slot_cost,
            stat_modifiers: row.stat_modifiers.clone(), behaviors: row.behaviors.clone(),
            max_enchantments: row.max_enchantments,
            innate_enchantment_ids: row.innate_enchantment_ids.clone(),
            stat_requirements: row.stat_requirements,
        };
        if ctx.db.item().item_id().find(&item.item_id).is_some() {
            ctx.db.item().item_id().update(item);
        } else {
            ctx.db.item().insert(item);
        }
    }
    Ok(())
}

/// Stage an enchantment revision without touching the live catalog row.
#[spacetimedb::reducer]
pub fn stage_enchantment(ctx: &ReducerContext, enchantment: Enchantment) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if enchantment.enchantment_id.trim().is_empty() { return Err("enchantment_id cannot be empty.".to_string()); }
    let staged = StagedEnchantment {
        enchantment_id: enchantment.enchantment_id.clone(), display_name: enchantment.display_name.clone(),
        description: enchantment.description.clone(), texture_id: enchantment.texture_id.clone(),
        stat_modifiers: enchantment.stat_modifiers.clone(), behaviors: enchantment.behaviors.clone(),
        allowed_slots: enchantment.allowed_slots.clone(),
        staged_by: ctx.sender().to_hex().to_string(),
    };
    if ctx.db.staged_enchantment().enchantment_id().find(&staged.enchantment_id).is_some() {
        ctx.db.staged_enchantment().enchantment_id().update(staged);
    } else {
        ctx.db.staged_enchantment().insert(staged);
    }
    Ok(())
}

/// Drop all staged enchantment rows.
#[spacetimedb::reducer]
pub fn clear_staged_enchantments(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    for row in ctx.db.staged_enchantment().iter().collect::<Vec<_>>() {
        ctx.db.staged_enchantment().enchantment_id().delete(&row.enchantment_id);
    }
    Ok(())
}

/// Validate staged enchantments (non-empty id; slot list may be empty = anywhere).
#[spacetimedb::reducer]
pub fn validate_staged_enchantments(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let mut bad: Vec<String> = vec![];
    for row in ctx.db.staged_enchantment().iter() {
        if row.enchantment_id.trim().is_empty() {
            bad.push(row.enchantment_id.clone());
        }
    }
    if bad.is_empty() { Ok(()) } else { Err(format!("Invalid staged enchantments: {}", bad.join(", "))) }
}

/// Import (upsert) all staged enchantments into the live catalog by primary key.
#[spacetimedb::reducer]
pub fn import_staged_enchantments(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    for row in ctx.db.staged_enchantment().iter().collect::<Vec<_>>() {
        let e = Enchantment {
            enchantment_id: row.enchantment_id.clone(), display_name: row.display_name.clone(),
            description: row.description.clone(), texture_id: row.texture_id.clone(),
            stat_modifiers: row.stat_modifiers.clone(), behaviors: row.behaviors.clone(),
            allowed_slots: row.allowed_slots.clone(),
        };
        if ctx.db.enchantment().enchantment_id().find(&e.enchantment_id).is_some() {
            ctx.db.enchantment().enchantment_id().update(e);
        } else {
            ctx.db.enchantment().insert(e);
        }
    }
    Ok(())
}
