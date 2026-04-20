extends Node2D

# Overall Description
"""Handles spawning of enemies AND players.
## ENEMY SPAWNING
Only the server spawns enemies, but enemies need to EXIST on all peers so that clients can see them and weapon hit-detection works client-side. We solve this with Godot's MultiplayerSpawner node:
- You tell it which node to watch and which scenes are allowed.
- When the SERVER calls add_child() for a registered scene in that node, MultiplayerSpawner automatically tells all connected clients to create the same node with the same initialization data.
- When the server destroys a node (queue_free), clients auto-destroy theirs.
This means we don't need manual "please spawn this enemy" RPCs — the MultiplayerSpawner handles it under the hood.
## PLAYER SPAWNING
When a peer connects, the server broadcasts an RPC to create a Player node on ALL peers. Each peer sets the correct multiplayer_authority on that node so only the owning peer processes input for it.
"""

# Preloading and initializing
""" Preloads and initializes
```gdscript """
const DROP_SCENE:   PackedScene = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn")
const RES_FOOD:     ResDropFood = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Food/res_food.tres")
const ENEMY_SCENE:  PackedScene = preload("res://VS-Like/Characters/Enemy/enemy.tscn")
const PLAYER_SCENE: PackedScene = preload("res://VS-Like/Characters/Player/player.tscn") # ^2

const MIN_SPAWN_RADIUS: int    = 400
const MAX_ENEMIES:      int    = 300
const DUMMY_ENEMY_RES:  String = "res://VS-Like/Characters/Enemy/Types/Dummy/res_dummy.tres"
const GOBLIN_ENEMY_RES: String = "res://VS-Like/Characters/Enemy/Types/Goblin/res_goblin.tres"

# Maps friendly command names to ResWeapon resource paths.
const WEAPON_RES_PATHS: Dictionary = {
	"axe":      "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Axe/res_axe.tres",
	"arrow":    "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/res_arrow.tres",
	"bible":    "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Bible/res_bible.tres",
	"boots":    "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Boots/res_boots.tres",
	"parchment":"res://VS-Like/Characters/Player/Utilities/Weapons/Types/Parchment/res_parchment.tres",
	"ring":     "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Ring/res_ring.tres",
	"sinebow":  "res://VS-Like/Characters/Player/Utilities/Weapons/Types/SineArrow/res_sine_arrow.tres",
}

@export var all_res_enemy: Array[ResEnemy]
@onready var spawn_timer: Timer         = $SpawnTimer
@onready var _vbox:       VBoxContainer = $"CanvasLayer/MarginContainer/VBoxContainer"

var enemies_count:    int  = 0    : set = _set_enemies_count
var minutes:          int        : set = _set_minutes
var seconds:          int        : set = _set_seconds
var spawning_enabled: bool = true

var _next_enemy_id:        int  = 0
var _spawn_position_index: int  = 0
var _initialized:          bool = false
var _chat_feedback:        Label

# Array of every live enemy on the SERVER.
# Used to send a catch-up list to clients that join after enemies already exist.
# Entries are removed when the enemy is despawned via `_rpc_despawn_enemy`.
var _active_enemy_data: Array[Dictionary] = []


const PLAYER_SPAWN_POSITIONS: Array = [
	Vector2(0, 0),
	Vector2(200, 0),
	Vector2(-200, 0),
	Vector2(0, 200),
]
"""
```
"""

## Admin management.
"""The host (peer_id == 1) is always implicitly admin. Only the host can grant or revoke admin; admins cannot promote others.
Empty admin_password string means disabled. Set with /setpass before players join; players claim admin with /adminpass.
```gdscript """
var _admins: Array[int] = []
var _admin_password: String = ""
"""
```
"""

# `_ready()`
"""The `_ready()` function is a special godot function that automatically runs when all of the nodes' children are added to the scene. Google it if you're curious.
```gdscript """
func _ready() -> void:
	# Guard against _ready() being called a second time (e.g. scene reload quirk).
	if _initialized:
		return
	_initialized = true

	# ── server setup ─────────────────────────────────────────────────────────
	if multiplayer.is_server():
		# Dedicated server — no player node for peer 1.

		# If any clients were ALREADY connected before this _ready() ran
		# (happens when the host dies and the scene reloads while a client
		# is still in the game), notify those clients to clean up stale nodes
		# and re-join the fresh scene.
		for peer_id in multiplayer.get_peers():
			_notify_scene_reload.rpc_id(peer_id)

		# Clean up player nodes when a peer disconnects.
		Network.player_left.connect(_on_player_left)
	else:
		# Client-side: tell the server we are in the scene and ready to be spawned.
		# rpc_id(1, ...) sends ONLY to peer 1 (the server).
		print("Spawner: client ready, requesting player spawn from server")
		_request_player_spawn.rpc_id(1)

	_setup_chat_console()
