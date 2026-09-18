//! Agent tick framework (Phase 0A — BitCraft `agents/mod.rs` pattern, translated).
//!
//! BitCraft gates every background agent on `should_run(ctx)` → `Config.agents_enabled`,
//! registers all timer rows in one `init()` called after seeds, and retunes periods via
//! an admin `update_scheduled_timers_from_static_data` reducer.
//!
//! Target mapping: the module currently has exactly one 1 Hz schedule
//! (`ConsumableEffectSchedule` → `tick_status_effects` → `status::methods::tick`).
//! This module adds the master switch + registration entry point and splits the tick
//! body into per-concern passes (`tick_regen`, `tick_zones`, `tick_cleanup`), each
//! early-returning on `should_run`. Periods live in `main/global.rs` (mirroring
//! BitCraft's `parameters_desc.player_regen_tick_millis`).

use spacetimedb::{table, ReducerContext, Table};
use crate::main::global::*;
use crate::player::methods::is_admin;
use crate::player::tables::consumable_effect_schedule;

/// Master switch row (BitCraft `Config.agents_enabled`). Single-row table keyed by a
/// fixed id so `should_run` is one PK lookup.
#[table(accessor = agent_config)]
pub struct AgentConfig {
    #[primary_key]
    pub id: u8,
    pub agents_enabled: bool,
}

/// BitCraft `agents::should_run` — false when the config row is missing (fail closed).
pub fn should_run(ctx: &ReducerContext) -> bool {
    ctx.db.agent_config().id().find(&0).map(|c| c.agents_enabled).unwrap_or(false)
}

/// BitCraft `agents::init` — called from `main/lifecycle.rs::init` after seeds.
/// Ensures the master-switch row and the 1 Hz schedule row exist, then logs.
/// Idempotent: re-publish / re-init never inserts duplicate timer rows.
pub fn init_agents(ctx: &ReducerContext) {
    if ctx.db.agent_config().id().find(&0).is_none() {
        ctx.db.agent_config().insert(AgentConfig { id: 0, agents_enabled: AGENTS_ENABLED_DEFAULT });
    }
    // The lifecycle init already inserts the ConsumableEffectSchedule row; only fill
    // the gap if it is missing (e.g. row was deleted to cancel ticks).
    if ctx.db.consumable_effect_schedule().iter().next().is_none() {
        ctx.db.consumable_effect_schedule().insert(
            crate::player::tables::ConsumableEffectSchedule {
                scheduled_id: 0,
                scheduled_at: spacetimedb::ScheduleAt::Interval(spacetimedb::TimeDuration::from(
                    std::time::Duration::from_secs_f32(CONSUMABLE_EFFECT_TICK_SECONDS),
                )),
            },
        );
    }
    log::info!(
        "Initialized agents (enabled={}, regen={}s, zones={}s, cleanup={}s)",
        AGENTS_ENABLED_DEFAULT,
        AGENT_REGEN_TICK_SECONDS,
        AGENT_ZONE_TICK_SECONDS,
        AGENT_CLEANUP_TICK_SECONDS,
    );
}

/// Regen pass of the 1 Hz tick (BitCraft `player_regen_agent` cadence slot).
/// Actual regen math lives in `status::methods::tick`; this is the per-concern
/// dispatch point so future work can give regen its own schedule row.
pub fn tick_regen(ctx: &ReducerContext) {
    if !should_run(ctx) {
        return;
    }
    crate::status::methods::tick_regen(ctx);
}

/// Zone pass of the 1 Hz tick (GroundZone membership refresh slot).
pub fn tick_zones(ctx: &ReducerContext) {
    if !should_run(ctx) {
        return;
    }
    crate::status::methods::tick_zones(ctx);
}

/// Cleanup pass: trade timeouts, chat retention, upkeep, strike sweeps.
/// Runs on the slower cadence slot; the dispatcher calls it every tick and the
/// function itself rate-limits by wall clock would be ideal — for now it runs the
/// cheap sweeps each tick (all are bounded scans with early-outs).
pub fn tick_cleanup(ctx: &ReducerContext) {
    if !should_run(ctx) {
        return;
    }
    crate::trade::tick_trade_sessions(ctx);
    crate::chat::tick_chat_cleanup(ctx);
    crate::claims::tick_upkeep(ctx);
    crate::anticheat::sweep_strikes(ctx);
}

/// BitCraft `update_scheduled_timers_from_static_data` — admin-only. Periods are
/// compile-time consts in this target (no parameters_desc table), so this re-asserts
/// the schedule rows exist and logs the effective periods.
#[spacetimedb::reducer]
pub fn update_agent_timers(ctx: &ReducerContext) -> Result<(), String> {
    if !is_admin(ctx) {
        return Err("Admin only.".to_string());
    }
    log::info!(
        "Updating agent timers (regen={}s, zones={}s, cleanup={}s, trade={}s, chat={}s)",
        AGENT_REGEN_TICK_SECONDS,
        AGENT_ZONE_TICK_SECONDS,
        AGENT_CLEANUP_TICK_SECONDS,
        AGENT_TRADE_TICK_SECONDS,
        AGENT_CHAT_CLEANUP_TICK_SECONDS,
    );
    init_agents(ctx);
    Ok(())
}

/// Admin toggle for the master switch (lets operators pause background sim without
/// deleting timer rows).
#[spacetimedb::reducer]
pub fn set_agents_enabled(ctx: &ReducerContext, enabled: bool) -> Result<(), String> {
    if !is_admin(ctx) {
        return Err("Admin only.".to_string());
    }
    match ctx.db.agent_config().id().find(&0) {
        Some(row) => {
            ctx.db.agent_config().id().update(AgentConfig { agents_enabled: enabled, ..row });
        }
        None => {
            ctx.db.agent_config().insert(AgentConfig { id: 0, agents_enabled: enabled });
        }
    }
    log::info!("Agents enabled set to {} by {:?}", enabled, ctx.sender());
    Ok(())
}
