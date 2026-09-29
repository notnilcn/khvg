//! MST test fixture: the hand-authored scene `client/Scenes/mst_test.tscn` recreated as
//! server-generated terrain at chunks (0, 0) and (0, 1).
//!
//! The arrays below are copied verbatim from the scene's chunk metadata
//! (`client/Scenes/mst_test_TerrainData/MarchingSquaresTerrain_e224d986/hex_rings/chunk_<q>_<r>/
//! metadata.res`): `height_map` (world-unit heights at the scene's cell_size 2.0) and
//! `smooth_map` (the addon's per-cell smooth flag), both row-major 5×5 bounding-box arrays
//! (chunk_hex_radius 2; the box corners outside the hex disk are not cells and are skipped).
//!
//! The addon reproduces the scene exactly. The server recreation differs in one deliberate way:
//! every stored height is a discrete whole world unit. The editor height is first scaled by the
//! ratio of the in-game hex spacing to the scene's cell size (sqrt(3) × hex_outer_radius /
//! MST_TEST_CELL_SIZE — the client sets the runtime MST cell_size.x to sqrt(3) × outer radius)
//! and then rounded. The client mirrors the same ratio as `TerrainComponent3D.FixtureHeightScale`
//! (which scales the chunk merge threshold) — the two copies change together.

use std::collections::HashMap;
use crate::world::hex::{chunk_center_hex, HEX_NEIGHBOR_DELTAS};
use crate::world::instance_tables::HexTile;
use crate::world::masks::{clear_edge, opposite_direction};
use crate::world::tables::MapConfig;

/// The editor scene's cell_size — MarchingSquaresTerrain's 2.0 default, unchanged in
/// `mst_test.tscn`. The fixture's height scale is derived from it (see the module docs).
pub const MST_TEST_CELL_SIZE: f32 = 2.0;

/// The scene was authored at chunk_hex_radius 2, so its metadata bbox is (2r+1)² = 5×5.
const MST_TEST_CHUNK_RADIUS: i32 = 2;
const MST_TEST_SIDE: i32 = 2 * MST_TEST_CHUNK_RADIUS + 1;

/// One authored chunk of the fixture scene.
struct MstTestChunk {
    chunk_q: i32,
    chunk_r: i32,
    /// The addon's `height_map`, row-major bbox (scene world units).
    height_map: [f32; 25],
    /// The addon's `smooth_map`, row-major bbox (1 = bridge-blended top).
    smooth_map: [u8; 25],
}

const MST_TEST_CHUNKS: [MstTestChunk; 2] = [
    MstTestChunk {
        chunk_q: 0,
        chunk_r: 0,
        height_map: [
            0.0, 0.0, 0.0, 0.00000095367432, 0.00000095367432,
            0.0, 0.0, 0.00000190734863, 0.67956740331473, 0.42479991912842,
            0.0, 0.00000095367432, 0.46049976348877, 1.5950708843867, 0.0,
            0.00000095367432, 0.38424777984619, 0.97718334197998, 2.46236897320273, 0.0,
            0.0, -0.00000095367432, 0.31976795196533, 0.0, 0.0,
        ],
        smooth_map: [
            0, 0, 0, 1, 1,
            0, 0, 1, 1, 1,
            0, 1, 1, 1, 1,
            1, 1, 1, 1, 0,
            1, 1, 1, 0, 0,
        ],
    },
    MstTestChunk {
        chunk_q: 0,
        chunk_r: 1,
        height_map: [
            0.0, 0.0, 0.41569042205811, 0.0, 0.0,
            0.0, 3.32966677466763, 4.48808807627045, 4.87489241409302, 0.0,
            1.13966941833496, 4.87489986419678, 4.87489986419678, 4.87489241409302, 0.0,
            0.20889700295976, 4.87489241409302, 4.87489241409302, 0.0, 0.0,
            0.0, 0.0, 0.0, 0.0, 0.0,
        ],
        smooth_map: [
            0, 0, 1, 1, 0,
            0, 1, 1, 0, 0,
            1, 1, 1, 0, 0,
            1, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
        ],
    },
];

