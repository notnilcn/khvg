// Overlay pass: pick at most one overlay per wedge, tag-compatible with the ground texture the
// ground pass just chose. Same noise-clustering + retry-on-conflict shape as the ground pass,
// but on its own decorrelated noise field and with "pick nothing" as a real outcome.

use spacetimedb::ReducerContext;
use crate::world::def_tables::OverlayTextureConfig;
use crate::world::hex::HexCell;
use crate::world::noise::{value_noise2d, OVERLAY_NOISE_SEED_OFFSET};
use crate::world::prng::{next_unit, weighted_pick_with_remainder, weighted_pick_with_remainder_from_unit};
use super::pick_rotation;
use super::rules::{overlay_compatible_with_base, overlay_tags_conflict, DecidedWedgeTags};

#[allow(clippy::too_many_arguments)]
pub(super) fn choose_overlay(
    ctx: &ReducerContext,
    cell: &HexCell,
    overlay_textures: &[OverlayTextureConfig],
    overlay_noise_scale: f32,
    ground_tags: &[String],
    seed: u64,
    draw: &mut u64,
    neighbors: &[&DecidedWedgeTags],
) -> (Option<String>, u8, f32, Vec<String>) {
    // Eligible: tag-compatible with the ground texture actually chosen above (LayeringRule).
    // A base with fewer compatible overlays naturally ends up with a lower overall overlay
    // chance — no separate per-base modifier needed.
    let eligible: Vec<&OverlayTextureConfig> = overlay_textures.iter()
        .filter(|o| overlay_compatible_with_base(ctx, &o.tags, ground_tags))
        .collect();

    let mut overlay_texture_id: Option<String> = None;
    let mut overlay_rotation: u8 = 0;
    let mut overlay_scale: f32 = 0.0;
    let mut overlay_tags: Vec<String> = Vec::new();
    if !eligible.is_empty() {
        // First pick is noise-driven (when overlay_noise_scale > 0), same idea as ground's
        // clustering above but on its own decorrelated field, so debris reads as denser in
        // some sections and sparser in others instead of an independent roll per wedge.
        let eligible_chances: Vec<f32> = eligible.iter().map(|o| o.chance).collect();
        let first_unit = if overlay_noise_scale > 0.0 { value_noise2d(cell.world_x, cell.world_y, overlay_noise_scale, seed ^ OVERLAY_NOISE_SEED_OFFSET) } else { next_unit(seed, draw) };
        let first_picked = weighted_pick_with_remainder_from_unit(&eligible_chances, first_unit);

        let mut chosen: Option<usize> = first_picked.filter(|&i| !neighbors.iter().any(|n| overlay_tags_conflict(ctx, &eligible[i].tags, &n.overlay_tags)));
        if chosen.is_none() && let Some(conflicting) = first_picked {
            // Retry-on-conflict: re-roll plain-random among the remaining eligible
            // overlays until one clears every already-decided neighbor, or give up
            // (no overlay) — landing in weighted_pick_with_remainder's "leftover" zone
            // isn't a conflict, so it's not retried here.
            let mut remaining: Vec<usize> = (0..eligible.len()).filter(|&i| i != conflicting).collect();
            while !remaining.is_empty() {
                let chances: Vec<f32> = remaining.iter().map(|&i| eligible[i].chance).collect();
                let Some(picked) = weighted_pick_with_remainder(&chances, seed, draw) else { break };
                let pick = remaining[picked];
                if !neighbors.iter().any(|n| overlay_tags_conflict(ctx, &eligible[pick].tags, &n.overlay_tags)) {
                    chosen = Some(pick);
                    break;
                }
                remaining.retain(|&i| i != pick);
            }
        }

        if let Some(i) = chosen {
            let overlay = eligible[i];
            overlay_rotation = pick_rotation(&overlay.allowed_rotations, seed, draw);
            overlay_texture_id = Some(overlay.texture_id.clone());
            overlay_scale = overlay.scale;
            overlay_tags = overlay.tags.clone();
        }
    }

    (overlay_texture_id, overlay_rotation, overlay_scale, overlay_tags)
}
