extends ResDrop
class_name ResDropFood

func drop_collected(player: Player):
	player.health += 20
