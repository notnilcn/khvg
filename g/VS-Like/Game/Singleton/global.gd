extends Node
# Overall Description
"""Global.gd — autoloaded singleton accessible from every script as "Global".
"""

# Player Helper variables and functions
#### `players`
"""Dictionary containing all active player nodes, keyed by their network peer_id.
The reason why this is in the singleton is because players are going to be able to see other players where ever they are.""" 
"""
 ```
"""
var players: Dictionary = {}   # { peer_id: int  →  Player node 
"""
 ```
"""

#### `get_local_player() -> `[`Player`](<player.gd>)
"""Returns the peer_id of the current player.
The reason for this function is because of the way godot multiplayer is set up. The client and the server run the same code, so there needs to be a function that identifies the current player.""" 
"""
```
"""
func get_local_player() -> Player:
	var my_id := multiplayer.get_unique_id()
	return players.get(my_id, null)
"""
```
"""


# Item Helper variables and functions
#### `RES_ITEMS`
"""Array containing all loot-able items in the game (weapons, abilities, accessories, armor).
Add new ResItem .tres files here to make them appear in chests.
The reason why this is in the singleton is because players are always going to be able to drop and pickup their items wherever they are."""
"""
```
"""
const RES_ITEMS: Array[ResItem] = [
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Axe/res_axe.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/res_arrow.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/SineArrow/res_sine_arrow.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Bible/res_bible.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Boots/res_boots.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Parchment/res_parchment.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Ring/res_ring.tres"),
]
"""
```
"""

#### `get_available_items() -> Array[`[`ResItem`](<res_item.gd>)`]`
"""Returns all available items that can appear in chests.
Currently returns the full pool; filter here if desired (e.g. exclude items the player already has both equipped and in a full inventory)."""
"""
```
"""
func get_available_items() -> Array[ResItem]:
	return RES_ITEMS.duplicate()
"""
```
"""

#### pickup_item(res_itm: [`ResItem`](<res_item.gd>))
"""Pick up an item
Equip it immediately if the relevant slot is empty, otherwise send it to the player's inventory.  If the inventory is also full, logs a warning and discards the item."""
"""
```
"""
func pickup_item(res_itm: ResItem) -> void:
	var player := get_local_player()
	if player == null:
		return
	if _slot_is_empty(player, res_itm):
		player.equip_item(res_itm)
	else:
		if not player.add_to_inventory(res_itm):
			push_warning("Inventory full — could not pick up: " + res_itm.item_name)
"""
```
"""

"""`_slot_is_empty()`"""
"""
```
"""
func _slot_is_empty(player: Player, res_wpn: ResItem) -> bool:
	match res_wpn.item_type:
		ResItem.ItemType.WEAPON:
			return player.equipped_weapon == null
		ResItem.ItemType.ABILITY:
			return player.equipped_ability == null
		ResItem.ItemType.ARMOR:
			return player.equipped_armor == null
		ResItem.ItemType.ACCESSORY:
			return player.equipped_accessories.size() < player.get_accessory_slot_count()
	return false
"""
```
"""
