extends Resource
class_name ResItem

## res_item.gd — single resource class for every loot-able item in the game.
##
## Design rationale
## ────────────────
## Rather than a deep hierarchy (ResWeapon, ResArmor, ResAccessory …) or
## separate classes per item, we use one flat class whose fields are grouped
## by which item_type actually uses them.  The chief advantage is that new
## accessories need ZERO new GDScript — just a .tres file with the right
## bonus_* values.  A new weapon type only needs a new Weapon scene.
##
## Field groups
## ────────────
## • Shared by every item: weapon_scene, item_name, item_description, sprite,
##   audio, item_type, is_passive.
## • Weapon / Ability only: damage, fire_rate, attach_to_rotation.
## • Accessory / Armor only: bonus_speed_mult, bonus_exp_mult,
##   bonus_damage_mult, bonus_defense, bonus_max_health.
## • Optional upgrade flavour text: upgrade_description_2/3.
##
## Unused fields on a given item default to 0 / false and are ignored at runtime.

enum ItemType {
	WEAPON    = 0,  ## Active weapon slot  (axe, arrow, sine bow …)
	ABILITY   = 1,  ## Ability slot        (bible …)
	ARMOR     = 2,  ## Armor slot          (not yet implemented)
	ACCESSORY = 3,  ## Accessory slot(s)   (boots, parchment, ring …)
}

## The Weapon node scene to instantiate when this item is equipped.
@export var weapon_scene: PackedScene

@export_category("Aspect")
@export var item_name: String
@export_multiline var item_description: String
@export var sprite: Texture2D
@export var audio: AudioStreamWAV

@export_category("Item")
## Which equipment slot this item occupies.
@export var item_type: ItemType = ItemType.WEAPON
## Passive items skip the fire timer audio and never broadcast _rpc_fire.
## Set true for any item that applies stat bonuses rather than firing.
@export var is_passive: bool = false

@export_category("Weapon / Ability Stats")
## Base damage dealt per hit.  Ignored for passive items.
@export var damage: float = 0.0
## Seconds between shots.  Also controls the fire-timer cadence for passive nodes.
@export var fire_rate: float = 1.0
## If true the weapon scene is parented to Sprite2D so it mirrors with the player.
@export var attach_to_rotation: bool = false

@export_category("Passive Stat Bonuses")
## Added to player.extra_speed (multiplicative layer).  e.g. 0.125 → +12.5 % speed.
@export var bonus_speed_mult:  float = 0.0
## Added to player.extra_exp  (multiplicative layer).  e.g. 0.125 → +12.5 % XP.
@export var bonus_exp_mult:    float = 0.0
## Added to player.extra_damage (multiplicative layer).  e.g. 0.25 → +25 % damage.
@export var bonus_damage_mult: float = 0.0
## Added directly to player.defense (flat).
@export var bonus_defense:     int   = 0
## Added directly to player.max_health (flat).
@export var bonus_max_health:  float = 0.0

@export_category("Upgrade Flavour Text")
@export_multiline var upgrade_description_2: String
@export_multiline var upgrade_description_3: String
