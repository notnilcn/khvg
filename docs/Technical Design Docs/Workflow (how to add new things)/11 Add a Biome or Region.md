# 11 Add a Biome or Region

World content is three layers of defs: a `WorldDef` (weighted biome list) → `BiomeDef`s (textures, noise, decor, region list) → `BiomeRegionDef`s (enemy spawn pools). Generation turns those into `BiomeRegion`/`TriangleTile`/`HexDecor` rows via the deterministic Voronoi pipeline ([[03 World & Hex Grid|03]]). All of it is **data in seed code** — no client changes (terrain renders from subscribed rows), no server logic changes.

## Add a region to an existing biome

A region is a spawn pool plus a name. Two steps in `main/seeds.rs`:

1. Define it in `seed_world_defs` ([[main/seeds.rs##pub fn seed_world_defs|seed_world_defs]]) — either add a `seed_region_def` call (shares the uniform `["Enemy", "Archer"]` pool) or write the `BiomeRegionDef` out longhand to customize:

```rust
BiomeRegionDef::seed(ctx, BiomeRegionDef {
    region_def_id: "GrasslandRegion4".to_string(),
    display_name: "Grassland IV".to_string(),
    enemy_template_ids: vec!["Shaman".to_string()],   // your pool — see [[07 Add an Enemy|07]]
    max_enemies: 3,
    spawn_radius: 250.0,
});
```

2. Add it to the biome's `region_weights` — `RegionWeight { region_def_id: "GrasslandRegion4".to_string(), weight: 25.0 }` — and rebalance the existing weights to still sum to 100 (validation requires it; note region weights currently gate only that validation, not actual area share — [[03 World & Hex Grid|03]] Known gaps).

## Add a biome

Seed a `BiomeDef` in `seed_world_defs`, following Grassland ([[main/seeds.rs##biome_id: "Grassland"|Grassland]]), then reference it from the `WorldDef`'s `biome_weights` (Earth currently 40/30/30 — rebalance to sum 100):

```rust
BiomeDef::seed(ctx, BiomeDef {
    biome_id: "Swamp".to_string(),
    display_name: "Swamp".to_string(),
    ground_textures: vec![
        GroundTextureConfig { texture_id: "Mud".to_string(), chance: 70.0, tags: vec!["wet".to_string()], allowed_rotations: vec![] },
        GroundTextureConfig { texture_id: "Grass".to_string(), chance: 30.0, tags: vec!["soft".to_string()], allowed_rotations: vec![] },
    ],
    overlay_textures: vec![],     // see layering rules below before adding any
    region_weights: vec![
        RegionWeight { region_def_id: "SwampRegion1".to_string(), weight: 100.0 },
    ],
    ground_noise_scale: 0.006,    // lower = larger patches; <= 0 disables clustering
    overlay_noise_scale: 0.004,
    decor_configs: vec![],        // see decor note below
    decor_noise_scale: 0.004,
    decor_min_separation: 1,
});
```

Field notes:

- **Ground `chance`** is a relative weight among the biome's ground entries — needn't sum to 100 ([[world/def_tables.rs##pub struct GroundTextureConfig|GroundTextureConfig]]). Every referenced `texture_id` needs a `TextureEntry` ([[01 Add a Texture or Sprite|01]]) — terrain textures live under `client/Textures/World/`.
- **`tags`** are the compatibility currency: `LayeringRule` (allow-list: which overlay may sit on which ground tag), `BaseAdjacencyRule`/`OverlayAdjacencyRule` (deny-lists: which textures may never be neighbors), and `DecorGroundRule` (which decor is denied on which ground tag) all match on them. Seeded in `main/seeds.rs` ([[main/seeds.rs##pub fn seed_layering_rules|seed_layering_rules]] etc.) — adjacency rules are currently seeded *empty*, so nothing conflicts yet; add deny pairs there if your new textures clash.
- **Overlays** need a `LayeringRule` row allowing the (overlay tag, ground tag) pair or they never appear — the allow-list is the whole gate.
- **Decor** works end-to-end (`DecorConfig { texture_id, shadow_texture_id, chance, tags }`, paired shadow sprite, `decor_min_separation` spacing) but **no seeded biome uses it yet** — collision/walkability is undecided and the art reads poorly ([[03 World & Hex Grid|03]] Known gaps). Seed decor only if you're ready to own that decision.

## Rebuild the world

Defs don't retro-generate — the world is generated rows. After editing seeds:

```bash
cd server && bash build.sh   # reseeds defs AND regenerates the world via init
```

Or against a live server (admin): `upsert_biome_def` / `upsert_biome_region_def` / `upsert_world_def`, then force regeneration:

```bash
spacetime call --server local bullethell generate_world_proc Earth 1   # new seed value = new layout roll
```

Regeneration is a full replace of `BiomeRegion`/`TriangleTile`/`HexDecor` ([[03 World & Hex Grid|03]]) and pushes to every connected client live. Deterministic: same `(world_id, seed)` → same map.

## Verify

- In game: walk (or check the debug hex-grid overlays) to find the new biome's Voronoi territory; confirm its ground textures cluster and its regions spawn their enemy pool (`tick_enemy_spawn` keeps regions topped up only while a player is nearby).
- `spacetime sql --server local bullethell "SELECT biome_id, enemy_template_ids, max_enemies FROM biome_region"` to confirm the generated regions carry your pools.

## Gotchas

- **Weights must sum to 100** at both levels (biomes within the world, regions within a biome) — generation validates and rejects otherwise.
- **`clear_chunks` does not reset terrain** — it only wipes `BuildingTile` rows. Regeneration is `generate_world_proc`/`generate_world_manual` ([[09 Admin, Debug & World Lifecycle|09]]).
- **Every hex is buildable by default** (`internal_add_chunks` inserts a `BuildingTile` per hex); new biomes don't change that.
- Hand-authoring a world layout instead of procedural weights: `generate_world_manual` takes `Vec<ManualBiomeInput>` (explicit biome/region placements — [[world/def_tables.rs##pub struct ManualBiomeInput|ManualBiomeInput]]).
