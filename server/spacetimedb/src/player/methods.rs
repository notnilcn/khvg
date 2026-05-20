use spacetimedb::{ReducerContext, Identity, Table};
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;

pub fn require_logged_in(ctx: &ReducerContext) -> Result<(), String> {
    if ctx.db.logged_in_player().player_id().find(ctx.sender()).is_none() {
        return Err("Not logged in.".to_string());
    }
    Ok(())
}

pub fn is_admin(ctx: &ReducerContext) -> bool {
    ctx.db.logged_in_player().player_id().find(ctx.sender())
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

pub fn starting_stats() -> PlayerStats {
    PlayerStats { player_id: Identity::default(), strength: 1, wisdom: 1, dexterity: 1, defense: 1, vitality: 1, speed: 25 }
}

pub fn compute_base_stats(level: u32) -> (i32, i32, i32, i32, i32, i32) {
    let bonus = (level as i32) - 1;
    let s = starting_stats();
    (s.strength + bonus, s.wisdom + bonus, s.dexterity + bonus, s.defense + bonus, s.vitality + bonus, s.speed + bonus)
}

pub fn recompute_stats(ctx: &ReducerContext, player_id: Identity) {
    let data = match ctx.db.player_data().player_id().find(player_id) {
        Some(d) => d,
        None => return,
    };
    let inv = match ctx.db.player_inventory().player_id().find(player_id) {
        Some(i) => i,
        None => return,
    };

    let (base_str, base_wis, base_dex, base_def, base_vit, base_spd) = compute_base_stats(data.level);

    let mut str_flat = 0i32; let mut wis_flat = 0i32; let mut dex_flat = 0i32;
    let mut def_flat = 0i32; let mut vit_flat = 0i32; let mut spd_flat = 0i32;
    let mut str_pct = 0f32; let mut wis_pct = 0f32; let mut dex_pct = 0f32;
    let mut def_pct = 0f32; let mut vit_pct = 0f32; let mut spd_pct = 0f32;

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
        strength: ((base_str + str_flat) as f32 * (1.0 + str_pct)) as i32,
        wisdom: ((base_wis + wis_flat) as f32 * (1.0 + wis_pct)) as i32,
        dexterity: ((base_dex + dex_flat) as f32 * (1.0 + dex_pct)) as i32,
        defense: ((base_def + def_flat) as f32 * (1.0 + def_pct)) as i32,
        vitality: ((base_vit + vit_flat) as f32 * (1.0 + vit_pct)) as i32,
        speed: ((base_spd + spd_flat) as f32 * (1.0 + spd_pct)) as i32,
    };

    if ctx.db.player_stats().player_id().find(player_id).is_some() {
        ctx.db.player_stats().player_id().update(new_stats);
    } else {
        ctx.db.player_stats().insert(new_stats);
    }
}

pub fn seed_new_player(ctx: &ReducerContext, player_id: Identity) {
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let (cq, cr) = world_to_chunk(0.0, 0.0, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    ctx.db.logged_in_player().insert(Player { player_id, username: String::new(), is_admin: false });
    ctx.db.player_data().insert(PlayerData { player_id, level: 1, xp: 0, hp: BASE_MAX_HP as u32, max_hp: BASE_MAX_HP as u32 });
    ctx.db.player_inventory().insert(PlayerInventory { player_id, slots: Vec::new() });
    ctx.db.player_position().insert(PlayerPosition { player_id, x: 0.0, y: 0.0, rotation: 0.0, chunk_index });
    recompute_stats(ctx, player_id);
}

pub fn compute_player_damage(ctx: &ReducerContext, player_id: Identity) -> u32 {
    let stats = match ctx.db.player_stats().player_id().find(player_id) {
        Some(s) => s,
        None => return 1,
    };
    let inv = match ctx.db.player_inventory().player_id().find(player_id) {
        Some(i) => i,
        None => return 1,
    };
    let weapon_slot = inv.slots.iter().find(|s| matches!(s.kind, SlotKind::Weapon));
    let item = weapon_slot.and_then(|s| ctx.db.item_stats().item_id().find(&s.item_id));
    match item {
        Some(i) => {
            let dmg = i.base_damage as f32
                + (stats.strength * i.str_damage_add) as f32
                + (stats.wisdom * i.wis_damage_add) as f32
                + (stats.dexterity * i.dex_damage_add) as f32;
            dmg.max(1.0) as u32
        }
        None => 1,
    }
}

pub fn internal_gain_xp(ctx: &ReducerContext, player_id: Identity, amount: u32) {
    let data = match ctx.db.player_data().player_id().find(player_id) {
        Some(d) => d,
        None => return,
    };
    let old_level = data.level;
    let new_xp = data.xp + amount;
    let new_level = compute_level(new_xp);
    let new_max_hp = (BASE_MAX_HP + (new_level as i32 - 1) * HP_PER_LEVEL) as u32;
    let heal = (new_level - old_level) * HP_PER_LEVEL as u32;
    let new_hp = (data.hp + heal).min(new_max_hp);
    ctx.db.player_data().player_id().update(PlayerData { level: new_level, xp: new_xp, hp: new_hp, max_hp: new_max_hp, ..data });
    if new_level != old_level {
        recompute_stats(ctx, player_id);
    }
}
