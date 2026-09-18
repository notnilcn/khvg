//! Guild-territory-lite claims / permissions (Phase 4 — BitCraft `claim_helper` +
//! `permission_helper` pattern, scoped down).
//!
//! BitCraft has full claims (members/tiles/tech/treasury + rent/decay agents). The
//! target has no ownership today (`place_building` is admin-only, chests are open),
//! so this ports the smallest useful slice: a `Territory` row (owner + tile list +
//! members + per-member permission bits), a `can_interact` helper mirroring
//! `can_interact_with_tile` (no claim → allow; inside → member/permission check),
//! enforcement in `take_from_chest` / `place_building`, and an optional upkeep tick
//! (treasury counter + dissolve on unpaid, mirroring rent_collector/building_decay).

use spacetimedb::{table, Query, ReducerContext, SpacetimeType, Table, Timestamp, ViewContext};
use crate::main::global::*;
use crate::player::methods::{is_admin, require_in_world};
use crate::world::building::{building_state, project_site};

/// Permission bits per member (BitCraft `ClaimPermission::{Inventory,Build,Usage}`).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum TerritoryPermission {
    Inventory,
    Build,
    Usage,
}

/// A claimed territory: owner + hex-tile footprint + members + per-member
/// permissions + upkeep treasury. Tiles are encoded as packed (q, r) pairs in
/// `tiles_q`/`tiles_r` (parallel vecs — SpacetimeType vecs of tuples are awkward).
#[table(accessor = territory, public)]
pub struct Territory {
    #[primary_key] #[auto_inc]
    pub territory_id: u64,
    pub owner_profile_id: u64,
    pub tiles_q: Vec<i32>,
    pub tiles_r: Vec<i32>,
    pub members: Vec<u64>,
    pub member_permissions: Vec<u8>,
    pub treasury: u32,
    pub last_upkeep_at: Timestamp,
}

fn has_permission(row: &Territory, profile_id: u64, perm: TerritoryPermission) -> bool {
    if profile_id == row.owner_profile_id {
        return true;
    }
    let bit = match perm {
        TerritoryPermission::Inventory => 0,
        TerritoryPermission::Build => 1,
        TerritoryPermission::Usage => 2,
    };
    row.members.iter().position(|m| *m == profile_id)
        .and_then(|i| row.member_permissions.get(i))
        .map(|bits| bits & (1 << bit) != 0)
        .unwrap_or(false)
}

fn territory_at_tile(ctx: &ReducerContext, q: i32, r: i32) -> Option<Territory> {
    ctx.db.territory().iter().find(|t| {
        t.tiles_q.iter().zip(t.tiles_r.iter()).any(|(tq, tr)| *tq == q && *tr == r)
    })
}

/// Hex entry point for building footprints (BitCraft `get_claim_under_footprint`
/// helper): which territory, if any, owns this hex. `None` = wilderness.
pub fn territory_id_at_hex(ctx: &ReducerContext, q: i32, r: i32) -> Option<u64> {
    territory_at_tile(ctx, q, r).map(|t| t.territory_id)
}

/// BitCraft `can_interact_with_tile` mirror: no claim → allow; inside → owner or
/// member-with-permission. World-position entry point (hex lookup via world_to_hex).
pub fn can_interact_pos(ctx: &ReducerContext, profile_id: u64, x: f32, y: f32, perm: TerritoryPermission) -> Result<(), String> {
    let cfg = crate::world::tables::MapConfig::load(ctx, 0, 0);
    let (q, r) = crate::world::hex::world_to_hex(x, y, cfg.hex_outer_radius);
    match territory_at_tile(ctx, q, r) {
        None => Ok(()),
        Some(t) => {
            if has_permission(&t, profile_id, perm) { Ok(()) }
            else { Err("This territory is claimed — no permission.".to_string()) }
        }
    }
}

/// Inventory-permission consult used by `validate_interact` (chests/pickups).
pub fn check_interact(ctx: &ReducerContext, profile_id: u64, x: f32, y: f32) -> Result<(), String> {
    can_interact_pos(ctx, profile_id, x, y, TerritoryPermission::Inventory)
}

/// Build-permission consult used by `place_building` and future stall placement.
pub fn check_build(ctx: &ReducerContext, profile_id: u64, x: f32, y: f32) -> Result<(), String> {
    can_interact_pos(ctx, profile_id, x, y, TerritoryPermission::Build)
}

