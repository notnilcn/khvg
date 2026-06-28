use std::collections::HashMap;
use hexx::{Hex, HexLayout, HexOrientation, Vec2};
use spacetimedb::{ReducerContext, Table};
use crate::main::global::*;
use crate::world::tables::*;
use crate::world::def_tables::*;
use crate::world::instance_tables::*;

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

pub fn hex_area(hex_radius: i32) -> i32 {
    Hex::range_count(hex_radius as u32) as i32
}

pub fn hex_shift(hex_radius: i32) -> i32 {
    3 * hex_radius + 2
}

pub fn world_to_hex(wx: f32, wy: f32, r_outer: f32) -> (i32, i32) {
    let layout = HexLayout { orientation: HexOrientation::Pointy, scale: Vec2::splat(r_outer), origin: Vec2::ZERO };
    let h = layout.world_pos_to_hex(Vec2::new(wx, wy));
    (h.x, h.y)
}

pub fn hex_to_world(hq: i32, hr: i32, r_outer: f32) -> (f32, f32) {
    let layout = HexLayout { orientation: HexOrientation::Pointy, scale: Vec2::splat(r_outer), origin: Vec2::ZERO };
    let pos = layout.hex_to_world_pos(Hex::new(hq, hr));
    (pos.x, pos.y)
}

pub fn chunk_center_hex(cq: i32, cr: i32, hex_radius: i32) -> (i32, i32) {
    let center = Hex::new(cq, cr).to_higher_res(hex_radius as u32);
    (center.x, center.y)
}

pub fn inv_hexmod(m: i32, hex_radius: i32, _shift: i32) -> (i32, i32) {
    let rel = Hex::from_hexmod_coordinates(m as u32, hex_radius as u32);
    (rel.x, rel.y)
}

// Bijective i64 chunk index using hex spiral order. Mirrors _hex_spiral_index in hex_grid_overlay.gd.
pub fn spiral_chunk_index(cq: i32, cr: i32) -> i64 {
    let x = cq;
    let z = cr;
    let y = -cq - cr;
    let rho = x.abs().max(y.abs()).max(z.abs());
    if rho == 0 { return 0; }
    let b = (3 * rho * (rho - 1) + 1) as i64;
    let (arm, step): (i64, i64) =
        if z == -rho && x > 0                  { (0, y as i64) }
        else if y == rho && x > -rho            { (1, -x as i64) }
        else if x == -rho && z < rho            { (2, z as i64) }
        else if z == rho && x < 0              { (3, (x + rho) as i64) }
        else if y == -rho && x >= 0 && x < rho { (4, x as i64) }
        else                                    { (5, (y + rho) as i64) };
    b + arm * rho as i64 + step
}

pub fn world_to_chunk(wx: f32, wy: f32, chunk_hex_radius: i32, hex_outer_radius: f32) -> (i32, i32) {
    let (hq, hr) = world_to_hex(wx, wy, hex_outer_radius);
    let chunk = Hex::new(hq, hr).to_lower_res(chunk_hex_radius as u32);
    (chunk.x, chunk.y)
}

pub fn wrap_chunk_coords(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32) -> (i32, i32) {
    (cq.rem_euclid(chunk_cols), cr.rem_euclid(chunk_rows))
}

