use spacetimedb::{ReducerContext, Table};
use crate::main::global::*;
use crate::world::hex::{spiral_chunk_index, world_to_chunk};
use crate::world::tables::MapConfig;
use crate::player::tables::*;
use crate::item::tables::*;

pub fn require_logged_in(ctx: &ReducerContext) -> Result<(), String> {
    if ctx.db.logged_in_player().player_id().find(ctx.sender()).is_none() {
        return Err("Not in world.".to_string());
    }
    Ok(())
}

pub fn require_in_world(ctx: &ReducerContext) -> Result<LoggedInPlayer, String> {
    ctx.db.logged_in_player().player_id().find(ctx.sender()).ok_or_else(|| "Not in world.".to_string())
}

pub fn require_in_lobby(ctx: &ReducerContext) -> Result<LoggedOutPlayer, String> {
    ctx.db.logged_out_player().player_id().find(ctx.sender())
        .ok_or_else(|| "Not in lobby.".to_string())
}

pub fn is_admin(ctx: &ReducerContext) -> bool {
    if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        return p.is_admin;
    }
    ctx.db.logged_out_player().player_id().find(ctx.sender())
        .map(|p| p.is_admin)
        .unwrap_or(false)
}

pub fn xp_for_level(level: u32) -> u32 {
    if level <= 1 { 0 } else { 100 * (level - 1) * level / 2 }
}

pub fn compute_level(total_xp: u32) -> u32 {
    let mut level = 1u32;
    while xp_for_level(level + 1) <= total_xp { level += 1; }
    level
}

pub fn compute_base_stats(level: u32) -> (i32, i32, i32, i32, i32, i32) {
    let bonus = (level as i32) - 1;
    (10 + bonus, 10 + bonus, 10 + bonus, 10 + bonus, 10 + bonus, 10 + bonus)
}

pub fn compute_base_max_hp(level: u32) -> i32 {
    BASE_MAX_HP + (level as i32 - 1) * HP_PER_LEVEL
}

fn stat_index(stat: StatKind) -> usize {
    match stat {
        StatKind::Strength => 0,
        StatKind::Wisdom => 1,
        StatKind::Dexterity => 2,
        StatKind::Defense => 3,
        StatKind::Vitality => 4,
        StatKind::Speed => 5,
        StatKind::Hp => 6,
    }
}

fn buff_to_modifier(effect: &ConsumableBuffEffect) -> StatModifier {
    let (stat, amount) = match effect {
        ConsumableBuffEffect::Strength(v) => (StatKind::Strength, *v),
        ConsumableBuffEffect::Wisdom(v) => (StatKind::Wisdom, *v),
        ConsumableBuffEffect::Dexterity(v) => (StatKind::Dexterity, *v),
        ConsumableBuffEffect::Defense(v) => (StatKind::Defense, *v),
        ConsumableBuffEffect::Vitality(v) => (StatKind::Vitality, *v),
        ConsumableBuffEffect::Speed(v) => (StatKind::Speed, *v),
    };
    StatModifier { stat, amount, mode: StatMode::Flat }
}

pub fn weapon_behavior(item: &Item) -> Option<&WeaponBehavior> {
    item.behaviors.iter().find_map(|b| match b {
        ItemBehavior::Weapon(w) => Some(w),
        _ => None,
    })
}

pub fn consumable_behavior(item: &Item) -> Option<&ConsumableBehavior> {
    item.behaviors.iter().find_map(|b| match b {
        ItemBehavior::Consumable(c) => Some(c),
        _ => None,
    })
}

