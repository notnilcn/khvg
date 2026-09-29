//! River generation. Ports `hex_map_godot`'s `_create_rivers`/`_create_river`
//! (hex_map_godot 135dc75, MIT — `scripts/hex_map_generator.gd`, `scripts/hex_cell.gd`), adapted
//! to main's data model:
//!
//! - Each river is a downhill walk over the canonical lap, one edge per step, laid into
//!   `HexTile.river_mask` on BOTH hexes of every edge (masks.rs invariant).
//! - main has no water level and no lakes, so `BuildingTile.height` is never mutated. A walk
//!   ends when it merges into an existing river (the neighbor has an outgoing river) or when it
//!   reaches a local minimum (no downhill candidate left).
//! - Origin density is a pseudo-moisture field times normalized elevation above the world's
//!   minimum, with the source's threshold duplication (> 0.75 twice, > 0.5 once, > 0.25 once);
//!   origins whose hex or neighbor already carries a river are rejected.
//! - Budget is RIVER_SEGMENT_PERCENT of the world's hex count, decremented by segments laid.
//!   A walk that lays only 1 segment without merging is rolled back (no single-edge stubs).
//!
//! Determinism: only `seed` + the shared `draw` counter drive randomness, cells are visited in
//! `enumerate_world_hexes` order, and direction candidates are gathered in
//! HEX_NEIGHBOR_DELTAS order — no HashMap iteration order ever feeds the RNG or a write.

use std::collections::HashMap;

use crate::main::global::{RIVER_MOISTURE_NOISE_SCALE, RIVER_SEGMENT_PERCENT};
use crate::world::hex::HexCell;
use crate::world::instance_tables::HexTile;
use crate::world::masks::{clear_edge, opposite_direction, set_edge};
use crate::world::noise::{value_noise2d, RIVER_NOISE_SEED_OFFSET};
use crate::world::prng::{pick_index, weighted_pick_index};
use crate::world::tables::MapConfig;
use crate::world::wrap::wrapped_hex_neighbors;

type Cell = (i32, i32);
// Direction index of the river edge leaving / entering a hex (masks.rs order).
type EdgeDirection = HashMap<Cell, u8>;

pub fn generate_rivers(
    tiles: &mut HashMap<Cell, HexTile>,
    cells: &[HexCell],
    elevation: &HashMap<Cell, i16>,
    cfg: &MapConfig,
    seed: u64,
    draw: &mut u64,
) {
    if cells.is_empty() || tiles.is_empty() { return; }

    // Origin weight needs each hex's height above the world minimum.
    let mut min_elevation = i16::MAX;
    let mut max_elevation = i16::MIN;
    for cell in cells {
        if let Some(&elevation) = elevation.get(&(cell.hex_q, cell.hex_r)) {
            min_elevation = min_elevation.min(elevation);
            max_elevation = max_elevation.max(elevation);
        }
    }
    if min_elevation == i16::MAX { return; }
    let span = (max_elevation - min_elevation).max(1) as f32;

    // Candidate origins, weighted by pseudo-moisture x normalized elevation and duplicated by
    // the source's thresholds: >0.75 three entries, >0.5 two, >0.25 one.
    let moisture_seed = seed ^ RIVER_NOISE_SEED_OFFSET;
    let mut origins: Vec<Cell> = Vec::new();
    for cell in cells {
        let Some(&cell_elevation) = elevation.get(&(cell.hex_q, cell.hex_r)) else { continue };
        if !tiles.contains_key(&(cell.hex_q, cell.hex_r)) { continue; }
        let moisture = value_noise2d(cell.world_x, cell.world_y, RIVER_MOISTURE_NOISE_SCALE, moisture_seed);
        let normalized_elevation = (cell_elevation - min_elevation) as f32 / span;
        let weight = moisture * normalized_elevation;
        if weight > 0.75 { origins.push((cell.hex_q, cell.hex_r)); origins.push((cell.hex_q, cell.hex_r)); }
        if weight > 0.5 { origins.push((cell.hex_q, cell.hex_r)); }
        if weight > 0.25 { origins.push((cell.hex_q, cell.hex_r)); }
    }

    let mut budget: usize = cells.len() * RIVER_SEGMENT_PERCENT / 100;
    let mut incoming: EdgeDirection = HashMap::new();
    let mut outgoing: EdgeDirection = HashMap::new();

    while budget > 0 && !origins.is_empty() {
        let origin = origins.swap_remove(pick_index(origins.len(), seed, draw));
        if incoming.contains_key(&origin) || outgoing.contains_key(&origin) { continue; }
        if !origin_is_valid(origin, tiles, cfg, &incoming, &outgoing) { continue; }
        let segments = create_river(tiles, elevation, cfg, origin, seed, draw, &mut incoming, &mut outgoing);
        budget = budget.saturating_sub(segments);
    }
}

