extends Button
class_name WeaponItem

## WeaponItem — legacy weapon-selection button.  The weapon-selection menu is no
## longer used (weapons come from chests only), but this file is kept so the
## .tscn that references it continues to parse without errors.

@onready var image:      TextureRect = %Image
@onready var name_label: Label       = %NameLabel
@onready var desc_label: Label       = %DescLabel

var res_weapon: ResItem : set = _set_res_weapon

func _set_res_weapon(res_wpn: ResItem) -> void:
	res_weapon = res_wpn
	image.texture   = res_weapon.sprite
	name_label.text = res_weapon.item_name
	desc_label.text = res_weapon.item_description


func _on_pressed() -> void:
	if res_weapon:
		Global.pickup_item(res_weapon)
	else:
		Global.get_local_player().health += 20
