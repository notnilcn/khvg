extends ResDrop
class_name ResDropChest

func drop_collected(player: Player):
	player.chest_ui.open_chest()
