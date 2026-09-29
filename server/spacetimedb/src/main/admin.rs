use spacetimedb::{ReducerContext, Identity, Table, ScheduleAt, TimeDuration};
use std::time::Duration;
use crate::world::hex::{chunk_center_hex, hex_area, hex_to_world, inv_hexmod, spiral_chunk_index, world_to_chunk};
use crate::world::wrap::wrap_world_pos;
use crate::world::terrain::{internal_generate_world_manual, internal_generate_world_proc};
use crate::world::tables::{animation_entry, building_tile, map_config, texture_entry, AnimationEntry, BuildingTile, MapConfig, TextureEntry, TextureKind};
use crate::world::building::{building_state, footprint_tile, project_site};
use crate::world::def_tables::{biome_def, biome_region_def, world_def, BiomeDef, BiomeRegionDef, ManualBiomeInput, WorldDef};
use crate::player::tables::*;
use crate::player::methods::*;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;
use crate::enemy::methods::{despawn_enemy_archetype, spawn_enemy_archetype};

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
pub fn change_stats(ctx: &ReducerContext, target_username: String, profile_name: String, strength: i32, wisdom: i32, dexterity: i32, damage_dealer: i32, supporter: i32, artisan: i32) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let profile = find_profile_by_name(ctx, target_id, &profile_name)
        .ok_or_else(|| format!("Profile '{}' not found.", profile_name))?;
    // Write the allocation input, not the PlayerStats output row — recompute_stats
    // rebuilds PlayerStats from allocation + gear on every inventory reducer, so values
    // written there are wiped on the next equip. Clamp negatives: allocation is unsigned.
    let alloc = ctx.db.player_stat_allocation().profile_id().find(&profile.profile_id).ok_or("Stat allocation not found.")?;
    ctx.db.player_stat_allocation().profile_id().update(PlayerStatAllocation {
        strength: strength.max(0) as u32,
        wisdom: wisdom.max(0) as u32,
        dexterity: dexterity.max(0) as u32,
        damage_dealer: damage_dealer.max(0) as u32,
        supporter: supporter.max(0) as u32,
        artisan: artisan.max(0) as u32,
        ..alloc
    });
    recompute_stats(ctx, profile.profile_id);
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

/// Field-wise upsert into `texture_entry` for admin CLI authoring: looks up the
/// row by `texture_id`, inserting when absent and updating when present.
/// Pass "_" for any of `res_path2d`/`res_path3d`/`kind` to preserve the
/// existing entry's value for that column on update. "_" on insert is an
/// error — a new row has nothing to preserve, so pass explicit values
/// (use "" for `res_path3d` when there is no 3D representation).
/// `kind` is parsed case-insensitively: Player|Enemy|Projectile|Item|Environment.
#[spacetimedb::reducer]
pub fn upsert_texture_entry_fields(ctx: &ReducerContext, texture_id: String, res_path2d: String, res_path3d: String, kind: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if texture_id.is_empty() { return Err("texture_id cannot be empty.".to_string()); }
    if texture_id == "_" { return Err("texture_id cannot be \"_\".".to_string()); }
    fn parse_kind(s: &str) -> Result<TextureKind, String> {
        match s.to_ascii_lowercase().as_str() {
            "player" => Ok(TextureKind::Player),
            "enemy" => Ok(TextureKind::Enemy),
            "projectile" => Ok(TextureKind::Projectile),
            "item" => Ok(TextureKind::Item),
            "environment" => Ok(TextureKind::Environment),
            _ => Err(format!("Unknown texture kind '{}'. Expected Player|Enemy|Projectile|Item|Environment or \"_\" to preserve.", s)),
        }
    }
    if let Some(prev) = ctx.db.texture_entry().texture_id().find(&texture_id) {
        let next = TextureEntry {
            texture_id,
            res_path2d: if res_path2d == "_" { prev.res_path2d.clone() } else { res_path2d },
            res_path3d: if res_path3d == "_" { prev.res_path3d.clone() } else { res_path3d },
            kind: if kind == "_" { prev.kind.clone() } else { parse_kind(&kind)? },
        };
        ctx.db.texture_entry().texture_id().update(next);
    } else {
        if res_path2d == "_" || res_path3d == "_" || kind == "_" {
            return Err(format!("Texture '{}' not found; pass explicit values for all fields to insert (\"_\" only preserves an existing entry).", texture_id));
        }
        ctx.db.texture_entry().insert(TextureEntry {
            texture_id,
            res_path2d,
            res_path3d,
            kind: parse_kind(&kind)?,
        });
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_animation_entry(ctx: &ReducerContext, entry: AnimationEntry) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.animation_entry().animation_id().find(&entry.animation_id).is_some() {
        ctx.db.animation_entry().animation_id().update(entry);
    } else {
        ctx.db.animation_entry().insert(entry);
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
    spawn_enemy_archetype(ctx, &template, x, y, is_elite, immortal, chunk_index);
    Ok(())
}

#[spacetimedb::reducer]
pub fn despawn_enemy(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let enemy = ctx.db.enemy().enemy_id().find(&enemy_id)
        .ok_or_else(|| format!("Enemy {} not found.", enemy_id))?;
    despawn_enemy_archetype(ctx, &enemy);
    // Same kill consult as despawn_all_enemies / the combat path: purge statuses
    // referencing the dead enemy and its own debuff rows.
    crate::status::methods::on_enemy_killed(ctx, enemy.enemy_id);
    Ok(())
}

/// Toggles biome-region enemy spawning on/off by cancelling or re-arming the
/// EnemySpawnSchedule job row (the 2s tick that drives spawn_from_biome_regions).
/// Spawning starts disarmed — init does not insert the job row.
/// Admin-spawned enemies and the behavior tick are unaffected.
#[spacetimedb::reducer]
pub fn toggle_enemy_spawning(ctx: &ReducerContext, enabled: bool) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if enabled {
        if ctx.db.enemy_spawn_schedule().iter().next().is_none() {
            ctx.db.enemy_spawn_schedule().insert(EnemySpawnSchedule {
                scheduled_id: 0,
                scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_secs(2))),
            });
            log::info!("toggle_enemy_spawning: re-armed by {:?}.", ctx.sender());
        }
    } else {
        let ids: Vec<u64> = ctx.db.enemy_spawn_schedule().iter().map(|j| j.scheduled_id).collect();
        for id in ids {
            ctx.db.enemy_spawn_schedule().scheduled_id().delete(&id);
        }
        log::info!("toggle_enemy_spawning: disabled by {:?}.", ctx.sender());
    }
    Ok(())
}

