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

    if item.equip_slot == EquipSlot::Ability {
        // Multi-slot ability items auto-equip into the first contiguous free span, falling
        // back to loose single-cell storage in the backpack (slot_cost only applies in the
        // ability region). Auto-equip respects stat requirements — unmet requirements just
        // divert the item to the backpack.
        if check_stat_requirements(ctx, profile_id, &item).is_ok() {
            if let Some(head) = find_free_span(ctx, profile_id, item.slot_cost.max(1)) {
                place_span(ctx, profile_id, head, &item, drop.enchantment_ids.clone());
                ctx.db.loot_drop().drop_id().delete(&drop_id);
                recompute_stats(ctx, profile_id);
                return Ok(());
            }
        }
    }

    {
        let slots: Vec<PlayerInventorySlot> = ctx.db.player_inventory_slot().profile_id().filter(&profile_id).collect();
        let is_free = |s: &&PlayerInventorySlot| s.item_id.is_none() && s.occupied_by.is_none() && slot_allowed(s.role, item.equip_slot);
        // Prefer a matching typed slot, fall back to general. Ability-role cells are skipped —
        // ability items only enter the region through span placement, never loose. A typed
        // (equipped) slot also requires the item's stat requirements — otherwise backpack.
        let target = slots.iter().filter(|s| s.role != SlotRole::General && s.role != SlotRole::Ability).find(is_free)
            .filter(|_| check_stat_requirements(ctx, profile_id, &item).is_ok())
            .or_else(|| slots.iter().filter(|s| s.role == SlotRole::General).find(is_free))
            .ok_or("No suitable slot available.")?;
        update_slot(ctx, PlayerInventorySlot {
            item_id: Some(drop.item_id.clone()), enchantment_ids: drop.enchantment_ids.clone(), ..target.clone()
        });
    }

    ctx.db.loot_drop().drop_id().delete(&drop_id);
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn drop_item(ctx: &ReducerContext, slot_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let mut slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    // Dropping a follower cell drops the whole span.
    if let Some(head) = slot.occupied_by {
        slot = get_slot(ctx, profile_id, head).ok_or("Span head not found.")?;
    }
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let enchantment_ids = slot.enchantment_ids.clone();
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;

    if slot.role == SlotRole::Ability {
        clear_span(ctx, profile_id, slot.slot_index, item.slot_cost);
    } else {
        update_slot(ctx, PlayerInventorySlot { item_id: None, enchantment_ids: vec![], ..slot });
    }

    ctx.db.loot_drop().insert(LootDrop {
        drop_id: 0, item_id, x: pos.x, y: pos.y, chunk_index: pos.chunk_index,
        expires_at: ctx.timestamp + Duration::from_secs(LOOT_DROP_EXPIRY),
        dropped_by: Some(ctx.sender()),
        enchantment_ids,
    });
    recompute_stats(ctx, profile_id);
    Ok(())
}

/// Equipped roles — the ones recompute_stats reads stats from. Stat requirements gate
/// an item *entering* one of these from loose (General) storage.
fn is_equip_role(role: SlotRole) -> bool {
    matches!(role, SlotRole::Weapon | SlotRole::Accessory | SlotRole::Armor | SlotRole::Ability)
}

