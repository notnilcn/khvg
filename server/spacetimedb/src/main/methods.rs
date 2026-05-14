use spacetimedb::{ReducerContext, Identity, Table};
use super::tables::*;

pub const BASE_MAX_HP: i32 = 100;
pub const HP_PER_LEVEL: i32 = 5;
pub const CHUNK_SIZE: f32 = 320.0;
pub const HEX_TILE_OUTER_RADIUS: f32 = 32.0; // outer radius R of each small hex in world units

pub fn starting_stats() -> PlayerStats {
    PlayerStats {
        player_id: Identity::default(),
        strength: 1,
        wisdom: 1,
        dexterity: 1,
        defense: 1,
        vitality: 1,
        speed: 25,
    }
}

pub fn is_admin(ctx: &ReducerContext) -> bool {
    ctx.db.player().player_id().find(ctx.sender())
        .map(|p| p.is_admin)
        .unwrap_or(false)
}

pub fn xp_for_level(level: u32) -> u32 {
    if level <= 1 { 0 } else { 100 * (level - 1) * level / 2 }
}

pub fn compute_level(total_xp: u32) -> u32 {
    let mut level = 1u32;
    while xp_for_level(level + 1) <= total_xp {
        level += 1;
    }
    level
}

pub fn compute_base_stats(level: u32) -> (i32, i32, i32, i32, i32, i32) {
    let bonus = (level as i32) - 1;
    let s = starting_stats();
    (
        s.strength  + bonus,
        s.wisdom    + bonus,
        s.dexterity + bonus,
        s.defense   + bonus,
        s.vitality  + bonus,
        s.speed     + bonus,
    )
}

// Recomputes PlayerStats from level + equipped items and writes the result.
pub fn recompute_stats(ctx: &ReducerContext, player_id: Identity) {
    let data = match ctx.db.player_data().player_id().find(player_id) {
        Some(d) => d,
        None => return,
    };
    let inv = match ctx.db.player_inventory().player_id().find(player_id) {
        Some(i) => i,
        None => return,
    };

    let (base_str, base_wis, base_dex, base_def, base_vit, base_spd) =
        compute_base_stats(data.level);

    let mut str_flat = 0i32; let mut wis_flat = 0i32; let mut dex_flat = 0i32;
    let mut def_flat = 0i32; let mut vit_flat = 0i32; let mut spd_flat = 0i32;
    let mut str_pct  = 0f32; let mut wis_pct  = 0f32; let mut dex_pct  = 0f32;
    let mut def_pct  = 0f32; let mut vit_pct  = 0f32; let mut spd_pct  = 0f32;

    for slot in &inv.slots {
        let item = match ctx.db.item_stats().item_id().find(&slot.item_id) {
            Some(i) => i,
            None => continue,
        };
        str_flat += item.str_bonus_add as i32; str_pct += item.str_bonus_mult;
        wis_flat += item.wis_bonus_add as i32; wis_pct += item.wis_bonus_mult;
        dex_flat += item.dex_bonus_add as i32; dex_pct += item.dex_bonus_mult;
        def_flat += item.def_bonus_add as i32; def_pct += item.def_bonus_mult;
        vit_flat += item.vit_bonus_add as i32; vit_pct += item.vit_bonus_mult;
        spd_flat += item.spd_bonus_add as i32; spd_pct += item.spd_bonus_mult;
    }

    let new_stats = PlayerStats {
        player_id,
        strength:  ((base_str + str_flat) as f32 * (1.0 + str_pct)) as i32,
        wisdom:    ((base_wis + wis_flat) as f32 * (1.0 + wis_pct)) as i32,
        dexterity: ((base_dex + dex_flat) as f32 * (1.0 + dex_pct)) as i32,
        defense:   ((base_def + def_flat) as f32 * (1.0 + def_pct)) as i32,
        vitality:  ((base_vit + vit_flat) as f32 * (1.0 + vit_pct)) as i32,
        speed:     ((base_spd + spd_flat) as f32 * (1.0 + spd_pct)) as i32,
    };

    if ctx.db.player_stats().player_id().find(player_id).is_some() {
        ctx.db.player_stats().player_id().update(new_stats);
    } else {
        ctx.db.player_stats().insert(new_stats);
    }
}

// ── Hex grid math ────────────────────────────────────────────────────────────

// Floored integer division: result is always rounded toward −∞.
fn floor_div(a: i32, b: i32) -> i32 {
    let d = a / b;
    let r = a % b;
    if r < 0 { d - 1 } else { d }
}

// Non-negative modulo: result is always in [0, b).
fn pos_mod(a: i32, b: i32) -> i32 {
    ((a % b) + b) % b
}

// Number of small hexes per chunk for a given chunk radius.
pub fn hex_area(hex_radius: i32) -> i32 {
    3 * hex_radius * hex_radius + 3 * hex_radius + 1
}

// Shift constant used throughout the sanderevers tiling algorithm.
pub fn hex_shift(hex_radius: i32) -> i32 {
    3 * hex_radius + 2
}

