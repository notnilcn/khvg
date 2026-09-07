//! The shared status-effect entry points. All targeting variants (self-cast, allies in a
//! radius, ground zones) funnel through apply_status_effect; all incoming player damage
//! funnels through intercept_incoming (called by combat::deal_damage_to_player_opts before
//! mitigation); all outgoing enemy damage funnels through the consults called by
//! combat::apply_damage_to_enemy. The 1 Hz tick lives here too — player/reducers.rs's
//! tick_status_effects calls tick() after the consumable expiry pass.

use spacetimedb::{ReducerContext, Table};
use crate::main::global::*;
use crate::world::tables::MapConfig;
use crate::world::wrap::wrapped_distance_sq;
use crate::player::tables::*;
use crate::player::methods::players_near_point;
use crate::enemy::def_tables::StunBehavior;
use crate::enemy::instance_tables::{enemy, Enemy};
use crate::enemy::methods::enemies_near_point;
use crate::main::time::seconds_since;
use crate::combat::{deal_damage_to_player_opts, heal_player, apply_damage_to_enemy, DamageRequest, SHARED_PORTION_OPTIONS, REDIRECTED_OPTIONS, BLEED_DRAIN_OPTIONS};
use crate::anticheat::tables::player_hit_flag;
use super::tables::*;

pub fn is_mark(kind: StatusEffectKind) -> bool {
    matches!(kind, StatusEffectKind::DamageToBleed | StatusEffectKind::StoreDamage)
}

/// The result of the incoming-damage intercept: the (possibly reduced/converted) raw
/// damage that proceeds to mitigation, and whether the hit was fully absorbed (invulnerable,
/// shield, or redirected) — absorbed hits apply no damage but have already been stamped.
pub struct IncomingVerdict {
    pub base_damage: u32,
    pub absorbed: bool,
}

/// The one shared application path. Uniqueness rules: one active row per (profile, kind)
/// — re-applying refreshes (replaces params/remaining) — and one active MARK per player:
/// casting any bleed/store mark replaces the existing one. Replacing a bleed mark dumps its
/// remaining pool instantly (no free shed); replacing a store mark forfeits the pool.
pub fn apply_status_effect(
    ctx: &ReducerContext,
    profile_id: u64,
    kind: StatusEffectKind,
    params: StatusParams,
    duration: f32,
    marked_enemy_id: u64,
    source_profile_id: u64,
) {
    for row in ctx.db.active_status_effect().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        let replaces = if is_mark(kind) { is_mark(row.kind) } else { row.kind == kind };
        if !replaces { continue; }
        if row.kind == StatusEffectKind::DamageToBleed && is_mark(kind) && row.pool >= 1.0 {
            if let Some(login) = ctx.db.logged_in_player().profile_id().find(&profile_id) {
                let _ = deal_damage_to_player_opts(ctx, &login, None, row.pool as u32, BLEED_DRAIN_OPTIONS);
                // The dump may have killed the player — teardown purged every status row.
                if ctx.db.logged_in_player().profile_id().find(&profile_id).is_none() { return; }
            }
        }
        ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
    }
    ctx.db.active_status_effect().insert(ActiveStatusEffect {
        effect_id: 0,
        profile_id,
        kind,
        params,
        pool: 0.0,
        counter: 0,
        progress: 0.0,
        violated: false,
        remaining: duration,
        marked_enemy_id,
        source_profile_id,
        last_event_at: ctx.timestamp,
    });
}

/// The one shared enemy-debuff application path. One row per (enemy_id, kind) — re-applying
/// refreshes (replaces magnitude/remaining/source). No-op when the enemy doesn't exist.
pub fn apply_enemy_status(
    ctx: &ReducerContext,
    enemy_id: u64,
    kind: EnemyStatusKind,
    magnitude: f32,
    duration: f32,
    source_profile_id: u64,
) {
    if ctx.db.enemy().enemy_id().find(&enemy_id).is_none() { return; }
    for row in ctx.db.active_enemy_status_effect().enemy_id().filter(&enemy_id).collect::<Vec<_>>() {
        if row.kind == kind {
            ctx.db.active_enemy_status_effect().effect_id().update(ActiveEnemyStatusEffect {
                magnitude, remaining: duration, source_profile_id, ..row
            });
            return;
        }
    }
    ctx.db.active_enemy_status_effect().insert(ActiveEnemyStatusEffect {
        effect_id: 0, enemy_id, kind, magnitude, remaining: duration, source_profile_id,
    });
}