"""
```
"""
# chat console
## Player chat
#### `_setup_chat_console()`
"""
```gdscript """
func _setup_chat_console() -> void:
	_chat_feedback = Label.new()
	_chat_feedback.add_theme_font_size_override("font_size", 8)
	_vbox.add_child(_chat_feedback)

	var input := LineEdit.new()
	input.placeholder_text = "press T to chat"
	input.add_theme_font_size_override("font_size", 8)
	input.text_submitted.connect(_on_command_submitted)
	# Release game focus when the field is done so movement keys work again.
	input.text_submitted.connect(func(_s: String) -> void: input.release_focus())
	# After any focus loss (Enter, Escape, click-away), clear all GUI focus so
	# _unhandled_key_input can receive the next T keypress.
	input.focus_exited.connect(func() -> void: get_viewport().gui_release_focus())
	_vbox.add_child(input)
"""
```
"""

#### `_unhandled_key_input(event`
"""
```gdscript """
func _unhandled_key_input(event: InputEvent) -> void:
	if not (event is InputEventKey) or not event.pressed or event.echo:
		return
	var ev := event as InputEventKey
	var input := _vbox.get_child(_vbox.get_child_count() - 1) as LineEdit
	if input == null or input.has_focus():
		return
	if ev.keycode == KEY_T:
		input.grab_focus()
		get_viewport().set_input_as_handled()
"""
```
"""

#### `_on_command_submitted(raw)`
"""
```gdscript """
func _on_command_submitted(raw: String) -> void:
	var text := raw.strip_edges()
	# Clear the input field regardless of whether the command is valid.
	# The LineEdit emits text_submitted before the field is cleared, so we
	# grab it via get_node lookup using the signal's implicit owner reference.
	# Easier: just find the LineEdit child we added last.
	var input := _vbox.get_child(_vbox.get_child_count() - 1) as LineEdit
	if input:
		input.clear()

	if text.is_empty():
		return
	_parse_command(text)
"""
```
"""

#### `_parse_command(cmd)`
"""
```gdscript """
func _parse_command(cmd: String) -> void:
	var parts := cmd.split(" ", false)
	var verb: String = (parts[0] as String).to_lower()
	var args: Array  = parts.slice(1)
	match verb:
		"/spawner", "/give", "/remove", "/admin", "/unadmin", "/setpass", "/spawn", "/chest":
			# Route all admin commands through the server so it can validate
			# permissions.  If we ARE the server, execute directly.
			if multiplayer.get_unique_id() == 1:
				_execute_admin_cmd(1, verb, args)
			else:
				_rpc_admin_cmd.rpc_id(1, verb, args)
		"/stats", "/inv":
			# Read-only self-inspection — anyone can run these, no admin needed.
			# Execute locally (no server round-trip required).
			_cmd_selfinfo(verb)
		"/adminpass":
			# Anyone can attempt this — the server validates the password.
			var password: String = args[0] as String if not args.is_empty() else ""
			if multiplayer.get_unique_id() == 1:
				_try_admin_password(1, password)
			else:
				_rpc_try_admin_password.rpc_id(1, password)
		_:
			_show_feedback("Unknown command. Try /stats, /inv, /spawner, /give, /remove, /spawn, /chest, /admin, /unadmin, /setpass, /adminpass")
"""
```
"""

