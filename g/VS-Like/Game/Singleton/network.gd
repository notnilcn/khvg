extends Node
## Network.gd — autoloaded singleton that owns the WebSocket connection.
##
## Godot's multiplayer system is built around a "MultiplayerPeer" object.
## Think of it like a socket: you create a server-flavoured one or a
## client-flavoured one, hand it to `multiplayer.multiplayer_peer`, and from
## that point on Godot routes all @rpc calls and MultiplayerSynchronizer
## updates through it automatically.
##
## We use WebSocketMultiplayerPeer because the game exports to HTML5.
## Regular ENet (UDP) doesn't work in browsers; WebSockets do.

# ── signals ────────────────────────────────────────────────────────────────
## Emitted on the server when a new client finishes connecting.
signal player_joined(peer_id: int)
## Emitted on the server when a client disconnects.
signal player_left(peer_id: int)
## Emitted everywhere once our own connection is established.
signal connected_to_server()
## Emitted on a client if the connection to the server fails or drops.
signal connection_failed()

# ── constants ───────────────────────────────────────────────────────────────
## Port used for both hosting and joining. Change here to change everywhere.
const DEFAULT_PORT: int = 9999

## How generous we are when validating hit distance server-side.
## A client reports "I hit enemy X". The server checks whether the enemy
## was within this many units of that client's player at the time.
## Larger = more forgiving of lag; smaller = harder to cheat.
const HIT_VALIDATION_RANGE: float = 350.0

# ── state ───────────────────────────────────────────────────────────────────
## True once start_server() or join_server() has been called.
var is_active: bool = false


# ── public API ──────────────────────────────────────────────────────────────

## Call this to host a game. The host also plays (listen-server model).
## `port` is the TCP port clients will connect to.
## Returns true on success, false if the port couldn't be opened.
func start_server(port: int = DEFAULT_PORT) -> bool:
	var peer := WebSocketMultiplayerPeer.new()
	var err := peer.create_server(port)
	if err != OK:
		push_error("Network: failed to create server on port %d (error %d)" % [port, err])
		return false

	# Hand the peer to Godot's multiplayer system.
	multiplayer.multiplayer_peer = peer

	# Wire up signals so we know when clients come and go.
	# In Godot 4, peer_id 1 is ALWAYS the server. When the server connects
	# a client, that client gets a unique integer peer_id > 1.
	multiplayer.peer_connected.connect(_on_peer_connected)
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)

	is_active = true
	print("Network: server listening on port ", port)
	return true


## Call this to join an existing game.
## `address` is the server's IP or hostname (no protocol prefix needed here).
func join_server(address: String, port: int = DEFAULT_PORT) -> void:
	var peer := WebSocketMultiplayerPeer.new()
	# WebSocket URLs must start with ws:// (or wss:// for TLS).
	var url := "ws://%s:%d" % [address, port]
	var err := peer.create_client(url)
	if err != OK:
		push_error("Network: failed to connect to %s (error %d)" % [url, err])
		connection_failed.emit()
		return

	multiplayer.multiplayer_peer = peer

	# These fire on the CLIENT when the handshake with the server completes.
	multiplayer.connected_to_server.connect(_on_connected_to_server)
	multiplayer.connection_failed.connect(_on_connection_failed)
	multiplayer.server_disconnected.connect(_on_server_disconnected)

	is_active = true
	print("Network: connecting to ", url)


## Disconnect and clean up. Safe to call even if not connected.
func disconnect_from_game() -> void:
	if multiplayer.multiplayer_peer:
		multiplayer.multiplayer_peer.close()
		multiplayer.multiplayer_peer = null
	is_active = false


# ── helpers ─────────────────────────────────────────────────────────────────

## True if this instance is running as the server (peer_id == 1).
func is_server() -> bool:
	return multiplayer.is_server()


## This peer's unique network id. On the server this is 1.
## On clients this is whatever the server assigned them (some integer > 1).
func get_peer_id() -> int:
	return multiplayer.get_unique_id()


# ── private callbacks ────────────────────────────────────────────────────────

func _on_peer_connected(peer_id: int) -> void:
	# Only fires on the SERVER.
	print("Network: peer connected — id ", peer_id)
	player_joined.emit(peer_id)


func _on_peer_disconnected(peer_id: int) -> void:
	# Only fires on the SERVER.
	print("Network: peer disconnected — id ", peer_id)
	player_left.emit(peer_id)


func _on_connected_to_server() -> void:
	# Fires on the CLIENT once the WebSocket handshake succeeds.
	print("Network: connected to server, our peer_id = ", get_peer_id())
	connected_to_server.emit()


func _on_connection_failed() -> void:
	print("Network: connection failed")
	connection_failed.emit()
	is_active = false


func _on_server_disconnected() -> void:
	print("Network: server disconnected")
	is_active = false