fn enemy_status_row(ctx: &ReducerContext, enemy_id: u64, kind: EnemyStatusKind) -> Option<ActiveEnemyStatusEffect> {
    ctx.db.active_enemy_status_effect().enemy_id().filter(&enemy_id).find(|r| r.kind == kind)
}

/// Target-side consult (Normal mitigation only): ArmorBreak zeroes the enemy's flat defence.
pub fn enemy_defense(ctx: &ReducerContext, enemy_id: u64, base_defense: u32) -> i32 {
    if enemy_status_row(ctx, enemy_id, EnemyStatusKind::ArmorBreak).is_some() { 0 } else { base_defense as i32 }
}

/// Target-side consult (Normal mitigation only): Vulnerability multiplies the final damage.
pub fn enemy_vulnerability_mult(ctx: &ReducerContext, enemy_id: u64) -> f32 {
    enemy_status_row(ctx, enemy_id, EnemyStatusKind::Vulnerability).map(|r| 1.0 + r.magnitude).unwrap_or(1.0)
}

/// Movement consult: Slow scales displacement down; a Stunned row counts as a Slow of its
/// magnitude for Slowed templates and speeds the enemy up for Enraged ones. Pause templates
/// are frozen wholesale by the behavior tick, not here.
pub fn enemy_movement_mult(ctx: &ReducerContext, enemy_id: u64, stun_behavior: &StunBehavior) -> f32 {
    let mut mult = 1.0;
    if let Some(slow) = enemy_status_row(ctx, enemy_id, EnemyStatusKind::Slow) {
        mult *= 1.0 - slow.magnitude;
    }
    if let Some(stun) = enemy_status_row(ctx, enemy_id, EnemyStatusKind::Stunned) {
        match stun_behavior {
            StunBehavior::Slowed => mult *= 1.0 - stun.magnitude,
            StunBehavior::Enraged => mult *= 1.0 + stun.magnitude,
            StunBehavior::Pause => {}
        }
    }
    mult
}

/// The magnitude of the enemy's Stunned row, if it has one.
pub fn stunned_magnitude(ctx: &ReducerContext, enemy_id: u64) -> Option<f32> {
    enemy_status_row(ctx, enemy_id, EnemyStatusKind::Stunned).map(|r| r.magnitude)
}

/// Outgoing consult (attacker side), sibling to withhold_outgoing: the warrior's outgoing
/// bleed mark (DamageToBleed with params.outgoing) CONVERTS a hit on the marked enemy into
/// an enemy DoT (magnitude = damage/3 for 3s) instead of instant damage. Returns true when
/// the hit was converted (caller skips the hp subtraction and kill handling). Skipped for
/// DoT ticks themselves (DamageRequest::DOT_TICK) so the bleed can't re-convert into itself.
pub fn convert_outgoing_to_bleed(ctx: &ReducerContext, attacker: Option<u64>, enemy_id: u64, damage: u32) -> bool {
    let Some(profile_id) = attacker else { return false };
    if damage == 0 { return false; }
    for row in ctx.db.active_status_effect().profile_id().filter(&profile_id) {
        if row.kind == StatusEffectKind::DamageToBleed && row.params.outgoing && row.marked_enemy_id == enemy_id {
            apply_enemy_status(ctx, enemy_id, EnemyStatusKind::DoT, damage as f32 / 3.0, 3.0, profile_id);
            return true;
        }
    }
    false
}

/// Ally targeting for AoE casts: radius 0 means self-only, otherwise every logged-in
/// player within ally_radius of (x, y) — the caster included.
pub fn ally_targets(ctx: &ReducerContext, caster_profile_id: u64, x: f32, y: f32, ally_radius: f32) -> Vec<u64> {
    if ally_radius <= 0.0 { return vec![caster_profile_id]; }
    players_near_point(ctx, x, y, ally_radius).iter().map(|p| p.profile_id).collect()
}

