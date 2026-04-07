extends CharacterBody2D
class_name Player

## player.gd
##
## In single-player this node ran input and physics for the one local player.
## In multiplayer EVERY peer spawns a copy of this node for EVERY player in the
## game — but each copy only processes INPUT for the player it "owns".
##
## Ownership is controlled by `multiplayer_authority`.  The server sets:
##   player_node.set_multiplayer_authority(peer_id)
## so peer 42's player only processes WASD on peer 42's machine.
## Everyone else just displays that player's replicated position.

# ── exports / references ─────────────────────────────────────────────────────
@export var resource: ResPlayer

@onready var sprite_2d:       Sprite2D     = $Sprite2D
@onready var camera_2d:       Camera2D     = $Camera2D
@onready var enemy_detection: Area2D       = $EnemyDetection

# HUD — only shown / updated on THIS machine's local player
@onready var health_bar:           Healthbar         = $HealthBar
@onready var progress_bar:         ProgressBar       = %ProgressBar
@onready var weapons_container_ui: WeaponContainerUI = %WeaponsContainerUI
@onready var chest_ui:             ChestUI           = %ChestUI
@onready var hud:                  CanvasLayer       = $HUD

# ── core stats ────────────────────────────────────────────────────────────────
var max_health: float
var speed:      float
var health:     float : set = _set_health
var points:     float = 0.0 : set = _set_points

# ── RPG character stats ───────────────────────────────────────────────────────
## Strength: scales weapon/ability damage output.
var strength:  int = 0
## Wisdom: scales ability effectiveness; also unlocks extra accessory slots.
##   0–499  → 1 accessory slot
##   500–999 → 2 accessory slots
##   1000+   → 3 accessory slots
var wisdom:    int = 0
## Dexterity: scales movement speed and attack fire rate.
var dexterity: int = 0
## Defense: reduces incoming damage.
var defense:   int = 0
## Vitality: increases maximum health.
var vitality:  int = 0

# ── equipment slots (instantiated Weapon nodes in the scene tree) ─────────────
var equipped_weapon:      Weapon = null
var equipped_ability:     Weapon = null
var equipped_armor               = null  # placeholder — no armor class yet
var equipped_accessories: Array[Weapon] = []

# ── inventory (item resources, not live nodes) ────────────────────────────────
var inventory:      Array[ResItem] = []
var consumables:    Array[ResItem] = []
var inventory_max:  int = 6    # expandable to 12 via purchasable backpack
var consumables_max: int = 2   # expandable to 8 via purchasable backpack

# ── passive stat multipliers (modified by equipped accessories / abilities) ───
var extra_exp:    float = 1.0
var extra_damage: float = 1.0
var extra_speed:  float = 1.0

# ── inventory UI (programmatic, created at runtime for local player) ──────────
var inventory_ui: InventoryUI = null


# ── ready ─────────────────────────────────────────────────────────────────────
func _ready() -> void:
	var my_peer_id := get_multiplayer_authority()
	Global.players[my_peer_id] = self

	await get_tree().process_frame
	_init_player()


func _init_player() -> void:
	sprite_2d.texture = resource.sprite
	max_health = resource.max_health
	speed      = resource.max_speed
	health     = max_health

	var is_local := is_multiplayer_authority()
	camera_2d.enabled            = is_local
	health_bar.visible           = is_local
	progress_bar.visible         = is_local
	weapons_container_ui.visible = is_local

	# Instantiate the starting weapon on EVERY peer so all machines can show
	# the swing animation.  Only the owner peer fires it for real.
	var start_wpn := _instantiate_weapon_node(resource.res_weapon, is_local)

	if is_local:
		# Slot the starting weapon into the correct equipment slot.
		_assign_to_slot(start_wpn, resource.res_weapon.item_type)
		weapons_container_ui.update_equipment(self)

		# Create the inventory UI and attach it to the HUD canvas layer.
		inventory_ui = InventoryUI.new()
		hud.add_child(inventory_ui)


