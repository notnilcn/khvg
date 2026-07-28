use super::hex::{chunk_center_hex, hex_to_world, world_to_chunk};

pub fn wrap_chunk_coords(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32) -> (i32, i32) {
    (cq.rem_euclid(chunk_cols), cr.rem_euclid(chunk_rows))
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
