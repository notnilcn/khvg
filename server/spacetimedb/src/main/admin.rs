use spacetimedb::{ReducerContext, Identity, Table};
use crate::world::methods::*;
use crate::world::tables::*;
use crate::player::tables::*;
use crate::player::methods::*;
use crate::enemy::tables::*;
use crate::enemy::methods::build_enemy_behavior;

const STRESS_TEMPLATE_IDS: &[&str] = &["LowStress", "MedStress", "HighStress"];

#[spacetimedb::reducer]
pub fn claim_admin(ctx: &ReducerContext) -> Result<(), String> {
    let any_admin = ctx.db.logged_in_player().iter().any(|p| p.is_admin)
        || ctx.db.logged_out_player().iter().any(|p| p.is_admin);
    if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        if p.is_admin { return Ok(()); }
        if any_admin { return Err("An admin already exists. Use release_admin to remove them first.".to_string()); }
        ctx.db.logged_in_player().player_id().update(LoggedInPlayer { is_admin: true, ..p });
    } else if let Some(p) = ctx.db.logged_out_player().player_id().find(ctx.sender()) {
        if p.is_admin { return Ok(()); }
        if any_admin { return Err("An admin already exists. Use release_admin to remove them first.".to_string()); }
        ctx.db.logged_out_player().player_id().update(LoggedOutPlayer { is_admin: true, ..p });
    } else {
        return Err("You must connect first so a Player row exists.".to_string());
    }
    log::info!("claim_admin: {:?} is now admin.", ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn release_admin(ctx: &ReducerContext) -> Result<(), String> {
    if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        if !p.is_admin { return Err("You are not an admin.".to_string()); }
        ctx.db.logged_in_player().player_id().update(LoggedInPlayer { is_admin: false, ..p });
    } else if let Some(p) = ctx.db.logged_out_player().player_id().find(ctx.sender()) {
        if !p.is_admin { return Err("You are not an admin.".to_string()); }
        ctx.db.logged_out_player().player_id().update(LoggedOutPlayer { is_admin: false, ..p });
    } else {
        return Err("No Player row found.".to_string());
    }
    log::info!("release_admin: {:?} released admin.", ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn change_stats(ctx: &ReducerContext, target_username: String, profile_name: String, strength: i32, wisdom: i32, dexterity: i32, defense: i32, vitality: i32, speed: i32) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let profile = find_profile_by_name(ctx, target_id, &profile_name)
        .ok_or_else(|| format!("Profile '{}' not found.", profile_name))?;
    let stats = ctx.db.player_stats().profile_id().find(&profile.profile_id).ok_or("Stats not found.")?;
    ctx.db.player_stats().profile_id().update(PlayerStats { strength, wisdom, dexterity, defense, vitality, speed, ..stats });
    Ok(())
}

#[spacetimedb::reducer]
pub fn give_item(ctx: &ReducerContext, target_username: String, profile_name: String, item_id: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let profile = find_profile_by_name(ctx, target_id, &profile_name)
        .ok_or_else(|| format!("Profile '{}' not found.", profile_name))?;
    let inv = ctx.db.player_inventory().profile_id().find(&profile.profile_id).ok_or("Inventory not found.")?;
    let empty_slot = inv.slots.iter().enumerate()
        .skip(14).take(9)
        .find(|(_, s)| s.item_id.is_none())
        .map(|(i, _)| i)
        .ok_or("No empty general slots available.")?;
    let mut slots = inv.slots.clone();
    slots[empty_slot].item_id = Some(item_id);
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_item(ctx: &ReducerContext, target_username: String, profile_name: String, item_id: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let profile = find_profile_by_name(ctx, target_id, &profile_name)
        .ok_or_else(|| format!("Profile '{}' not found.", profile_name))?;
    let inv = ctx.db.player_inventory().profile_id().find(&profile.profile_id).ok_or("Inventory not found.")?;
    let slots: Vec<InventorySlot> = inv.slots.iter().map(|s| {
        if s.item_id.as_deref() == Some(item_id.as_str()) {
            InventorySlot { item_id: None, ..s.clone() }
        } else { s.clone() }
    }).collect();
    ctx.db.player_inventory().profile_id().update(PlayerInventory { slots, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_texture_entry(ctx: &ReducerContext, entry: TextureEntry) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.texture_entry().texture_id().find(&entry.texture_id).is_some() {
        ctx.db.texture_entry().texture_id().update(entry);
    } else {
        ctx.db.texture_entry().insert(entry);
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
pub fn upsert_enemy_template(ctx: &ReducerContext, template: EnemyTemplate) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.enemy_template().template_id().find(&template.template_id).is_some() {
        ctx.db.enemy_template().template_id().update(template);
    } else {
        ctx.db.enemy_template().insert(template);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn spawn_enemy(ctx: &ReducerContext, template_id: String, x: f32, y: f32, is_elite: bool, immortal: bool) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let template = ctx.db.enemy_template().template_id().find(&template_id)
        .ok_or_else(|| format!("Template '{}' not found.", template_id))?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let chunk_rows = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(1);
    let (x, y) = wrap_world_pos(x, y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    let inserted_behavior = ctx.db.enemy_behavior().insert(build_enemy_behavior(ctx, &template));
    ctx.db.enemy().insert(Enemy {
        enemy_id: 0, template_id, behavior_id: inserted_behavior.behavior_id,
        x, y, spawn_time: ctx.timestamp, hp: template.max_hp,
        phase: 0, is_elite, immortal, chunk_index,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn despawn_enemy(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    ctx.db.enemy().enemy_id().delete(&enemy_id);
    Ok(())
}

pub fn internal_add_chunks(ctx: &ReducerContext, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> Result<(), String> {
    if !(chunk_rows % chunk_cols == 0) { return Err("chunk_cols must cleanly divide into chunk_rows.".to_string()); }
    if hex_outer_radius <= 0.0 { return Err("hex_outer_radius must be positive.".to_string()); }
    let area = hex_area(chunk_hex_radius);
    let shift = hex_shift(chunk_hex_radius);
    for cr in 0..chunk_rows {
        for cq in 0..chunk_cols {
            let cidx = spiral_chunk_index(cq, cr);
            let (cx, cz) = chunk_center_hex(cq, cr, chunk_hex_radius);
            for m in 0..area {
                let (rx, rz) = inv_hexmod(m, chunk_hex_radius, shift);
                ctx.db.building_tile().insert(BuildingTile {
                    tile_id: 0, hex_q: cx + rx, hex_r: cz + rz,
                    chunk_q: cq, chunk_r: cr, local_index: m, chunk_index: cidx,
                    building_type: String::new(), owner_id: None,
                });
            }
        }
    }
    if ctx.db.map_config().id().find(&0u32).is_some() {
        ctx.db.map_config().id().update(MapConfig { id: 0, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius });
    } else {
        ctx.db.map_config().insert(MapConfig { id: 0, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius });
    }
    log::info!("add_chunks: {} tiles inserted ({} chunks, area {})", chunk_cols * chunk_rows * area, chunk_cols * chunk_rows, area);
    Ok(())
}

#[spacetimedb::reducer]
pub fn add_chunks(ctx: &ReducerContext, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    internal_add_chunks(ctx, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius)
}

#[spacetimedb::reducer]
pub fn clear_chunks(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let count = ctx.db.building_tile().iter().count();
    for tile in ctx.db.building_tile().iter() {
        ctx.db.building_tile().tile_id().delete(&tile.tile_id);
    }
    log::info!("clear_chunks: {} tiles deleted.", count);
    Ok(())
}

#[spacetimedb::reducer]
pub fn spawn_stress_test(ctx: &ReducerContext, low: u32, med: u32, high: u32, spread_radius: f32) -> Result<(), String> {
    //if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let chunk_rows = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(1);
    let us = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros() as u64;

    let counts = [("LowStress", low), ("MedStress", med), ("HighStress", high)];
    let mut spawn_index: u64 = 0;

    for (template_id, count) in counts {
        if count == 0 { continue; }
        let template = match ctx.db.enemy_template().template_id().find(&template_id.to_string()) {
            Some(t) => t,
            None => { log::warn!("spawn_stress_test: template '{}' not found, skipping.", template_id); continue; }
        };
        for _ in 0..count {
            let h = spawn_index.wrapping_add(us);
            let h = (h ^ (h >> 30)).wrapping_mul(0xbf58476d1ce4e5b9);
            let h = (h ^ (h >> 27)).wrapping_mul(0x94d049bb133111eb);
            let h = h ^ (h >> 31);
            let angle  = (h >> 11) as f32 / (1u64 << 53) as f32 * std::f32::consts::TAU;
            let h2     = h.wrapping_mul(0x9e3779b97f4a7c15);
            let radius = ((h2 >> 11) as f32 / (1u64 << 53) as f32).sqrt() * spread_radius;
            let (x, y) = wrap_world_pos(radius * angle.cos(), radius * angle.sin(), chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
            let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
            let chunk_index = spiral_chunk_index(cq, cr);
            let inserted_behavior = ctx.db.enemy_behavior().insert(build_enemy_behavior(ctx, &template));
            ctx.db.enemy().insert(Enemy {
                enemy_id: 0, template_id: template.template_id.clone(),
                behavior_id: inserted_behavior.behavior_id,
                x, y, spawn_time: ctx.timestamp,
                hp: template.max_hp, phase: 0, is_elite: false, immortal: false, chunk_index,
            });
            spawn_index += 1;
        }
    }
    log::info!("spawn_stress_test: spawned {} low, {} med, {} high.", low, med, high);
    Ok(())
}

#[spacetimedb::reducer]
pub fn clear_stress_test(ctx: &ReducerContext) -> Result<(), String> {
    //if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let enemies: Vec<Enemy> = ctx.db.enemy().iter()
        .filter(|e| STRESS_TEMPLATE_IDS.contains(&e.template_id.as_str()))
        .collect();
    let count = enemies.len();
    for enemy in enemies {
        ctx.db.enemy_behavior().behavior_id().delete(&enemy.behavior_id);
        ctx.db.enemy().enemy_id().delete(&enemy.enemy_id);
    }
    log::info!("clear_stress_test: removed {} stress test enemies.", count);
    Ok(())
}

pub fn find_player_by_username(ctx: &ReducerContext, lookup: &str) -> Option<Identity> {
    let lower = lookup.to_lowercase();
    let hex = lower.trim_start_matches("0x");
    let all = ctx.db.logged_in_player().iter().map(|p| (p.player_id, p.username))
        .chain(ctx.db.logged_out_player().iter().map(|p| (p.player_id, p.username)));
    for (player_id, username) in all {
        let id_hex = player_id.to_hex().to_string().to_lowercase();
        if (!username.is_empty() && username.to_lowercase() == lower)
            || id_hex == hex
            || (!hex.is_empty() && id_hex.starts_with(hex))
        {
            return Some(player_id);
        }
    }
    None
}
