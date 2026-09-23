//! Road generation: connect the BiomeRegion centers planted during `run_generation` with a
//! deterministic wrap-aware Dijkstra (`world/pathing.rs`) and lay the resulting paths into
//! `HexTile.road_mask` on both hexes of every edge.
//!
//! Connection order is region_id ascending (the caller's insertion order), with each region
//! attaching to the cheapest already-connected region — tie-break: the lower region_id (the
//! loop keeps the first minimum). Costs come from `pathing::terrain_step_cost`: base step +
//! elevation delta, cliffs impassable both ways, a bridge surcharge on river edges, and a
//! discount on edges that already carry a road (so later connections reuse earlier ones).
//!
//! A road never shares an edge with a river: traversing a river edge is allowed (that IS the
//! crossing) but carries the bridge surcharge and gets no road bit — the client renders the
//! bridge. Reuse note: this pass and Phase 4's enemy pathfinding share `find_path` and
//! `terrain_step_cost`; nothing here is road-specific beyond the write step.

use std::collections::HashMap;

use crate::main::global::{MAX_WALKABLE_DOWN_DELTA, MAX_WALKABLE_UP_DELTA, ROAD_BASE_STEP_COST, ROAD_ELEVATION_STEP_COST, ROAD_EXISTING_DISCOUNT, ROAD_RIVER_CROSS_COST};
use crate::world::instance_tables::HexTile;
use crate::world::masks::{direction_index, has_edge, opposite_direction, set_edge};
use crate::world::pathing::{find_path, path_cost, terrain_step_cost, HexTerrain, StepCostConfig};
use crate::world::tables::MapConfig;
use crate::world::wrap::wrapped_hex_neighbors;

type Cell = (i32, i32);

pub fn generate_roads(
    tiles: &mut HashMap<Cell, HexTile>,
    elevation: &HashMap<Cell, i16>,
    region_centers: &[(u64, i32, i32)],
    cfg: &MapConfig,
) {
    let neighbors = |(q, r): Cell| wrapped_hex_neighbors(q, r, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows);
    let cost_cfg = StepCostConfig {
        base_step_cost: ROAD_BASE_STEP_COST,
        elevation_step_cost: ROAD_ELEVATION_STEP_COST,
        river_cross_cost: ROAD_RIVER_CROSS_COST,
        road_discount: ROAD_EXISTING_DISCOUNT,
        max_walkable_up_delta: MAX_WALKABLE_UP_DELTA,
        max_walkable_down_delta: MAX_WALKABLE_DOWN_DELTA,
    };

    let mut ordered: Vec<(u64, Cell)> = region_centers.iter()
        .map(|&(region_id, q, r)| (region_id, (q, r)))
        .collect();
    ordered.sort_by_key(|&(region_id, _)| region_id);
    let centers: Vec<Cell> = ordered.into_iter()
        .filter(|&(_, cell)| tiles.contains_key(&cell) && elevation.contains_key(&cell))
        .map(|(_, cell)| cell)
        .collect();
    if centers.len() < 2 { return; }

    // Mutable cost view of the hexes: road bits written by earlier connections feed the
    // existing-road discount of later ones, so it is kept in sync with the HexTile rows.
    let mut terrain: HashMap<Cell, HexTerrain> = tiles.iter()
        .filter_map(|(&cell, tile)| elevation.get(&cell).map(|&elevation| (cell, HexTerrain {
            elevation, river_mask: tile.river_mask, road_mask: tile.road_mask, wall_mask: tile.wall_mask,
        })))
        .collect();

    for i in 1..centers.len() {
        let goal = centers[i];
        let mut best: Option<(u32, Vec<Cell>)> = None;
        for &start in &centers[..i] {
            let Some(path) = find_path(start, goal, neighbors, |from, to, direction| {
                road_step_cost(&terrain, &cost_cfg, from, to, direction)
            }) else { continue };
            let Some(total) = path_cost(&path, neighbors, |from, to, direction| {
                road_step_cost(&terrain, &cost_cfg, from, to, direction)
            }) else { continue };
            // Strict `<` keeps the lowest-index (lowest region_id) connection on ties.
            if best.as_ref().is_none_or(|(best_total, _)| total < *best_total) {
                best = Some((total, path));
            }
        }
        if let Some((_, path)) = best {
            write_road_path(tiles, &mut terrain, neighbors, &path);
        }
    }
}

fn road_step_cost(terrain: &HashMap<Cell, HexTerrain>, cfg: &StepCostConfig, from: Cell, to: Cell, direction: usize) -> Option<u32> {
    terrain_step_cost(terrain.get(&from).copied()?, terrain.get(&to).copied()?, direction, cfg)
}

fn write_road_path(
    tiles: &mut HashMap<Cell, HexTile>,
    terrain: &mut HashMap<Cell, HexTerrain>,
    neighbors: impl Fn(Cell) -> [Cell; 6],
    path: &[Cell],
) {
    for window in path.windows(2) {
        let (from, to) = (window[0], window[1]);
        let Some(direction) = direction_index(&neighbors(from), to) else { continue };
        if tiles.get(&from).is_some_and(|tile| has_edge(tile.river_mask, direction)) { continue; }
        let opposite = opposite_direction(direction);
        if let Some(tile) = tiles.get_mut(&from) { set_edge(&mut tile.road_mask, direction); }
        if let Some(tile) = tiles.get_mut(&to) { set_edge(&mut tile.road_mask, opposite); }
        if let Some(hex) = terrain.get_mut(&from) { set_edge(&mut hex.road_mask, direction); }
        if let Some(hex) = terrain.get_mut(&to) { set_edge(&mut hex.road_mask, opposite); }
    }
}