#[spacetimedb::reducer]
pub fn swap_slots(ctx: &ReducerContext, from_index: u32, to_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;
    if from_index == to_index { return Ok(()); }

    // Follower cells stand in for their span's head on the source side. On the target side,
    // only resolve followers of a *different* span — targeting your own span's follower is a
    // nudge (the head should move there), not a no-op.
    let resolve_head = |index: u32| -> Result<PlayerInventorySlot, String> {
        let slot = get_slot(ctx, profile_id, index).ok_or("Invalid slot index.")?;
        match slot.occupied_by {
            Some(head) => get_slot(ctx, profile_id, head).ok_or_else(|| "Span head not found.".to_string()),
            None => Ok(slot),
        }
    };
    let from = resolve_head(from_index)?;
    let to_raw = get_slot(ctx, profile_id, to_index).ok_or("Invalid slot index.")?;
    let to = match to_raw.occupied_by {
        Some(head) if head != from.slot_index => get_slot(ctx, profile_id, head).ok_or_else(|| "Span head not found.".to_string())?,
        _ => to_raw,
    };
    if from.slot_index == to.slot_index { return Ok(()); }

    let find_item = |slot: &PlayerInventorySlot| -> Result<Option<Item>, String> {
        slot.item_id.as_ref()
            .map(|id| ctx.db.item().item_id().find(id).ok_or_else(|| "Item not found.".to_string()))
            .transpose()
    };
    let from_item = find_item(&from)?;
    let to_item = find_item(&to)?;

    if let Some(item) = &from_item {
        if !slot_allowed(to.role, item.equip_slot) {
            return Err(format!("Item '{}' is not allowed in slot {}.", item.item_id, to.slot_index));
        }
    }
    if let Some(item) = &to_item {
        if !slot_allowed(from.role, item.equip_slot) {
            return Err(format!("Item '{}' is not allowed in slot {}.", item.item_id, from.slot_index));
        }
    }
    // Stat-requirement gates (doc 03 class thresholds): an item entering an equipped role
    // from loose storage must have its requirements met. Covers both the plain-swap and
    // the ability-region paths below.
    if let Some(item) = &from_item {
        if is_equip_role(to.role) && !is_equip_role(from.role) {
            check_stat_requirements(ctx, profile_id, item)?;
        }
    }
    if let Some(item) = &to_item {
        if is_equip_role(from.role) && !is_equip_role(to.role) {
            check_stat_requirements(ctx, profile_id, item)?;
        }
    }

    let from_in_region = from.role == SlotRole::Ability;
    let to_in_region = to.role == SlotRole::Ability;

    if from_in_region || to_in_region {
        // A span can't 1:1 swap — the receiving side must be empty. The acceptance checks
        // above have already forced the moving item to be an Ability item.
        if from_item.is_some() && to_item.is_some() {
            return Err("Ability items occupy multiple slots and cannot be swapped onto an occupied slot.".to_string());
        }
        let moving = from_item.as_ref().map(|i| (i, &from, &to))
            .or_else(|| to_item.as_ref().map(|i| (i, &to, &from)));
        if let Some((item, src, dst)) = moving {
            let cost = item.slot_cost.max(1);
            // Carried runtime state (cooldown/charges/toggle) travels with the item — except
            // when entering the ability region from loose backpack storage, where charges
            // initialize from the item (the backpack cell's unlimited default is meaningless).
            let (item_id, enchantment_ids, cooldown_until, charges, active_toggle) =
                (src.item_id.clone(), src.enchantment_ids.clone(), src.cooldown_until, src.charges, src.active_toggle);
            let (cooldown_until, charges) = if dst.role == SlotRole::Ability && src.role != SlotRole::Ability {
                (None, ability_behavior(item)
                    .map(|a| if a.max_charges == 0 { u32::MAX } else { a.max_charges })
                    .unwrap_or(u32::MAX))
            } else {
                (cooldown_until, charges)
            };
            if dst.role == SlotRole::Ability {
                // The run starting exactly at the target must be free once the source is vacated.
                let src_span = if src.role == SlotRole::Ability { cost } else { 1 };
                let fits = (dst.slot_index..dst.slot_index + cost).all(|i| {
                    if i >= src.slot_index && i < src.slot_index + src_span { return true; } // freed by the move
                    get_slot(ctx, profile_id, i)
                        .map(|s| s.role == SlotRole::Ability && s.item_id.is_none() && s.occupied_by.is_none())
                        .unwrap_or(false)
                });
                if !fits {
                    return Err(format!("Item '{}' needs {} contiguous free ability slots at index {}.", item.item_id, cost, dst.slot_index));
                }
            }
            if src.role == SlotRole::Ability {
                clear_span(ctx, profile_id, src.slot_index, cost);
            } else {
                update_slot(ctx, PlayerInventorySlot { item_id: None, enchantment_ids: vec![], ..src.clone() });
            }
            if let Some(head) = get_slot(ctx, profile_id, dst.slot_index) {
                update_slot(ctx, PlayerInventorySlot {
                    item_id, enchantment_ids, cooldown_until, charges, active_toggle, occupied_by: None, ..head
                });
            }
            if dst.role == SlotRole::Ability {
                for i in dst.slot_index + 1..dst.slot_index + cost {
                    if let Some(follower) = get_slot(ctx, profile_id, i) {
                        update_slot(ctx, PlayerInventorySlot {
                            item_id: None, enchantment_ids: vec![], occupied_by: Some(dst.slot_index),
                            cooldown_until: None, charges: u32::MAX, active_toggle: 0, ..follower
                        });
                    }
                }
            }
        }
    } else {
        // Neither side is in the ability region — plain single-cell swap, even for ability
        // items stored loose in the backpack (slot_cost only applies in the ability region).
        update_slot(ctx, PlayerInventorySlot { item_id: to.item_id.clone(), enchantment_ids: to.enchantment_ids.clone(), ..from.clone() });
        update_slot(ctx, PlayerInventorySlot { item_id: from.item_id.clone(), enchantment_ids: from.enchantment_ids.clone(), ..to.clone() });
    }
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

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    let consumable = consumable_behavior(&item)
        .ok_or("Only consumable items can be used.")?
        .clone();
    apply_consumable_effect(ctx, profile_id, &consumable);

    update_slot(ctx, PlayerInventorySlot { item_id: None, ..slot });
    recompute_stats(ctx, profile_id);
    Ok(())
}