/// DamageShare (P3): live zone membership, NOT a carried status. If the victim's position
/// row is inside a DamageShare zone's radius, the hit splits across the zone's LIVE
/// occupants (players_near_point): the victim takes damage/occupants and every OTHER
/// occupant takes the same share through the flagged damage path. skip_conversion on those
/// shared portions is the loop guard — a shared hit must never re-trigger the status
/// intercept (shields/redirect/share). Only splits when occupants > 1; with overlapping
/// share zones the FIRST zone found wins (they are not stacked). Returns the victim's share.
fn share_zone_damage(ctx: &ReducerContext, profile_id: u64, source_enemy_id: Option<u64>, damage: u32) -> u32 {
    if damage == 0 { return damage; }
    let Some(pos) = ctx.db.player_position().profile_id().find(&profile_id) else { return damage; };
    let cfg = MapConfig::load(ctx, 0, 0);
    let (lap_q, lap_r) = ((cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));
    let Some(zone) = ctx.db.ground_zone().iter().find(|z| {
        z.kind == ZoneKind::DamageShare
            && wrapped_distance_sq(pos.x, pos.y, z.x, z.y, lap_q, lap_r) <= z.radius * z.radius
    }) else { return damage; };
    let occupants = players_near_point(ctx, zone.x, zone.y, zone.radius);
    if occupants.len() <= 1 { return damage; }
    let share = damage / occupants.len() as u32;
    for occupant in occupants.iter().filter(|p| p.profile_id != profile_id) {
        if let Some(login) = ctx.db.logged_in_player().profile_id().find(&occupant.profile_id) {
            let _ = deal_damage_to_player_opts(ctx, &login, source_enemy_id, share, SHARED_PORTION_OPTIONS);
        }
    }
    share
}

/// Incoming-damage interception hook, evaluated in this FIXED order before mitigation:
/// Invulnerable → DamageShield (consumes itself) → RedirectToCaster (re-routes the raw hit
/// to the source paladin; redirected hits can't re-redirect) → zone DamageShare (P3: live
/// zone membership) → DamageReductionPct → DamageToBleed (only hits from the marked enemy:
/// conversion% to the pool, the rest proceeds). Every hit — even fully absorbed — stamps
/// the player's window rows (counter / last_event_at): a hit is a hit.
pub fn intercept_incoming(ctx: &ReducerContext, profile_id: u64, source_enemy_id: Option<u64>, base_damage: u32, skip_redirect: bool) -> IncomingVerdict {
    let rows: Vec<ActiveStatusEffect> = ctx.db.active_status_effect().profile_id().filter(&profile_id).collect();
    if rows.is_empty() {
        // No carried statuses — but DamageShare is positional (zone membership), not a row.
        return IncomingVerdict { base_damage: share_zone_damage(ctx, profile_id, source_enemy_id, base_damage), absorbed: false };
    }

    for row in rows.iter().filter(|r| matches!(r.kind, StatusEffectKind::DamageToBleed | StatusEffectKind::StoreDamage)) {
        ctx.db.active_status_effect().effect_id().update(ActiveStatusEffect {
            counter: row.counter + if row.kind == StatusEffectKind::StoreDamage { 1 } else { 0 },
            last_event_at: ctx.timestamp,
            ..*row
        });
    }

    let mut damage = base_damage;
    let mut absorbed = false;

    if rows.iter().any(|r| r.kind == StatusEffectKind::Invulnerable) {
        absorbed = true;
    } else if let Some(shield) = rows.iter().find(|r| r.kind == StatusEffectKind::DamageShield) {
        ctx.db.active_status_effect().effect_id().delete(&shield.effect_id);
        absorbed = true;
    } else if !skip_redirect {
        if let Some(redirect) = rows.iter().find(|r| r.kind == StatusEffectKind::RedirectToCaster) {
            if let Some(paladin) = ctx.db.logged_in_player().profile_id().find(&redirect.source_profile_id) {
                let _ = deal_damage_to_player_opts(ctx, &paladin, source_enemy_id, base_damage, REDIRECTED_OPTIONS);
                absorbed = true;
            }
        }
    }

    if !absorbed {
        // DamageShare (P3) — the documented slot between RedirectToCaster and
        // DamageReductionPct: the hit splits across the share zone's live occupants first,
        // then the victim's own share proceeds through reduction/conversion.
        damage = share_zone_damage(ctx, profile_id, source_enemy_id, damage);
        for row in rows.iter().filter(|r| r.kind == StatusEffectKind::DamageReductionPct) {
            damage = (damage as f32 * (1.0 - row.params.pct)).max(0.0) as u32;
        }
        for row in rows.iter().filter(|r| r.kind == StatusEffectKind::DamageToBleed && Some(r.marked_enemy_id) == source_enemy_id) {
            let to_pool = ((damage as f32 * row.params.conversion) as u32).min(damage);
            if to_pool > 0 {
                damage -= to_pool;
                ctx.db.active_status_effect().effect_id().update(ActiveStatusEffect { pool: row.pool + to_pool as f32, ..*row });
            }
        }
        // StoreIncoming: the POST-mitigation hit accumulates into the pool (the hit
        // itself still lands normally — this is a store, not a conversion). Pooling what
        // the player actually loses keeps 1 HP taken ≠ 10 stored.
        for row in rows.iter().filter(|r| matches!(r.kind, StatusEffectKind::StoreIncomingSmite | StatusEffectKind::StoreIncomingBash)) {
            let stored = crate::combat::compute_incoming_damage(ctx, profile_id, damage);
            ctx.db.active_status_effect().effect_id().update(ActiveStatusEffect { pool: row.pool + stored as f32, ..*row });
        }
    }

    IncomingVerdict { base_damage: damage, absorbed }
}

