use spacetimedb::{ReducerContext, Identity, Table};
use crate::world::hex::{chunk_center_hex, hex_area, hex_shift, hex_to_world, inv_hexmod, spiral_chunk_index, world_to_chunk};
use crate::world::wrap::wrap_world_pos;
use crate::world::terrain::{internal_generate_world_manual, internal_generate_world_proc};
use crate::world::tables::{building_tile, map_config, texture_entry, BuildingTile, MapConfig, TextureEntry};
use crate::world::def_tables::{biome_def, biome_region_def, world_def, BiomeDef, BiomeRegionDef, ManualBiomeInput, WorldDef};
use crate::player::tables::*;
use crate::player::methods::*;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;
use crate::enemy::methods::build_enemy_behavior;

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
    let cfg = MapConfig::load(ctx, 1, 1);
    let (x, y) = wrap_world_pos(x, y, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, cfg.chunk_hex_radius, cfg.hex_outer_radius);
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
    let (pq_hq, pq_hr) = chunk_center_hex(chunk_cols, 0, chunk_hex_radius);
    let (pr_hq, pr_hr) = chunk_center_hex(0, chunk_rows, chunk_hex_radius);
    let (lap_q_x, lap_q_y) = hex_to_world(pq_hq, pq_hr, hex_outer_radius);
    let (lap_r_x, lap_r_y) = hex_to_world(pr_hq, pr_hr, hex_outer_radius);
    let cfg = MapConfig { id: 0, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius, lap_q_x, lap_q_y, lap_r_x, lap_r_y };
    if ctx.db.map_config().id().find(&0u32).is_some() {
        ctx.db.map_config().id().update(cfg);
    } else {
        ctx.db.map_config().insert(cfg);
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
pub fn upsert_world_def(ctx: &ReducerContext, def: WorldDef) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.world_def().world_id().find(&def.world_id).is_some() {
        ctx.db.world_def().world_id().update(def);
    } else {
        ctx.db.world_def().insert(def);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_biome_def(ctx: &ReducerContext, def: BiomeDef) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.biome_def().biome_id().find(&def.biome_id).is_some() {
        ctx.db.biome_def().biome_id().update(def);
    } else {
        ctx.db.biome_def().insert(def);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_biome_region_def(ctx: &ReducerContext, def: BiomeRegionDef) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.biome_region_def().region_def_id().find(&def.region_def_id).is_some() {
        ctx.db.biome_region_def().region_def_id().update(def);
    } else {
        ctx.db.biome_region_def().insert(def);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn generate_world_proc(ctx: &ReducerContext, world_id: String, seed: u64) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    internal_generate_world_proc(ctx, world_id, seed)
}

#[spacetimedb::reducer]
pub fn generate_world_manual(ctx: &ReducerContext, biomes: Vec<ManualBiomeInput>) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    internal_generate_world_manual(ctx, biomes)
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
