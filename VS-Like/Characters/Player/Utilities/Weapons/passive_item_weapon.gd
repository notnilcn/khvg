extends Weapon
class_name PassiveItemWeapon

## passive_item_weapon.gd
##
## Generic base class for every passive equipment item (accessories, armor).
## All bonus values come from the ResItem resource — no hardcoding needed.
## To add a new accessory, create a .tres with the right bonus_* fields and
## point weapon_scene at a scene that uses this script (or a subclass of it).
##
## Subclass this only if you need custom logic beyond simple stat bonuses.
## Otherwise boots / parchment / ring can all extend this class directly and
## their individual .gd files become a single "extends PassiveItemWeapon" line.


func _ready() -> void:
	super._ready()  # sets up the fire timer in Weapon._ready()
	if not is_owner_weapon:
		return
	var player := Global.get_local_player()
	if player == null:
		return
	_apply(player, 1.0)


## Reverse all bonuses when this item is unequipped.
func remove_passive_effects(player: Player) -> void:
	_apply(player, -1.0)


## Apply (sign = +1.0) or remove (sign = -1.0) every bonus defined in the resource.
func _apply(player: Player, sign: float) -> void:
	player.extra_speed  += resource.bonus_speed_mult  * sign
	player.extra_exp    += resource.bonus_exp_mult    * sign
	player.extra_damage += resource.bonus_damage_mult * sign
	player.max_health   += resource.bonus_max_health  * sign
	player.defense      += int(resource.bonus_defense * sign)
