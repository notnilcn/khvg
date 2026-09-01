//! Shared combat pipeline: the single place where damage is computed, mitigated, and
//! applied, and where death side effects live. Reducers resolve base damage and delegate
//! here, so the player and enemy paths share one implementation — the server-side
//! counterpart of the client's HealthComponent/DamageComponent composition.
//!
//! Composition mapping: each per-entity table is a component (one concern per table) and
//! `profile_id`/`enemy_id` is the entity id joining its rows. Death/despawn teardown goes
//! through the shared archetype helpers — teardown_profile for players, despawn_enemy_archetype
//! for enemies — so there is exactly one table list per entity type.
//!
//! Damage model (Game Design Doc 02): defence is a FLAT per-bullet subtraction with
//! minimum damage 1, which is what makes many-weak-bullets patterns trade off against
//! few-strong-bullets ones. Weapon damage is per trigger pull, divided among the
//! pattern's bullets. True-damage enchantment bonuses apply after defence.

use spacetimedb::{ReducerContext, Identity, Table};
use std::time::Duration;
use crate::player::tables::{logged_in_player, logged_out_player, player_data, player_stats, LoggedInPlayer, LoggedOutPlayer, PlayerData};
use crate::player::methods::{internal_gain_xp, resolve_effective_weapon, teardown_profile};
use crate::enemy::def_tables::enemy_template;
use crate::enemy::instance_tables::{enemy, enemy_behavior, Enemy};
use crate::enemy::methods::{compute_phase, despawn_enemy_archetype, recompute_aggro};
use crate::status::tables::active_status_effect;

/// How `apply_damage_to_enemy` treats the target's flat defence and the enemy-status
/// consults (ArmorBreak zeroes defence, Vulnerability multiplies the final damage).
pub enum MitigationMode {
    /// Subtract the target's flat defence (minimum damage 1); ArmorBreak and Vulnerability
    /// on the target apply.
    Normal,
    /// No defence subtraction and NO enemy-status consults — the amount was already
    /// mitigated when it was first captured (store-damage release), so mitigating again
    /// would double-dip.
    FlatRelease,
}

/// Options for the enemy damage path. DoT ticks call with DOT_TICK_OPTIONS: the outgoing
/// bleed conversion consult is skipped so a Bloodbath DoT can't re-convert its own tick
/// into a fresh (decaying) DoT forever instead of ever landing, and the StoreDamage
/// withhold consult is skipped so a caster's own DoT ticks aren't withheld into their own
/// store pool.
pub struct EnemyDamageOptions {
    /// Skip the outgoing-bleed conversion consult (DamageToBleed with params.outgoing).
    pub skip_outgoing_bleed: bool,
    /// Skip the StoreDamage withhold consult.
    pub skip_outgoing_withhold: bool,
}

pub const DEFAULT_ENEMY_OPTIONS: EnemyDamageOptions = EnemyDamageOptions { skip_outgoing_bleed: false, skip_outgoing_withhold: false };
/// Enemy DoT ticks: bypass the outgoing-bleed conversion and the StoreDamage withhold,
/// everything else applies.
pub const DOT_TICK_OPTIONS: EnemyDamageOptions = EnemyDamageOptions { skip_outgoing_bleed: true, skip_outgoing_withhold: true };

/// Outgoing weapon damage split at the mitigation seam: `base` is pre-defence (defence is
/// subtracted by `apply_damage_to_enemy` so all damage paths share one mitigation site),
/// `post_mitigation` is the true-damage bonus defence must not touch (added after).
pub struct ComputedDamage {
    pub base: u32,
    pub post_mitigation: u32,
}

