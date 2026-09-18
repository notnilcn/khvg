//! Direct player trade + economy (Phase 3 — BitCraft `trade_initiate_session` +
//! `trade_session_state` + `trade_sessions_agent` pattern, translated).
//!
//! BitCraft guards: signed-in check, one-session-per-player, in-combat block,
//! max-pockets limit, distance validation, 45 s timeout + logout cleanup via an
//! agent. This ports that shape onto the target's inventory (`transfer_item_to_
//! inventory` both directions, atomically in one reducer) and INTERACT_RADIUS.

use spacetimedb::{table, Query, ReducerContext, SpacetimeType, Table, Timestamp, ViewContext};
use crate::main::global::*;
use crate::player::methods::{get_slot, is_admin, require_in_world, transfer_item_to_inventory};
use crate::player::tables::*;
use crate::player::tables::logged_in_player__view;
use crate::item::tables::item;

/// One offered pocket: an item id + quantity (quantity is informational — slots hold
/// one item each; the swap moves the slot contents).
#[derive(SpacetimeType, Clone, PartialEq)]
pub struct TradePocket {
    pub item_id: String,
    pub slot_index: u32,
    pub qty: u32,
}

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum TradeStatus {
    Offered,
    InitiatorAccepted,
    AcceptorAccepted,
    Resolved,
}

/// A two-party trade session (BitCraft `TradeSessionState`). Public so both parties
/// render it; the swap executes atomically through `transfer_item_to_inventory`
/// both directions in `confirm_swap`.
#[table(accessor = trade_session, public)]
#[derive(Clone)]
pub struct TradeSession {
    #[primary_key] #[auto_inc]
    pub session_id: u64,
    #[index(btree)]
    pub initiator_profile_id: u64,
    #[index(btree)]
    pub acceptor_profile_id: u64,
    pub status: TradeStatus,
    pub initiator_offer: Vec<TradePocket>,
    pub acceptor_offer: Vec<TradePocket>,
    pub updated_at: Timestamp,
    pub resolution_message: String,
}

fn active_session_for(ctx: &ReducerContext, profile_id: u64) -> Option<TradeSession> {
    ctx.db.trade_session().iter().find(|s| {
        s.status != TradeStatus::Resolved
            && (s.initiator_profile_id == profile_id || s.acceptor_profile_id == profile_id)
    })
}

fn parties_in_range(ctx: &ReducerContext, a: u64, b: u64) -> bool {
    let (Some(pa), Some(pb)) = (
        ctx.db.player_position().profile_id().find(&a),
        ctx.db.player_position().profile_id().find(&b),
    ) else { return false };
    let cfg = crate::world::tables::MapConfig::load(ctx, 0, 0);
    let d2 = crate::world::wrap::wrapped_distance_sq(
        pa.x, pa.y, pb.x, pb.y, (cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y),
    );
    d2 <= MAX_TRADE_DISTANCE * MAX_TRADE_DISTANCE
}

fn in_combat(ctx: &ReducerContext, profile_id: u64) -> bool {
    // Combat block (BitCraft ThreatState::in_combat): any live enemy aggroed onto a
    // nearby player counts. Cheap approximation — an enemy within aggro range of the
    // trader blocks the trade.
    let Some(pos) = ctx.db.player_position().profile_id().find(&profile_id) else { return false };
    !crate::enemy::methods::enemies_near_point(ctx, pos.x, pos.y, 300.0).is_empty()
}

#[spacetimedb::reducer]
pub fn trade_initiate(ctx: &ReducerContext, acceptor_profile_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let initiator = player.profile_id;
    if initiator == acceptor_profile_id {
        return Err("Cannot trade with yourself.".to_string());
    }
    ctx.db.logged_in_player().profile_id().find(&acceptor_profile_id).ok_or("Acceptor is not in the world.")?;
    if active_session_for(ctx, initiator).is_some() {
        return Err("You already have an active trade session.".to_string());
    }
    if active_session_for(ctx, acceptor_profile_id).is_some() {
        return Err("The other player is already trading.".to_string());
    }
    if in_combat(ctx, initiator) {
        return Err("Cannot trade while in combat.".to_string());
    }
    if !parties_in_range(ctx, initiator, acceptor_profile_id) {
        return Err("The other player is too far away.".to_string());
    }
    ctx.db.trade_session().insert(TradeSession {
        session_id: 0, initiator_profile_id: initiator, acceptor_profile_id,
        status: TradeStatus::Offered, initiator_offer: vec![], acceptor_offer: vec![],
        updated_at: ctx.timestamp, resolution_message: String::new(),
    });
    Ok(())
}

fn offer_mut(session: &mut TradeSession, profile_id: u64) -> Result<&mut Vec<TradePocket>, String> {
    if profile_id == session.initiator_profile_id { Ok(&mut session.initiator_offer) }
    else if profile_id == session.acceptor_profile_id { Ok(&mut session.acceptor_offer) }
    else { Err("Not a party to this session.".to_string()) }
}