/// Outgoing consult (attacker side): the DamageDealtMult multiplier at deal-time. Applied
/// to the pre-mitigation amount so defence still eats proportionally.
pub fn outgoing_damage_mult(ctx: &ReducerContext, attacker: Option<u64>) -> f32 {
    let Some(profile_id) = attacker else { return 1.0 };
    // One row per (profile, kind) — see ActiveStatusEffect's uniqueness rule.
    ctx.db.active_status_effect().profile_id().filter(&profile_id)
        .find(|r| r.kind == StatusEffectKind::DamageDealtMult)
        .map(|r| r.params.mult)
        .unwrap_or(1.0)
}

/// Outgoing consult (attacker side): records damage dealt to `enemy_id`. Drives the
/// DamageToBleed DealDamage purge (sheds immediately once the threshold is met).
pub fn note_damage_dealt(ctx: &ReducerContext, attacker: Option<u64>, enemy_id: u64, damage: u32) {
    let Some(profile_id) = attacker else { return };
    for row in ctx.db.active_status_effect().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        if row.kind == StatusEffectKind::DamageToBleed
            && row.params.purge == PurgeKind::DealDamage
            && row.marked_enemy_id == enemy_id
        {
            let counter = row.counter + damage;
            if counter as f32 >= row.params.x {
                ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
            } else {
                ctx.db.active_status_effect().effect_id().update(ActiveStatusEffect { counter, ..row });
            }
        }
    }
}

/// Outgoing consult (attacker side): StoreDamage withhold. While the window is open, the
/// attacker's damage to the marked enemy accumulates into the pool INSTEAD of being dealt
/// (it is released at settlement, flat — already mitigated at hit time). Returns true when
/// the damage was withheld (caller skips the hp subtraction and kill handling).
pub fn withhold_outgoing(ctx: &ReducerContext, attacker: Option<u64>, enemy_id: u64, damage: u32) -> bool {
    let Some(profile_id) = attacker else { return false };
    if damage == 0 { return false; }
    for row in ctx.db.active_status_effect().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        if row.kind == StatusEffectKind::StoreDamage && row.marked_enemy_id == enemy_id && row.remaining > 0.0 {
            ctx.db.active_status_effect().effect_id().update(ActiveStatusEffect { pool: row.pool + damage as f32, ..row });
            return true;
        }
    }
    false
}

