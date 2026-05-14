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

    ctx.db.player_position().insert(PlayerPosition {
        player_id: ctx.sender(),
        x: 0.0,
        y: 0.0,
        rotation: 0.0,
        chunk_x: 0,
        chunk_y: 0,
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
    let chunk_x = (x / CHUNK_SIZE).floor() as i32;
    let chunk_y = (y / CHUNK_SIZE).floor() as i32;
    ctx.db.player_position().player_id().update(PlayerPosition {
        x, y, rotation, chunk_x, chunk_y,
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
            chunk_x: enemy.chunk_x,
            chunk_y: enemy.chunk_y,
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
    let chunk_x = (x / CHUNK_SIZE).floor() as i32;
    let chunk_y = (y / CHUNK_SIZE).floor() as i32;
    ctx.db.enemy().insert(Enemy {
        enemy_id: 0,
        template_id,
        res_path,
        spawn_x: x,
        spawn_y: y,
        spawn_time: ctx.timestamp,
        hp: template.max_hp,
        phase: 0,
        is_elite,
        immortal,
        chunk_x,
        chunk_y,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn despawn_enemy(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    if !is_admin(ctx) { return Err("Admin only.".to_string()); }
    ctx.db.enemy().enemy_id().delete(&enemy_id);
    Ok(())
}

// ── Internal helpers ─────────────────────────────────────────────────────────

fn find_player_by_username(ctx: &ReducerContext, username: &str) -> Option<Identity> {
    let lower = username.to_lowercase();
    for p in ctx.db.player().iter() {
        if p.username.to_lowercase() == lower {
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
