use spacetimedb::{ReducerContext, Table};
use crate::main::global::*;
use crate::world::hex::{spiral_chunk_index, world_to_chunk};
use crate::world::wrap::{wrap_world_pos, wrapped_distance_sq};
use crate::world::aoi::chunk_indices_covering;
use crate::world::tables::MapConfig;
use crate::player::tables::*;
use crate::item::tables::*;
use crate::item::methods::ability_behavior;

pub fn require_logged_in(ctx: &ReducerContext) -> Result<(), String> {
    if ctx.db.logged_in_player().player_id().find(ctx.sender()).is_none() {
        return Err("Not in world.".to_string());
    }
    Ok(())
}

pub fn require_in_world(ctx: &ReducerContext) -> Result<LoggedInPlayer, String> {
    ctx.db.logged_in_player().player_id().find(ctx.sender()).ok_or_else(|| "Not in world.".to_string())
}

pub fn require_in_lobby(ctx: &ReducerContext) -> Result<LoggedOutPlayer, String> {
    ctx.db.logged_out_player().player_id().find(ctx.sender())
        .ok_or_else(|| "Not in lobby.".to_string())
}

pub fn is_admin(ctx: &ReducerContext) -> bool {
    if let Some(p) = ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        return p.is_admin;
    }
    ctx.db.logged_out_player().player_id().find(ctx.sender())
        .map(|p| p.is_admin)
        .unwrap_or(false)
}

/// Token-bucket rate limiter behind the server-only PlayerHitRate table (one 1-second
/// fixed window per profile). Resets the window once a second has elapsed, then returns
/// false once `cap` reports of the given direction were already consumed this window.
/// `outgoing` selects the report_enemy_hit counter, false the report_hit one. The row is
/// persisted either way so the window reset isn't lost on a rejected report.
pub fn hit_rate_allow(ctx: &ReducerContext, profile_id: u64, outgoing: bool, cap: u32) -> bool {
    let existing = ctx.db.player_hit_rate().profile_id().find(&profile_id);
    let exists = existing.is_some();
    let mut rate = existing
        .unwrap_or(PlayerHitRate { profile_id, window_start: ctx.timestamp, outgoing: 0, incoming: 0 });
    if crate::main::time::seconds_since(ctx, rate.window_start) >= 1.0 {
        rate.window_start = ctx.timestamp;
        rate.outgoing = 0;
        rate.incoming = 0;
    }
    let count = if outgoing { &mut rate.outgoing } else { &mut rate.incoming };
    let allowed = *count < cap;
    if allowed { *count += 1; }
    if exists {
        ctx.db.player_hit_rate().profile_id().update(rate);
    } else {
        ctx.db.player_hit_rate().insert(rate);
    }
    allowed
}

// u64 math: the old u32 formula overflowed past ~level 2930, which also made
// compute_level's loop spin forever on large xp values.
pub fn xp_for_level(level: u32) -> u64 {
    if level <= 1 { 0 } else { 100 * (level as u64 - 1) * level as u64 / 2 }
}

pub fn compute_level(total_xp: u32) -> u32 {
    let mut level = 1u32;
    while level < MAX_LEVEL && xp_for_level(level + 1) <= total_xp as u64 { level += 1; }
    level
}

pub fn compute_base_max_hp(level: u32) -> i32 {
    BASE_MAX_HP + (level as i32 - 1) * HP_PER_LEVEL
}

// Indexes into the flat/mult accumulators in recompute_stats: the six allocatable
// stats first (matching PlayerStatAllocation/PlayerStats field order), then the
// modifier-only kinds.
fn stat_index(stat: StatKind) -> usize {
    match stat {
        StatKind::Strength => 0,
        StatKind::Wisdom => 1,
        StatKind::Dexterity => 2,
        StatKind::DamageDealer => 3,
        StatKind::Supporter => 4,
        StatKind::Artisan => 5,
        StatKind::Hp => 6,
        StatKind::Defense => 7,
        StatKind::BaseSpeed => 8,
    }
}

fn buff_to_modifier(effect: &ConsumableBuffEffect) -> StatModifier {
    let (stat, amount) = match effect {
        ConsumableBuffEffect::Strength(v) => (StatKind::Strength, *v),
        ConsumableBuffEffect::Wisdom(v) => (StatKind::Wisdom, *v),
        ConsumableBuffEffect::Dexterity(v) => (StatKind::Dexterity, *v),
        ConsumableBuffEffect::DamageDealer(v) => (StatKind::DamageDealer, *v),
        ConsumableBuffEffect::Supporter(v) => (StatKind::Supporter, *v),
        ConsumableBuffEffect::Artisan(v) => (StatKind::Artisan, *v),
    };
    StatModifier { stat, amount, mode: StatMode::Flat }
}

