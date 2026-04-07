extends MarginContainer
class_name WeaponImage

@onready var weapon_texture: TextureRect = %WeaponTexture
@onready var level_2: ColorRect = %Level2
@onready var level_3: ColorRect = %Level3

# Data
var resource: ResItem : set = _set_resource
var curr_weapon_level: int = 1 : set = _set_curr_weapon_level
var _pending_resource: ResItem

## READY
func _ready() -> void:
	if _pending_resource:
		_update_weapon_texture(_pending_resource)

## SET WEAPON IMAGE USING THE RESOURCE
func _set_resource(new_resource: ResItem):
	resource = new_resource
	if !is_node_ready():
		_pending_resource = new_resource
	else:
		_update_weapon_texture(new_resource)

## ADD WEAPON SPRITE AS IMAGE
func _update_weapon_texture(res: ResItem) -> void:
	if res && weapon_texture:
		weapon_texture.texture = res.sprite

## UPDATE WEAPON LEVEL
func _set_curr_weapon_level(new_level: int):
	curr_weapon_level = new_level
	
	match curr_weapon_level:
		2:
			level_2.color = Color.WHITE
		3:
			level_3.color = Color.WHITE
