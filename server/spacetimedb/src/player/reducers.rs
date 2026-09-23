use std::time::Duration;
use spacetimedb::{ReducerContext, Table};
use crate::main::global::*;
use crate::world::hex::{spiral_chunk_index, world_to_chunk, world_to_hex};
use crate::world::reducers::hex_elevation;
use crate::world::wrap::{wrap_world_pos, wrapped_distance_sq};
use crate::world::tables::MapConfig;
use crate::player::tables::*;
use crate::player::methods::*;
use crate::combat::deal_damage_to_player;
use crate::item::tables::*;
use crate::item::methods::{ability_behavior, ability_is_charge_capable, consumable_behavior, equipped_enchantment_behaviors, weapon_toggle_options};
use crate::enemy::instance_tables::*;
use crate::enemy::methods::enemies_near_point;
use crate::status::tables::*;
use crate::status::methods::{ally_targets, apply_status_effect};
use crate::anticheat::tables::player_hit_flag;

#[spacetimedb::reducer]
pub fn set_username(ctx: &ReducerContext, username: String) -> Result<(), String> {
    if username.trim().is_empty() || username.len() > 20 {
        return Err("Username must be 1-20 non-whitespace characters.".to_string());
    }
    if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        ctx.db.logged_in_player().player_id().update(LoggedInPlayer { username, ..p });
    } else if let Some(p) = ctx.db.logged_out_player().player_id().find(ctx.sender()) {
        ctx.db.logged_out_player().player_id().update(LoggedOutPlayer { username, ..p });
    } else {
        return Err("Player not found.".to_string());
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn create_profile(ctx: &ReducerContext, profile_name: String, texture_id: String) -> Result<(), String> {
    let _ = require_in_lobby(ctx)?;
    if profile_name.trim().is_empty() || profile_name.len() > 20 {
        return Err("Profile name must be 1-20 non-whitespace characters.".to_string());
    }
    let existing: Vec<_> = ctx.db.player_profile().player_id().filter(&ctx.sender()).collect();
    if existing.len() >= 3 {
        return Err("Maximum 3 profiles allowed.".to_string());
    }
    let lower = profile_name.to_lowercase();
    if existing.iter().any(|p| p.profile_name.to_lowercase() == lower) {
        return Err(format!("Profile '{}' already exists.", profile_name));
    }
    ctx.db.player_profile().insert(PlayerProfile {
        profile_id: 0, player_id: ctx.sender(), profile_name, texture_id, aim_assist: false, lock_on: false,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn delete_profile(ctx: &ReducerContext, profile_id: u64) -> Result<(), String> {
    let _ = require_in_lobby(ctx)?;
    let profile = ctx.db.player_profile().profile_id().find(&profile_id)
        .ok_or("Profile not found.")?;
    if profile.player_id != ctx.sender() {
        return Err("Not your profile.".to_string());
    }
    teardown_profile(ctx, profile_id);
    // Explored-map snapshots are deliberately NOT part of teardown_profile — death must not
    // erase the map — so a deleted profile's rows are removed explicitly here.
    for explored in ctx.db.player_explored_chunk().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        ctx.db.player_explored_chunk().explored_id().delete(&explored.explored_id);
    }
    ctx.db.player_profile().profile_id().delete(&profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn join_world(ctx: &ReducerContext, profile_id: u64) -> Result<(), String> {
    let lobby = require_in_lobby(ctx)?;
    let profile = ctx.db.player_profile().profile_id().find(&profile_id).ok_or("Profile not found.")?;
    if profile.player_id != ctx.sender() { return Err("Not your profile.".to_string()); }

    ctx.db.logged_out_player().player_id().delete(ctx.sender());
    ctx.db.logged_in_player().insert(LoggedInPlayer {player_id: lobby.player_id, username: lobby.username, is_admin: lobby.is_admin, profile_id});
    try_scaffold_profile(ctx, profile_id);

    log::info!("Player {:?} joined world with profile {}", ctx.sender(), profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn leave_world(ctx: &ReducerContext) -> Result<(), String> {
    let player = ctx.db.logged_in_player().player_id().find(ctx.sender()).ok_or("Not in world.")?;
    // Runtime rows (statuses, charges, zones) never outlive the world session — no status
    // may kill or tick a logged-out player, and a caster's zones die with them.
    crate::status::methods::purge_profile_statuses(ctx, player.profile_id);
    crate::status::methods::purge_profile_zones(ctx, player.profile_id);
    ctx.db.logged_in_player().player_id().delete(ctx.sender());
    ctx.db.logged_out_player().insert(LoggedOutPlayer { player_id: player.player_id, username: player.username, is_admin: player.is_admin});
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_movement(ctx: &ReducerContext, x: f32, y: f32, movement_direction: f32, movement_speed: f32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let pos = ctx.db.player_position().profile_id().find(&profile_id)
        .ok_or_else(|| format!("Position not found for profile {}", profile_id))?;
    let cfg = MapConfig::load(ctx, 0, 0);
    let (lap_q, lap_r) = ((cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));
    let (x, y) = wrap_world_pos(x, y, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);

    // Elevation rules (per-hex levels on BuildingTile, stamped by world generation): a move
    // within ±MAX_WALKABLE_*_DELTA levels is normal walking; rising more than
    // MAX_WALKABLE_UP_DELTA is blocked; dropping more than MAX_WALKABLE_DOWN_DELTA is a fall
    // with a movement lockout proportional to the drop, plus damage once the drop exceeds
    // FALL_SAFE_DROP. A live Grapple displacement allowance exempts the report entirely
    // (grappling across cliffs is a feature); teleport_player never goes through
    // report_movement at all.
    let state = ctx.db.player_movement_state().profile_id().find(&profile_id);
    let (nx, ny) = nearest_lap_copy(x, y, pos.x, pos.y, lap_q, lap_r);
    let (dx, dy) = (nx - pos.x, ny - pos.y);
    let dist = (dx * dx + dy * dy).sqrt();
    let grapple_active = matches!(
        ctx.db.player_displacement_allowance().profile_id().find(&profile_id),
        Some(a) if ctx.timestamp < a.expires_at && dist <= a.max_distance
    );
    let mut fall_delta: i16 = 0;
    if !grapple_active {
        if let Some(s) = &state {
            if let Some(until) = s.falling_until {
                if ctx.timestamp < until {
                    // Mid-fall: the client is tweening to the landing point and movement is
                    // locked, so position claims are ignored until the fall ends. Restamp
                    // the report clock so the first post-fall report's displacement budget
                    // doesn't absorb the fall duration.
                    ctx.db.player_movement_state().profile_id().update(PlayerMovementState { profile_id, last_report: ctx.timestamp, falling_until: s.falling_until });
                    return Ok(());
                }
            }
        }
        let (q0, r0) = world_to_hex(pos.x, pos.y, cfg.hex_outer_radius);
        let (q1, r1) = world_to_hex(x, y, cfg.hex_outer_radius);
        let e0 = hex_elevation(ctx, q0, r0);
        let e1 = hex_elevation(ctx, q1, r1);
        let rising = e1 - e0;
        if rising > MAX_WALKABLE_UP_DELTA {
            // Too steep to climb: reject the position claim. Honest clients predict the
            // block and never send this — this is the anti-cheat belt. The report clock
            // still restamps so the budget doesn't balloon while pushing into the cliff.
            match &state {
                Some(s) => { ctx.db.player_movement_state().profile_id().update(PlayerMovementState { profile_id, last_report: ctx.timestamp, falling_until: s.falling_until }); }
                None => { ctx.db.player_movement_state().insert(PlayerMovementState { profile_id, last_report: ctx.timestamp, falling_until: None }); }
            }
            return Ok(());
        }
        let drop = e0 - e1;
        if drop > MAX_WALKABLE_DOWN_DELTA {
            fall_delta = drop;
        }
    }

    // Clamp reported speed into [0, resolved base_speed] (gear/buffs included) — clients
    // should never exceed base_speed, so anything over is a cheat or a bug. While any
    // SelfSlow status row is held (P4 ability charge), the cap tightens to
    // base_speed × the minimum slow_factor — the client-side slow is cosmetic without this.
    let base_speed = ctx.db.player_data().profile_id().find(&profile_id)
        .map(|d| d.base_speed)
        .unwrap_or(BASE_SPEED);
    let slow_factor = ctx.db.active_status_effect().profile_id().filter(&profile_id)
        .filter(|r| r.kind == StatusEffectKind::SelfSlow)
        .map(|r| r.params.slow_factor)
        .fold(f32::MAX, f32::min);
    let speed_cap = if slow_factor < f32::MAX { base_speed * slow_factor } else { base_speed };
    let movement_speed = movement_speed.clamp(0.0, speed_cap);

    // Displacement clamp: a report may move at most speed_cap × dt_since_last_report ×
    // MOVE_SLACK from the stored position row (floor MIN_REPORT_DISPLACEMENT, which is
    // also the whole budget for the first report). Over-budget reports are CLAMPED along
    // their own wrap-aware direction, not rejected — rejecting would rubber-band legit
    // lag. teleport_player is exempt (it writes the row directly and restamps the clock).
    let budget = state.as_ref()
        .map(|s| {
            // Clamp the gap so idle time can't bank a cross-map budget (past-tolerance
            // window); negative gaps (clock anomaly) floor to zero → MIN displacement.
            let dt = crate::main::time::seconds_since(ctx, s.last_report)
                .clamp(0.0, MOVE_TIMESTAMP_PAST_TOLERANCE_SECS);
            speed_cap * dt * MOVE_SLACK
        })
        .unwrap_or(0.0)
        .max(MIN_REPORT_DISPLACEMENT);
    // Move-validation strikes (Phase 1 — BitCraft move_validation_helpers pattern):
    // timestamp monotonicity (server clock behind the last report = anomaly) and gross
    // origin-speed violations (>2× budget, beyond lag slack) record a strike. Strikes
    // ALLOW (clamp along the vector, current behavior) until STRIKES_BEFORE_REJECT
    // within STRIKE_WINDOW_SECS, then reject + schedule a resync (teleport restamp =
    // BitCraft's reset_mobile_entity_timer). Admins bypass (mirror has_role_no_dev GM).
    if !is_admin(ctx) {
        let monotonic_ok = state.as_ref().map(|s| ctx.timestamp >= s.last_report).unwrap_or(true);
        let gross_violation = dist > budget * 2.0 && fall_delta == 0 && !grapple_active;
        if !monotonic_ok || gross_violation {
            let strikes = crate::anticheat::record_move_strike(ctx, profile_id);
            if strikes >= STRIKES_BEFORE_REJECT {
                // Reject + resync: restamp the clock and snap the client back by
                // re-asserting the stored position (authoritative echo on next tick).
                match &state {
                    Some(s) => { ctx.db.player_movement_state().profile_id().update(PlayerMovementState { profile_id, last_report: ctx.timestamp, falling_until: s.falling_until }); }
                    None => { ctx.db.player_movement_state().insert(PlayerMovementState { profile_id, last_report: ctx.timestamp, falling_until: None }); }
                }
                log::warn!("move validation: rejecting report for profile {} after {} strikes", profile_id, strikes);
                return Ok(());
            }
        }
    }
    let (x, y) = if dist > budget {
        if fall_delta > 0 && dist <= MAX_FALL_DISPLACEMENT {
            // Downhill fall: exempt from the displacement budget up to
            // MAX_FALL_DISPLACEMENT — the client tweens the landing like a grapple.
            // Beyond that bound the report falls through to the normal clamp.
            (x, y)
        } else {
        // Grapple (P4) is client-tweened: a cast stamps a one-shot displacement
        // allowance (see apply_ability_effect), and one oversized report up to the
        // grapple range consumes it instead of being clamped. Expired rows are swept
        // on contact.
        match ctx.db.player_displacement_allowance().profile_id().find(&profile_id) {
            Some(a) if ctx.timestamp < a.expires_at && dist <= a.max_distance => {
                ctx.db.player_displacement_allowance().profile_id().delete(&profile_id);
                (x, y)
            }
            Some(_) => {
                ctx.db.player_displacement_allowance().profile_id().delete(&profile_id);
                (pos.x + dx / dist * budget, pos.y + dy / dist * budget)
            }
            None => (pos.x + dx / dist * budget, pos.y + dy / dist * budget),
        }
        }
    } else {
        (x, y)
    };
    let (x, y) = wrap_world_pos(x, y, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, cfg.chunk_hex_radius, cfg.hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);

    // Delta-checked write: skip the row update when nothing a subscriber sees changed —
    // position, reported velocity, and chunk all ride on this one row.
    if x != pos.x || y != pos.y
        || movement_direction != pos.movement_direction
        || movement_speed != pos.movement_speed
        || chunk_index != pos.chunk_index
    {
        ctx.db.player_position().profile_id().update(PlayerPosition { x, y, movement_direction, movement_speed, chunk_index, ..pos });
    }

    // Restamp the report clock — the next report's displacement budget measures from here.
    // A fall started by this report also stamps its movement lockout (falling_until).
    let fall_until = if fall_delta > 0 {
        Some(ctx.timestamp + Duration::from_secs_f64(FALL_SECONDS_PER_LEVEL * fall_delta as f64))
    } else {
        None
    };
    match state {
        Some(s) => {
            let falling_until = fall_until.or(s.falling_until);
            ctx.db.player_movement_state().profile_id().update(PlayerMovementState { last_report: ctx.timestamp, falling_until, ..s });
        }
        None => { ctx.db.player_movement_state().insert(PlayerMovementState { profile_id, last_report: ctx.timestamp, falling_until: fall_until }); }
    }

    // Radius-exit stamping for active radius wagers (stamped from movement reports, NOT
    // sampled at 1 Hz — darting out and back between ticks must not cheat "never leave r2").
    for row in ctx.db.active_status_effect().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        if row.kind != StatusEffectKind::StoreDamage || row.params.wager != WagerKind::Radius { continue; }
        let Some(marked) = ctx.db.enemy().enemy_id().find(&row.marked_enemy_id) else { continue };
        let dist_sq = wrapped_distance_sq(x, y, marked.x, marked.y, lap_q, lap_r);
        if dist_sq > row.params.r2 * row.params.r2 {
            ctx.db.active_status_effect().effect_id().update(ActiveStatusEffect { violated: true, last_event_at: ctx.timestamp, ..row });
        } else if dist_sq > row.params.r1 * row.params.r1 && row.progress < 1.0 {
            // `progress` doubles as the r1-exit flag for the radius wager.
            ctx.db.active_status_effect().effect_id().update(ActiveStatusEffect { progress: 1.0, last_event_at: ctx.timestamp, ..row });
        }
    }

    if let Some(chunk) = ctx.db.player_chunk().profile_id().find(&profile_id) {
        if (chunk.chunk_q, chunk.chunk_r) != (cq, cr) {
            ctx.db.player_chunk().profile_id().update(PlayerChunk { chunk_q: cq, chunk_r: cr, ..chunk });
            // A real chunk crossing reveals the destination AOI disk (PLAN §4.2). No-op
            // (one ≤37-row set-diff read) when every chunk in the disk is already revealed.
            crate::player::explored::reveal_aoi(ctx, profile_id, cq, cr);
        }
    }

    // Fall damage, proportional to the drop — only past FALL_SAFE_DROP; smaller falls still
    // got the lockout and displacement exemption above but land unharmed. Applied last
    // because it can kill: teardown_profile then deletes the rows written above and nothing
    // follows it.
    if fall_delta > FALL_SAFE_DROP {
        deal_damage_to_player(ctx, &player, None, FALL_DAMAGE_PER_LEVEL * fall_delta as u32)?;
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_screen_rotation(ctx: &ReducerContext, screen_rotation: f32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let rot = ctx.db.player_rotation().profile_id().find(&profile_id)
        .ok_or_else(|| format!("Rotation not found for profile {}", profile_id))?;
    // Keep chunk_index mirroring the position row so the AOI view filters correctly.
    let chunk_index = ctx.db.player_position().profile_id().find(&profile_id)
        .map(|p| p.chunk_index)
        .unwrap_or(rot.chunk_index);
    // Delta-checked write: sub-epsilon rotation changes (~0.5°) don't rewrite the row, but a
    // chunk crossing always does so the mirror above stays exact.
    if (screen_rotation - rot.screen_rotation).abs() >= ROTATION_REPORT_EPSILON || chunk_index != rot.chunk_index {
        ctx.db.player_rotation().profile_id().update(PlayerRotation { screen_rotation, chunk_index, ..rot });
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_hit(ctx: &ReducerContext, event_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;

    // Rate limit (PlayerHitRate token bucket, 1s window): self-reported damage intake is
    // capped at INCOMING_HIT_RATE_CAP/s — generous for real bullet-hell play, but bounds
    // the StoreIncoming farming loop's input rate.
    if !hit_rate_allow(ctx, player.profile_id, false, INCOMING_HIT_RATE_CAP) {
        return Err("Hit report rate exceeded.".to_string());
    }

    // Hit attestation: the victim's client sends back the event_id of the
    // BulletPatternEvent that spawned the bullet, and the server only honors claims
    // against a live attestation row stamped by the behavior tick. A client-chosen
    // (step, enemy) pair is no longer accepted — the step must have actually fired,
    // from that enemy, within the TTL window. Each accepted claim decrements the
    // event's pellet pool, so one volley cannot be replayed past its bullets; player-
    // ability fan/split pellets draw from the same pool as their source event.
    let attestation = ctx.db.bullet_fire_attestation().event_id().find(&event_id).ok_or("Unknown bullet event.")?;
    if crate::main::time::seconds_since(ctx, attestation.fired_at) >= BULLET_ATTESTATION_TTL_SECONDS as f32 {
        ctx.db.bullet_fire_attestation().event_id().delete(&event_id);
        return Err("Bullet event expired.".to_string());
    }
    if attestation.remaining == 0 {
        ctx.db.bullet_fire_attestation().event_id().delete(&event_id);
        return Err("Bullet event already claimed.".to_string());
    }

    // The attested enemy must still exist and be plausibly near the player — this
    // rejects nonsense attributions (stale event ids, far-away enemies).
    let enemy_id = attestation.enemy_id;
    let enemy = ctx.db.enemy().enemy_id().find(&enemy_id).ok_or("Attack source enemy not found.")?;
    let pos = ctx.db.player_position().profile_id().find(&player.profile_id).ok_or("Position not found.")?;
    let cfg = MapConfig::load(ctx, 0, 0);
    let dist_sq = wrapped_distance_sq(pos.x, pos.y, enemy.x, enemy.y, (cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));
    if dist_sq > HIT_VALIDATION_RADIUS * HIT_VALIDATION_RADIUS {
        return Err("Attack source enemy is not near the player.".to_string());
    }

    // Damage is the tick-stamped value on the attestation — never client-supplied.
    let base_damage = attestation.damage;
    if attestation.remaining == 1 {
        ctx.db.bullet_fire_attestation().event_id().delete(&event_id);
    } else {
        ctx.db.bullet_fire_attestation().event_id().update(BulletFireAttestation { remaining: attestation.remaining - 1, ..attestation });
    }

    deal_damage_to_player(ctx, &player, Some(enemy_id), base_damage)?;
    Ok(())
}

#[spacetimedb::reducer]
pub fn pickup_drop(ctx: &ReducerContext, drop_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let drop = ctx.db.loot_drop().drop_id().find(&drop_id).ok_or("Drop not found.")?;
    // Enforce expires_at at pickup; the 1 Hz status tick also sweeps expired rows.
    if ctx.timestamp >= drop.expires_at {
        ctx.db.loot_drop().drop_id().delete(&drop_id);
        return Err("Drop has expired.".to_string());
    }
    if drop.item_id.is_empty() { return Ok(()); }
    // Shared interaction gate (Phase 0C validate_interact): wrap-aware range check +
    // territory inventory-permission consult — no picking up drops across the map.
    validate_interact(ctx, profile_id, drop.x, drop.y).map_err(|e| {
        if e == "Target is too far away." { "Drop is too far away.".to_string() } else { e }
    })?;
    let item = ctx.db.item().item_id().find(&drop.item_id).ok_or("Item not found.")?;

    // Runtime state travels with the item: a dropped ability's residual cooldown/charges
    // restore onto the destination slot (a different player picking it up inherits them).
    transfer_item_to_inventory(ctx, profile_id, &item, drop.enchantment_ids.clone(), drop.cooldown_until, drop.charges)?;

    // Discovery (Phase 1 — BitCraft discover_item_stack): picking up auto-discovers
    // the item and its enchantments.
    let mut ids = vec![item.item_id.clone()];
    ids.extend(drop.enchantment_ids.clone());
    discover_ids(ctx, profile_id, ids);

    ctx.db.loot_drop().drop_id().delete(&drop_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn drop_item(ctx: &ReducerContext, slot_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let mut slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    // Dropping a follower cell drops the whole span.
    if let Some(head) = slot.occupied_by {
        slot = get_slot(ctx, profile_id, head).ok_or("Span head not found.")?;
    }
    // Storage-category gate (Phase 0C): empty/blank ids are never storable.
    if !is_storable(&slot.item_id) {
        return Err("Slot is empty.".to_string());
    }
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let enchantment_ids = slot.enchantment_ids.clone();
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
    // Runtime state (cooldown/charges) travels with the item onto the LootDrop row;
    // pickup_drop restores it. Ordinary slots carry the (None, u32::MAX) defaults.
    let (cooldown_until, charges) = (slot.cooldown_until, slot.charges);

    if slot.role == SlotRole::Ability {
        clear_span(ctx, profile_id, slot.slot_index, item.slot_cost);
    } else {
        update_slot(ctx, PlayerInventorySlot { item_id: None, enchantment_ids: vec![], ..slot });
    }

    ctx.db.loot_drop().insert(LootDrop {
        drop_id: 0, item_id, x: pos.x, y: pos.y, chunk_index: pos.chunk_index,
        expires_at: ctx.timestamp + Duration::from_secs(LOOT_DROP_EXPIRY),
        dropped_by: Some(ctx.sender()),
        enchantment_ids,
        cooldown_until,
        charges,
    });
    recompute_stats(ctx, profile_id);
    Ok(())
}

/// Equipped roles — the ones recompute_stats reads stats from. Stat requirements gate
/// an item *entering* one of these from loose (General) storage.
fn is_equip_role(role: SlotRole) -> bool {
    matches!(role, SlotRole::Weapon | SlotRole::Accessory | SlotRole::Armor | SlotRole::Ability)
}

#[spacetimedb::reducer]
pub fn swap_slots(ctx: &ReducerContext, from_index: u32, to_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;
    if from_index == to_index { return Ok(()); }

    // Follower cells stand in for their span's head on the source side. On the target side,
    // only resolve followers of a *different* span — targeting your own span's follower is a
    // nudge (the head should move there), not a no-op.
    let resolve_head = |index: u32| -> Result<PlayerInventorySlot, String> {
        let slot = get_slot(ctx, profile_id, index).ok_or("Invalid slot index.")?;
        match slot.occupied_by {
            Some(head) => get_slot(ctx, profile_id, head).ok_or_else(|| "Span head not found.".to_string()),
            None => Ok(slot),
        }
    };
    let from = resolve_head(from_index)?;
    let to_raw = get_slot(ctx, profile_id, to_index).ok_or("Invalid slot index.")?;
    let to = match to_raw.occupied_by {
        Some(head) if head != from.slot_index => get_slot(ctx, profile_id, head).ok_or_else(|| "Span head not found.".to_string())?,
        _ => to_raw,
    };
    if from.slot_index == to.slot_index { return Ok(()); }

    let find_item = |slot: &PlayerInventorySlot| -> Result<Option<Item>, String> {
        slot.item_id.as_ref()
            .map(|id| ctx.db.item().item_id().find(id).ok_or_else(|| "Item not found.".to_string()))
            .transpose()
    };
    let from_item = find_item(&from)?;
    let to_item = find_item(&to)?;

    if let Some(item) = &from_item {
        if !slot_allowed(to.role, item.equip_slot) {
            return Err(format!("Item '{}' is not allowed in slot {}.", item.item_id, to.slot_index));
        }
    }
    if let Some(item) = &to_item {
        if !slot_allowed(from.role, item.equip_slot) {
            return Err(format!("Item '{}' is not allowed in slot {}.", item.item_id, from.slot_index));
        }
    }
    // Stat-requirement gates (doc 03 class thresholds): an item entering an equipped role
    // from loose storage must have its requirements met. Covers both the plain-swap and
    // the ability-region paths below.
    if let Some(item) = &from_item {
        if is_equip_role(to.role) && !is_equip_role(from.role) {
            check_stat_requirements(ctx, profile_id, item)?;
        }
    }
    if let Some(item) = &to_item {
        if is_equip_role(from.role) && !is_equip_role(to.role) {
            check_stat_requirements(ctx, profile_id, item)?;
        }
    }

    let from_in_region = from.role == SlotRole::Ability;
    let to_in_region = to.role == SlotRole::Ability;

    if from_in_region || to_in_region {
        // A span can't 1:1 swap — the receiving side must be empty. The acceptance checks
        // above have already forced the moving item to be an Ability item.
        if from_item.is_some() && to_item.is_some() {
            return Err("Ability items occupy multiple slots and cannot be swapped onto an occupied slot.".to_string());
        }
        let moving = from_item.as_ref().map(|i| (i, &from, &to))
            .or_else(|| to_item.as_ref().map(|i| (i, &to, &from)));
        if let Some((item, src, dst)) = moving {
            let cost = item.slot_cost.max(1);
            // Carried runtime state (cooldown/charges/toggle) travels with the item in
            // BOTH directions. Entering the region from loose backpack storage
            // initializes charges from the item ONLY when the source carried no state
            // (a fresh backpack cell: no cooldown, unlimited charges) — a cell with
            // residual state (unequipped mid-cooldown, or a picked-up drop) restores it.
            let (item_id, enchantment_ids, cooldown_until, charges, active_toggle) =
                (src.item_id.clone(), src.enchantment_ids.clone(), src.cooldown_until, src.charges, src.active_toggle);
            let (cooldown_until, charges) = if dst.role == SlotRole::Ability && src.role != SlotRole::Ability
                && cooldown_until.is_none() && charges == u32::MAX
            {
                (None, ability_behavior(item)
                    .map(|a| if a.max_charges == 0 { u32::MAX } else { a.max_charges })
                    .unwrap_or(u32::MAX))
            } else {
                (cooldown_until, charges)
            };
            if dst.role == SlotRole::Ability {
                // The run starting exactly at the target must be free once the source is vacated.
                let src_span = if src.role == SlotRole::Ability { cost } else { 1 };
                let fits = (dst.slot_index..dst.slot_index + cost).all(|i| {
                    if i >= src.slot_index && i < src.slot_index + src_span { return true; } // freed by the move
                    get_slot(ctx, profile_id, i)
                        .map(|s| s.role == SlotRole::Ability && s.item_id.is_none() && s.occupied_by.is_none())
                        .unwrap_or(false)
                });
                if !fits {
                    return Err(format!("Item '{}' needs {} contiguous free ability slots at index {}.", item.item_id, cost, dst.slot_index));
                }
            }
            if src.role == SlotRole::Ability {
                clear_span(ctx, profile_id, src.slot_index, cost);
            } else {
                update_slot(ctx, PlayerInventorySlot { item_id: None, enchantment_ids: vec![], ..src.clone() });
            }
            if let Some(head) = get_slot(ctx, profile_id, dst.slot_index) {
                update_slot(ctx, PlayerInventorySlot {
                    item_id, enchantment_ids, cooldown_until, charges, active_toggle, occupied_by: None, ..head
                });
            }
            if dst.role == SlotRole::Ability {
                for i in dst.slot_index + 1..dst.slot_index + cost {
                    if let Some(follower) = get_slot(ctx, profile_id, i) {
                        update_slot(ctx, PlayerInventorySlot {
                            item_id: None, enchantment_ids: vec![], occupied_by: Some(dst.slot_index),
                            cooldown_until: None, charges: u32::MAX, active_toggle: 0, ..follower
                        });
                    }
                }
            }
        }
    } else {
        // Neither side is in the ability region — plain single-cell swap, even for ability
        // items stored loose in the backpack (slot_cost only applies in the ability region).
        // Runtime state (cooldown/charges/toggle) travels with the ITEM here too — leaving
        // it on the slot would let a backpack-cell shuffle launder a cooldown before
        // re-entering the ability region.
        update_slot(ctx, PlayerInventorySlot {
            item_id: to.item_id.clone(), enchantment_ids: to.enchantment_ids.clone(),
            cooldown_until: to.cooldown_until, charges: to.charges, active_toggle: to.active_toggle,
            ..from.clone()
        });
        update_slot(ctx, PlayerInventorySlot {
            item_id: from.item_id.clone(), enchantment_ids: from.enchantment_ids.clone(),
            cooldown_until: from.cooldown_until, charges: from.charges, active_toggle: from.active_toggle,
            ..to.clone()
        });
    }
    recompute_stats(ctx, profile_id);
    Ok(())
}

/// The single 1 Hz status tick (renamed from tick_active_consumable_effects):
/// per-concern dispatcher (Phase 0A agent framework): regen pass → zone pass → core
/// status tick (consumable expiry + bleed/charge/enemy/zone) → cleanup pass (trade
/// timeouts, chat retention, upkeep, strike sweeps). Every pass early-returns on the
/// agent master switch except the core consumable-expiry scan below.
#[spacetimedb::reducer]
pub fn tick_status_effects(ctx: &ReducerContext, _job: ConsumableEffectSchedule) {
    let mut affected_profiles = Vec::new();
    for active in ctx.db.active_consumable_effect().iter() {
        let remaining = active.remaining - CONSUMABLE_EFFECT_TICK_SECONDS;
        if remaining <= 0.0 {
            ctx.db.active_consumable_effect().effect_id().delete(&active.effect_id);
            affected_profiles.push(active.profile_id);
        } else {
            ctx.db.active_consumable_effect().effect_id().update(ActiveConsumableEffect { remaining, ..active });
        }
    }
    for profile_id in affected_profiles {
        recompute_stats(ctx, profile_id);
    }
    // Anti-cheat housekeeping: sweep witness flags older than HIT_FLAG_MAX_AGE_SECONDS —
    // no NoHits wager window outlives that, and the table must not grow unbounded.
    for flag in ctx.db.player_hit_flag().iter().collect::<Vec<_>>() {
        if crate::main::time::seconds_since(ctx, flag.reported_at) >= HIT_FLAG_MAX_AGE_SECONDS as f32 {
            ctx.db.player_hit_flag().flag_id().delete(&flag.flag_id);
        }
    }
    // Fire-attestation housekeeping: sweep unclaimed rows older than
    // BULLET_ATTESTATION_TTL_SECONDS — every fired volley inserts one, and missed
    // volleys are never claimed, so without this the table grows with the fire rate.
    for attestation in ctx.db.bullet_fire_attestation().iter().collect::<Vec<_>>() {
        if crate::main::time::seconds_since(ctx, attestation.fired_at) >= BULLET_ATTESTATION_TTL_SECONDS as f32 {
            ctx.db.bullet_fire_attestation().event_id().delete(&attestation.event_id);
        }
    }
    // Expired loot drops (expires_at is also enforced at pickup time).
    for drop in ctx.db.loot_drop().iter().collect::<Vec<_>>() {
        if ctx.timestamp >= drop.expires_at {
            ctx.db.loot_drop().drop_id().delete(&drop.drop_id);
        }
    }
    crate::main::agents::tick_regen(ctx);
    crate::main::agents::tick_zones(ctx);
    crate::status::methods::tick(ctx);
    crate::main::agents::tick_cleanup(ctx);
}

#[spacetimedb::reducer]
pub fn use_item(ctx: &ReducerContext, slot_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    // The client only exposes UseItem on the hotbar (InventoryPanel hotbar keys → slots
    // 1-4); match that here instead of accepting any slot.
    if slot.role != SlotRole::Hotbar { return Err("Only hotbar items can be used.".to_string()); }
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    // Never consume an item with an Ability behavior — using a span head would orphan
    // its follower cells. Defense in depth: ability items have no Consumable behavior
    // today, and the Hotbar role already rejects them, but a future dual-behavior item
    // must not be consumable either.
    if ability_behavior(&item).is_some() { return Err("Ability items cannot be used.".to_string()); }
    let consumable = consumable_behavior(&item)
        .ok_or("Only consumable items can be used.")?
        .clone();
    apply_consumable_effect(ctx, profile_id, &consumable);

    update_slot(ctx, PlayerInventorySlot { item_id: None, ..slot });
    recompute_stats(ctx, profile_id);
    Ok(())
}

// Applies the position multiplier to an ability's effect: Heal scales through potency, but
// Buff amounts and bullet-control radii live inside the effect variant, so those are scaled
// here directly.
fn scale_ability_effect(effect: &AbilityEffect, multiplier: f32) -> AbilityEffect {
    match effect {
        AbilityEffect::Heal => AbilityEffect::Heal,
        AbilityEffect::Buff(buff) => AbilityEffect::Buff(match buff {
            ConsumableBuffEffect::Strength(v) => ConsumableBuffEffect::Strength(v * multiplier),
            ConsumableBuffEffect::Wisdom(v) => ConsumableBuffEffect::Wisdom(v * multiplier),
            ConsumableBuffEffect::Dexterity(v) => ConsumableBuffEffect::Dexterity(v * multiplier),
            ConsumableBuffEffect::DamageDealer(v) => ConsumableBuffEffect::DamageDealer(v * multiplier),
            ConsumableBuffEffect::Supporter(v) => ConsumableBuffEffect::Supporter(v * multiplier),
            ConsumableBuffEffect::Artisan(v) => ConsumableBuffEffect::Artisan(v * multiplier),
        }),
        AbilityEffect::DeleteBullets(r) => AbilityEffect::DeleteBullets(r * multiplier),
        AbilityEffect::AttractBullets(r) => AbilityEffect::AttractBullets(r * multiplier),
        AbilityEffect::SplitBullets(p) => AbilityEffect::SplitBullets(RectParams { length: p.length * multiplier, width: p.width * multiplier }),
        AbilityEffect::DeleteBulletsInRect(p) => AbilityEffect::DeleteBulletsInRect(RectParams { length: p.length * multiplier, width: p.width * multiplier }),
        AbilityEffect::MarkBleed(p) => AbilityEffect::MarkBleed(MarkBleedParams { ally_radius: p.ally_radius * multiplier, ..*p }),
        AbilityEffect::MarkStore(p) => AbilityEffect::MarkStore(MarkStoreParams { ally_radius: p.ally_radius * multiplier, ..*p }),
        AbilityEffect::Invulnerable(r) => AbilityEffect::Invulnerable(r * multiplier),
        AbilityEffect::DamageShield(r) => AbilityEffect::DamageShield(r * multiplier),
        AbilityEffect::ReduceDamage(p) => AbilityEffect::ReduceDamage(ReduceParams { pct: p.pct * multiplier, ally_radius: p.ally_radius * multiplier }),
        AbilityEffect::RedirectToCaster(r) => AbilityEffect::RedirectToCaster(r * multiplier),
        AbilityEffect::RegenAura(p) => AbilityEffect::RegenAura(RegenParams { per_second: p.per_second * multiplier, ally_radius: p.ally_radius * multiplier }),
        AbilityEffect::StoreIncoming(p) => AbilityEffect::StoreIncoming(StoreIncomingParams { release_radius: p.release_radius * multiplier, ..*p }),
        AbilityEffect::Curse(p) => AbilityEffect::Curse(CurseParams { magnitude: p.magnitude * multiplier, ..*p }),
        AbilityEffect::Bash(p) => AbilityEffect::Bash(BashParams { stagger: p.stagger * multiplier, range: p.range * multiplier, ..*p }),
        AbilityEffect::Slash(p) => AbilityEffect::Slash(SlashParams { length: p.length * multiplier, width: p.width * multiplier, ..*p }),
        AbilityEffect::Wall(p) => AbilityEffect::Wall(WallParams { length: p.length * multiplier, width: p.width * multiplier }),
        AbilityEffect::CastZone(p) => AbilityEffect::CastZone(ZoneParams { radius: p.radius * multiplier, magnitude: p.magnitude * multiplier, ..*p }),
        AbilityEffect::Teleport(p) => AbilityEffect::Teleport(TeleportParams { range: p.range * multiplier, ..*p }),
        AbilityEffect::Grapple(r) => AbilityEffect::Grapple(r * multiplier),
        // Damage and the Circle radius scale; Line/Cone dims and the volley hit count don't.
        AbilityEffect::Spell(p) => AbilityEffect::Spell(SpellParams {
            shape: match p.shape {
                SpellShape::Circle(c) => SpellShape::Circle(CircleParams { radius: c.radius * multiplier }),
                shape => shape,
            },
            damage: p.damage * multiplier,
            ..*p
        }),
    }
}

/// Charge-scaled range multiplier (P4): 25% at a 0-charge release up to 100% at full
/// charge. The client's optimistic path mirrors this formula
/// (AbilityChargeComponent.ChargeRangeMultiplier) — both must change together.
pub fn charge_range_multiplier(charge_fraction: f32) -> f32 {
    CHARGE_MIN_RANGE_FRACTION + (1.0 - CHARGE_MIN_RANGE_FRACTION) * charge_fraction.clamp(0.0, 1.0)
}

/// The shared ability-slot validation preamble (P4: factored out of activate_ability so
/// begin/release_ability_charge run the identical checks): ability head slot with an item
/// that has an Ability behavior, not on cooldown, not out of charges. Unlike use_item,
/// the item is not consumed on use — it goes on cooldown and may spend a charge.
pub fn resolve_ability_slot(ctx: &ReducerContext, profile_id: u64, slot_index: u32) -> Result<(PlayerInventorySlot, AbilityBehavior), String> {
    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    if slot.role != SlotRole::Ability { return Err("Not an ability slot.".to_string()); }
    if slot.occupied_by.is_some() { return Err("Activate the ability's head slot.".to_string()); }
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    let ability = ability_behavior(&item).ok_or("Item has no ability.")?.clone();

    if let Some(until) = slot.cooldown_until {
        if ctx.timestamp < until { return Err("Ability is on cooldown.".to_string()); }
    }
    if slot.charges == 0 { return Err("Ability is out of charges.".to_string()); }
    Ok((slot, ability))
}

/// The shared post-effect spend (P4: factored out of activate_ability so
/// release_ability_charge spends exactly the same way): the "whenever you use an
/// ability..." enchantment triggers (doc 03), then cooldown + a charge.
pub fn spend_ability(ctx: &ReducerContext, profile_id: u64, slot: PlayerInventorySlot, ability: &AbilityBehavior) {
    for b in equipped_enchantment_behaviors(ctx, profile_id) {
        if let EnchantmentBehavior::OnAbilityUseBuff(params) = b {
            apply_consumable_effect(ctx, profile_id, &ConsumableBehavior {
                effect: ConsumableEffect::Buff(params.buff),
                potency: 0.0,
                duration: params.duration,
            });
        }
    }

    let charges = if ability.max_charges == 0 { u32::MAX } else { slot.charges - 1 };
    update_slot(ctx, PlayerInventorySlot {
        cooldown_until: Some(ctx.timestamp + Duration::from_secs_f32(ability.cooldown_seconds)),
        charges,
        ..slot
    });
}

/// Mark targeting, shared by every mark ability: the enemy nearest the (range-clamped)
/// cursor point within MARK_PICK_RADIUS, resolved server-side. The client mirrors the same
/// pick optimistically; on divergence the server row wins (the mark indicator is
/// server-echoed). Both sides must clamp and pick identically.
fn cursor_pick_enemy(ctx: &ReducerContext, pos: &PlayerPosition, target_x: f32, target_y: f32) -> Result<Enemy, String> {
    let (dx, dy) = (target_x - pos.x, target_y - pos.y);
    let dist = (dx * dx + dy * dy).sqrt();
    let (tx, ty) = if dist > MAX_ABILITY_TARGET_RANGE {
        (pos.x + dx / dist * MAX_ABILITY_TARGET_RANGE, pos.y + dy / dist * MAX_ABILITY_TARGET_RANGE)
    } else {
        (target_x, target_y)
    };
    let cfg = MapConfig::load(ctx, 0, 0);
    let (lap_q, lap_r) = ((cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));
    enemies_near_point(ctx, tx, ty, MARK_PICK_RADIUS)
        .into_iter()
        .min_by(|a, b| {
            let da = wrapped_distance_sq(tx, ty, a.x, a.y, lap_q, lap_r);
            let db = wrapped_distance_sq(tx, ty, b.x, b.y, lap_q, lap_r);
            da.partial_cmp(&db).unwrap_or(std::cmp::Ordering::Equal)
        })
        .ok_or_else(|| "No enemy near the target point.".to_string())
}

/// The shared ability-effect dispatch (P4: factored out of activate_ability so
/// release_ability_charge dispatches the identical effect scaled by its charge fraction;
/// instant casts pass 1.0). Potency scales by span-head position (first ability stronger).
pub fn apply_ability_effect(
    ctx: &ReducerContext,
    profile_id: u64,
    slot: &PlayerInventorySlot,
    ability: &AbilityBehavior,
    target_x: f32,
    target_y: f32,
    charge_fraction: f32,
) -> Result<(), String> {
    let multiplier = ability_position_multiplier(slot.slot_index);
    let effect = scale_ability_effect(&ability.effect, multiplier);
    match &effect {
        AbilityEffect::Heal | AbilityEffect::Buff(_) => {
            let consumable_effect = match &effect {
                AbilityEffect::Heal => ConsumableEffect::Heal,
                AbilityEffect::Buff(b) => ConsumableEffect::Buff(b.clone()),
                _ => unreachable!(),
            };
            apply_consumable_effect(ctx, profile_id, &ConsumableBehavior {
                effect: consumable_effect,
                potency: ability.potency * multiplier,
                duration: ability.duration,
            });
        }
        AbilityEffect::DeleteBullets(radius) | AbilityEffect::AttractBullets(radius) => {
            // Clamp the client-supplied cursor target into range of the server's own
            // position row, then relay the cast as a BulletControlEvent — the server tracks
            // no live bullets; every client resolves the proximity query locally. Attract
            // carries the ability's duration as the black hole's linger time (0 = one-shot).
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let (dx, dy) = (target_x - pos.x, target_y - pos.y);
            let dist = (dx * dx + dy * dy).sqrt();
            let (tx, ty) = if dist > MAX_ABILITY_TARGET_RANGE {
                (pos.x + dx / dist * MAX_ABILITY_TARGET_RANGE, pos.y + dy / dist * MAX_ABILITY_TARGET_RANGE)
            } else {
                (target_x, target_y)
            };
            let kind = match &effect {
                AbilityEffect::DeleteBullets(_) => BulletControlKind::Delete,
                _ => BulletControlKind::Attract,
            };
            ctx.db.bullet_control_event().insert(BulletControlEvent {
                event_id: 0, kind, x: tx, y: ty, radius: *radius, target_x: tx, target_y: ty,
                duration: ability.duration, cast_by: ctx.sender(),
            });
        }
        AbilityEffect::DeleteBulletsInRect(p) | AbilityEffect::SplitBullets(p) => {
            // The delete/split rectangle goes from the player toward the cursor: relay
            // origin + far end + width. Direction comes from the clamped cursor target, with a
            // fixed fallback when the cursor sits on the player (clients mirror it).
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let (dx, dy) = (target_x - pos.x, target_y - pos.y);
            let dist = (dx * dx + dy * dy).sqrt();
            let (ux, uy) = if dist > 0.001 { (dx / dist, dy / dist) } else { (1.0, 0.0) };
            let kind = match &effect {
                AbilityEffect::SplitBullets(_) => BulletControlKind::SplitRect,
                _ => BulletControlKind::DeleteRect,
            };
            ctx.db.bullet_control_event().insert(BulletControlEvent {
                event_id: 0, kind, x: pos.x, y: pos.y,
                target_x: pos.x + ux * p.length, target_y: pos.y + uy * p.length,
                radius: p.width, duration: 0.0, cast_by: ctx.sender(),
            });
        }
        AbilityEffect::MarkBleed(p) => {
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let marked = cursor_pick_enemy(ctx, &pos, target_x, target_y)?;
            let params = StatusParams {
                purge: p.purge,
                conversion: p.conversion,
                pool_seconds: p.pool_seconds,
                r1: BLEED_STAY_CLOSE_RADIUS,
                x: BLEED_DEAL_DAMAGE_THRESHOLD,
                outgoing: p.outgoing,
                ..StatusParams::none()
            };
            for target in ally_targets(ctx, profile_id, pos.x, pos.y, p.ally_radius) {
                apply_status_effect(ctx, target, StatusEffectKind::DamageToBleed, params, ability.duration, marked.enemy_id, profile_id);
            }
        }
        AbilityEffect::MarkStore(p) => {
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let marked = cursor_pick_enemy(ctx, &pos, target_x, target_y)?;
            let params = StatusParams {
                wager: p.wager,
                x: p.x, z: p.z, r1: p.r1, r2: p.r2, max_hits: p.max_hits,
                ..StatusParams::none()
            };
            for target in ally_targets(ctx, profile_id, pos.x, pos.y, p.ally_radius) {
                apply_status_effect(ctx, target, StatusEffectKind::StoreDamage, params, ability.duration, marked.enemy_id, profile_id);
            }
        }
        AbilityEffect::StoreIncoming(p) => {
            let kind = match p.release {
                ReleaseKind::Smite => StatusEffectKind::StoreIncomingSmite,
                ReleaseKind::Bash => StatusEffectKind::StoreIncomingBash,
            };
            // Smite recast during an active window releases the pool early — at the cursor,
            // which is the only release path that has one (window expiry bursts around the
            // paladin instead).
            if p.release == ReleaseKind::Smite {
                if let Some(existing) = ctx.db.active_status_effect().profile_id().filter(&profile_id)
                    .find(|r| r.kind == StatusEffectKind::StoreIncomingSmite)
                {
                    if existing.pool >= 1.0 {
                        let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
                        let (dx, dy) = (target_x - pos.x, target_y - pos.y);
                        let dist = (dx * dx + dy * dy).sqrt();
                        let (tx, ty) = if dist > MAX_ABILITY_TARGET_RANGE {
                            (pos.x + dx / dist * MAX_ABILITY_TARGET_RANGE, pos.y + dy / dist * MAX_ABILITY_TARGET_RANGE)
                        } else {
                            (target_x, target_y)
                        };
                        for enemy in enemies_near_point(ctx, tx, ty, p.release_radius) {
                            crate::combat::apply_damage_to_enemy(ctx, enemy, existing.pool as u32, 0, crate::combat::DamageRequest::NORMAL, Some(profile_id), Some(ctx.sender()));
                        }
                    }
                    ctx.db.active_status_effect().effect_id().delete(&existing.effect_id);
                }
            }
            let params = StatusParams { release: p.release, release_radius: p.release_radius, ..StatusParams::none() };
            apply_status_effect(ctx, profile_id, kind, params, ability.duration, 0, profile_id);
        }
        AbilityEffect::Invulnerable(radius) | AbilityEffect::DamageShield(radius) => {
            let kind = match &effect {
                AbilityEffect::Invulnerable(_) => StatusEffectKind::Invulnerable,
                _ => StatusEffectKind::DamageShield,
            };
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            for target in ally_targets(ctx, profile_id, pos.x, pos.y, *radius) {
                apply_status_effect(ctx, target, kind, StatusParams::none(), ability.duration, 0, profile_id);
            }
        }
        AbilityEffect::ReduceDamage(p) => {
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let params = StatusParams { pct: p.pct, ..StatusParams::none() };
            for target in ally_targets(ctx, profile_id, pos.x, pos.y, p.ally_radius) {
                apply_status_effect(ctx, target, StatusEffectKind::DamageReductionPct, params, ability.duration, 0, profile_id);
            }
        }
        AbilityEffect::RedirectToCaster(radius) => {
            // Allies in radius EXCLUDING the caster — the paladin is the sink, not a beneficiary.
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            for target in ally_targets(ctx, profile_id, pos.x, pos.y, *radius) {
                if target == profile_id { continue; }
                apply_status_effect(ctx, target, StatusEffectKind::RedirectToCaster, StatusParams::none(), ability.duration, 0, profile_id);
            }
        }
        AbilityEffect::RegenAura(p) => {
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let params = StatusParams { per_second: p.per_second, ..StatusParams::none() };
            for target in ally_targets(ctx, profile_id, pos.x, pos.y, p.ally_radius) {
                apply_status_effect(ctx, target, StatusEffectKind::Regen, params, ability.duration, 0, profile_id);
            }
        }
        AbilityEffect::Curse(p) => {
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let target = cursor_pick_enemy(ctx, &pos, target_x, target_y)?;
            crate::status::methods::apply_enemy_status(ctx, target.enemy_id, p.kind, p.magnitude, ability.duration, profile_id);
        }
        AbilityEffect::Slash(_) | AbilityEffect::Wall(_) => {
            // Same rectangle relay as DeleteRect/SplitRect: origin + far end + width, with a
            // fixed fallback direction when the cursor sits on the player (clients mirror
            // it). Slash carries its damage threshold in duration as f32 bits; Wall carries
            // the ability's duration as the linger time (the Attract pattern). The server
            // stays bullet-agnostic — clients resolve the query locally.
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let (dx, dy) = (target_x - pos.x, target_y - pos.y);
            let dist = (dx * dx + dy * dy).sqrt();
            let (ux, uy) = if dist > 0.001 { (dx / dist, dy / dist) } else { (1.0, 0.0) };
            let (kind, length, width, duration) = match &effect {
                AbilityEffect::Slash(p) => (BulletControlKind::SlashRect, p.length, p.width, f32::from_bits(p.max_bullet_damage)),
                AbilityEffect::Wall(p) => (BulletControlKind::WallRect, p.length, p.width, ability.duration),
                _ => unreachable!(),
            };
            ctx.db.bullet_control_event().insert(BulletControlEvent {
                event_id: 0, kind, x: pos.x, y: pos.y,
                target_x: pos.x + ux * length, target_y: pos.y + uy * length,
                radius: width, duration, cast_by: ctx.sender(),
            });
        }
        AbilityEffect::Bash(p) => {
            // Melee swing around the PLAYER: every enemy in range takes weapon damage through
            // the shared weapon path (true-damage/mitigation/status consults all apply) plus
            // stagger-bar points; at 100 the enemy is Stunned per its template's
            // stun_behavior and the bar resets. An ARMED StoreIncomingBash row (P1c:
            // violated == armed, pool frozen) rides the hit — its pool merges pre-mitigation
            // into every enemy hit — and is consumed.
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let armed_store = ctx.db.active_status_effect().profile_id().filter(&profile_id)
                .find(|r| r.kind == StatusEffectKind::StoreIncomingBash && r.violated);
            let bonus = armed_store.as_ref().map(|r| r.pool as u32).unwrap_or(0);
            for enemy in enemies_near_point(ctx, pos.x, pos.y, p.range) {
                let enemy_id = enemy.enemy_id;
                crate::combat::deal_damage_to_enemy_bonus(ctx, profile_id, enemy, bonus);
                // The hit may have killed it — no stagger on a corpse.
                let Some(alive) = ctx.db.enemy().enemy_id().find(&enemy_id) else { continue; };
                let stagger = alive.stagger + p.stagger;
                if stagger >= 100.0 {
                    crate::status::methods::apply_enemy_status(ctx, enemy_id, EnemyStatusKind::Stunned, BASH_STUN_MAGNITUDE, p.stun_seconds, profile_id);
                    if p.armor_break {
                        crate::status::methods::apply_enemy_status(ctx, enemy_id, EnemyStatusKind::ArmorBreak, 1.0, p.stun_seconds, profile_id);
                    }
                    ctx.db.enemy().enemy_id().update(Enemy { stagger: 0.0, ..alive });
                } else {
                    ctx.db.enemy().enemy_id().update(Enemy { stagger, ..alive });
                }
            }
            if let Some(store) = armed_store {
                ctx.db.active_status_effect().effect_id().delete(&store.effect_id);
            }
        }
        AbilityEffect::CastZone(p) => {
            // Ground zone (P3) at the range-clamped cursor point — the same clamp math as
            // the bullet-control arms. Owned by the caster (zones die with them), lives
            // ability.duration seconds; the 1 Hz status tick drives the zone's effects.
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let (dx, dy) = (target_x - pos.x, target_y - pos.y);
            let dist = (dx * dx + dy * dy).sqrt();
            let (tx, ty) = if dist > MAX_ABILITY_TARGET_RANGE {
                (pos.x + dx / dist * MAX_ABILITY_TARGET_RANGE, pos.y + dy / dist * MAX_ABILITY_TARGET_RANGE)
            } else {
                (target_x, target_y)
            };
            ctx.db.ground_zone().insert(GroundZone {
                zone_id: 0, owner_profile_id: profile_id, kind: p.kind,
                x: tx, y: ty, radius: p.radius, magnitude: p.magnitude,
                remaining: ability.duration,
            });
        }
        AbilityEffect::Spell(p) => {
            // P5: shape damage at/toward the range-clamped cursor. Damage per enemy is
            // damage × (1 + scaling stat × 0.002) — mirroring the strength formula in
            // compute_player_damage, but off the ability's ScalingStat (wizard spells scale
            // off Wisdom, bullet-master spells off Dexterity) — × the charge-range
            // multiplier on a charged release. A volley (hits > 1) lands as hits SEPARATE
            // mitigated hits of damage/hits each, the enemy row re-found before every
            // pellet so a kill mid-volley makes the rest no-ops. The cast is relayed as an
            // AbilityVisualEvent — every client draws the flash (the caster skips its own
            // echo, having rendered optimistically).
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let (dx, dy) = (target_x - pos.x, target_y - pos.y);
            let dist = (dx * dx + dy * dy).sqrt();
            let (tx, ty) = if dist > MAX_ABILITY_TARGET_RANGE {
                (pos.x + dx / dist * MAX_ABILITY_TARGET_RANGE, pos.y + dy / dist * MAX_ABILITY_TARGET_RANGE)
            } else {
                (target_x, target_y)
            };
            let (ux, uy) = if dist > 0.001 { (dx / dist, dy / dist) } else { (1.0, 0.0) };
            let charge_mult = if p.charge_scaled { charge_range_multiplier(charge_fraction) } else { 1.0 };
            let stat = match ctx.db.player_stats().profile_id().find(&profile_id) {
                Some(s) => match p.scaling {
                    ScalingStat::Strength => s.strength,
                    ScalingStat::Wisdom => s.wisdom,
                    ScalingStat::Dexterity => s.dexterity,
                },
                None => 0,
            };
            let total = p.damage * (1.0 + stat as f32 * 0.002) * charge_mult;
            let hits = p.hits.max(1);
            let per_hit = (total / hits as f32) as u32;
            for enemy in spell_targets(ctx, (pos.x, pos.y), (ux, uy), (tx, ty), &p.shape) {
                let enemy_id = enemy.enemy_id;
                for _ in 0..hits {
                    let Some(alive) = ctx.db.enemy().enemy_id().find(&enemy_id) else { break };
                    crate::combat::apply_damage_to_enemy(ctx, alive, per_hit, 0, crate::combat::DamageRequest::NORMAL, Some(profile_id), Some(ctx.sender()));
                }
            }
            // The visual event encodes the shape per AbilityVisualEvent's field encoding:
            // Circle kinds center on the clamped cursor; Line/Cone run origin → far end.
            let (vx, vy, vr) = match &p.shape {
                SpellShape::Circle(c) => (tx, ty, c.radius),
                SpellShape::Line(l) => (pos.x + ux * l.length, pos.y + uy * l.length, l.width),
                SpellShape::Cone(c) => (pos.x + ux * c.length, pos.y + uy * c.length, c.spread),
            };
            ctx.db.ability_visual_event().insert(AbilityVisualEvent {
                event_id: 0, kind: p.visual, x: pos.x, y: pos.y,
                target_x: vx, target_y: vy, radius: vr,
                duration: SPELL_VISUAL_SECONDS, cast_by: ctx.sender(),
            });
        }
        AbilityEffect::Teleport(p) => {
            // P4: clamp the cursor into `range` around the server's own position row
            // (the charge path shrinks that range by charge_range_multiplier), then move
            // the caster server-side — wrap + chunk write exactly like report_movement.
            // No extra client report needed: the echoed row hard-corrects puppets via
            // the desync snap, and the next scheduled report carries the new position.
            let pos = ctx.db.player_position().profile_id().find(&profile_id).ok_or("Position not found.")?;
            let range = p.range * charge_range_multiplier(charge_fraction);
            let (dx, dy) = (target_x - pos.x, target_y - pos.y);
            let dist = (dx * dx + dy * dy).sqrt();
            let (tx, ty) = if dist > range {
                (pos.x + dx / dist * range, pos.y + dy / dist * range)
            } else {
                (target_x, target_y)
            };
            teleport_player(ctx, profile_id, tx, ty)?;
        }
        AbilityEffect::Grapple(range) => {
            // P4: the pull is client-tweened and client-authoritative — validation,
            // cooldown/charges and enchantment triggers all run through the shared
            // activate/spend path; the client's next movement reports carry the position.
            // Stamp a one-shot displacement allowance so report_movement's displacement
            // clamp accepts the tween's oversized report (up to the scaled grapple range)
            // instead of rubber-banding it; consumed on first use, expires quickly.
            let allowance = PlayerDisplacementAllowance {
                profile_id,
                max_distance: *range,
                expires_at: ctx.timestamp + Duration::from_secs_f32(GRAPPLE_ALLOWANCE_SECONDS),
            };
            if ctx.db.player_displacement_allowance().profile_id().find(&profile_id).is_some() {
                ctx.db.player_displacement_allowance().profile_id().update(allowance);
            } else {
                ctx.db.player_displacement_allowance().insert(allowance);
            }
        }
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn activate_ability(ctx: &ReducerContext, slot_index: u32, target_x: f32, target_y: f32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let (slot, ability) = resolve_ability_slot(ctx, profile_id, slot_index)?;
    // Knowledge gate (Phase 1 — BitCraft required_knowledges): the slot's item may
    // carry required knowledge ids; all must be acquired. No row = no gate.
    if let Some(item_id) = slot.item_id.as_ref() {
        if let Some(req) = ctx.db.ability_knowledge_requirement().item_id().find(item_id) {
            if !req.required_ids.is_empty() {
                // blocking_knowledges early-out via knowledge_state (2 = acquired).
                let missing: Vec<&String> = req.required_ids.iter()
                    .filter(|id| knowledge_state(ctx, profile_id, id) < 2)
                    .collect();
                if !missing.is_empty() {
                    return Err(format!("Missing required knowledge: {}", missing.into_iter().map(|s| s.as_str()).collect::<Vec<_>>().join(", ")));
                }
            }
        }
    }
    // Charge-capable abilities (P4) go through begin/release_ability_charge — activating
    // one directly would bypass the charge (and its SelfSlow) at full effect.
    if ability_is_charge_capable(&ability) {
        return Err("Hold the ability key to charge, release to cast.".to_string());
    }

    apply_ability_effect(ctx, profile_id, &slot, &ability, target_x, target_y, 1.0)?;
    spend_ability(ctx, profile_id, slot, &ability);
    Ok(())
}

#[spacetimedb::reducer]
pub fn set_slot_toggle(ctx: &ReducerContext, slot_index: u32, value: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    if slot.occupied_by.is_some() { return Err("Cannot toggle a span follower slot.".to_string()); }
    if slot.role != SlotRole::Weapon { return Err("Only the weapon slot has shot-pattern toggles.".to_string()); }
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    // Toggle options = the item's own Weapon behaviors + WeaponToggle enchantment grants;
    // the same list resolve_effective_weapon indexes into.
    let count = weapon_toggle_options(ctx, profile_id, &item).len() as u32;
    if count < 2 { return Err("Item has no toggle patterns.".to_string()); }
    if value >= count { return Err(format!("Toggle index {} out of range ({} patterns).", value, count)); }

    update_slot(ctx, PlayerInventorySlot { active_toggle: value, ..slot });
    Ok(())
}

#[spacetimedb::reducer]
pub fn allocate_stat(ctx: &ReducerContext, stat: StatKind, points: u32) -> Result<(), String> {
    let _player = require_in_world(ctx)?;
    let profile_id = _player.profile_id;
    if points == 0 { return Err("No points requested.".to_string()); }

    let mut alloc = ctx.db.player_stat_allocation().profile_id().find(&profile_id)
        .ok_or("Stat allocation not found.")?;
    if alloc.unspent_points < points { return Err("Not enough unspent skill points.".to_string()); }

    alloc.unspent_points -= points;
    match stat {
        StatKind::Strength => alloc.strength += points,
        StatKind::Wisdom => alloc.wisdom += points,
        StatKind::Dexterity => alloc.dexterity += points,
        StatKind::DamageDealer => alloc.damage_dealer += points,
        StatKind::Supporter => alloc.supporter += points,
        StatKind::Artisan => alloc.artisan += points,
        StatKind::Hp | StatKind::Defense | StatKind::BaseSpeed => return Err("Hp, Defense and BaseSpeed come from gear, not allocation.".to_string()),
    }
    ctx.db.player_stat_allocation().profile_id().update(alloc);
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn apply_enchantment(ctx: &ReducerContext, slot_index: u32, enchantment_id: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    let item = ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    let enchantment = ctx.db.enchantment().enchantment_id().find(&enchantment_id)
        .ok_or_else(|| format!("Enchantment '{}' not found.", enchantment_id))?;
    if !enchantment.allowed_slots.contains(&item.equip_slot) {
        return Err(format!("Enchantment '{}' cannot be applied to '{}'.", enchantment_id, item_id));
    }
    if slot.enchantment_ids.len() as u32 >= item.max_enchantments {
        return Err(format!("Item '{}' cannot hold more enchantments.", item_id));
    }
    if slot.enchantment_ids.contains(&enchantment_id) {
        return Err(format!("Enchantment '{}' is already applied to this item.", enchantment_id));
    }

    let mut enchantment_ids = slot.enchantment_ids.clone();
    enchantment_ids.push(enchantment_id);
    update_slot(ctx, PlayerInventorySlot { enchantment_ids, ..slot });
    recompute_stats(ctx, profile_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn remove_enchantment(ctx: &ReducerContext, slot_index: u32, enchantment_id: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let profile_id = player.profile_id;

    let slot = get_slot(ctx, profile_id, slot_index).ok_or("Invalid slot index.")?;
    if !slot.enchantment_ids.contains(&enchantment_id) {
        return Err(format!("Enchantment '{}' is not applied to this item.", enchantment_id));
    }

    let mut enchantment_ids = slot.enchantment_ids.clone();
    enchantment_ids.retain(|id| id != &enchantment_id);
    update_slot(ctx, PlayerInventorySlot { enchantment_ids, ..slot });
    recompute_stats(ctx, profile_id);
    Ok(())
}

// ===== Discovery / knowledge (Phase 1 — BitCraft discover/acquire pattern) =====

/// Explicit discover (observe without acquiring): marks ids as seen. Pickup and
/// chest takes auto-discover; this covers "seen in the world" flows.
#[spacetimedb::reducer]
pub fn discover_knowledge(ctx: &ReducerContext, ids: Vec<String>) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    if ids.is_empty() || ids.len() > 32 {
        return Err("Provide 1-32 ids.".to_string());
    }
    discover_ids(ctx, player.profile_id, ids);
    Ok(())
}

/// Explicit unlock (BitCraft `acquire_*`): moves ids from discovered to acquired.
/// Acquiring an undiscovered id discovers it first (idempotent).
#[spacetimedb::reducer]
pub fn acquire_knowledge(ctx: &ReducerContext, ids: Vec<String>) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    if ids.is_empty() || ids.len() > 32 {
        return Err("Provide 1-32 ids.".to_string());
    }
    discover_ids(ctx, player.profile_id, ids.clone());
    if let Some(row) = ctx.db.player_knowledge().profile_id().find(&player.profile_id) {
        let mut acquired = row.acquired.clone();
        for id in ids {
            if !acquired.iter().any(|a| a == &id) {
                acquired.push(id);
            }
        }
        ctx.db.player_knowledge().profile_id().update(PlayerKnowledge { acquired, ..row });
    }
    Ok(())
}

/// Admin: set the knowledge gate for an ability item (empty = no gate).
#[spacetimedb::reducer]
pub fn set_ability_knowledge(ctx: &ReducerContext, item_id: String, required_ids: Vec<String>) -> Result<(), String> {
    if !is_admin(ctx) {
        return Err("Admin only.".to_string());
    }
    ctx.db.item().item_id().find(&item_id).ok_or("Item not found.")?;
    match ctx.db.ability_knowledge_requirement().item_id().find(&item_id) {
        Some(row) => {
            ctx.db.ability_knowledge_requirement().item_id().update(
                crate::item::tables::AbilityKnowledgeRequirement { required_ids, ..row },
            );
        }
        None => {
            ctx.db.ability_knowledge_requirement().insert(
                crate::item::tables::AbilityKnowledgeRequirement { item_id, required_ids },
            );
        }
    }
    Ok(())
}