/// The lap-shifted copy of point (x, y) nearest to the reference point — the same 9-shift
/// search as wrapped_distance_sq, but returning the shifted point so segment/angle tests
/// can run in ordinary Euclidean space (P5 spell geometry).
pub fn nearest_lap_copy(x: f32, y: f32, ref_x: f32, ref_y: f32, lap_q: (f32, f32), lap_r: (f32, f32)) -> (f32, f32) {
    let mut best = (x, y);
    let mut best_d = f32::MAX;
    for lq in -1..=1 {
        for lr in -1..=1 {
            let cx = x + lq as f32 * lap_q.0 + lr as f32 * lap_r.0;
            let cy = y + lq as f32 * lap_q.1 + lr as f32 * lap_r.1;
            let d = (cx - ref_x).powi(2) + (cy - ref_y).powi(2);
            if d < best_d { best_d = d; best = (cx, cy); }
        }
    }
    best
}

/// Exact point-in-shape test for a Spell ability (P5), in world coordinates: `anchor` is
/// the shape's reference point (the clamped cursor for Circle, the player for Line/Cone),
/// `dir` the normalized cast direction (unused by Circle), `point` the test position
/// (lap-shifted near the anchor by the caller). Pure math — unit-tested below.
pub fn spell_shape_contains(shape: &SpellShape, anchor: (f32, f32), dir: (f32, f32), point: (f32, f32)) -> bool {
    let (rx, ry) = (point.0 - anchor.0, point.1 - anchor.1);
    match shape {
        SpellShape::Circle(c) => rx * rx + ry * ry <= c.radius * c.radius,
        SpellShape::Line(l) => {
            let along = rx * dir.0 + ry * dir.1;
            if along < 0.0 || along > l.length { return false; }
            let across = (rx * dir.1 - ry * dir.0).abs();
            across <= l.width * 0.5
        }
        SpellShape::Cone(c) => {
            let along = rx * dir.0 + ry * dir.1;
            if along <= 0.0 { return false; }
            let across = (rx * dir.1 - ry * dir.0).abs();
            if along * along + across * across > c.length * c.length { return false; }
            across <= along * (c.spread * 0.5).tan()
        }
    }
}