#### `_cmd_selfinfo(verb)`
"""
/stats  — print your current RPG stats to the chat feedback label.
/inv    — print your equipped items and inventory to the chat feedback label.
Both run locally (no server round-trip; no admin required).
```gdscript """
func _cmd_selfinfo(verb: String) -> void:
	var player := Global.get_local_player()
	if player == null:
		_show_feedback("No local player found")
		return

	var msg: String
	match verb:
		"/stats":
			msg = (
				"STR %d  WIS %d  DEX %d  DEF %d  VIT %d\n"
				+ "HP %d/%d   Slots: %d acc"
			) % [
				player.strength, player.wisdom, player.dexterity,
				player.defense,  player.vitality,
				int(player.health), int(player.max_health),
				player.get_accessory_slot_count(),
			]
		"/inv":
			var wpn  := player.equipped_weapon.resource.item_name  if player.equipped_weapon  else "—"
			var abl  := player.equipped_ability.resource.item_name if player.equipped_ability else "—"
			var arm  := "—"
			var accs : Array[String] = []
			for acc in player.equipped_accessories:
				accs.append(acc.resource.item_name)
			var acc_str := ", ".join(accs) if not accs.is_empty() else "—"
			var inv_names : Array[String] = []
			for res in player.inventory:
				inv_names.append(res.item_name)
			var inv_str := ", ".join(inv_names) if not inv_names.is_empty() else "empty"
			msg = (
				"Wpn:%s  Abl:%s  Arm:%s  Acc:%s\nInv(%d/%d): %s"
			) % [wpn, abl, arm, acc_str,
				 player.inventory.size(), player.inventory_max, inv_str]
		_:
			return
	_show_feedback(msg)
"""
```
"""

#### `_rpc_try_admin_password(password)`
"""/adminpass [password] — any player: claim admin if the password matches
Separate RPC so non-admins can reach the server without going through
`_execute_admin_cmd` (which would block them at the `_is_admin()` check).
```gdscript """
@rpc("any_peer", "call_remote", "reliable")
func _rpc_try_admin_password(password: String) -> void:
	_try_admin_password(multiplayer.get_remote_sender_id(), password)


func _try_admin_password(sender: int, password: String) -> void:
	if _admin_password.is_empty():
		_send_feedback(sender, "No admin password is set")
		return
	if password != _admin_password:
		_send_feedback(sender, "Incorrect password")
		return
	if _is_admin(sender):
		_send_feedback(sender, "You are already an admin")
		return
	_admins.append(sender)
	_send_feedback(sender, "Admin granted")
"""
```
"""

#### `_show_feedback(msg)`
"""
```gdscript """
func _show_feedback(msg: String) -> void:
	if _chat_feedback == null:
		return
	_chat_feedback.text = msg
	get_tree().create_timer(3.0).timeout.connect(
		func() -> void:
			if is_instance_valid(_chat_feedback):
				_chat_feedback.text = ""
	)
"""
```
"""


## Admin chat.
#### `_rpc_admin_cmd(verb, args)`
"""The server validates admin status before executing; clients cannot bypass this by calling `_execute_admin_cmd` directly because that function only runs server-side.
"""
"""
```gdscript """
@rpc("any_peer", "call_remote", "reliable")
func _rpc_admin_cmd(verb: String, args: Array) -> void:
	_execute_admin_cmd(multiplayer.get_remote_sender_id(), verb, args)
"""
```
"""

#### `_rpc_feedback(msg)`
"""Server → client RPC to deliver command feedback.
```gdscript """
@rpc("authority", "call_remote", "reliable")
func _rpc_feedback(msg: String) -> void:
	_show_feedback(msg)
"""
```
"""

#### `_send_feedback(peer_id, msg)`
"""Sends feedback to the given peer (or locally if peer_id == 1).
```gdscript """
func _send_feedback(peer_id: int, msg: String) -> void:
	if peer_id == 1:
		_show_feedback(msg)
	else:
		_rpc_feedback.rpc_id(peer_id, msg)
"""
```
"""

#### `_is_admin(peer_id)`
"""Returns true if the given peer_id has admin privileges.
```gdscript """
func _is_admin(peer_id: int) -> bool:
	return peer_id == 1 or peer_id in _admins
"""
```
"""

#### `_execute_admin_cmd(sender)`
"""Validates admin status then dispatches to the appropriate command handler.
Always runs on the server.
```gdscript """
func _execute_admin_cmd(sender: int, verb: String, args: Array) -> void:
	if not _is_admin(sender):
		_send_feedback(sender, "Permission denied: you are not an admin")
		return
	match verb:
		"/spawner": _cmd_spawner(sender, args)
		"/give":    _cmd_give(sender, args)
		"/remove":  _cmd_remove(sender, args)
		"/admin":   _cmd_admin(sender, args)
		"/unadmin": _cmd_unadmin(sender, args)
		"/setpass": _cmd_setpass(sender, args)
		"/spawn":   _cmd_spawn(sender, args)
		"/chest":   _cmd_chest(sender, args)
"""
```
"""