/// A fixture cell resolved to game units.
pub struct MstTestCell {
    pub height: i16,
    pub smooth: bool,
}

/// Fixture cell at a canonical hex, or `None` when the hex is not part of the fixture (or the
/// world's chunk layout no longer matches the scene's radius).
pub fn cell_at(hex_q: i32, hex_r: i32, cfg: &MapConfig) -> Option<MstTestCell> {
    if cfg.chunk_hex_radius != MST_TEST_CHUNK_RADIUS {
        return None;
    }
    for chunk in &MST_TEST_CHUNKS {
        let (center_q, center_r) = chunk_center_hex(chunk.chunk_q, chunk.chunk_r, MST_TEST_CHUNK_RADIUS);
        let lx = hex_q - center_q + MST_TEST_CHUNK_RADIUS;
        let lz = hex_r - center_r + MST_TEST_CHUNK_RADIUS;
        if !has_local(lx, lz, MST_TEST_CHUNK_RADIUS) {
            continue;
        }
        let idx = (lz * MST_TEST_SIDE + lx) as usize;
        return Some(MstTestCell {
            height: quantize(chunk.height_map[idx], cfg.hex_outer_radius),
            smooth: chunk.smooth_map[idx] != 0,
        });
    }
    None
}

/// Scene units → game world units: the in-game center-to-center hex spacing
/// (sqrt(3) × hex_outer_radius) divided by the scene's cell size.
pub fn height_scale(hex_outer_radius: f32) -> f32 {
    3.0f32.sqrt() * hex_outer_radius / MST_TEST_CELL_SIZE
}

/// Clears river/road/wall edge bits on every fixture hex and the mirrored bits on its six
/// neighbours, preserving the masks.rs both-sides invariant. The authored scene has no
/// rivers/roads — without this the world's mask passes leave floating strips over it.
pub fn strip_edge_masks(tiles: &mut HashMap<(i32, i32), HexTile>, cfg: &MapConfig) {
    if cfg.chunk_hex_radius != MST_TEST_CHUNK_RADIUS {
        return;
    }
    for (hex_q, hex_r) in fixture_cells() {
        if let Some(tile) = tiles.get_mut(&(hex_q, hex_r)) {
            tile.river_mask = 0;
            tile.road_mask = 0;
            tile.wall_mask = 0;
        }
        for (d, (dq, dr)) in HEX_NEIGHBOR_DELTAS.iter().enumerate() {
            if let Some(tile) = tiles.get_mut(&(hex_q + dq, hex_r + dr)) {
                let mirrored = opposite_direction(d);
                clear_edge(&mut tile.river_mask, mirrored);
                clear_edge(&mut tile.road_mask, mirrored);
                clear_edge(&mut tile.wall_mask, mirrored);
            }
        }
    }
}

/// Every fixture hex (canonical axial coords) across both chunks.
pub fn fixture_cells() -> Vec<(i32, i32)> {
    let mut cells = Vec::with_capacity(2 * 19);
    for chunk in &MST_TEST_CHUNKS {
        let (center_q, center_r) = chunk_center_hex(chunk.chunk_q, chunk.chunk_r, MST_TEST_CHUNK_RADIUS);
        for dq in -MST_TEST_CHUNK_RADIUS..=MST_TEST_CHUNK_RADIUS {
            for dr in -MST_TEST_CHUNK_RADIUS..=MST_TEST_CHUNK_RADIUS {
                if in_disk(dq, dr, MST_TEST_CHUNK_RADIUS) {
                    cells.push((center_q + dq, center_r + dr));
                }
            }
        }
    }
    cells
}

/// Scene height → discrete whole world units.
fn quantize(raw_height: f32, hex_outer_radius: f32) -> i16 {
    (raw_height * height_scale(hex_outer_radius)).round() as i16
}

/// Hex-disk membership over axial offsets (|dq|, |dr|, |dq + dr| all within the radius).
fn in_disk(dq: i32, dr: i32, radius: i32) -> bool {
    dq.abs() <= radius && dr.abs() <= radius && (dq + dr).abs() <= radius
}