pub fn recompute_stats(ctx: &ReducerContext, profile_id: u64) {
    let data = match ctx.db.player_data().profile_id().find(&profile_id) {
        Some(d) => d,
        None => return,
    };
    let inv = match ctx.db.player_inventory().profile_id().find(&profile_id) {
        Some(i) => i,
        None => return,
    };
    let (base_str, base_wis, base_dex, base_def, base_vit, base_spd) = compute_base_stats(data.level);

    let mut flat = [0f32; 7];
    let mut mult = [0f32; 7];
    let mut fold_modifier = |m: &StatModifier| {
        let idx = stat_index(m.stat);
        match m.mode {
            StatMode::Flat => flat[idx] += m.amount,
            StatMode::Mult => mult[idx] += m.amount,
        }
    };

    for slot in &inv.slots {
        if slot.allowed_slots.len() != 1 { continue; }
        let item_id = match &slot.item_id { Some(id) => id, None => continue };
        let item = match ctx.db.item().item_id().find(item_id) { Some(i) => i, None => continue };
        for m in &item.stat_modifiers {
            fold_modifier(m);
        }
        for enchantment_id in &slot.enchantment_ids {
            if let Some(enchantment) = ctx.db.enchantment().enchantment_id().find(enchantment_id) {
                for m in &enchantment.stat_modifiers {
                    fold_modifier(m);
                }
            }
        }
    }

    for active in ctx.db.active_consumable_effect().profile_id().filter(&profile_id) {
        fold_modifier(&active.modifier);
    }

    let resolve = |idx: usize, base: i32| -> i32 {
        ((base as f32 + flat[idx]) * (1.0 + mult[idx])).round() as i32
    };

    let new_stats = PlayerStats {
        profile_id,
        strength: resolve(0, base_str),
        wisdom: resolve(1, base_wis),
        dexterity: resolve(2, base_dex),
        defense: resolve(3, base_def),
        vitality: resolve(4, base_vit),
        speed: resolve(5, base_spd),
    };

    if ctx.db.player_stats().profile_id().find(&profile_id).is_some() {
        ctx.db.player_stats().profile_id().update(new_stats);
    } else {
        ctx.db.player_stats().insert(new_stats);
    }

    let new_max_hp = resolve(6, compute_base_max_hp(data.level)).max(1) as u32;
    if new_max_hp != data.max_hp || data.hp > new_max_hp {
        let new_hp = data.hp.min(new_max_hp);
        ctx.db.player_data().profile_id().update(PlayerData { max_hp: new_max_hp, hp: new_hp, ..data });
    }
}

fn weapon_slot(item_id: Option<String>) -> InventorySlot { InventorySlot { allowed_slots: vec![EquipSlot::Weapon], item_id, enchantment_ids: vec![] } }
fn hotbar_slot() -> InventorySlot { InventorySlot { allowed_slots: vec![EquipSlot::Consumable], item_id: None, enchantment_ids: vec![] } }
fn accessory_slot() -> InventorySlot { InventorySlot { allowed_slots: vec![EquipSlot::Accessory], item_id: None, enchantment_ids: vec![] } }
fn armor_slot() -> InventorySlot { InventorySlot { allowed_slots: vec![EquipSlot::Armor], item_id: None, enchantment_ids: vec![] } }
fn artifact_slot() -> InventorySlot { InventorySlot { allowed_slots: vec![EquipSlot::Artifact], item_id: None, enchantment_ids: vec![] } }
fn general_slot() -> InventorySlot { InventorySlot { allowed_slots: vec![EquipSlot::Weapon, EquipSlot::Artifact, EquipSlot::Armor, EquipSlot::Accessory, EquipSlot::Consumable], item_id: None, enchantment_ids: vec![] } }
fn bag_slot() -> InventorySlot { InventorySlot { allowed_slots: vec![EquipSlot::Bag], item_id: None, enchantment_ids: vec![] } }