// Applies the position multiplier to an ability's effect: Heal scales through potency, but
// Buff amounts and bullet-control radii live inside the effect variant, so those are scaled
// here directly.
fn scale_ability_effect(effect: &AbilityEffect, multiplier: f32) -> AbilityEffect {
    match effect {
        AbilityEffect::Heal => AbilityEffect::Heal,
        AbilityEffect::Buff(buff) => AbilityEffect::Buff(match buff {
            ConsumableBuffEffect::Strength(v) => ConsumableBuffEffect::Strength(v * multiplier),
            ConsumableBuffEffect::Wisdom(v) => ConsumableBuffEffect::Wisdom(v * multiplier),
            ConsumableBuffEffect::Dexterity(v) => ConsumableBuffEffect::Dexterity(v * multiplier),
            ConsumableBuffEffect::DamageDealer(v) => ConsumableBuffEffect::DamageDealer(v * multiplier),
            ConsumableBuffEffect::Supporter(v) => ConsumableBuffEffect::Supporter(v * multiplier),
            ConsumableBuffEffect::Artisan(v) => ConsumableBuffEffect::Artisan(v * multiplier),
        }),
        AbilityEffect::DeleteBullets(r) => AbilityEffect::DeleteBullets(r * multiplier),
        AbilityEffect::SplitBullets(r) => AbilityEffect::SplitBullets(r * multiplier),
        AbilityEffect::AttractBullets(r) => AbilityEffect::AttractBullets(r * multiplier),
    }
}

