use spacetimedb::{table, SpacetimeType};
use crate::status::tables::{PurgeKind, WagerKind, ReleaseKind, EnemyStatusKind, ZoneKind};
use crate::enemy::instance_tables::AbilityVisualKind;

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum WeaponPattern {
    Single,
    Triple,
    Cluster,
}

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum EquipSlot {
    Weapon,
    Armor,
    Accessory,
    Bag,
    Consumable,
    Ability,
}

// The six allocatable stats are Strength/Wisdom/Dexterity + the three temperaments
// (DamageDealer/Supporter/Artisan) — temperaments are mechanically identical to normal
// stats, only conceptually different (Game Design Doc 03). Players get
// SKILL_POINTS_PER_LEVEL points per level to allocate across the six (see
// PlayerStatAllocation). Hp, Defense and BaseSpeed are modifier-only: gear/buffs grant
// them, but they can't be allocated into and have no PlayerStats field — Hp resolves
// into PlayerData.max_hp, Defense into PlayerData.defense, BaseSpeed into
// PlayerData.base_speed.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum StatKind { Strength, Wisdom, Dexterity, DamageDealer, Supporter, Artisan, Hp, Defense, BaseSpeed }

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum StatMode { Flat, Mult }

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct StatModifier { pub stat: StatKind, pub amount: f32, pub mode: StatMode }

// Gear requirements (Game Design Doc 03: classes emerge from gear locked behind stat
// thresholds). Checked against resolved PlayerStats when an item enters an equipped slot.
// All-zero means no requirement.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct StatRequirements {
    pub strength: u32,
    pub wisdom: u32,
    pub dexterity: u32,
    pub damage_dealer: u32,
    pub supporter: u32,
    pub artisan: u32,
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct WeaponBehavior {
    // Total damage per trigger pull, divided among the bullets fired (per-bullet =
    // damage / effective shot_count) so that multi-shot and single-shot patterns trade
    // off against enemy defence (Game Design Doc 02).
    pub damage: u32, pub range: f32, pub fire_rate: f32, pub projectile_speed: f32,
    pub shot_count: u32, pub zone_count: u32, pub pierce: bool,
    pub projectile_texture_id: String, pub pattern: WeaponPattern, pub spread_angle: f32,
}

