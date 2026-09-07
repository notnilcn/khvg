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

// ===== Elevation =====

// Discrete elevation levels stamped per hex (BuildingTile.elevation) by world generation.
// The client mirrors this for its per-hex tint.
pub const ELEVATION_LEVELS: i16 = 8;
// Spatial frequency of the elevation noise field — low enough that plateaus span many hexes.
pub const ELEVATION_NOISE_SCALE: f32 = 0.004;
// Walkable deltas: a player may walk onto a hex up to this many levels above/below their
// current hex with no block and no fall. Anything steeper is a cliff: rising more than
// MAX_WALKABLE_UP_DELTA blocks the move; dropping more than MAX_WALKABLE_DOWN_DELTA is a fall.
pub const MAX_WALKABLE_UP_DELTA: i16 = 1;
pub const MAX_WALKABLE_DOWN_DELTA: i16 = 1;
// A drop larger than this deals fall damage; drops of (MAX_WALKABLE_DOWN_DELTA,
// FALL_SAFE_DROP] are still falls (lockout + displacement exemption) but deal zero damage.
pub const FALL_SAFE_DROP: i16 = 2;
// Fall damage per elevation level dropped.
pub const FALL_DAMAGE_PER_LEVEL: u32 = 15;
// Movement lockout (seconds) per level dropped — the client mirrors this for its fall tween.
pub const FALL_SECONDS_PER_LEVEL: f64 = 0.35;
// Max wrap-aware displacement a downhill-fall report may claim while staying exempt from
// report_movement's displacement budget (~2.5 hexes — a bounded grapple-style exemption).
pub const MAX_FALL_DISPLACEMENT: f32 = DEFAULT_HEX_OUTER_RADIUS * 2.5;
// Bullet elevation tolerances, relative to the bullet's origin-hex band: a bullet survives
// entering a hex up to BULLET_ELEVATION_CLIMB levels above its band, and can hit entities up
// to BULLET_ELEVATION_DROP levels below it. Exported constants only — no server logic reads
// them today (hence the allow); enforcement is client-side (the client mirrors these).
#[allow(dead_code)]
pub const BULLET_ELEVATION_CLIMB: i16 = 1;
#[allow(dead_code)]
pub const BULLET_ELEVATION_DROP: i16 = 2;

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
// Flat base value of each of the six allocatable stats — level-ups don't raise stats
// directly, they grant skill points (see PlayerStatAllocation).
pub const BASE_STAT: i32 = 10;
// Skill points granted per level, allocatable into the six stats via allocate_stat.
pub const SKILL_POINTS_PER_LEVEL: u32 = 2;
// Max distance from the player an ability target point may be; activate_ability clamps
// the client-supplied cursor position to this range.
pub const MAX_ABILITY_TARGET_RANGE: f32 = 600.0;
// Visual linger (seconds) of a Spell cast's AbilityVisualEvent flash (P5) — damage is
// instant; this is only how long clients keep the Polygon2D on screen. Mirrored client-side
// as SpellVisualComponent.DefaultDuration for the caster's optimistic render.
pub const SPELL_VISUAL_SECONDS: f32 = 0.5;
// Max (wrap-aware) distance between a player and the enemy whose bullet they report being
// hit by. Generous on purpose — bullets outlive their firing enemy's attack range and the
// player may have moved — since report_hit is self-reported anyway; this only catches
// nonsense attributions, and the no-hit wager corroborates against player_hit_flag.
pub const HIT_VALIDATION_RADIUS: f32 = CAMERA_VIEW_RADIUS * 4.0;
// Move speed (world units/sec) with no gear — the base PlayerData.base_speed resolves
// over. Items raise it via StatKind::BaseSpeed modifiers; it is not allocatable.
pub const BASE_SPEED: f32 = 100.0;

// ===== Player Inventory =====

// Slot layout (index ranges per SlotRole) is constructed in try_scaffold_profile; the client
// mirrors those ranges in LocalPlayerInventoryComponent: 0 weapon, 1-4 hotbar, 5 accessory,
// 6 armor, 7-30 general, 31 bag, 32-37 ability. First ability slot index:
pub const ABILITY_SLOT_START: u32 = 32;
// Number of ability slots (design doc: n=6; shields cost 6, tomes 3, spells 2).
pub const ABILITY_SLOT_COUNT: u32 = 6;
// Potency multiplier for an ability by the position of its span's head within the ability
// region — "order matters, first ability stronger" (design doc 03). Applied to passive stat
// modifiers in recompute_stats and to effect potency in activate_ability.
pub const ABILITY_POSITION_MULTIPLIERS: [f32; ABILITY_SLOT_COUNT as usize] = [1.5, 1.25, 1.1, 1.0, 0.9, 0.8];

// ===== Enemy Simulation =====

// Fixed timestep (seconds) for enemy movement/phase simulation.
pub const BEHAVIOR_TICK_DT: f32 = 0.1;