# ── physics ───────────────────────────────────────────────────────────────────
func _physics_process(_delta: float) -> void:
	if not is_multiplayer_authority():
		return

	_handle_movement()
	move_and_slide()
	_sync_state.rpc(global_position, sprite_2d.scale.x)


func _unhandled_input(event: InputEvent) -> void:
	if not is_multiplayer_authority():
		return
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_I and inventory_ui != null:
			if inventory_ui.visible:
				inventory_ui.close()
			else:
				inventory_ui.open(self)


@rpc("any_peer", "call_remote", "unreliable_ordered")
func _sync_state(pos: Vector2, scale_x: float) -> void:
	if multiplayer.get_remote_sender_id() != get_multiplayer_authority():
		return
	global_position   = pos
	sprite_2d.scale.x = scale_x


func _handle_movement() -> void:
	var movement_input := Input.get_vector("Left", "Right", "Up", "Down").normalized()
	velocity = movement_input * (speed * extra_speed)

	if velocity.x > 0:
		sprite_2d.scale.x = 1
	elif velocity.x < 0:
		sprite_2d.scale.x = -1


# ── health ────────────────────────────────────────────────────────────────────
@rpc("any_peer", "call_local", "reliable")
func hurt(dmg: float) -> void:
	var sender := multiplayer.get_remote_sender_id()
	if sender != 0 and sender != 1:
		return

	health -= dmg

	if health <= 0 and is_multiplayer_authority():
		get_tree().call_deferred("change_scene_to_file", "res://VS-Like/Maps/Woods/woods_map.tscn")


func _set_health(new_health: float) -> void:
	health = clamp(new_health, 0, max_health)
	if health_bar and is_multiplayer_authority():
		health_bar.value = health


# ── experience / levelling ────────────────────────────────────────────────────
func _set_points(new_points: float) -> void:
	if not is_multiplayer_authority():
		return

	if new_points >= progress_bar.max_value:
		points = 0
		progress_bar.max_value += 100
		# Level up: no weapon selection anymore — gain a small stat bonus instead.
		_on_level_up()
	else:
		points = new_points

	progress_bar.value = points


func _on_level_up() -> void:
	# Placeholder: grant a small flat stat increase each level.
	# This can be replaced with a stat-choice UI later.
	strength  += 1
	dexterity += 1
	defense   += 1
	vitality  += 1
	max_health += 5.0
	health = min(health + 5.0, max_health)

	if inventory_ui and inventory_ui.visible:
		inventory_ui.refresh()


# ── accessory slot count ──────────────────────────────────────────────────────
## Returns how many accessory slots the player currently has unlocked.
## Scales with Wisdom:  0–499 → 1,  500–999 → 2,  1000+ → 3.
func get_accessory_slot_count() -> int:
	if wisdom >= 1000:
		return 3
	elif wisdom >= 500:
		return 2
	return 1


# ── equipment helpers ─────────────────────────────────────────────────────────
## Instantiate a weapon scene and attach it to the player (for every peer so
## animations are visible, but only the owner fires it for real).
func _instantiate_weapon_node(res_wpn: ResItem, is_owner: bool) -> Weapon:
	var weapon: Weapon = res_wpn.weapon_scene.instantiate()
	weapon.resource        = res_wpn
	weapon.is_owner_weapon = is_owner
	if res_wpn.attach_to_rotation:
		sprite_2d.add_child(weapon)
	else:
		add_child(weapon)
	return weapon


## Put an already-instantiated weapon node into the correct equipment variable.
func _assign_to_slot(weapon: Weapon, item_type: ResItem.ItemType) -> void:
	match item_type:
		ResItem.ItemType.WEAPON:
			equipped_weapon = weapon
		ResItem.ItemType.ABILITY:
			equipped_ability = weapon
		ResItem.ItemType.ARMOR:
			equipped_armor = weapon
		ResItem.ItemType.ACCESSORY:
			equipped_accessories.append(weapon)


