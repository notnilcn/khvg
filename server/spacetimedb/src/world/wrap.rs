// Portions derived from hex_map_godot (135dc75) - MIT. See THIRD_PARTY_NOTICES.md.
use super::hex::{chunk_center_hex, hex_to_chunk, hex_to_world, hex_neighbors, world_to_chunk};

pub fn wrap_chunk_coords(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32) -> (i32, i32) {
    (cq.rem_euclid(chunk_cols), cr.rem_euclid(chunk_rows))
}

// Map an arbitrary hex back into the canonical lap: the hex's chunk coords wrap, and its local
// offset inside the chunk is preserved, so the wrapped hex is the same point on the torus. A
// non-positive grid size is not a torus — the input passes through unchanged.
// Lockstep siblings: client HexMath.WrapHex (client/Scripts/World/HexMath.cs) and
// MarchingSquaresHexRingsGrid.wrap_cell (client/addons/MarchingSquaresTerrain/algorithm/
// hex_rings/marching_squares_hex_rings_grid.gd)
// — all copies must change together.
pub fn wrap_hex_coords(q: i32, r: i32, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32) -> (i32, i32) {
    if chunk_cols <= 0 || chunk_rows <= 0 { return (q, r); }
    let (cq, cr) = hex_to_chunk(q, r, chunk_hex_radius);
    let (cqw, crw) = wrap_chunk_coords(cq, cr, chunk_cols, chunk_rows);
    if (cq, cr) == (cqw, crw) { return (q, r); }
    let (ccq, ccr) = chunk_center_hex(cq, cr, chunk_hex_radius);
    let (wcq, wcr) = chunk_center_hex(cqw, crw, chunk_hex_radius);
    (wcq + (q - ccq), wcr + (r - ccr))
}

// The 6 canonical-lap neighbors of (q, r): hex_neighbors mapped through wrap_hex_coords, so a
// seam hex sees its true across-the-seam neighbors as canonical coordinates (raw-coordinate
// hex_neighbors callers stay valid). Used by seam-aware terrain generation.
pub fn wrapped_hex_neighbors(q: i32, r: i32, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32) -> [(i32, i32); 6] {
    hex_neighbors(q, r).map(|(nq, nr)| wrap_hex_coords(nq, nr, chunk_hex_radius, chunk_cols, chunk_rows))
}