#[spacetimedb::reducer]
pub fn trade_add_item(ctx: &ReducerContext, session_id: u64, slot_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let mut session = ctx.db.trade_session().session_id().find(&session_id).ok_or("Trade session not found.")?;
    if session.status == TradeStatus::Resolved {
        return Err("Session is resolved.".to_string());
    }
    let slot = get_slot(ctx, player.profile_id, slot_index).ok_or("Invalid slot index.")?;
    let item_id = slot.item_id.clone().ok_or("Slot is empty.")?;
    if slot.occupied_by.is_some() {
        return Err("Cannot offer a span follower cell.".to_string());
    }
    let offer = offer_mut(&mut session, player.profile_id)?;
    if offer.len() >= MAX_TRADED_POCKETS {
        return Err("Offer is full.".to_string());
    }
    if offer.iter().any(|p| p.slot_index == slot_index) {
        return Err("Slot already offered.".to_string());
    }
    offer.push(TradePocket { item_id, slot_index, qty: 1 });
    session.status = TradeStatus::Offered;
    session.updated_at = ctx.timestamp;
    ctx.db.trade_session().session_id().update(session);
    Ok(())
}

#[spacetimedb::reducer]
pub fn trade_remove_item(ctx: &ReducerContext, session_id: u64, slot_index: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let mut session = ctx.db.trade_session().session_id().find(&session_id).ok_or("Trade session not found.")?;
    if session.status == TradeStatus::Resolved {
        return Err("Session is resolved.".to_string());
    }
    let offer = offer_mut(&mut session, player.profile_id)?;
    let before = offer.len();
    offer.retain(|p| p.slot_index != slot_index);
    if offer.len() == before {
        return Err("Slot is not in the offer.".to_string());
    }
    session.status = TradeStatus::Offered;
    session.updated_at = ctx.timestamp;
    ctx.db.trade_session().session_id().update(session);
    Ok(())
}

#[spacetimedb::reducer]
pub fn trade_accept(ctx: &ReducerContext, session_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let mut session = ctx.db.trade_session().session_id().find(&session_id).ok_or("Trade session not found.")?;
    if session.status == TradeStatus::Resolved {
        return Err("Session is resolved.".to_string());
    }
    if !parties_in_range(ctx, session.initiator_profile_id, session.acceptor_profile_id) {
        session.resolution_message = "Trade cancelled: parties moved apart.".to_string();
        session.status = TradeStatus::Resolved;
        ctx.db.trade_session().session_id().update(session);
        return Err("The other player is too far away.".to_string());
    }
    let new_status = if player.profile_id == session.initiator_profile_id {
        match session.status {
            TradeStatus::AcceptorAccepted => {
                return confirm_swap(ctx, &mut session);
            }
            _ => TradeStatus::InitiatorAccepted,
        }
    } else if player.profile_id == session.acceptor_profile_id {
        match session.status {
            TradeStatus::InitiatorAccepted => {
                return confirm_swap(ctx, &mut session);
            }
            _ => TradeStatus::AcceptorAccepted,
        }
    } else {
        return Err("Not a party to this session.".to_string());
    };
    session.status = new_status;
    session.updated_at = ctx.timestamp;
    ctx.db.trade_session().session_id().update(session);
    Ok(())
}

