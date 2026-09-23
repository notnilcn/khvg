// Portions derived from hex_map_godot (135dc75) - MIT. See THIRD_PARTY_NOTICES.md.
// Wrap-aware hex pathfinding: deterministic Dijkstra over the hex grid. Written for the road
// generator (world/terrain/roads.rs, Phase 3) and shaped for reuse by Phase 4's enemy
// pathfinding — `find_path` and `terrain_step_cost` take no table/context types, so any caller
// can feed them neighbors and a cost model. The move-cost model follows hex_map_godot's
// hex_unit.gd (135dc75, MIT); worldgen picks the knobs in world/terrain/roads.rs.
//
// Determinism: the neighbor order is the caller's (canonical HEX_NEIGHBOR_DELTAS order via
// wrapped_hex_neighbors) and the frontier heap breaks ties on (cost, q, r), so equal-cost
// paths resolve identically regardless of hash-map iteration order.
use std::cmp::Reverse;
use std::collections::{BinaryHeap, HashMap};

use super::masks::{direction_index, has_edge};

pub type HexCoord = (i32, i32);

// A terrain snapshot covering the whole canonical grid, keyed by wrapped hex coordinate.
// Callers build it once and share it across every path query in a tick (both table scans are
// O(hexes)); the path search itself never touches tables.
pub type HexTerrainSnapshot = HashMap<HexCoord, HexTerrain>;

// What the movement layer should do with an enemy's stored path this tick, decided by
// `enemy::methods::path_decision` (pure — see that module for the cadence rationale).
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum PathDecision {
    Follow,
    Direct,
    Recompute,
}

// Terrain snapshot a cost model reads. Pure data (no table rows) so pathing is testable
// standalone. The masks are per-edge, one bit per edge in HEX_NEIGHBOR_DELTAS order (see
// world/masks.rs); both hexes sharing an edge carry the bit at mirrored indices.
#[derive(Clone, Copy, Default, PartialEq, Eq)]
pub struct HexTerrain {
    pub elevation: i16,
    pub river_mask: u8,
    pub road_mask: u8,
    pub wall_mask: u8,
}

// Cost knobs. Worldgen fills these from the ROAD_* constants in main/global.rs; Phase 4's
// enemy pathfinding can reuse the struct with enemy-specific values.
#[derive(Clone, Copy)]
pub struct StepCostConfig {
    pub base_step_cost: u32,
    // Added per elevation level of |to.elevation - from.elevation|.
    pub elevation_step_cost: u32,
    // Surcharge for traversing an edge that carries a river (a bridge crossing).
    pub river_cross_cost: u32,
    // Discount for traversing an edge that already carries a road.
    pub road_discount: u32,
    pub max_walkable_up_delta: i16,
    pub max_walkable_down_delta: i16,
}

// One step from `from` to `to`, `direction` being `to`'s index in from's wrapped neighbor
// array. None = impassable. A walled edge is impassable in both directions (the both-sides
// mask invariant means checking the `from` side is sufficient). Cliffs block both ways: rising
// past max_walkable_up_delta and dropping past max_walkable_down_delta are both rejected,
// because roads must stay player-walkable (Phase 4 can pass relaxed knobs). A river edge is
// crossable at a bridge surcharge; an existing road edge earns a discount.
pub fn terrain_step_cost(from: HexTerrain, to: HexTerrain, direction: usize, cfg: &StepCostConfig) -> Option<u32> {
    if has_edge(from.wall_mask, direction) { return None; }
    let delta = to.elevation - from.elevation;
    if delta > cfg.max_walkable_up_delta || delta < -cfg.max_walkable_down_delta { return None; }
    let mut cost = cfg.base_step_cost;
    cost = cost.saturating_add(cfg.elevation_step_cost.saturating_mul(delta.unsigned_abs() as u32));
    if has_edge(from.river_mask, direction) {
        cost = cost.saturating_add(cfg.river_cross_cost);
    }
    if has_edge(from.road_mask, direction) {
        cost = cost.saturating_sub(cfg.road_discount);
    }
    Some(cost)
}

