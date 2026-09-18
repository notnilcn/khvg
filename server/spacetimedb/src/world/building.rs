//! BitCraft-mirrored building system.
//!
//! BitCraft (`packages/game/src/messages/components.rs`) stores buildings as:
//! - `BuildingState` anchor (`entity_id`, `claim_entity_id`, `direction_index`,
//!   `building_description_id`, `constructed_by`) + `LocationState` row for position,
//! - `FootprintTileState` one row per footprint hex (`owner_entity_id`, `footprint_type`),
//! - `ProjectSiteState` construction progress (`construction_recipe_id`, `progress`, ...),
//! - `*_desc` static catalogs (`building_desc.footprint: Vec<FootprintTile>`, ...).
//!
//! Target adaptation (single-module, axial q/r hex grid, SpacetimeDB 2.2):
//! - The dense `BuildingTile` grid stays as the terrain store (hex_q/r + chunk_index +
//!   elevation). It is NOT the building record anymore — it is the hex-existence check
//!   ("has add_chunks been called?") and the chunk_index source, plus a
//!   backward-compat `building_type` stamp so `nearby_building_tiles` / elevation clients
//!   keep working.
//! - `BuildingState` carries its own anchor (anchor_q/r + chunk_index) instead of a
//!   separate generic `LocationState` table — one less join, same normalized idea.
//! - `FootprintTileState` (accessor `footprint_tile`) is one row per footprint hex,
//!   owned by a finished building. Project sites keep their footprint implicit
//!   (computed from the desc on demand) so site ids and building ids never collide
//!   in one owner column.
//! - `BuildingDesc.footprint` is `Vec<FootprintOffset>` axial deltas, rotated by
//!   `direction` (0-5, 60-degree steps clockwise) — the analogue of BitCraft's
//!   `get_footprint()` (`direction / 2` there because their direction enum is 12-way).

use spacetimedb::{table, Identity, ReducerContext, SpacetimeType, Table, Timestamp, ViewContext, AnonymousViewContext, Query};
use crate::main::global::DEFAULT_TERRAIN_AOI_CHUNK_RADIUS;
use crate::player::views::nearby_indices_from_chunk;
use crate::world::tables::{building_tile, MapConfig};

// ===== Static catalog (BitCraft `building_desc` mirror) =====

/// Per-tile role inside a building footprint (BitCraft `FootprintType` minus
/// `WalkableResource`, which needs the resource pipeline we don't have).
#[derive(SpacetimeType, Clone, Copy, PartialEq, Debug)]
pub enum FootprintType {
    Hitbox,
    Walkable,
    Perimeter,
}

/// One axial delta of a building footprint, relative to the anchor hex.
#[derive(SpacetimeType, Clone, PartialEq, Debug)]
pub struct FootprintOffset {
    pub dq: i32,
    pub dr: i32,
    pub kind: FootprintType,
}

/// Static building definition (BitCraft `building_desc` + `construction_recipe_desc`
/// collapsed: footprint + build cost in work actions). Admin-authored, seeded by
/// `seed_default_buildings`.
#[table(accessor = building_desc, public)]
pub struct BuildingDesc {
    #[primary_key]
    pub building_id: String,
    pub display_name: String,
    pub footprint: Vec<FootprintOffset>,
    pub max_health: u32,
    pub actions_required: u32,
}

// ===== Instance tables (BitCraft `*_state` mirror) =====

/// Finished building anchor (BitCraft `BuildingState`). Position lives on this row;
/// per-hex occupancy lives in `FootprintTileState`. `territory_id` mirrors
/// `claim_entity_id` (`None` = wilderness, like BitCraft's `0`).
#[table(accessor = building_state, public)]
pub struct BuildingState {
    #[primary_key] #[auto_inc]
    pub building_entity_id: u64,
    pub anchor_q: i32,
    pub anchor_r: i32,
    #[index(btree)]
    pub chunk_index: i64,
    pub direction: i32,
    #[index(btree)]
    pub building_def_id: String,
    pub territory_id: Option<u64>,
    pub owner_profile_id: u64,
    pub constructed_by: Identity,
}

