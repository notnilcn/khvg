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

pub fn compute_base_max_hp(level: u32) -> i32 {
    BASE_MAX_HP + (level as i32 - 1) * HP_PER_LEVEL
}

// Indexes into the flat/mult accumulators in recompute_stats: the six allocatable
// stats first (matching PlayerStatAllocation/PlayerStats field order), then the
// modifier-only kinds.
fn stat_index(stat: StatKind) -> usize {
    match stat {
        StatKind::Strength => 0,
        StatKind::Wisdom => 1,
        StatKind::Dexterity => 2,
        StatKind::DamageDealer => 3,
        StatKind::Supporter => 4,
        StatKind::Artisan => 5,
        StatKind::Hp => 6,
        StatKind::Defense => 7,
        StatKind::BaseSpeed => 8,
    }
}

fn buff_to_modifier(effect: &ConsumableBuffEffect) -> StatModifier {
    let (stat, amount) = match effect {
        ConsumableBuffEffect::Strength(v) => (StatKind::Strength, *v),
        ConsumableBuffEffect::Wisdom(v) => (StatKind::Wisdom, *v),
        ConsumableBuffEffect::Dexterity(v) => (StatKind::Dexterity, *v),
        ConsumableBuffEffect::DamageDealer(v) => (StatKind::DamageDealer, *v),
        ConsumableBuffEffect::Supporter(v) => (StatKind::Supporter, *v),
        ConsumableBuffEffect::Artisan(v) => (StatKind::Artisan, *v),
    };
    StatModifier { stat, amount, mode: StatMode::Flat }
}

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