/// StoreDamage settlement at window expiry: release pool x multiplier at the marked enemy
/// via FlatRelease (no second defence subtraction, no vulnerability double-dip), then delete
/// the row. Marked-enemy death never reaches here — on_enemy_killed already forfeited it.
fn settle_store_mark(ctx: &ReducerContext, row: &ActiveStatusEffect, login: &LoggedInPlayer) {
    let multiplier = match row.params.wager {
        WagerKind::NoHits => {
            let mut m = if row.counter == 0 { 2.0 } else if row.counter <= row.params.max_hits { 1.0 } else { 0.0 };
            if m > 1.0 {
                // Corroborate the 0-hits claim against independent witnesses. counter == 0
                // means last_event_at is still the apply time, so flags after it fall inside
                // the window. Solo play has no witnesses — accepted residual exploit.
                let witnesses = ctx.db.player_hit_flag().hit_player().filter(&login.player_id)
                    .filter(|f| f.reported_at >= row.last_event_at)
                    .count() as u32;
                if witnesses >= NO_HIT_WITNESS_THRESHOLD { m = 1.0; }
            }
            m
        }
        // violated = ever left r2; progress == 1.0 = ever left r1 (stamped from report_movement).
        WagerKind::Radius => if row.violated { 0.0 } else if row.progress >= 1.0 { 1.0 } else { 2.0 },
        WagerKind::Damage => if row.pool >= row.params.x { 2.0 } else if row.pool >= row.params.z { 1.0 } else { 0.0 },
    };
    let payout = (row.pool * multiplier) as u32;
    if payout > 0 {
        if let Some(enemy) = ctx.db.enemy().enemy_id().find(&row.marked_enemy_id) {
            apply_damage_to_enemy(ctx, enemy, payout, 0, DamageRequest::FLAT_RELEASE, Some(row.profile_id), Some(login.player_id));
        }
    }
    ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
}

/// Kill consult: purge every status referencing the dead enemy (the OnKill purge for bleed
/// marks; store-damage wagers forfeit their unsettled pools) and every enemy-side debuff row
/// on it. Called by combat::apply_damage_to_enemy on kill.
pub fn on_enemy_killed(ctx: &ReducerContext, enemy_id: u64) {
    for row in ctx.db.active_status_effect().marked_enemy_id().filter(&enemy_id).collect::<Vec<_>>() {
        ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
    }
    for row in ctx.db.active_enemy_status_effect().enemy_id().filter(&enemy_id).collect::<Vec<_>>() {
        ctx.db.active_enemy_status_effect().effect_id().delete(&row.effect_id);
    }
}

/// Lifecycle purge — teardown_profile (death/profile delete), leave_world, and
/// client_disconnected all route here so no status can kill or tick a logged-out player.
/// A held ability charge (P4) vanishes with the session too — no cooldown is spent.
pub fn purge_profile_statuses(ctx: &ReducerContext, profile_id: u64) {
    for row in ctx.db.active_status_effect().profile_id().filter(&profile_id).collect::<Vec<_>>() {
        ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
    }
    ctx.db.ability_charge().profile_id().delete(&profile_id);
}

/// Zone purge, sibling to purge_profile_statuses: a caster's ground zones die with them.
/// The same three lifecycle sites (teardown_profile, leave_world, client_disconnected)
/// call both purges so no zone outlives its owner or keeps buffing a dead/logged-out
/// player's allies.
pub fn purge_profile_zones(ctx: &ReducerContext, profile_id: u64) {
    for row in ctx.db.ground_zone().owner_profile_id().filter(&profile_id).collect::<Vec<_>>() {
        ctx.db.ground_zone().zone_id().delete(&row.zone_id);
    }
}