// Deterministic Dijkstra from `start` to `goal`. `neighbors` returns the 6 neighbors in
// canonical direction order (wrapped_hex_neighbors for the torus); `cost` returns None for
// impassable steps. Returns the inclusive cell path start..=goal, or None when unreachable.
// Deliberately no straight-line heuristic: the road discounts/surcharges make it inadmissible
// and the world is small (684 hexes), so the plain Dijkstra cost is negligible.
pub fn find_path<N, C>(start: HexCoord, goal: HexCoord, neighbors: N, cost: C) -> Option<Vec<HexCoord>>
where
    N: Fn(HexCoord) -> [HexCoord; 6],
    C: Fn(HexCoord, HexCoord, usize) -> Option<u32>,
{
    if start == goal { return Some(vec![start]); }

    let mut best: HashMap<HexCoord, u32> = HashMap::new();
    let mut came_from: HashMap<HexCoord, HexCoord> = HashMap::new();
    let mut frontier: BinaryHeap<Reverse<(u32, i32, i32)>> = BinaryHeap::new();
    best.insert(start, 0);
    frontier.push(Reverse((0, start.0, start.1)));

    while let Some(Reverse((dist, q, r))) = frontier.pop() {
        let current = (q, r);
        if dist > best.get(&current).copied().unwrap_or(u32::MAX) { continue; }
        if current == goal { return Some(reconstruct(&came_from, start, goal)); }

        let next_cells = neighbors(current);
        for (direction, &next) in next_cells.iter().enumerate() {
            let Some(step) = cost(current, next, direction) else { continue };
            let next_dist = dist.saturating_add(step);
            if next_dist < best.get(&next).copied().unwrap_or(u32::MAX) {
                best.insert(next, next_dist);
                came_from.insert(next, current);
                frontier.push(Reverse((next_dist, next.0, next.1)));
            }
        }
    }
    None
}

fn reconstruct(came_from: &HashMap<HexCoord, HexCoord>, start: HexCoord, goal: HexCoord) -> Vec<HexCoord> {
    let mut path = vec![goal];
    let mut current = goal;
    while current != start {
        let Some(&previous) = came_from.get(&current) else { break };
        path.push(previous);
        current = previous;
    }
    path.reverse();
    path
}