/// Outgoing player damage per reported bullet hit: the effective weapon's per-trigger
/// damage divided among its bullets (toggle + enchantment composition already applied by
/// resolve_effective_weapon), scaled by strength. Defence is NOT subtracted here — that
/// happens in apply_damage_to_enemy so weapon, spell, DoT, and zone damage all share the
/// same mitigation (and future enemy-status) consult points.
pub fn compute_player_damage(ctx: &ReducerContext, profile_id: u64) -> ComputedDamage {
    let stats = match ctx.db.player_stats().profile_id().find(&profile_id) { Some(s) => s, None => return ComputedDamage { base: 1, post_mitigation: 0 } };
    let weapon = match resolve_effective_weapon(ctx, profile_id) { Some(w) => w, None => return ComputedDamage { base: 0, post_mitigation: 0 } };
    let raw = weapon.behavior.damage as f32
        * (1.0 + stats.strength as f32 * 0.002)
        / weapon.behavior.shot_count.max(1) as f32;
    ComputedDamage {
        base: (raw + weapon.flat_bullet_mod).max(0.0) as u32,
        post_mitigation: (weapon.true_flat + raw * weapon.true_pct) as u32,
    }
}

/// Incoming player damage after the player's flat defence (minimum 1).
pub fn compute_incoming_damage(ctx: &ReducerContext, profile_id: u64, base_damage: u32) -> u32 {
    let data = match ctx.db.player_data().profile_id().find(&profile_id) { Some(d) => d, None => return base_damage.max(1) };
    (base_damage as i32 - data.defense).max(1) as u32
}

/// Options for the internal damage path. Bleed-pool drains call with BLEED_DRAIN_OPTIONS:
/// no re-conversion of their own ticks, but HpFloor still applies and death cleanup runs.
/// No direct HP writes anywhere — death cleanup lives only in this function.
pub struct IncomingDamageOptions {
    /// Skip the whole status intercept (invulnerable/shield/redirect/reduction/bleed
    /// conversion) for this hit — used by the bleed pool's own drain ticks.
    pub skip_conversion: bool,
    /// Whether the HpFloor post-mitigation clamp applies.
    pub respect_floor: bool,
    /// Whether RedirectToCaster consults run — redirected hits pass true so a redirect
    /// chain can never loop.
    pub skip_redirect: bool,
}

pub const DEFAULT_INCOMING_OPTIONS: IncomingDamageOptions = IncomingDamageOptions { skip_conversion: false, respect_floor: true, skip_redirect: false };
/// Bleed-pool drains: bypass the status intercept and redirect, still respect the floor.
pub const BLEED_DRAIN_OPTIONS: IncomingDamageOptions = IncomingDamageOptions { skip_conversion: true, respect_floor: true, skip_redirect: true };

/// Applies damage to a player: status intercept, mitigates, subtracts, and on zero performs
/// the full death cleanup (drops the profile's rows and moves the player back to the lobby).
/// `source_enemy_id` attributes the hit (bleed marks only convert hits from their mark).
pub fn deal_damage_to_player(ctx: &ReducerContext, player: &LoggedInPlayer, source_enemy_id: Option<u64>, base_damage: u32) -> Result<(), String> {
    deal_damage_to_player_opts(ctx, player, source_enemy_id, base_damage, DEFAULT_INCOMING_OPTIONS)
}

