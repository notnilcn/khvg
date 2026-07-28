// Voronoi seed distribution: turns a World's biome weights (and a biome's region weights) into
// seed points scattered over the map, and buckets every world hex under its nearest seed.
// Wrap-aware throughout — see nearest_seed_index.

use crate::main::global::BIOME_VORONOI_SEED_BUDGET;
use crate::world::def_tables::BiomeWeight;
use crate::world::hex::HexCell;
use crate::world::prng::pick_index;
use crate::world::wrap::wrapped_distance_sq;

pub fn validate_weights_sum_100(weights: &[f32]) -> Result<(), String> {
    let sum: f32 = weights.iter().sum();
    if (sum - 100.0).abs() > 0.5 {
        return Err(format!("Weights must sum to 100%, got {:.2}%", sum));
    }
    Ok(())
}

// Wrap-aware: with only one Voronoi seed (the original single-biome case) which seed "wins" never
// mattered, but with multiple seeds a plain Euclidean nearest-seed check is wrong on a torus world
// — a seed near a wrap seam would see its closest seed as artificially far away across that seam,
// ceding it a disproportionate share of the map to whichever other seed it's compared against.
pub fn nearest_seed_index(x: f32, y: f32, seeds: &[(f32, f32)], lap_q: (f32, f32), lap_r: (f32, f32)) -> usize {
    seeds.iter().enumerate()
        .map(|(i, &(sx, sy))| (i, wrapped_distance_sq(x, y, sx, sy, lap_q, lap_r)))
        .min_by(|a, b| a.1.partial_cmp(&b.1).unwrap())
        .map(|(i, _)| i)
        .unwrap()
}

// Sum duplicate biome_weights entries by biome_id, preserving first-seen order — the returned Vec
// doubles as the deterministic biome iteration order for generation (a HashMap's order isn't
// guaranteed stable across publishes, which would silently break "same seed -> same layout" by
// feeding draws/adjacency checks in a different order each time).
pub fn combine_biome_weights(biome_weights: &[BiomeWeight]) -> Vec<(String, f32)> {
    let mut totals: Vec<(String, f32)> = Vec::new();
    for bw in biome_weights {
        match totals.iter_mut().find(|(id, _)| id == &bw.biome_id) {
            Some(entry) => entry.1 += bw.weight,
            None => totals.push((bw.biome_id.clone(), bw.weight)),
        }
    }
    totals
}

// Each unique biome_id's combined weight (summed across however many biome_weights entries name
// it) gets a proportional share of BIOME_VORONOI_SEED_BUDGET seed points — e.g. 40% weight gets
// ~40% of the seeds, which on average claims ~40% of the map's area for randomly placed Voronoi
// seeds. Every weight > 0 gets at least 1 seed so a small biome can't vanish entirely to rounding.
pub fn distribute_biome_seeds(biome_weight_totals: &[(String, f32)], cells: &[HexCell], seed: u64, draw: &mut u64) -> Vec<Vec<(f32, f32)>> {
    let mut per_biome: Vec<Vec<(f32, f32)>> = Vec::with_capacity(biome_weight_totals.len());
    for (_, weight) in biome_weight_totals {
        let seed_count = ((weight / 100.0) * BIOME_VORONOI_SEED_BUDGET as f32).round().max(1.0) as usize;
        let mut positions: Vec<(f32, f32)> = Vec::with_capacity(seed_count);
        for _ in 0..seed_count {
            let cell = &cells[pick_index(cells.len(), seed, draw)];
            positions.push((cell.world_x, cell.world_y));
        }
        per_biome.push(positions);
    }
    per_biome
}

// Bucket every cell under the biome whose seed is nearest (wrap-aware). Seeds from all biomes
// compete in one pool, so a cell always joins its closest seed regardless of which biome owns it.
pub fn group_cells_by_seeds<'c>(cells: &'c [HexCell], seed_positions_per_biome: &[Vec<(f32, f32)>], lap_q: (f32, f32), lap_r: (f32, f32)) -> Vec<Vec<&'c HexCell>> {
    let mut all_positions: Vec<(f32, f32)> = Vec::new();
    let mut biome_of_seed: Vec<usize> = Vec::new();
    for (biome_idx, positions) in seed_positions_per_biome.iter().enumerate() {
        for &pos in positions {
            all_positions.push(pos);
            biome_of_seed.push(biome_idx);
        }
    }
    let mut grouped: Vec<Vec<&HexCell>> = vec![Vec::new(); seed_positions_per_biome.len()];
    for cell in cells {
        let idx = nearest_seed_index(cell.world_x, cell.world_y, &all_positions, lap_q, lap_r);
        grouped[biome_of_seed[idx]].push(cell);
    }
    grouped
}
