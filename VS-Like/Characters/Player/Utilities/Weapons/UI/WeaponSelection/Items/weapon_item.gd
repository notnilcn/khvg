extends Button
class_name WeaponItem

@onready var image: TextureRect = %Image
@onready var name_label: Label = %NameLabel
@onready var desc_label: Label = %DescLabel

var res_weapon: ResWeapon : set = _set_res_weapon

# Set Weapon Button using the Resource
func _set_res_weapon(res_wpn: ResWeapon):
	res_weapon = res_wpn
	image.texture = res_weapon.sprite
	name_label.text = res_weapon.weapon_name
	
	for weapon in Global.player.weapons:
		if weapon.resource == res_weapon:
			match weapon.upgrade_level:
				1:
					desc_label.text = res_weapon.upgrade_description_2
				2:
					desc_label.text = res_weapon.upgrade_description_3
			return
	
	desc_label.text = res_weapon.weapon_description

## ADD/UPGRADE WEAPON
func _on_pressed() -> void:
	if res_weapon:
		Global.add_weapon(res_weapon)
	else:
		Global.player.health += 20
