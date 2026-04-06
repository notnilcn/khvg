extends Node
## Global.gd — autoloaded singleton accessible from every script as "Global".

# ── item registry ─────────────────────────────────────────────────────────────
## All loot-able items in the game (weapons, abilities, accessories, armor).
## Add new ResItem .tres files here to make them appear in chests.
const RES_ITEMS: Array[ResItem] = [
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Axe/res_axe.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/res_arrow.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/SineArrow/res_sine_arrow.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Bible/res_bible.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Boots/res_boots.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Parchment/res_parchment.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Ring/res_ring.tres"),
]

# ── player registry ───────────────────────────────────────────────────────────
## All active player nodes, keyed by their network peer_id.
var players: Dictionary = {}   # { peer_id: int  →  Player node }


# ── local player helper ───────────────────────────────────────────────────────
func get_local_player() -> Player:
	var my_id := multiplayer.get_unique_id()
	return players.get(my_id, null)


# ── item helpers ──────────────────────────────────────────────────────────────
## Returns all available items that can appear in chests.
## Currently returns the full pool; filter here if desired (e.g. exclude items
## the player already has both equipped and in a full inventory).
func get_available_items() -> Array[ResItem]:
	return RES_ITEMS.duplicate()


## Pick up an item: equip it immediately if the relevant slot is empty,
## otherwise send it to the player's inventory.  If the inventory is also full,
## logs a warning and discards the item.
func pickup_item(res_wpn: ResItem) -> void:
	var player := get_local_player()
	if player == null:
		return

	if _slot_is_empty(player, res_wpn):
		player.equip_item(res_wpn)
	else:
		if not player.add_to_inventory(res_wpn):
			push_warning("Inventory full — could not pick up: " + res_wpn.item_name)


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


# ── legacy alias ─────────────────────────────────────────────────────────────
## Kept so any existing call sites (admin commands, etc.) still compile.
## Prefer pickup_item() for new code.
func add_weapon(res_wpn: ResItem) -> void:
	pickup_item(res_wpn)


## Legacy: returns items that are not yet equipped (used by nothing now that
## weapon_selection is removed, but kept to avoid compile errors if referenced).
func get_available_weapons() -> Array[ResItem]:
	return get_available_items()