pub fn try_scaffold_profile(ctx: &ReducerContext, profile_id: u64) {
    if ctx.db.player_data().profile_id().find(&profile_id).is_none() {
        ctx.db.player_data().insert(PlayerData {
            profile_id, level: 1, xp: 0, hp: BASE_MAX_HP as u32, max_hp: BASE_MAX_HP as u32,
        });
    }

    if ctx.db.player_inventory().profile_id().find(&profile_id).is_none() {
        let mut slots = vec![
            weapon_slot(Some("Bow".to_string())), // 0: weapon
            hotbar_slot(), hotbar_slot(), hotbar_slot(), hotbar_slot(), // 1-4: hotbar (consumable)
            accessory_slot(), accessory_slot(), accessory_slot(), accessory_slot(), // 5-8: accessories
            armor_slot(), armor_slot(), armor_slot(), armor_slot(), // 9-12: armor
            artifact_slot(), artifact_slot(), // 13-14: artifact
            general_slot(), general_slot(), general_slot(), general_slot(), general_slot(), general_slot(), general_slot(), general_slot(), // 15-22: general
            bag_slot(), // 23: bag
        ];
        slots[1].item_id = Some("Bread".to_string());
        slots[5].item_id = Some("Hat".to_string());
        slots[9].item_id = Some("Helmet".to_string());
        slots[13].item_id = Some("Skull".to_string());
        slots[23].item_id = Some("Bag".to_string());
        ctx.db.player_inventory().insert(PlayerInventory { profile_id, slots });
    }
    
    if ctx.db.player_stats().profile_id().find(&profile_id).is_none() {
        recompute_stats(ctx, profile_id);
    }

    if ctx.db.player_position().profile_id().find(&profile_id).is_none() {
        let cfg = MapConfig::load(ctx, 0, 0);
        let (cq, cr) = world_to_chunk(0.0, 0.0, cfg.chunk_hex_radius, cfg.hex_outer_radius);
        let chunk_index = spiral_chunk_index(cq, cr);
        ctx.db.player_position().insert(PlayerPosition { profile_id, player_id: ctx.sender(), x: 0.0, y: 0.0, rotation: 0.0, chunk_index});
    }

    if ctx.db.player_chunk().profile_id().find(&profile_id).is_none() {
        let cfg = MapConfig::load(ctx, 0, 0);
        let (cq, cr) = world_to_chunk(0.0, 0.0, cfg.chunk_hex_radius, cfg.hex_outer_radius);
        ctx.db.player_chunk().insert(PlayerChunk { profile_id, chunk_q: cq, chunk_r: cr });
    }
}

/// Tears down every per-profile row — the inverse of try_scaffold_profile, plus PlayerStats
/// (scaffolded lazily by recompute_stats) and any active consumable effects. This is the one
/// shared table list for profile cleanup; callers handle the identity-level rows themselves
/// (combat death keeps PlayerProfile and moves the player to LoggedOutPlayer; delete_profile
/// removes PlayerProfile while the player sits in the lobby).
pub fn teardown_profile(ctx: &ReducerContext, profile_id: u64) {
    ctx.db.player_data().profile_id().delete(profile_id);
    ctx.db.player_stats().profile_id().delete(profile_id);
    ctx.db.player_inventory().profile_id().delete(profile_id);
    ctx.db.player_position().profile_id().delete(profile_id);
    ctx.db.player_chunk().profile_id().delete(profile_id);
    for active in ctx.db.active_consumable_effect().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        ctx.db.active_consumable_effect().effect_id().delete(active.effect_id);
    }
}

use crate::combat::heal_player;

pub fn apply_consumable_effect(ctx: &ReducerContext, profile_id: u64, consumable: &ConsumableBehavior) {
    match &consumable.effect {
        ConsumableEffect::Heal => {
            heal_player(ctx, profile_id, consumable.potency as u32);
        }
        ConsumableEffect::Buff(effect) => {
            ctx.db.active_consumable_effect().insert(ActiveConsumableEffect {
                effect_id: 0, profile_id, modifier: buff_to_modifier(effect), remaining: consumable.duration,
            });
            recompute_stats(ctx, profile_id);
        }
    }
}

pub fn internal_gain_xp(ctx: &ReducerContext, profile_id: u64, amount: u32) {
    let data = match ctx.db.player_data().profile_id().find(&profile_id) { Some(d) => d, None => return };
    let old_level = data.level;
    let new_xp = data.xp + amount;
    let new_level = compute_level(new_xp);
    // Heal by the level-derived max_hp increase on top of current HP, so leveling up while
    // hurt doesn't fully restore (or waste) the gained health. recompute_stats folds in any
    // item-derived Hp bonus and clamps hp to the final max_hp afterward.
    let heal = (new_level - old_level) * HP_PER_LEVEL as u32;
    let new_hp = data.hp + heal;
    ctx.db.player_data().profile_id().update(PlayerData { level: new_level, xp: new_xp, hp: new_hp, ..data });
    if new_level != old_level { recompute_stats(ctx, profile_id); }
}

pub fn find_profile_by_name(ctx: &ReducerContext, player_id: spacetimedb::Identity, profile_name: &str) -> Option<PlayerProfile> {
    let lower = profile_name.to_lowercase();
    ctx.db.player_profile().player_id().filter(&player_id).find(|p| p.profile_name.to_lowercase() == lower)
}
