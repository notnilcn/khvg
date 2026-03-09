extends Node

## ALL WEAPON RESOURCES
# Add each new Weapon Resource you create here
const RES_WEAPONS: Array[ResWeapon] = [
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Axe/res_axe.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/res_arrow.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Bible/res_bible.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Boots/res_boots.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Parchment/res_parchment.tres"),
	preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Ring/res_ring.tres")
]

## PLAYER REFERENCE
var player: Player

## GET AVAILABLE WEAPONS (CAN BE UPGRADED OR ADDED)
func get_available_weapons() -> Array[ResWeapon]:
	var av_res_weapons: Array[ResWeapon] = RES_WEAPONS.duplicate()
	
	for wpn in player.weapons:
		if av_res_weapons.has(wpn.resource) && wpn.upgrade_level == 3:
			av_res_weapons.erase(wpn.resource)
	
	return av_res_weapons

## ADD/UPGRADE WEAPON
func add_weapon(res_wpn: ResWeapon):
	for weapon in player.weapons:
		if weapon.resource == res_wpn:
			weapon.upgrade_weapon()
			player.weapons_container_ui.update_weapon(weapon)
			return
	
	var weapon: Weapon = res_wpn.weapon_scene.instantiate()
	weapon.resource = res_wpn
	
	if weapon.attach_to_rotation:
		player.sprite_2d.call_deferred("add_child", weapon) # Add to Sprite2D to rotate with it
	else:
		player.call_deferred("add_child", weapon) # Add to Player to have an independent rotation
	
	player.weapons.append(weapon) # Add to Player Array
	player.weapons_container_ui.add_weapon(weapon) # To Player HUD
