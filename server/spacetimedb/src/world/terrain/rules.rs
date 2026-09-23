// Tag rules consulted during generation: which overlays may layer on which ground textures
// (LayeringRule, allow-list), which ground/overlay tag pairs may never be lateral neighbors
// (BaseAdjacencyRule/OverlayAdjacencyRule, deny-lists), and which decor tags are blocked by a
// hex's ground/overlay tags (DecorGroundRule). All four rule sets are table-driven content.

use std::collections::HashMap;
use spacetimedb::{ReducerContext, Table};
use crate::world::tables::{base_adjacency_rule, decor_ground_rule, layering_rule, overlay_adjacency_rule};
use crate::world::wrap::wrapped_hex_neighbors;

pub fn overlay_compatible_with_base(ctx: &ReducerContext, overlay_tags: &[String], base_tags: &[String]) -> bool {
    ctx.db.layering_rule().iter().any(|r| overlay_tags.contains(&r.overlay_tag) && base_tags.contains(&r.allowed_base_tag))
}

pub fn base_tags_conflict(ctx: &ReducerContext, tags_a: &[String], tags_b: &[String]) -> bool {
    ctx.db.base_adjacency_rule().iter().any(|r|
        (tags_a.contains(&r.tag_a) && tags_b.contains(&r.tag_b)) || (tags_a.contains(&r.tag_b) && tags_b.contains(&r.tag_a)))
}

pub fn overlay_tags_conflict(ctx: &ReducerContext, tags_a: &[String], tags_b: &[String]) -> bool {
    ctx.db.overlay_adjacency_rule().iter().any(|r|
        (tags_a.contains(&r.tag_a) && tags_b.contains(&r.tag_b)) || (tags_a.contains(&r.tag_b) && tags_b.contains(&r.tag_a)))
}

pub fn decor_ground_conflict(ctx: &ReducerContext, decor_tags: &[String], hex_tags: &[String]) -> bool {
    ctx.db.decor_ground_rule().iter().any(|r| decor_tags.contains(&r.decor_tag) && hex_tags.contains(&r.denied_base_tag))
}

// Tags of a hex already written this generation pass, kept around just long enough for
// not-yet-written neighbor hexes to check adjacency against. Not persisted to HexTile.
#[derive(Default, Clone)]
pub struct DecidedHexTags {
    pub ground_tags: Vec<String>,
    pub overlay_tags: Vec<String>,
}

pub type NeighborTagMap = HashMap<(i32, i32), DecidedHexTags>;

// Tags of already-decided neighbors, canonicalized through wrap first so a seam hex consults its
// true across-the-seam neighbors. Wrapping brings the seam to interior parity — a neighbor is
// found iff it was already written, never one that hasn't been decided — not beyond it.
pub fn decided_neighbor_tags(map: &NeighborTagMap, hex_q: i32, hex_r: i32, chunk_hex_radius: i32, chunk_cols: i32, chunk_rows: i32) -> Vec<&DecidedHexTags> {
    let mut out = Vec::with_capacity(6);
    for (nq, nr) in wrapped_hex_neighbors(hex_q, hex_r, chunk_hex_radius, chunk_cols, chunk_rows) {
        if let Some(d) = map.get(&(nq, nr)) { out.push(d); }
    }
    out
}