/// One row per footprint hex of a finished building (BitCraft `FootprintTileState`).
#[table(accessor = footprint_tile, public)]
pub struct FootprintTileState {
    #[primary_key] #[auto_inc]
    pub footprint_id: u64,
    #[index(btree)]
    pub owner_building_id: u64,
    #[index(btree)]
    pub hex_q: i32,
    pub hex_r: i32,
    #[index(btree)]
    pub chunk_index: i64,
    pub kind: FootprintType,
}

/// Construction site (BitCraft `ProjectSiteState`, scoped to work progress).
/// Footprint is implicit — resolved from the desc whenever occupancy is checked.
#[table(accessor = project_site, public)]
pub struct ProjectSiteState {
    #[primary_key] #[auto_inc]
    pub site_id: u64,
    pub anchor_q: i32,
    pub anchor_r: i32,
    #[index(btree)]
    pub chunk_index: i64,
    pub building_def_id: String,
    pub direction: i32,
    pub progress: u32,
    pub actions_required: u32,
    pub owner_profile_id: u64,
    pub constructed_by: Identity,
    pub territory_id: Option<u64>,
    pub last_worked_at: Timestamp,
}

// ===== Footprint math (BitCraft `BuildingDesc::get_footprint` mirror) =====

pub fn normalize_direction(direction: i32) -> i32 {
    direction.rem_euclid(6)
}

/// One 60-degree clockwise axial step: (1,0) -> (0,1) -> (-1,1) -> ... (matches
/// `HEX_NEIGHBOR_DELTAS` order in `world/hex.rs`).
pub fn rotate_offset_cw(dq: i32, dr: i32) -> (i32, i32) {
    (-dr, dq + dr)
}

pub fn rotate_offset(dq: i32, dr: i32, direction: i32) -> (i32, i32) {
    let mut out = (dq, dr);
    for _ in 0..normalize_direction(direction) {
        out = rotate_offset_cw(out.0, out.1);
    }
    out
}

/// Anchor + rotated footprint deltas -> absolute hex cells (BitCraft `get_footprint`).
pub fn footprint_cells(desc: &BuildingDesc, anchor_q: i32, anchor_r: i32, direction: i32) -> Vec<(i32, i32, FootprintType)> {
    desc.footprint.iter().map(|off| {
        let (rdq, rdr) = rotate_offset(off.dq, off.dr, direction);
        (anchor_q + rdq, anchor_r + rdr, off.kind)
    }).collect()
}

fn building_tile_at(ctx: &ReducerContext, q: i32, r: i32) -> Option<crate::world::tables::BuildingTile> {
    ctx.db.building_tile().hex_q().filter(&q).find(|t| t.hex_r == r)
}

/// True when any building record touches (q, r): finished footprint rows, any
/// project-site footprint (computed on the fly), or the legacy single-hex stamp.
pub fn hex_occupied(ctx: &ReducerContext, q: i32, r: i32) -> bool {
    if ctx.db.footprint_tile().hex_q().filter(&q).any(|t| t.hex_r == r) {
        return true;
    }
    for site in ctx.db.project_site().iter() {
        if let Some(desc) = ctx.db.building_desc().building_id().find(&site.building_def_id) {
            for (cq, cr, _) in footprint_cells(&desc, site.anchor_q, site.anchor_r, site.direction) {
                if cq == q && cr == r {
                    return true;
                }
            }
        } else if site.anchor_q == q && site.anchor_r == r {
            return true;
        }
    }
    match building_tile_at(ctx, q, r) {
        Some(t) => !t.building_type.is_empty(),
        None => false,
    }
}

fn footprint_occupied(ctx: &ReducerContext, cells: &[(i32, i32, FootprintType)]) -> Option<(i32, i32)> {
    cells.iter().find(|(q, r, _)| hex_occupied(ctx, *q, *r)).map(|(q, r, _)| (*q, *r))
}

