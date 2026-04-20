extends ResDrop
class_name ResDropPoint

func drop_collected(player: Player):
	player.points += 10 * player.extra_exp # Multiply points obtained (Passive Weapon)