/// Both parties accepted: atomically swap the offered slot contents both directions
/// through `transfer_item_to_inventory`-adjacent slot writes (one reducer = one
/// transaction, so partial swaps are impossible).
fn confirm_swap(ctx: &ReducerContext, session: &mut TradeSession) -> Result<(), String> {
    let a = session.initiator_profile_id;
    let b = session.acceptor_profile_id;
    // Snapshot the offered items first (reads before any writes).
    let mut a_items: Vec<(PlayerInventorySlot, crate::item::tables::Item)> = vec![];
    let mut b_items: Vec<(PlayerInventorySlot, crate::item::tables::Item)> = vec![];
    for pocket in session.initiator_offer.iter() {
        let slot = get_slot(ctx, a, pocket.slot_index).ok_or("Initiator slot missing.")?;
        if slot.item_id.as_deref() != Some(pocket.item_id.as_str()) {
            return Err("Initiator offer changed.".to_string());
        }
        let item = ctx.db.item().item_id().find(&pocket.item_id).ok_or("Item not found.")?;
        a_items.push((slot, item));
    }
    for pocket in session.acceptor_offer.iter() {
        let slot = get_slot(ctx, b, pocket.slot_index).ok_or("Acceptor slot missing.")?;
        if slot.item_id.as_deref() != Some(pocket.item_id.as_str()) {
            return Err("Acceptor offer changed.".to_string());
        }
        let item = ctx.db.item().item_id().find(&pocket.item_id).ok_or("Item not found.")?;
        b_items.push((slot, item));
    }
    // Clear sources, then place cross-wise. Plain single-cell placement: trade offers
    // only individual slots (span heads trade as their head cell; followers stay put
    // and are cleared by the head move only inside swap_slots — here we move the cell
    // contents, so multi-slot spans are rejected to avoid orphaning followers).
    for (slot, item) in a_items.iter() {
        if slot.role == crate::player::tables::SlotRole::Ability && item.slot_cost > 1 {
            return Err(format!("Item '{}' spans multiple slots and cannot be traded.", item.item_id));
        }
    }
    for (slot, item) in b_items.iter() {
        if slot.role == crate::player::tables::SlotRole::Ability && item.slot_cost > 1 {
            return Err(format!("Item '{}' spans multiple slots and cannot be traded.", item.item_id));
        }
    }
    for (slot, _) in a_items.iter() {
        crate::player::methods::update_slot(ctx, PlayerInventorySlot {
            item_id: None, enchantment_ids: vec![], cooldown_until: None,
            charges: u32::MAX, active_toggle: 0, ..slot.clone()
        });
    }
    for (slot, _) in b_items.iter() {
        crate::player::methods::update_slot(ctx, PlayerInventorySlot {
            item_id: None, enchantment_ids: vec![], cooldown_until: None,
            charges: u32::MAX, active_toggle: 0, ..slot.clone()
        });
    }
    for ((slot, item), pocket) in a_items.iter().zip(session.initiator_offer.iter()) {
        let _ = pocket;
        transfer_item_to_inventory(ctx, b, item, slot.enchantment_ids.clone(), slot.cooldown_until, slot.charges)?;
    }
    for ((slot, item), pocket) in b_items.iter().zip(session.acceptor_offer.iter()) {
        let _ = pocket;
        transfer_item_to_inventory(ctx, a, item, slot.enchantment_ids.clone(), slot.cooldown_until, slot.charges)?;
    }
    session.status = TradeStatus::Resolved;
    session.resolution_message = "Trade completed.".to_string();
    session.updated_at = ctx.timestamp;
    ctx.db.trade_session().session_id().update(session.clone());
    Ok(())
}

#[spacetimedb::reducer]
pub fn trade_decline(ctx: &ReducerContext, session_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let mut session = ctx.db.trade_session().session_id().find(&session_id).ok_or("Trade session not found.")?;
    if player.profile_id != session.initiator_profile_id
        && player.profile_id != session.acceptor_profile_id
        && !is_admin(ctx) {
        return Err("Not a party to this session.".to_string());
    }
    session.status = TradeStatus::Resolved;
    session.resolution_message = "Trade declined.".to_string();
    session.updated_at = ctx.timestamp;
    ctx.db.trade_session().session_id().update(session);
    Ok(())
}

/// Agent timeout pass (BitCraft `trade_sessions_agent`: 45 s timeout, logout
/// cleanup, resolved-row sweep). Called from the cleanup pass.
pub fn tick_trade_sessions(ctx: &ReducerContext) {
    let now_micros = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    let timeout_micros = (TRADE_SESSION_TIMEOUT_SECS * 1_000_000.0) as u128;
    for mut session in ctx.db.trade_session().iter().collect::<Vec<_>>() {
        if session.status == TradeStatus::Resolved {
            ctx.db.trade_session().session_id().delete(&session.session_id);
            continue;
        }
        let then = session.updated_at.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
        let timed_out = now_micros.saturating_sub(then) >= timeout_micros;
        let logged_out = ctx.db.logged_in_player().profile_id().find(&session.initiator_profile_id).is_none()
            || ctx.db.logged_in_player().profile_id().find(&session.acceptor_profile_id).is_none();
        let out_of_range = !parties_in_range(ctx, session.initiator_profile_id, session.acceptor_profile_id);
        // Combat cancel (BitCraft ThreatState::in_combat block on initiate + agent sweep).
        let in_combat_now = in_combat(ctx, session.initiator_profile_id) || in_combat(ctx, session.acceptor_profile_id);
        if timed_out || logged_out || out_of_range || in_combat_now {
            session.status = TradeStatus::Resolved;
            session.resolution_message = if timed_out { "Trade timed out.".to_string() }
            else if logged_out { "Trade cancelled: player logged out.".to_string() }
            else if in_combat_now { "Trade cancelled: combat started.".to_string() }
            else { "Trade cancelled: parties moved apart.".to_string() };
            session.updated_at = ctx.timestamp;
            ctx.db.trade_session().session_id().update(session);
        }
    }
}

#[spacetimedb::view(accessor = local_trade_session, public)]
fn local_trade_session(ctx: &ViewContext) -> impl Query<TradeSession> {
    let empty = ctx.from.trade_session().r#where(|s| s.session_id.eq(u64::MAX)).build();
    let profile_id = match ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        Some(p) => p.profile_id,
        None => return empty,
    };
    ctx.from.trade_session().r#where(move |s| {
        s.initiator_profile_id.eq(profile_id).or(s.acceptor_profile_id.eq(profile_id))
    }).build()
}
