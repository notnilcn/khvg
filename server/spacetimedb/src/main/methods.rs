use spacetimedb::{ReducerContext, Identity, Table};
use super::tables::*;

pub const BASE_MAX_HP: i32 = 100;
pub const HP_PER_LEVEL: i32 = 5;
pub const CHUNK_SIZE: f32 = 320.0;

pub fn starting_stats() -> RpgStats {
    RpgStats { strength: 1, wisdom: 1, dexterity: 1, defense: 1, vitality: 1, speed: 25 }
}

pub fn xp_for_level(level: u32) -> u32 {
    if level <= 1 { 0 } 

    else { 
        100 * (level - 1) * level / 2 
    }
}

pub fn compute_level(total_xp: u32) -> u32 {
    let mut level = 1u32;
    while xp_for_level(level + 1) <= total_xp {
        level += 1;
    }
    level
}

pub fn compute_stats_for_level(level: u32) -> RpgStats {
    let bonus = (level as i32) - 1;
    let s = starting_stats();
    RpgStats {
        strength: s.strength + bonus,
        wisdom: s.wisdom + bonus,
        dexterity: s.dexterity + bonus,
        defense: s.defense + bonus,
        vitality: s.vitality + bonus,
        speed: s.speed + bonus,
    }
}

pub fn server_collect_stats(ctx: &ReducerContext, player_id: Identity) {
    let data = match ctx.db.character_data().player_id().find(player_id) {
        Some(d) => d,
        None => return,
    };
    let inv = match ctx.db.player_inventory().player_id().find(player_id) {
        Some(i) => i,
        None => return,
    };

    let base = compute_stats_for_level(data.level);

    let mut str_flat = 0i32; let mut wis_flat = 0i32; let mut dex_flat = 0i32;
    let mut def_flat = 0i32; let mut vit_flat = 0i32; let mut spd_flat = 0i32;
    let mut str_pct = 0f32; let mut wis_pct = 0f32; let mut dex_pct = 0f32;
    let mut def_pct = 0f32; let mut vit_pct = 0f32; let mut spd_pct = 0f32;

    let mut slots: Vec<Option<String>> = vec![
        inv.equipped_weapon.clone(),
        inv.equipped_ability.clone(),
        inv.equipped_armor.clone(),
    ];
    
    for acc in &inv.equipped_accessories {
        slots.push(Some(acc.clone()));
    }

    for slot in &slots {
        let id = match slot {
            Some(s) if !s.is_empty() => s,
            _ => continue,
        };
        
        let item = match ctx.db.item_stats().item_id().find(id) {
            Some(i) => i,
            None => continue,
        };
        str_flat += item.str_bonus_add as i32; str_pct += item.str_bonus_mult;
        wis_flat += item.wis_bonus_add as i32; wis_pct += item.wis_bonus_mult;
        dex_flat += item.dex_bonus_add as i32; dex_pct += item.dex_bonus_mult;
        def_flat += item.def_bonus_add as i32; def_pct += item.def_bonus_mult;
        vit_flat += item.vit_bonus_add as i32; vit_pct += item.vit_bonus_mult;
        spd_flat += item.spd_bonus_add as i32; spd_pct += item.spd_bonus_mult;
    }

    ctx.db.character_data().player_id().update(CharacterData {
        stats: RpgStats {
            strength: ((base.strength + str_flat) as f32 * (1.0 + str_pct)) as i32,
            wisdom: ((base.wisdom + wis_flat) as f32 * (1.0 + wis_pct)) as i32,
            dexterity: ((base.dexterity + dex_flat) as f32 * (1.0 + dex_pct)) as i32,
            defense: ((base.defense + def_flat) as f32 * (1.0 + def_pct)) as i32,
            vitality: ((base.vitality + vit_flat) as f32 * (1.0 + vit_pct)) as i32,
            speed: ((base.speed + spd_flat) as f32 * (1.0 + spd_pct)) as i32,
        },
        ..data
    });
}

pub fn can_control_enemies(ctx: &ReducerContext) -> bool {
    let mut any_elevated = false;
    for row in ctx.db.elevated_client().iter() {
        if row.identity == ctx.sender() {
            return true;
        }
        any_elevated = true;
    }
    !any_elevated
}