// Total cost of a path under the same neighbors/cost pair a caller used to find it. None when
// a step is now impassable (terrain changed, or the path was built for another cost model).
pub fn path_cost<N, C>(path: &[HexCoord], neighbors: N, cost: C) -> Option<u32>
where
    N: Fn(HexCoord) -> [HexCoord; 6],
    C: Fn(HexCoord, HexCoord, usize) -> Option<u32>,
{
    let mut total = 0u32;
    for window in path.windows(2) {
        let (from, to) = (window[0], window[1]);
        let direction = direction_index(&neighbors(from), to)?;
        total = total.saturating_add(cost(from, to, direction)?);
    }
    Some(total)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::world::wrap::wrapped_hex_neighbors;

    // Must match the grid shape used by wrap.rs's tests: chunk radius 1 here to keep the
    // synthetic torus small, with a 3x3 chunk grid (63 hexes).
    const RADIUS: i32 = 1;
    const COLS: i32 = 3;
    const ROWS: i32 = 3;

    fn wrapped() -> impl Fn(HexCoord) -> [HexCoord; 6] {
        move |(q, r)| wrapped_hex_neighbors(q, r, RADIUS, COLS, ROWS)
    }

    fn raw_distance(a: HexCoord, b: HexCoord) -> i32 {
        let (dq, dr) = (a.0 - b.0, a.1 - b.1);
        (dq.abs() + dr.abs() + (dq + dr).abs()) / 2
    }

    fn unit_cost(_from: HexCoord, _to: HexCoord, _direction: usize) -> Option<u32> {
        Some(1)
    }

    #[test]
    fn finds_a_straight_path() {
        let path = find_path((0, 0), (2, 0), wrapped(), unit_cost).expect("straight path missing");
        assert_eq!(path.first(), Some(&(0, 0)));
        assert_eq!(path.last(), Some(&(2, 0)));
        assert_eq!(path.len(), 3, "straight two-step path should be three cells: {path:?}");
        assert_eq!(path_cost(&path, wrapped(), unit_cost), Some(2));
    }

    #[test]
    fn detours_around_a_blocked_cell() {
        let blocked = (1, 0);
        let cost = move |_from: HexCoord, to: HexCoord, _d: usize| {
            if to == blocked { None } else { Some(1) }
        };
        let path = find_path((0, 0), (2, 0), wrapped(), cost).expect("detour path missing");
        assert_eq!(path.first(), Some(&(0, 0)));
        assert_eq!(path.last(), Some(&(2, 0)));
        assert!(!path.contains(&blocked), "path ran through the blocked cell: {path:?}");
        assert_eq!(path.len(), 4, "detour around one cell should be four cells: {path:?}");
    }

    #[test]
    fn returns_none_when_start_is_walled_in() {
        let isolated = (0, 0);
        let cost = move |from: HexCoord, _to: HexCoord, _d: usize| {
            if from == isolated { None } else { Some(1) }
        };
        assert_eq!(find_path(isolated, (2, 0), wrapped(), cost), None);
    }

    #[test]
    fn uses_wrapped_neighbors_across_the_seam() {
        // Find a canonical hex whose wrapped neighbor leaves the lap (raw coordinates jump).
        let mut seam: Option<(HexCoord, HexCoord)> = None;
        'search: for q in -40..=40 {
            for r in -40..=40 {
                let cell = (q, r);
                if crate::world::wrap::wrap_hex_coords(q, r, RADIUS, COLS, ROWS) != cell { continue; }
                for &neighbor in wrapped_hex_neighbors(q, r, RADIUS, COLS, ROWS).iter() {
                    if raw_distance(cell, neighbor) > 1 {
                        seam = Some((cell, neighbor));
                        break 'search;
                    }
                }
            }
        }
        let (start, target) = seam.expect("test grid has no displaced seam neighbor");
        let path = find_path(start, target, wrapped(), unit_cost).expect("seam path missing");
        assert_eq!(path, vec![start, target], "seam neighbor should be one wrapped step");
    }

    #[test]
    fn cliffs_rivers_and_roads_move_the_cost() {
        let cfg = StepCostConfig {
            base_step_cost: 10,
            elevation_step_cost: 5,
            river_cross_cost: 40,
            road_discount: 6,
            max_walkable_up_delta: 1,
            max_walkable_down_delta: 1,
        };
        let flat = HexTerrain { elevation: 2, ..Default::default() };
        let up_one = HexTerrain { elevation: 3, ..Default::default() };
        let up_two = HexTerrain { elevation: 4, ..Default::default() };
        let down_one = HexTerrain { elevation: 1, ..Default::default() };
        let down_two = HexTerrain { elevation: 0, ..Default::default() };
        // Edge bits are indexed from the FROM hex, so the masks live on the leading side.
        let river = HexTerrain { elevation: 2, river_mask: 1 << 2, ..Default::default() };
        let road = HexTerrain { elevation: 2, road_mask: 1 << 2, ..Default::default() };
        let wall = HexTerrain { elevation: 2, wall_mask: 1 << 2, ..Default::default() };

        assert_eq!(terrain_step_cost(flat, flat, 2, &cfg), Some(10));
        assert_eq!(terrain_step_cost(flat, up_one, 2, &cfg), Some(15));
        assert_eq!(terrain_step_cost(flat, down_one, 2, &cfg), Some(15));
        assert_eq!(terrain_step_cost(flat, up_two, 2, &cfg), None, "two-level climb must be a cliff");
        assert_eq!(terrain_step_cost(flat, down_two, 2, &cfg), None, "two-level drop must be a cliff");
        assert_eq!(terrain_step_cost(river, flat, 2, &cfg), Some(50), "river edge needs the bridge surcharge");
        assert_eq!(terrain_step_cost(river, flat, 1, &cfg), Some(10), "surcharge must key off the crossed edge");
        assert_eq!(terrain_step_cost(road, flat, 2, &cfg), Some(4), "existing road should discount the step");
        assert_eq!(terrain_step_cost(wall, flat, 2, &cfg), None, "a walled edge must be impassable");
        assert_eq!(terrain_step_cost(wall, flat, 1, &cfg), Some(10), "other edges of a walled hex stay open");
    }

    #[test]
    fn walls_seal_a_hex_through_the_real_cost_model() {
        let cfg = StepCostConfig {
            base_step_cost: 10,
            elevation_step_cost: 5,
            river_cross_cost: 40,
            road_discount: 6,
            max_walkable_up_delta: 1,
            max_walkable_down_delta: 1,
        };
        // Wall every edge of (0, 0) (both sides, as generation would) in a small terrain map.
        let mut terrain: std::collections::HashMap<HexCoord, HexTerrain> = std::collections::HashMap::new();
        let mut sealed = HexTerrain { elevation: 0, ..Default::default() };
        for d in 0..6 { crate::world::masks::set_edge(&mut sealed.wall_mask, d); }
        terrain.insert((0, 0), sealed);
        for (q, r) in crate::world::hex::hex_neighbors(0, 0) {
            terrain.insert((q, r), HexTerrain { elevation: 0, ..Default::default() });
        }
        terrain.insert((2, 0), HexTerrain { elevation: 0, ..Default::default() });
        let cost = |from: HexCoord, to: HexCoord, direction: usize| {
            terrain_step_cost(terrain.get(&from).copied().unwrap_or_default(), terrain.get(&to).copied().unwrap_or_default(), direction, &cfg)
        };
        assert_eq!(
            find_path((0, 0), (2, 0), |(q, r)| crate::world::hex::hex_neighbors(q, r), cost),
            None,
            "a fully walled hex must be unreachable",
        );
    }
}
