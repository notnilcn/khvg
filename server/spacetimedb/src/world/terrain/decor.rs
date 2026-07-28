// Decor pass: at most one decor prop per hex, rolled once per cell right after the ground and
// overlay passes for that same cell — decor's ground/overlay check reads that hex's own 6
// wedges, which those passes just finished deciding.

use hexx::Hex;
use spacetimedb::{ReducerContext, Table};
use crate::world::def_tables::DecorConfig;
use crate::world::hex::HexCell;
use crate::world::instance_tables::{hex_decor, HexDecor};
use crate::world::noise::{value_noise2d, DECOR_NOISE_SEED_OFFSET};
use crate::world::prng::{next_unit, weighted_pick_with_remainder_from_unit};
use super::rules::{decor_ground_conflict, NeighborTagMap};

#[allow(clippy::too_many_arguments)]
pub(super) fn place_decor(ctx: &ReducerContext, cell: &HexCell, biome_id: &str, region_id: u64, decor_configs: &[DecorConfig], decor_noise_scale: f32, decor_min_separation: i32, seed: u64, draw: &mut u64, neighbor_tags: &NeighborTagMap, decided_decor: &mut std::collections::HashSet<(i32, i32)>) {
    if decor_configs.is_empty() { return; }

    // Union of ground+overlay tags across all 6 of this hex's wedges — every tri_index is already
    // decided by the ground/overlay passes that just ran for this same cell.
    let mut hex_tags: Vec<String> = Vec::new();
    for tri in 0..6u8 {
        if let Some(d) = neighbor_tags.get(&(cell.hex_q, cell.hex_r, tri)) {
            for t in d.ground_tags.iter().chain(d.overlay_tags.iter()) {
                if !hex_tags.contains(t) { hex_tags.push(t.clone()); }
            }
        }
    }

    let eligible: Vec<&DecorConfig> = decor_configs.iter().filter(|d| !decor_ground_conflict(ctx, &d.tags, &hex_tags)).collect();
    if eligible.is_empty() { return; }

    // Separation is type-agnostic — if anything's already decided nearby, no decor goes here at
    // all regardless of which type would otherwise get rolled, so this is checked before (and
    // instead of) a retry-on-conflict loop like the ground/overlay rules use.
    if decor_min_separation > 0 {
        let center = Hex::new(cell.hex_q, cell.hex_r);
        let blocked = center.range(decor_min_separation as u32).any(|h| h != center && decided_decor.contains(&(h.x, h.y)));
        if blocked { return; }
    }

    let chances: Vec<f32> = eligible.iter().map(|d| d.chance).collect();
    let unit = if decor_noise_scale > 0.0 { value_noise2d(cell.world_x, cell.world_y, decor_noise_scale, seed ^ DECOR_NOISE_SEED_OFFSET) } else { next_unit(seed, draw) };
    let Some(i) = weighted_pick_with_remainder_from_unit(&chances, unit) else { return };
    let decor = eligible[i];

    decided_decor.insert((cell.hex_q, cell.hex_r));
    ctx.db.hex_decor().insert(HexDecor {
        decor_id: 0, hex_q: cell.hex_q, hex_r: cell.hex_r, chunk_index: cell.chunk_index,
        texture_id: decor.texture_id.clone(), shadow_texture_id: decor.shadow_texture_id.clone(),
        // Decor art has its shadow baked into a fixed relative position — rotating would break
        // that relationship, so every placement stays upright.
        rotation_degrees: 0.0, biome_id: biome_id.to_string(), region_id,
    });
}