/// The enemies a Spell shape hits (P5): wrap-filtered candidates from enemies_near_point
/// (a covering circle per shape — for Circle it IS the exact test), then the exact
/// geometry test on lap-shifted positions. Wrap simplification: the cast DIRECTION is
/// torus-naive (raw cursor − player, matching the target clamp in apply_ability_effect and
/// every other ability arm); only enemy positions are lap-shifted. A cast aimed across a
/// lap seam can therefore point the "long way" — the same seam case every other targeted
/// ability already has.
pub fn spell_targets(ctx: &ReducerContext, origin: (f32, f32), dir: (f32, f32), target: (f32, f32), shape: &SpellShape) -> Vec<crate::enemy::instance_tables::Enemy> {
    let cfg = MapConfig::load(ctx, 0, 0);
    let (lap_q, lap_r) = ((cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));
    let (anchor, reach) = match shape {
        SpellShape::Circle(c) => (target, c.radius),
        SpellShape::Line(l) => (origin, l.length + l.width * 0.5),
        SpellShape::Cone(c) => (origin, c.length),
    };
    crate::enemy::methods::enemies_near_point(ctx, anchor.0, anchor.1, reach)
        .into_iter()
        .filter(|e| {
            let p = nearest_lap_copy(e.x, e.y, anchor.0, anchor.1, lap_q, lap_r);
            spell_shape_contains(shape, anchor, dir, p)
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn circle_contains_by_radius() {
        let shape = SpellShape::Circle(CircleParams { radius: 100.0 });
        assert!(spell_shape_contains(&shape, (0.0, 0.0), (1.0, 0.0), (60.0, 80.0)));
        assert!(!spell_shape_contains(&shape, (0.0, 0.0), (1.0, 0.0), (60.0, 80.1)));
    }

    #[test]
    fn line_tests_along_and_across() {
        let shape = SpellShape::Line(LineParams { length: 500.0, width: 40.0 });
        let dir = (1.0, 0.0);
        assert!(spell_shape_contains(&shape, (0.0, 0.0), dir, (250.0, 19.9)));
        assert!(!spell_shape_contains(&shape, (0.0, 0.0), dir, (250.0, 20.1))); // too wide
        assert!(!spell_shape_contains(&shape, (0.0, 0.0), dir, (500.1, 0.0)));  // too far
        assert!(!spell_shape_contains(&shape, (0.0, 0.0), dir, (-0.1, 0.0)));   // behind
    }

    #[test]
    fn cone_tests_angle_and_length() {
        // spread 0.6 rad: half-angle 0.3, tan ≈ 0.3093.
        let shape = SpellShape::Cone(ConeParams { length: 350.0, spread: 0.6 });
        let dir = (1.0, 0.0);
        assert!(spell_shape_contains(&shape, (0.0, 0.0), dir, (300.0, 90.0)));  // ~17° < 0.3 rad
        assert!(!spell_shape_contains(&shape, (0.0, 0.0), dir, (300.0, 96.0))); // just outside
        assert!(!spell_shape_contains(&shape, (0.0, 0.0), dir, (351.0, 0.0)));  // beyond length
        assert!(!spell_shape_contains(&shape, (0.0, 0.0), dir, (-10.0, 0.0)));  // behind
    }

    #[test]
    fn nearest_lap_copy_picks_the_shifted_copy() {
        // World 1000 wide in q, laps in r irrelevant (zero vector).
        let p = nearest_lap_copy(990.0, 0.0, 10.0, 0.0, (1000.0, 0.0), (0.0, 0.0));
        assert_eq!(p, (-10.0, 0.0));
    }
}

/// Server-side teleport (P4): moves the position row to (x, y) exactly like a
/// report_movement write — wrap, chunk recompute, PlayerChunk updated only on a real
/// crossing (AOI views recompute on chunk changes, not every position write). No extra
/// client report is needed afterward: the echoed row hard-corrects local/remote puppets
/// via the desync snap, and the next scheduled report carries the new position.
pub fn teleport_player(ctx: &ReducerContext, profile_id: u64, x: f32, y: f32) -> Result<(), String> {
    let pos = ctx.db.player_position().profile_id().find(&profile_id)
        .ok_or_else(|| format!("Position not found for profile {}", profile_id))?;
    let cfg = MapConfig::load(ctx, 0, 0);
    let (x, y) = wrap_world_pos(x, y, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    let (cq, cr) = world_to_chunk(x, y, cfg.chunk_hex_radius, cfg.hex_outer_radius);
    let chunk_index = spiral_chunk_index(cq, cr);
    ctx.db.player_position().profile_id().update(PlayerPosition { x, y, chunk_index, ..pos });
    if let Some(chunk) = ctx.db.player_chunk().profile_id().find(&profile_id) {
        if (chunk.chunk_q, chunk.chunk_r) != (cq, cr) {
            ctx.db.player_chunk().profile_id().update(PlayerChunk { chunk_q: cq, chunk_r: cr, ..chunk });
        }
    }
    // Teleports are exempt from report_movement's displacement clamp; restamp the report
    // clock so the next report's budget measures from the teleport, not from before it.
    match ctx.db.player_movement_state().profile_id().find(&profile_id) {
        Some(state) => { ctx.db.player_movement_state().profile_id().update(PlayerMovementState { last_report: ctx.timestamp, ..state }); }
        None => { ctx.db.player_movement_state().insert(PlayerMovementState { profile_id, last_report: ctx.timestamp, falling_until: None }); }
    }
    Ok(())
}

fn fold_modifier(flat: &mut [f32; 9], mult: &mut [f32; 9], m: &StatModifier, scale: f32) {
    let idx = stat_index(m.stat);
    match m.mode {
        StatMode::Flat => flat[idx] += m.amount * scale,
        StatMode::Mult => mult[idx] += m.amount * scale,
    }
}

/// Stage 1: allocated skill points fold into the flat accumulator, so gear mults scale
/// them like any other flat source — temperaments work exactly like normal stats.
fn fold_allocation(ctx: &ReducerContext, profile_id: u64, flat: &mut [f32; 9]) {
    if let Some(alloc) = ctx.db.player_stat_allocation().profile_id().find(&profile_id) {
        flat[0] += alloc.strength as f32;
        flat[1] += alloc.wisdom as f32;
        flat[2] += alloc.dexterity as f32;
        flat[3] += alloc.damage_dealer as f32;
        flat[4] += alloc.supporter as f32;
        flat[5] += alloc.artisan as f32;
    }
}

/// Stage 2: equipped items and their enchantments. Only worn equipment and ability heads
/// contribute — the role check (not slot inference) decides. Ability passives scale by
/// the position multiplier (first ability stronger); enchantment behaviors never do.
fn fold_equipment(ctx: &ReducerContext, profile_id: u64, flat: &mut [f32; 9], mult: &mut [f32; 9]) {
    for slot in ctx.db.player_inventory_slot().profile_id().filter(&profile_id) {
        if slot.occupied_by.is_some() { continue; } // span follower cells hold no item
        let scale = match slot.role {
            SlotRole::Weapon | SlotRole::Accessory | SlotRole::Armor => 1.0,
            SlotRole::Ability => ability_position_multiplier(slot.slot_index),
            _ => continue,
        };
        let item_id = match &slot.item_id { Some(id) => id, None => continue };
        let item = match ctx.db.item().item_id().find(item_id) { Some(i) => i, None => continue };
        for m in &item.stat_modifiers {
            fold_modifier(flat, mult, m, scale);
        }
        // Innate enchantments (on the item row) apply like socketed ones (on the slot
        // row) but can't be removed and don't count against max_enchantments.
        for enchantment_id in item.innate_enchantment_ids.iter().chain(slot.enchantment_ids.iter()) {
            if let Some(enchantment) = ctx.db.enchantment().enchantment_id().find(enchantment_id) {
                for m in &enchantment.stat_modifiers {
                    fold_modifier(flat, mult, m, scale);
                }
            }
        }
    }
}

/// Stage 3: active consumable buffs apply unscaled on top.
fn fold_buffs(ctx: &ReducerContext, profile_id: u64, flat: &mut [f32; 9], mult: &mut [f32; 9]) {
    for active in ctx.db.active_consumable_effect().profile_id().filter(&profile_id) {
        fold_modifier(flat, mult, &active.modifier, 1.0);
    }
}

pub fn recompute_stats(ctx: &ReducerContext, profile_id: u64) {
    let data = match ctx.db.player_data().profile_id().find(&profile_id) {
        Some(d) => d,
        None => return,
    };

    let mut flat = [0f32; 9];
    let mut mult = [0f32; 9];
    fold_allocation(ctx, profile_id, &mut flat);
    fold_equipment(ctx, profile_id, &mut flat, &mut mult);
    fold_buffs(ctx, profile_id, &mut flat, &mut mult);

    let resolve = |idx: usize, base: i32| -> i32 {
        ((base as f32 + flat[idx]) * (1.0 + mult[idx])).round() as i32
    };

    let new_stats = PlayerStats {
        profile_id,
        strength: resolve(0, BASE_STAT),
        wisdom: resolve(1, BASE_STAT),
        dexterity: resolve(2, BASE_STAT),
        damage_dealer: resolve(3, BASE_STAT),
        supporter: resolve(4, BASE_STAT),
        artisan: resolve(5, BASE_STAT),
    };

    if ctx.db.player_stats().profile_id().find(&profile_id).is_some() {
        ctx.db.player_stats().profile_id().update(new_stats);
    } else {
        ctx.db.player_stats().insert(new_stats);
    }

    // Modifier-only kinds resolve onto PlayerData: Hp into max_hp, Defense into defense,
    // BaseSpeed into base_speed (f32 — speed isn't rounded to int like the others).
    let new_max_hp = resolve(6, compute_base_max_hp(data.level)).max(1) as u32;
    let new_defense = resolve(7, 0).max(0);
    let new_base_speed = ((BASE_SPEED + flat[8]) * (1.0 + mult[8])).max(1.0);
    if new_max_hp != data.max_hp || data.hp > new_max_hp || new_defense != data.defense || new_base_speed != data.base_speed {
        let new_hp = data.hp.min(new_max_hp);
        ctx.db.player_data().profile_id().update(PlayerData { max_hp: new_max_hp, hp: new_hp, defense: new_defense, base_speed: new_base_speed, ..data });
    }
}

fn inventory_slot(profile_id: u64, slot_index: u32, role: SlotRole, item_id: Option<String>) -> PlayerInventorySlot {
    PlayerInventorySlot {
        slot_id: 0, profile_id, slot_index, role, item_id,
        enchantment_ids: vec![], cooldown_until: None, charges: u32::MAX, active_toggle: 0, occupied_by: None,
    }
}

pub fn try_scaffold_profile(ctx: &ReducerContext, profile_id: u64) {
    if ctx.db.player_data().profile_id().find(&profile_id).is_none() {
        ctx.db.player_data().insert(PlayerData {
            profile_id, level: 1, xp: 0, hp: BASE_MAX_HP as u32, max_hp: BASE_MAX_HP as u32, defense: 0,
            base_speed: BASE_SPEED,
        });
    }

    if ctx.db.player_inventory_slot().profile_id().filter(&profile_id).next().is_none() {
        let mut rows = vec![
            inventory_slot(profile_id, 0, SlotRole::Weapon, Some("Bow".to_string())), // 0: weapon
        ];
        for i in 1..=4u32 { rows.push(inventory_slot(profile_id, i, SlotRole::Hotbar, None)); } // 1-4: hotbar (consumable)
        rows.push(inventory_slot(profile_id, 5, SlotRole::Accessory, Some("Hat".to_string()))); // 5: accessory
        rows.push(inventory_slot(profile_id, 6, SlotRole::Armor, Some("Helmet".to_string()))); // 6: armor
        for i in 7..=30u32 { rows.push(inventory_slot(profile_id, i, SlotRole::General, None)); } // 7-30: general
        rows.push(inventory_slot(profile_id, 31, SlotRole::Bag, Some("Bag".to_string()))); // 31: bag
        for i in ABILITY_SLOT_START..ABILITY_SLOT_START + ABILITY_SLOT_COUNT { // 32-37: ability
            rows.push(inventory_slot(profile_id, i, SlotRole::Ability, None));
        }
        rows[1].item_id = Some("Bread".to_string());
        for row in rows {
            ctx.db.player_inventory_slot().insert(row);
        }
        // Pre-equip ability items so activation is testable immediately: Skull (a 1-cost
        // "artifact" ability) at 32, Tome of Mending spanning 33-35.
        if let Some(skull) = ctx.db.item().item_id().find(&"Skull".to_string()) {
            place_span(ctx, profile_id, ABILITY_SLOT_START, &skull, vec![]);
        }
        if let Some(tome) = ctx.db.item().item_id().find(&"Tome of Mending".to_string()) {
            place_span(ctx, profile_id, ABILITY_SLOT_START + 1, &tome, vec![]);
        }
    }

    if ctx.db.player_stat_allocation().profile_id().find(&profile_id).is_none() {
        // The auto-created "Knight" profile (client_connected) starts maxed for testing;
        // manually created profiles start at zero. Allocation is the input recompute_stats
        // reads, so these survive every later recompute (resolved = BASE_STAT + 999).
        let maxed = ctx.db.player_profile().profile_id().find(&profile_id)
            .map(|p| p.profile_name == "Knight")
            .unwrap_or(false);
        let points = if maxed { 999 } else { 0 };
        ctx.db.player_stat_allocation().insert(PlayerStatAllocation {
            profile_id, strength: points, wisdom: points, dexterity: points,
            damage_dealer: points, supporter: points, artisan: points, unspent_points: 0,
        });
    }

    if ctx.db.player_stats().profile_id().find(&profile_id).is_none() {
        recompute_stats(ctx, profile_id);
    }

    // Spawn is always at the world origin: leave_world keeps the position/chunk rows,
    // so without this reset a re-join would resume wherever the player last was.
    let cfg = MapConfig::load(ctx, 0, 0);
    let (spawn_cq, spawn_cr) = world_to_chunk(0.0, 0.0, cfg.chunk_hex_radius, cfg.hex_outer_radius);
    let spawn_chunk_index = spiral_chunk_index(spawn_cq, spawn_cr);
    if let Some(pos) = ctx.db.player_position().profile_id().find(&profile_id) {
        ctx.db.player_position().profile_id().update(PlayerPosition { x: 0.0, y: 0.0, movement_direction: 0.0, movement_speed: 0.0, chunk_index: spawn_chunk_index, ..pos });
    } else {
        ctx.db.player_position().insert(PlayerPosition { profile_id, player_id: ctx.sender(), x: 0.0, y: 0.0, movement_direction: 0.0, movement_speed: 0.0, chunk_index: spawn_chunk_index });
    }

    if ctx.db.player_rotation().profile_id().find(&profile_id).is_none() {
        let chunk_index = ctx.db.player_position().profile_id().find(&profile_id)
            .map(|p| p.chunk_index)
            .unwrap_or(0);
        ctx.db.player_rotation().insert(PlayerRotation { profile_id, player_id: ctx.sender(), screen_rotation: 0.0, chunk_index });
    }

    if let Some(chunk) = ctx.db.player_chunk().profile_id().find(&profile_id) {
        ctx.db.player_chunk().profile_id().update(PlayerChunk { chunk_q: spawn_cq, chunk_r: spawn_cr, ..chunk });
    } else {
        ctx.db.player_chunk().insert(PlayerChunk { profile_id, chunk_q: spawn_cq, chunk_r: spawn_cr });
    }
}

/// Tears down every per-profile row — the inverse of try_scaffold_profile, plus PlayerStats
/// (scaffolded lazily by recompute_stats) and any active consumable effects. This is the one
/// shared table list for profile cleanup; callers handle the identity-level rows themselves
/// (combat death keeps PlayerProfile and moves the player to LoggedOutPlayer; delete_profile
/// removes PlayerProfile while the player sits in the lobby).
pub fn teardown_profile(ctx: &ReducerContext, profile_id: u64) {
    ctx.db.player_data().profile_id().delete(profile_id);
    ctx.db.player_stats().profile_id().delete(profile_id);
    ctx.db.player_stat_allocation().profile_id().delete(profile_id);
    for slot in ctx.db.player_inventory_slot().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        ctx.db.player_inventory_slot().slot_id().delete(slot.slot_id);
    }
    ctx.db.player_position().profile_id().delete(profile_id);
    ctx.db.player_rotation().profile_id().delete(profile_id);
    ctx.db.player_chunk().profile_id().delete(profile_id);
    ctx.db.player_movement_state().profile_id().delete(profile_id);
    ctx.db.player_displacement_allowance().profile_id().delete(profile_id);
    ctx.db.player_hit_rate().profile_id().delete(profile_id);
    for active in ctx.db.active_consumable_effect().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        ctx.db.active_consumable_effect().effect_id().delete(active.effect_id);
    }
    crate::status::methods::purge_profile_statuses(ctx, profile_id);
    // A caster's ground zones die with them (P3) — the same purge leave_world and
    // client_disconnected run.
    crate::status::methods::purge_profile_zones(ctx, profile_id);
}

/// Player death cleanup: the single place that tears down a dead player's world rows
/// and moves them back to the lobby. Combat calls this on a killing blow instead of
/// orchestrating teardown_profile + the lobby move itself — death is player-domain, and
/// reducers stay transactional (no deferred death event that could split the kill).
/// player.player_id, not ctx.sender() — scheduled-reducer drains have no sender.
pub fn kill_player(ctx: &ReducerContext, player: &LoggedInPlayer) {
    teardown_profile(ctx, player.profile_id);
    ctx.db.logged_in_player().player_id().delete(&player.player_id);
    ctx.db.logged_out_player().insert(LoggedOutPlayer { player_id: player.player_id, username: player.username.clone(), is_admin: player.is_admin });
}

use crate::combat::heal_player;

pub fn apply_consumable_effect(ctx: &ReducerContext, profile_id: u64, consumable: &ConsumableBehavior) {
    match &consumable.effect {
        ConsumableEffect::Heal => {
            heal_player(ctx, profile_id, consumable.potency as u32);
        }
        ConsumableEffect::Buff(effect) => {
            ctx.db.active_consumable_effect().insert(ActiveConsumableEffect {
                effect_id: 0, profile_id, modifier: buff_to_modifier(effect), remaining: consumable.duration,
            });
            recompute_stats(ctx, profile_id);
        }
    }
}

pub fn internal_gain_xp(ctx: &ReducerContext, profile_id: u64, amount: u32) {
    let data = match ctx.db.player_data().profile_id().find(&profile_id) { Some(d) => d, None => return };
    let old_level = data.level;
    let new_xp = data.xp.saturating_add(amount);
    let new_level = compute_level(new_xp);
    // Heal by the level-derived max_hp increase on top of current HP, so leveling up while
    // hurt doesn't fully restore (or waste) the gained health. recompute_stats folds in any
    // item-derived Hp bonus and clamps hp to the final max_hp afterward.
    let heal = (new_level - old_level) * HP_PER_LEVEL as u32;
    let new_hp = data.hp + heal;
    ctx.db.player_data().profile_id().update(PlayerData { level: new_level, xp: new_xp, hp: new_hp, ..data });
    if new_level != old_level {
        if let Some(alloc) = ctx.db.player_stat_allocation().profile_id().find(&profile_id) {
            let gained = (new_level - old_level) * SKILL_POINTS_PER_LEVEL;
            ctx.db.player_stat_allocation().profile_id().update(PlayerStatAllocation {
                unspent_points: alloc.unspent_points + gained, ..alloc
            });
        }
        recompute_stats(ctx, profile_id);
    }
}

pub fn find_profile_by_name(ctx: &ReducerContext, player_id: spacetimedb::Identity, profile_name: &str) -> Option<PlayerProfile> {
    let lower = profile_name.to_lowercase();
    ctx.db.player_profile().player_id().filter(&player_id).find(|p| p.profile_name.to_lowercase() == lower)
}

/// Reducer-side AOI query: position rows of every *logged-in* player within (wrap-aware)
/// `radius` of (x, y), via the PlayerPosition.chunk_index btree. Ghost positions of
/// logged-out players are excluded (their rows outlive logout). Used by AoE ability
/// targeting (paladin/trickster ally buffs) and zone membership checks.
pub fn players_near_point(ctx: &ReducerContext, x: f32, y: f32, radius: f32) -> Vec<PlayerPosition> {
    let cfg = MapConfig::load(ctx, 0, 0);
    let lap_q = (cfg.lap_q_x, cfg.lap_q_y);
    let lap_r = (cfg.lap_r_x, cfg.lap_r_y);
    let radius_sq = radius * radius;
    chunk_indices_covering(x, y, radius, cfg.chunk_hex_radius, cfg.hex_outer_radius, cfg.chunk_cols, cfg.chunk_rows)
        .iter()
        .flat_map(|idx| ctx.db.player_position().chunk_index().filter(idx))
        .filter(|pos| ctx.db.logged_in_player().profile_id().find(&pos.profile_id).is_some())
        .filter(|pos| wrapped_distance_sq(x, y, pos.x, pos.y, lap_q, lap_r) <= radius_sq)
        .collect()
}

// ===== Slot helpers =====

/// Which items a slot accepts, derived from its role. General (backpack) accepts everything —
/// ability items stored there sit loose in a single cell; slot_cost only applies in the
/// ability region. Every other role accepts exactly its matching EquipSlot.
pub fn slot_allowed(role: SlotRole, equip_slot: EquipSlot) -> bool {
    match role {
        SlotRole::General => true,
        SlotRole::Weapon => equip_slot == EquipSlot::Weapon,
        SlotRole::Hotbar => equip_slot == EquipSlot::Consumable,
        SlotRole::Accessory => equip_slot == EquipSlot::Accessory,
        SlotRole::Armor => equip_slot == EquipSlot::Armor,
        SlotRole::Bag => equip_slot == EquipSlot::Bag,
        SlotRole::Ability => equip_slot == EquipSlot::Ability,
    }
}

pub fn get_slot(ctx: &ReducerContext, profile_id: u64, slot_index: u32) -> Option<PlayerInventorySlot> {
    // One row per (profile, slot_index) — the composite by_profile_slot index is a point read.
    ctx.db.player_inventory_slot().by_profile_slot().filter((profile_id, slot_index)).next()
}

pub fn update_slot(ctx: &ReducerContext, slot: PlayerInventorySlot) {
    ctx.db.player_inventory_slot().slot_id().update(slot);
}

/// Occupied (item-holding) head cells of a role — span followers are excluded.
pub fn ability_position_multiplier(slot_index: u32) -> f32 {
    let pos = slot_index.saturating_sub(ABILITY_SLOT_START) as usize;
    ABILITY_POSITION_MULTIPLIERS.get(pos).copied().unwrap_or(1.0)
}

/// First index of a contiguous run of `cost` free ability cells, or None. Free means no
/// item and not a span follower.
pub fn find_free_span(ctx: &ReducerContext, profile_id: u64, cost: u32) -> Option<u32> {
    if cost == 0 || cost > ABILITY_SLOT_COUNT { return None; }
    for start in ABILITY_SLOT_START..=ABILITY_SLOT_START + ABILITY_SLOT_COUNT - cost {
        let free = (start..start + cost).all(|i| {
            get_slot(ctx, profile_id, i).map(|s| s.item_id.is_none() && s.occupied_by.is_none()).unwrap_or(false)
        });
        if free { return Some(start); }
    }
    None
}

/// Writes an ability item into the span starting at `head_index`: the head holds the item,
/// the following `slot_cost - 1` cells become followers. Charges initialize from the item's
/// AbilityBehavior (max_charges 0 = unlimited). Callers must have validated the span is free.
pub fn place_span(ctx: &ReducerContext, profile_id: u64, head_index: u32, item: &Item, enchantment_ids: Vec<String>) {
    let cost = item.slot_cost.max(1);
    let charges = ability_behavior(item)
        .map(|a| if a.max_charges == 0 { u32::MAX } else { a.max_charges })
        .unwrap_or(u32::MAX);
    if let Some(head) = get_slot(ctx, profile_id, head_index) {
        update_slot(ctx, PlayerInventorySlot {
            item_id: Some(item.item_id.clone()), enchantment_ids, charges,
            cooldown_until: None, active_toggle: 0, occupied_by: None, ..head
        });
    }
    for i in head_index + 1..head_index + cost {
        if let Some(follower) = get_slot(ctx, profile_id, i) {
            update_slot(ctx, PlayerInventorySlot {
                item_id: None, enchantment_ids: vec![], occupied_by: Some(head_index),
                cooldown_until: None, charges: u32::MAX, active_toggle: 0, ..follower
            });
        }
    }
}

/// Clears the span headed at `head_index` (head + `slot_cost - 1` followers) back to empty.
pub fn clear_span(ctx: &ReducerContext, profile_id: u64, head_index: u32, slot_cost: u32) {
    for i in head_index..head_index + slot_cost.max(1) {
        if let Some(slot) = get_slot(ctx, profile_id, i) {
            update_slot(ctx, PlayerInventorySlot {
                item_id: None, enchantment_ids: vec![], occupied_by: None,
                cooldown_until: None, charges: u32::MAX, active_toggle: 0, ..slot
            });
        }
    }
}

/// Stat-requirement check for equipping (doc 03: classes emerge from gear locked behind
/// stat thresholds). Returns Err naming the first unmet requirement, Ok(()) when the
/// player's resolved stats meet every requirement (all-zero always passes).
pub fn check_stat_requirements(ctx: &ReducerContext, profile_id: u64, item: &Item) -> Result<(), String> {
    let req = &item.stat_requirements;
    let stats = match ctx.db.player_stats().profile_id().find(&profile_id) {
        Some(s) => s,
        None => return Err("Player stats not found.".to_string()),
    };
    let checks = [
        ("STR", req.strength, stats.strength),
        ("WIS", req.wisdom, stats.wisdom),
        ("DEX", req.dexterity, stats.dexterity),
        ("DPS", req.damage_dealer, stats.damage_dealer),
        ("SUP", req.supporter, stats.supporter),
        ("ART", req.artisan, stats.artisan),
    ];
    for (name, required, actual) in checks {
        if (actual as u32) < required {
            return Err(format!("Item '{}' requires {} {}.", item.item_id, name, required));
        }
    }
    Ok(())
}

/// The shared "player acquires an item" placement path, used by pickup_drop and
/// take_from_chest. Ability items auto-equip into the first contiguous free span if stat
/// requirements pass, else fall back to loose backpack storage; other items prefer a
/// matching typed slot (stat requirements gate equipped roles) and fall back to general.
/// `cooldown_until`/`charges` carry runtime state from the source (a dropped ability
/// keeps its residual cooldown — a different player picking it up inherits it);
/// None/u32::MAX means fresh (chest takes, ordinary items). Does not recompute stats —
/// the caller decides.
pub fn store_item_in_inventory(ctx: &ReducerContext, profile_id: u64, item: &Item, enchantment_ids: Vec<String>, cooldown_until: Option<spacetimedb::Timestamp>, charges: u32) -> Result<(), String> {
    if item.equip_slot == EquipSlot::Ability {
        if check_stat_requirements(ctx, profile_id, item).is_ok() {
            if let Some(head) = find_free_span(ctx, profile_id, item.slot_cost.max(1)) {
                place_span(ctx, profile_id, head, item, enchantment_ids);
                // Carried state overrides place_span's fresh initialization — restore
                // when the source had any, never initialize over it.
                if cooldown_until.is_some() || charges != u32::MAX {
                    if let Some(head_slot) = get_slot(ctx, profile_id, head) {
                        update_slot(ctx, PlayerInventorySlot { cooldown_until, charges, ..head_slot });
                    }
                }
                return Ok(());
            }
        }
    }

    let slots: Vec<PlayerInventorySlot> = ctx.db.player_inventory_slot().profile_id().filter(&profile_id).collect();
    let is_free = |s: &&PlayerInventorySlot| s.item_id.is_none() && s.occupied_by.is_none() && slot_allowed(s.role, item.equip_slot);
    // Prefer a matching typed slot, fall back to general. Ability-role cells are skipped —
    // ability items only enter the region through span placement, never loose. A typed
    // (equipped) slot also requires the item's stat requirements — otherwise backpack.
    let target = slots.iter().filter(|s| s.role != SlotRole::General && s.role != SlotRole::Ability).find(is_free)
        .filter(|_| check_stat_requirements(ctx, profile_id, item).is_ok())
        .or_else(|| slots.iter().filter(|s| s.role == SlotRole::General).find(is_free))
        .ok_or("No suitable slot available.")?;
    update_slot(ctx, PlayerInventorySlot {
        item_id: Some(item.item_id.clone()), enchantment_ids, cooldown_until, charges, ..target.clone()
    });
    Ok(())
}

/// The single inventory-transfer entry point for item acquisition: stores the item (with
/// carried runtime state) and re-resolves stats. Reducers outside the player module
/// (pickup_drop, take_from_chest) must go through this instead of pairing
/// store_item_in_inventory + recompute_stats by hand — forgetting the recompute silently
/// desyncs stats from equipment.
pub fn transfer_item_to_inventory(ctx: &ReducerContext, profile_id: u64, item: &Item, enchantment_ids: Vec<String>, cooldown_until: Option<spacetimedb::Timestamp>, charges: u32) -> Result<(), String> {
    store_item_in_inventory(ctx, profile_id, item, enchantment_ids, cooldown_until, charges)?;
    recompute_stats(ctx, profile_id);
    Ok(())
}