// Torus wrap in continuous space. If the position's chunk coord wrapped around the grid
// (cqw != cq), shift x/y by `laps` world-space periods — the period vector for one full lap
// in q (or r) is the world position of the hex at chunk (chunk_cols, 0) (or (0, chunk_rows)),
// i.e. how far one full grid traversal moves you in continuous coordinates.
pub fn wrap_world_pos(x: f32, y: f32, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> (f32, f32) {
    if chunk_cols <= 0 || chunk_rows <= 0 { return (x, y); }
    let (cq, cr) = world_to_chunk(x, y, chunk_hex_radius, hex_outer_radius);
    let (cqw, crw) = wrap_chunk_coords(cq, cr, chunk_cols, chunk_rows);
    if cq == cqw && cr == crw { return (x, y); }
    let laps_q = (cqw - cq) / chunk_cols;
    let laps_r = (crw - cr) / chunk_rows;
    let (pq_hq, pq_hr) = chunk_center_hex(chunk_cols, 0, chunk_hex_radius);
    let (pr_hq, pr_hr) = chunk_center_hex(0, chunk_rows, chunk_hex_radius);
    let (pq_x, pq_y) = hex_to_world(pq_hq, pq_hr, hex_outer_radius);
    let (pr_x, pr_y) = hex_to_world(pr_hq, pr_hr, hex_outer_radius);
    (x + laps_q as f32 * pq_x + laps_r as f32 * pr_x,
     y + laps_q as f32 * pq_y + laps_r as f32 * pr_y)
}

// Squared distance between two world points, accounting for torus wrap — mirrors the client's
// TorusMath.NearestCandidate. Needed whenever code compares a stored (already-wrapped) position
// against a point computed from a region/chunk center that hasn't been wrapped itself: an offset
// from that center can cross a lap seam even when the center's own chunk is canonical, so a plain
// Euclidean distance can silently never match — checking all 9 lap-shift combinations always
// finds the true nearest copy regardless of which side of a seam either point landed on.
pub fn wrapped_distance_sq(x1: f32, y1: f32, x2: f32, y2: f32, lap_q: (f32, f32), lap_r: (f32, f32)) -> f32 {
    let mut best = f32::MAX;
    for lq in -1..=1 {
        for lr in -1..=1 {
            let cx = x2 + lq as f32 * lap_q.0 + lr as f32 * lap_r.0;
            let cy = y2 + lq as f32 * lap_q.1 + lr as f32 * lap_r.1;
            let d = (cx - x1).powi(2) + (cy - y1).powi(2);
            if d < best { best = d; }
        }
    }
    best
}

pub fn surrounding_chunk_indices(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32, rings: Option<i32>) -> Vec<i64> {
    let rings = rings.unwrap_or(DEFAULT_AOI_CHUNK_RADIUS) as u32;
    let mut seen = std::collections::HashSet::new();
    Hex::new(cq, cr)
        .range(rings)
        .filter_map(|h| {
            let (wq, wr) = wrap_chunk_coords(h.x, h.y, chunk_cols, chunk_rows);
            let idx = spiral_chunk_index(wq, wr);
            if seen.insert(idx) { Some(idx) } else { None }
        })
        .collect()
}

pub struct HexCell {
    pub hex_q: i32,
    pub hex_r: i32,
    pub chunk_index: i64,
    pub world_x: f32,
    pub world_y: f32,
}

pub fn enumerate_world_hexes(chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32, hex_outer_radius: f32) -> Vec<HexCell> {
    let area = hex_area(chunk_hex_radius);
    let shift = hex_shift(chunk_hex_radius);
    let mut cells = Vec::with_capacity((chunk_cols * chunk_rows * area).max(0) as usize);
    for cr in 0..chunk_rows {
        for cq in 0..chunk_cols {
            let chunk_index = spiral_chunk_index(cq, cr);
            let (cx, cz) = chunk_center_hex(cq, cr, chunk_hex_radius);
            for m in 0..area {
                let (rx, rz) = inv_hexmod(m, chunk_hex_radius, shift);
                let hex_q = cx + rx;
                let hex_r = cz + rz;
                let (world_x, world_y) = hex_to_world(hex_q, hex_r, hex_outer_radius);
                cells.push(HexCell { hex_q, hex_r, chunk_index, world_x, world_y });
            }
        }
    }
    cells
}

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

pub fn clear_world_geometry(ctx: &ReducerContext) {
    for region in ctx.db.biome_region().iter() {
        ctx.db.biome_region().region_id().delete(&region.region_id);
    }
    for tile in ctx.db.triangle_tile().iter() {
        ctx.db.triangle_tile().tile_id().delete(&tile.tile_id);
    }
    for decor in ctx.db.hex_decor().iter() {
        ctx.db.hex_decor().decor_id().delete(&decor.decor_id);
    }
}

fn pick_rotation(allowed_rotations: &[u8], seed: u64, draw: &mut u64) -> u8 {
    if allowed_rotations.is_empty() { pick_index(3, seed, draw) as u8 }
    else { allowed_rotations[pick_index(allowed_rotations.len(), seed, draw)] }
}

fn overlay_compatible_with_base(ctx: &ReducerContext, overlay_tags: &[String], base_tags: &[String]) -> bool {
    ctx.db.layering_rule().iter().any(|r| overlay_tags.contains(&r.overlay_tag) && base_tags.contains(&r.allowed_base_tag))
}

fn base_tags_conflict(ctx: &ReducerContext, tags_a: &[String], tags_b: &[String]) -> bool {
    ctx.db.base_adjacency_rule().iter().any(|r|
        (tags_a.contains(&r.tag_a) && tags_b.contains(&r.tag_b)) || (tags_a.contains(&r.tag_b) && tags_b.contains(&r.tag_a)))
}

fn overlay_tags_conflict(ctx: &ReducerContext, tags_a: &[String], tags_b: &[String]) -> bool {
    ctx.db.overlay_adjacency_rule().iter().any(|r|
        (tags_a.contains(&r.tag_a) && tags_b.contains(&r.tag_b)) || (tags_a.contains(&r.tag_b) && tags_b.contains(&r.tag_a)))
}

// Axial deltas for the 6 hex neighbor directions, indexed 0-5, where direction D's hex_to_world
// offset has angle 60*D degrees (standard axial-coordinate neighbor set).
const HEX_NEIGHBOR_DELTAS: [(i32, i32); 6] = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];