#### `_cmd_spawner(sender, args)`
"""/spawner on|off
```gdscript """
func _cmd_spawner(sender: int, args: Array) -> void:
	if args.is_empty():
		_send_feedback(sender, "Usage: /spawner on|off")
		return
	match (args[0] as String).to_lower():
		"on":
			spawning_enabled = true
			_send_feedback(sender, "Enemy spawning enabled")
		"off":
			spawning_enabled = false
			_send_feedback(sender, "Enemy spawning disabled")
		_:
			_send_feedback(sender, "Usage: /spawner on|off")
"""
```
"""

#### `_cmd_give(sender, args)`
"""/give [weapon] [peer_id] — give a weapon to self or another peer
```gdscript """
func _cmd_give(sender: int, args: Array) -> void:
	if args.is_empty():
		_send_feedback(sender, "Usage: /give <weapon> [peer_id]\nWeapons: " + ", ".join(WEAPON_RES_PATHS.keys()))
		return

	var weapon_name: String = (args[0] as String).to_lower()
	if not WEAPON_RES_PATHS.has(weapon_name):
		_send_feedback(sender, "Unknown weapon '%s'. Options: %s" % [weapon_name, ", ".join(WEAPON_RES_PATHS.keys())])
		return

	var res_path: String  = WEAPON_RES_PATHS[weapon_name]
	var target_id: int    = int(args[1] as String) if args.size() >= 2 else sender
	var target := Global.players.get(target_id, null) as Player
	if target == null:
		_send_feedback(sender, "No player found with peer_id %d" % target_id)
		return
	target.rpc_give_weapon.rpc_id(target_id, res_path)
	_send_feedback(sender, "Gave %s to peer %d" % [weapon_name, target_id])
"""
```
"""

#### `_cmd_remove(sender, args)`
"""/remove [weapon] [peer_id] — remove a weapon from self or another peer
```gdscript """
func _cmd_remove(sender: int, args: Array) -> void:
	if args.is_empty():
		_send_feedback(sender, "Usage: /remove <weapon> [peer_id]\nWeapons: " + ", ".join(WEAPON_RES_PATHS.keys()))
		return

	var weapon_name: String = (args[0] as String).to_lower()
	if not WEAPON_RES_PATHS.has(weapon_name):
		_send_feedback(sender, "Unknown weapon '%s'. Options: %s" % [weapon_name, ", ".join(WEAPON_RES_PATHS.keys())])
		return

	var res_path: String = WEAPON_RES_PATHS[weapon_name]
	var target_id: int   = int(args[1] as String) if args.size() >= 2 else sender
	var target := Global.players.get(target_id, null) as Player
	if target == null:
		_send_feedback(sender, "No player found with peer_id %d" % target_id)
		return
	target.rpc_remove_weapon.rpc_id(target_id, res_path)
	_send_feedback(sender, "Removed %s from peer %d" % [weapon_name, target_id])
"""
```
"""

#### `_cmd_admin(sender, args`
"""/admin [peer_id] — host only: grant admin to a connected player
```gdscript """
func _cmd_admin(sender: int, args: Array) -> void:
	if sender != 1:
		_send_feedback(sender, "Only the host can grant admin")
		return
	if args.is_empty():
		_send_feedback(sender, "Usage: /admin <peer_id>")
		return
	var target_id: int = int(args[0] as String)
	if target_id == 1:
		_send_feedback(sender, "The host is always admin")
		return
	if not Global.players.has(target_id):
		_send_feedback(sender, "No player found with peer_id %d" % target_id)
		return
	if target_id in _admins:
		_send_feedback(sender, "Peer %d is already an admin" % target_id)
		return
	_admins.append(target_id)
	_send_feedback(sender, "Granted admin to peer %d" % target_id)
	_send_feedback(target_id, "You have been granted admin privileges")
"""
```
"""

#### `_cmd_unadmin(sender, args)`
"""/unadmin [peer_id] — host only: revoke admin from a player
```gdscript """
func _cmd_unadmin(sender: int, args: Array) -> void:
	if sender != 1:
		_send_feedback(sender, "Only the host can revoke admin")
		return
	if args.is_empty():
		_send_feedback(sender, "Usage: /unadmin <peer_id>")
		return
	var target_id: int = int(args[0] as String)
	if target_id == 1:
		_send_feedback(sender, "Cannot remove admin from the host")
		return
	if not target_id in _admins:
		_send_feedback(sender, "Peer %d is not an admin" % target_id)
		return
	_admins.erase(target_id)
	_send_feedback(sender, "Revoked admin from peer %d" % target_id)
	_send_feedback(target_id, "Your admin privileges have been revoked")
