use hexx::{Hex, HexLayout, HexOrientation, Vec2};

pub fn hex_area(hex_radius: i32) -> i32 {
    Hex::range_count(hex_radius as u32) as i32
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

pub fn inv_hexmod(m: i32, hex_radius: i32) -> (i32, i32) {
    let rel = Hex::from_hexmod_coordinates(m as u32, hex_radius as u32);
    (rel.x, rel.y)
}

// Bijective i64 chunk index using hex spiral order. Lockstep sibling of HexMath.SpiralIndex
// in client/Scripts/World/HexMath.cs (consumed by HexGridOverlayComponent.cs and
// HexGridOverlay3DComponent.cs) — all copies must change together.
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

// Chunk owning a fine hex (hexx to_lower_res, the inverse of chunk_center_hex). Lockstep
// siblings: client HexMath.ToLowerRes (client/Scripts/World/HexMath.cs) and
// MarchingSquaresHexRingsGrid.to_lower_res (client/addons/MarchingSquaresTerrain/algorithm/
// hex_rings/marching_squares_hex_rings_grid.gd)
// — all copies must change together.
pub fn hex_to_chunk(q: i32, r: i32, hex_radius: i32) -> (i32, i32) {
    let chunk = Hex::new(q, r).to_lower_res(hex_radius as u32);
    (chunk.x, chunk.y)
}

pub fn world_to_chunk(wx: f32, wy: f32, chunk_hex_radius: i32, hex_outer_radius: f32) -> (i32, i32) {
    let (hq, hr) = world_to_hex(wx, wy, hex_outer_radius);
    hex_to_chunk(hq, hr, chunk_hex_radius)
}

// Axial deltas for the 6 hex neighbor directions, indexed 0-5, where direction D's hex_to_world
// offset has angle 60*D degrees (standard axial-coordinate neighbor set).
pub const HEX_NEIGHBOR_DELTAS: [(i32, i32); 6] = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];

// The 6 laterally neighboring hexes of (hex_q, hex_r) — the adjacency neighborhood for
// per-hex terrain generation (ground/overlay tag conflicts are checked hex-to-hex).
pub fn hex_neighbors(hex_q: i32, hex_r: i32) -> [(i32, i32); 6] {
    HEX_NEIGHBOR_DELTAS.map(|(dq, dr)| (hex_q + dq, hex_r + dr))
}

pub struct HexCell {
    pub hex_q: i32,
    pub hex_r: i32,
    pub chunk_index: i64,
    pub world_x: f32,
    pub world_y: f32,
}

pub fn enumerate_world_hexes(chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> Vec<HexCell> {
    let area = hex_area(chunk_hex_radius);
    let mut cells = Vec::with_capacity((chunk_cols * chunk_rows * area).max(0) as usize);
    for cr in 0..chunk_rows {
        for cq in 0..chunk_cols {
            let chunk_index = spiral_chunk_index(cq, cr);
            let (cx, cz) = chunk_center_hex(cq, cr, chunk_hex_radius);
            for m in 0..area {
                let (rx, rz) = inv_hexmod(m, chunk_hex_radius);
                let hex_q = cx + rx;
                let hex_r = cz + rz;
                let (world_x, world_y) = hex_to_world(hex_q, hex_r, hex_outer_radius);
                cells.push(HexCell { hex_q, hex_r, chunk_index, world_x, world_y });
            }
        }
    }
    cells
}
