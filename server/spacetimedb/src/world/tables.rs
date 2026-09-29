use spacetimedb::{table, Identity, ReducerContext, SpacetimeType};
use crate::main::global::{DEFAULT_CHUNK_HEX_RADIUS, DEFAULT_HEX_OUTER_RADIUS};

#[derive(SpacetimeType, Clone)]
pub enum TextureKind {
    Player,
    Enemy,
    Projectile,
    Item,
    Environment,
}

#[table(accessor = map_config, public)]
pub struct MapConfig {
    #[primary_key]
    pub id: u32,
    pub chunk_hex_radius: i32,
    pub chunk_cols: i32,
    pub chunk_rows: i32,
    pub hex_outer_radius: f32,
    // World-space displacement of one full torus lap in each axis — same vectors wrap_world_pos
    // uses internally. Exposed so clients can pick the correct wrapped copy of distant tiles/
    // entities when rendering near a chunk-grid seam, without re-deriving the hex-of-hexes math.
    pub lap_q_x: f32,
    pub lap_q_y: f32,
    pub lap_r_x: f32,
    pub lap_r_y: f32,
}

impl MapConfig {
    /// Loads the singleton MapConfig row (id 0), falling back to compile-time defaults when it
    /// doesn't exist yet (i.e. before add_chunks has ever run). Radii fall back to the DEFAULT_*
    /// constants and lap vectors to zero; `fallback_chunk_cols`/`fallback_chunk_rows` are
    /// caller-chosen because call sites deliberately disagree on the degenerate grid:
    /// 0/0 trips wrap_world_pos's `<= 0` guard (disables torus wrapping entirely), while 1/1
    /// collapses the grid to a single chunk so wrap/AOI math still runs, degenerately.
    pub fn load(ctx: &ReducerContext, fallback_chunk_cols: i32, fallback_chunk_rows: i32) -> MapConfig {
        ctx.db.map_config().id().find(0u32).unwrap_or(MapConfig {
            id: 0,
            chunk_hex_radius: DEFAULT_CHUNK_HEX_RADIUS,
            chunk_cols: fallback_chunk_cols,
            chunk_rows: fallback_chunk_rows,
            hex_outer_radius: DEFAULT_HEX_OUTER_RADIUS,
            lap_q_x: 0.0,
            lap_q_y: 0.0,
            lap_r_x: 0.0,
            lap_r_y: 0.0,
        })
    }
}

#[table(accessor = building_tile, public)]
pub struct BuildingTile {
    #[primary_key] #[auto_inc]
    pub tile_id: u64,
    #[index(btree)]
    pub hex_q: i32,
    pub hex_r: i32,
    pub chunk_q: i32,
    pub chunk_r: i32,
    pub local_index: i32,
    #[index(btree)]
    pub chunk_index: i64,
    pub building_type: String,
    pub owner_id: Option<Identity>,
    // World-unit Y height of this hex's cell top, stamped by world generation
    // (terrain::run_generation). This is the addon's `height_map` value: the client passes it
    // straight to MarchingSquaresCellChunk.draw_height, and the addon's merge modes turn small
    // deltas into ramps. Unclamped. Gameplay derives the integer elevation level as
    // floor(height / WORLD_HEIGHT_PER_LEVEL) (world::reducers::height_to_level); deltas within
    // ±MAX_WALKABLE_*_DELTA levels are walkable, anything steeper is a cliff (see
    // report_movement).
    pub height: i16,
}

#[table(accessor = texture_entry, public)]
pub struct TextureEntry {
    #[primary_key]
    pub texture_id: String,
    pub res_path2d: String,
    // Empty string = no 3D representation (e.g. 2D shadow decals — 3D lighting casts real shadows).
    pub res_path3d: String,
    pub kind: TextureKind,
}

#[derive(SpacetimeType, Clone)]
pub enum AnimationKind {
    Locomotion,
    Combat,
    Interaction,
    Emote,
}

// A named animation clip living inside a source scene's AnimationPlayer (e.g. the Universal
// Animation Library glbs). The client merges clips from res_path into the active character
// model's AnimationPlayer under animation_id and plays them by that id.
#[table(accessor = animation_entry, public)]
pub struct AnimationEntry {
    #[primary_key]
    pub animation_id: String,
    // Scene whose AnimationPlayer holds the clip (merged into the character model at runtime).
    pub res_path: String,
    // Clip name inside that scene's AnimationPlayer.
    pub clip_name: String,
    pub kind: AnimationKind,
}

// Allow-list: an overlay tagged `overlay_tag` may be layered on a base tagged `allowed_base_tag`.
// Default is deny — most overlay/base pairs don't make sense, so compatibility is opt-in.
#[table(accessor = layering_rule, public)]
pub struct LayeringRule {
    #[primary_key] #[auto_inc]
    pub rule_id: u64,
    pub overlay_tag: String,
    pub allowed_base_tag: String,
}

// Deny-list: tag_a and tag_b may never sit in laterally neighboring tiles (same-hex or
// cross-hex). Default is allow — most neighboring base textures are fine, only specific
// clashing pairs (e.g. swamp next to sand) get blocked. Symmetric: a row blocks tag_a next to
// tag_b in either direction, so only one row is needed per clashing pair.
#[table(accessor = base_adjacency_rule, public)]
pub struct BaseAdjacencyRule {
    #[primary_key] #[auto_inc]
    pub rule_id: u64,
    pub tag_a: String,
    pub tag_b: String,
}

// Same shape and symmetry as BaseAdjacencyRule, kept as a separate table (rather than one rule
// type for both layers) so the same tag string can never cross-apply between a base-to-base rule
// and an overlay-to-overlay rule just because they share a tag name.
#[table(accessor = overlay_adjacency_rule, public)]
pub struct OverlayAdjacencyRule {
    #[primary_key] #[auto_inc]
    pub rule_id: u64,
    pub tag_a: String,
    pub tag_b: String,
}

// Deny-list: decor tagged decor_tag may never be placed in a hex where any of its 6 wedges
// carries denied_base_tag (ground OR overlay tag) — not symmetric like the adjacency rules
// above, since this relates two different kinds of tag (decor vs. ground/overlay), not two
// occurrences of the same kind.
#[table(accessor = decor_ground_rule, public)]
pub struct DecorGroundRule {
    #[primary_key] #[auto_inc]
    pub rule_id: u64,
    pub decor_tag: String,
    pub denied_base_tag: String,
}