// Torus wrap in continuous space. If the position's chunk coord wrapped around the grid
// (cqw != cq), shift x/y by `laps` world-space periods — the period vector for one full lap
// in q (or r) is the world position of the hex at chunk (chunk_cols, 0) (or (0, chunk_rows)),
// i.e. how far one full grid traversal moves you in continuous coordinates.
pub fn wrap_world_pos(x: f32, y: f32, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> (f32, f32) {
    if chunk_cols <= 0 || chunk_rows <= 0 { return (x, y); }
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let (cqw, crw) = wrap_chunk_coords(cq, cr, chunk_cols, chunk_rows);
    if cq == cqw && cr == crw { return (x, y); }
    let laps_q = (cqw - cq) / chunk_cols;
    let laps_r = (crw - cr) / chunk_rows;
    let (pq_hq, pq_hr) = chunk_center_hex(chunk_cols, 0, chunk_hex_radius);
    let (pr_hq, pr_hr) = chunk_center_hex(0, chunk_rows, chunk_hex_radius);
    let (pq_x, pq_y) = hex_to_world(pq_hq, pq_hr, hex_outer_radius);
    let (pr_x, pr_y) = hex_to_world(pr_hq, pr_hr, hex_outer_radius);
    (x + laps_q as f32 * pq_x + laps_r as f32 * pr_x,
     y + laps_q as f32 * pq_y + laps_r as f32 * pr_y)
}

// Squared distance between two world points, accounting for torus wrap — mirrors the client's
// TorusMath.NearestCandidate. Needed whenever code compares a stored (already-wrapped) position
// against a point computed from a region/chunk center that hasn't been wrapped itself: an offset
// from that center can cross a lap seam even when the center's own chunk is canonical, so a plain
// Euclidean distance can silently never match — checking all 9 lap-shift combinations always
// finds the true nearest copy regardless of which side of a seam either point landed on.
pub fn wrapped_distance_sq(x1: f32, y1: f32, x2: f32, y2: f32, lap_q: (f32, f32), lap_r: (f32, f32)) -> f32 {
    let mut best = f32::MAX;
    for lq in -1..=1 {
        for lr in -1..=1 {
            let cx = x2 + lq as f32 * lap_q.0 + lr as f32 * lap_r.0;
            let cy = y2 + lq as f32 * lap_q.1 + lr as f32 * lap_r.1;
            let d = (cx - x1).powi(2) + (cy - y1).powi(2);
            if d < best { best = d; }
        }
    }
    best
}

#[cfg(test)]
mod tests {
    use super::*;

    // The live world grid: chunk radius 2 (19 hexes/chunk), 6x6 chunks, hex outer radius 48.
    const RADIUS: i32 = 2;
    const COLS: i32 = 6;
    const ROWS: i32 = 6;
    const OUTER: f32 = 48.0;

    // The world-space lap vectors: one full traversal of the grid in q (or r) shifts a position
    // by center(cols,0) - center(0,0) (or center(0,rows) - center(0,0)).
    fn lap_vectors() -> ((f32, f32), (f32, f32)) {
        let (oq, or) = chunk_center_hex(0, 0, RADIUS);
        let (pq, pr) = chunk_center_hex(COLS, 0, RADIUS);
        let (rq, rr) = chunk_center_hex(0, ROWS, RADIUS);
        let (ox, oy) = hex_to_world(oq, or, OUTER);
        let (px, py) = hex_to_world(pq, pr, OUTER);
        let (rx, ry) = hex_to_world(rq, rr, OUTER);
        ((px - ox, py - oy), (rx - ox, ry - oy))
    }

    #[test]
    fn wrap_hex_coords_passes_through_when_guard_tripped() {
        assert_eq!(wrap_hex_coords(123, -45, RADIUS, 0, ROWS), (123, -45));
        assert_eq!(wrap_hex_coords(123, -45, RADIUS, COLS, -2), (123, -45));
    }

    #[test]
    fn wrap_hex_coords_keeps_canonical_hexes() {
        // Chunk (0,0) center and the +q edge hex of chunk (5,5) — both already in the lap.
        assert_eq!(wrap_hex_coords(0, 0, RADIUS, COLS, ROWS), (0, 0));
        assert_eq!(wrap_hex_coords(37, 5, RADIUS, COLS, ROWS), (37, 5));
    }

    #[test]
    fn wrap_hex_coords_is_idempotent_far_out_of_lap() {
        for (q, r) in [(100, -73), (-500, 512), (1234, 5678), (-97, -401), (38, 5)] {
            let wrapped = wrap_hex_coords(q, r, RADIUS, COLS, ROWS);
            assert_eq!(
                wrap_hex_coords(wrapped.0, wrapped.1, RADIUS, COLS, ROWS),
                wrapped,
                "wrap of ({q}, {r}) is not idempotent"
            );
        }
    }

    #[test]
    fn wrapped_neighbors_of_seam_hex_are_canonical_and_distinct() {
        // (37, 5) is the +q edge hex of canonical chunk (5, 5): its +q neighbor leaves the lap.
        let (hq, hr) = (37, 5);
        let neighbors = wrapped_hex_neighbors(hq, hr, RADIUS, COLS, ROWS);

        for (i, &(nq, nr)) in neighbors.iter().enumerate() {
            assert_eq!(
                wrap_hex_coords(nq, nr, RADIUS, COLS, ROWS),
                (nq, nr),
                "wrapped neighbor {i} is not canonical"
            );
        }

        for i in 0..6 {
            for j in (i + 1)..6 {
                assert_ne!(neighbors[i], neighbors[j], "wrapped neighbors {i} and {j} collide");
            }
        }

        // Each wrapped neighbor sits one inverse-delta away from the seam hex once the lap
        // shift is canonicalized — i.e. the wrap preserves the 6-neighborhood.
        let deltas = hex_neighbors(0, 0);
        let seam = wrap_hex_coords(hq, hr, RADIUS, COLS, ROWS);
        for (i, &(nq, nr)) in neighbors.iter().enumerate() {
            let (dq, dr) = deltas[i];
            assert_eq!(
                wrap_hex_coords(nq - dq, nr - dr, RADIUS, COLS, ROWS),
                seam,
                "wrapped neighbor {i} does not canonicalize back to the seam hex"
            );
        }
    }

    #[test]
    fn wrap_hex_coords_displaces_by_lap_combination() {
        let (lap_q, lap_r) = lap_vectors();
        for (q, r) in [(100, -73), (-500, 512), (1234, 5678), (38, 5)] {
            let (wq, wr) = wrap_hex_coords(q, r, RADIUS, COLS, ROWS);
            let (cq, cr) = hex_to_chunk(q, r, RADIUS);
            let (cqw, crw) = hex_to_chunk(wq, wr, RADIUS);
            let laps_q = (cqw - cq) / COLS;
            let laps_r = (crw - cr) / ROWS;

            let (ox, oy) = hex_to_world(q, r, OUTER);
            let (wx, wy) = hex_to_world(wq, wr, OUTER);
            let expected_x = laps_q as f32 * lap_q.0 + laps_r as f32 * lap_r.0;
            let expected_y = laps_q as f32 * lap_q.1 + laps_r as f32 * lap_r.1;
            assert!(
                (wx - ox - expected_x).abs() < 0.5,
                "x displacement of ({q}, {r}) is not a lap combination"
            );
            assert!(
                (wy - oy - expected_y).abs() < 0.5,
                "y displacement of ({q}, {r}) is not a lap combination"
            );
        }
    }
}