// Decorrelates the overlay noise field from the ground noise field above — both read the same
// world seed, so without this offset overlay clustering would just trace the ground-texture
// boundary instead of forming its own independent pattern.
const OVERLAY_NOISE_SEED_OFFSET: u64 = 0x9E3779B97F4A7C15;

// Same decorrelation purpose as OVERLAY_NOISE_SEED_OFFSET, for decor density's own noise field.
const DECOR_NOISE_SEED_OFFSET: u64 = 0xC2B2AE3D27D4EB4F;

fn decor_ground_conflict(ctx: &ReducerContext, decor_tags: &[String], hex_tags: &[String]) -> bool {
    ctx.db.decor_ground_rule().iter().any(|r| decor_tags.contains(&r.decor_tag) && hex_tags.contains(&r.denied_base_tag))
}

// Tags of a wedge already written this generation pass, kept around just long enough for
// not-yet-written neighbor wedges to check adjacency against. Not persisted to TriangleTile.
#[derive(Default, Clone)]
struct DecidedWedgeTags {
    ground_tags: Vec<String>,
    overlay_tags: Vec<String>,
}

type NeighborTagMap = HashMap<(i32, i32, u8), DecidedWedgeTags>;

// A wedge's same-hex neighbors are always the two adjacent tri_index values — wedges tile the hex
// with no gaps (TerrainManager.cs's GetWedgeVertices: wedge N's second vertex is wedge N+1's first),
// so wedge N borders wedge N-1 and wedge N+1 within the same hex.
fn same_hex_neighbor_indices(tri_index: u8) -> [u8; 2] {
    [(tri_index + 5) % 6, (tri_index + 1) % 6]
}

// A wedge's outer edge (the one opposite the hex center) points in hex-direction (tri_index+1)%6:
// TerrainManager.cs's wedge vertices sit at angles 30+60*triIndex / 30+60*(triIndex+1), so the
// outer edge's midpoint angle is 60*(triIndex+1) — exactly direction (triIndex+1)%6's offset angle
// in the table above. Crossing that edge lands on the wedge that shares it from the neighbor hex's
// side: directions 180 degrees apart differ by 3 in this 6-direction scheme, so that wedge is
// (tri_index+3)%6 of the neighbor hex.
fn cross_hex_neighbor(hex_q: i32, hex_r: i32, tri_index: u8) -> (i32, i32, u8) {
    let (dq, dr) = HEX_NEIGHBOR_DELTAS[((tri_index + 1) % 6) as usize];
    (hex_q + dq, hex_r + dr, (tri_index + 3) % 6)
}