/// BitCraft `claim_helper::get_claim_under_footprint` mirror: `Some(id)` only when
/// every footprint cell sits inside the SAME territory, else `None` (wilderness).
fn territory_under_footprint(ctx: &ReducerContext, cells: &[(i32, i32, FootprintType)]) -> Option<u64> {
    let mut acc: Option<u64> = None;
    for (q, r, _) in cells {
        match crate::claims::territory_id_at_hex(ctx, *q, *r) {
            Some(id) => match acc {
                None => acc = Some(id),
                Some(prev) if prev == id => {}
                _ => return None,
            },
            None => return None,
        }
    }
    acc
}

fn check_footprint_tiles_exist(ctx: &ReducerContext, cells: &[(i32, i32, FootprintType)]) -> Result<Vec<i64>, String> {
    let mut chunk_indices = Vec::with_capacity(cells.len());
    for (q, r, _) in cells {
        match building_tile_at(ctx, *q, *r) {
            Some(t) => chunk_indices.push(t.chunk_index),
            None => return Err("Tile not found — has add_chunks been called?".to_string()),
        }
    }
    Ok(chunk_indices)
}

fn check_build_perm_for_cells(ctx: &ReducerContext, profile_id: u64, cells: &[(i32, i32, FootprintType)]) -> Result<(), String> {
    let cfg = MapConfig::load(ctx, 0, 0);
    for (q, r, _) in cells {
        let (wx, wy) = crate::world::hex::hex_to_world(*q, *r, cfg.hex_outer_radius);
        crate::claims::check_build(ctx, profile_id, wx, wy)?;
    }
    Ok(())
}

fn stamp_legacy_tiles(ctx: &ReducerContext, cells: &[(i32, i32, FootprintType)], chunk_indices: &[i64], building_id: &str, owner: Option<Identity>) {
    for ((q, r, _), chunk_index) in cells.iter().zip(chunk_indices.iter()) {
        if let Some(tile) = building_tile_at(ctx, *q, *r) {
            // chunk_index is re-stamped from the existing row (== *chunk_index); spread
            // keeps elevation/chunk_q/r/local_index intact.
            let _ = chunk_index;
            ctx.db.building_tile().tile_id().update(crate::world::tables::BuildingTile {
                building_type: building_id.to_string(), owner_id: owner, ..tile
            });
        }
    }
}

fn clear_legacy_stamp_if_free(ctx: &ReducerContext, q: i32, r: i32) {
    if ctx.db.footprint_tile().hex_q().filter(&q).any(|t| t.hex_r == r) {
        return;
    }
    if let Some(tile) = building_tile_at(ctx, q, r) {
        if !tile.building_type.is_empty() {
            ctx.db.building_tile().tile_id().update(crate::world::tables::BuildingTile {
                building_type: String::new(), owner_id: None, ..tile
            });
        }
    }
}

fn complete_site(ctx: &ReducerContext, site: &ProjectSiteState, desc: &BuildingDesc, cells: &[(i32, i32, FootprintType)], chunk_indices: &[i64]) -> Result<(), String> {
    let building_id = ctx.db.building_state().insert(BuildingState {
        building_entity_id: 0,
        anchor_q: site.anchor_q,
        anchor_r: site.anchor_r,
        chunk_index: site.chunk_index,
        direction: site.direction,
        building_def_id: site.building_def_id.clone(),
        territory_id: site.territory_id,
        owner_profile_id: site.owner_profile_id,
        constructed_by: site.constructed_by,
    }).building_entity_id;
    for ((q, r, kind), chunk_index) in cells.iter().zip(chunk_indices.iter()) {
        ctx.db.footprint_tile().insert(FootprintTileState {
            footprint_id: 0,
            owner_building_id: building_id,
            hex_q: *q,
            hex_r: *r,
            chunk_index: *chunk_index,
            kind: *kind,
        });
    }
    stamp_legacy_tiles(ctx, cells, chunk_indices, &desc.building_id, Some(site.constructed_by));
    ctx.db.project_site().site_id().delete(&site.site_id);
    log::info!("Building {} ({}) completed by {:?}.", building_id, desc.building_id, ctx.sender());
    Ok(())
}