/// Bounding-box local cell test, lockstep with the addon's
/// MarchingSquaresHexRingsGrid.has_local (chunk disks exclude the box corners).
fn has_local(lx: i32, lz: i32, radius: i32) -> bool {
    in_disk(lx - radius, lz - radius, radius)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::main::global::{DEFAULT_CHUNK_HEX_RADIUS, DEFAULT_HEX_OUTER_RADIUS};

    fn cfg() -> MapConfig {
        MapConfig {
            id: 0,
            chunk_hex_radius: DEFAULT_CHUNK_HEX_RADIUS,
            chunk_cols: 6,
            chunk_rows: 6,
            hex_outer_radius: DEFAULT_HEX_OUTER_RADIUS,
            lap_q_x: 0.0,
            lap_q_y: 0.0,
            lap_r_x: 0.0,
            lap_r_y: 0.0,
        }
    }

    #[test]
    fn fixture_hexes_match_the_scene_scaled_and_quantized() {
        // Chunk (0, 0) center hex: local (2, 2) -> index 12, height 0.4605 -> 19, smooth 1.
        let cell = cell_at(0, 0, &cfg()).unwrap();
        assert_eq!(cell.height, 19);
        assert!(cell.smooth);

        // Chunk (0, 1) center hex (2, 3): local (2, 2) -> index 12, height 4.8749 -> 203,
        // smooth 1.
        let cell = cell_at(2, 3, &cfg()).unwrap();
        assert_eq!(cell.height, 203);
        assert!(cell.smooth);

        // Chunk (0, 0) hex (1, 1): local (3, 3) -> index 18, height 2.4624 -> 102, smooth 1.
        let cell = cell_at(1, 1, &cfg()).unwrap();
        assert_eq!(cell.height, 102);
        assert!(cell.smooth);

        // Chunk (0, 1) hex (1, 2): local (1, 1) -> index 6, height 3.3297 -> 138, smooth 1.
        let cell = cell_at(1, 2, &cfg()).unwrap();
        assert_eq!(cell.height, 138);
        assert!(cell.smooth);

        // A non-smooth fixture cell: chunk (0, 1) hex (3, 2) local (3, 1) -> index 8, 4.8749
        // -> 203.
        let cell = cell_at(3, 2, &cfg()).unwrap();
        assert_eq!(cell.height, 203);
        assert!(!cell.smooth);
    }

    #[test]
    fn hexes_outside_the_fixture_chunks_are_absent() {
        assert!(cell_at(10, 10, &cfg()).is_none());
        // A disk corner of the fixture chunks' bbox is not a cell.
        assert!(cell_at(0, -3, &cfg()).is_none()); // chunk (0,0) bbox corner (2, -1) local
        let mut other = cfg();
        other.chunk_hex_radius = 1;
        assert!(cell_at(0, 0, &other).is_none());
    }

    fn hex_tile(q: i32, r: i32) -> HexTile {
        HexTile {
            tile_id: 0,
            hex_q: q,
            hex_r: r,
            chunk_index: 0,
            texture_id: String::new(),
            rotation: 0,
            overlay_texture_id: None,
            overlay_rotation: 0,
            overlay_scale: 1.0,
            biome_id: String::new(),
            region_id: 0,
            river_mask: 0,
            road_mask: 0,
            wall_mask: 0,
        }
    }

    #[test]
    fn strip_edge_masks_clears_fixture_and_mirrored_neighbour_bits() {
        let mut tiles = HashMap::new();
        // A fixture edge whose far side is outside the fixture: (2, 0) - (3, 0), direction 0.
        let mut fixture = hex_tile(2, 0);
        fixture.river_mask = 1 << 0;
        let mut outside = hex_tile(3, 0);
        outside.river_mask = 1 << 3; // mirrored edge
        outside.road_mask = 1 << 5; // unrelated road bit must survive
        tiles.insert((2, 0), fixture);
        tiles.insert((3, 0), outside);

        strip_edge_masks(&mut tiles, &cfg());

        assert_eq!(tiles[&(2, 0)].river_mask, 0);
        assert_eq!(tiles[&(3, 0)].river_mask, 0);
        assert_eq!(tiles[&(3, 0)].road_mask, 1 << 5);
    }
}