"""
```
"""

#### `_cmd_setpass(sender, args)`
"""/setpass [password] — host only: set (or clear) the admin password
```gdscript """
func _cmd_setpass(sender: int, args: Array) -> void:
	if sender != 1:
		_send_feedback(sender, "Only the host can set the admin password")
		return
	if args.is_empty():
		_admin_password = ""
		_send_feedback(sender, "Admin password cleared")
	else:
		_admin_password = args[0] as String
		_send_feedback(sender, "Admin password set")
"""
```
"""

#### `_cmd_spawn(sender, args)`
"""/spawn [x y] — spawn a stationary immortal dummy at the given world position (default 0,0)
```gdscript """
func _cmd_spawn(sender: int, args: Array) -> void:
	if args.size() == 1:
		_send_feedback(sender, "Usage: /spawn [x y]")
		return
	var pos := Vector2.ZERO
	if args.size() >= 2:
		pos = Vector2(float(args[0] as String), float(args[1] as String))
	var res := load(DUMMY_ENEMY_RES) as ResEnemy
	_create_enemy(pos, res, false, true)
	_send_feedback(sender, "Spawned dummy at (%d, %d)" % [int(pos.x), int(pos.y)])
"""
```
"""

#### `_cmd_chest(sender, args)`
"""/chest [x y] — spawn an elite enemy that is guaranteed to drop a chest when killed.
Uses a random enemy from the current pool, or a goblin if the pool is empty.
If no position is given, spawns near the first available player.
```gdscript """
func _cmd_chest(sender: int, args: Array) -> void:
	if args.size() == 1:
		_send_feedback(sender, "Usage: /chest [x y]")
		return

	var pos: Vector2
	if args.size() >= 2:
		pos = Vector2(float(args[0] as String), float(args[1] as String))
	else:
		var ref_player := _get_reference_player()
		if ref_player == null:
			_send_feedback(sender, "No players in the world to spawn near")
			return
		pos = _get_random_spawn_pos(ref_player.global_position, MIN_SPAWN_RADIUS)

	# Prefer a random enemy from the current pool; fall back to goblin.
	var res: ResEnemy = _get_enemy() if not all_res_enemy.is_empty() else load(GOBLIN_ENEMY_RES)
	_create_enemy(pos, res, true)   # is_elite = true → always drops a chest
	_send_feedback(sender, "Spawned elite enemy (chest drop) at (%d, %d)" % [int(pos.x), int(pos.y)])
"""
```
"""

# RPCs
#### `_notify_scene_reload()`
"""Sent by the server to already-connected clients when the server scene reloads (e.g. the host died and respawned).  The client removes all stale player nodes from the previous game session and re-requests a fresh spawn. @rpc("authority")    — only the server sends this. @rpc("call_remote")  — only clients run it (server handles its own state).
```gdscript """
@rpc("authority", "call_remote", "reliable")
func _notify_scene_reload() -> void:
	print("Spawner: server scene reloaded — clearing stale nodes and rejoining")

	# Remove every player node we were tracking from the old session.
	for peer_id: int in Global.players.keys():
		var player: Player = Global.players[peer_id]
		if is_instance_valid(player):
			player.queue_free()
	Global.players.clear()

	# Also remove any enemy nodes that still exist under this spawner from the
	# previous session (they're now orphaned on the client's side).
	for child in get_children():
		if child is Enemy:
			child.queue_free()

	# Re-join the server's fresh game session.
	_request_player_spawn.rpc_id(1)
"""
```
"""

