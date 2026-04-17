extends Node

# Overall Description
"""Network.gd — autoloaded singleton that owns the WebSocket connection.
Godot's multiplayer system is built around a "MultiplayerPeer" object. Think of it like a socket: you create a server-flavoured one or a client-flavoured one, hand it to `multiplayer.multiplayer_peer`, and from that point on Godot routes all @rpc calls and MultiplayerSynchronizer updates through it automatically.

We use WebSocketMultiplayerPeer because the game exports to HTML5. Regular ENet (UDP) doesn't work in browsers; WebSockets do.
"""
# signals
 ## ![signals](<001 Things in godot#Signals>)
#### `player_joined()`
"""Emitted on the server when a new client finishes connecting.
```gdscript """
signal player_joined(peer_id: int)
"""
```
"""

#### `player_left()`
"""Emitted on the server when a client disconnects.
```gdscript """
signal player_left(peer_id: int)
"""
```
"""

#### `connected_to_server()`
"""Emitted everywhere once our own connection is established.
```gdscript """
signal connected_to_server()
"""
```
"""

#### `connection_failed()`
"""Emitted on a client if the connection to the server fails or drops.
```gdscript """
signal connection_failed()
"""
```
"""

# constants
#### `DEFAULT_PORT`
"""Port used for both hosting and joining. Change here to change everywhere.
```gdscript """
const DEFAULT_PORT: int = 9999
"""
```
""" 

#### `HIT_VALIDATION_RANGE`
"""How generous we are when validating hit distance server-side.
A client reports "I hit enemy X". The server checks whether the enemy was within this many
units of that client's player at the time. Larger = more forgiving of lag; smaller = harder to cheat.
```gdscript """
const HIT_VALIDATION_RANGE: float = 350.0
"""
```
"""

# states
#### `is_active`
"""True once start_server() or join_server() has been called.
```gdscript """
var is_active: bool = false
"""
```
"""

# public API
#### `start_server(port)`
""" Call this to host a game. The host also plays (listen-server model).
`port` is the TCP port clients will connect to.
Returns true on success, false if the port couldnt be opened.
```gdscript """

func start_server(port: int = DEFAULT_PORT) -> bool:
	var peer := WebSocketMultiplayerPeer.new()
	
	# Specify that the websocket peer is a server and return error if error.
	var err := peer.create_server(port)
	if err != OK:
		push_error("Network: failed to create server on port %d (error %d)" % [port, err])
		return false

	# Hand the server to Godot's multiplayer system.
	multiplayer.multiplayer_peer = peer

	# Define which signals to emit whenever a client interracts with the server so we know when clients come and go.
	# In Godot 4, peer_id 1 is ALWAYS the server. When the server connects
	# a client, that client gets a unique integer peer_id > 1.
	multiplayer.peer_connected.connect(_on_peer_connected)
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)


	is_active = true
	print("Network: server listening on port ", port)
	return true
"""
```
"""

#### `join_server(address, port)`
"""Call this to join an existing game.
`address` is the server's IP or hostname (no protocol prefix needed here).
```gdscript """
func join_server(address: String, port: int = DEFAULT_PORT) -> void:
	var peer := WebSocketMultiplayerPeer.new()
	# WebSocket URLs must start with ws:// (or wss:// for TLS).
	var url := "ws://%s:%d" % [address, port]
	
	# Specify that the websocket peer is a client and return error if unable to connect.
	var err := peer.create_client(url)
	if err != OK:
		push_error("Network: failed to connect to %s (error %d)" % [url, err])
		connection_failed.emit()
		return

	# Hand the server to Godot's multiplayer system.
	multiplayer.multiplayer_peer = peer

	# Define which signals to emit whenever the client interracts with the server.
	# These fire on the CLIENT when the handshake with the server completes.
	multiplayer.connected_to_server.connect(_on_connected_to_server)
	multiplayer.connection_failed.connect(_on_connection_failed)
	multiplayer.server_disconnected.connect(_on_server_disconnected)

	is_active = true
	print("Network: connecting to ", url)
"""
```
"""

#### `disconnect_from_game()`
"""Disconnect and clean up. Safe to call even if not connected.
```gdscript """
func disconnect_from_game() -> void:
	if multiplayer.multiplayer_peer:
		multiplayer.multiplayer_peer.close()
		multiplayer.multiplayer_peer = null
	is_active = false
"""
```
"""

# helpers
#### `is_server()`
"""True if this instance is running as the server (peer_id == 1).
```gdscript """
func is_server() -> bool:
	return multiplayer.is_server()
"""
```
"""

#### `get_peer_id()`
"""This peer's unique network id. On the server this is 1.
On clients this is whatever the server assigned them (some integer > 1).
```gdscript """
func get_peer_id() -> int:
	return multiplayer.get_unique_id()
"""
```
"""

# private callbacks
#### `_on_peer_connected(peer_id)`
"""Only fires on the SERVER.
```gdscript """
func _on_peer_connected(peer_id: int) -> void:
	print("Network: peer connected — id ", peer_id)
	player_joined.emit(peer_id)
"""
```
"""

#### `_on_peer_disconnected(peer_id)`
"""Only fires on the SERVER.
```gdscript """
func _on_peer_disconnected(peer_id: int) -> void:
	print("Network: peer disconnected — id ", peer_id)
	player_left.emit(peer_id)
"""
```
"""

#### `_on_connected_to_server()`
"""Fires on the CLIENT once the WebSocket handshake succeeds.
```gdscript """
func _on_connected_to_server() -> void:
	print("Network: connected to server, our peer_id = ", get_peer_id())
	connected_to_server.emit()
"""
```
"""

#### `_on_connection_failed()`
"""
```gdscript """
func _on_connection_failed() -> void:
	print("Network: connection failed")
	connection_failed.emit()
	is_active = false
"""
```
"""

#### `_on_server_disconnected()`
"""
```gdscript """
func _on_server_disconnected() -> void:
	print("Network: server disconnected")
	is_active = false
"""
```
"""