// ===== Reducers =====

/// Admin catalog upsert (BitCraft static-data staging, scoped to one table).
#[spacetimedb::reducer]
pub fn upsert_building_desc(ctx: &ReducerContext, desc: BuildingDesc) -> Result<(), String> {
    if !crate::player::methods::is_admin(ctx) {
        return Err("Admin only.".to_string());
    }
    if desc.building_id.is_empty() {
        return Err("building_id cannot be empty.".to_string());
    }
    if desc.footprint.is_empty() {
        return Err("footprint cannot be empty.".to_string());
    }
    if ctx.db.building_desc().building_id().find(&desc.building_id).is_some() {
        ctx.db.building_desc().building_id().update(desc);
    } else {
        ctx.db.building_desc().insert(desc);
    }
    Ok(())
}

/// Place a construction site (BitCraft `project_site_place` mirror). Validates the
/// rotated footprint, claim Build permission, then inserts a `ProjectSiteState` at
/// progress 0. Zero-action descs complete instantly.
#[spacetimedb::reducer]
pub fn place_project_site(ctx: &ReducerContext, building_def_id: String, anchor_q: i32, anchor_r: i32, direction: i32) -> Result<(), String> {
    let player = crate::player::methods::require_in_world(ctx)?;
    let desc = ctx.db.building_desc().building_id().find(&building_def_id)
        .ok_or_else(|| format!("Building '{}' not found.", building_def_id))?;
    let direction = normalize_direction(direction);
    let cells = footprint_cells(&desc, anchor_q, anchor_r, direction);
    let chunk_indices = check_footprint_tiles_exist(ctx, &cells)?;
    if let Some((q, r)) = footprint_occupied(ctx, &cells) {
        return Err(format!("Tile ({}, {}) already occupied.", q, r));
    }
    if !crate::player::methods::is_admin(ctx) {
        check_build_perm_for_cells(ctx, player.profile_id, &cells)?;
    }
    let anchor_tile = building_tile_at(ctx, anchor_q, anchor_r)
        .ok_or("Tile not found — has add_chunks been called?".to_string())?;
    let territory_id = territory_under_footprint(ctx, &cells);
    let site = ctx.db.project_site().insert(ProjectSiteState {
        site_id: 0,
        anchor_q,
        anchor_r,
        chunk_index: anchor_tile.chunk_index,
        building_def_id: desc.building_id.clone(),
        direction,
        progress: 0,
        actions_required: desc.actions_required,
        owner_profile_id: player.profile_id,
        constructed_by: ctx.sender(),
        territory_id,
        last_worked_at: ctx.timestamp,
    });
    if desc.actions_required == 0 {
        let site_row = ctx.db.project_site().site_id().find(&site.site_id).ok_or("Site vanished.".to_string())?;
        complete_site(ctx, &site_row, &desc, &cells, &chunk_indices)?;
    }
    Ok(())
}