#### `_request_player_spawn()`
"""Client calls this to the server once the client's game scene is fully loaded. The server then spawns a player node for that client on all peers. @rpc("any_peer")  — clients (non-servers) need to call this. @rpc("call_remote") — runs only on the server, not on the calling client.
```gdscript """
@rpc("any_peer", "call_remote", "reliable")
func _request_player_spawn() -> void:
	# This runs on the SERVER.
	var new_peer_id := multiplayer.get_remote_sender_id()
	print("Spawner: server received spawn request from peer ", new_peer_id)

	# ── catch-up: send all already-existing players to the new client ─────────
	# The host's player (and anyone else already in the game) was spawned
	# before this client connected, so the client missed those RPCs entirely.
	# We send them now, targeting only the new client via rpc_id.
	for existing_peer_id: int in Global.players.keys():
		var existing_player: Player = Global.players[existing_peer_id]
		print("Spawner: sending existing player %d to new peer %d" % [existing_peer_id, new_peer_id])
		_create_player_node.rpc_id(new_peer_id, existing_peer_id, existing_player.global_position)

	# ── now spawn the new player for EVERYONE (broadcast) ─────────────────────
	_spawn_player_for_peer(new_peer_id)

	# ── catch-up: send all currently-alive enemies to the new client ──────────
	# MultiplayerSpawner's built-in catch-up fires at peer_connected time —
	# BEFORE the client's game scene is loaded, so those packets are dropped.
	# We solve this by tracking enemy data ourselves and manually replaying it
	# when we know the client's scene is ready (i.e. right now).
	print("Spawner: sending %d existing enemies to peer %d" % [_active_enemy_data.size(), new_peer_id])
	for data: Dictionary in _active_enemy_data:
		_rpc_spawn_enemy.rpc_id(new_peer_id, data)
"""
```
"""

#### `_on_player_left(peer_id)`
"""
```gdscript """
func _on_player_left(peer_id: int) -> void:
	_remove_player.rpc(peer_id)
	_admins.erase(peer_id)
"""
```
"""

#### `_spawn_player_for_peer(peer_id)`
"""
```gdscript """
func _spawn_player_for_peer(peer_id: int) -> void:
	var pos: Vector2 = PLAYER_SPAWN_POSITIONS[
		_spawn_position_index % PLAYER_SPAWN_POSITIONS.size()
	]
	_spawn_position_index += 1
	print("Spawner: spawning player for peer ", peer_id, " at ", pos)
	_create_player_node.rpc(peer_id, pos)
"""
```
"""


#### `_create_player_node(peer_id, spawn_pos)`
"""Runs on ALL peers. Creates the player node with the correct authority so only the owning peer processes input, while everyone can see the character.
```gdscript """
@rpc("authority", "call_local", "reliable")
func _create_player_node(peer_id: int, spawn_pos: Vector2) -> void:
	# Guard: if this peer already has a node for this player, skip.
	# This can happen if the catch-up RPC and the broadcast RPC both arrive.
	if get_node_or_null("Player_%d" % peer_id) != null:
		return
	var player: Player = PLAYER_SCENE.instantiate()
	player.name                  = "Player_%d" % peer_id
	player.set_multiplayer_authority(peer_id)
	player.global_position       = spawn_pos
	add_child(player)
"""
```
"""

#### `_remove_player(peer_id)`
"""
```gdscript """
@rpc("authority", "call_local", "reliable")
func _remove_player(peer_id: int) -> void:
	var node := get_node_or_null("Player_%d" % peer_id)
	if node:
		node.queue_free()
"""
```
"""

# Enemy spawning

#### `_spawn_single_enemy(is_elite)`
"""
```gdscript """
func _spawn_single_enemy(is_elite: bool = false) -> void:
	var ref_player := _get_reference_player()
	if ref_player == null:
		return
	var spawn_pos: Vector2 = _get_random_spawn_pos(ref_player.global_position, MIN_SPAWN_RADIUS)
	_create_enemy(spawn_pos, _get_enemy(), is_elite)


func _spawn_circle_pattern(enemies_to_spawn: int) -> void:
	var ref_player := _get_reference_player()
	if ref_player == null:
		return
	var player_pos: Vector2 = ref_player.global_position
	var angle_step: float   = TAU / enemies_to_spawn
	for i in range(enemies_to_spawn):
		var angle:     float   = i * angle_step
		var spawn_pos: Vector2 = player_pos + Vector2(cos(angle), sin(angle)) * MIN_SPAWN_RADIUS
		_create_enemy(spawn_pos, all_res_enemy[0])
"""
```
"""

## SERVER ONLY: spawn an enemy and replicate it to all connected clients.
"""We no longer use MultiplayerSpawner because its built-in late-join catch-up fires at peer_connected time — before the client's game scene exists — so the packets are silently dropped.  Instead we use an explicit RPC that only fires when we KNOW the client is ready (see _request_player_spawn).
"""
#### `_create_enemy(spawn_pos,res_enemy,is_elite,immortal)`
"""
```gdscript """
func _create_enemy(spawn_pos: Vector2, res_enemy: ResEnemy, is_elite: bool = false, immortal: bool = false) -> void:
	var eid := _next_enemy_id
	_next_enemy_id += 1

	var data := {
		"res_path": res_enemy.resource_path,
		"pos":      spawn_pos,
		"is_elite": is_elite,
		"immortal": immortal,
		"eid":      eid,
	}

	# Track for late-join catch-up (sent inside _request_player_spawn).
	_active_enemy_data.append(data)

	# Broadcast to server (call_local) and all currently-connected clients.
	_rpc_spawn_enemy.rpc(data)