/// The internal flags path behind deal_damage_to_player. The status module's
/// intercept_incoming hook (Invulnerable → DamageShield → RedirectToCaster → DamageShare →
/// DamageReductionPct → DamageToBleed) runs before mitigation unless
/// `opts.skip_conversion`; the HpFloor clamp applies post-mitigation when
/// `opts.respect_floor`.
pub fn deal_damage_to_player_opts(ctx: &ReducerContext, player: &LoggedInPlayer, source_enemy_id: Option<u64>, base_damage: u32, opts: IncomingDamageOptions) -> Result<(), String> {
    let profile_id = player.profile_id;
    let base_damage = if opts.skip_conversion {
        base_damage
    } else {
        let verdict = crate::status::methods::intercept_incoming(ctx, profile_id, source_enemy_id, base_damage, opts.skip_redirect);
        if verdict.absorbed {
            return Ok(());
        }
        verdict.base_damage
    };
    let damage = compute_incoming_damage(ctx, profile_id, base_damage);
    let data = ctx.db.player_data().profile_id().find(&profile_id).ok_or("Player data not found.")?;
    let mut new_hp = data.hp.saturating_sub(damage);
    if new_hp == 0 && opts.respect_floor
        && ctx.db.active_status_effect().profile_id().filter(&profile_id).any(|r| r.kind == crate::status::tables::StatusEffectKind::HpFloor)
    {
        new_hp = 1;
    }
    if new_hp == 0 {
        teardown_profile(ctx, profile_id);
        // player.player_id, not ctx.sender() — scheduled-reducer drains have no sender.
        ctx.db.logged_in_player().player_id().delete(&player.player_id);
        ctx.db.logged_out_player().insert(LoggedOutPlayer { player_id: player.player_id, username: player.username.clone(), is_admin: player.is_admin });
        Ok(())
    } else {
        ctx.db.player_data().profile_id().update(PlayerData { hp: new_hp, ..data });
        Ok(())
    }
}

/// The weapon-path wrapper: computes the attacker's effective-weapon damage and delegates
/// to apply_damage_to_enemy with full mitigation, xp credit for the attacker, and re-aggro
/// onto the caller.
pub fn deal_damage_to_enemy(ctx: &ReducerContext, profile_id: u64, enemy: Enemy) {
    deal_damage_to_enemy_bonus(ctx, profile_id, enemy, 0)
}

/// deal_damage_to_enemy with a flat PRE-mitigation bonus riding the same hit — the armed
/// StoreIncomingBash pool on a shield bash (the pool accumulates post-mitigation, then
/// merges into the weapon hit's base before defence rather than landing as a second
/// mitigated hit).
pub fn deal_damage_to_enemy_bonus(ctx: &ReducerContext, profile_id: u64, enemy: Enemy, bonus: u32) {
    let damage = compute_player_damage(ctx, profile_id);
    apply_damage_to_enemy(ctx, enemy, damage.base + bonus, damage.post_mitigation, MitigationMode::Normal, Some(profile_id), Some(ctx.sender()))
}

/// The one enemy damage sink — weapon hits, spells, DoT ticks, zone damage, and store-damage
/// releases all come through here. Subtracts defence per `mitigation`, adds the
/// post-mitigation bonus, and on kill despawns the enemy (and its behavior rows) and awards
/// xp to `xp_credit`. On survival, recomputes the phase from hp thresholds and re-aggros
/// onto `aggro_from` (skipped when None — scheduled-reducer ticks have no meaningful sender).
/// Normal mitigation consults the target's enemy statuses (ArmorBreak → defence 0,
/// Vulnerability → final damage ×(1+magnitude)); FlatRelease skips both.
pub fn apply_damage_to_enemy(
    ctx: &ReducerContext,
    enemy: Enemy,
    amount: u32,
    post_mitigation_bonus: u32,
    mitigation: MitigationMode,
    xp_credit: Option<u64>,
    aggro_from: Option<Identity>,
) {
    apply_damage_to_enemy_opts(ctx, enemy, amount, post_mitigation_bonus, mitigation, xp_credit, aggro_from, DEFAULT_ENEMY_OPTIONS)
}