/// Contribute work to a site (BitCraft `project_site_advance_project` mirror).
/// Completes into `BuildingState` + footprint rows once progress is met.
#[spacetimedb::reducer]
pub fn advance_project_site(ctx: &ReducerContext, site_id: u64, work: u32) -> Result<(), String> {
    let player = crate::player::methods::require_in_world(ctx)?;
    if work == 0 {
        return Err("work must be positive.".to_string());
    }
    let mut site = ctx.db.project_site().site_id().find(&site_id).ok_or("Project site not found.".to_string())?;
    let desc = ctx.db.building_desc().building_id().find(&site.building_def_id)
        .ok_or_else(|| format!("Building '{}' not found.", site.building_def_id))?;
    let allowed = site.owner_profile_id == player.profile_id
        || crate::player::methods::is_admin(ctx)
        || {
            let cells = footprint_cells(&desc, site.anchor_q, site.anchor_r, site.direction);
            check_build_perm_for_cells(ctx, player.profile_id, &cells).is_ok()
        };
    if !allowed {
        return Err("Not the owner.".to_string());
    }
    let cells = footprint_cells(&desc, site.anchor_q, site.anchor_r, site.direction);
    let chunk_indices = check_footprint_tiles_exist(ctx, &cells)?;
    // Re-check occupancy excluding this site's own (implicit) footprint: any finished
    // footprint, other site, or legacy stamp on these cells blocks completion.
    for (q, r, _) in &cells {
        if ctx.db.footprint_tile().hex_q().filter(q).any(|t| t.hex_r == *r) {
            return Err(format!("Tile ({}, {}) already occupied.", q, r));
        }
        for other in ctx.db.project_site().iter() {
            if other.site_id == site.site_id {
                continue;
            }
            if let Some(other_desc) = ctx.db.building_desc().building_id().find(&other.building_def_id) {
                let other_cells = footprint_cells(&other_desc, other.anchor_q, other.anchor_r, other.direction);
                if other_cells.iter().any(|(oq, orr, _)| oq == q && orr == r) {
                    return Err(format!("Tile ({}, {}) already occupied.", q, r));
                }
            } else if other.anchor_q == *q && other.anchor_r == *r {
                return Err(format!("Tile ({}, {}) already occupied.", q, r));
            }
        }
        match building_tile_at(ctx, *q, *r) {
            Some(t) if !t.building_type.is_empty() => return Err(format!("Tile ({}, {}) already occupied.", q, r)),
            _ => {}
        }
    }
    site.progress = site.progress.saturating_add(work);
    site.last_worked_at = ctx.timestamp;
    if site.progress >= site.actions_required {
        complete_site(ctx, &site, &desc, &cells, &chunk_indices)?;
    } else {
        ctx.db.project_site().site_id().update(site);
    }
    Ok(())
}

/// Cancel a site before completion (owner, claim builder, or admin).
#[spacetimedb::reducer]
pub fn cancel_project_site(ctx: &ReducerContext, site_id: u64) -> Result<(), String> {
    let player = crate::player::methods::require_in_world(ctx)?;
    let site = ctx.db.project_site().site_id().find(&site_id).ok_or("Project site not found.".to_string())?;
    let allowed = site.owner_profile_id == player.profile_id || crate::player::methods::is_admin(ctx);
    if !allowed {
        return Err("Not the owner.".to_string());
    }
    ctx.db.project_site().site_id().delete(&site_id);
    Ok(())
}

/// Demolish a finished building (BitCraft `delete_footprint` + building delete
/// mirror). Clears footprint rows and frees the legacy tile stamps they covered.
#[spacetimedb::reducer]
pub fn demolish_building(ctx: &ReducerContext, building_entity_id: u64) -> Result<(), String> {
    let player = crate::player::methods::require_in_world(ctx)?;
    let building = ctx.db.building_state().building_entity_id().find(&building_entity_id)
        .ok_or("Building not found.".to_string())?;
    let allowed = building.owner_profile_id == player.profile_id
        || building.constructed_by == ctx.sender()
        || crate::player::methods::is_admin(ctx)
        || {
            let cfg = MapConfig::load(ctx, 0, 0);
            let (wx, wy) = crate::world::hex::hex_to_world(building.anchor_q, building.anchor_r, cfg.hex_outer_radius);
            crate::claims::check_build(ctx, player.profile_id, wx, wy).is_ok()
        };
    if !allowed {
        return Err("Not the owner.".to_string());
    }
    let owned: Vec<FootprintTileState> = ctx.db.footprint_tile().owner_building_id().filter(&building_entity_id).collect();
    for fp in &owned {
        ctx.db.footprint_tile().footprint_id().delete(&fp.footprint_id);
    }
    ctx.db.building_state().building_entity_id().delete(&building_entity_id);
    for fp in &owned {
        clear_legacy_stamp_if_free(ctx, fp.hex_q, fp.hex_r);
    }
    // Anchor hex may carry a legacy stamp without a footprint row (pre-mirror rows).
    clear_legacy_stamp_if_free(ctx, building.anchor_q, building.anchor_r);
    Ok(())
}

