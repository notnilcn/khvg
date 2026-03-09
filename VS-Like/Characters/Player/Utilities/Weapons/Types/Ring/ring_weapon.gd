extends Weapon

## READY
func _ready() -> void:
	Global.get_local_player().extra_damage += 0.25 # Increment Player Damage


func _on_upgraded() -> void:
	Global.get_local_player().extra_damage += 0.125 # Increment extra damage 0.125 per upgrade
