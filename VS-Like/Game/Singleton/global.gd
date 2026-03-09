extends Node
## Global.gd — autoloaded singleton accessible from every script as "Global".
##
## Previously this held a single `player` variable. Now it holds a dictionary
## of ALL connected players, keyed by their network peer_id.
##
## Why a dictionary?  In multiplayer each peer has a unique integer id.
## Keying by that id lets any system quickly look up "which node belongs to
## peer 42?" without searching an array.

# ── weapon resources ─────────────────────────────────────────────────────────
## All weapon types available in the game. Add new ResWeapon .tres files here
## to make them appear in weapon selection menus.
const RES_WEAPONS: Array[ResWeapon] = [
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Axe/res_axe.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/res_arrow.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Bible/res_bible.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Boots/res_boots.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Parchment/res_parchment.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Ring/res_ring.tres"),
]

# ── player registry ───────────────────────────────────────────────────────────
## All active player nodes, keyed by their network peer_id (an integer).
## peer_id 1 is always the server/host.  Client peer_ids are assigned by the
## server and are integers > 1.
##
## Example:  Global.players[2]  →  the Player node belonging to peer 2.
var players: Dictionary = {}   # { peer_id: int  →  Player node }


# ── local player helper ───────────────────────────────────────────────────────
## Returns the Player node that THIS instance of the game is controlling.
## Every machine in the game calls this to get "my" player.
##
## How it works: `multiplayer.get_unique_id()` returns the peer_id that the
## server assigned to us.  We look that id up in the players dictionary.
## Returns null if our player hasn't been spawned yet (e.g. during loading).
func get_local_player() -> Player:
	var my_id := multiplayer.get_unique_id()
	return players.get(my_id, null)


# ── weapon helpers ────────────────────────────────────────────────────────────
## Returns weapon resources that the LOCAL player can still acquire or upgrade.
## Fully-upgraded weapons (level 3) are excluded so they don't show up in menus.
func get_available_weapons() -> Array[ResWeapon]:
	var local_player := get_local_player()
	if local_player == null:
		return []

	var av_res_weapons: Array[ResWeapon] = RES_WEAPONS.duplicate()

	for wpn in local_player.weapons:
		if av_res_weapons.has(wpn.resource) && wpn.upgrade_level == 3:
			av_res_weapons.erase(wpn.resource)

	return av_res_weapons


## Gives the LOCAL player a weapon (or upgrades it if they already have it).
## Also updates the weapon display in the player's HUD.
func add_weapon(res_wpn: ResWeapon) -> void:
	var local_player := get_local_player()
	if local_player == null:
		return

	# If player already owns this weapon, upgrade it instead of adding again.
	for weapon in local_player.weapons:
		if weapon.resource == res_wpn:
			weapon.upgrade_weapon()
			local_player.weapons_container_ui.update_weapon(weapon)
			return

	# Otherwise instantiate a fresh weapon scene and attach it to the player.
	var weapon: Weapon = res_wpn.weapon_scene.instantiate()
	weapon.resource = res_wpn

	# Weapons with `attach_to_rotation` are parented to the Sprite2D so they
	# rotate when the player flips direction.  Others sit directly on the
	# Player node and rotate independently (e.g. arrow, bible).
	if weapon.attach_to_rotation:
		local_player.sprite_2d.call_deferred("add_child", weapon)
	else:
		local_player.call_deferred("add_child", weapon)

	local_player.weapons.append(weapon)
	local_player.weapons_container_ui.add_weapon(weapon)
