extends VBoxContainer
class_name WeaponContainerUI

## weapons_container_ui.gd
##
## HUD strip that shows the player's currently equipped items.
## Call update_equipment(player) whenever the loadout changes.
##
## Layout (reusing the two existing HBoxContainers from the .tscn):
##   ActivesCont  → Weapon slot  +  Ability slot
##   PassivesCont → Armor slot   +  Accessory slots (1–3)

const WEAPON_IMAGE_SCENE: PackedScene = preload("res://VS-Like/Characters/Player/Utilities/UI/WeaponUI/weapon_image.tscn")

@onready var actives_cont:  HBoxContainer = $ActivesCont
@onready var passives_cont: HBoxContainer = $PassivesCont


## Rebuild the HUD strip from the player's current equipment state.
func update_equipment(player: Player) -> void:
	_clear(actives_cont)
	_clear(passives_cont)

	if player.equipped_weapon:
		_add_image(actives_cont, player.equipped_weapon.resource)
	if player.equipped_ability:
		_add_image(actives_cont, player.equipped_ability.resource)

	if player.equipped_armor != null:
		# When armor is implemented this will show the armor icon.
		pass
	for acc in player.equipped_accessories:
		_add_image(passives_cont, acc.resource)


func _clear(container: HBoxContainer) -> void:
	for child in container.get_children():
		child.queue_free()


func _add_image(container: HBoxContainer, res: ResItem) -> void:
	var img: WeaponImage = WEAPON_IMAGE_SCENE.instantiate()
	img.resource = res
	container.add_child(img)


# ── legacy stubs (kept so old call-sites still compile) ───────────────────────
func add_weapon(_weapon: Weapon) -> void:
	pass

func remove_weapon(_weapon: Weapon) -> void:
	pass

func update_weapon(_weapon: Weapon) -> void:
	pass
