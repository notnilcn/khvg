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

// Like weighted_pick_index, but `chances` are percentages (0-100) that don't have to add up to
// 100 — e.g. picking which overlay (if any) lands on a tile. Anything left under 100 after
// summing is real "pick nothing" probability (None); anything over 100 is scaled down so it
// still fits, rather than overflowing past a guaranteed pick.
pub fn weighted_pick_with_remainder(chances: &[f32], seed: u64, draw: &mut u64) -> Option<usize> {
    let total: f32 = chances.iter().sum();
    if total <= 0.0 { return None; }
    let scale = if total > 100.0 { 100.0 / total } else { 1.0 };
    let roll = next_unit(seed, draw) * 100.0;
    let mut cumulative = 0.0;
    for (i, c) in chances.iter().enumerate() {
        cumulative += c * scale;
        if roll < cumulative { return Some(i); }
    }
    None
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

pub fn nearest_seed_index(x: f32, y: f32, seeds: &[(f32, f32)]) -> usize {
    seeds.iter().enumerate()
        .map(|(i, &(sx, sy))| (i, (sx - x).powi(2) + (sy - y).powi(2)))
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
}

fn pick_rotation(allowed_rotations: &[u8], seed: u64, draw: &mut u64) -> u8 {
    if allowed_rotations.is_empty() { pick_index(3, seed, draw) as u8 }
    else { allowed_rotations[pick_index(allowed_rotations.len(), seed, draw)] }
}

fn overlay_compatible_with_base(ctx: &ReducerContext, overlay_tags: &[String], base_tags: &[String]) -> bool {
    ctx.db.layering_rule().iter().any(|r| overlay_tags.contains(&r.overlay_tag) && base_tags.contains(&r.allowed_base_tag))
}

fn write_triangle_tiles(ctx: &ReducerContext, cell: &HexCell, biome_id: &str, region_id: u64, ground_textures: &[GroundTextureConfig], overlay_textures: &[OverlayTextureConfig], seed: u64, draw: &mut u64) {
    if ground_textures.is_empty() { return; }
    let ground_chances: Vec<f32> = ground_textures.iter().map(|g| g.chance).collect();

    for tri_index in 0..6u8 {
        let ground = &ground_textures[weighted_pick_index(&ground_chances, seed, draw)];
        let rotation = pick_rotation(&ground.allowed_rotations, seed, draw);

        // Eligible: tag-compatible with the just-chosen ground texture (LayeringRule). A base
        // with fewer compatible overlays naturally ends up with a lower overall overlay chance —
        // no separate per-base modifier needed.
        let eligible: Vec<&OverlayTextureConfig> = overlay_textures.iter()
            .filter(|o| overlay_compatible_with_base(ctx, &o.tags, &ground.tags))
            .collect();

        let mut overlay_texture_id: Option<String> = None;
        let mut overlay_rotation: u8 = 0;
        let mut overlay_scale: f32 = 0.0;
        if !eligible.is_empty() {
            let chances: Vec<f32> = eligible.iter().map(|o| o.chance).collect();
            if let Some(i) = weighted_pick_with_remainder(&chances, seed, draw) {
                let overlay = eligible[i];
                overlay_rotation = pick_rotation(&overlay.allowed_rotations, seed, draw);
                overlay_texture_id = Some(overlay.texture_id.clone());
                overlay_scale = overlay.scale;
            }
        }

        ctx.db.triangle_tile().insert(TriangleTile {
            tile_id: 0, hex_q: cell.hex_q, hex_r: cell.hex_r, tri_index, chunk_index: cell.chunk_index,
            texture_id: ground.texture_id.clone(), rotation, overlay_texture_id, overlay_rotation, overlay_scale,
            biome_id: biome_id.to_string(), region_id,
        });
    }
}

pub fn internal_generate_world_proc(ctx: &ReducerContext, world_id: String, seed: u64) -> Result<(), String> {
    let world = ctx.db.world_def().world_id().find(&world_id).ok_or_else(|| format!("WorldDef '{}' not found.", world_id))?;
    if world.biome_weights.is_empty() { return Err("WorldDef has no biome_weights.".to_string()); }
    validate_weights_sum_100(&world.biome_weights.iter().map(|b| b.weight).collect::<Vec<_>>())?;

    let cfg = ctx.db.map_config().id().find(&0u32).ok_or("MapConfig not found — call add_chunks first.")?;
    let cells = enumerate_world_hexes(cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    if cells.is_empty() { return Err("World has no hexes — call add_chunks first.".to_string()); }

    clear_world_geometry(ctx);
    let mut draw: u64 = 0;

    // One Voronoi seed per defined entry, full stop — no count multiplier. A WorldDef with a
    // single biome_weights entry produces exactly one biome patch (trivially the whole map);
    // repeating the same biome_id across multiple entries is how you'd deliberately author
    // several disconnected patches of it.
    let mut biome_seed_positions: Vec<(f32, f32)> = Vec::new();
    let mut biome_seed_ids: Vec<String> = Vec::new();
    for bw in &world.biome_weights {
        let cell = &cells[pick_index(cells.len(), seed, &mut draw)];
        biome_seed_positions.push((cell.world_x, cell.world_y));
        biome_seed_ids.push(bw.biome_id.clone());
    }

    let mut cells_by_biome: HashMap<String, Vec<&HexCell>> = HashMap::new();
    for cell in &cells {
        let idx = nearest_seed_index(cell.world_x, cell.world_y, &biome_seed_positions);
        cells_by_biome.entry(biome_seed_ids[idx].clone()).or_default().push(cell);
    }

    for (biome_id, biome_cells) in cells_by_biome {
        let biome_def = match ctx.db.biome_def().biome_id().find(&biome_id) {
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

        for cell in &biome_cells {
            let idx = nearest_seed_index(cell.world_x, cell.world_y, &region_seed_positions);
            write_triangle_tiles(ctx, cell, &biome_id, region_ids[idx], &biome_def.ground_textures, &biome_def.overlay_textures, seed, &mut draw);
        }
    }

    Ok(())
}

pub fn internal_generate_world_manual(ctx: &ReducerContext, biomes: Vec<ManualBiomeInput>) -> Result<(), String> {
    if biomes.is_empty() { return Err("No biomes provided.".to_string()); }
    let cfg = ctx.db.map_config().id().find(&0u32).ok_or("MapConfig not found — call add_chunks first.")?;
    let cells = enumerate_world_hexes(cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    if cells.is_empty() { return Err("World has no hexes — call add_chunks first.".to_string()); }

    clear_world_geometry(ctx);
    let seed = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros() as u64;
    let mut draw: u64 = 0;

    let biome_seed_positions: Vec<(f32, f32)> = biomes.iter()
        .map(|b| hex_to_world(b.center_hex_q, b.center_hex_r, cfg.hex_outer_radius))
        .collect();

    let mut cells_by_biome: Vec<Vec<&HexCell>> = vec![Vec::new(); biomes.len()];
    for cell in &cells {
        let idx = nearest_seed_index(cell.world_x, cell.world_y, &biome_seed_positions);
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
            let idx = nearest_seed_index(cell.world_x, cell.world_y, &region_seed_positions);
            write_triangle_tiles(ctx, cell, &biome_input.biome_id, region_ids[idx], &biome_input.ground_textures, &biome_input.overlay_textures, seed, &mut draw);
        }
    }

    Ok(())
}
