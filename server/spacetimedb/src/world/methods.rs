use hexx::{Hex, HexLayout, HexOrientation, Vec2};

pub const BASE_MAX_HP: i32 = 100;
pub const HP_PER_LEVEL: i32 = 5;
pub const DEFAULT_CHUNK_HEX_RADIUS: i32 = 5;
pub const DEFAULT_CHUNK_COLS: i32 = 30;
pub const DEFAULT_CHUNK_ROWS: i32 = 30;
pub const DEFAULT_HEX_OUTER_RADIUS: f32 = 32.0;
pub const DEFAULT_AOI_CHUNK_RADIUS: i32 = 3;
pub const BEHAVIOR_TICK_DT: f32 = 0.1;
pub const LOOT_DROP_EXPIRY: u64 = 300;
pub const CONSUMABLE_EFFECT_TICK_SECONDS: f32 = 1.0;

pub fn hex_area(hex_radius: i32) -> i32 {
    Hex::range_count(hex_radius as u32) as i32
}

pub fn hex_shift(hex_radius: i32) -> i32 {
    3 * hex_radius + 2
}

pub fn world_to_hex(wx: f32, wy: f32, r_outer: f32) -> (i32, i32) {
    let layout = HexLayout { orientation: HexOrientation::Pointy, scale: Vec2::splat(r_outer), origin: Vec2::ZERO };
    let h = layout.world_pos_to_hex(Vec2::new(wx, wy));
    (h.x, h.y)
}

pub fn hex_to_world(hq: i32, hr: i32, r_outer: f32) -> (f32, f32) {
    let layout = HexLayout { orientation: HexOrientation::Pointy, scale: Vec2::splat(r_outer), origin: Vec2::ZERO };
    let pos = layout.hex_to_world_pos(Hex::new(hq, hr));
    (pos.x, pos.y)
}

pub fn chunk_center_hex(cq: i32, cr: i32, hex_radius: i32) -> (i32, i32) {
    let center = Hex::new(cq, cr).to_higher_res(hex_radius as u32);
    (center.x, center.y)
}

pub fn inv_hexmod(m: i32, hex_radius: i32, _shift: i32) -> (i32, i32) {
    let rel = Hex::from_hexmod_coordinates(m as u32, hex_radius as u32);
    (rel.x, rel.y)
}

// Bijective i64 chunk index using hex spiral order. Mirrors _hex_spiral_index in hex_grid_overlay.gd.
pub fn spiral_chunk_index(cq: i32, cr: i32) -> i64 {
    let x = cq;
    let z = cr;
    let y = -cq - cr;
    let rho = x.abs().max(y.abs()).max(z.abs());
    if rho == 0 { return 0; }
    let b = (3 * rho * (rho - 1) + 1) as i64;
    let (arm, step): (i64, i64) =
        if z == -rho && x > 0                  { (0, y as i64) }
        else if y == rho && x > -rho            { (1, -x as i64) }
        else if x == -rho && z < rho            { (2, z as i64) }
        else if z == rho && x < 0              { (3, (x + rho) as i64) }
        else if y == -rho && x >= 0 && x < rho { (4, x as i64) }
        else                                    { (5, (y + rho) as i64) };
    b + arm * rho as i64 + step
}

pub fn world_to_chunk(wx: f32, wy: f32, chunk_hex_radius: i32, hex_outer_radius: f32) -> (i32, i32) {
    let (hq, hr) = world_to_hex(wx, wy, hex_outer_radius);
    let chunk = Hex::new(hq, hr).to_lower_res(chunk_hex_radius as u32);
    (chunk.x, chunk.y)
}

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

pub fn surrounding_chunk_indices(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32, rings: Option<i32>) -> Vec<i64> {
    let rings = rings.unwrap_or(DEFAULT_AOI_CHUNK_RADIUS) as u32;
    let mut seen = std::collections::HashSet::new();
    Hex::new(cq, cr)
        .range(rings)
        .filter_map(|h| {
            let (wq, wr) = wrap_chunk_coords(h.x, h.y, chunk_cols, chunk_rows);
            let idx = spiral_chunk_index(wq, wr);
            if seen.insert(idx) { Some(idx) } else { None }
        })
        .collect()
}