fn decided_neighbor_tags<'a>(map: &'a NeighborTagMap, hex_q: i32, hex_r: i32, tri_index: u8) -> Vec<&'a DecidedWedgeTags> {
    let mut out = Vec::with_capacity(3);
    for n in same_hex_neighbor_indices(tri_index) {
        if let Some(d) = map.get(&(hex_q, hex_r, n)) { out.push(d); }
    }
    let (nq, nr, ntri) = cross_hex_neighbor(hex_q, hex_r, tri_index);
    if let Some(d) = map.get(&(nq, nr, ntri)) { out.push(d); }
    out
}

fn write_triangle_tiles(ctx: &ReducerContext, cell: &HexCell, biome_id: &str, region_id: u64, ground_textures: &[GroundTextureConfig], overlay_textures: &[OverlayTextureConfig], ground_noise_scale: f32, overlay_noise_scale: f32, seed: u64, draw: &mut u64, neighbor_tags: &mut NeighborTagMap) {
    if ground_textures.is_empty() { return; }
    let ground_chances: Vec<f32> = ground_textures.iter().map(|g| g.chance).collect();

    for tri_index in 0..6u8 {
        let neighbors = decided_neighbor_tags(neighbor_tags, cell.hex_q, cell.hex_r, tri_index);

        // First pick is noise-driven (when ground_noise_scale > 0) so nearby wedges land in the
        // same noise band and cluster into patches; a non-positive scale opts back into the old
        // pure per-wedge randomness.
        let first_unit = if ground_noise_scale > 0.0 { value_noise2d(cell.world_x, cell.world_y, ground_noise_scale, seed) } else { next_unit(seed, draw) };
        let first_index = weighted_pick_index_from_unit(&ground_chances, first_unit);

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

        let (texture_id, rotation, ground_tags) = match chosen_ground {
            Some(g) => (g.texture_id.clone(), pick_rotation(&g.allowed_rotations, seed, draw), g.tags.clone()),
            None => (String::new(), 0u8, Vec::new()),
        };

        // Eligible: tag-compatible with the ground texture actually chosen above (LayeringRule).
        // A base with fewer compatible overlays naturally ends up with a lower overall overlay
        // chance — no separate per-base modifier needed.
        let eligible: Vec<&OverlayTextureConfig> = overlay_textures.iter()
            .filter(|o| overlay_compatible_with_base(ctx, &o.tags, &ground_tags))
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
            if chosen.is_none() {
                if let Some(conflicting) = first_picked {
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
            }

            if let Some(i) = chosen {
                let overlay = eligible[i];
                overlay_rotation = pick_rotation(&overlay.allowed_rotations, seed, draw);
                overlay_texture_id = Some(overlay.texture_id.clone());
                overlay_scale = overlay.scale;
                overlay_tags = overlay.tags.clone();
            }
        }

        neighbor_tags.insert((cell.hex_q, cell.hex_r, tri_index), DecidedWedgeTags { ground_tags, overlay_tags });

        ctx.db.triangle_tile().insert(TriangleTile {
            tile_id: 0, hex_q: cell.hex_q, hex_r: cell.hex_r, tri_index, chunk_index: cell.chunk_index,
            texture_id, rotation, overlay_texture_id, overlay_rotation, overlay_scale,
            biome_id: biome_id.to_string(), region_id,
        });
    }
}

