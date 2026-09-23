//! Per-profile explored-map snapshots (PLAN §4.2).
//!
//! `reveal_aoi` snapshots the terrain AOI disk — the same `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`
//! chunk rings `nearby_terrain_tiles` streams live — into per-chunk `PlayerExploredChunk`
//! rows. It runs on every real chunk crossing (`report_movement` / `teleport_player`) and
//! once at `join_world` (via `try_scaffold_profile`), so the set-diff it does is bounded by
//! the profile's own rows (≤ 19 today — a 2-ring disk).

use std::collections::{HashMap, HashSet};
use spacetimedb::{ReducerContext, Table};
use crate::main::global::DEFAULT_TERRAIN_AOI_CHUNK_RADIUS;
use crate::world::aoi::surrounding_chunk_coords;
use crate::world::instance_tables::*;
use crate::world::tables::*;
use super::tables::*;

/// Pure payload assembly for one chunk: maps the chunk's `HexTile` rows through the
/// `(hex_q, hex_r) -> elevation` lookup and sorts by `(hex_q, hex_r)`, so a reveal's row
/// contents depend only on the world rows, never on scan/iteration order. Hexes with no
/// `HexTile` row are absent by construction; a hex with no `BuildingTile` row (or one with
/// no indexed elevation) defaults to elevation 0.
fn assemble_explored_hexes(
    tiles: impl Iterator<Item = HexTile>,
    elevations: &HashMap<(i32, i32), i16>,
) -> Vec<ExploredHex> {
    let mut hexes: Vec<ExploredHex> = tiles
        .map(|tile| ExploredHex {
            hex_q: tile.hex_q,
            hex_r: tile.hex_r,
            texture_id: tile.texture_id,
            rotation: tile.rotation,
            overlay_texture_id: tile.overlay_texture_id,
            overlay_rotation: tile.overlay_rotation,
            overlay_scale: tile.overlay_scale,
            elevation: elevations.get(&(tile.hex_q, tile.hex_r)).copied().unwrap_or(0),
            river_mask: tile.river_mask,
            road_mask: tile.road_mask,
            wall_mask: tile.wall_mask,
        })
        .collect();
    hexes.sort_unstable_by_key(|hex| (hex.hex_q, hex.hex_r));
    hexes
}

/// Reveals the AOI chunk disk around (`chunk_q`, `chunk_r`) for `profile_id`: inserts one
/// `PlayerExploredChunk` snapshot per chunk of the disk that the profile has not revealed yet.
/// Already-revealed chunks are skipped (set-diff over the profile's own rows), so moving one
/// chunk inserts only the newly entered outer ring. `MapConfig` uses the 1/1 fallback: a
/// degenerate grid still runs the AOI math.
pub fn reveal_aoi(ctx: &ReducerContext, profile_id: u64, chunk_q: i32, chunk_r: i32) {
    let cfg = MapConfig::load(ctx, 1, 1);
    let aoi = surrounding_chunk_coords(chunk_q, chunk_r, cfg.chunk_cols, cfg.chunk_rows, Some(DEFAULT_TERRAIN_AOI_CHUNK_RADIUS));

    let revealed: HashSet<i64> = ctx.db.player_explored_chunk().profile_id().filter(&profile_id)
        .map(|row| row.chunk_index)
        .collect();

    for (cq, cr, chunk_index) in aoi {
        if revealed.contains(&chunk_index) { continue; }
        // HexTile is the render payload, BuildingTile the per-hex elevation. Both are keyed
        // by chunk_index (btree), so a reveal reads only the chunks it is inserting.
        let elevations: HashMap<(i32, i32), i16> = ctx.db.building_tile().chunk_index().filter(&chunk_index)
            .map(|tile| ((tile.hex_q, tile.hex_r), tile.elevation))
            .collect();
        let hexes = assemble_explored_hexes(
            ctx.db.hex_tile().chunk_index().filter(&chunk_index),
            &elevations,
        );
        ctx.db.player_explored_chunk().insert(PlayerExploredChunk {
            explored_id: 0,
            profile_id,
            chunk_q: cq,
            chunk_r: cr,
            chunk_index,
            revealed_at: ctx.timestamp,
            hexes,
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn tile_at(q: i32, r: i32, texture_id: &str) -> HexTile {
        HexTile {
            tile_id: 0,
            hex_q: q,
            hex_r: r,
            chunk_index: 0,
            texture_id: texture_id.to_string(),
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
    fn payload_is_sorted_with_defaulted_elevation() {
        // Deliberately unordered input (a table scan order the caller doesn't control).
        let tiles = vec![tile_at(2, 0, "b"), tile_at(0, 1, "a"), tile_at(1, -1, "c")];
        let mut elevations = HashMap::new();
        elevations.insert((2, 0), 5);
        elevations.insert((0, 1), 3);

        let hexes = assemble_explored_hexes(tiles.into_iter(), &elevations);
        let keys: Vec<(i32, i32)> = hexes.iter().map(|h| (h.hex_q, h.hex_r)).collect();
        assert_eq!(keys, vec![(0, 1), (1, -1), (2, 0)]);
        let elevation = |q: i32, r: i32| hexes.iter().find(|h| (h.hex_q, h.hex_r) == (q, r)).unwrap().elevation;
        assert_eq!(elevation(0, 1), 3);
        assert_eq!(elevation(2, 0), 5);
        assert_eq!(elevation(1, -1), 0); // no BuildingTile row
    }
}
