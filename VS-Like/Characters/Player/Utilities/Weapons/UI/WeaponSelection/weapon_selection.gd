extends Control
class_name WeaponSelectionUI

# Scene to spawn
const WEAPON_ITEM_SCENE: PackedScene = preload("res://VS-Like/Characters/Player/Utilities/Weapons/UI/WeaponSelection/Items/weapon_item.tscn")

@onready var items_container: VBoxContainer = %ItemsContainer
@onready var audio_player: AudioStreamPlayer = $AudioPlayer

# Show Menu
func show_selection():
	get_tree().paused = true
	visible = true
	audio_player.play()
	set_items()

# Add Weapon Buttons to Menu
func set_items():
	var av_res_weapons: Array[ResItem] = Global.get_available_weapons()
	if av_res_weapons.is_empty():
		create_item()
	else:
		var amount_to_show: int = clampi(av_res_weapons.size(), 0, 3)
		av_res_weapons.shuffle()
		
		for i in amount_to_show:
			create_item(av_res_weapons[i])

# Create Weapon Buttons
func create_item(res: ResItem = null):
	var weapon_item: WeaponItem = WEAPON_ITEM_SCENE.instantiate()
	items_container.add_child(weapon_item)
	if res:
		weapon_item.res_weapon = res
	weapon_item.pressed.connect(hide_selection)

# Remove Weapon Buttons when exit
func remove_items():
	for item in items_container.get_children():
		item.queue_free()

# Hide Menu
func hide_selection():
	visible = false
	remove_items()
	get_tree().paused = false