#[spacetimedb::reducer]
pub fn claim_territory(ctx: &ReducerContext, tiles_q: Vec<i32>, tiles_r: Vec<i32>) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    if tiles_q.len() != tiles_r.len() || tiles_q.is_empty() || tiles_q.len() > MAX_TERRITORY_TILES {
        return Err(format!("Claim must cover 1-{} tiles.", MAX_TERRITORY_TILES));
    }
    // Reject overlapping claims (BitCraft split-claim reject).
    for t in ctx.db.territory().iter() {
        for (q, r) in tiles_q.iter().zip(tiles_r.iter()) {
            if t.tiles_q.iter().zip(t.tiles_r.iter()).any(|(tq, tr)| tq == q && tr == r) {
                return Err("Tiles overlap an existing territory.".to_string());
            }
        }
    }
    ctx.db.territory().insert(Territory {
        territory_id: 0, owner_profile_id: player.profile_id,
        tiles_q, tiles_r, members: vec![], member_permissions: vec![],
        treasury: 0, last_upkeep_at: ctx.timestamp,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn territory_add_member(ctx: &ReducerContext, territory_id: u64, member_profile_id: u64, permissions: u8) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.territory().territory_id().find(&territory_id).ok_or("Territory not found.")?;
    if row.owner_profile_id != player.profile_id && !is_admin(ctx) {
        return Err("Only the owner can add members.".to_string());
    }
    let mut row = row;
    match row.members.iter().position(|m| *m == member_profile_id) {
        Some(i) => { row.member_permissions[i] = permissions; }
        None => { row.members.push(member_profile_id); row.member_permissions.push(permissions); }
    }
    ctx.db.territory().territory_id().update(row);
    Ok(())
}

#[spacetimedb::reducer]
pub fn territory_remove_member(ctx: &ReducerContext, territory_id: u64, member_profile_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.territory().territory_id().find(&territory_id).ok_or("Territory not found.")?;
    if row.owner_profile_id != player.profile_id && !is_admin(ctx) {
        return Err("Only the owner can remove members.".to_string());
    }
    let mut row = row;
    if let Some(i) = row.members.iter().position(|m| *m == member_profile_id) {
        row.members.remove(i);
        row.member_permissions.remove(i);
        ctx.db.territory().territory_id().update(row);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn territory_deposit(ctx: &ReducerContext, territory_id: u64, amount: u32) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.territory().territory_id().find(&territory_id).ok_or("Territory not found.")?;
    if row.owner_profile_id != player.profile_id && !is_admin(ctx) {
        return Err("Only the owner can fund the treasury.".to_string());
    }
    ctx.db.territory().territory_id().update(Territory { treasury: row.treasury.saturating_add(amount), ..row });
    Ok(())
}

#[spacetimedb::reducer]
pub fn disband_territory(ctx: &ReducerContext, territory_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.territory().territory_id().find(&territory_id).ok_or("Territory not found.")?;
    if row.owner_profile_id != player.profile_id && !is_admin(ctx) {
        return Err("Only the owner can disband.".to_string());
    }
    unclaim_buildings_in_territory(ctx, territory_id);
    ctx.db.territory().territory_id().delete(&territory_id);
    Ok(())
}

/// BitCraft unclaim mirror: dissolving a claim releases its buildings/sites to
/// wilderness (`territory_id = None`) instead of leaving a dangling FK.
fn unclaim_buildings_in_territory(ctx: &ReducerContext, territory_id: u64) {
    for b in ctx.db.building_state().iter().collect::<Vec<_>>() {
        if b.territory_id == Some(territory_id) {
            ctx.db.building_state().building_entity_id().update(crate::world::building::BuildingState {
                territory_id: None, ..b
            });
        }
    }
    for s in ctx.db.project_site().iter().collect::<Vec<_>>() {
        if s.territory_id == Some(territory_id) {
            ctx.db.project_site().site_id().update(crate::world::building::ProjectSiteState {
                territory_id: None, ..s
            });
        }
    }
}

/// Upkeep tick (BitCraft rent_collector/building_decay mirror): every
/// TERRITORY_UPKEEP_INTERVAL_SECS charge tiles*UPKEEP from the treasury;
/// unpaid → the territory dissolves.
pub fn tick_upkeep(ctx: &ReducerContext) {
    let now_micros = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    let interval_micros = (TERRITORY_UPKEEP_INTERVAL_SECS * 1_000_000.0) as u128;
    for row in ctx.db.territory().iter().collect::<Vec<_>>() {
        let then = row.last_upkeep_at.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
        if now_micros.saturating_sub(then) < interval_micros {
            continue;
        }
        let cost = row.tiles_q.len() as u32 * TERRITORY_UPKEEP_PER_TILE;
        if row.treasury >= cost {
            ctx.db.territory().territory_id().update(Territory {
                treasury: row.treasury - cost, last_upkeep_at: ctx.timestamp, ..row
            });
        } else {
            log::info!("Territory {} dissolved (unpaid upkeep).", row.territory_id);
            unclaim_buildings_in_territory(ctx, row.territory_id);
            ctx.db.territory().territory_id().delete(&row.territory_id);
        }
    }
}

#[spacetimedb::view(accessor = all_territories, public)]
fn all_territories(_ctx: &ViewContext) -> impl Query<Territory> {
    _ctx.from.territory().build()
}
