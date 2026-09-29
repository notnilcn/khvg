//! Terrain generation pipeline. `internal_generate_world_proc` (WorldDef-driven, weighted
//! Voronoi biomes/regions) and `internal_generate_world_manual` (explicit biome/region inputs)
//! are two front-ends over one parameterized pipeline (`run_generation`); they differ only in
//! how biome/region seeds and content are resolved. Every run executes the same passes in the
//! same deterministic order, sharing one `draw` counter for the PRNG:
//!
//! 1. resolve biome seeds and bucket every world hex under its nearest seed (`voronoi`)
//! 2. per biome: resolve region seeds, insert `BiomeRegion` rows
//! 3. per hex: ground pass (`ground`) → overlay pass (`overlay`) → decor pass (`decor`),
//!    with adjacency/layering rules checked against already-written neighbor hexes (`rules`)
//! 4. rivers over the written hexes (`rivers`) — weighted downhill walks
//! 5. roads between the inserted region centers (`roads`) — wrap-aware Dijkstra
//!
//! The emitted `HexTile`/`HexDecor` rows are the server-side payload that populates the
//! client's per-chunk tile scenes (ground/overlay hexes, decor props). HexTile also carries
//! the per-edge river/road masks passes 4-5 write — each edge is stamped on both of its hexes
//! (masks.rs invariant).

use std::collections::{HashMap, HashSet};
use spacetimedb::{ReducerContext, Table};
use crate::world::def_tables::{biome_def, biome_region_def, world_def, DecorConfig, GroundTextureConfig, ManualBiomeInput, OverlayTextureConfig, RegionWeight};
use crate::main::global::{ELEVATION_LEVELS, ELEVATION_NOISE_SCALE, WORLD_HEIGHT_PER_LEVEL};
use crate::world::instance_tables::{biome_region, hex_decor, hex_tile, BiomeRegion, HexTile};
use crate::world::hex::{enumerate_world_hexes, hex_to_world, HexCell};
use crate::world::noise::{value_noise2d, ELEVATION_NOISE_SEED_OFFSET};
use crate::world::prng::pick_index;
use crate::world::reducers::height_to_level;
use crate::world::tables::{building_tile, map_config, BuildingTile, MapConfig};

mod decor;
mod ground;
mod mst_test;
mod overlay;
mod rivers;
mod roads;
mod rules;
mod voronoi;

use decor::place_decor;
use ground::choose_ground;
use overlay::choose_overlay;
use rules::{decided_neighbor_tags, DecidedHexTags, NeighborTagMap};
use voronoi::{combine_biome_weights, distribute_biome_seeds, group_cells_by_seeds, nearest_seed_index, validate_weights_sum_100};

fn pick_rotation(allowed_rotations: &[u8], seed: u64, draw: &mut u64) -> u8 {
    // Hex tiles have 6 orientations (60-degree steps); an explicit allow-list (a subset
    // of 0-5) still wins when content provides one.
    if allowed_rotations.is_empty() { pick_index(6, seed, draw) as u8 }
    else { allowed_rotations[pick_index(allowed_rotations.len(), seed, draw)] }
}

fn clear_world_geometry(ctx: &ReducerContext) {
    for region in ctx.db.biome_region().iter() {
        ctx.db.biome_region().region_id().delete(region.region_id);
    }
    for tile in ctx.db.hex_tile().iter() {
        ctx.db.hex_tile().tile_id().delete(tile.tile_id);
    }
    for decor in ctx.db.hex_decor().iter() {
        ctx.db.hex_decor().decor_id().delete(decor.decor_id);
    }
}

