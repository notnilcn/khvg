//! Item behavior lookups and weapon composition: the catalog-side reading of items and
//! enchantments. Pure per-item lookups (consumable/ability behavior, charge capability)
//! take only the item; composition over the player's loadout (equipped enchantment
//! behaviors, toggle options, the effective weapon) reads equipped slots. The client's
//! EffectiveWeaponResolver mirrors resolve_effective_weapon exactly — fold order (slot
//! index ascending, innate before socketed) is load-bearing on both sides.

use spacetimedb::{ReducerContext, Table};
use crate::item::tables::*;
use crate::player::tables::{player_inventory_slot, PlayerInventorySlot, SlotRole};

pub fn consumable_behavior(item: &Item) -> Option<&ConsumableBehavior> {
    item.behaviors.iter().find_map(|b| match b {
        ItemBehavior::Consumable(c) => Some(c),
        _ => None,
    })
}

pub fn ability_behavior(item: &Item) -> Option<&AbilityBehavior> {
    item.behaviors.iter().find_map(|b| match b {
        ItemBehavior::Ability(a) => Some(a),
        _ => None,
    })
}

/// Charge-capable abilities (P4) route through begin/release_ability_charge instead of
/// activate_ability. One helper so a future charge-capable variant adds a single arm
/// here; the client mirrors this check (LocalPlayerInventoryComponent.IsChargeCapable).
pub fn ability_is_charge_capable(ability: &AbilityBehavior) -> bool {
    matches!(&ability.effect, AbilityEffect::Teleport(p) if p.charge_scaled)
        || matches!(&ability.effect, AbilityEffect::Spell(p) if p.charge_scaled)
}

pub fn equipped_head_slots(ctx: &ReducerContext, profile_id: u64, role: SlotRole) -> Vec<PlayerInventorySlot> {
    ctx.db.player_inventory_slot().profile_id().filter(&profile_id)
        .filter(|s| s.role == role && s.occupied_by.is_none() && s.item_id.is_some())
        .collect()
}

/// Every EnchantmentBehavior from the player's equipped items (Weapon/Accessory/Armor/
/// Ability heads; backpack/hotbar don't count), innate enchantments before socketed
/// ones, slots in slot-index order. The order is deterministic and load-bearing:
/// resolve_effective_weapon's toggle indexing and the client's mirror both depend on it.
/// Unlike stat modifiers, behaviors are NOT scaled by ability position.
pub fn equipped_enchantment_behaviors(ctx: &ReducerContext, profile_id: u64) -> Vec<EnchantmentBehavior> {
    let mut heads: Vec<PlayerInventorySlot> = ctx.db.player_inventory_slot().profile_id().filter(&profile_id)
        .filter(|s| s.occupied_by.is_none() && s.item_id.is_some())
        .filter(|s| matches!(s.role, SlotRole::Weapon | SlotRole::Accessory | SlotRole::Armor | SlotRole::Ability))
        .collect();
    heads.sort_by_key(|s| s.slot_index);
    let mut out = Vec::new();
    for slot in heads {
        let item = match slot.item_id.as_ref().and_then(|id| ctx.db.item().item_id().find(id)) {
            Some(i) => i,
            None => continue,
        };
        for enchantment_id in item.innate_enchantment_ids.iter().chain(slot.enchantment_ids.iter()) {
            if let Some(enchantment) = ctx.db.enchantment().enchantment_id().find(enchantment_id) {
                out.extend(enchantment.behaviors.iter().cloned());
            }
        }
    }
    out
}

/// The toggle options of an item: its own Weapon behaviors first, then WeaponToggle
/// grants from the player's equipped enchantments.
pub fn weapon_toggle_options(ctx: &ReducerContext, profile_id: u64, item: &Item) -> Vec<WeaponBehavior> {
    let mut options: Vec<WeaponBehavior> = item.behaviors.iter().filter_map(|b| match b {
        ItemBehavior::Weapon(w) => Some(w.clone()),
        _ => None,
    }).collect();
    for b in equipped_enchantment_behaviors(ctx, profile_id) {
        if let EnchantmentBehavior::WeaponToggle(w) = b {
            options.push(w);
        }
    }
    options
}

/// The weapon the player is actually firing: the weapon head slot's item, the
/// active_toggle-selected pattern, with shot-count/damage enchantment behaviors folded
/// in. Both compute_player_damage (server) and CombatComponent (client mirror) derive
/// everything from this composition.
pub struct EffectiveWeapon {
    pub behavior: WeaponBehavior,
    pub flat_bullet_mod: f32,
    pub true_flat: f32,
    pub true_pct: f32,
}

pub fn resolve_effective_weapon(ctx: &ReducerContext, profile_id: u64) -> Option<EffectiveWeapon> {
    let slot = equipped_head_slots(ctx, profile_id, SlotRole::Weapon).into_iter().next()?;
    let item = ctx.db.item().item_id().find(slot.item_id.as_ref()?)?;
    let enchantments = equipped_enchantment_behaviors(ctx, profile_id);

    let mut options: Vec<WeaponBehavior> = item.behaviors.iter().filter_map(|b| match b {
        ItemBehavior::Weapon(w) => Some(w.clone()),
        _ => None,
    }).collect();
    for b in &enchantments {
        if let EnchantmentBehavior::WeaponToggle(w) = b {
            options.push(w.clone());
        }
    }
    if options.is_empty() { return None; }
    // Clamp instead of erroring: removing a toggle-granting enchantment can leave
    // active_toggle pointing past the end.
    let mut behavior = options[(slot.active_toggle as usize).min(options.len() - 1)].clone();

    let mut add_shots: i32 = 0;
    let mut shot_mult = 0f32;
    let mut flat_bullet_mod = 0f32;
    let mut true_flat = 0f32;
    let mut true_pct = 0f32;
    for b in &enchantments {
        match b {
            EnchantmentBehavior::AddShots(n) => add_shots += n,
            EnchantmentBehavior::ShotCountMult(m) => shot_mult += m,
            EnchantmentBehavior::FlatBulletDamageMod(v) => flat_bullet_mod += v,
            EnchantmentBehavior::TrueDamageFlat(v) => true_flat += v,
            EnchantmentBehavior::TrueDamagePercent(v) => true_pct += v,
            _ => {}
        }
    }
    behavior.shot_count = ((behavior.shot_count as i32 + add_shots).max(1) as f32 * (1.0 + shot_mult))
        .round().max(1.0) as u32;

    Some(EffectiveWeapon { behavior, flat_bullet_mod, true_flat, true_pct })
}