// No neighbor may already be on a river (main has no water, so this is the source's only
// origin filter), and the origin itself must have renderable terrain.
fn origin_is_valid(origin: Cell, tiles: &HashMap<Cell, HexTile>, cfg: &MapConfig, incoming: &EdgeDirection, outgoing: &EdgeDirection) -> bool {
    if !tiles.contains_key(&origin) { return false; }
    let neighbors = wrapped_hex_neighbors(origin.0, origin.1, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows);
    !neighbors.iter().any(|n| incoming.contains_key(n) || outgoing.contains_key(n))
}

// One walk from `origin`. Returns the number of segments laid (0 for a rolled-back stub).
#[allow(clippy::too_many_arguments)]
fn create_river(
    tiles: &mut HashMap<Cell, HexTile>,
    elevation: &HashMap<Cell, i16>,
    cfg: &MapConfig,
    origin: Cell,
    seed: u64,
    draw: &mut u64,
    incoming: &mut EdgeDirection,
    outgoing: &mut EdgeDirection,
) -> usize {
    let neighbors_of = |cell: Cell| wrapped_hex_neighbors(cell.0, cell.1, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows);

    let mut laid: Vec<(Cell, usize)> = Vec::new();
    let mut cell = origin;
    let mut heading: Option<usize> = None;
    let mut merged = false;

    while let Some(&cell_elevation) = elevation.get(&cell) {
        let neighbors = neighbors_of(cell);
        let mut candidates: Vec<usize> = Vec::new();
        let mut weights: Vec<f32> = Vec::new();
        let mut merge_direction: Option<usize> = None;

        for (direction, &neighbor) in neighbors.iter().enumerate() {
            if neighbor == origin { continue; }
            let Some(&neighbor_elevation) = elevation.get(&neighbor) else { continue };
            if !tiles.contains_key(&neighbor) { continue; }
            if incoming.contains_key(&neighbor) { continue; }
            let delta = neighbor_elevation - cell_elevation;
            if delta > 0 { continue; }
            if outgoing.contains_key(&neighbor) { merge_direction = Some(direction); break; }

            let mut weight = 1.0f32;                          // base
            if delta < 0 { weight += 3.0; }                   // downhill
            if let Some(previous) = heading {
                let forward2 = (previous + 2) % 6;
                let backward2 = (previous + 4) % 6;
                if direction != forward2 && direction != backward2 { weight += 1.0; }
            }
            candidates.push(direction);
            weights.push(weight);
        }

        if let Some(direction) = merge_direction {
            lay_edge(tiles, incoming, outgoing, cell, direction, &neighbors);
            laid.push((cell, direction));
            merged = true;
            break;
        }
        if candidates.is_empty() { break; }

        let direction = candidates[weighted_pick_index(&weights, seed, draw)];
        lay_edge(tiles, incoming, outgoing, cell, direction, &neighbors);
        laid.push((cell, direction));
        cell = neighbors[direction];
        heading = Some(direction);
    }

    // A single-edge stub (a source that dies immediately, no merge) is removed again so no
    // isolated one-edge rivers exist in the final masks. Merges cannot happen before the second
    // segment: origin validity already rejects any origin adjacent to a river.
    if laid.len() < 2 && !merged {
        for (cell, direction) in laid {
            let neighbors = neighbors_of(cell);
            roll_back_edge(tiles, incoming, outgoing, cell, direction, &neighbors);
        }
        return 0;
    }
    laid.len()
}

// Writes both sides of one river edge and records the head/tail directions.
fn lay_edge(tiles: &mut HashMap<Cell, HexTile>, incoming: &mut EdgeDirection, outgoing: &mut EdgeDirection, from: Cell, direction: usize, neighbors: &[Cell; 6]) {
    let to = neighbors[direction];
    let opposite = opposite_direction(direction);
    if let Some(tile) = tiles.get_mut(&from) { set_edge(&mut tile.river_mask, direction); }
    if let Some(tile) = tiles.get_mut(&to) { set_edge(&mut tile.river_mask, opposite); }
    outgoing.insert(from, direction as u8);
    incoming.insert(to, opposite as u8);
}

fn roll_back_edge(tiles: &mut HashMap<Cell, HexTile>, incoming: &mut EdgeDirection, outgoing: &mut EdgeDirection, from: Cell, direction: usize, neighbors: &[Cell; 6]) {
    let to = neighbors[direction];
    let opposite = opposite_direction(direction);
    if let Some(tile) = tiles.get_mut(&from) { clear_edge(&mut tile.river_mask, direction); }
    if let Some(tile) = tiles.get_mut(&to) { clear_edge(&mut tile.river_mask, opposite); }
    outgoing.remove(&from);
    incoming.remove(&to);
}