// One variant per allocatable stat (the six of StatKind minus Hp/Defense).
#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ConsumableBuffEffect {
    Strength(f32),
    Wisdom(f32),
    Dexterity(f32),
    DamageDealer(f32),
    Supporter(f32),
    Artisan(f32),
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ConsumableEffect {
    Heal,
    Buff(ConsumableBuffEffect),
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct ConsumableBehavior { pub effect: ConsumableEffect, pub potency: f32, pub duration: f32 }

// What an ability actually does. Heal/Buff are self-targeted (potency = heal amount,
// duration = buff seconds). DeleteBullets/AttractBullets are cast at a target point with
// the f32 as the effect radius — the server appends a BulletControlEvent and every
// client resolves the proximity query locally (server tracks no live bullets).
// AttractBullets uses the ability's duration as the black hole's linger time (0 = one-shot).
// DeleteBulletsInRect/SplitBullets are the rectangle variants: from the player toward the
// cursor, encoded in the event as origin + far-end + width (see BulletControlEvent).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct RectParams { pub length: f32, pub width: f32 }

/// Bleed mark: hits from the marked enemy split `conversion` into a draining pool (the
/// rest lands instantly); the mark sheds per `purge`. ally_radius 0 = self only.
/// `outgoing` (the warrior's dealt-and-received variant) converts your hits on the mark
/// into an enemy DoT instead — applied on top of the incoming conversion.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct MarkBleedParams { pub purge: PurgeKind, pub conversion: f32, pub ally_radius: f32, pub pool_seconds: f32, pub outgoing: bool }

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct ReduceParams { pub pct: f32, pub ally_radius: f32 }

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct RegenParams { pub per_second: f32, pub ally_radius: f32 }

/// Store-damage mark: damage dealt to the marked enemy is withheld during the window, then
/// released at 2x/1x/0x per the wager. Threshold meanings per WagerKind — NoHits: max_hits
/// (more hits taken = 0x); Radius: r1 (never leave = 2x) / r2 (ever leave = 0x); Damage:
/// stored >= x = 2x, >= z = 1x. ally_radius 0 = self only.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct MarkStoreParams { pub wager: WagerKind, pub x: f32, pub z: f32, pub r1: f32, pub r2: f32, pub max_hits: u32, pub ally_radius: f32 }

/// Store-incoming: damage TAKEN during the window accumulates pre-mitigation. Smite
/// releases it as an AoE burst (release_radius) at window expiry — or early at the cursor
/// on recast; Bash freezes the pool at expiry and re-arms for ~30s, the next shield bash
/// consuming it (P2).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct StoreIncomingParams { pub release: ReleaseKind, pub release_radius: f32 }

/// Curse: a single-target enemy debuff (status/ module's ActiveEnemyStatusEffect) applied
/// to the cursor-picked enemy for the ability's duration. Magnitude meaning per kind —
/// DoT: damage per second; Slow/Vulnerability: fraction; ArmorBreak/Stunned: unused on apply.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct CurseParams { pub kind: EnemyStatusKind, pub magnitude: f32 }

/// Bash: melee swing — every enemy within range of the PLAYER takes weapon damage (through
/// the shared weapon path, so true-damage/mitigation/statuses apply) plus `stagger` stagger-
/// bar points; at 100 the enemy is Stunned (per its template's stun_behavior) for
/// stun_seconds and the bar resets. armor_break also applies ArmorBreak for stun_seconds.
/// An armed StoreIncomingBash row adds its frozen pool to each hit and is consumed.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct BashParams { pub stagger: f32, pub armor_break: bool, pub range: f32, pub stun_seconds: f32 }

/// Slash: deletes only bullets whose damage is at most max_bullet_damage, in a rectangle
/// from the player toward the cursor (the DeleteRect event encoding + the threshold in
/// duration as f32 bits — see BulletControlEvent). Server stays bullet-agnostic; the client
/// knows each bullet's damage from BulletPatternEvent.damage.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct SlashParams { pub length: f32, pub width: f32, pub max_bullet_damage: u32 }

/// Wall: a LINGERING delete-rect from the player toward the cursor that keeps erasing
/// bullets for the ability's duration (the Attract linger pattern as a rectangle).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct WallParams { pub length: f32, pub width: f32 }

/// CastZone (P3): places a GroundZone (status/ module) at the range-clamped cursor point;
/// the zone lives the ability's duration. Magnitude meaning per ZoneKind (status/tables.rs);
/// radius and magnitude both scale with the position multiplier.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct ZoneParams { pub kind: ZoneKind, pub radius: f32, pub magnitude: f32 }

/// Teleport (P4): moves the caster toward the cursor, clamped to `range` around the
/// server's own position row (wrap-aware; PlayerPosition + PlayerChunk written exactly
/// like report_movement). charge_scaled = hold-to-charge (begin/release_ability_charge):
/// the release teleports at range × the charge-range multiplier
/// (CHARGE_MIN_RANGE_FRACTION + (1 − it) × charge fraction).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct TeleportParams { pub range: f32, pub charge_scaled: bool }

/// Spell shape (P5). Circle is centered on the range-clamped cursor point; Line and Cone
/// run from the player toward the cursor (the cursor supplies direction only, with the
/// bullet-control arms' fixed fallback when it sits on the player). Cone's spread is in
/// radians (full angle).
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct CircleParams { pub radius: f32 }
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct LineParams { pub length: f32, pub width: f32 }
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct ConeParams { pub length: f32, pub spread: f32 }

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum SpellShape {
    Circle(CircleParams),
    Line(LineParams),
    Cone(ConeParams),
}

/// The allocatable stat a spell's damage scales off: damage × (1 + stat × 0.002), mirroring
/// the strength formula in compute_player_damage. Wizard spells scale off Wisdom,
/// bullet-master spells off Dexterity.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum ScalingStat { Strength, Wisdom, Dexterity }

/// Spell (P5): shape damage at/toward the range-clamped cursor — damage × (1 + scaling
/// stat × 0.002) to every enemy in the shape, applied as `hits` SEPARATE mitigated hits of
/// damage/hits each (per-pellet mitigation: flat defence eats every pellet, so a volley
/// trades off against a single shot). charge_scaled = hold-to-charge (P4 path); the release
/// multiplies damage by the charge-range multiplier. `visual` picks the AbilityVisualEvent
/// kind relayed to clients (Meteor/Converge are visual-only variants of the Circle shape —
/// the damage is the plain circle either way). Damage and the Circle radius scale with the
/// position multiplier.
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct SpellParams { pub shape: SpellShape, pub damage: f32, pub hits: u32, pub charge_scaled: bool, pub scaling: ScalingStat, pub visual: AbilityVisualKind }

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum AbilityEffect {
    Heal,
    Buff(ConsumableBuffEffect),
    DeleteBullets(f32),
    SplitBullets(RectParams),
    AttractBullets(f32),
    DeleteBulletsInRect(RectParams),
    // Status effects (status/ module). All status rows live ability.duration seconds.
    // The f32 payloads are the ally radius (0 = self only).
    MarkBleed(MarkBleedParams),
    MarkStore(MarkStoreParams),
    Invulnerable(f32),
    DamageShield(f32),
    ReduceDamage(ReduceParams),
    RedirectToCaster(f32),
    RegenAura(RegenParams),
    StoreIncoming(StoreIncomingParams),
    // P2 — enemy statuses + stagger + the damage-aware bullet controllers.
    Curse(CurseParams),
    Bash(BashParams),
    Slash(SlashParams),
    Wall(WallParams),
    // P3 — ground zones (status/ module's GroundZone).
    CastZone(ZoneParams),
    // P4 — movement abilities. Teleport moves the caster server-side (position + chunk
    // rows written like report_movement); Grapple is client-tweened — the server arm
    // only runs the shared validation/cooldown, the movement reports carry the position.
    Teleport(TeleportParams),
    Grapple(f32), // range
    // P5 — shape damage spells; the cast is relayed to clients as an AbilityVisualEvent.
    Spell(SpellParams),
}

// Abilities are not consumed on use — they go on cooldown and optionally spend charges.
// max_charges of 0 means unlimited. duration only matters for Buff effects (0 for
// instant effects like Heal). potency scales with the span head's position multiplier.
#[derive(SpacetimeType, Clone, PartialEq)]
pub struct AbilityBehavior { pub effect: AbilityEffect, pub potency: f32, pub duration: f32, pub cooldown_seconds: f32, pub max_charges: u32 }

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ItemBehavior { Weapon(WeaponBehavior), Consumable(ConsumableBehavior), Ability(AbilityBehavior) }

// Non-stat enchantment effects (Game Design Doc 02 damage buffs + Doc 03 examples:
// "a ring could double the player's number of bullets", "armor buffs you when you use
// an ability", weapon shot-pattern toggles). Shot/damage modifiers compose onto the
// equipped weapon in resolve_effective_weapon; mults are additive, per project rule.
#[derive(SpacetimeType, Clone, PartialEq)]
pub struct AbilityUseBuffParams { pub buff: ConsumableBuffEffect, pub duration: f32 }

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum EnchantmentBehavior {
    AddShots(i32),              // flat bonus bullets per trigger pull
    ShotCountMult(f32),         // 1.0 = double bullets; stacked mults add before applying
    FlatBulletDamageMod(f32),   // flat per-bullet damage modifier (negative when trading damage for bullets)
    TrueDamageFlat(f32),        // added after enemy defence subtraction
    TrueDamagePercent(f32),     // fraction of raw per-bullet damage, added after defence
    OnAbilityUseBuff(AbilityUseBuffParams), // fires whenever the wearer activates an ability
    WeaponToggle(WeaponBehavior),           // grants the equipped weapon an extra toggle pattern
}

#[table(accessor = item, public)]
pub struct Item {
    #[primary_key]
    pub item_id: String,
    pub display_name: String,
    pub description: String,
    pub texture_id: String,
    pub equip_slot: EquipSlot,
    // Number of contiguous slots this item occupies. 1 for everything except multi-slot
    // ability items (shield 6, tome 3, spell 2 — see Game Design Doc 03).
    pub slot_cost: u32,
    pub stat_modifiers: Vec<StatModifier>,
    pub behaviors: Vec<ItemBehavior>,
    pub max_enchantments: u32,
    // Enchantments born on the item: always applied while equipped, don't count against
    // max_enchantments, can't be removed (they live here, not on the slot row).
    pub innate_enchantment_ids: Vec<String>,
    pub stat_requirements: StatRequirements,
}

#[table(accessor = enchantment, public)]
pub struct Enchantment {
    #[primary_key]
    pub enchantment_id: String,
    pub display_name: String,
    pub description: String,
    pub texture_id: String,
    pub stat_modifiers: Vec<StatModifier>,
    pub behaviors: Vec<EnchantmentBehavior>,
    pub allowed_slots: Vec<EquipSlot>,
}

/// Static-data staging (Phase 0B — BitCraft `stage_*/validate_staged` pattern).
/// Same fields as `Item` plus `staged_by`; live rows are untouched until an admin
/// validates + imports. Deterministic, admin-only, `Err` not panic.
#[table(accessor = staged_item)]
pub struct StagedItem {
    #[primary_key]
    pub item_id: String,
    pub display_name: String,
    pub description: String,
    pub texture_id: String,
    pub equip_slot: EquipSlot,
    pub slot_cost: u32,
    pub stat_modifiers: Vec<StatModifier>,
    pub behaviors: Vec<ItemBehavior>,
    pub max_enchantments: u32,
    pub innate_enchantment_ids: Vec<String>,
    pub stat_requirements: StatRequirements,
    pub staged_by: String,
}

/// Per-ability knowledge gate (Phase 1 — BitCraft `required_knowledges` pattern).
/// Separate side table so `AbilityBehavior` seeds stay untouched (default empty =
/// backward compatible: no row means no gate). `blocking_knowledges` equivalent =
/// "already know everything" early-out in the check.
#[table(accessor = ability_knowledge_requirement)]
pub struct AbilityKnowledgeRequirement {
    #[primary_key]
    pub item_id: String,
    pub required_ids: Vec<String>,
}

/// Staged counterpart of `Enchantment` (same fields + `staged_by`).
#[table(accessor = staged_enchantment)]
pub struct StagedEnchantment {
    #[primary_key]
    pub enchantment_id: String,
    pub display_name: String,
    pub description: String,
    pub texture_id: String,
    pub stat_modifiers: Vec<StatModifier>,
    pub behaviors: Vec<EnchantmentBehavior>,
    pub allowed_slots: Vec<EquipSlot>,
    pub staged_by: String,
}