## Equip an item resource (from inventory or directly from a chest).
## Returns true if the item was equipped, false if the slot was full.
func equip_item(res_wpn: ResItem) -> bool:
	if not is_multiplayer_authority():
		return false

	match res_wpn.item_type:
		ResItem.ItemType.WEAPON:
			if equipped_weapon:
				_unequip_to_inventory(equipped_weapon)
			equipped_weapon = _instantiate_weapon_node(res_wpn, true)
			weapons_container_ui.update_equipment(self)
			return true

		ResItem.ItemType.ABILITY:
			if equipped_ability:
				_unequip_to_inventory(equipped_ability)
			equipped_ability = _instantiate_weapon_node(res_wpn, true)
			weapons_container_ui.update_equipment(self)
			return true

		ResItem.ItemType.ARMOR:
			# TODO: implement armor node class
			return false

		ResItem.ItemType.ACCESSORY:
			if equipped_accessories.size() < get_accessory_slot_count():
				var acc := _instantiate_weapon_node(res_wpn, true)
				equipped_accessories.append(acc)
				weapons_container_ui.update_equipment(self)
				return true
			return false

	return false


## Remove an equipped weapon node, sending it to inventory if possible.
func _unequip_to_inventory(weapon: Weapon) -> void:
	if weapon == null:
		return
	if inventory.size() < inventory_max:
		inventory.append(weapon.resource)
	weapon.remove_passive_effects(self)
	weapon.queue_free()


## Add an item resource directly to inventory (returns false if full).
func add_to_inventory(res_wpn: ResItem) -> bool:
	if inventory.size() < inventory_max:
		inventory.append(res_wpn)
		return true
	return false


## Add a consumable to the consumable slots (returns false if full).
func add_consumable(res_wpn: ResItem) -> bool:
	if consumables.size() < consumables_max:
		consumables.append(res_wpn)
		return true
	return false


# ── chat command RPCs ─────────────────────────────────────────────────────────
@rpc("any_peer", "call_remote", "reliable")
func rpc_give_weapon(res_path: String) -> void:
	if multiplayer.get_remote_sender_id() not in [0, 1]:
		return
	if not is_multiplayer_authority():
		return
	var res := load(res_path) as ResItem
	if res:
		Global.pickup_item(res)


@rpc("any_peer", "call_remote", "reliable")
func rpc_remove_weapon(res_path: String) -> void:
	if multiplayer.get_remote_sender_id() not in [0, 1]:
		return
	if not is_multiplayer_authority():
		return

	# Try to unequip from active equipment slots first.
	for slot_weapon in [equipped_weapon, equipped_ability]:
		if slot_weapon and slot_weapon.resource.resource_path == res_path:
			_remove_equipped_slot(slot_weapon)
			return

	for i in range(equipped_accessories.size()):
		if equipped_accessories[i].resource.resource_path == res_path:
			var acc := equipped_accessories[i]
			acc.remove_passive_effects(self)
			acc.queue_free()
			equipped_accessories.remove_at(i)
			weapons_container_ui.update_equipment(self)
			return

	# Try inventory.
	for i in range(inventory.size()):
		if inventory[i].resource_path == res_path:
			inventory.remove_at(i)
			return


func _remove_equipped_slot(weapon: Weapon) -> void:
	weapon.remove_passive_effects(self)
	weapon.queue_free()
	if weapon == equipped_weapon:
		equipped_weapon = null
	elif weapon == equipped_ability:
		equipped_ability = null
	weapons_container_ui.update_equipment(self)


# ── cleanup ───────────────────────────────────────────────────────────────────
func _exit_tree() -> void:
	var my_peer_id := get_multiplayer_authority()
	Global.players.erase(my_peer_id)
