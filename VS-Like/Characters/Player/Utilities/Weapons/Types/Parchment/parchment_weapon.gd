extends Weapon

## READY
func _ready() -> void:
	Global.player.extra_exp += 0.125 # Add points multiplier


## WEAPON UPGRADE
func _on_upgraded() -> void:
	Global.player.extra_exp += 0.125 # Increment multiplier 0.125 per upgrade