/// The flagged path behind apply_damage_to_enemy — see EnemyDamageOptions.
pub fn apply_damage_to_enemy_opts(
    ctx: &ReducerContext,
    enemy: Enemy,
    amount: u32,
    post_mitigation_bonus: u32,
    mitigation: MitigationMode,
    xp_credit: Option<u64>,
    aggro_from: Option<Identity>,
    opts: EnemyDamageOptions,
) {
    let template = ctx.db.enemy_template().template_id().find(&enemy.template_id);
    let base_defense = template.as_ref().map(|t| t.defense).unwrap_or(0);
    // Target-side enemy-status consults (Normal only — FlatRelease was already mitigated at
    // capture and must not double-dip): ArmorBreak zeroes defence, Vulnerability multiplies
    // the final damage.
    let (defense, vulnerability_mult) = match mitigation {
        MitigationMode::Normal => (
            crate::status::methods::enemy_defense(ctx, enemy.enemy_id, base_defense),
            crate::status::methods::enemy_vulnerability_mult(ctx, enemy.enemy_id),
        ),
        MitigationMode::FlatRelease => (base_defense as i32, 1.0),
    };
    // Attacker-side status consult: DamageDealtMult applies at deal-time, pre-mitigation.
    let amount = (amount as f32 * crate::status::methods::outgoing_damage_mult(ctx, xp_credit)) as u32;
    // amount == 0 stays 0 (the no-weapon case deals no damage rather than the min-1 floor).
    let base = match mitigation {
        MitigationMode::Normal if amount > 0 => (amount as i32 - defense).max(1) as u32,
        _ => amount,
    };
    let damage = ((base + post_mitigation_bonus) as f32 * vulnerability_mult) as u32;
    // Attacker-side status consults on the final damage: StoreDamage withholds it into the
    // pool (released flat at settlement — already mitigated here); an outgoing bleed mark
    // converts it into an enemy DoT instead; otherwise record it for the DamageToBleed
    // DealDamage purge progress.
    if !opts.skip_outgoing_withhold
        && crate::status::methods::withhold_outgoing(ctx, xp_credit, enemy.enemy_id, damage)
    {
        return;
    }
    if !opts.skip_outgoing_bleed
        && crate::status::methods::convert_outgoing_to_bleed(ctx, xp_credit, enemy.enemy_id, damage)
    {
        return;
    }
    crate::status::methods::note_damage_dealt(ctx, xp_credit, enemy.enemy_id, damage);
    let new_hp = enemy.hp.saturating_sub(damage);

    if new_hp == 0 {
        let xp_reward = template.as_ref().map(|t| t.max_hp / 10).unwrap_or(10);
        despawn_enemy_archetype(ctx, &enemy);
        crate::status::methods::on_enemy_killed(ctx, enemy.enemy_id);
        if let Some(profile_id) = xp_credit {
            internal_gain_xp(ctx, profile_id, xp_reward);
        }
        return;
    }

    let new_phase = template.as_ref().map(|t| compute_phase(new_hp, t.max_hp, &t.phases)).unwrap_or(enemy.phase);
    let aggro_lock_seconds = template.as_ref().map(|t| t.aggro_lock_seconds).unwrap_or(0.0);

    if let Some(mut behavior) = ctx.db.enemy_behavior().behavior_id().find(&enemy.behavior_id) {
        let mut changed = false;
        if new_phase != enemy.phase {
            let (aggro_target, aggro_locked_until) = recompute_aggro(ctx, enemy.x, enemy.y, aggro_lock_seconds);
            behavior.aggro_target = aggro_target;
            behavior.aggro_locked_until = aggro_locked_until;
            changed = true;
        } else if let Some(from) = aggro_from {
            if behavior.aggro_target != Some(from) && ctx.timestamp >= behavior.aggro_locked_until {
                behavior.aggro_target = Some(from);
                behavior.aggro_locked_until = ctx.timestamp + Duration::from_secs_f32(aggro_lock_seconds.max(0.0));
                changed = true;
            }
        }
        if changed {
            ctx.db.enemy_behavior().behavior_id().update(behavior);
        }
    }

    ctx.db.enemy().enemy_id().update(Enemy { hp: new_hp, phase: new_phase, ..enemy });
}

/// Heals a player, clamped to max_hp. No-op if the player has no data row.
pub fn heal_player(ctx: &ReducerContext, profile_id: u64, amount: u32) {
    if let Some(data) = ctx.db.player_data().profile_id().find(&profile_id) {
        let new_hp = (data.hp + amount).min(data.max_hp);
        ctx.db.player_data().profile_id().update(PlayerData { hp: new_hp, ..data });
    }
}