// ===== Seeds (demo catalog) =====

pub fn seed_default_buildings(ctx: &ReducerContext) {
    let defs = vec![
        BuildingDesc {
            building_id: "hut".to_string(),
            display_name: "Hut".to_string(),
            footprint: vec![FootprintOffset { dq: 0, dr: 0, kind: FootprintType::Hitbox }],
            max_health: 100,
            actions_required: 3,
        },
        BuildingDesc {
            building_id: "hall".to_string(),
            display_name: "Hall".to_string(),
            footprint: vec![
                FootprintOffset { dq: 0, dr: 0, kind: FootprintType::Hitbox },
                FootprintOffset { dq: 1, dr: 0, kind: FootprintType::Hitbox },
                FootprintOffset { dq: 0, dr: 1, kind: FootprintType::Hitbox },
                FootprintOffset { dq: -1, dr: 1, kind: FootprintType::Hitbox },
                FootprintOffset { dq: -1, dr: 0, kind: FootprintType::Hitbox },
                FootprintOffset { dq: 0, dr: -1, kind: FootprintType::Hitbox },
                FootprintOffset { dq: 1, dr: -1, kind: FootprintType::Hitbox },
            ],
            max_health: 500,
            actions_required: 10,
        },
        BuildingDesc {
            building_id: "paved".to_string(),
            display_name: "Paved Plot".to_string(),
            footprint: vec![FootprintOffset { dq: 0, dr: 0, kind: FootprintType::Walkable }],
            max_health: 25,
            actions_required: 1,
        },
    ];
    for def in defs {
        if ctx.db.building_desc().building_id().find(&def.building_id).is_some() {
            ctx.db.building_desc().building_id().update(def);
        } else {
            ctx.db.building_desc().insert(def);
        }
    }
}

// ===== Views =====

#[spacetimedb::view(accessor = all_building_desc, public)]
fn all_building_desc(_ctx: &AnonymousViewContext) -> impl Query<BuildingDesc> {
    _ctx.from.building_desc().build()
}

#[spacetimedb::view(accessor = nearby_buildings, public)]
fn nearby_buildings(ctx: &ViewContext) -> impl Query<BuildingState> {
    let empty = ctx.from.building_state().r#where(|b| b.building_entity_id.eq(u64::MAX)).build();
    let indices = match nearby_indices_from_chunk(ctx, DEFAULT_TERRAIN_AOI_CHUNK_RADIUS) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.building_state()
        .r#where(move |b| {
            let mut cond = b.chunk_index.eq(indices[0]);
            for &idx in &indices[1..] { cond = cond.or(b.chunk_index.eq(idx)); }
            cond
        })
        .build()
}

#[spacetimedb::view(accessor = nearby_footprint_tiles, public)]
fn nearby_footprint_tiles(ctx: &ViewContext) -> impl Query<FootprintTileState> {
    let empty = ctx.from.footprint_tile().r#where(|t| t.footprint_id.eq(u64::MAX)).build();
    let indices = match nearby_indices_from_chunk(ctx, DEFAULT_TERRAIN_AOI_CHUNK_RADIUS) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.footprint_tile()
        .r#where(move |t| {
            let mut cond = t.chunk_index.eq(indices[0]);
            for &idx in &indices[1..] { cond = cond.or(t.chunk_index.eq(idx)); }
            cond
        })
        .build()
}

#[spacetimedb::view(accessor = nearby_project_sites, public)]
fn nearby_project_sites(ctx: &ViewContext) -> impl Query<ProjectSiteState> {
    let empty = ctx.from.project_site().r#where(|s| s.site_id.eq(u64::MAX)).build();
    let indices = match nearby_indices_from_chunk(ctx, DEFAULT_TERRAIN_AOI_CHUNK_RADIUS) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.project_site()
        .r#where(move |s| {
            let mut cond = s.chunk_index.eq(indices[0]);
            for &idx in &indices[1..] { cond = cond.or(s.chunk_index.eq(idx)); }
            cond
        })
        .build()
}
