use spacetimedb::{ReducerContext, Table};
use crate::item::tables::*;
use crate::main::seeds::Seed;
use crate::status::tables::{PurgeKind, WagerKind, ReleaseKind, EnemyStatusKind, ZoneKind};
use crate::enemy::instance_tables::AbilityVisualKind;

// Generic gear keeps all-zero requirements — the gate exists and is enforced at equip
// time, but only the P1b–P5 ability items are class-locked (P6, below).
const NO_REQUIREMENTS: StatRequirements = StatRequirements {
    strength: 0, wisdom: 0, dexterity: 0, damage_dealer: 0, supporter: 0, artisan: 0,
};

// ===== P6 class gates =====
// The doc's class headers (docs/Game Design Docs/Items/Abilities/Abilities.md) gate on
// "> 0" of a stat, but BASE_STAT is 10 for everyone, so a literal "> 0" gate would be
// meaningless. These are modest above-base thresholds — primary stat >= 15, secondary
// >= 12, i.e. a few allocated points — pending tuning. "dps"/"sup" in the doc map to
// the damage_dealer/supporter temperaments. Two documented deviations:
// - Paladin has NO header in the doc — it mirrors the witch doctor (wis/sup) as the
//   magic support class. Open design question.
// - Trickster's header is an ORDERING ((wis > dex > 0) & (sup > 0)) that flat
//   StatRequirements can't express — approximated as wis >= 15, dex >= 12, sup >= 12.
// The curse sigils (Venom/Frost/Frailty) seed as trickster (the doc's "poisons,
// debuffs"), not bullet master.
const REQ_BULLET_MASTER: StatRequirements = StatRequirements { dexterity: 15, damage_dealer: 12, ..NO_REQUIREMENTS };
const REQ_WARRIOR: StatRequirements = StatRequirements { strength: 15, damage_dealer: 12, ..NO_REQUIREMENTS };
const REQ_WIZARD: StatRequirements = StatRequirements { wisdom: 15, damage_dealer: 12, ..NO_REQUIREMENTS };
const REQ_WITCH_DOCTOR: StatRequirements = StatRequirements { wisdom: 15, supporter: 12, ..NO_REQUIREMENTS };
const REQ_PALADIN: StatRequirements = REQ_WITCH_DOCTOR;
const REQ_TANK: StatRequirements = StatRequirements { strength: 15, supporter: 12, ..NO_REQUIREMENTS };
const REQ_TRICKSTER: StatRequirements = StatRequirements { wisdom: 15, dexterity: 12, supporter: 12, ..NO_REQUIREMENTS };

