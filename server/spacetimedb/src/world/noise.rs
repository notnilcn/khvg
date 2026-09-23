use super::prng::{hash_to_unit, splitmix64};

// Decorrelates the overlay noise field from the ground noise field above — both read the same
// world seed, so without this offset overlay clustering would just trace the ground-texture
// boundary instead of forming its own independent pattern.
pub const OVERLAY_NOISE_SEED_OFFSET: u64 = 0x9E3779B97F4A7C15;

// Same decorrelation purpose as OVERLAY_NOISE_SEED_OFFSET, for decor density's own noise field.
pub const DECOR_NOISE_SEED_OFFSET: u64 = 0xC2B2AE3D27D4EB4F;

// Same decorrelation purpose again, for the pseudo-moisture field that weights river origins
// (terrain/rivers.rs) — independent of the elevation field it is multiplied with.
pub const RIVER_NOISE_SEED_OFFSET: u64 = 0xA1B2C3D4E5F60718;

// Ridge height field (world/heights.rs) seed offsets. Each field is a separate value_noise2d
// instance keyed on the world seed, so they need distinct offsets for the same reason as the
// offsets above: terrain classes, the two base-height octaves, the ridge erosion field and the
// hill bump must not trace each other.
pub const TERRAIN_CLASS_NOISE_SEED_OFFSET: u64 = 0x8F51B2C3D4E5F607;
pub const HEIGHT_NOISE_SEED_OFFSET: u64 = 0xB7E151628AED2A6B;
pub const HEIGHT_NOISE_OCTAVE2_SEED_OFFSET: u64 = 0x165667B19E3779F9;
pub const RIDGE_NOISE_SEED_OFFSET: u64 = 0x27D4EB2F165667C5;
pub const HEIGHT_HILL_NOISE_SEED_OFFSET: u64 = 0x94D049BB133111EB;

// Hash a lattice point to [0,1) — same hash family as the PRNG above, but keyed by integer grid
// coordinates instead of a draw counter, so the same (seed, x, y) always reproduces the same value.
fn noise_lattice_hash(ix: i32, iy: i32, seed: u64) -> f32 {
    let key = (ix as u32 as u64).wrapping_mul(0x9E3779B97F4A7C15) ^ (iy as u32 as u64).wrapping_mul(0xC2B2AE3D27D4EB4F) ^ seed;
    hash_to_unit(splitmix64(key))
}

fn smoothstep(t: f32) -> f32 { t * t * (3.0 - 2.0 * t) }

// Deterministic 2D value noise (bilinear-interpolated lattice hash) in [0,1) — spatially coherent,
// so nearby world positions return similar values instead of an independent roll per call. Used to
// make texture picks cluster into patches instead of scattering uniformly per-wedge. `scale` is
// spatial frequency: lower scale means larger patches.
pub fn value_noise2d(x: f32, y: f32, scale: f32, seed: u64) -> f32 {
    let sx = x * scale;
    let sy = y * scale;
    let x0 = sx.floor();
    let y0 = sy.floor();
    let tx = smoothstep(sx - x0);
    let ty = smoothstep(sy - y0);
    let (ix0, iy0) = (x0 as i32, y0 as i32);
    let h00 = noise_lattice_hash(ix0, iy0, seed);
    let h10 = noise_lattice_hash(ix0 + 1, iy0, seed);
    let h01 = noise_lattice_hash(ix0, iy0 + 1, seed);
    let h11 = noise_lattice_hash(ix0 + 1, iy0 + 1, seed);
    let top = h00 + (h10 - h00) * tx;
    let bottom = h01 + (h11 - h01) * tx;
    top + (bottom - top) * ty
}
