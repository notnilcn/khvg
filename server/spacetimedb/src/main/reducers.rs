use spacetimedb::{ReducerContext, Identity, Table};
use std::time::Duration;
use super::tables::*;
use super::methods::*;

#[spacetimedb::reducer(client_connected)]
pub fn client_connected(ctx: &ReducerContext) {
    if ctx.db.player_account().player_id().find(ctx.sender()).is_none() {
        ctx.db.player_account().insert(PlayerAccount {
            player_id: ctx.sender(),
            username: String::new(),
            is_admin: false,
            created_at: ctx.timestamp,
        });

        ctx.db.character_data().insert(CharacterData {
            player_id: ctx.sender(),
            level: 1,
            xp: 0,
            max_hp: BASE_MAX_HP as u32,
            current_hp: BASE_MAX_HP as u32,
            stats: starting_stats(),
        });

        ctx.db.player_inventory().insert(PlayerInventory {
            player_id: ctx.sender(),
            equipped_weapon: None,
            equipped_ability: None,
            equipped_armor: None,
            equipped_accessories: Vec::new(),
            backpack_slots: Vec::new(),
            consumable_slots: Vec::new(),
        });

        log::info!("New player: {:?}", ctx.sender());
    } 
    
    else {
        log::info!("Returning player: {:?}", ctx.sender());
    }

    ctx.db.player_position().insert(PlayerPosition {
        player_id: ctx.sender(),
        x: 0.0,
        y: 0.0,
        move_x: 0,
        move_y: 0,
        speed: 0,
        event_time: ctx.timestamp,
        chunk_x: 0,
        chunk_y: 0,
    });
}

#[spacetimedb::reducer(client_disconnected)]
pub fn client_disconnected(ctx: &ReducerContext) {
    ctx.db.player_position().player_id().delete(ctx.sender());
    ctx.db.elevated_client().identity().delete(ctx.sender());
    log::info!("Player disconnected: {:?}", ctx.sender());
}

