// Deterministic PRNG (SplitMix64) — mirrors BulletManager.cs's SplitMix64/HashToUnit on the
// client, so Wander directions and bullet pellet jitter can be reproduced from an event/enemy
// id alone, with no shared RNG state between server and clients. Also drives world gen so the
// same (world_id, seed) pair always produces the same layout.
pub fn splitmix64(seed: u64) -> u64 {
    let h = (seed ^ (seed >> 30)).wrapping_mul(0xbf58476d1ce4e5b9);
    let h = (h ^ (h >> 27)).wrapping_mul(0x94d049bb133111eb);
    h ^ (h >> 31)
}

pub fn hash_to_unit(h: u64) -> f32 {
    (h >> 11) as f32 / (1u64 << 53) as f32
}

pub fn next_unit(seed: u64, draw: &mut u64) -> f32 {
    let v = hash_to_unit(splitmix64(seed.wrapping_add(*draw)));
    *draw += 1;
    v
}

pub fn pick_index(len: usize, seed: u64, draw: &mut u64) -> usize {
    ((next_unit(seed, draw) * len as f32) as usize).min(len.saturating_sub(1))
}

// Weighted-random index, e.g. picking a ground texture by its relative GroundTextureConfig.chance.
// Weights don't need to sum to anything in particular. Falls back to index 0 if every weight
// is zero (bad content data) rather than dividing by zero.
pub fn weighted_pick_index(weights: &[f32], seed: u64, draw: &mut u64) -> usize {
    let total: f32 = weights.iter().sum();
    if total <= 0.0 { return 0; }
    let roll = next_unit(seed, draw) * total;
    let mut cumulative = 0.0;
    for (i, w) in weights.iter().enumerate() {
        cumulative += w;
        if roll < cumulative { return i; }
    }
    weights.len() - 1
}

// Same cumulative-weight selection as weighted_pick_index, but driven by an already-computed unit
// value (e.g. value_noise2d's output) instead of a fresh PRNG draw — lets a pick be "random" with
// respect to the weights while still following a spatially-coherent field across calls.
pub fn weighted_pick_index_from_unit(weights: &[f32], unit: f32) -> usize {
    let total: f32 = weights.iter().sum();
    if total <= 0.0 { return 0; }
    let roll = unit.clamp(0.0, 1.0) * total;
    let mut cumulative = 0.0;
    for (i, w) in weights.iter().enumerate() {
        cumulative += w;
        if roll < cumulative { return i; }
    }
    weights.len() - 1
}

// Like weighted_pick_index, but `chances` are percentages (0-100) that don't have to add up to
// 100 — e.g. picking which overlay (if any) lands on a tile. Anything left under 100 after
// summing is real "pick nothing" probability (None); anything over 100 is scaled down so it
// still fits, rather than overflowing past a guaranteed pick.
pub fn weighted_pick_with_remainder(chances: &[f32], seed: u64, draw: &mut u64) -> Option<usize> {
    weighted_pick_with_remainder_from_unit(chances, next_unit(seed, draw))
}

// Same as weighted_pick_with_remainder, but driven by an already-computed unit value (e.g.
// value_noise2d's output) instead of a fresh PRNG draw — see weighted_pick_index_from_unit.
pub fn weighted_pick_with_remainder_from_unit(chances: &[f32], unit: f32) -> Option<usize> {
    let total: f32 = chances.iter().sum();
    if total <= 0.0 { return None; }
    let scale = if total > 100.0 { 100.0 / total } else { 1.0 };
    let roll = unit.clamp(0.0, 1.0) * 100.0;
    let mut cumulative = 0.0;
    for (i, c) in chances.iter().enumerate() {
        cumulative += c * scale;
        if roll < cumulative { return Some(i); }
    }
    None
}
