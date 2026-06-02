use spacetimedb::{ReducerContext, Table};
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::world::tables::ItemType;

pub fn require_logged_in(ctx: &ReducerContext) -> Result<(), String> {
    if ctx.db.logged_in_player().player_id().find(ctx.sender()).is_none() {
        return Err("Not in world.".to_string());
    }
    Ok(())
}

pub fn require_in_world(ctx: &ReducerContext) -> Result<u64, String> {
    ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .ok_or_else(|| "Not in world.".to_string())
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
    (1 + bonus, 1 + bonus, 1 + bonus, 1 + bonus, 1 + bonus, 25 + bonus)
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
    let mut str_flat = 0i32; let mut wis_flat = 0i32; let mut dex_flat = 0i32;
    let mut def_flat = 0i32; let mut vit_flat = 0i32; let mut spd_flat = 0i32;
    let mut str_pct = 0f32; let mut wis_pct = 0f32; let mut dex_pct = 0f32;
    let mut def_pct = 0f32; let mut vit_pct = 0f32; let mut spd_pct = 0f32;
    for slot in &inv.slots {
        let item_id = match &slot.item_id { Some(id) => id, None => continue };
        let item = match ctx.db.item().item_id().find(item_id) { Some(i) => i, None => continue };
        match item.item_type {
            ItemType::Armor => {
                if let Some(a) = ctx.db.armor().armor_id().find(&item.item_id) {
                    def_flat += a.defense_bonus;
                    vit_flat += a.vitality_bonus;
                }
            }
            ItemType::Accessory => {
                if let Some(a) = ctx.db.accessory().accessory_id().find(&item.item_id) {
                    str_flat += a.strength_bonus;
                    wis_flat += a.wisdom_bonus;
                    dex_flat += a.dexterity_bonus;
                    spd_flat += a.speed_bonus;
                }
            }
            _ => {}
        }
    }
    let new_stats = PlayerStats {
        profile_id,
        strength: ((base_str + str_flat) as f32 * (1.0 + str_pct)) as i32,
        wisdom: ((base_wis + wis_flat) as f32 * (1.0 + wis_pct)) as i32,
        dexterity: ((base_dex + dex_flat) as f32 * (1.0 + dex_pct)) as i32,
        defense: ((base_def + def_flat) as f32 * (1.0 + def_pct)) as i32,
        vitality: ((base_vit + vit_flat) as f32 * (1.0 + vit_pct)) as i32,
        speed: ((base_spd + spd_flat) as f32 * (1.0 + spd_pct)) as i32,
    };
    if ctx.db.player_stats().profile_id().find(&profile_id).is_some() {
        ctx.db.player_stats().profile_id().update(new_stats);
    } else {
        ctx.db.player_stats().insert(new_stats);
    }
}

fn weapon_slot(item_id: Option<String>) -> InventorySlot { InventorySlot { allowed_types: vec![ItemType::Weapon], item_id } }
fn hotbar_slot() -> InventorySlot { InventorySlot { allowed_types: vec![ItemType::Ability, ItemType::Consumable], item_id: None } }
fn accessory_slot() -> InventorySlot { InventorySlot { allowed_types: vec![ItemType::Accessory], item_id: None } }
fn armor_slot() -> InventorySlot { InventorySlot { allowed_types: vec![ItemType::Armor], item_id: None } }
fn general_slot() -> InventorySlot { InventorySlot { allowed_types: vec![ItemType::Weapon, ItemType::Ability, ItemType::Armor, ItemType::Accessory, ItemType::Consumable], item_id: None } }
fn bag_slot() -> InventorySlot { InventorySlot { allowed_types: vec![ItemType::Bag], item_id: None } }

pub fn scaffold_profile(ctx: &ReducerContext, profile_id: u64) {
    if ctx.db.player_data().profile_id().find(&profile_id).is_none() {
        ctx.db.player_data().insert(PlayerData {
            profile_id, level: 1, xp: 0, hp: BASE_MAX_HP as u32, max_hp: BASE_MAX_HP as u32,
        });
    }
    if ctx.db.player_inventory().profile_id().find(&profile_id).is_none() {
        ctx.db.player_inventory().insert(PlayerInventory {
            profile_id,
            slots: vec![
                weapon_slot(Some("StarterBow".to_string())), // 0: weapon
                hotbar_slot(), hotbar_slot(), hotbar_slot(), hotbar_slot(), hotbar_slot(), // 1-5: hotbar
                accessory_slot(), accessory_slot(), accessory_slot(), accessory_slot(), // 6-9: accessories
                armor_slot(), armor_slot(), armor_slot(), armor_slot(), // 10-13: armor
                general_slot(), general_slot(), general_slot(), general_slot(), general_slot(), // 14-18
                general_slot(), general_slot(), general_slot(), general_slot(), // 19-22: general
                bag_slot(), // 23: bag
            ],
        });
    }
    recompute_stats(ctx, profile_id);
}

pub fn compute_player_damage(ctx: &ReducerContext, profile_id: u64) -> u32 {
    let stats = match ctx.db.player_stats().profile_id().find(&profile_id) { Some(s) => s, None => return 1 };
    let inv = match ctx.db.player_inventory().profile_id().find(&profile_id) { Some(i) => i, None => return 1 };
    let weapon_item = inv.slots.get(0)
        .and_then(|s| s.item_id.as_ref())
        .and_then(|id| ctx.db.item().item_id().find(id));
    let weapon_stats = weapon_item.and_then(|i| ctx.db.weapon().weapon_id().find(&i.item_id));
    match weapon_stats {
        Some(w) => (w.damage as f32 * (1.0 + stats.strength as f32 * 0.02)).max(1.0) as u32,
        None => 1,
    }
}

pub fn internal_gain_xp(ctx: &ReducerContext, profile_id: u64, amount: u32) {
    let data = match ctx.db.player_data().profile_id().find(&profile_id) { Some(d) => d, None => return };
    let old_level = data.level;
    let new_xp = data.xp + amount;
    let new_level = compute_level(new_xp);
    let new_max_hp = (BASE_MAX_HP + (new_level as i32 - 1) * HP_PER_LEVEL) as u32;
    let heal = (new_level - old_level) * HP_PER_LEVEL as u32;
    let new_hp = (data.hp + heal).min(new_max_hp);
    ctx.db.player_data().profile_id().update(PlayerData { level: new_level, xp: new_xp, hp: new_hp, max_hp: new_max_hp, ..data });
    if new_level != old_level { recompute_stats(ctx, profile_id); }
}

pub fn find_profile_by_name(ctx: &ReducerContext, player_id: spacetimedb::Identity, profile_name: &str) -> Option<PlayerProfile> {
    let lower = profile_name.to_lowercase();
    ctx.db.player_profile().player_id().filter(&player_id).find(|p| p.profile_name.to_lowercase() == lower)
}
