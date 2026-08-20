use spacetimedb::{table, SpacetimeType};

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

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum AbilityEffect {
    Heal,
    Buff(ConsumableBuffEffect),
    DeleteBullets(f32),
    SplitBullets(RectParams),
    AttractBullets(f32),
    DeleteBulletsInRect(RectParams),
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
