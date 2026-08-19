use spacetimedb::{ReducerContext, Table};
use crate::item::tables::*;
use crate::main::seeds::Seed;

// Every existing item has all-zero requirements (doc 03 class thresholds) — the gate
// exists and is enforced at equip time, but nothing is gated yet.
const NO_REQUIREMENTS: StatRequirements = StatRequirements {
    strength: 0, wisdom: 0, dexterity: 0, damage_dealer: 0, supporter: 0, artisan: 0,
};

pub fn seed_world_items(ctx: &ReducerContext) {
    Item::seed(ctx, Item {
        item_id: "Helmet".to_string(),
        display_name: "Helmet".to_string(),
        description: "A sturdy iron helmet. Increases defense and max HP.".to_string(),
        texture_id: "Helmet".to_string(),
        equip_slot: EquipSlot::Armor,
        slot_cost: 1,
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Defense, amount: 5.0, mode: StatMode::Flat },
            StatModifier { stat: StatKind::Supporter, amount: 3.0, mode: StatMode::Flat },
            StatModifier { stat: StatKind::Hp, amount: 15.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
        max_enchantments: 2,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Hat".to_string(),
        display_name: "Hat".to_string(),
        description: "A traveler's hat. Slightly boosts wisdom, dexterity, and artisan.".to_string(),
        texture_id: "Hat".to_string(),
        equip_slot: EquipSlot::Accessory,
        slot_cost: 1,
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Wisdom, amount: 2.0, mode: StatMode::Flat },
            StatModifier { stat: StatKind::Dexterity, amount: 2.0, mode: StatMode::Flat },
            StatModifier { stat: StatKind::Artisan, amount: 2.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
        max_enchantments: 1,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Skull".to_string(),
        display_name: "Skull".to_string(),
        description: "An ancient skull radiating dark energy. Adds raw strength.".to_string(),
        texture_id: "Skull".to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 1,
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Strength, amount: 5.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
        max_enchantments: 3,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Bag".to_string(),
        display_name: "Bag".to_string(),
        description: "Expands your carrying capacity.".to_string(),
        texture_id: "Bag".to_string(),
        equip_slot: EquipSlot::Bag,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Bread".to_string(),
        display_name: "Bread".to_string(),
        description: "Freshly baked bread. Restores a small amount of HP.".to_string(),
        texture_id: "Bread".to_string(),
        equip_slot: EquipSlot::Consumable,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Consumable(ConsumableBehavior {
            effect: ConsumableEffect::Heal,
            potency: 25.0,
            duration: 0.0,
        })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Bow".to_string(),
        display_name: "Bow".to_string(),
        description: "A light recurve bow. Toggle between a three-arrow spread and a single heavy shot.".to_string(),
        texture_id: "Bow".to_string(),
        equip_slot: EquipSlot::Weapon,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![
            // Toggle option 0: 3-shot spread — 10/bullet raw. Option 1: single heavy —
            // 30/bullet raw. Same per-pull damage; the spread loses more to enemy defence.
            ItemBehavior::Weapon(WeaponBehavior {
                damage: 30,
                range: 150.0,
                fire_rate: 4.0,
                projectile_speed: 300.0,
                shot_count: 3,
                zone_count: 25,
                pierce: false,
                projectile_texture_id: "Arrow".to_string(),
                pattern: WeaponPattern::Triple,
                spread_angle: 0.35,
            }),
            ItemBehavior::Weapon(WeaponBehavior {
                damage: 30,
                range: 200.0,
                fire_rate: 2.0,
                projectile_speed: 400.0,
                shot_count: 1,
                zone_count: 25,
                pierce: true,
                projectile_texture_id: "Arrow".to_string(),
                pattern: WeaponPattern::Single,
                spread_angle: 0.0,
            }),
        ],
        max_enchantments: 2,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Fan Bow".to_string(),
        display_name: "Fan Bow".to_string(),
        description: "A bow born with a point-blank fan technique. Toggle to fire a 10-arrow circle around you.".to_string(),
        texture_id: "Bow".to_string(),
        equip_slot: EquipSlot::Weapon,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Weapon(WeaponBehavior {
            damage: 40,
            range: 160.0,
            fire_rate: 3.0,
            projectile_speed: 320.0,
            shot_count: 1,
            zone_count: 25,
            pierce: false,
            projectile_texture_id: "Arrow".to_string(),
            pattern: WeaponPattern::Single,
            spread_angle: 0.0,
        })],
        max_enchantments: 1,
        // The Extreme-Prejudice-style toggle as an INNATE enchantment (doc 03) — always
        // granted while equipped, can't be removed, doesn't eat the socket.
        innate_enchantment_ids: vec!["point_blank".to_string()],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Tome of Mending".to_string(),
        display_name: "Tome of Mending".to_string(),
        description: "A healer's tome. Occupies 3 ability slots. Activate to restore HP.".to_string(),
        texture_id: "Tome".to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 3,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Ability(AbilityBehavior {
            effect: AbilityEffect::Heal,
            potency: 40.0,
            duration: 0.0,
            cooldown_seconds: 8.0,
            max_charges: 0, // unlimited
        })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Ember Scripture".to_string(),
        display_name: "Ember Scripture".to_string(),
        description: "A smoldering scripture. Occupies 2 ability slots. Activate for a temporary strength blessing. Limited charges.".to_string(),
        texture_id: "Scripture".to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 2,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Ability(AbilityBehavior {
            effect: AbilityEffect::Buff(ConsumableBuffEffect::Strength(5.0)),
            potency: 1.0,
            duration: 10.0,
            cooldown_seconds: 15.0,
            max_charges: 3,
        })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Null Sigil".to_string(),
        display_name: "Null Sigil".to_string(),
        description: "A ward against bullet-hell. Activate to erase enemy bullets near the cursor.".to_string(),
        texture_id: "Skull".to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Ability(AbilityBehavior {
            effect: AbilityEffect::DeleteBullets(120.0),
            potency: 1.0,
            duration: 0.0,
            cooldown_seconds: 5.0,
            max_charges: 0, // unlimited
        })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Slash Orb".to_string(),
        display_name: "Slash Orb".to_string(),
        description: "A void-dark orb. Activate to slash the space in front of you, erasing any enemy bullets caught in the arc.".to_string(),
        texture_id: "Orb".to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Ability(AbilityBehavior {
            effect: AbilityEffect::SlashBullets(SlashParams { length: 100.0, width: 60.0 }),
            potency: 1.0,
            duration: 0.0,
            cooldown_seconds: 1.5,
            max_charges: 0, // unlimited
        })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });
}

