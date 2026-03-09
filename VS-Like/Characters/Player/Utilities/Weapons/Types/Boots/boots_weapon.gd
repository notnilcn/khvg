extends Weapon

## READY
func _ready() -> void:
	Global.get_local_player().extra_speed += 0.125 # Increment Player Speed


## WEAPON UPGRADE
func _on_upgraded() -> void:
	Global.get_local_player().extra_speed += 0.125 # Increment value 0.125 per upgrade
