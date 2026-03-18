extends VBoxContainer
class_name WeaponContainerUI

# SCENE TO SPAWN
const WEAPON_IMAGE_SCENE: PackedScene = preload("res://VS-Like/Characters/Player/Utilities/UI/WeaponUI/weapon_image.tscn")

# References
@onready var actives_cont: HBoxContainer = $ActivesCont
@onready var passives_cont: HBoxContainer = $PassivesCont

# Weapons in hand
var curr_weapons: Array[WeaponImage] = []

## ADD WEAPON
func add_weapon(weapon: Weapon):
	var weapon_image: WeaponImage = WEAPON_IMAGE_SCENE.instantiate()
	if weapon && weapon.resource:
		weapon_image.resource = weapon.resource # Add the Resource to Weapon Image
		
		# Choose container if it is passive or not
		if weapon.resource.is_passive:
			passives_cont.add_child(weapon_image)
		else:
			actives_cont.add_child(weapon_image)
		
		# Add to Current Weapons Array
		curr_weapons.append(weapon_image)

## REMOVE WEAPON
func remove_weapon(weapon: Weapon) -> void:
	for i in range(curr_weapons.size()):
		if curr_weapons[i].resource == weapon.resource:
			curr_weapons[i].queue_free()
			curr_weapons.remove_at(i)
			return

# LEVEL UP WEAPON
func update_weapon(weapon: Weapon):
	if weapon && weapon.resource:
		for wpn_img in curr_weapons:
			if wpn_img && wpn_img.resource == weapon.resource:
				wpn_img.curr_weapon_level += 1
				return
		print("ERROR: WEAPON IMAGE NOT FOUND")
