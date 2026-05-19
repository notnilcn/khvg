use spacetimedb::{ReducerContext, Identity, Table, ScheduleAt, TimeDuration, Timestamp};
use std::time::Duration;
use super::tables::*;
use super::methods::*;
use super::seeds::*;

// ── Init ─────────────────────────────────────────────────────────────────────

#[spacetimedb::reducer(init)]
pub fn init(ctx: &ReducerContext) {
    ctx.db.enemy_behavior_schedule().insert(EnemyBehaviorSchedule {
        scheduled_id: 0,
        scheduled_at: ScheduleAt::Interval(TimeDuration::from(Duration::from_millis(500))),
    });
}

// ── Lifecycle ────────────────────────────────────────────────────────────────

#[spacetimedb::reducer(client_connected)]
pub fn client_connected(ctx: &ReducerContext) {
    let is_new = ctx.db.logged_in_player().player_id().find(ctx.sender()).is_none()
        && ctx.db.logged_out_player().player_id().find(ctx.sender()).is_none();
    if is_new {
        seed_new_player(ctx, ctx.sender());
        log::info!("New player: {:?}", ctx.sender());
    } else if let Some(player) = ctx.db.logged_out_player().player_id().find(ctx.sender()) {
        ctx.db.logged_out_player().player_id().delete(ctx.sender());
        ctx.db.logged_in_player().insert(player);
        log::info!("Returning player: {:?}", ctx.sender());
    }
}

#[spacetimedb::reducer(client_disconnected)]
pub fn client_disconnected(ctx: &ReducerContext) {
    if let Some(player) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        ctx.db.logged_in_player().player_id().delete(ctx.sender());
        ctx.db.logged_out_player().insert(player);
    }
    log::info!("Player disconnected: {:?}", ctx.sender());
}

// ── Player reducers ──────────────────────────────────────────────────────────

#[spacetimedb::reducer]
pub fn set_username(ctx: &ReducerContext, username: String) -> Result<(), String> {
    if username.trim().is_empty() || username.len() > 20 {
        return Err("Username must be 1-20 non-whitespace characters.".to_string());
    }
    let player = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .ok_or("Player not found.")?;
    ctx.db.logged_in_player().player_id().update(Player { username, ..player });
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_movement(ctx: &ReducerContext, x: f32, y: f32, rotation: f32) -> Result<(), String> {
    require_logged_in(ctx)?;
    let pos = ctx.db.player_position().player_id().find(ctx.sender())
        .ok_or_else(|| format!("Position not found for {:?}", ctx.sender()))?;
    let cfg = ctx.db.map_config().id().find(&0u32);
    let chunk_hex_radius = cfg.as_ref().map(|c| c.chunk_hex_radius).unwrap_or(DEFAULT_CHUNK_HEX_RADIUS);
    let hex_outer_radius = cfg.as_ref().map(|c| c.hex_outer_radius).unwrap_or(DEFAULT_HEX_OUTER_RADIUS);
    let chunk_cols       = cfg.as_ref().map(|c| c.chunk_cols).unwrap_or(0);
    let chunk_rows       = cfg.as_ref().map(|c| c.chunk_rows).unwrap_or(0);
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
    require_logged_in(ctx)?;
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
    require_logged_in(ctx)?;
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
    require_logged_in(ctx)?;
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
            x: enemy.x,
            y: enemy.y,
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
    require_logged_in(ctx)?;
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

// ── Enemy behavior schedule ───────────────────────────────────────────────────

#[spacetimedb::table(accessor = enemy_behavior_schedule, scheduled(tick_enemy_behavior))]
pub struct EnemyBehaviorSchedule {
    #[primary_key] #[auto_inc]
    pub scheduled_id: u64,
    pub scheduled_at: ScheduleAt,
}

#[spacetimedb::reducer]
pub fn tick_enemy_behavior(ctx: &ReducerContext, _job: EnemyBehaviorSchedule) {
    for enemy in ctx.db.enemy().iter() {
        if !has_player_in_range(ctx, enemy.x, enemy.y, ENEMY_SIMULATION_RANGE) {
            continue;
        }
    }
}

// Place a building on an empty hex tile.
#[spacetimedb::reducer]
pub fn place_building(ctx: &ReducerContext, hex_q: i32, hex_r: i32, building_type: String) -> Result<(), String> {
    require_logged_in(ctx)?;
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
    require_logged_in(ctx)?;
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
