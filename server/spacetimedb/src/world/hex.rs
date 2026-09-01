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

pub fn world_to_chunk(wx: f32, wy: f32, chunk_hex_radius: i32, hex_outer_radius: f32) -> (i32, i32) {
    let (hq, hr) = world_to_hex(wx, wy, hex_outer_radius);
    let chunk = Hex::new(hq, hr).to_lower_res(chunk_hex_radius as u32);
    (chunk.x, chunk.y)
}

// Axial deltas for the 6 hex neighbor directions, indexed 0-5, where direction D's hex_to_world
// offset has angle 60*D degrees (standard axial-coordinate neighbor set).
const HEX_NEIGHBOR_DELTAS: [(i32, i32); 6] = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];

// A wedge's same-hex neighbors are always the two adjacent tri_index values — wedges tile the hex
// with no gaps (TerrainManager.cs's GetWedgeVertices: wedge N's second vertex is wedge N+1's first),
// so wedge N borders wedge N-1 and wedge N+1 within the same hex.
pub fn same_hex_neighbor_indices(tri_index: u8) -> [u8; 2] {
    [(tri_index + 5) % 6, (tri_index + 1) % 6]
}

// A wedge's outer edge (the one opposite the hex center) points in hex-direction (tri_index+1)%6:
// TerrainManager.cs's wedge vertices sit at angles 30+60*triIndex / 30+60*(triIndex+1), so the
// outer edge's midpoint angle is 60*(triIndex+1) — exactly direction (triIndex+1)%6's offset angle
// in the table above. Crossing that edge lands on the wedge that shares it from the neighbor hex's
// side: directions 180 degrees apart differ by 3 in this 6-direction scheme, so that wedge is
// (tri_index+3)%6 of the neighbor hex.
pub fn cross_hex_neighbor(hex_q: i32, hex_r: i32, tri_index: u8) -> (i32, i32, u8) {
    let (dq, dr) = HEX_NEIGHBOR_DELTAS[((tri_index + 1) % 6) as usize];
    (hex_q + dq, hex_r + dr, (tri_index + 3) % 6)
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
