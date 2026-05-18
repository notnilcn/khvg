/*
```rust */
use spacetimedb::{ReducerContext, Identity, Table};
use hexx::{Hex, HexLayout, HexOrientation, Vec2};
use super::tables::*;

pub const BASE_MAX_HP: i32 = 100;
pub const HP_PER_LEVEL: i32 = 5;
pub const DEFAULT_HEX_OUTER_RADIUS: f32 = 32.0; // fallback outer radius before MapConfig is written
pub const DEFAULT_CHUNK_HEX_RADIUS: i32 = 5; // fallback chunk size before MapConfig is written
pub const DEFAULT_AOI_CHUNK_RADIUS: i32 = 3; // fallback chunk size before MapConfig is written

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

// ── Hex grid math (wrappers around hexx) ─────────────────────────────────────

// Number of small hexes per chunk. Delegates to Hex::range_count.
pub fn hex_area(hex_radius: i32) -> i32 {
    Hex::range_count(hex_radius as u32) as i32
}

// Shift constant for hexmod arithmetic. hexx::Hex::shift is pub(crate) so the
// formula is reproduced here; it is identical to hexx's implementation.
pub fn hex_shift(hex_radius: i32) -> i32 {
    3 * hex_radius + 2
}

// World position → small hex axial (q, r). Delegates to HexLayout::world_pos_to_hex.
pub fn world_to_hex(wx: f32, wy: f32, r_outer: f32) -> (i32, i32) {
    let layout = HexLayout {
        orientation: HexOrientation::Pointy,
        scale: Vec2::splat(r_outer),
        origin: Vec2::ZERO,
    };
    let h = layout.world_pos_to_hex(Vec2::new(wx, wy));
    (h.x, h.y)
}

// Small hex (q, r) → chunk (cq, cr). Delegates to Hex::to_lower_res.
// `area` is unused; radius is derived from `shift` (shift = 3r+2 → r = (shift-2)/3).
pub fn small_to_big(hq: i32, hr: i32, _area: i32, shift: i32) -> (i32, i32) {
    let radius = ((shift - 2) / 3) as u32;
    let chunk = Hex::new(hq, hr).to_lower_res(radius);
    (chunk.x, chunk.y)
}

// Compact local index m ∈ [0, area) for a small hex within its chunk.
// Delegates to Hex::to_hexmod_coordinates.
// `area` is unused; radius is derived from `shift` (shift = 3r+2 → r = (shift-2)/3).
pub fn hexmod(hq: i32, hr: i32, _area: i32, shift: i32) -> i32 {
    let radius = ((shift - 2) / 3) as u32;
    Hex::new(hq, hr).to_hexmod_coordinates(radius) as i32
}

// Center of chunk (cq, cr) as small hex axial coords. Delegates to Hex::to_higher_res.
pub fn chunk_center_hex(cq: i32, cr: i32, hex_radius: i32) -> (i32, i32) {
    let center = Hex::new(cq, cr).to_higher_res(hex_radius as u32);
    (center.x, center.y)
}

// Local hexmod index → relative small hex (q, r) offset from chunk center.
// Delegates to Hex::from_hexmod_coordinates. `shift` is unused; hexx derives it
// internally from hex_radius.
pub fn inv_hexmod(m: i32, hex_radius: i32, _shift: i32) -> (i32, i32) {
    let rel = Hex::from_hexmod_coordinates(m as u32, hex_radius as u32);
    (rel.x, rel.y)
}

// Bijective i64 chunk index using hex spiral order (ring ρ = max(|x|,|y|,|z|) in cube coords).
// Unique for all integer (cq, cr) including negative values. Mirrors _hex_spiral_index in
// hex_grid_overlay.gd so the server, overlay, and future AoI client code all share one formula.
pub fn spiral_chunk_index(cq: i32, cr: i32) -> i64 {
    let x = cq;
    let z = cr;
    let y = -cq - cr;
    let rho = x.abs().max(y.abs()).max(z.abs());
    if rho == 0 { return 0; }
    let b = (3 * rho * (rho - 1) + 1) as i64;
    let (arm, step): (i64, i64) =
        if z == -rho && x > 0          { (0, y as i64) }
        else if y == rho && x > -rho   { (1, -x as i64) }
        else if x == -rho && z < rho   { (2, z as i64) }
        else if z == rho && x < 0      { (3, (x + rho) as i64) }
        else if y == -rho && x >= 0 && x < rho { (4, x as i64) }
        else                           { (5, (y + rho) as i64) };
    b + arm * rho as i64 + step
}

// Small hex axial (q, r) → world center position. Delegates to HexLayout::hex_to_world_pos.
pub fn hex_to_world(hq: i32, hr: i32, r_outer: f32) -> (f32, f32) {
    let layout = HexLayout {
        orientation: HexOrientation::Pointy,
        scale: Vec2::splat(r_outer),
        origin: Vec2::ZERO,
    };
    let pos = layout.hex_to_world_pos(Hex::new(hq, hr));
    (pos.x, pos.y)
}

// Returns chunk spiral indices for all chunks within `rings` rings of (cq, cr),
// clamped to map bounds. Delegates to Hex::range (yields 3r(r+1)+1 chunks before clamping).
// `rings` defaults to DEFAULT_AOI_CHUNK_RADIUS when None (r=0→1, r=1→7, r=2→19).
pub fn surrounding_chunk_indices(cq: i32, cr: i32, chunk_cols: i32, chunk_rows: i32, rings: Option<i32>) -> Vec<i64> {
    let rings = rings.unwrap_or(DEFAULT_AOI_CHUNK_RADIUS) as u32;
    Hex::new(cq, cr)
        .range(rings)
        .filter(|h| h.x >= 0 && h.x < chunk_cols && h.y >= 0 && h.y < chunk_rows)
        .map(|h| spiral_chunk_index(h.x, h.y))
        .collect()
}

// Returns the chunk (cq, cr) that a world position falls within.
pub fn world_to_chunk(wx: f32, wy: f32, chunk_hex_radius: i32, hex_outer_radius: f32) -> (i32, i32) {
    let (hq, hr) = world_to_hex(wx, wy, hex_outer_radius);
    let chunk = Hex::new(hq, hr).to_lower_res(chunk_hex_radius as u32);
    (chunk.x, chunk.y)
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
/*
```
*/