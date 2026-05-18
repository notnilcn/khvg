use spacetimedb::{ReducerContext, Identity, Table};
use std::time::Duration;
use super::tables::*;
use super::methods::*;

// ── Lifecycle ────────────────────────────────────────────────────────────────

#[spacetimedb::reducer(client_connected)]
pub fn client_connected(ctx: &ReducerContext) {
    if ctx.db.player().player_id().find(ctx.sender()).is_none() {
        ctx.db.player().insert(Player {
            player_id: ctx.sender(),
            username: String::new(),
            is_admin: false,
        });
        ctx.db.player_data().insert(PlayerData {
            player_id: ctx.sender(),
            level: 1,
            xp: 0,
            hp: BASE_MAX_HP as u32,
            max_hp: BASE_MAX_HP as u32,
        });
        ctx.db.player_inventory().insert(PlayerInventory {
            player_id: ctx.sender(),
            slots: Vec::new(),
        });
        recompute_stats(ctx, ctx.sender());
        log::info!("New player: {:?}", ctx.sender());
    } else {
        log::info!("Returning player: {:?}", ctx.sender());
    }

    let spawn_x = 0.0f32;
    let spawn_y = 0.0f32;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let (cq, cr) = world_to_chunk(spawn_x, spawn_y, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    ctx.db.player_position().insert(PlayerPosition {
        player_id: ctx.sender(),
        x: spawn_x,
        y: spawn_y,
        rotation: 0.0,
        chunk_index,
    });
}

#[spacetimedb::reducer(client_disconnected)]
pub fn client_disconnected(ctx: &ReducerContext) {
    ctx.db.player_position().player_id().delete(ctx.sender());
    log::info!("Player disconnected: {:?}", ctx.sender());
}

// ── Player reducers ──────────────────────────────────────────────────────────

#[spacetimedb::reducer]
pub fn set_username(ctx: &ReducerContext, username: String) -> Result<(), String> {
    if username.trim().is_empty() || username.len() > 20 {
        return Err("Username must be 1–20 non-whitespace characters.".to_string());
    }
    let player = ctx.db.player().player_id().find(ctx.sender())
        .ok_or("Player not found.")?;
    ctx.db.player().player_id().update(Player { username, ..player });
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_movement(ctx: &ReducerContext, x: f32, y: f32, rotation: f32) -> Result<(), String> {
    let pos = ctx.db.player_position().player_id().find(ctx.sender())
        .ok_or_else(|| format!("Position not found for {:?}", ctx.sender()))?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let chunk_rows       = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(1);
    let (x, y) = wrap_world_pos(x, y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    ctx.db.player_position().player_id().update(PlayerPosition {
        x, y, rotation, chunk_index,
        ..pos
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn gain_xp(ctx: &ReducerContext, amount: u32) -> Result<(), String> {
    const MAX_XP_PER_GAIN: u32 = 500;
    if amount == 0 { return Ok(()); }
    if amount > MAX_XP_PER_GAIN {
        return Err(format!("XP gain {} exceeds per-call maximum of {}.", amount, MAX_XP_PER_GAIN));
    }
    let data = ctx.db.player_data().player_id().find(ctx.sender())
        .ok_or("Player data not found.")?;
    let old_level = data.level;
    let new_xp = data.xp + amount;
    let new_level = compute_level(new_xp);
    let new_max_hp = (BASE_MAX_HP + (new_level as i32 - 1) * HP_PER_LEVEL) as u32;
    let heal = (new_level - old_level) * HP_PER_LEVEL as u32;
    let new_hp = (data.hp + heal).min(new_max_hp);
    ctx.db.player_data().player_id().update(PlayerData {
        level: new_level, xp: new_xp, hp: new_hp, max_hp: new_max_hp,
        ..data
    });
    if new_level != old_level {
        recompute_stats(ctx, ctx.sender());
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn update_inventory(ctx: &ReducerContext, slots: Vec<InventorySlot>) -> Result<(), String> {
    let backpack_count = slots.iter().filter(|s| matches!(s.kind, SlotKind::Backpack)).count();
    let accessory_count = slots.iter().filter(|s| matches!(s.kind, SlotKind::Accessory)).count();
    let consumable_count = slots.iter().filter(|s| matches!(s.kind, SlotKind::Consumable)).count();
    if backpack_count > 12 { return Err("Too many backpack slots.".to_string()); }
    if accessory_count > 3 { return Err("Too many accessory slots.".to_string()); }
    if consumable_count > 8 { return Err("Too many consumable slots.".to_string()); }
    let inv = ctx.db.player_inventory().player_id().find(ctx.sender())
        .ok_or("Inventory not found.")?;
    ctx.db.player_inventory().player_id().update(PlayerInventory { slots, ..inv });
    recompute_stats(ctx, ctx.sender());
    Ok(())
}

// ── Enemy hit reporting ──────────────────────────────────────────────────────

// Client calls this when their weapon hits an enemy. Server computes damage from
// the caller's stats + equipped weapon, applies it to the enemy, and handles death.
#[spacetimedb::reducer]
pub fn report_enemy_hit(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    let enemy = match ctx.db.enemy().enemy_id().find(&enemy_id) {
        Some(e) => e,
        None => return Ok(()),
    };
    if enemy.immortal { return Ok(()); }

    let damage = compute_player_damage(ctx, ctx.sender());
    let new_hp = enemy.hp.saturating_sub(damage);

    if new_hp == 0 {
        let template = ctx.db.enemy_template().template_id().find(&enemy.template_id);
        let xp_reward = template.as_ref().map(|t| t.max_hp / 10).unwrap_or(10);
        ctx.db.loot_drop().insert(LootDrop {
            drop_id: 0,
            item_id: String::new(),
            x: enemy.spawn_x,
            y: enemy.spawn_y,
            chunk_index: enemy.chunk_index,
            expires_at: ctx.timestamp + Duration::from_secs(60),
        });
        ctx.db.enemy().enemy_id().delete(&enemy_id);
        internal_gain_xp(ctx, ctx.sender(), xp_reward);
        return Ok(());
    }

    let new_phase = match ctx.db.enemy_template().template_id().find(&enemy.template_id) {
        Some(template) => compute_phase(new_hp, template.max_hp, &template.phases),
        None => enemy.phase,
    };

    ctx.db.enemy().enemy_id().update(Enemy { hp: new_hp, phase: new_phase, ..enemy });
    Ok(())
}

// ── Loot pickup ──────────────────────────────────────────────────────────────

#[spacetimedb::reducer]
pub fn pickup_drop(ctx: &ReducerContext, drop_id: u64) -> Result<(), String> {
    let drop = ctx.db.loot_drop().drop_id().find(&drop_id)
        .ok_or("Drop not found.")?;
    if drop.item_id.is_empty() { return Ok(()); }
    let inv = ctx.db.player_inventory().player_id().find(ctx.sender())
        .ok_or("Inventory not found.")?;
    let backpack_count = inv.slots.iter().filter(|s| matches!(s.kind, SlotKind::Backpack)).count();
    if backpack_count >= 12 { return Err("Backpack full.".to_string()); }
    let next_index = inv.slots.iter()
        .filter(|s| matches!(s.kind, SlotKind::Backpack))
        .map(|s| s.index)
        .max()
        .map(|i| i + 1)
        .unwrap_or(0);
    let mut slots = inv.slots.clone();
    slots.push(InventorySlot { kind: SlotKind::Backpack, index: next_index, item_id: drop.item_id });
    ctx.db.player_inventory().player_id().update(PlayerInventory { slots, ..inv });
    ctx.db.loot_drop().drop_id().delete(&drop_id);
    Ok(())
}

// ── Admin reducers ───────────────────────────────────────────────────────────

// Bootstrap: caller becomes admin only if no admins exist yet.
// If the caller is already admin, succeeds silently (idempotent).
#[spacetimedb::reducer]
pub fn claim_admin(ctx: &ReducerContext) -> Result<(), String> {
    let player = ctx.db.player().player_id().find(ctx.sender())
        .ok_or("You must connect first so a Player row exists.")?;
    if player.is_admin {
        return Ok(()); // already admin, nothing to do
    }
    let any_admin = ctx.db.player().iter().any(|p| p.is_admin);
    if any_admin {
        return Err("An admin already exists. Use release_admin to remove them first.".to_string());
    }
    ctx.db.player().player_id().update(Player { is_admin: true, ..player });
    log::info!("claim_admin: {:?} is now admin.", ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn release_admin(ctx: &ReducerContext) -> Result<(), String> {
    let player = ctx.db.player().player_id().find(ctx.sender())
        .ok_or("No Player row found.")?;
    if !player.is_admin {
        return Err("You are not an admin.".to_string());
    }
    ctx.db.player().player_id().update(Player { is_admin: false, ..player });
    log::info!("release_admin: {:?} released admin.", ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn change_stats(ctx: &ReducerContext, target_username: String, strength: i32, wisdom: i32, dexterity: i32, defense: i32, vitality: i32, speed: i32) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let stats = ctx.db.player_stats().player_id().find(target_id)
        .ok_or("Stats not found.")?;
    ctx.db.player_stats().player_id().update(PlayerStats {
        strength, wisdom, dexterity, defense, vitality, speed, ..stats
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn give_item(ctx: &ReducerContext, target_username: String, item_id: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let inv = ctx.db.player_inventory().player_id().find(target_id)
        .ok_or("Inventory not found.")?;
    let next_index = inv.slots.iter()
        .filter(|s| matches!(s.kind, SlotKind::Backpack))
        .map(|s| s.index)
        .max()
        .map(|i| i + 1)
        .unwrap_or(0);
    let mut slots = inv.slots.clone();
    slots.push(InventorySlot { kind: SlotKind::Backpack, index: next_index, item_id });
    ctx.db.player_inventory().player_id().update(PlayerInventory { slots, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_item(ctx: &ReducerContext, target_username: String, item_id: String) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let target_id = find_player_by_username(ctx, &target_username)
        .ok_or_else(|| format!("Player '{}' not found.", target_username))?;
    let inv = ctx.db.player_inventory().player_id().find(target_id)
        .ok_or("Inventory not found.")?;
    let slots: Vec<InventorySlot> = inv.slots.iter()
        .filter(|s| s.item_id != item_id)
        .cloned()
        .collect();
    ctx.db.player_inventory().player_id().update(PlayerInventory { slots, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_item_stats(ctx: &ReducerContext, stats: ItemStats) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if ctx.db.item_stats().item_id().find(&stats.item_id).is_some() {
        ctx.db.item_stats().item_id().update(stats);
    } else {
        ctx.db.item_stats().insert(stats);
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
pub fn spawn_enemy(ctx: &ReducerContext, template_id: String, res_path: String, x: f32, y: f32, is_elite: bool, immortal: bool) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    let template = ctx.db.enemy_template().template_id().find(&template_id)
        .ok_or_else(|| format!("Template '{}' not found.", template_id))?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(1);
    let chunk_rows       = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(1);
    let (x, y) = wrap_world_pos(x, y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    ctx.db.enemy().insert(Enemy {
        enemy_id: 0,
        template_id,
        res_path,
        spawn_x: x,
        spawn_y: y,
        spawn_time: ctx.timestamp,
        hp: template.max_hp,
        phase: 0,
        target: EnemyTarget::NearestPlayer,
        is_elite,
        immortal,
        chunk_index,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn despawn_enemy(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    ctx.db.enemy().enemy_id().delete(&enemy_id);
    Ok(())
}

// ── Hex building grid ────────────────────────────────────────────────────────

// Pre-populates every BuildingTile row for the map. Admin-only; call once after deploy.
// hexRadius: chunk radius in small hexes (1–10)
// chunkCols: rhombus width in chunks (1–200)
// chunkRows: rhombus height in chunks (1–200)
#[spacetimedb::reducer]
pub fn add_chunks(ctx: &ReducerContext, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    if !(1..=10).contains(&chunk_hex_radius)  { return Err("chunk_hex_radius must be 1–10.".to_string()); }
    if !(1..=200).contains(&chunk_cols) { return Err("chunk_cols must be 1–200.".to_string()); }
    if !(1..=200).contains(&chunk_rows) { return Err("chunk_rows must be 1–200.".to_string()); }
    if hex_outer_radius <= 0.0 { return Err("hex_outer_radius must be positive.".to_string()); }

    let area  = hex_area(chunk_hex_radius);
    let shift = hex_shift(chunk_hex_radius);

    for cr in 0..chunk_rows {
        for cq in 0..chunk_cols {
            let cidx = spiral_chunk_index(cq, cr);
            let (cx, cz) = chunk_center_hex(cq, cr, chunk_hex_radius);
            for m in 0..area {
                let (rx, rz) = inv_hexmod(m, chunk_hex_radius, shift);
                ctx.db.building_tile().insert(BuildingTile {
                    tile_id: 0,
                    hex_q: cx + rx,
                    hex_r: cz + rz,
                    chunk_q: cq,
                    chunk_r: cr,
                    local_index: m,
                    chunk_index: cidx,
                    building_type: String::new(),
                    owner_id: None,
                });
            }
        }
    }
    if ctx.db.map_config().id().find(&0u32).is_some() {
        ctx.db.map_config().id().update(MapConfig {
            id: 0, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius,
        });
    } else {
        ctx.db.map_config().insert(MapConfig {
            id: 0, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius,
        });
    }
    log::info!("add_chunks: {} tiles inserted ({} chunks, area {})", chunk_cols * chunk_rows * area, chunk_cols * chunk_rows, area);
    Ok(())
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

// Place a building on an empty hex tile.
#[spacetimedb::reducer]
pub fn place_building(ctx: &ReducerContext, hex_q: i32, hex_r: i32, building_type: String) -> Result<(), String> {
    if building_type.is_empty() { return Err("building_type cannot be empty.".to_string()); }

    let tile = ctx.db.building_tile().hex_q().filter(&hex_q)
        .find(|t| t.hex_r == hex_r)
        .ok_or("Tile not found — has add_chunks been called?")?;

    if !tile.building_type.is_empty() {
        return Err("Tile already occupied.".to_string());
    }

    ctx.db.building_tile().tile_id().update(BuildingTile {
        building_type,
        owner_id: Some(ctx.sender()),
        ..tile
    });
    Ok(())
}

// Remove a building from a tile. Caller must be the owner or an admin.
#[spacetimedb::reducer]
pub fn remove_building(ctx: &ReducerContext, hex_q: i32, hex_r: i32) -> Result<(), String> {
    let tile = ctx.db.building_tile().hex_q().filter(&hex_q)
        .find(|t| t.hex_r == hex_r)
        .ok_or("Tile not found.")?;

    if tile.building_type.is_empty() { return Ok(()); }

    if tile.owner_id != Some(ctx.sender()) && !is_admin(ctx) {
        return Err("Not the owner.".to_string());
    }

    ctx.db.building_tile().tile_id().update(BuildingTile {
        building_type: String::new(),
        owner_id: None,
        ..tile
    });
    Ok(())
}

// ── Internal helpers ─────────────────────────────────────────────────────────

fn find_player_by_username(ctx: &ReducerContext, lookup: &str) -> Option<Identity> {
    let lower = lookup.to_lowercase();
    let hex = lower.trim_start_matches("0x");
    for p in ctx.db.player().iter() {
        let id_hex = p.player_id.to_hex().to_string().to_lowercase();
        if (!p.username.is_empty() && p.username.to_lowercase() == lower)
            || id_hex == hex
            || (!hex.is_empty() && id_hex.starts_with(hex))
        {
            return Some(p.player_id);
        }
    }
    None
}

fn compute_player_damage(ctx: &ReducerContext, player_id: Identity) -> u32 {
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
                + (stats.wisdom   * i.wis_damage_add) as f32
                + (stats.dexterity * i.dex_damage_add) as f32;
            dmg.max(1.0) as u32
        }
        None => 1,
    }
}

fn internal_gain_xp(ctx: &ReducerContext, player_id: Identity, amount: u32) {
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
    ctx.db.player_data().player_id().update(PlayerData {
        level: new_level, xp: new_xp, hp: new_hp, max_hp: new_max_hp,
        ..data
    });
    if new_level != old_level {
        recompute_stats(ctx, player_id);
    }
}
