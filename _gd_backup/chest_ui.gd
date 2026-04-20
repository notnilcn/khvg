extends Control
class_name ChestUI

## chest_ui.gd
##
## Displays up to three random items from the global item pool when the player
## opens a chest dropped by an elite enemy.  The player chooses ONE item to
## pick up; the rest are left behind.  Pressing "Done" skips all items.

const FOOD_IMAGE: Texture2D = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Food/sprite_food.tres")

@onready var items_container: HBoxContainer    = %ItemsContainer
@onready var audio_player:    AudioStreamPlayer = $AudioPlayer


func open_chest() -> void:
	get_tree().paused = true
	visible = true
	audio_player.play()
	_populate_items()


func _populate_items() -> void:
	var pool: Array[ResItem] = Global.get_available_items()

	if pool.is_empty():
		# Nothing to offer — heal the player as a fallback.
		_show_food_fallback()
		return

	pool.shuffle()
	var count := mini(pool.size(), 3)

	for i in count:
		_create_item_button(pool[i])


func _create_item_button(res: ResItem) -> void:
	var btn := Button.new()
	btn.custom_minimum_size = Vector2(96, 112)
	btn.icon                = res.sprite
	btn.expand_icon         = true
	btn.text                = res.item_name
	btn.icon_alignment      = HORIZONTAL_ALIGNMENT_CENTER
	btn.vertical_icon_alignment = VERTICAL_ALIGNMENT_TOP
	btn.tooltip_text        = res.item_description
	btn.pressed.connect(func() -> void: _on_item_chosen(res))
	items_container.add_child(btn)


func _show_food_fallback() -> void:
	var img := TextureRect.new()
	img.custom_minimum_size = Vector2(64, 64)
	img.texture = FOOD_IMAGE
	items_container.add_child(img)
	Global.get_local_player().health += 20


func _on_item_chosen(res: ResItem) -> void:
	Global.pickup_item(res)
	close_chest()


func close_chest() -> void:
	for child in items_container.get_children():
		child.queue_free()
	visible = false
	get_tree().paused = false


func _on_done_button_pressed() -> void:
	close_chest()