pub fn seed_world_enchantments(ctx: &ReducerContext) {
    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "bear".to_string(),
        display_name: "Bear".to_string(),
        description: "Grants the strength of a bear.".to_string(),
        texture_id: "bear".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Strength, amount: 3.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
        allowed_slots: vec![EquipSlot::Armor, EquipSlot::Ability],
    });

    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "swift".to_string(),
        display_name: "Swift".to_string(),
        description: "Lightens the wearer's step.".to_string(),
        texture_id: "swift".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Dexterity, amount: 2.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
        allowed_slots: vec![EquipSlot::Weapon, EquipSlot::Ability, EquipSlot::Armor, EquipSlot::Accessory, EquipSlot::Bag, EquipSlot::Consumable],
    });

    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "eagle_eye".to_string(),
        display_name: "Eagle Eye".to_string(),
        description: "Sharpens aim and reflexes.".to_string(),
        texture_id: "eagle_eye".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::Dexterity, amount: 3.0, mode: StatMode::Flat },
        ],
        behaviors: vec![],
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
        behaviors: vec![],
        allowed_slots: vec![EquipSlot::Armor],
    });

    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "vampiric".to_string(),
        display_name: "Vampiric".to_string(),
        description: "Drains a sliver of life from the world around you.".to_string(),
        texture_id: "vampiric".to_string(),
        stat_modifiers: vec![
            StatModifier { stat: StatKind::DamageDealer, amount: 0.05, mode: StatMode::Mult },
        ],
        behaviors: vec![],
        allowed_slots: vec![EquipSlot::Ability],
    });

    // The doc-02 tradeoff as an enchantment: double the bullets, -3 damage per bullet.
    // Favors few-strong-bullet weapons; armour shreds the doubled weak bullets.
    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "splitting".to_string(),
        display_name: "Splitting".to_string(),
        description: "Doubles your weapon's bullets, but each bullet deals 3 less damage.".to_string(),
        texture_id: "splitting".to_string(),
        stat_modifiers: vec![],
        behaviors: vec![
            EnchantmentBehavior::ShotCountMult(1.0),
            EnchantmentBehavior::FlatBulletDamageMod(-3.0),
        ],
        allowed_slots: vec![EquipSlot::Accessory],
    });

    // Doc 03's armor example: using an ability buffs you.
    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "echoing".to_string(),
        display_name: "Echoing".to_string(),
        description: "Activating an ability sharpens your reflexes for a short time.".to_string(),
        texture_id: "echoing".to_string(),
        stat_modifiers: vec![],
        behaviors: vec![
            EnchantmentBehavior::OnAbilityUseBuff(AbilityUseBuffParams {
                buff: ConsumableBuffEffect::Dexterity(3.0),
                duration: 5.0,
            }),
        ],
        allowed_slots: vec![EquipSlot::Armor],
    });

    // The Extreme-Prejudice-style toggle: a 10-bullet point-blank circle. Granted innately
    // by the Fan Bow; socketable nowhere — being born with it is what makes it valuable.
    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "point_blank".to_string(),
        display_name: "Point Blank".to_string(),
        description: "Toggle: fire a 10-bullet circle around you. Devastating up close, useless at range.".to_string(),
        texture_id: "point_blank".to_string(),
        stat_modifiers: vec![],
        behaviors: vec![
            EnchantmentBehavior::WeaponToggle(WeaponBehavior {
                damage: 100,
                range: 60.0,
                fire_rate: 1.5,
                projectile_speed: 250.0,
                shot_count: 10,
                zone_count: 12,
                pierce: false,
                projectile_texture_id: "Arrow".to_string(),
                pattern: WeaponPattern::Triple, // even fan: step = spread/(n-1) = 36° at n=10
                spread_angle: std::f32::consts::TAU * 0.9,
            }),
        ],
        allowed_slots: vec![],
    });
}

impl Seed for Item {
    fn seed(ctx: &ReducerContext, data: Item) {
        if ctx.db.item().item_id().find(&data.item_id).is_some() {
            ctx.db.item().item_id().update(data);
        } else {
            ctx.db.item().insert(data);
        }
    }
}