"""
```
"""

# Sync

"""Runs on ALL peers (server via call_local, clients via network). Creates, names, and adds the enemy node to this spawner.
@rpc("authority")   — only the server sends this.
@rpc("call_local")  — also runs on the server immediately (no extra step needed).
@rpc("reliable")    — enemy appearance must not be lost.
"""
#### `_rpc_spawn_enemy(data)`
"""
```gdscript """
@rpc("authority", "call_local", "reliable")
func _rpc_spawn_enemy(data: Dictionary) -> void:
	var enemy: Enemy       = ENEMY_SCENE.instantiate()
	enemy.res_enemy        = load(data["res_path"]) as ResEnemy
	enemy.is_elite         = data["is_elite"]
	enemy.immortal         = data.get("immortal", false)
	enemy.global_position  = data["pos"]
	enemy.enemy_id         = data["eid"]
	# Deterministic name required so _rpc_despawn_enemy can find it by name on
	# all peers, and so the MultiplayerSynchronizer path is the same everywhere.
	enemy.name             = "Enemy_%d" % data["eid"]

	add_child(enemy)

	# Server-only bookkeeping: count enemies and wire up the despawn callback.
	if multiplayer.is_server():
		enemies_count += 1
		enemy.died.connect(enemy_died)
		# When the enemy calls die(), it emits request_despawn so we can
		# broadcast the removal to all peers (see _rpc_despawn_enemy below).
		enemy.request_despawn.connect(_on_enemy_request_despawn)
"""
```
"""

#### `_on_enemy_request_despawn(eid)`
"""Called on the server when an enemy's die() method fires. Broadcasts removal to ALL peers so every machine frees the node.
```gdscript """
func _on_enemy_request_despawn(eid: int) -> void:
	_rpc_despawn_enemy.rpc(eid)
"""
```
"""

#### `_rpc_spawn_drop(res_path,pos)`
"""SERVER ONLY helper called by enemy.spawn_drop(). Broadcasts a drop spawn to ALL peers so every player can collect it independently on their own machine (each gets XP / heal / chest). Drops are lightweight Area2D nodes — having one copy per client is fine. Each peer's copy is collected independently and freed locally on pickup.
```gdscript """
@rpc("authority", "call_local", "reliable")
func _rpc_spawn_drop(res_path: String, pos: Vector2) -> void:
	var drop: Drop       = DROP_SCENE.instantiate()
	drop.resource        = load(res_path) as ResDrop
	drop.global_position = pos
	# Add to the map scene root (same parent as the original single-player code).
	get_tree().current_scene.call_deferred("add_child", drop)
"""
```
"""

#### `_rpc_despawn_enemy(eid)`
"""Runs on ALL peers (server via call_local, clients via network). Finds the enemy node by name and frees it.
@rpc("authority")   — only the server sends this.
@rpc("call_local")  — also frees it on the server.
@rpc("reliable")    — enemy removal must not be lost.
```gdscript """
@rpc("authority", "call_local", "reliable")
func _rpc_despawn_enemy(eid: int) -> void:
	if multiplayer.is_server():
		# Remove from the catch-up list so late-joining clients don't get a
		# ghost entry for an enemy that's already dead.
		_active_enemy_data = _active_enemy_data.filter(
			func(d: Dictionary) -> bool: return d["eid"] != eid
		)

	var enemy := get_node_or_null("Enemy_%d" % eid)
	if enemy:
		enemy.queue_free()
"""
```
"""

#### `_create_enemy_from_data(data)`
"""Creates and configures an enemy from spawn data.  Called from `_rpc_spawn_enemy`.
Kept as a separate helper so `_request_player_spawn` can reuse the same logic.
(Previously this was the MultiplayerSpawner's `spawn_function` callback.)
```gdscript """
func _create_enemy_from_data(data: Dictionary) -> Enemy:
	var enemy: Enemy       = ENEMY_SCENE.instantiate()
	enemy.res_enemy        = load(data["res_path"]) as ResEnemy
	enemy.is_elite         = data["is_elite"]
	enemy.immortal         = data.get("immortal", false)
	enemy.global_position  = data["pos"]
	enemy.enemy_id         = data["eid"]
	enemy.name             = "Enemy_%d" % data["eid"]
	return enemy