#[spacetimedb::reducer]
pub fn claim_elevated(ctx: &ReducerContext) -> Result<(), String> {
    if let Some(existing) = ctx.db.elevated_client().identity().find(ctx.sender()) {
        ctx.db.elevated_client().identity().update(ElevatedClient { claimed_at: ctx.timestamp, ..existing });
        if let Some(acc) = ctx.db.player_account().player_id().find(ctx.sender()) {
            ctx.db.player_account().player_id().update(PlayerAccount { is_admin: true, ..acc });
        }
        log::info!("Elevated seat refreshed by {:?}", ctx.sender());
        return Ok(());
    }

    for _ in ctx.db.elevated_client().iter() {
        return Err("Elevated seat already claimed by a different identity.".to_string());
    }

    ctx.db.elevated_client().insert(ElevatedClient { identity: ctx.sender(), claimed_at: ctx.timestamp });
    if let Some(acc) = ctx.db.player_account().player_id().find(ctx.sender()) {
        ctx.db.player_account().player_id().update(PlayerAccount { is_admin: true, ..acc });
    }
    
    log::info!("Elevated seat claimed by {:?}", ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_movement(ctx: &ReducerContext, x: f32, y: f32, move_x: i8, move_y: i8, speed: u16) -> Result<(), String> {
    let pos = ctx.db.player_position().player_id().find(ctx.sender())
        .ok_or_else(|| format!("Position not found for {:?}", ctx.sender()))?;

    let chunk_x = (x / CHUNK_SIZE).floor() as i32;
    let chunk_y = (y / CHUNK_SIZE).floor() as i32;

    ctx.db.player_position().player_id().update(PlayerPosition {
        x, y, move_x, move_y, speed, event_time: ctx.timestamp, chunk_x, chunk_y,
        ..pos
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn set_username(ctx: &ReducerContext, username: String) -> Result<(), String> {
    if username.trim().is_empty() || username.len() > 20 {
        return Err("Username must be 1–20 non-whitespace characters.".to_string());
    }
    let account = ctx.db.player_account().player_id().find(ctx.sender())
        .ok_or("Account not found.")?;
    ctx.db.player_account().player_id().update(PlayerAccount { username, ..account });
    Ok(())
}

#[spacetimedb::reducer]
pub fn gain_xp(ctx: &ReducerContext, amount: u32) -> Result<(), String> {
    const MAX_XP_PER_GAIN: u32 = 500;
    if amount == 0 { return Ok(()); }
    if amount > MAX_XP_PER_GAIN {
        return Err(format!("XP gain {} exceeds the per-call maximum of {}.", amount, MAX_XP_PER_GAIN));
    }
    let data = ctx.db.character_data().player_id().find(ctx.sender())
        .ok_or("Character not found.")?;

    let old_level = data.level;
    let new_xp = data.xp + amount;
    let new_level = compute_level(new_xp);
    let new_max_hp = (BASE_MAX_HP + (new_level as i32 - 1) * HP_PER_LEVEL) as u32;
    let heal_amt = (new_level - old_level) * HP_PER_LEVEL as u32;
    let new_cur_hp = (data.current_hp + heal_amt).min(new_max_hp);

    ctx.db.character_data().player_id().update(CharacterData {
        level: new_level, xp: new_xp, max_hp: new_max_hp, current_hp: new_cur_hp,
        ..data
    });
    server_collect_stats(ctx, ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn change_stats(ctx: &ReducerContext, target_username: String, new_stats: RpgStats) -> Result<(), String> {
    let caller = ctx.db.player_account().player_id().find(ctx.sender())
        .ok_or("Account not found.")?;
    if !caller.is_admin {
        return Err("Only admins can change stats.".to_string());
    }

    let target_lower = target_username.to_lowercase();
    let mut target_id: Option<Identity> = None;
    for acc in ctx.db.player_account().iter() {
        let id_str = acc.player_id.to_hex().to_string().to_lowercase();
        if acc.username.to_lowercase() == target_lower || id_str == target_lower || id_str.starts_with(&target_lower) {
            target_id = Some(acc.player_id);
            break;
        }
    }
    let target_id = target_id.ok_or_else(|| format!("Player '{}' not found.", target_username))?;

    let data = ctx.db.character_data().player_id().find(target_id)
        .ok_or("Character data not found.")?;
    ctx.db.character_data().player_id().update(CharacterData { stats: new_stats, ..data });
    Ok(())
}

#[spacetimedb::reducer]
pub fn update_inventory(ctx: &ReducerContext, equipped_weapon: Option<String>, equipped_ability: Option<String>, equipped_armor: Option<String>, equipped_accessories: Vec<String>, backpack_slots: Vec<String>, consumable_slots: Vec<String>) -> Result<(), String> {
    let inv = ctx.db.player_inventory().player_id().find(ctx.sender())
        .ok_or("Inventory not found.")?;

    if equipped_accessories.len() > 3 { return Err("Too many accessories.".to_string()); }
    if backpack_slots.len() > 12 { return Err("Too many backpack slots.".to_string()); }
    if consumable_slots.len() > 8 { return Err("Too many consumable slots.".to_string()); }

    ctx.db.player_inventory().player_id().update(PlayerInventory {
        equipped_weapon, equipped_ability, equipped_armor,
        equipped_accessories, backpack_slots, consumable_slots,
        ..inv
    });
    server_collect_stats(ctx, ctx.sender());
    Ok(())
}

#[spacetimedb::reducer]
pub fn spawn_enemy(ctx: &ReducerContext, enemy_id: u64, x: f32, y: f32, res_path: String, is_elite: bool, immortal: bool, hp: u32, bullet_pattern_type: u8) -> Result<(), String> {
    if !can_control_enemies(ctx) {
        return Err("Unauthorized: only the elevated client may spawn enemies.".to_string());
    }
    if ctx.db.enemy_state().enemy_id().find(&enemy_id).is_some() {
        return Err(format!("Enemy {} already exists", enemy_id));
    }
    ctx.db.enemy_state().insert(EnemyState {
        enemy_id, x, y, move_x: 0, move_y: 0, speed: 0,
        event_time: ctx.timestamp, res_path, is_elite, immortal, hp,
        target_id: None, bullet_pattern_type,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn update_enemy_snapshots(ctx: &ReducerContext, snapshots: Vec<EnemySnapshot>) -> Result<(), String> {
    if !can_control_enemies(ctx) {
        return Err("Unauthorized: only the elevated client may update enemy snapshots.".to_string());
    }
    for snap in snapshots {
        let enemy = match ctx.db.enemy_state().enemy_id().find(&snap.enemy_id) {
            Some(e) => e,
            None => continue,
        };
        ctx.db.enemy_state().enemy_id().update(EnemyState {
            x: snap.x, y: snap.y, move_x: snap.move_x, move_y: snap.move_y,
            speed: snap.speed, event_time: ctx.timestamp,
            ..enemy
        });
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn update_enemy_target(ctx: &ReducerContext, enemy_id: u64, target_id: String) -> Result<(), String> {
    if !can_control_enemies(ctx) {
        return Err("Unauthorized: only the elevated client may update enemy targets.".to_string());
    }
    let enemy = match ctx.db.enemy_state().enemy_id().find(&enemy_id) {
        Some(e) => e,
        None => return Ok(()),
    };
    ctx.db.enemy_state().enemy_id().update(EnemyState {
        target_id: Some(target_id), event_time: ctx.timestamp,
        ..enemy
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn despawn_enemy(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    if !can_control_enemies(ctx) {
        return Err("Unauthorized: only the elevated client may despawn enemies.".to_string());
    }
    ctx.db.enemy_state().enemy_id().delete(&enemy_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn upsert_item_stats(ctx: &ReducerContext, stats: ItemStats) -> Result<(), String> {
    let caller = ctx.db.player_account().player_id().find(ctx.sender())
        .ok_or("Account not found.")?;
    if !caller.is_admin {
        return Err("Only admins can modify item stats.".to_string());
    }
    if ctx.db.item_stats().item_id().find(&stats.item_id).is_some() {
        ctx.db.item_stats().item_id().update(stats);
    } else {
        ctx.db.item_stats().insert(stats);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn give_weapon(ctx: &ReducerContext, target_username: String, item_res_path: String) -> Result<(), String> {
    let caller = ctx.db.player_account().player_id().find(ctx.sender())
        .ok_or("Account not found.")?;
    if !caller.is_admin {
        return Err("Only admins can give weapons.".to_string());
    }

    let mut found_inv: Option<PlayerInventory> = None;
    for account in ctx.db.player_account().iter() {
        if account.username.to_lowercase() == target_username.to_lowercase() {
            found_inv = ctx.db.player_inventory().player_id().find(account.player_id);
            break;
        }
    }
    let inv = found_inv.ok_or_else(|| format!("Player '{}' not found.", target_username))?;

    let mut backpack = inv.backpack_slots.clone();
    backpack.push(item_res_path);
    ctx.db.player_inventory().player_id().update(PlayerInventory { backpack_slots: backpack, ..inv });
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_weapon(ctx: &ReducerContext, target_username: String, item_res_path: String) -> Result<(), String> {
    let caller = ctx.db.player_account().player_id().find(ctx.sender())
        .ok_or("Account not found.")?;
    if !caller.is_admin {
        return Err("Only admins can remove weapons.".to_string());
    }

    let mut found_inv: Option<PlayerInventory> = None;
    for account in ctx.db.player_account().iter() {
        if account.username.to_lowercase() == target_username.to_lowercase() {
            found_inv = ctx.db.player_inventory().player_id().find(account.player_id);
            break;
        }
    }
    let inv = found_inv.ok_or_else(|| format!("Player '{}' not found.", target_username))?;

    let mut accessories = inv.equipped_accessories.clone();
    accessories.retain(|s| s != &item_res_path);
    let mut backpack = inv.backpack_slots.clone();
    backpack.retain(|s| s != &item_res_path);
    let mut consumables = inv.consumable_slots.clone();
    consumables.retain(|s| s != &item_res_path);

    ctx.db.player_inventory().player_id().update(PlayerInventory {
        equipped_weapon: if inv.equipped_weapon.as_deref() == Some(&item_res_path) { None } else { inv.equipped_weapon.clone() },
        equipped_ability: if inv.equipped_ability.as_deref() == Some(&item_res_path) { None } else { inv.equipped_ability.clone() },
        equipped_armor: if inv.equipped_armor.as_deref() == Some(&item_res_path) { None } else { inv.equipped_armor.clone() },
        equipped_accessories: accessories,
        backpack_slots: backpack,
        consumable_slots: consumables,
        ..inv
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn fire_bullet(ctx: &ReducerContext, slot_id: u16, pattern_type: u8, origin_x: f32, origin_y: f32, target_x: f32, target_y: f32, lifetime_secs: f32) -> Result<(), String> {
    if !can_control_enemies(ctx) {
        return Err("Unauthorized: only the elevated client may fire bullets.".to_string());
    }
    let row = BulletPattern {
        slot_id, pattern_type, origin_x, origin_y, target_x, target_y,
        spawn_time: ctx.timestamp,
        despawn_time: ctx.timestamp + Duration::from_secs_f32(lifetime_secs),
    };
    if ctx.db.bullet_pattern().slot_id().find(&slot_id).is_some() {
        ctx.db.bullet_pattern().slot_id().update(row);
    } else {
        ctx.db.bullet_pattern().insert(row);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn despawn_bullet(ctx: &ReducerContext, slot_id: u16) -> Result<(), String> {
    if !can_control_enemies(ctx) {
        return Err("Unauthorized: only the elevated client may despawn bullets.".to_string());
    }
    ctx.db.bullet_pattern().slot_id().delete(&slot_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn damage_player(ctx: &ReducerContext, player_id: Identity, damage: i32) -> Result<(), String> {
    if !can_control_enemies(ctx) {
        return Err("Unauthorized: only the elevated client may deal damage.".to_string());
    }
    let data = match ctx.db.character_data().player_id().find(player_id) {
        Some(d) => d,
        None => return Ok(()),
    };
    let new_hp = (data.current_hp as i32 - damage).max(0) as u32;
    ctx.db.character_data().player_id().update(CharacterData { current_hp: new_hp, ..data });
    Ok(())
}