/// Wipes every spawned enemy (biome-spawned and admin-spawned alike) through the
/// archetype teardown + status purge — no XP, unlike the combat kill path.
#[spacetimedb::reducer]
pub fn despawn_all_enemies(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let enemies: Vec<Enemy> = ctx.db.enemy().iter().collect();
    let count = enemies.len();
    for enemy in enemies {
        despawn_enemy_archetype(ctx, &enemy);
        crate::status::methods::on_enemy_killed(ctx, enemy.enemy_id);
    }
    log::info!("despawn_all_enemies: {:?} wiped {} enemies.", ctx.sender(), count);
    Ok(())
}

// Runtime enemy-authoring upserts. `def_id` is auto-increment, so pass 0 to insert a new row
// (then discover the assigned id via `spacetime sql`), or an existing id to update that row.

#[spacetimedb::reducer]
pub fn upsert_movement_def(ctx: &ReducerContext, def: MovementDef) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if def.def_id != 0 && ctx.db.movement_def().def_id().find(&def.def_id).is_some() {
        ctx.db.movement_def().def_id().update(def);
    } else {
        ctx.db.movement_def().insert(MovementDef { def_id: 0, ..def });
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_single_step_def(ctx: &ReducerContext, def: SingleStepDef) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if def.def_id != 0 && ctx.db.single_step_def().def_id().find(&def.def_id).is_some() {
        ctx.db.single_step_def().def_id().update(def);
    } else {
        ctx.db.single_step_def().insert(SingleStepDef { def_id: 0, ..def });
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_repeat_step_def(ctx: &ReducerContext, def: RepeatStepDef) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if def.def_id != 0 && ctx.db.repeat_step_def().def_id().find(&def.def_id).is_some() {
        ctx.db.repeat_step_def().def_id().update(def);
    } else {
        ctx.db.repeat_step_def().insert(RepeatStepDef { def_id: 0, ..def });
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_multi_step_def(ctx: &ReducerContext, def: MultiStepDef) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if def.def_id != 0 && ctx.db.multi_step_def().def_id().find(&def.def_id).is_some() {
        ctx.db.multi_step_def().def_id().update(def);
    } else {
        ctx.db.multi_step_def().insert(MultiStepDef { def_id: 0, ..def });
    }
    Ok(())
}

pub fn internal_add_chunks(ctx: &ReducerContext, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> Result<(), String> {
    if !(chunk_rows % chunk_cols == 0) { return Err("chunk_cols must cleanly divide into chunk_rows.".to_string()); }
    if hex_outer_radius <= 0.0 { return Err("hex_outer_radius must be positive.".to_string()); }
    let area = hex_area(chunk_hex_radius);
    for cr in 0..chunk_rows {
        for cq in 0..chunk_cols {
            let cidx = spiral_chunk_index(cq, cr);
            let (cx, cz) = chunk_center_hex(cq, cr, chunk_hex_radius);
            for m in 0..area {
                let (rx, rz) = inv_hexmod(m, chunk_hex_radius);
                ctx.db.building_tile().insert(BuildingTile {
                    tile_id: 0, hex_q: cx + rx, hex_r: cz + rz,
                    chunk_q: cq, chunk_r: cr, local_index: m, chunk_index: cidx,
                    building_type: String::new(), owner_id: None,
                    // Filled by the generation elevation pass (terrain::run_generation).
                    height: 0,
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
    // BitCraft cascade mirror: wiping the hex grid orphans footprints/sites, so clear
    // the building records alongside the tiles (same reason delete_footprint deletes
    // both tables).
    for fp in ctx.db.footprint_tile().iter() {
        ctx.db.footprint_tile().footprint_id().delete(&fp.footprint_id);
    }
    for b in ctx.db.building_state().iter() {
        ctx.db.building_state().building_entity_id().delete(&b.building_entity_id);
    }
    for s in ctx.db.project_site().iter() {
        ctx.db.project_site().site_id().delete(&s.site_id);
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
