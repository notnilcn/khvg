extends Control
class_name ChestUI

const FOOD_IMAGE: Texture2D = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Food/sprite_food.tres")

@onready var items_container: HBoxContainer = %ItemsContainer
@onready var audio_player: AudioStreamPlayer = $AudioPlayer

func open_chest():
	get_tree().paused = true
	visible = true
	audio_player.play()
	select_weapons()

func select_weapons():
	var av_res_weapons: Array[ResWeapon] = Global.get_available_weapons()
	if av_res_weapons.is_empty():
		create_img(FOOD_IMAGE)
		Global.get_local_player().health += 20
	else:
		var amount_to_show: int = clampi(av_res_weapons.size(), 1, 3)
		var rand_value: int = randi_range(1, amount_to_show)
		av_res_weapons.shuffle()
		var selected_res: Array[ResWeapon] = av_res_weapons.slice(0, rand_value)
		
		for res in selected_res:
			create_img(res.sprite)
			Global.add_weapon(res)

func create_img(img_texture: Texture2D):
	var img: TextureRect = TextureRect.new()
	img.custom_minimum_size = Vector2(64, 64)
	img.texture = img_texture
	items_container.add_child(img)

func close_chest():
	for item in items_container.get_children():
		item.queue_free()
	visible = false
	get_tree().paused = false


func _on_done_button_pressed() -> void:
	close_chest()
