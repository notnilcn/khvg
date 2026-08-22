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

// ===== World Timers =====

// Seconds before an unclaimed loot drop expires.
pub const LOOT_DROP_EXPIRY: u64 = 300;
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