/// Single-behavior, 1-slot, unlimited-charge ability item — the shape every status-effect
/// ability seed shares. `stat_requirements` is the item's class gate (P6, above).
fn ability_item(item_id: &str, description: &str, texture_id: &str, effect: AbilityEffect, duration: f32, cooldown_seconds: f32, stat_requirements: StatRequirements) -> Item {
    Item {
        item_id: item_id.to_string(),
        display_name: item_id.to_string(),
        description: description.to_string(),
        texture_id: texture_id.to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Ability(AbilityBehavior { effect, potency: 1.0, duration, cooldown_seconds, max_charges: 0 })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements,
    }
}

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

    // ===== P6: bullet-master toggle weapons (class-gated) =====

    // The Bow pattern taken further: three Weapon behaviors = three toggle options (T to
    // cycle), covering the bullet-master piercing/shotgun/quick-shot fantasy.
    Item::seed(ctx, Item {
        item_id: "Marksman Bow".to_string(),
        display_name: "Marksman Bow".to_string(),
        description: "A master archer's bow. Toggle between a piercing longshot, a point-blank spread, and a quick light shot.".to_string(),
        texture_id: "Bow".to_string(),
        equip_slot: EquipSlot::Weapon,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![
            // Toggle option 0: piercing longshot — 45/bullet raw, punches through ranks.
            ItemBehavior::Weapon(WeaponBehavior {
                damage: 45,
                range: 260.0,
                fire_rate: 1.5,
                projectile_speed: 450.0,
                shot_count: 1,
                zone_count: 25,
                pierce: true,
                projectile_texture_id: "Arrow".to_string(),
                pattern: WeaponPattern::Single,
                spread_angle: 0.0,
            }),
            // Toggle option 1: point-blank spread — 12/bullet raw; armour eats the weak
            // pellets, so it shines against low-defence enemies.
            ItemBehavior::Weapon(WeaponBehavior {
                damage: 36,
                range: 130.0,
                fire_rate: 2.0,
                projectile_speed: 320.0,
                shot_count: 3,
                zone_count: 25,
                pierce: false,
                projectile_texture_id: "Arrow".to_string(),
                pattern: WeaponPattern::Triple,
                spread_angle: 0.5,
            }),
            // Toggle option 2: quick light shot — 20/bullet raw at a fast cadence.
            ItemBehavior::Weapon(WeaponBehavior {
                damage: 20,
                range: 180.0,
                fire_rate: 5.0,
                projectile_speed: 380.0,
                shot_count: 1,
                zone_count: 25,
                pierce: false,
                projectile_texture_id: "Arrow".to_string(),
                pattern: WeaponPattern::Single,
                spread_angle: 0.0,
            }),
        ],
        max_enchantments: 2,
        innate_enchantment_ids: vec![],
        stat_requirements: REQ_BULLET_MASTER,
    });

    // The Fan Bow pattern: a single heavy behavior plus the shotgun half granted as an
    // innate WeaponToggle enchantment (scatter_fan, seeded below).
    Item::seed(ctx, Item {
        item_id: "Warbow".to_string(),
        display_name: "Warbow".to_string(),
        description: "A heavy warbow born with a scatter technique. Toggle to loose a five-arrow fan.".to_string(),
        texture_id: "Bow".to_string(),
        equip_slot: EquipSlot::Weapon,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Weapon(WeaponBehavior {
            damage: 60,
            range: 220.0,
            fire_rate: 1.2,
            projectile_speed: 420.0,
            shot_count: 1,
            zone_count: 25,
            pierce: false,
            projectile_texture_id: "Arrow".to_string(),
            pattern: WeaponPattern::Single,
            spread_angle: 0.0,
        })],
        max_enchantments: 1,
        innate_enchantment_ids: vec!["scatter_fan".to_string()],
        stat_requirements: REQ_BULLET_MASTER,
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
        item_id: "Delete Orb".to_string(),
        display_name: "Delete Orb".to_string(),
        description: "A void-dark orb. Activate to summon a black rectangle in front of you, erasing any enemy bullets caught inside it.".to_string(),
        texture_id: "Orb".to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Ability(AbilityBehavior {
            effect: AbilityEffect::DeleteBulletsInRect(RectParams { length: 100.0, width: 60.0 }),
            potency: 1.0,
            duration: 0.0,
            cooldown_seconds: 1.5,
            max_charges: 0, // unlimited
        })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Attract Orb".to_string(),
        display_name: "Attract Orb".to_string(),
        description: "A void-dark orb. Activate to summon a black hole at the cursor that lingers for 3 seconds, sucking in enemy bullets.".to_string(),
        texture_id: "Orb".to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Ability(AbilityBehavior {
            effect: AbilityEffect::AttractBullets(120.0),
            potency: 1.0,
            duration: 3.0,
            cooldown_seconds: 5.0,
            max_charges: 0, // unlimited
        })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    Item::seed(ctx, Item {
        item_id: "Split Orb".to_string(),
        display_name: "Split Orb".to_string(),
        description: "A void-dark orb. Activate to summon a black rectangle in front of you, splitting any enemy bullets caught inside it in two.".to_string(),
        texture_id: "Orb".to_string(),
        equip_slot: EquipSlot::Ability,
        slot_cost: 1,
        stat_modifiers: vec![],
        behaviors: vec![ItemBehavior::Ability(AbilityBehavior {
            effect: AbilityEffect::SplitBullets(RectParams { length: 100.0, width: 60.0 }),
            potency: 1.0,
            duration: 0.0,
            cooldown_seconds: 1.5,
            max_charges: 0, // unlimited
        })],
        max_enchantments: 0,
        innate_enchantment_ids: vec![],
        stat_requirements: NO_REQUIREMENTS,
    });

    // ===== Status-effect abilities (status/ module) =====
    // Bleed marks: 70% of each hit FROM the marked enemy becomes a pool that drains over
    // ~5s; 30% lands instantly. One item per purge condition. One active mark per player.

    // Warrior — self bleed, four purge conditions.
    Item::seed(ctx, ability_item("Sanguine Brand",
        "Mark an enemy: its hits on you bleed (70% drains over ~5s). Sheds after 2s without taking a hit.",
        "Skull", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::NoDamageTaken, conversion: 0.7, ally_radius: 0.0, pool_seconds: 5.0, outgoing: false }), 10.0, 15.0, REQ_WARRIOR));
    Item::seed(ctx, ability_item("Blood Oath",
        "Mark an enemy: its hits on you bleed. Sheds after staying within 200 of the mark for 5s.",
        "Skull", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::StayClose, conversion: 0.7, ally_radius: 0.0, pool_seconds: 5.0, outgoing: false }), 15.0, 15.0, REQ_WARRIOR));
    Item::seed(ctx, ability_item("Crimson Tithe",
        "Mark an enemy: its hits on you bleed. Sheds once you damage the mark.",
        "Skull", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::DealDamage, conversion: 0.7, ally_radius: 0.0, pool_seconds: 5.0, outgoing: false }), 15.0, 15.0, REQ_WARRIOR));
    Item::seed(ctx, ability_item("Deathmark",
        "Mark an enemy: its hits on you bleed. Sheds when the mark dies.",
        "Skull", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::OnKill, conversion: 0.7, ally_radius: 0.0, pool_seconds: 5.0, outgoing: false }), 20.0, 15.0, REQ_WARRIOR));

    // Paladin — the same four bleed marks as an AoE over nearby allies (identical call,
    // different targeting).
    Item::seed(ctx, ability_item("Consecrated Brand",
        "Mark an enemy: its hits on you and nearby allies bleed. Sheds after 2s without taking a hit.",
        "Tome", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::NoDamageTaken, conversion: 0.7, ally_radius: 150.0, pool_seconds: 5.0, outgoing: false }), 10.0, 20.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Martyr's Oath",
        "Mark an enemy: its hits on you and nearby allies bleed. Sheds after staying near the mark for 5s.",
        "Tome", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::StayClose, conversion: 0.7, ally_radius: 150.0, pool_seconds: 5.0, outgoing: false }), 15.0, 20.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Shared Tithe",
        "Mark an enemy: its hits on you and nearby allies bleed. Sheds once you damage the mark.",
        "Tome", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::DealDamage, conversion: 0.7, ally_radius: 150.0, pool_seconds: 5.0, outgoing: false }), 15.0, 20.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Last Rites",
        "Mark an enemy: its hits on you and nearby allies bleed. Sheds when the mark dies.",
        "Tome", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::OnKill, conversion: 0.7, ally_radius: 150.0, pool_seconds: 5.0, outgoing: false }), 20.0, 20.0, REQ_PALADIN));

    // Self/ally buffs.
    Item::seed(ctx, ability_item("Aegis of Faith",
        "Become invulnerable for 3 seconds.", "Scripture",
        AbilityEffect::Invulnerable(0.0), 3.0, 20.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Bulwark",
        "Become invulnerable for 2 seconds.", "Scripture",
        AbilityEffect::Invulnerable(0.0), 2.0, 25.0, REQ_TANK));
    Item::seed(ctx, ability_item("Smoke Veil",
        "You and nearby allies become invulnerable for 2 seconds.", "Orb",
        AbilityEffect::Invulnerable(150.0), 2.0, 30.0, REQ_TRICKSTER));
    Item::seed(ctx, ability_item("Sacred Ward",
        "A shield that fully absorbs your next hit within 10 seconds.", "Scripture",
        AbilityEffect::DamageShield(0.0), 10.0, 15.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Iron Stance",
        "Take 50% reduced damage for 8 seconds.", "Scripture",
        AbilityEffect::ReduceDamage(ReduceParams { pct: 0.5, ally_radius: 0.0 }), 8.0, 20.0, REQ_TANK));
    Item::seed(ctx, ability_item("Guardian's Pact",
        "For 8 seconds, hits on nearby allies are redirected to you.", "Tome",
        AbilityEffect::RedirectToCaster(150.0), 8.0, 25.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Prayer of Mending",
        "You and nearby allies regenerate 2 HP/s for 10 seconds.", "Tome",
        AbilityEffect::RegenAura(RegenParams { per_second: 2.0, ally_radius: 150.0 }), 10.0, 20.0, REQ_PALADIN));

    // Store-damage marks: your damage to the marked enemy is withheld during the window,
    // then released at 2x/1x/0x per the wager. Marked-enemy death forfeits the pool.
    // Warrior — self, three wagers.
    Item::seed(ctx, ability_item("Gambler's Mark",
        "Mark an enemy: your damage to it is stored for 8s, then released — doubled if you take no hits, lost at 5+.",
        "Skull", AbilityEffect::MarkStore(MarkStoreParams { wager: WagerKind::NoHits, x: 0.0, z: 0.0, r1: 0.0, r2: 0.0, max_hits: 4, ally_radius: 0.0 }), 8.0, 20.0, REQ_WARRIOR));
    Item::seed(ctx, ability_item("Duelist's Wager",
        "Mark an enemy: your damage to it is stored for 8s — doubled if you never stray 150 from it, lost if you ever pass 300.",
        "Skull", AbilityEffect::MarkStore(MarkStoreParams { wager: WagerKind::Radius, x: 0.0, z: 0.0, r1: 150.0, r2: 300.0, max_hits: 0, ally_radius: 0.0 }), 8.0, 20.0, REQ_WARRIOR));
    Item::seed(ctx, ability_item("Blood Price",
        "Mark an enemy: your damage to it is stored for 8s — doubled if the store reaches 500, lost below 200.",
        "Skull", AbilityEffect::MarkStore(MarkStoreParams { wager: WagerKind::Damage, x: 500.0, z: 200.0, r1: 0.0, r2: 0.0, max_hits: 0, ally_radius: 0.0 }), 8.0, 20.0, REQ_WARRIOR));
    // Paladin — the same three wagers as an AoE over nearby allies.
    Item::seed(ctx, ability_item("Martyr's Gamble",
        "Mark an enemy: your and nearby allies' damage to it is stored for 8s — doubled with no hits taken, lost at 5+.",
        "Tome", AbilityEffect::MarkStore(MarkStoreParams { wager: WagerKind::NoHits, x: 0.0, z: 0.0, r1: 0.0, r2: 0.0, max_hits: 4, ally_radius: 150.0 }), 8.0, 25.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Templar's Wager",
        "Mark an enemy: your and nearby allies' damage to it is stored for 8s — doubled if you never stray 150, lost past 300.",
        "Tome", AbilityEffect::MarkStore(MarkStoreParams { wager: WagerKind::Radius, x: 0.0, z: 0.0, r1: 150.0, r2: 300.0, max_hits: 0, ally_radius: 150.0 }), 8.0, 25.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Congregation's Price",
        "Mark an enemy: your and nearby allies' damage to it is stored for 8s — doubled at 500 stored, lost below 200.",
        "Tome", AbilityEffect::MarkStore(MarkStoreParams { wager: WagerKind::Damage, x: 500.0, z: 200.0, r1: 0.0, r2: 0.0, max_hits: 0, ally_radius: 150.0 }), 8.0, 25.0, REQ_PALADIN));

    // Store-incoming: damage you TAKE accumulates pre-mitigation, then releases.
    Item::seed(ctx, ability_item("Smite",
        "Store the damage you take for 8s, then unleash it around you. Recast to release early at the cursor.",
        "Scripture", AbilityEffect::StoreIncoming(StoreIncomingParams { release: ReleaseKind::Smite, release_radius: 150.0 }), 8.0, 25.0, REQ_PALADIN));
    Item::seed(ctx, ability_item("Retribution",
        "Store the damage you take for 8s; your next shield bash within 30s unleashes it.",
        "Scripture", AbilityEffect::StoreIncoming(StoreIncomingParams { release: ReleaseKind::Bash, release_radius: 0.0 }), 8.0, 25.0, REQ_TANK));

    // ===== P2: enemy statuses, stagger, damage-aware bullet controllers =====

    // Curses (trickster — the doc's "poisons, debuffs"): single-target enemy debuffs at
    // the cursor-picked enemy, duration = the ability's duration.
    Item::seed(ctx, ability_item("Venom Sigil",
        "Curse the enemy near the cursor: it bleeds for 8 damage/s over 6 seconds.",
        "Skull", AbilityEffect::Curse(CurseParams { kind: EnemyStatusKind::DoT, magnitude: 8.0 }), 6.0, 8.0, REQ_TRICKSTER));
    Item::seed(ctx, ability_item("Frost Sigil",
        "Curse the enemy near the cursor: slowed 40% for 4 seconds.",
        "Orb", AbilityEffect::Curse(CurseParams { kind: EnemyStatusKind::Slow, magnitude: 0.4 }), 4.0, 8.0, REQ_TRICKSTER));
    Item::seed(ctx, ability_item("Frailty Sigil",
        "Curse the enemy near the cursor: it takes 50% more damage for 6 seconds.",
        "Tome", AbilityEffect::Curse(CurseParams { kind: EnemyStatusKind::Vulnerability, magnitude: 0.5 }), 6.0, 12.0, REQ_TRICKSTER));

    // Bashes (tank): weapon damage to every enemy within 120 of you +34 stagger; at 100
    // stagger the enemy is stunned for 2s. Sundering Bash also breaks its armor.
    Item::seed(ctx, ability_item("Shield Bash",
        "Bash enemies within 120 for weapon damage and 34 stagger; a full stagger bar stuns for 2s. An armed Retribution store rides the hit.",
        "Scripture", AbilityEffect::Bash(BashParams { stagger: 34.0, armor_break: false, range: 120.0, stun_seconds: 2.0 }), 0.0, 6.0, REQ_TANK));
    Item::seed(ctx, ability_item("Sundering Bash",
        "Bash enemies within 120 for weapon damage and 34 stagger; a full stagger bar stuns for 2s and breaks their armor.",
        "Scripture", AbilityEffect::Bash(BashParams { stagger: 34.0, armor_break: true, range: 120.0, stun_seconds: 2.0 }), 0.0, 10.0, REQ_TANK));

    // Outgoing bleed (warrior): your hits on the mark become a bleed on the enemy instead
    // of landing instantly — and its hits on you still bleed too.
    Item::seed(ctx, ability_item("Bloodbath",
        "Mark an enemy for 20s: your hits on it become a bleed (a third of the hit per second for 3s), and its hits on you bleed. Sheds when the mark dies.",
        "Skull", AbilityEffect::MarkBleed(MarkBleedParams { purge: PurgeKind::OnKill, conversion: 0.7, ally_radius: 0.0, pool_seconds: 5.0, outgoing: true }), 20.0, 20.0, REQ_WARRIOR));

    // Damage-aware bullet controllers.
    Item::seed(ctx, ability_item("Quick Slash",
        "Slash in front of you, erasing enemy bullets that deal 15 damage or less in a 140×60 rectangle.",
        "Orb", AbilityEffect::Slash(SlashParams { length: 140.0, width: 60.0, max_bullet_damage: 15 }), 0.0, 4.0, REQ_BULLET_MASTER));
    Item::seed(ctx, ability_item("Shield Wall",
        "Raise a 200×80 wall in front of you for 4 seconds, erasing any enemy bullet that crosses it.",
        "Orb", AbilityEffect::Wall(WallParams { length: 200.0, width: 80.0 }), 4.0, 12.0, REQ_TANK));

    // ===== P3: ground zones (status/ module's GroundZone) =====
    // Witch doctor circles — one per ZoneKind. Ally zones re-apply their effect to whoever
    // stands inside (shed ~1.5s after leaving); enemy zones debuff enemies inside; bursts
    // fire once when the zone expires. Magnitude is unused for DebuffImmunity/HpFloor/
    // DamageShare (presence-based) — seeded as 1.0.

    // Ally wards.
    Item::seed(ctx, ability_item("Warding Circle",
        "Draw a circle at the cursor for 10s: allies inside are immune to enemy debuffs.",
        "Tome", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::DebuffImmunity, radius: 150.0, magnitude: 1.0 }), 10.0, 20.0, REQ_WITCH_DOCTOR));
    Item::seed(ctx, ability_item("Aegis Circle",
        "Draw a circle at the cursor for 10s: allies inside take 30% reduced damage.",
        "Scripture", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::DamageReduction, radius: 150.0, magnitude: 0.3 }), 10.0, 20.0, REQ_WITCH_DOCTOR));
    Item::seed(ctx, ability_item("Sanctuary Circle",
        "Draw a circle at the cursor for 10s: allies inside cannot drop below 1 HP.",
        "Scripture", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::HpFloor, radius: 150.0, magnitude: 1.0 }), 10.0, 25.0, REQ_WITCH_DOCTOR));
    Item::seed(ctx, ability_item("Empowering Circle",
        "Draw a circle at the cursor for 10s: allies inside deal 20% more damage.",
        "Tome", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::DamageBuff, radius: 150.0, magnitude: 1.2 }), 10.0, 20.0, REQ_WITCH_DOCTOR));

    // Ally heals.
    Item::seed(ctx, ability_item("Mending Circle",
        "Draw a circle at the cursor for 10s: allies inside regenerate 3 HP/s.",
        "Tome", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::HealOverTime, radius: 150.0, magnitude: 3.0 }), 10.0, 20.0, REQ_WITCH_DOCTOR));
    Item::seed(ctx, ability_item("Surging Circle",
        "Draw a circle at the cursor that detonates after 3s, healing allies inside for 25 HP.",
        "Tome", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::BurstHeal, radius: 150.0, magnitude: 25.0 }), 3.0, 15.0, REQ_WITCH_DOCTOR));

    // Enemy curses.
    Item::seed(ctx, ability_item("Withering Circle",
        "Draw a circle at the cursor for 10s: enemies inside bleed for 6 damage/s.",
        "Skull", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::EnemyDot, radius: 150.0, magnitude: 6.0 }), 10.0, 15.0, REQ_WITCH_DOCTOR));
    Item::seed(ctx, ability_item("Ruin Circle",
        "Draw a circle at the cursor that detonates after 3s, dealing 40 damage to enemies inside.",
        "Skull", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::EnemyBurst, radius: 150.0, magnitude: 40.0 }), 3.0, 15.0, REQ_WITCH_DOCTOR));
    Item::seed(ctx, ability_item("Hindering Circle",
        "Draw a circle at the cursor for 10s: enemies inside are slowed 40%.",
        "Orb", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::EnemySlow, radius: 150.0, magnitude: 0.4 }), 10.0, 15.0, REQ_WITCH_DOCTOR));
    Item::seed(ctx, ability_item("Exposing Circle",
        "Draw a circle at the cursor for 10s: enemies inside take 50% more damage.",
        "Orb", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::EnemyVulnerability, radius: 150.0, magnitude: 0.5 }), 10.0, 20.0, REQ_WITCH_DOCTOR));

    // Communion.
    Item::seed(ctx, ability_item("Communion Circle",
        "Draw a circle at the cursor for 10s: hits on anyone inside are split evenly across everyone inside.",
        "Scripture", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::DamageShare, radius: 150.0, magnitude: 1.0 }), 10.0, 25.0, REQ_WITCH_DOCTOR));

    // Wizard — the offensive spire IS a seeded EnemyDot zone.
    Item::seed(ctx, ability_item("Arcane Spire",
        "Raise an arcane spire at the cursor for 8s: enemies near it burn for 8 damage/s.",
        "Orb", AbilityEffect::CastZone(ZoneParams { kind: ZoneKind::EnemyDot, radius: 120.0, magnitude: 8.0 }), 8.0, 15.0, REQ_WIZARD));

    // ===== P4: movement abilities =====
    // Warrior — hold-to-charge teleport: slowed (SelfSlow) and unable to fire while
    // charging; release blinks toward the cursor at 25–100% of range by charge fraction.
    Item::seed(ctx, ability_item("Warpath",
        "Hold to charge (slowed, can't fire), release to blink toward the cursor — up to 400 at full charge.",
        "Skull", AbilityEffect::Teleport(TeleportParams { range: 400.0, charge_scaled: true }), 0.0, 12.0, REQ_WARRIOR));
    // Trickster — the instant variant.
    Item::seed(ctx, ability_item("Blink",
        "Instantly teleport toward the cursor, up to 300.",
        "Orb", AbilityEffect::Teleport(TeleportParams { range: 300.0, charge_scaled: false }), 0.0, 8.0, REQ_TRICKSTER));
    // Bullet master — client-tweened pull (the server only validates; movement reports
    // carry the position).
    Item::seed(ctx, ability_item("Grapple Hook",
        "Pull yourself toward the cursor, up to 450.",
        "Orb", AbilityEffect::Grapple(450.0), 0.0, 10.0, REQ_BULLET_MASTER));

    // ===== P5: spells (shape damage + visual relay) =====
    // Wizard — all scale off Wisdom.
    Item::seed(ctx, ability_item("Arc Beam",
        "Blast a 500×40 beam in front of you for 30 damage. Scales with wisdom.",
        "Orb", AbilityEffect::Spell(SpellParams {
            shape: SpellShape::Line(LineParams { length: 500.0, width: 40.0 }),
            damage: 30.0, hits: 1, charge_scaled: false, scaling: ScalingStat::Wisdom,
            visual: AbilityVisualKind::Line,
        }), 0.0, 8.0, REQ_WIZARD));
    Item::seed(ctx, ability_item("Converge",
        "Collapse arcane energy onto the cursor: 40 damage within 120. Scales with wisdom.",
        "Tome", AbilityEffect::Spell(SpellParams {
            shape: SpellShape::Circle(CircleParams { radius: 120.0 }),
            damage: 40.0, hits: 1, charge_scaled: false, scaling: ScalingStat::Wisdom,
            visual: AbilityVisualKind::Converge,
        }), 0.0, 12.0, REQ_WIZARD));
    Item::seed(ctx, ability_item("Fireball",
        "Hold to charge (slowed, can't fire), release to hurl a fireball at the cursor: up to 50 damage within 100. Scales with wisdom.",
        "Scripture", AbilityEffect::Spell(SpellParams {
            shape: SpellShape::Circle(CircleParams { radius: 100.0 }),
            damage: 50.0, hits: 1, charge_scaled: true, scaling: ScalingStat::Wisdom,
            visual: AbilityVisualKind::Circle,
        }), 0.0, 10.0, REQ_WIZARD));
    Item::seed(ctx, ability_item("Meteor",
        "Call a meteor down on the cursor: 80 damage within 200. Scales with wisdom.",
        "Skull", AbilityEffect::Spell(SpellParams {
            shape: SpellShape::Circle(CircleParams { radius: 200.0 }),
            damage: 80.0, hits: 1, charge_scaled: false, scaling: ScalingStat::Wisdom,
            visual: AbilityVisualKind::Meteor,
        }), 0.0, 20.0, REQ_WIZARD));
    Item::seed(ctx, ability_item("Arc Laser",
        "Pierce the battlefield with an 800×15 laser for 45 damage. Scales with wisdom.",
        "Orb", AbilityEffect::Spell(SpellParams {
            shape: SpellShape::Line(LineParams { length: 800.0, width: 15.0 }),
            damage: 45.0, hits: 1, charge_scaled: false, scaling: ScalingStat::Wisdom,
            visual: AbilityVisualKind::Line,
        }), 0.0, 15.0, REQ_WIZARD));

    // Bullet master — both scale off Dexterity.
    Item::seed(ctx, ability_item("Charged Shot",
        "Hold to charge (slowed, can't fire), release to fire a 600×20 piercing shot for up to 70 damage. Scales with dexterity.",
        "Skull", AbilityEffect::Spell(SpellParams {
            shape: SpellShape::Line(LineParams { length: 600.0, width: 20.0 }),
            damage: 70.0, hits: 1, charge_scaled: true, scaling: ScalingStat::Dexterity,
            visual: AbilityVisualKind::Line,
        }), 0.0, 12.0, REQ_BULLET_MASTER));
    Item::seed(ctx, ability_item("Volley",
        "Spray a 350 cone in front of you: 6 pellets of 10 damage each — armour eats every pellet. Scales with dexterity.",
        "Orb", AbilityEffect::Spell(SpellParams {
            shape: SpellShape::Cone(ConeParams { length: 350.0, spread: 0.6 }),
            damage: 60.0, hits: 6, charge_scaled: false, scaling: ScalingStat::Dexterity,
            visual: AbilityVisualKind::Cone,
        }), 0.0, 10.0, REQ_BULLET_MASTER));
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

    // The Warbow's innate shotgun toggle: a 5-arrow fan — 7/bullet raw against the heavy
    // single shot's 60, so armour decides which toggle wins.
    Enchantment::seed(ctx, Enchantment {
        enchantment_id: "scatter_fan".to_string(),
        display_name: "Scatter Fan".to_string(),
        description: "Toggle: loose a 5-arrow fan in front of you.".to_string(),
        texture_id: "scatter_fan".to_string(),
        stat_modifiers: vec![],
        behaviors: vec![
            EnchantmentBehavior::WeaponToggle(WeaponBehavior {
                damage: 35,
                range: 110.0,
                fire_rate: 1.5,
                projectile_speed: 300.0,
                shot_count: 5,
                zone_count: 25,
                pierce: false,
                projectile_texture_id: "Arrow".to_string(),
                pattern: WeaponPattern::Triple, // even fan: step = spread/(n-1)
                spread_angle: 0.9,
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