// One hex: run the ground pass, then the overlay pass on top of the ground pick, record the
// decided tags for not-yet-written neighbor hexes, and emit the HexTile row. Returns None when
// the biome has no ground content (the cell gets no HexTile row at all, as before).
#[allow(clippy::too_many_arguments)]
fn write_hex_tile(ctx: &ReducerContext, cfg: &MapConfig, cell: &HexCell, biome_id: &str, region_id: u64, ground_textures: &[GroundTextureConfig], overlay_textures: &[OverlayTextureConfig], ground_noise_scale: f32, overlay_noise_scale: f32, seed: u64, draw: &mut u64, neighbor_tags: &mut NeighborTagMap) -> Option<HexTile> {
    if ground_textures.is_empty() { return None; }
    let ground_chances: Vec<f32> = ground_textures.iter().map(|g| g.chance).collect();

    let neighbors = decided_neighbor_tags(neighbor_tags, cell.hex_q, cell.hex_r, cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows);
    let (texture_id, rotation, ground_tags) = choose_ground(ctx, cell, ground_textures, &ground_chances, ground_noise_scale, seed, draw, &neighbors);
    let (overlay_texture_id, overlay_rotation, overlay_scale, overlay_tags) = choose_overlay(ctx, cell, overlay_textures, overlay_noise_scale, &ground_tags, seed, draw, &neighbors);

    neighbor_tags.insert((cell.hex_q, cell.hex_r), DecidedHexTags { ground_tags, overlay_tags });

    Some(ctx.db.hex_tile().insert(HexTile {
        tile_id: 0, hex_q: cell.hex_q, hex_r: cell.hex_r, chunk_index: cell.chunk_index,
        texture_id, rotation, overlay_texture_id, overlay_rotation, overlay_scale,
        biome_id: biome_id.to_string(), region_id,
        river_mask: 0, road_mask: 0, wall_mask: 0,
    }))
}

// One fully-resolved region: where its Voronoi seed sits plus everything needed to insert its
// BiomeRegion row.
#[derive(Clone)]
struct RegionPlan {
    seed_position: (f32, f32),
    center_hex_q: i32,
    center_hex_r: i32,
    enemy_template_ids: Vec<String>,
    max_enemies: u32,
    spawn_radius: f32,
}

// How a biome's regions get their seeds: explicitly placed (manual) or drawn one per
// RegionWeight entry with a resolvable BiomeRegionDef (procedural — resolved inside the
// per-biome loop so the shared `draw` counter ticks in exactly the pre-merge order).
enum RegionGenSpec {
    Skip,
    Fixed(Vec<RegionPlan>),
    Weighted(Vec<RegionWeight>),
}

// One biome's worth of generation input, resolved from either a WorldDef/BiomeDef chain
// (procedural) or a ManualBiomeInput (manual).
struct BiomeGen {
    biome_id: String,
    seed_positions: Vec<(f32, f32)>,
    ground_textures: Vec<GroundTextureConfig>,
    overlay_textures: Vec<OverlayTextureConfig>,
    ground_noise_scale: f32,
    overlay_noise_scale: f32,
    decor_configs: Vec<DecorConfig>,
    decor_noise_scale: f32,
    decor_min_separation: i32,
    regions: RegionGenSpec,
}

impl BiomeGen {
    // A biome whose BiomeDef couldn't be resolved: its seeds still claim cells (those cells
    // simply get no content), matching the old loop's def-missing `continue`.
    fn skipped(biome_id: String, seed_positions: Vec<(f32, f32)>) -> BiomeGen {
        BiomeGen {
            biome_id,
            seed_positions,
            ground_textures: Vec::new(),
            overlay_textures: Vec::new(),
            ground_noise_scale: 0.0,
            overlay_noise_scale: 0.0,
            decor_configs: Vec::new(),
            decor_noise_scale: 0.0,
            decor_min_separation: 0,
            regions: RegionGenSpec::Skip,
        }
    }
}

