use hexx::Hex;
use crate::main::global::DEFAULT_AOI_CHUNK_RADIUS;
use super::hex::{spiral_chunk_index, world_to_chunk};
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

/// Chunk-index superset of a world-space circle, for reducer-side AOI queries
/// (enemies_near_point / players_near_point). The ring estimate divides by a deliberately
/// small chunk radius so the returned chunks always cover the circle — callers must still
/// apply an exact wrapped-distance filter per row.
pub fn chunk_indices_covering(x: f32, y: f32, radius: f32, chunk_hex_radius: i32, hex_outer_radius: f32, chunk_cols: i32, chunk_rows: i32) -> Vec<i64> {
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let chunk_radius = chunk_hex_radius.max(1) as f32 * hex_outer_radius;
    let rings = (radius / chunk_radius).ceil().max(0.0) as i32;
    surrounding_chunk_indices(cq, cr, chunk_cols, chunk_rows, Some(rings))
}