// Max shots one Repeat step may fire in a single behavior tick when catching up on elapsed
// intervals (a 25/s emitter needs ~3 per tick in steady state). Bounds the burst dumped
// after a stall, e.g. when an enemy re-enters simulation range.
pub const MAX_CATCHUP_SHOTS: usize = 8;
// Per-tick enemy simulation budget for tick_enemy_behavior. The biome spawner caps live
// enemies per region, but admin spawn_enemy does not — without this a spawn flood makes
// one 10 Hz tick simulate (and fire/row-insert for) an unbounded set. Lowest enemy_ids
// win; overflow is logged, not silent. Never binds in normal play (biome cap ≈ 45).
pub const BEHAVIOR_TICK_ENEMY_BUDGET: usize = 96;
// Per-visit Multi-step shot cap. Multi emits its whole shots vec at once (content-sized,
// no pacing), so one authored/admin-upserted mega-Multi is a row-insert storm (event +
// attestation per shot, per firing enemy, per tick). No shipped content uses Multi.
pub const MAX_MULTI_SHOTS: usize = 24;
// Global live-enemy cap for the automatic biome spawner (9 regions × max_enemies 5 = 45
// biome max; headroom is for admin spawns). Per-region caps alone allow 1 spawn per
// region per 2 s tick with unbounded behavior-tree fan-out rows each.
pub const MAX_LIVE_ENEMIES: u32 = 64;
// Biome spawns per 2 s tick. Each spawn inserts a behavior-tree fan-out (Enemy +
// Behavior + Phase/Attack/Step rows), so this — not 1-per-region — bounds per-tick
// insert pressure. Admin spawn_enemy stays operator-gated and uncapped.
pub const MAX_SPAWNS_PER_TICK: u32 = 3;

// Context-steering movement (see enemy/new_enemy_movement_system.md). Angles here are
// radians — the movement layer's unit.
// Fixed candidate directions in the per-tick interest map (360°/12 = 30° steps).
pub const STEERING_DIRECTIONS: usize = 12;
// How far ahead (world units) each candidate direction is probed for elevation cliffs.
// ~1.5 hexes: far enough to steer along a cliff edge instead of bumping into it.
pub const STEERING_PROBE_DISTANCE: f32 = DEFAULT_HEX_OUTER_RADIUS * 1.5;
// Radius (world units) within which enemies steer away from each other (~2 hexes).
pub const SEPARATION_RADIUS: f32 = DEFAULT_HEX_OUTER_RADIUS * 2.0;
// Max per-pair rotation (radians, ~20°) applied to separation away-directions — the
// doc's anti-jitter fix: push away at a slight angle so neighbors don't tug-of-war.
pub const SEPARATION_ANGLE_OFFSET: f32 = 0.35;
// Per-enemy Orbit range offset magnitude (world units) — the doc's anti-clumping fix:
// offset follow distances so orbits don't coincide.
pub const ORBIT_RANGE_JITTER: f32 = 32.0;
// Cap (radians/second) on a Meander heading's turn rate — bounds the sway even for
// content-authored sway_radians above it (~70°/s: curves read, spins don't).
pub const MEANDER_MAX_TURN_RATE: f32 = 1.2;
// Seed offset decorrelating the meander sway noise field from the other value_noise2d
// users (worldgen fields in world/noise.rs).
pub const MEANDER_NOISE_SEED_OFFSET: u64 = 0xA24BAED4963EE407;

// ===== World Timers =====

// Seconds before an unclaimed loot drop expires.
pub const LOOT_DROP_EXPIRY: u64 = 10;
// Seconds between consumable effect ticks (DoT/regen).
pub const CONSUMABLE_EFFECT_TICK_SECONDS: f32 = 1.0;

// ===== Status Effects (status/ module) =====