fn run_generation(ctx: &ReducerContext, cfg: &MapConfig, cells: Vec<HexCell>, seed: u64, biomes: Vec<BiomeGen>, mut draw: u64) -> Result<(), String> {
    let (lap_q, lap_r) = ((cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));

    clear_world_geometry(ctx);

    // Elevation pass: sample a dedicated value-noise field as a world-unit height (the addon's
    // `height_map` value, unclamped) and stamp it onto every BuildingTile row
    // (clear_world_geometry never touches building_tile). The gameplay elevation level is
    // derived from the height and collected into a lookup map for the river/road mask passes
    // at the end. A pure function of the seed — consumes no PRNG draws, so the deterministic
    // pass order below is untouched. Hexes covered by the MST test fixture (chunks (0, 0) and
    // (0, 1), mst_test) use the authored scene data instead — scaled to game units, quantized
    // to whole units, and carrying the addon's per-cell smooth flag.
    let elevation_seed = seed ^ ELEVATION_NOISE_SEED_OFFSET;
    let mut elevation_by_hex: HashMap<(i32, i32), i16> = HashMap::new();
    for tile in ctx.db.building_tile().iter() {
        let (height, smooth) = match mst_test::cell_at(tile.hex_q, tile.hex_r, cfg) {
            Some(cell) => (cell.height, cell.smooth),
            None => {
                let (wx, wy) = hex_to_world(tile.hex_q, tile.hex_r, cfg.hex_outer_radius);
                let height = (value_noise2d(wx, wy, ELEVATION_NOISE_SCALE, elevation_seed)
                    * ELEVATION_LEVELS as f32 * WORLD_HEIGHT_PER_LEVEL) as i16;
                (height, false)
            }
        };
        elevation_by_hex.insert((tile.hex_q, tile.hex_r), height_to_level(height));
        ctx.db.building_tile().tile_id().update(BuildingTile { height, smooth, ..tile });
    }

    let mut neighbor_tags: NeighborTagMap = HashMap::new();
    let mut decided_decor: HashSet<(i32, i32)> = HashSet::new();
    // In-memory output rows for the mask passes; persisted after all passes run.
    let mut tiles: HashMap<(i32, i32), HexTile> = HashMap::with_capacity(cells.len());
    let mut region_centers: Vec<(u64, i32, i32)> = Vec::new();

    let seed_positions_per_biome: Vec<Vec<(f32, f32)>> = biomes.iter().map(|b| b.seed_positions.clone()).collect();
    let cells_by_biome = group_cells_by_seeds(&cells, &seed_positions_per_biome, lap_q, lap_r);

    for (biome_idx, biome) in biomes.iter().enumerate() {
        let biome_cells = &cells_by_biome[biome_idx];
        if biome_cells.is_empty() { continue; }

        let region_plans: Vec<RegionPlan> = match &biome.regions {
            RegionGenSpec::Skip => Vec::new(),
            RegionGenSpec::Fixed(plans) => plans.clone(),
            RegionGenSpec::Weighted(weights) => {
                if weights.is_empty() { Vec::new() } else {
                    validate_weights_sum_100(&weights.iter().map(|r| r.weight).collect::<Vec<_>>())?;
                    let mut plans: Vec<RegionPlan> = Vec::new();
                    // Same one-seed-per-entry rule as biomes, scoped to this biome's own cells;
                    // entries whose BiomeRegionDef is missing are skipped without a draw.
                    for rw in weights {
                        let region_def = match ctx.db.biome_region_def().region_def_id().find(&rw.region_def_id) {
                            Some(r) => r,
                            None => continue,
                        };
                        let cell = &biome_cells[pick_index(biome_cells.len(), seed, &mut draw)];
                        plans.push(RegionPlan {
                            seed_position: (cell.world_x, cell.world_y),
                            center_hex_q: cell.hex_q,
                            center_hex_r: cell.hex_r,
                            enemy_template_ids: region_def.enemy_template_ids.clone(),
                            max_enemies: region_def.max_enemies,
                            spawn_radius: region_def.spawn_radius,
                        });
                    }
                    plans
                }
            }
        };
        if region_plans.is_empty() { continue; }

        let mut region_seed_positions: Vec<(f32, f32)> = Vec::with_capacity(region_plans.len());
        let mut region_ids: Vec<u64> = Vec::with_capacity(region_plans.len());
        for plan in &region_plans {
            let inserted = ctx.db.biome_region().insert(BiomeRegion {
                region_id: 0, biome_id: biome.biome_id.clone(),
                center_hex_q: plan.center_hex_q, center_hex_r: plan.center_hex_r,
                enemy_template_ids: plan.enemy_template_ids.clone(),
                max_enemies: plan.max_enemies, spawn_radius: plan.spawn_radius,
            });
            region_centers.push((inserted.region_id, plan.center_hex_q, plan.center_hex_r));
            region_seed_positions.push(plan.seed_position);
            region_ids.push(inserted.region_id);
        }

        for cell in biome_cells {
            let idx = nearest_seed_index(cell.world_x, cell.world_y, &region_seed_positions, lap_q, lap_r);
            if let Some(tile) = write_hex_tile(ctx, cfg, cell, &biome.biome_id, region_ids[idx], &biome.ground_textures, &biome.overlay_textures, biome.ground_noise_scale, biome.overlay_noise_scale, seed, &mut draw, &mut neighbor_tags) {
                tiles.insert((cell.hex_q, cell.hex_r), tile);
            }
            place_decor(ctx, cell, &biome.biome_id, region_ids[idx], &biome.decor_configs, biome.decor_noise_scale, biome.decor_min_separation, seed, &mut draw, &neighbor_tags, &mut decided_decor);
        }
    }

    // Mask passes, after every hex row exists: rivers first (roads bridge them), then roads
    // between the region centers. Both only touch the in-memory rows; the changed rows are
    // persisted once, in enumerate_world_hexes order.
    rivers::generate_rivers(&mut tiles, &cells, &elevation_by_hex, cfg, seed, &mut draw);
    roads::generate_roads(&mut tiles, &elevation_by_hex, &region_centers, cfg);
    // The MST fixture scene is bare terrain: strip any generated river/road masks on and
    // around the fixture hexes (both sides of each incident edge) so no strips render there.
    mst_test::strip_edge_masks(&mut tiles, cfg);
    for cell in &cells {
        if let Some(tile) = tiles.remove(&(cell.hex_q, cell.hex_r)) {
            if tile.river_mask != 0 || tile.road_mask != 0 || tile.wall_mask != 0 {
                ctx.db.hex_tile().tile_id().update(tile);
            }
        }
    }

    Ok(())
}

