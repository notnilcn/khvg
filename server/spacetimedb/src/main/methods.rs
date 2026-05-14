use spacetimedb::{ReducerContext, Identity, Table};
use super::tables::*;

pub const BASE_MAX_HP: i32 = 100;
pub const HP_PER_LEVEL: i32 = 5;
pub const CHUNK_SIZE: f32 = 320.0;

pub fn starting_stats() -> PlayerStats {
    PlayerStats {
        player_id: Identity::default(),
        strength: 1,
        wisdom: 1,
        dexterity: 1,
        defense: 1,
        vitality: 1,
        speed: 25,
    }
}

pub fn is_admin(ctx: &ReducerContext) -> bool {
    ctx.db.player().player_id().find(ctx.sender())
        .map(|p| p.is_admin)
        .unwrap_or(false)
}

pub fn xp_for_level(level: u32) -> u32 {
    if level <= 1 { 0 } else { 100 * (level - 1) * level / 2 }
}

pub fn compute_level(total_xp: u32) -> u32 {
    let mut level = 1u32;
    while xp_for_level(level + 1) <= total_xp {
        level += 1;
    }
    level
}

pub fn compute_base_stats(level: u32) -> (i32, i32, i32, i32, i32, i32) {
    let bonus = (level as i32) - 1;
    let s = starting_stats();
    (
        s.strength  + bonus,
        s.wisdom    + bonus,
        s.dexterity + bonus,
        s.defense   + bonus,
        s.vitality  + bonus,
        s.speed     + bonus,
    )
}

// Recomputes PlayerStats from level + equipped items and writes the result.
pub fn recompute_stats(ctx: &ReducerContext, player_id: Identity) {
    let data = match ctx.db.player_data().player_id().find(player_id) {
        Some(d) => d,
        None => return,
    };
    let inv = match ctx.db.player_inventory().player_id().find(player_id) {
        Some(i) => i,
        None => return,
    };

    let (base_str, base_wis, base_dex, base_def, base_vit, base_spd) =
        compute_base_stats(data.level);

    let mut str_flat = 0i32; let mut wis_flat = 0i32; let mut dex_flat = 0i32;
    let mut def_flat = 0i32; let mut vit_flat = 0i32; let mut spd_flat = 0i32;
    let mut str_pct  = 0f32; let mut wis_pct  = 0f32; let mut dex_pct  = 0f32;
    let mut def_pct  = 0f32; let mut vit_pct  = 0f32; let mut spd_pct  = 0f32;

    for slot in &inv.slots {
        let item = match ctx.db.item_stats().item_id().find(&slot.item_id) {
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

    let new_stats = PlayerStats {
        player_id,
        strength:  ((base_str + str_flat) as f32 * (1.0 + str_pct)) as i32,
        wisdom:    ((base_wis + wis_flat) as f32 * (1.0 + wis_pct)) as i32,
        dexterity: ((base_dex + dex_flat) as f32 * (1.0 + dex_pct)) as i32,
        defense:   ((base_def + def_flat) as f32 * (1.0 + def_pct)) as i32,
        vitality:  ((base_vit + vit_flat) as f32 * (1.0 + vit_pct)) as i32,
        speed:     ((base_spd + spd_flat) as f32 * (1.0 + spd_pct)) as i32,
    };

    if ctx.db.player_stats().player_id().find(player_id).is_some() {
        ctx.db.player_stats().player_id().update(new_stats);
    } else {
        ctx.db.player_stats().insert(new_stats);
    }
}

// Returns the phase index an enemy should be in given current hp and max_hp.
pub fn compute_phase(hp: u32, max_hp: u32, phases: &[PhaseDef]) -> u8 {
    let hp_pct = hp as f32 / max_hp as f32;
    let mut phase = 0u8;
    for def in phases {
        if hp_pct <= def.hp_threshold && def.phase_index > phase {
            phase = def.phase_index;
        }
    }
    phase
}