// Cursor-pick radius for mark abilities: the enemy nearest the (range-clamped) cursor
// point within this radius is marked.
pub const MARK_PICK_RADIUS: f32 = 100.0;
// Bleed purge (NoDamageTaken): seconds without taking any hit before the mark sheds.
pub const BLEED_NO_DAMAGE_SECONDS: f32 = 2.0;
// Bleed purge (StayClose): accumulated seconds within BLEED_STAY_CLOSE_RADIUS of the
// marked enemy before the mark sheds.
pub const BLEED_STAY_CLOSE_SECONDS: f32 = 5.0;
pub const BLEED_STAY_CLOSE_RADIUS: f32 = 200.0;
// Bleed purge (DealDamage): total damage dealt to the marked enemy before the mark sheds.
pub const BLEED_DEAL_DAMAGE_THRESHOLD: f32 = 1.0;
// NoHits wager corroboration: this many independent witness flags (player_hit_flag) for the
// victim during the window clamp a "0 hits taken" claim to a 1x payout. Solo play has no
// witnesses — accepted and documented in the trust model.
pub const NO_HIT_WITNESS_THRESHOLD: u32 = 2;
// StoreIncomingBash: after the store window expires the pool freezes and the row re-arms
// for this many seconds, waiting for the next shield bash to consume it.
pub const BASH_HOLD_SECONDS: f32 = 30.0;
// Stagger bar (Enemy.stagger, 0–100) bleed-off per second on the 1 Hz status tick.
pub const STAGGER_DECAY_PER_SECOND: f32 = 20.0;
// Ground zones (P3): the lifetime stamped on status rows a zone re-applies to its occupants
// every 1 Hz tick — leaving the zone sheds the effect within roughly this many seconds.
pub const ZONE_STATUS_REFRESH_SECONDS: f32 = 1.5;
// Magnitude stamped on a bash's Stunned enemy-status row. Irrelevant for Pause templates
// (all current seeds); it is the slow/speed-up factor for Slowed/Enraged ones.
pub const BASH_STUN_MAGNITUDE: f32 = 0.5;
// Ability charge (P4): max seconds a charge can be held — the 1 Hz status tick
// auto-releases at this age so a dropped client can't hold the SelfSlow forever.
pub const MAX_CHARGE_SECONDS: f32 = 3.0;
// Move-speed multiplier while a charge is held: stamped as the SelfSlow status's
// slow_factor and enforced by report_movement's speed clamp. Mirrored client-side
// (AbilityChargeComponent) for the local feel.
pub const CHARGE_SELF_SLOW_FACTOR: f32 = 0.5;
// Charge-scaled range multiplier floor: range scales as
// (CHARGE_MIN_RANGE_FRACTION + (1 − it) × charge_fraction) — a 0-charge release still
// goes 25%. The client mirrors this formula (AbilityChargeComponent.ChargeRangeMultiplier).
pub const CHARGE_MIN_RANGE_FRACTION: f32 = 0.25;

// ===== Server-Side Validation (anti-cheat / rate limits) =====

// report_movement displacement slack: a report may move at most
// base_speed × dt_since_last_report × MOVE_SLACK from the stored position row before the
// server clamps it (clamped along the reported direction, not rejected — rejecting
// rubber-bands legit lag spikes). 2× absorbs jitter/bursted reports.
pub const MOVE_SLACK: f32 = 2.0;
// Floor for the movement-clamp budget, and the whole budget when there is no previous
// report to measure dt against (first report after join/teleport). 64 units ≈ 1.3 hexes.
pub const MIN_REPORT_DISPLACEMENT: f32 = 64.0;
// report_enemy_hit sustained-rate budget per 1s window: equipped weapon fire_rate ×
// effective shot_count × this slack. 2× absorbs buffered/burst client fire without
// opening a damage-farming tap.
pub const HIT_RATE_SLACK: f32 = 2.0;
// report_hit (self-reported damage TAKEN) cap per 1s window. Generous on purpose —
// bullet-hell games genuinely hit you a lot; this only bounds the StoreIncoming farming
// loop's input rate.
pub const INCOMING_HIT_RATE_CAP: u32 = 30;
// Wrap-aware interaction distance for pickup_drop / take_from_chest, checked against the
// caller's position row.
pub const INTERACT_RADIUS: f32 = 128.0;
// Enemy Repeat-step catch-up backlog cap, in fire intervals: an enemy re-entering
// simulation range with a stale step_timer fires at most this many backlog shots per
// tick instead of a seconds-long wall of bullets.
pub const BACKLOG_CAP: u32 = 2;
// Seconds a Grapple cast's PlayerDisplacementAllowance stays valid (the client tween is
// 0.25s; the margin covers report latency).
pub const GRAPPLE_ALLOWANCE_SECONDS: f32 = 1.0;
// player_hit_flag rows older than this are swept by the 1 Hz status tick — no NoHits
// wager window outlives it, and the table must not grow unbounded.
pub const HIT_FLAG_MAX_AGE_SECONDS: u64 = 60;
// BulletFireAttestation rows older than this are swept by the 1 Hz status tick. Must
// exceed the longest bullet lifetime plus report lag so legit late hits still attest;
// rejected claims fail safe (no damage applied). Unclaimed rows (missed volleys) must
// not accumulate — every fired event inserts one.
pub const BULLET_ATTESTATION_TTL_SECONDS: u64 = 30;
// Per-player cooldown between takes from a regenerating chest (the row is never
// consumed, so the take rate is gated instead).
pub const CHEST_TAKE_COOLDOWN_SECONDS: u64 = 1;
// report_screen_rotation delta-check: updates smaller than this (radians, ~0.5 degrees)
// don't rewrite the PlayerRotation row. The client already gates sends on facing change
// (PositionSyncComponent.RotationEpsilon) — this is the server-side belt.
pub const ROTATION_REPORT_EPSILON: f32 = 0.0087;
// Safety bound for compute_level's loop. u64 xp_for_level math already prevents the old
// u32 overflow (~level 2930); the bound just keeps a corrupt xp value from spinning.
pub const MAX_LEVEL: u32 = 10_000;
