use spacetimedb::{ReducerContext, Table};
use crate::item::tables::*;
use crate::main::seeds::Seed;

pub fn seed_world_items(ctx: &ReducerContext) 
{
    Item::seed(ctx, Item {
        item_id: "Helmet".to_string(),
        display_name: "Helmet".to_string(),
        description: "A sturdy iron helmet. Increases defense and vitality.".to_string(),
        texture_id: "Helmet".to_string(),
        equip_slot: EquipSlot::Armor,
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Defense, amount: 5.0, mode: StatMode::Flat },
            StatModifier { stat: StatKind::Vitality, amount: 3.0, mode: StatMode::Flat },
            StatModifier { stat: StatKind::Hp, amount: 15.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
        max_enchantments: 2,
    });

    Item::seed(ctx, Item {
        item_id: "Hat".to_string(),
        display_name: "Hat".to_string(),
        description: "A traveler's hat. Slightly boosts wisdom, dexterity, and speed.".to_string(),
        texture_id: "Hat".to_string(),
        equip_slot: EquipSlot::Accessory,
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Wisdom, amount: 2.0, mode: StatMode::Flat },
            StatModifier { stat: StatKind::Dexterity, amount: 2.0, mode: StatMode::Flat },
            StatModifier { stat: StatKind::Speed, amount: 2.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
        max_enchantments: 1,
    });

    Item::seed(ctx, Item {
        item_id: "Skull".to_string(),
        display_name: "Skull".to_string(),
        description: "An ancient skull radiating dark energy. Adds raw strength.".to_string(),
        texture_id: "Skull".to_string(),
        equip_slot: EquipSlot::Artifact,
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Strength, amount: 5.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
        max_enchantments: 3,
    });

    Item::seed(ctx, Item {
        item_id: "Bag".to_string(),
        display_name: "Bag".to_string(),
        description: "Expands your carrying capacity.".to_string(),
        texture_id: "Bag".to_string(),
        equip_slot: EquipSlot::Bag,
        stat_modifiers: vec![],
        behaviors: vec![],
        max_enchantments: 0,
    });

    Item::seed(ctx, Item {
        item_id: "Bread".to_string(),
        display_name: "Bread".to_string(),
        description: "Freshly baked bread. Restores a small amount of HP.".to_string(),
        texture_id: "Bread".to_string(),
        equip_slot: EquipSlot::Consumable,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Consumable(ConsumableBehavior {
            effect: ConsumableEffect::Heal,
            potency: 25.0,
            duration: 0.0,
        })],
        max_enchantments: 0,
    });

    Item::seed(ctx, Item {
        item_id: "Bow".to_string(),
        display_name: "Bow".to_string(),
        description: "A light recurve bow. Fires three arrows in a spread.".to_string(),
        texture_id: "Bow".to_string(),
        equip_slot: EquipSlot::Weapon,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Weapon(WeaponBehavior {
            damage: 10,
            range: 150.0,
            fire_rate: 4.0,
            projectile_speed: 300.0,
            shot_count: 3,
            zone_count: 25,
            pierce: false,
            projectile_texture_id: "Arrow".to_string(),
            pattern: WeaponPattern::Triple,
            spread_angle: 0.35,
        })],
        max_enchantments: 2,
    });
}

pub fn seed_world_enchantments(ctx: &ReducerContext) 
{
    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "bear".to_string(),
        display_name: "Bear".to_string(),
        description: "Grants the strength of a bear.".to_string(),
        texture_id: "bear".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Strength, amount: 3.0, mode: StatMode::Flat },
        ],
        allowed_slots: vec![EquipSlot::Armor, EquipSlot::Artifact],
    });

    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "swift".to_string(),
        display_name: "Swift".to_string(),
        description: "Lightens the wearer's step.".to_string(),
        texture_id: "swift".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Speed, amount: 2.0, mode: StatMode::Flat },
        ],
        allowed_slots: vec![EquipSlot::Weapon, EquipSlot::Artifact, EquipSlot::Armor, EquipSlot::Accessory, EquipSlot::Bag, EquipSlot::Consumable],
    });

    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "eagle_eye".to_string(),
        display_name: "Eagle Eye".to_string(),
        description: "Sharpens aim and reflexes.".to_string(),
        texture_id: "eagle_eye".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Dexterity, amount: 3.0, mode: StatMode::Flat },
        ],
        allowed_slots: vec![EquipSlot::Weapon, EquipSlot::Accessory],
    });

    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "iron_skin".to_string(),
        display_name: "Iron Skin".to_string(),
        description: "Hardens the body like iron.".to_string(),
        texture_id: "iron_skin".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Defense, amount: 2.0, mode: StatMode::Flat },
        ],
        allowed_slots: vec![EquipSlot::Armor],
    });

    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "vampiric".to_string(),
        display_name: "Vampiric".to_string(),
        description: "Drains a sliver of life from the world around you.".to_string(),
        texture_id: "vampiric".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Vitality, amount: 0.05, mode: StatMode::Mult },
        ],
        allowed_slots: vec![EquipSlot::Artifact],
    });
}

impl Seed for Item {
    fn seed(ctx: &ReducerContext, data: Item) 
{
        if ctx.db.item().item_id().find(&data.item_id).is_some() {
            ctx.db.item().item_id().update(data);
        } else {
            ctx.db.item().insert(data);
        }
    }
}