"""
```
"""

# helpers
#### `_get_reference_player()`
"""
```gdscript """
func _get_reference_player() -> Player:
	if Global.players.is_empty():
		return null
	return Global.players.values()[0]
"""
```
"""

#### `_get_random_spawn_pos(global_pos, radius_distance)`
"""
```gdscript """
func _get_random_spawn_pos(global_pos: Vector2, radius_distance: int) -> Vector2:
	var angle    := randf() * TAU
	var distance := radius_distance + randf() * 100
	return global_pos + Vector2(cos(angle), sin(angle)) * distance
"""
```
"""

#### `_get_enemy()`
"""
```gdscript """
func _get_enemy() -> ResEnemy:
	return all_res_enemy.pick_random()
"""
```
"""

#### `enemy_died()`
"""
```gdscript """
func enemy_died() -> void:
	enemies_count -= 1
"""
```
"""

#### `_add_enemy_type(minute)`
"""
```gdscript """
func _add_enemy_type(minute: int) -> void:
	match minute:
		1:
			all_res_enemy.append(load("res://VS-Like/Characters/Enemy/Types/Zombie/res_zombie.tres"))
			spawn_timer.wait_time = 0.75
		2:
			all_res_enemy.append(load("res://VS-Like/Characters/Enemy/Types/Bat/res_bat.tres"))
			spawn_timer.wait_time = 0.5
		3:
			all_res_enemy.append(load("res://VS-Like/Characters/Enemy/Types/Cyclops/res_cyclops.tres"))
			spawn_timer.wait_time = 0.35
"""
```
"""

# setters
#### `_set_enemies_count(value)`
"""
```gdscript """
func _set_enemies_count(value: int) -> void:
	enemies_count = value
	%EnemiesCountLabel.text = "ENEMIES: " + str(value)
"""
```
"""

#### `_set_minutes(value)`
"""
```gdscript """
func _set_minutes(value: int) -> void:
	minutes = value
	if multiplayer.is_server():
		_add_enemy_type(minutes)
	%Minutes.text = _set_digits(minutes)
"""
```
"""

#### `_set_seconds(value)`
"""
```gdscript """
func _set_seconds(value: int) -> void:
	seconds = value
	if seconds >= 60:
		seconds  = 0
		minutes += 1
	%Seconds.text = _set_digits(seconds)
"""
```
"""

#### `_set_digits(n)`
"""
```gdscript """
func _set_digits(n: int) -> String:
	return ("0" if str(abs(n)).length() == 1 else "") + str(n)
"""
```
"""

# timer callbacks

#### `func _on_timer_timeout()`
"""
```gdscript """
func _on_timer_timeout() -> void:
	seconds += 1
"""
```
"""


#### `func _on_spawn_timer_timeout()`
"""
```gdscript """
func _on_spawn_timer_timeout() -> void:
	if not multiplayer.is_server() or not spawning_enabled:
		return
	if enemies_count < MAX_ENEMIES:
		_spawn_single_enemy()
"""
```
"""


#### `func _on_pattern_timer_timeout()`
"""
```gdscript """
func _on_pattern_timer_timeout() -> void:
	if not multiplayer.is_server() or not spawning_enabled:
		return
	if enemies_count < MAX_ENEMIES:
		_spawn_circle_pattern(min(40, MAX_ENEMIES - enemies_count))
"""
```
"""


#### `func _on_elite_timer_timeout()`
"""
```gdscript """
func _on_elite_timer_timeout() -> void:
	if not multiplayer.is_server() or not spawning_enabled:
		return
	_spawn_single_enemy(true)
"""
```
"""


#### `func _on_food_timer_timeout()`
"""
```gdscript """
func _on_food_timer_timeout() -> void:
	if not multiplayer.is_server():
		return
	var ref_player := _get_reference_player()
	if ref_player == null:
		return
	var spawn_pos: Vector2 = _get_random_spawn_pos(
		ref_player.global_position, randi_range(MIN_SPAWN_RADIUS, 600)
	)
	var food: Drop       = DROP_SCENE.instantiate()
	food.resource        = RES_FOOD
	food.global_position = spawn_pos
	add_child(food)
"""
```
"""