#[spacetimedb::reducer]
pub fn activate_ability(ctx: &ReducerContext, slot_index: u32, target_x: f32, target_y: f32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    if slot.role != SlotRole::Ability { return Err("Not an ability slot.".to_string()); }
    if slot.occupied_by.is_some() { return Err("Activate the ability's head slot.".to_string()); }
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    let ability = ability_behavior(&item).ok_or("Item has no ability.")?.clone();

    if let Some(until) = slot.cooldown_until {
        if ctx.timestamp < until { return Err("Ability is on cooldown.".to_string()); }
    }
    if slot.charges == 0 { return Err("Ability is out of charges.".to_string()); }

    // Unlike use_item, the item is not consumed — it goes on cooldown and may spend a charge.
    // Potency scales by span-head position (first ability stronger).
    let multiplier = ability_position_multiplier(slot.slot_index);
    let effect = scale_ability_effect(&ability.effect, multiplier);
    match &effect {
        AbilityEffect::Heal | AbilityEffect::Buff(_) => {
            let consumable_effect = match &effect {
                AbilityEffect::Heal => ConsumableEffect::Heal,
                AbilityEffect::Buff(b) => ConsumableEffect::Buff(b.clone()),
                _ => unreachable!(),
            };
            apply_consumable_effect(ctx, profile_id, &ConsumableBehavior {
                effect: consumable_effect,
                potency: ability.potency * multiplier,
                duration: ability.duration,
            });
        }
        AbilityEffect::DeleteBullets(radius) | AbilityEffect::SplitBullets(radius) | AbilityEffect::AttractBullets(radius) => {
            // Clamp the client-supplied cursor target into range of the server's own
            // position row, then relay the cast as a BulletControlEvent — the server tracks
            // no live bullets; every client resolves the proximity query locally.
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let (dx, dy) = (target_x - pos.x, target_y - pos.y);
            let dist = (dx * dx + dy * dy).sqrt();
            let (tx, ty) = if dist > MAX_ABILITY_TARGET_RANGE {
                (pos.x + dx / dist * MAX_ABILITY_TARGET_RANGE, pos.y + dy / dist * MAX_ABILITY_TARGET_RANGE)
            } else {
                (target_x, target_y)
            };
            let kind = match &effect {
                AbilityEffect::DeleteBullets(_) => BulletControlKind::Delete,
                AbilityEffect::SplitBullets(_) => BulletControlKind::Split,
                _ => BulletControlKind::Attract,
            };
            ctx.db.bullet_control_event().insert(BulletControlEvent {
                event_id: 0, kind, x: tx, y: ty, radius: *radius, target_x: tx, target_y: ty, cast_by: ctx.sender(),
            });
        }
    }

    // "Whenever you use an ability..." enchantment triggers (doc 03).
    for b in equipped_enchantment_behaviors(ctx, profile_id) {
        if let EnchantmentBehavior::OnAbilityUseBuff(params) = b {
            apply_consumable_effect(ctx, profile_id, &ConsumableBehavior {
                effect: ConsumableEffect::Buff(params.buff),
                potency: 0.0,
                duration: params.duration,
            });
        }
    }

    let charges = if ability.max_charges == 0 { u32::MAX } else { slot.charges - 1 };
    update_slot(ctx, PlayerInventorySlot {
        cooldown_until: Some(ctx.timestamp + Duration::from_secs_f32(ability.cooldown_seconds)),
        charges,
        ..slot
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn set_slot_toggle(ctx: &ReducerContext, slot_index: u32, value: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    if slot.occupied_by.is_some() { return Err("Cannot toggle a span follower slot.".to_string()); }
    if slot.role != SlotRole::Weapon { return Err("Only the weapon slot has shot-pattern toggles.".to_string()); }
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    // Toggle options = the item's own Weapon behaviors + WeaponToggle enchantment grants;
    // the same list resolve_effective_weapon indexes into.
    let count = weapon_toggle_options(ctx, profile_id, &item).len() as u32;
    if count < 2 { return Err("Item has no toggle patterns.".to_string()); }
    if value >= count { return Err(format!("Toggle index {} out of range ({} patterns).", value, count)); }

    update_slot(ctx, PlayerInventorySlot { active_toggle: value, ..slot });
    Ok(())
}

#[spacetimedb::reducer]
pub fn allocate_stat(ctx: &ReducerContext, stat: StatKind, points: u32) -> Result<(), String> {
    let _player = require_in_world(ctx)?;
    let profile_id = _player.profile_id;
    if points == 0 { return Err("No points requested.".to_string()); }

    let mut alloc = ctx.db.player_stat_allocation().profile_id().find(&profile_id)
        .ok_or("Stat allocation not found.")?;
    if alloc.unspent_points < points { return Err("Not enough unspent skill points.".to_string()); }

    alloc.unspent_points -= points;
    match stat {
        StatKind::Strength => alloc.strength += points,
        StatKind::Wisdom => alloc.wisdom += points,
        StatKind::Dexterity => alloc.dexterity += points,
        StatKind::DamageDealer => alloc.damage_dealer += points,
        StatKind::Supporter => alloc.supporter += points,
        StatKind::Artisan => alloc.artisan += points,
        StatKind::Hp | StatKind::Defense => return Err("Hp and Defense come from gear, not allocation.".to_string()),
    }
    ctx.db.player_stat_allocation().profile_id().update(alloc);
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn apply_enchantment(ctx: &ReducerContext, slot_index: u32, enchantment_id: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
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

    let mut enchantment_ids = slot.enchantment_ids.clone();
    enchantment_ids.push(enchantment_id);
    update_slot(ctx, PlayerInventorySlot { enchantment_ids, ..slot });
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_enchantment(ctx: &ReducerContext, slot_index: u32, enchantment_id: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    if !slot.enchantment_ids.contains(&enchantment_id) {
        return Err(format!("Enchantment '{}' is not applied to this item.", enchantment_id));
    }

    let mut enchantment_ids = slot.enchantment_ids.clone();
    enchantment_ids.retain(|id| id != &enchantment_id);
    update_slot(ctx, PlayerInventorySlot { enchantment_ids, ..slot });
    recompute_stats(ctx, profile_id);
    Ok(())
}
