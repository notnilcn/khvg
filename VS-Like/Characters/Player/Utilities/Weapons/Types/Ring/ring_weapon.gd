extends Weapon

## READY
func _ready() -> void:
	if not is_owner_weapon:
		return
	Global.get_local_player().extra_damage += 0.25 # Increment Player Damage


func _on_upgraded() -> void:
	Global.get_local_player().extra_damage += 0.125 # Increment extra damage 0.125 per upgrade


func remove_passive_effects(player: Player) -> void:
	# Initial bonus is 0.25; each upgrade adds 0.125. Total = 0.25 + 0.125*(level-1).
	player.extra_damage -= 0.25 + 0.125 * (upgrade_level - 1)