pub fn internal_generate_world_proc(ctx: &ReducerContext, world_id: String, seed: u64) -> Result<(), String> {
    let world = ctx.db.world_def().world_id().find(&world_id).ok_or_else(|| format!("WorldDef '{}' not found.", world_id))?;
    if world.biome_weights.is_empty() { return Err("WorldDef has no biome_weights.".to_string()); }
    validate_weights_sum_100(&world.biome_weights.iter().map(|b| b.weight).collect::<Vec<_>>())?;

    let cfg = ctx.db.map_config().id().find(0u32).ok_or("MapConfig not found — call add_chunks first.")?;
    let cells = enumerate_world_hexes(cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    if cells.is_empty() { return Err("World has no hexes — call add_chunks first.".to_string()); }

    let mut draw: u64 = 0;
    let biome_weight_totals = combine_biome_weights(&world.biome_weights);
    let biome_seed_positions = distribute_biome_seeds(&biome_weight_totals, &cells, seed, &mut draw);

    let biomes: Vec<BiomeGen> = biome_weight_totals.iter().enumerate().map(|(i, (biome_id, _))| {
        let seed_positions = biome_seed_positions[i].clone();
        match ctx.db.biome_def().biome_id().find(biome_id) {
            Some(def) => BiomeGen {
                biome_id: biome_id.clone(),
                seed_positions,
                ground_textures: def.ground_textures.clone(),
                overlay_textures: def.overlay_textures.clone(),
                ground_noise_scale: def.ground_noise_scale,
                overlay_noise_scale: def.overlay_noise_scale,
                decor_configs: def.decor_configs.clone(),
                decor_noise_scale: def.decor_noise_scale,
                decor_min_separation: def.decor_min_separation,
                regions: RegionGenSpec::Weighted(def.region_weights.clone()),
            },
            None => BiomeGen::skipped(biome_id.clone(), seed_positions),
        }
    }).collect();

    run_generation(ctx, &cfg, cells, seed, biomes, draw)
}

pub fn internal_generate_world_manual(ctx: &ReducerContext, biomes: Vec<ManualBiomeInput>) -> Result<(), String> {
    if biomes.is_empty() { return Err("No biomes provided.".to_string()); }
    let cfg = ctx.db.map_config().id().find(0u32).ok_or("MapConfig not found — call add_chunks first.")?;
    let cells = enumerate_world_hexes(cfg.chunk_hex_radius, cfg.chunk_cols, cfg.chunk_rows, cfg.hex_outer_radius);
    if cells.is_empty() { return Err("World has no hexes — call add_chunks first.".to_string()); }

    let seed = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros() as u64;

    let biomes: Vec<BiomeGen> = biomes.iter().map(|b| BiomeGen {
        biome_id: b.biome_id.clone(),
        seed_positions: vec![hex_to_world(b.center_hex_q, b.center_hex_r, cfg.hex_outer_radius)],
        ground_textures: b.ground_textures.clone(),
        overlay_textures: b.overlay_textures.clone(),
        ground_noise_scale: b.ground_noise_scale,
        overlay_noise_scale: b.overlay_noise_scale,
        decor_configs: b.decor_configs.clone(),
        decor_noise_scale: b.decor_noise_scale,
        decor_min_separation: b.decor_min_separation,
        regions: RegionGenSpec::Fixed(b.regions.iter().map(|r| RegionPlan {
            seed_position: hex_to_world(r.center_hex_q, r.center_hex_r, cfg.hex_outer_radius),
            center_hex_q: r.center_hex_q,
            center_hex_r: r.center_hex_r,
            enemy_template_ids: r.enemy_template_ids.clone(),
            max_enemies: r.max_enemies,
            spawn_radius: r.spawn_radius,
        }).collect()),
    }).collect();

    run_generation(ctx, &cfg, cells, seed, biomes, 0)
}
