// Single home for every tunable constant, grouped by subsystem.

// ===== World Generation & Map Grid =====

// Chunk size, in hex-grid resolution (not world units). Smaller = finer AOI granularity.
pub const DEFAULT_CHUNK_HEX_RADIUS: i32 = 2;
// Map width in chunks. Shrunk from 22 for the demo — with only ~45 enemies max (9 regions x 5)
// spread across the old ~9000-unit-wide map, players could wander for a long time without
// crossing paths with one. At 6, the map is ~2500 units wide, comfortably explorable on foot.
pub const DEFAULT_CHUNK_COLS: i32 = 6;
// Map height in chunks. Must evenly divide DEFAULT_CHUNK_COLS.
pub const DEFAULT_CHUNK_ROWS: i32 = 6;
// World-unit size of one hex tile. Not chunk size — that's DEFAULT_CHUNK_HEX_RADIUS.
pub const DEFAULT_HEX_OUTER_RADIUS: f32 = 48.0;
// Total Voronoi seed points spread across a WorldDef's biomes, split proportionally by each
// biome's combined biome_weights share — e.g. a 40% weight gets ~40% of this budget's seeds,
// which (on average, for randomly placed seeds) claims ~40% of the map's area. Same idea applies
// per-biome to region_weights, but that's not yet wired up to a budget — region weight values
// currently only gate validation, not area share.
pub const BIOME_VORONOI_SEED_BUDGET: usize = 16;

// ===== Area of Interest (AOI) & Camera View =====

// Subscription range (chunk rings) for players/enemies/loot.
pub const DEFAULT_AOI_CHUNK_RADIUS: i32 = 2;
// Subscription range (chunk rings) for terrain tiles.
pub const DEFAULT_TERRAIN_AOI_CHUNK_RADIUS: i32 = 2;
// Client viewport height in px — used to derive CAMERA_VIEW_RADIUS.
pub const CLIENT_VIEWPORT_HEIGHT_PX: f32 = 360.0;
// Client camera zoom — used to derive CAMERA_VIEW_RADIUS.
pub const CLIENT_CAMERA_ZOOM: f32 = 0.75;
// Half the visible screen height in world units. Drives enemy move/attack sim ranges.
pub const CAMERA_VIEW_RADIUS: f32 = (CLIENT_VIEWPORT_HEIGHT_PX / CLIENT_CAMERA_ZOOM) / 2.0;
// Chunk rings searched for nearby players before an exact-distance sim check. Must cover the
// largest move/attack_sim_factor range or simulation silently stops working for it.
pub const SIMULATION_CHUNK_RINGS: i32 = 2;
// Chunk rings searched to check if a region has a player nearby before spawning into it.
pub const SPAWN_LOOKAHEAD_CHUNK_RINGS: i32 = 2;

// ===== Player Stats & Leveling =====

// Max HP at level 1.
pub const BASE_MAX_HP: i32 = 100;
// HP gained (and healed) per level up.
pub const HP_PER_LEVEL: i32 = 5;

// ===== Player Inventory =====

// First general-purpose inventory slot index.
pub const GENERAL_SLOT_START: usize = 15;
// Number of general inventory slots.
pub const GENERAL_SLOT_COUNT: usize = 8;

// ===== Enemy Simulation =====

// Fixed timestep (seconds) for enemy movement/phase simulation.
pub const BEHAVIOR_TICK_DT: f32 = 0.1;

// ===== World Timers =====

// Seconds before an unclaimed loot drop expires.
pub const LOOT_DROP_EXPIRY: u64 = 300;
// Seconds between consumable effect ticks (DoT/regen).
pub const CONSUMABLE_EFFECT_TICK_SECONDS: f32 = 1.0;
