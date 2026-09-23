// Per-edge terrain masks (HexTile.river_mask / road_mask / wall_mask): one bit per edge, the
// bit index being the HEX_NEIGHBOR_DELTAS direction (world/hex.rs). The two hexes sharing an
// edge carry the same edge at mirrored indices — direction d on one side is (d + 3) % 6 on
// the other — and generation always writes both sides together, so a half-set edge never
// exists. These helpers are deliberately table-free so pathing/road code and their tests stay
// standalone.

pub const EDGE_COUNT: usize = 6;

// The mirrored edge index on the far side of the shared edge (its own inverse).
pub fn opposite_direction(direction: usize) -> usize {
    debug_assert!(direction < EDGE_COUNT, "edge direction out of range: {direction}");
    (direction + 3) % EDGE_COUNT
}

pub fn has_edge(mask: u8, direction: usize) -> bool {
    mask & (1u8 << direction) != 0
}

pub fn set_edge(mask: &mut u8, direction: usize) {
    *mask |= 1u8 << direction;
}

pub fn clear_edge(mask: &mut u8, direction: usize) {
    *mask &= !(1u8 << direction);
}

// The direction index of `target` within a neighbor list (wrapped_hex_neighbors order).
// None when `target` is not one of the 6 neighbors.
pub fn direction_index(neighbors: &[(i32, i32); EDGE_COUNT], target: (i32, i32)) -> Option<usize> {
    neighbors.iter().position(|&n| n == target)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn opposite_direction_is_an_involution_pairing() {
        for d in 0..EDGE_COUNT {
            assert_eq!(opposite_direction(opposite_direction(d)), d, "opposite of {d} is not its own inverse");
            assert_ne!(opposite_direction(d), d, "edge {d} maps to itself");
        }
        assert_eq!(opposite_direction(0), 3);
        assert_eq!(opposite_direction(1), 4);
        assert_eq!(opposite_direction(2), 5);
        assert_eq!(opposite_direction(3), 0);
        assert_eq!(opposite_direction(4), 1);
        assert_eq!(opposite_direction(5), 2);
    }

    #[test]
    fn edge_bits_encode_and_decode_round_trip() {
        let mut mask = 0u8;
        for d in 0..EDGE_COUNT {
            set_edge(&mut mask, d);
            assert!(has_edge(mask, d), "edge {d} did not survive set_edge");
        }
        assert_eq!(mask, 0b0011_1111, "six set edges do not fill the low six bits");
        for d in 0..EDGE_COUNT {
            assert!(has_edge(mask, d));
            clear_edge(&mut mask, d);
            assert!(!has_edge(mask, d), "edge {d} did not survive clear_edge");
        }
        assert_eq!(mask, 0);
    }

    #[test]
    fn direction_index_finds_neighbor_by_coordinate() {
        let neighbors = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];
        for (d, &n) in neighbors.iter().enumerate() {
            assert_eq!(direction_index(&neighbors, n), Some(d));
        }
        assert_eq!(direction_index(&neighbors, (2, 0)), None);
    }
}