// Run once per cell, right after write_triangle_tiles for that same cell — decor's ground/overlay
// check reads that hex's own 6 wedges, which write_triangle_tiles just finished deciding.
fn place_decor(ctx: &ReducerContext, cell: &HexCell, biome_id: &str, region_id: u64, decor_configs: &[DecorConfig], decor_noise_scale: f32, decor_min_separation: i32, seed: u64, draw: &mut u64, neighbor_tags: &NeighborTagMap, decided_decor: &mut std::collections::HashSet<(i32, i32)>) {
    if decor_configs.is_empty() { return; }

    // Union of ground+overlay tags across all 6 of this hex's wedges — every tri_index is already
    // decided by the write_triangle_tiles call that just ran for this same cell.
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

pub fn internal_generate_world_proc(ctx: &ReducerContext, world_id: String, seed: u64) -> Result<(), String> {
    let world = ctx.db.world_def().world_id().find(&world_id).ok_or_else(|| format!("WorldDef '{}' not found.", world_id))?;
    if world.biome_weights.is_empty() { return Err("WorldDef has no biome_weights.".to_string()); }
    validate_weights_sum_100(&world.biome_weights.iter().map(|b| b.weight).collect::<Vec<_>>())?;

    let cfg = ctx.db.map_config().id().find(&0u32).ok_or("MapConfig not found — call add_chunks first.")?;
    let cells = enumerate_world_hexes(cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    if cells.is_empty() { return Err("World has no hexes — call add_chunks first.".to_string()); }
    let (lap_q, lap_r) = ((cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));

    clear_world_geometry(ctx);
    let mut draw: u64 = 0;
    let mut neighbor_tags: NeighborTagMap = HashMap::new();
    let mut decided_decor: std::collections::HashSet<(i32, i32)> = std::collections::HashSet::new();

    // Each unique biome_id's combined weight (summed across however many biome_weights entries
    // name it) gets a proportional share of BIOME_VORONOI_SEED_BUDGET seed points — e.g. 40%
    // weight gets ~40% of the seeds, which on average claims ~40% of the map's area for randomly
    // placed Voronoi seeds. Every weight > 0 gets at least 1 seed so a small biome can't vanish
    // entirely to rounding.
    let mut biome_weight_totals: Vec<(String, f32)> = Vec::new();
    for bw in &world.biome_weights {
        match biome_weight_totals.iter_mut().find(|(id, _)| id == &bw.biome_id) {
            Some(entry) => entry.1 += bw.weight,
            None => biome_weight_totals.push((bw.biome_id.clone(), bw.weight)),
        }
    }

    let mut biome_seed_positions: Vec<(f32, f32)> = Vec::new();
    let mut biome_seed_ids: Vec<String> = Vec::new();
    for (biome_id, weight) in &biome_weight_totals {
        let seed_count = ((weight / 100.0) * BIOME_VORONOI_SEED_BUDGET as f32).round().max(1.0) as usize;
        for _ in 0..seed_count {
            let cell = &cells[pick_index(cells.len(), seed, &mut draw)];
            biome_seed_positions.push((cell.world_x, cell.world_y));
            biome_seed_ids.push(biome_id.clone());
        }
    }

    let mut cells_by_biome: HashMap<String, Vec<&HexCell>> = HashMap::new();
    for cell in &cells {
        let idx = nearest_seed_index(cell.world_x, cell.world_y, &biome_seed_positions, lap_q, lap_r);
        cells_by_biome.entry(biome_seed_ids[idx].clone()).or_default().push(cell);
    }

    // Iterate biome_weight_totals (a Vec, already deduped by biome_id in deterministic order)
    // rather than draining the cells_by_biome HashMap directly — HashMap iteration order isn't
    // guaranteed stable across publishes, which would silently break "same seed -> same layout"
    // by feeding draw()/adjacency checks in a different order each time.
    for (biome_id, _) in &biome_weight_totals {
        let Some(biome_cells) = cells_by_biome.get(biome_id) else { continue };
        let biome_def = match ctx.db.biome_def().biome_id().find(biome_id) {
            Some(b) => b,
            None => continue,
        };
        if biome_def.region_weights.is_empty() || biome_cells.is_empty() { continue; }
        validate_weights_sum_100(&biome_def.region_weights.iter().map(|r| r.weight).collect::<Vec<_>>())?;

        // Same one-seed-per-entry rule as biomes above, scoped to this biome's own cells.
        let mut region_seed_positions: Vec<(f32, f32)> = Vec::new();
        let mut region_ids: Vec<u64> = Vec::new();
        for rw in &biome_def.region_weights {
            let region_def = match ctx.db.biome_region_def().region_def_id().find(&rw.region_def_id) {
                Some(r) => r,
                None => continue,
            };
            let cell = &biome_cells[pick_index(biome_cells.len(), seed, &mut draw)];
            let inserted = ctx.db.biome_region().insert(BiomeRegion {
                region_id: 0, biome_id: biome_id.clone(),
                center_hex_q: cell.hex_q, center_hex_r: cell.hex_r,
                enemy_template_ids: region_def.enemy_template_ids.clone(),
                max_enemies: region_def.max_enemies, spawn_radius: region_def.spawn_radius,
            });
            region_seed_positions.push((cell.world_x, cell.world_y));
            region_ids.push(inserted.region_id);
        }
        if region_seed_positions.is_empty() { continue; }

        for cell in biome_cells {
            let idx = nearest_seed_index(cell.world_x, cell.world_y, &region_seed_positions, lap_q, lap_r);
            write_triangle_tiles(ctx, cell, biome_id, region_ids[idx], &biome_def.ground_textures, &biome_def.overlay_textures, biome_def.ground_noise_scale, biome_def.overlay_noise_scale, seed, &mut draw, &mut neighbor_tags);
            place_decor(ctx, cell, biome_id, region_ids[idx], &biome_def.decor_configs, biome_def.decor_noise_scale, biome_def.decor_min_separation, seed, &mut draw, &neighbor_tags, &mut decided_decor);
        }
    }

    Ok(())
}

pub fn internal_generate_world_manual(ctx: &ReducerContext, biomes: Vec<ManualBiomeInput>) -> Result<(), String> {
    if biomes.is_empty() { return Err("No biomes provided.".to_string()); }
    let cfg = ctx.db.map_config().id().find(&0u32).ok_or("MapConfig not found — call add_chunks first.")?;
    let cells = enumerate_world_hexes(cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    if cells.is_empty() { return Err("World has no hexes — call add_chunks first.".to_string()); }
    let (lap_q, lap_r) = ((cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));

    clear_world_geometry(ctx);
    let seed = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros() as u64;
    let mut draw: u64 = 0;
    let mut neighbor_tags: NeighborTagMap = HashMap::new();
    let mut decided_decor: std::collections::HashSet<(i32, i32)> = std::collections::HashSet::new();

    let biome_seed_positions: Vec<(f32, f32)> = biomes.iter()
        .map(|b| hex_to_world(b.center_hex_q, b.center_hex_r, cfg.hex_outer_radius))
        .collect();

    let mut cells_by_biome: Vec<Vec<&HexCell>> = vec![Vec::new(); biomes.len()];
    for cell in &cells {
        let idx = nearest_seed_index(cell.world_x, cell.world_y, &biome_seed_positions, lap_q, lap_r);
        cells_by_biome[idx].push(cell);
    }

    for (biome_idx, biome_input) in biomes.iter().enumerate() {
        let biome_cells = &cells_by_biome[biome_idx];
        if biome_input.regions.is_empty() || biome_cells.is_empty() { continue; }

        let region_seed_positions: Vec<(f32, f32)> = biome_input.regions.iter()
            .map(|r| hex_to_world(r.center_hex_q, r.center_hex_r, cfg.hex_outer_radius))
            .collect();
        let mut region_ids: Vec<u64> = Vec::with_capacity(biome_input.regions.len());
        for region in &biome_input.regions {
            let inserted = ctx.db.biome_region().insert(BiomeRegion {
                region_id: 0, biome_id: biome_input.biome_id.clone(),
                center_hex_q: region.center_hex_q, center_hex_r: region.center_hex_r,
                enemy_template_ids: region.enemy_template_ids.clone(),
                max_enemies: region.max_enemies, spawn_radius: region.spawn_radius,
            });
            region_ids.push(inserted.region_id);
        }

        for cell in biome_cells {
            let idx = nearest_seed_index(cell.world_x, cell.world_y, &region_seed_positions, lap_q, lap_r);
            write_triangle_tiles(ctx, cell, &biome_input.biome_id, region_ids[idx], &biome_input.ground_textures, &biome_input.overlay_textures, biome_input.ground_noise_scale, biome_input.overlay_noise_scale, seed, &mut draw, &mut neighbor_tags);
            place_decor(ctx, cell, &biome_input.biome_id, region_ids[idx], &biome_input.decor_configs, biome_input.decor_noise_scale, biome_input.decor_min_separation, seed, &mut draw, &neighbor_tags, &mut decided_decor);
        }
    }

    Ok(())
}
