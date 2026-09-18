// Ground pass: pick one ground texture per hex. First pick follows the biome's noise field
// (so hexes cluster into patches); on an adjacency conflict with an already-decided neighbor,
// retry plain-random among the remaining candidates until one fits or the hex is left blank.

use spacetimedb::ReducerContext;
use crate::world::def_tables::GroundTextureConfig;
use crate::world::hex::HexCell;
use crate::world::noise::value_noise2d;
use crate::world::prng::{next_unit, weighted_pick_index, weighted_pick_index_from_unit};
use super::pick_rotation;
use super::rules::{base_tags_conflict, DecidedHexTags};

#[allow(clippy::too_many_arguments)]
pub(super) fn choose_ground(
    ctx: &ReducerContext,
    cell: &HexCell,
    ground_textures: &[GroundTextureConfig],
    ground_chances: &[f32],
    ground_noise_scale: f32,
    seed: u64,
    draw: &mut u64,
    neighbors: &[&DecidedHexTags],
) -> (String, u8, Vec<String>) {
    // First pick is noise-driven (when ground_noise_scale > 0) so nearby hexes land in the
    // same noise band and cluster into patches; a non-positive scale opts back into the old
    // pure per-hex randomness.
    let first_unit = if ground_noise_scale > 0.0 { value_noise2d(cell.world_x, cell.world_y, ground_noise_scale, seed) } else { next_unit(seed, draw) };
    let first_index = weighted_pick_index_from_unit(ground_chances, first_unit);

    // Retry-on-conflict: re-roll plain-random among the remaining candidates (clustering is a
    // nice-to-have here, a guaranteed-compatible neighbor is not) until one clears every
    // already-decided neighbor, or every candidate's exhausted and the tile is left blank.
    let mut chosen_ground: Option<&GroundTextureConfig> = None;
    if !neighbors.iter().any(|n| base_tags_conflict(ctx, &ground_textures[first_index].tags, &n.ground_tags)) {
        chosen_ground = Some(&ground_textures[first_index]);
    } else {
        let mut remaining: Vec<usize> = (0..ground_textures.len()).filter(|&i| i != first_index).collect();
        while !remaining.is_empty() {
            let weights: Vec<f32> = remaining.iter().map(|&i| ground_chances[i]).collect();
            let pick = remaining[weighted_pick_index(&weights, seed, draw)];
            if !neighbors.iter().any(|n| base_tags_conflict(ctx, &ground_textures[pick].tags, &n.ground_tags)) {
                chosen_ground = Some(&ground_textures[pick]);
                break;
            }
            remaining.retain(|&i| i != pick);
        }
    }

    match chosen_ground {
        Some(g) => (g.texture_id.clone(), pick_rotation(&g.allowed_rotations, seed, draw), g.tags.clone()),
        None => (String::new(), 0u8, Vec::new()),
    }
}