pub fn recompute_stats(ctx: &ReducerContext, profile_id: u64) {
    let data = match ctx.db.player_data().profile_id().find(&profile_id) {
        Some(d) => d,
        None => return,
    };

    let mut flat = [0f32; 9];
    let mut mult = [0f32; 9];

    // Allocated skill points fold into the flat accumulator, so gear mults scale them
    // like any other flat source — temperaments work exactly like normal stats. (Folded
    // before fold_modifier is defined — the closure borrows `flat` for the rest of the fn.)
    if let Some(alloc) = ctx.db.player_stat_allocation().profile_id().find(&profile_id) {
        flat[0] += alloc.strength as f32;
        flat[1] += alloc.wisdom as f32;
        flat[2] += alloc.dexterity as f32;
        flat[3] += alloc.damage_dealer as f32;
        flat[4] += alloc.supporter as f32;
        flat[5] += alloc.artisan as f32;
    }

    let mut fold_modifier = |m: &StatModifier, scale: f32| {
        let idx = stat_index(m.stat);
        match m.mode {
            StatMode::Flat => flat[idx] += m.amount * scale,
            StatMode::Mult => mult[idx] += m.amount * scale,
        }
    };

    for slot in ctx.db.player_inventory_slot().profile_id().filter(&profile_id) {
        if slot.occupied_by.is_some() { continue; } // span follower cells hold no item
        // Only worn equipment and ability heads contribute stats — this role check replaces
        // the old `allowed_slots.len() != 1` "is equipped" inference. Ability passives scale
        // by the position multiplier (first ability stronger).
        let scale = match slot.role {
            SlotRole::Weapon | SlotRole::Accessory | SlotRole::Armor => 1.0,
            SlotRole::Ability => ability_position_multiplier(slot.slot_index),
            _ => continue,
        };
        let item_id = match &slot.item_id { Some(id) => id, None => continue };
        let item = match ctx.db.item().item_id().find(item_id) { Some(i) => i, None => continue };
        for m in &item.stat_modifiers {
            fold_modifier(m, scale);
        }
        // Innate enchantments (on the item row) apply like socketed ones (on the slot
        // row) but can't be removed and don't count against max_enchantments.
        for enchantment_id in item.innate_enchantment_ids.iter().chain(slot.enchantment_ids.iter()) {
            if let Some(enchantment) = ctx.db.enchantment().enchantment_id().find(enchantment_id) {
                for m in &enchantment.stat_modifiers {
                    fold_modifier(m, scale);
                }
            }
        }
    }

    for active in ctx.db.active_consumable_effect().profile_id().filter(&profile_id) {
        fold_modifier(&active.modifier, 1.0);
    }

    let resolve = |idx: usize, base: i32| -> i32 {
        ((base as f32 + flat[idx]) * (1.0 + mult[idx])).round() as i32
    };

    let new_stats = PlayerStats {
        profile_id,
        strength: resolve(0, BASE_STAT),
        wisdom: resolve(1, BASE_STAT),
        dexterity: resolve(2, BASE_STAT),
        damage_dealer: resolve(3, BASE_STAT),
        supporter: resolve(4, BASE_STAT),
        artisan: resolve(5, BASE_STAT),
    };

    if ctx.db.player_stats().profile_id().find(&profile_id).is_some() {
        ctx.db.player_stats().profile_id().update(new_stats);
    } else {
        ctx.db.player_stats().insert(new_stats);
    }

    // Modifier-only kinds resolve onto PlayerData: Hp into max_hp, Defense into defense,
    // BaseSpeed into base_speed (f32 — speed isn't rounded to int like the others).
    let new_max_hp = resolve(6, compute_base_max_hp(data.level)).max(1) as u32;
    let new_defense = resolve(7, 0).max(0);
    let new_base_speed = ((BASE_SPEED + flat[8]) * (1.0 + mult[8])).max(1.0);
    if new_max_hp != data.max_hp || data.hp > new_max_hp || new_defense != data.defense || new_base_speed != data.base_speed {
        let new_hp = data.hp.min(new_max_hp);
        ctx.db.player_data().profile_id().update(PlayerData { max_hp: new_max_hp, hp: new_hp, defense: new_defense, base_speed: new_base_speed, ..data });
    }
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

fn inventory_slot(profile_id: u64, slot_index: u32, role: SlotRole, item_id: Option<String>) -> PlayerInventorySlot {
    PlayerInventorySlot {
        slot_id: 0, profile_id, slot_index, role, item_id,
        enchantment_ids: vec![], cooldown_until: None, charges: u32::MAX, active_toggle: 0, occupied_by: None,
    }
}

pub fn try_scaffold_profile(ctx: &ReducerContext, profile_id: u64) {
    if ctx.db.player_data().profile_id().find(&profile_id).is_none() {
        ctx.db.player_data().insert(PlayerData {
            profile_id, level: 1, xp: 0, hp: BASE_MAX_HP as u32, max_hp: BASE_MAX_HP as u32, defense: 0,
            base_speed: BASE_SPEED,
        });
    }

    if ctx.db.player_inventory_slot().profile_id().filter(&profile_id).next().is_none() {
        let mut rows = vec![
            inventory_slot(profile_id, 0, SlotRole::Weapon, Some("Bow".to_string())), // 0: weapon
        ];
        for i in 1..=4u32 { rows.push(inventory_slot(profile_id, i, SlotRole::Hotbar, None)); } // 1-4: hotbar (consumable)
        rows.push(inventory_slot(profile_id, 5, SlotRole::Accessory, Some("Hat".to_string()))); // 5: accessory
        rows.push(inventory_slot(profile_id, 6, SlotRole::Armor, Some("Helmet".to_string()))); // 6: armor
        for i in 7..=30u32 { rows.push(inventory_slot(profile_id, i, SlotRole::General, None)); } // 7-30: general
        rows.push(inventory_slot(profile_id, 31, SlotRole::Bag, Some("Bag".to_string()))); // 31: bag
        for i in ABILITY_SLOT_START..ABILITY_SLOT_START + ABILITY_SLOT_COUNT { // 32-37: ability
            rows.push(inventory_slot(profile_id, i, SlotRole::Ability, None));
        }
        rows[1].item_id = Some("Bread".to_string());
        for row in rows {
            ctx.db.player_inventory_slot().insert(row);
        }
        // Pre-equip ability items so activation is testable immediately: Skull (a 1-cost
        // "artifact" ability) at 32, Tome of Mending spanning 33-35.
        if let Some(skull) = ctx.db.item().item_id().find(&"Skull".to_string()) {
            place_span(ctx, profile_id, ABILITY_SLOT_START, &skull, vec![]);
        }
        if let Some(tome) = ctx.db.item().item_id().find(&"Tome of Mending".to_string()) {
            place_span(ctx, profile_id, ABILITY_SLOT_START + 1, &tome, vec![]);
        }
    }

    if ctx.db.player_stat_allocation().profile_id().find(&profile_id).is_none() {
        ctx.db.player_stat_allocation().insert(PlayerStatAllocation {
            profile_id, strength: 0, wisdom: 0, dexterity: 0,
            damage_dealer: 0, supporter: 0, artisan: 0, unspent_points: 0,
        });
    }

    if ctx.db.player_stats().profile_id().find(&profile_id).is_none() {
        recompute_stats(ctx, profile_id);
    }

    if ctx.db.player_position().profile_id().find(&profile_id).is_none() {
        let cfg = MapConfig::load(ctx, 0, 0);
        let (cq, cr) = world_to_chunk(0.0, 0.0, cfg.chunk_hex_radius, cfg.hex_outer_radius);
        let chunk_index = spiral_chunk_index(cq, cr);
        ctx.db.player_position().insert(PlayerPosition { profile_id, player_id: ctx.sender(), x: 0.0, y: 0.0, movement_direction: 0.0, movement_speed: 0.0, chunk_index});
    }

    if ctx.db.player_rotation().profile_id().find(&profile_id).is_none() {
        let chunk_index = ctx.db.player_position().profile_id().find(&profile_id)
            .map(|p| p.chunk_index)
            .unwrap_or(0);
        ctx.db.player_rotation().insert(PlayerRotation { profile_id, player_id: ctx.sender(), screen_rotation: 0.0, chunk_index });
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
    ctx.db.player_stat_allocation().profile_id().delete(profile_id);
    for slot in ctx.db.player_inventory_slot().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        ctx.db.player_inventory_slot().slot_id().delete(slot.slot_id);
    }
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
    if new_level != old_level {
        if let Some(alloc) = ctx.db.player_stat_allocation().profile_id().find(&profile_id) {
            let gained = (new_level - old_level) * SKILL_POINTS_PER_LEVEL;
            ctx.db.player_stat_allocation().profile_id().update(PlayerStatAllocation {
                unspent_points: alloc.unspent_points + gained, ..alloc
            });
        }
        recompute_stats(ctx, profile_id);
    }
}

pub fn find_profile_by_name(ctx: &ReducerContext, player_id: spacetimedb::Identity, profile_name: &str) -> Option<PlayerProfile> {
    let lower = profile_name.to_lowercase();
    ctx.db.player_profile().player_id().filter(&player_id).find(|p| p.profile_name.to_lowercase() == lower)
}

// ===== Slot helpers =====

/// Which items a slot accepts, derived from its role. General (backpack) accepts everything —
/// ability items stored there sit loose in a single cell; slot_cost only applies in the
/// ability region. Every other role accepts exactly its matching EquipSlot.
pub fn slot_allowed(role: SlotRole, equip_slot: EquipSlot) -> bool {
    match role {
        SlotRole::General => true,
        SlotRole::Weapon => equip_slot == EquipSlot::Weapon,
        SlotRole::Hotbar => equip_slot == EquipSlot::Consumable,
        SlotRole::Accessory => equip_slot == EquipSlot::Accessory,
        SlotRole::Armor => equip_slot == EquipSlot::Armor,
        SlotRole::Bag => equip_slot == EquipSlot::Bag,
        SlotRole::Ability => equip_slot == EquipSlot::Ability,
    }
}

pub fn get_slot(ctx: &ReducerContext, profile_id: u64, slot_index: u32) -> Option<PlayerInventorySlot> {
    ctx.db.player_inventory_slot().profile_id().filter(&profile_id).find(|s| s.slot_index == slot_index)
}

pub fn update_slot(ctx: &ReducerContext, slot: PlayerInventorySlot) {
    ctx.db.player_inventory_slot().slot_id().update(slot);
}

/// Occupied (item-holding) head cells of a role — span followers are excluded.
pub fn equipped_head_slots(ctx: &ReducerContext, profile_id: u64, role: SlotRole) -> Vec<PlayerInventorySlot> {
    ctx.db.player_inventory_slot().profile_id().filter(&profile_id)
        .filter(|s| s.role == role && s.occupied_by.is_none() && s.item_id.is_some())
        .collect()
}

pub fn ability_position_multiplier(slot_index: u32) -> f32 {
    let pos = slot_index.saturating_sub(ABILITY_SLOT_START) as usize;
    ABILITY_POSITION_MULTIPLIERS.get(pos).copied().unwrap_or(1.0)
}

/// First index of a contiguous run of `cost` free ability cells, or None. Free means no
/// item and not a span follower.
pub fn find_free_span(ctx: &ReducerContext, profile_id: u64, cost: u32) -> Option<u32> {
    if cost == 0 || cost > ABILITY_SLOT_COUNT { return None; }
    for start in ABILITY_SLOT_START..=ABILITY_SLOT_START + ABILITY_SLOT_COUNT - cost {
        let free = (start..start + cost).all(|i| {
            get_slot(ctx, profile_id, i).map(|s| s.item_id.is_none() && s.occupied_by.is_none()).unwrap_or(false)
        });
        if free { return Some(start); }
    }
    None
}

/// Writes an ability item into the span starting at `head_index`: the head holds the item,
/// the following `slot_cost - 1` cells become followers. Charges initialize from the item's
/// AbilityBehavior (max_charges 0 = unlimited). Callers must have validated the span is free.
pub fn place_span(ctx: &ReducerContext, profile_id: u64, head_index: u32, item: &Item, enchantment_ids: Vec<String>) {
    let cost = item.slot_cost.max(1);
    let charges = ability_behavior(item)
        .map(|a| if a.max_charges == 0 { u32::MAX } else { a.max_charges })
        .unwrap_or(u32::MAX);
    if let Some(head) = get_slot(ctx, profile_id, head_index) {
        update_slot(ctx, PlayerInventorySlot {
            item_id: Some(item.item_id.clone()), enchantment_ids, charges,
            cooldown_until: None, active_toggle: 0, occupied_by: None, ..head
        });
    }
    for i in head_index + 1..head_index + cost {
        if let Some(follower) = get_slot(ctx, profile_id, i) {
            update_slot(ctx, PlayerInventorySlot {
                item_id: None, enchantment_ids: vec![], occupied_by: Some(head_index),
                cooldown_until: None, charges: u32::MAX, active_toggle: 0, ..follower
            });
        }
    }
}

/// Clears the span headed at `head_index` (head + `slot_cost - 1` followers) back to empty.
pub fn clear_span(ctx: &ReducerContext, profile_id: u64, head_index: u32, slot_cost: u32) {
    for i in head_index..head_index + slot_cost.max(1) {
        if let Some(slot) = get_slot(ctx, profile_id, i) {
            update_slot(ctx, PlayerInventorySlot {
                item_id: None, enchantment_ids: vec![], occupied_by: None,
                cooldown_until: None, charges: u32::MAX, active_toggle: 0, ..slot
            });
        }
    }
}

/// Stat-requirement check for equipping (doc 03: classes emerge from gear locked behind
/// stat thresholds). Returns Err naming the first unmet requirement, Ok(()) when the
/// player's resolved stats meet every requirement (all-zero always passes).
pub fn check_stat_requirements(ctx: &ReducerContext, profile_id: u64, item: &Item) -> Result<(), String> {
    let req = &item.stat_requirements;
    let stats = match ctx.db.player_stats().profile_id().find(&profile_id) {
        Some(s) => s,
        None => return Err("Player stats not found.".to_string()),
    };
    let checks = [
        ("STR", req.strength, stats.strength),
        ("WIS", req.wisdom, stats.wisdom),
        ("DEX", req.dexterity, stats.dexterity),
        ("DPS", req.damage_dealer, stats.damage_dealer),
        ("SUP", req.supporter, stats.supporter),
        ("ART", req.artisan, stats.artisan),
    ];
    for (name, required, actual) in checks {
        if (actual as u32) < required {
            return Err(format!("Item '{}' requires {} {}.", item.item_id, name, required));
        }
    }
    Ok(())
}
