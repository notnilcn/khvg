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
    Artifact,
    Armor,
    Accessory,
    Bag,
    Consumable,
}

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum StatKind { Strength, Wisdom, Dexterity, Defense, Vitality, Speed, Hp }

#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub enum StatMode { Flat, Mult }

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct StatModifier { pub stat: StatKind, pub amount: f32, pub mode: StatMode }

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct WeaponBehavior {
    pub damage: u32, pub range: f32, pub fire_rate: f32, pub projectile_speed: f32,
    pub shot_count: u32, pub zone_count: u32, pub pierce: bool,
    pub projectile_texture_id: String, pub pattern: WeaponPattern, pub spread_angle: f32,
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ConsumableBuffEffect {
    Strength(f32),
    Wisdom(f32),
    Dexterity(f32),
    Defense(f32),
    Vitality(f32),
    Speed(f32),
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ConsumableEffect {
    Heal,
    Buff(ConsumableBuffEffect),
}

#[derive(SpacetimeType, Clone, PartialEq)]
pub struct ConsumableBehavior { pub effect: ConsumableEffect, pub potency: f32, pub duration: f32 }

#[derive(SpacetimeType, Clone, PartialEq)]
pub enum ItemBehavior { Weapon(WeaponBehavior), Consumable(ConsumableBehavior) }

#[table(accessor = item, public)]
pub struct Item {
    #[primary_key]
    pub item_id: String,
    pub display_name: String,
    pub description: String,
    pub texture_id: String,
    pub equip_slot: EquipSlot,
    pub stat_modifiers: Vec<StatModifier>,
    pub behaviors: Vec<ItemBehavior>,
    pub max_enchantments: u32,
}

#[table(accessor = enchantment, public)]
pub struct Enchantment {
    #[primary_key]
    pub enchantment_id: String,
    pub display_name: String,
    pub description: String,
    pub texture_id: String,
    pub stat_modifiers: Vec<StatModifier>,
    pub allowed_slots: Vec<EquipSlot>,
}