/// The 1 Hz status tick: drains bleed pools (through the flagged damage path — never
/// direct HP writes, so death cleanup and HpFloor still apply), applies Regen, evaluates
/// the bleed purges, auto-releases timed-out ability charges (P4), and sweeps expired
/// rows. An expired bleed mark stops converting but keeps draining until its pool is
/// gone. Also ticks ground zones (P3, below).
pub fn tick(ctx: &ReducerContext) {
    let cfg = MapConfig::load(ctx, 0, 0);
    let lap_q = (cfg.lap_q_x, cfg.lap_q_y);
    let lap_r = (cfg.lap_r_x, cfg.lap_r_y);

    for row in ctx.db.active_status_effect().iter().collect::<Vec<_>>() {
        let Some(login) = ctx.db.logged_in_player().profile_id().find(&row.profile_id) else {
            ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
            continue;
        };
        let mut row = row;
        match row.kind {
            StatusEffectKind::DamageToBleed => {
                if row.pool >= 1.0 {
                    // Exponential drain with time-constant pool_seconds/3 (≈95% drained after
                    // pool_seconds), snapped to zero below 1.
                    let tau = (row.params.pool_seconds / 3.0).max(0.5);
                    let mut drain = (row.pool * (CONSUMABLE_EFFECT_TICK_SECONDS / tau)).max(1.0).min(row.pool);
                    if row.pool - drain < 1.0 { drain = row.pool; }
                    row.pool -= drain;
                    let _ = deal_damage_to_player_opts(ctx, &login, None, drain as u32, BLEED_DRAIN_OPTIONS);
                    // The drain may have killed the player — teardown purged this row.
                    if ctx.db.active_status_effect().effect_id().find(&row.effect_id).is_none() { continue; }
                }
                let purged = match row.params.purge {
                    PurgeKind::NoDamageTaken => seconds_since(ctx, row.last_event_at) >= BLEED_NO_DAMAGE_SECONDS,
                    PurgeKind::StayClose => {
                        let pos = ctx.db.player_position().profile_id().find(&row.profile_id);
                        let marked = ctx.db.enemy().enemy_id().find(&row.marked_enemy_id);
                        match (pos, marked) {
                            (Some(pos), Some(marked))
                                if wrapped_distance_sq(pos.x, pos.y, marked.x, marked.y, lap_q, lap_r) <= row.params.r1 * row.params.r1 =>
                            {
                                row.progress += CONSUMABLE_EFFECT_TICK_SECONDS;
                                row.progress >= BLEED_STAY_CLOSE_SECONDS
                            }
                            _ => false,
                        }
                    }
                    PurgeKind::DealDamage => row.counter as f32 >= row.params.x,
                    PurgeKind::OnKill => false, // handled by on_enemy_killed
                };
                if purged {
                    ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
                    continue;
                }
                row.remaining = (row.remaining - CONSUMABLE_EFFECT_TICK_SECONDS).max(0.0);
                if row.remaining <= 0.0 && row.pool < 1.0 {
                    ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
                } else {
                    ctx.db.active_status_effect().effect_id().update(row);
                }
            }
            StatusEffectKind::Regen => {
                heal_player(ctx, row.profile_id, row.params.per_second.max(0.0) as u32);
                row.remaining -= CONSUMABLE_EFFECT_TICK_SECONDS;
                if row.remaining <= 0.0 {
                    ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
                } else {
                    ctx.db.active_status_effect().effect_id().update(row);
                }
            }
            StatusEffectKind::StoreDamage => {
                row.remaining -= CONSUMABLE_EFFECT_TICK_SECONDS;
                if row.remaining <= 0.0 {
                    settle_store_mark(ctx, &row, &login);
                } else {
                    ctx.db.active_status_effect().effect_id().update(row);
                }
            }
            StatusEffectKind::StoreIncomingSmite => {
                row.remaining -= CONSUMABLE_EFFECT_TICK_SECONDS;
                if row.remaining <= 0.0 {
                    // Smite release: the stored pool bursts around the paladin (Normal
                    // mitigation at release). Plan deviation: centered on the
                    // paladin — a scheduled tick has no cursor; recasting releases early AT
                    // the cursor instead (see activate_ability).
                    if row.pool >= 1.0 {
                        if let Some(pos) = ctx.db.player_position().profile_id().find(&row.profile_id) {
                            for enemy in enemies_near_point(ctx, pos.x, pos.y, row.params.release_radius) {
                                apply_damage_to_enemy(ctx, enemy, row.pool as u32, 0, DamageRequest::NORMAL, Some(row.profile_id), Some(login.player_id));
                            }
                        }
                    }
                    ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
                } else {
                    ctx.db.active_status_effect().effect_id().update(row);
                }
            }
            StatusEffectKind::StoreIncomingBash => {
                row.remaining -= CONSUMABLE_EFFECT_TICK_SECONDS;
                if row.remaining <= 0.0 {
                    if !row.violated {
                        // First expiry: freeze the pool and re-arm — the next shield bash
                        // consumes it (P2). `violated` doubles as the armed flag.
                        ctx.db.active_status_effect().effect_id().update(ActiveStatusEffect {
                            violated: true, remaining: BASH_HOLD_SECONDS, ..row
                        });
                    } else {
                        ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
                    }
                } else {
                    ctx.db.active_status_effect().effect_id().update(row);
                }
            }
            _ => {
                row.remaining -= CONSUMABLE_EFFECT_TICK_SECONDS;
                if row.remaining <= 0.0 {
                    ctx.db.active_status_effect().effect_id().delete(&row.effect_id);
                } else {
                    ctx.db.active_status_effect().effect_id().update(row);
                }
            }
        }
    }

    // Ability charges (P4): auto-release at MAX_CHARGE_SECONDS — no free indefinite
    // SelfSlow. A scheduled tick has no cursor, so the release targets the player's own
    // position (a Teleport fizzles in place but still spends its cooldown).
    for charge in ctx.db.ability_charge().iter().collect::<Vec<_>>() {
        if seconds_since(ctx, charge.started_at) < MAX_CHARGE_SECONDS { continue; }
        if let Some(pos) = ctx.db.player_position().profile_id().find(&charge.profile_id) {
            let _ = super::reducers::internal_release_charge(ctx, charge.profile_id, pos.x, pos.y);
        } else {
            // No position row (mid-teardown) — just drop the charge; the lifecycle purge
            // removes the SelfSlow with every other status row.
            ctx.db.ability_charge().profile_id().delete(&charge.profile_id);
        }
    }

    // Enemy statuses: DoT ticks deal their magnitude through the one damage sink
    // (DamageRequest::DOT_TICK — per-tick defence applies, min 1, so DoT is weak vs
    // armour, and an outgoing bleed mark can't re-convert the tick into itself). XP credit goes
    // to the debuff's source profile; aggro to that player's identity only while they're
    // still logged in (a scheduled reducer has no meaningful sender). The same pass sweeps
    // expired rows and purges orphans — enemies killed by weapon fire skip this tick, so
    // on_enemy_killed can't be the only cleanup.
    for row in ctx.db.active_enemy_status_effect().iter().collect::<Vec<_>>() {
        let Some(enemy) = ctx.db.enemy().enemy_id().find(&row.enemy_id) else {
            ctx.db.active_enemy_status_effect().effect_id().delete(&row.effect_id);
            continue;
        };
        if row.kind == EnemyStatusKind::DoT {
            let amount = row.magnitude as u32;
            if amount > 0 {
                let aggro_from = ctx.db.logged_in_player().profile_id().find(&row.source_profile_id).map(|l| l.player_id);
                apply_damage_to_enemy(ctx, enemy, amount, 0, DamageRequest::DOT_TICK, Some(row.source_profile_id), aggro_from);
                // The tick may have killed the enemy — its debuff rows died with it.
                if ctx.db.enemy().enemy_id().find(&row.enemy_id).is_none() { continue; }
            }
        }
        let remaining = row.remaining - CONSUMABLE_EFFECT_TICK_SECONDS;
        if remaining <= 0.0 {
            ctx.db.active_enemy_status_effect().effect_id().delete(&row.effect_id);
        } else {
            ctx.db.active_enemy_status_effect().effect_id().update(ActiveEnemyStatusEffect { remaining, ..row });
        }
    }

    // Stagger decay (P2): the bar bleeds off on this same 1 Hz tick. A full scan filtered
    // to stagger > 0 is acceptable at current enemy counts.
    for enemy in ctx.db.enemy().iter().filter(|e| e.stagger > 0.0).collect::<Vec<_>>() {
        let stagger = (enemy.stagger - STAGGER_DECAY_PER_SECOND * CONSUMABLE_EFFECT_TICK_SECONDS).max(0.0);
        ctx.db.enemy().enemy_id().update(Enemy { stagger, ..enemy });
    }

    // Ground zones (P3). Membership is LIVE each tick — players_near_point /
    // enemies_near_point on the zone center, no carried occupant list. Ally zones re-apply
    // their status with ZONE_STATUS_REFRESH_SECONDS remaining so stepping out sheds the
    // effect within ~1.5s; enemy zones refresh their debuffs the same way. Burst kinds do
    // nothing per tick and fire ONCE at expiry. DamageShare has no per-tick effect — it is
    // consulted live by intercept_incoming. The same pass decrements `remaining` and
    // deletes expired rows.
    for zone in ctx.db.ground_zone().iter().collect::<Vec<_>>() {
        match zone.kind {
            ZoneKind::DebuffImmunity | ZoneKind::DamageReduction | ZoneKind::HpFloor | ZoneKind::DamageBuff => {
                let (kind, params) = match zone.kind {
                    ZoneKind::DebuffImmunity => (StatusEffectKind::DebuffImmune, StatusParams::none()),
                    ZoneKind::DamageReduction => (StatusEffectKind::DamageReductionPct, StatusParams { pct: zone.magnitude, ..StatusParams::none() }),
                    ZoneKind::HpFloor => (StatusEffectKind::HpFloor, StatusParams::none()),
                    _ => (StatusEffectKind::DamageDealtMult, StatusParams { mult: zone.magnitude, ..StatusParams::none() }),
                };
                for pos in players_near_point(ctx, zone.x, zone.y, zone.radius) {
                    apply_status_effect(ctx, pos.profile_id, kind, params, ZONE_STATUS_REFRESH_SECONDS, 0, zone.owner_profile_id);
                }
            }
            ZoneKind::HealOverTime => {
                for pos in players_near_point(ctx, zone.x, zone.y, zone.radius) {
                    heal_player(ctx, pos.profile_id, zone.magnitude.max(0.0) as u32);
                }
            }
            ZoneKind::EnemyDot | ZoneKind::EnemySlow | ZoneKind::EnemyVulnerability => {
                let kind = match zone.kind {
                    ZoneKind::EnemyDot => EnemyStatusKind::DoT,
                    ZoneKind::EnemySlow => EnemyStatusKind::Slow,
                    _ => EnemyStatusKind::Vulnerability,
                };
                for enemy in enemies_near_point(ctx, zone.x, zone.y, zone.radius) {
                    apply_enemy_status(ctx, enemy.enemy_id, kind, zone.magnitude, ZONE_STATUS_REFRESH_SECONDS, zone.owner_profile_id);
                }
            }
            ZoneKind::BurstHeal | ZoneKind::EnemyBurst | ZoneKind::DamageShare => {}
        }

        let remaining = zone.remaining - CONSUMABLE_EFFECT_TICK_SECONDS;
        if remaining > 0.0 {
            ctx.db.ground_zone().zone_id().update(GroundZone { remaining, ..zone });
            continue;
        }
        // Expiry: bursts fire once, then the row dies with every other expired zone.
        match zone.kind {
            ZoneKind::BurstHeal => {
                for pos in players_near_point(ctx, zone.x, zone.y, zone.radius) {
                    heal_player(ctx, pos.profile_id, zone.magnitude.max(0.0) as u32);
                }
            }
            ZoneKind::EnemyBurst => {
                // XP credit goes to the owner; aggro to their identity only while logged in
                // (a scheduled reducer has no meaningful sender) — same rule as DoT ticks.
                let aggro_from = ctx.db.logged_in_player().profile_id().find(&zone.owner_profile_id).map(|l| l.player_id);
                for enemy in enemies_near_point(ctx, zone.x, zone.y, zone.radius) {
                    apply_damage_to_enemy(ctx, enemy, zone.magnitude as u32, 0, DamageRequest::NORMAL, Some(zone.owner_profile_id), aggro_from);
                }
            }
            _ => {}
        }
        ctx.db.ground_zone().zone_id().delete(&zone.zone_id);
    }
}
