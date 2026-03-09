extends Area2D
## hurtbox.gd  —  SERVER-AUTHORITATIVE PLAYER DAMAGE
##
## Enemy-to-player contact damage runs through here.
##
## Unlike player-to-enemy hits (which are client-authoritative), player damage
## is server-authoritative — the server detects the overlap and tells the
## player's owning machine to reduce health.
##
## Why the asymmetry?
##   - Outgoing hits: client-authoritative = zero latency for the shooter.
##   - Incoming damage: server-authoritative = cheaters can't make themselves
##     invulnerable by never reporting hits.
##
## The server runs enemy physics, so it's the only peer that actually has
## enemies in motion.  Enemy body_entered signals naturally fire on the server.

@onready var timer:        Timer              = $Timer
@onready var audio_player: AudioStreamPlayer = $AudioPlayer


## Called when an enemy physics body enters the hurtbox Area2D.
func _on_body_entered(body: Node2D) -> void:
	if not body is Enemy:
		return

	# This signal fires on the server (enemy physics runs there).
	# Only the server should apply damage to prevent double-hits.
	if not multiplayer.is_server():
		return

	_hurt_owner_player(body.damage)
	timer.start()


## Poll for enemies still overlapping (handles sustained contact damage).
func _on_timer_timeout() -> void:
	if not multiplayer.is_server():
		return

	var overlapping := get_overlapping_bodies()
	if overlapping.is_empty():
		return

	for enemy in overlapping:
		_hurt_owner_player(enemy.damage)
	timer.start()


## Find which Player owns this hurtbox, then call hurt() on them via RPC.
func _hurt_owner_player(dmg: float) -> void:
	# The hurtbox is a child of the Player node.  Walk up to find it.
	var player: Player = _get_owner_player()
	if player == null:
		return

	audio_player.play()

	# Call hurt() as an RPC.  Because hurt() is declared with "authority"
	# in player.gd, the server (which IS the authority of every player node)
	# can call it.  `rpc()` without rpc_id broadcasts to all peers, but hurt()
	# internally checks `is_multiplayer_authority()` so only the owning peer
	# applies the visual health reduction.
	player.hurt.rpc(dmg)


func _get_owner_player() -> Player:
	var node: Node = self
	while node != null:
		if node is Player:
			return node as Player
		node = node.get_parent()
	return null
