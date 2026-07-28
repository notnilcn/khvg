use hexx::Hex;
use crate::main::global::DEFAULT_AOI_CHUNK_RADIUS;
use super::hex::spiral_chunk_index;
use super::wrap::wrap_chunk_coords;

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
