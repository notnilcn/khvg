use hexx::Hex;
use crate::main::global::DEFAULT_AOI_CHUNK_RADIUS;
use super::hex::{spiral_chunk_index, world_to_chunk};
use super::wrap::wrap_chunk_coords;

/// The wrapped chunk disk around (cq, cr): each AOI chunk as `(wrapped_q, wrapped_r,
/// chunk_index)`, deduped by chunk index on a torus small enough for the disk to overlap
/// itself. Iteration order is `Hex::range` order (center, then ring by ring), so callers get a
/// deterministic sequence — `surrounding_chunk_indices` is this function's index projection.
/// The explored-map reveal (player/explored.rs) needs the coords, not just the index, because
/// it stores one row per chunk.
pub fn surrounding_chunk_coords(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32, rings: Option<i32>) -> Vec<(i32, i32, i64)> {
    let rings = rings.unwrap_or(DEFAULT_AOI_CHUNK_RADIUS) as u32;
    let mut seen = std::collections::HashSet::new();
    Hex::new(cq, cr)
        .range(rings)
        .filter_map(|h| {
            let (wq, wr) = wrap_chunk_coords(h.x, h.y, chunk_cols, chunk_rows);
            let idx = spiral_chunk_index(wq, wr);
            if seen.insert(idx) { Some((wq, wr, idx)) } else { None }
        })
        .collect()
}

pub fn surrounding_chunk_indices(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32, rings: Option<i32>) -> Vec<i64> {
    surrounding_chunk_coords(cq, cr, chunk_cols, chunk_rows, rings)
        .into_iter()
        .map(|(_, _, idx)| idx)
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

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashSet;

    // The live world grid: 6x6 chunks, AOI radius 2 (19 chunks).
    const COLS: i32 = 6;
    const ROWS: i32 = 6;

    #[test]
    fn coords_and_indices_agree() {
        for (cq, cr) in [(0, 0), (3, 4), (5, 5), (13, -7), (-1, 0)] {
            let coords = surrounding_chunk_coords(cq, cr, COLS, ROWS, Some(2));
            let indices = surrounding_chunk_indices(cq, cr, COLS, ROWS, Some(2));
            assert_eq!(indices, coords.iter().map(|&(_, _, idx)| idx).collect::<Vec<_>>());
            // `None` falls back to DEFAULT_AOI_CHUNK_RADIUS (same value).
            assert_eq!(indices, surrounding_chunk_indices(cq, cr, COLS, ROWS, None));
        }
    }

    #[test]
    fn coords_are_canonical_and_deduped() {
        for (cq, cr) in [(0, 0), (5, 5), (2, 4), (-3, 11)] {
            let coords = surrounding_chunk_coords(cq, cr, COLS, ROWS, Some(2));
            let unique: HashSet<i64> = coords.iter().map(|&(_, _, idx)| idx).collect();
            assert_eq!(coords.len(), unique.len(), "duplicate chunk in disk around ({cq}, {cr})");
            for (wq, wr, idx) in &coords {
                assert_eq!(wrap_chunk_coords(*wq, *wr, COLS, ROWS), (*wq, *wr), "non-canonical coord in disk");
                assert_eq!(spiral_chunk_index(*wq, *wr), *idx, "index/coord mismatch");
            }
        }
    }

    #[test]
    fn wrap_collapses_and_dedupes_on_a_small_torus() {
        // A 1x1 grid has exactly one chunk no matter how far the disk reaches.
        assert_eq!(surrounding_chunk_coords(0, 0, 1, 1, Some(2)).len(), 1);
        // A 2x2 grid can hold at most 4 distinct chunks; the wrapped disk must dedupe to that.
        let tiny = surrounding_chunk_coords(1, 1, 2, 2, Some(3));
        let unique: HashSet<i64> = tiny.iter().map(|&(_, _, idx)| idx).collect();
        assert_eq!(tiny.len(), unique.len());
        assert!(tiny.len() <= 4);
        // Radius 2 (19 chunks) never self-overlaps on the 6x6 live grid...
        assert_eq!(surrounding_chunk_coords(5, 5, COLS, ROWS, Some(2)).len(), 19);
        // ...but radius 3 (37 chunks) does, and the wrapped result dedupes it.
        let wide = surrounding_chunk_coords(0, 0, COLS, ROWS, Some(3));
        let wide_unique: HashSet<i64> = wide.iter().map(|&(_, _, idx)| idx).collect();
        assert_eq!(wide.len(), wide_unique.len());
        assert!(wide.len() < 37 && wide.len() >= 19);
    }
}