// World position → small hex axial (q, r) using fractional cube rounding.
// r_outer is HEX_TILE_OUTER_RADIUS; pointy-top orientation.
pub fn world_to_hex(wx: f32, wy: f32, r_outer: f32) -> (i32, i32) {
    let inner = r_outer * 0.866_025_4_f32;
    let mut fx = wx / (inner * 2.0);
    let mut fy = -fx;
    let offset = wy / (r_outer * 3.0);
    fx -= offset;
    fy -= offset;
    let mut ix = fx.round() as i32;
    let iy = fy.round() as i32;
    let mut iz = (-fx - fy).round() as i32;
    if ix + iy + iz != 0 {
        let dx = (fx - ix as f32).abs();
        let dy = (fy - iy as f32).abs();
        let dz = (-fx - fy - iz as f32).abs();
        if dx > dy && dx > dz { ix = -iy - iz; }
        else if dz > dy       { iz = -ix - iy; }
    }
    (ix, iz) // (hex_q, hex_r); implicit cube y = -ix-iz
}

// Small hex (hq, hr) → chunk (cq, cr). Implements sanderevers' small_to_big algorithm.
pub fn small_to_big(hq: i32, hr: i32, area: i32, shift: i32) -> (i32, i32) {
    let hy = -hq - hr;
    let xh = floor_div(hy + shift * hq, area);
    let yh = floor_div(hr + shift * hy, area);
    let zh = floor_div(hq + shift * hr, area);
    let ci = floor_div(1 + xh - yh, 3);
    let ck = floor_div(1 + zh - xh, 3);
    (ci, ck) // (chunk_q, chunk_r)
}

// Compact local index m ∈ [0, area) uniquely identifying a small hex within its chunk.
pub fn hexmod(hq: i32, hr: i32, area: i32, shift: i32) -> i32 {
    let hy = -hq - hr;
    pos_mod(hy + shift * hq, area)
}

// Center of chunk (cq, cr) expressed as small hex axial coordinates.
pub fn chunk_center_hex(cq: i32, cr: i32, hex_radius: i32) -> (i32, i32) {
    let r = hex_radius;
    let cj = -cq - cr;
    let hq = (r + 1) * cq - r * cr;
    let hr = (r + 1) * cr - r * cj;
    (hq, hr)
}

// Local hexmod index → relative small hex (q, r) offset from chunk center.
// Add this offset to chunk_center_hex to get absolute hex coords.
pub fn inv_hexmod(m: i32, hex_radius: i32, shift: i32) -> (i32, i32) {
    let r = hex_radius;
    let ms  = floor_div(m + r,     shift);
    let mcs = floor_div(m + 2 * r, shift - 1);
    let hq = ms * (r + 1) + mcs * (-r);
    let hr = -m + ms * r + mcs * (2 * r + 1);
    (hq, hr)
}

// Flat i64 chunk index for SpacetimeDB AoI subscription queries.
pub fn flat_chunk_index(cq: i32, cr: i32, chunk_cols: i32) -> i64 {
    (cr * chunk_cols + cq) as i64
}

// Small hex axial (q, r) → world center position (pointy-top, world units).
pub fn hex_to_world(hq: i32, hr: i32, r_outer: f32) -> (f32, f32) {
    let inner = r_outer * 0.866_025_4_f32;
    let wx = 2.0 * inner * hq as f32 + inner * hr as f32;
    let wy = 1.5 * r_outer * hr as f32;
    (wx, wy)
}

// Returns the 9 chunk indices (3×3 neighbourhood) around (cq, cr), clamped to map bounds.
pub fn surrounding_chunk_indices(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32) -> Vec<i64> {
    let mut result = Vec::with_capacity(9);
    for dr in -1..=1i32 {
        for dq in -1..=1i32 {
            let nq = cq + dq;
            let nr = cr + dr;
            if nq >= 0 && nq < chunk_cols && nr >= 0 && nr < chunk_rows {
                result.push(flat_chunk_index(nq, nr, chunk_cols));
            }
        }
    }
    result
}

// Returns the chunk (cq, cr) that a world position falls within.
pub fn world_to_chunk(wx: f32, wy: f32, hex_radius: i32) -> (i32, i32) {
    let (hq, hr) = world_to_hex(wx, wy, HEX_TILE_OUTER_RADIUS);
    let area  = hex_area(hex_radius);
    let shift = hex_shift(hex_radius);
    small_to_big(hq, hr, area, shift)
}

// ── Enemy phase ───────────────────────────────────────────────────────────────

// Returns the phase index an enemy should be in given current hp and max_hp.
pub fn compute_phase(hp: u32, max_hp: u32, phases: &[PhaseDef]) -> u8 {
    let hp_pct = hp as f32 / max_hp as f32;
    let mut phase = 0u8;
    for def in phases {
        if hp_pct <= def.hp_threshold && def.phase_index > phase {
            phase = def.phase_index;
        }
    }
    phase
}
