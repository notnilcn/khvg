// Tag rules consulted during generation: which overlays may layer on which ground textures
// (LayeringRule, allow-list), which ground/overlay tag pairs may never be lateral neighbors
// (BaseAdjacencyRule/OverlayAdjacencyRule, deny-lists), and which decor tags are blocked by a
// hex's ground/overlay tags (DecorGroundRule). All four rule sets are table-driven content.

use std::collections::HashMap;
use spacetimedb::{ReducerContext, Table};
use crate::world::hex::{cross_hex_neighbor, same_hex_neighbor_indices};
use crate::world::tables::{base_adjacency_rule, decor_ground_rule, layering_rule, overlay_adjacency_rule};

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

// Tags of a wedge already written this generation pass, kept around just long enough for
// not-yet-written neighbor wedges to check adjacency against. Not persisted to TriangleTile.
#[derive(Default, Clone)]
pub struct DecidedWedgeTags {
    pub ground_tags: Vec<String>,
    pub overlay_tags: Vec<String>,
}

pub type NeighborTagMap = HashMap<(i32, i32, u8), DecidedWedgeTags>;

pub fn decided_neighbor_tags(map: &NeighborTagMap, hex_q: i32, hex_r: i32, tri_index: u8) -> Vec<&DecidedWedgeTags> {
    let mut out = Vec::with_capacity(3);
    for n in same_hex_neighbor_indices(tri_index) {
        if let Some(d) = map.get(&(hex_q, hex_r, n)) { out.push(d); }
    }
    let (nq, nr, ntri) = cross_hex_neighbor(hex_q, hex_r, tri_index);
    if let Some(d) = map.get(&(nq, nr, ntri)) { out.push(d); }
    out
}
