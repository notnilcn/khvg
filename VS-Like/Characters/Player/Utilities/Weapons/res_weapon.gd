extends Resource
class_name ResWeapon

## Which equipment slot this item occupies when equipped.
enum ItemType {
	WEAPON    = 0,  ## Active weapon slot (axe, arrow, sine bow, …)
	ABILITY   = 1,  ## Ability slot (bible, …)
	ARMOR     = 2,  ## Armor slot (not yet implemented)
	ACCESSORY = 3,  ## Magical accessory slot (boots, parchment, ring, …)
}

@export var weapon_scene: PackedScene # Scene to Spawn

@export_category("Aspect")
@export var weapon_name: String
@export_multiline var weapon_description: String # Main Description
@export var sprite: Texture2D
@export var audio: AudioStreamWAV # Attack Audio

@export_category("Stats")
@export var item_type: ItemType = ItemType.WEAPON
@export var damage: float = 10.0
@export var fire_rate: float = 1.0
@export var attach_to_rotation: bool = false # Rotate with the Player Sprite
@export var is_passive: bool = false # It does not attack, just upgrades stats

@export_category("Upgrades Description")
@export_multiline var upgrade_description_2: String # First Upgrade Desc (Level 2)
@export_multiline var upgrade_description_3: String # Second Upgrade Desc (Level 3)
