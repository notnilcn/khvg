# **When you open the game**:
1. You launch the game (F5 or exported binary)
2. The Godot engine starts, reads `project.godot`
3. Engine loads [`lobby.tscn`](lobby.tscn) as the root scene — no code triggers this, it's the engine doing it directly 
	1. [`lobby.gd`](<lobby.gd>) is attached to the root node of that scene, so `lobby._ready()` fires automatically as part of scene initialization
---
# **When you click "Host Game":**
1. [`lobby._on_host_pressed()`](<lobby.gd#`_on_host_pressed()`>)
	1. [`lobby._set_ui_enabled(false)`](<lobby.gd#`_set_ui_enabled(enabled)`>)
		- Disables lobby ui
	2. [`Network.start_server()`](<network.gd#`start_server(port)`>)
			- Assign `peer = WebSocketMultiplayerPeer.new()`. This is a low-level godot multiplayer api that you don't need to understand.
			- Specify that the websocket peer is a server using peer.create_server(port) and return error if error.
				- ==**I am aware that it kind of doesn't make sense that we're creating the server before defining how the low-level APIs are connected to the signals**==. You don't need to understand how it works.
			- Hands peer over to [`multiplayer`](<002 Godot Multiplayer.md#`multiplayer`>)`.multiplayer_peer`. You don't need to understand this.
			- connect the low-level [`multiplayer`](<002 Godot Multiplayer.md#`multiplayer`>)`.peer_connected` API to the [`player_joined`](<network.gd#`player_joined()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
			- connect the low-level godot [`multiplayer`](<002 Godot Multiplayer.md#`multiplayer`>)`.peer_disconnected` API to the [`player_left`](<network.gd#`player_left()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
	3. [`lobby._load_game()`](<lobby.gd#`_load_game()`>)
		1. `get_tree().change_scene_to_file("`[woods_map.tscn](<woods_map.tscn>)`")` — Godot loads the woods_map scene.
			- [`Spawner.gd`](<spawner.gd>) is in the [`woods_map.tscn`](<woods_map.tscn>) scene, so it is loaded as well. 
2. [`Spawner.ready()`](<spawner.gd#`_ready()`>) runs because is a special Godot function that runs when the scene and all of its children are loaded.
	1. `multiplayer.is_server()` is true, so finish setting up the server.
		- The `multiplayer` variable is inherited from `Node2d`, which inherits it from `Node`.
	2. connect [`Network.player_left()`](<network.gd#`player_left()`>) → `_on_player_left`
Note: the host gets **no player node**. The comment on line 65 says "Dedicated server — no player node for peer 1", which contradicts `network.gd`'s doc comment saying it's a listen-server. That looks like an unresolved gap.
---
# **When a client clicks "Join Game":**

1. [`lobby._on_join_pressed()`](<lobby.gd#`_on_join_pressed()`>)
	1. [`lobby._set_ui_enabled(false)`](<lobby.gd#`_set_ui_enabled(enabled)`>)
		- Disables lobby ui.
	2. [`Network.join_server()`](<network.gd#`join_server(address, port)`>)
			- Assign `peer = WebSocketMultiplayerPeer`. This is a low-level godot multiplayer api that you don't need to understand.
			- Specify that the websocket peer is a client using peer.create_client(url) and return error if error.
			- Hands peer over to [`multiplayer`](<002 Godot Multiplayer.md#`multiplayer`>)`.multiplayer_peer`. You don't need to understand this.
				- I think this initiates the handshake. A signal is emitted depending on the result of the handshake. I'm not sure but you don't really need to understand this. ==**I am aware that it kind of doesn't make sense that we're initiating the handshake before defining how the low-level APIs are connected to the signals**==. You don't need to understand how it works.
			- connect the low-level [`multiplayer`](<002 Godot Multiplayer.md#`multiplayer`>)`.connected_to_server` API to the [`_on_connected_to_server`](<network.gd#`_on_connected_to_server()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
			- connect the low-level godot [`multiplayer`](<002 Godot Multiplayer.md#`multiplayer`>)`.connection_failed` API to the [`_on_connection_failed`](<network.gd#`_on_connection_failed()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
			- connect the low-level godot [`multiplayer`](<002 Godot Multiplayer.md#`multiplayer`>)`.server_disconnected` API to the [`_on_server_disconnected`](<network.gd#`_on_server_disconnected()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
	3. [`Network._on_connected_to_server()`](<network.gd#`_on_connected_to_server()`>) emits [`Network.connected_to_server()`](<network.gd#`connected_to_server()`>) (assuming handshake is successful)
	4. [`lobby._load_game()`](<lobby.gd#`_load_game()`>) runs after player connects to server
			- [`lobby._load_game()`](<lobby.gd#`_load_game()`>) runs because [`lobby._ready()`](<lobby.gd#`_ready()`>) connects the [`lobby._on_connected()`](<lobby.gd#`_on_connected()`>) function to the [`Network.connected_to_server()`](<network.gd#`connected_to_server()`>) signal.
		1. `get_tree().change_scene_to_file("`[woods_map.tscn](<woods_map.tscn>)`")` — Godot loads the woods_map scene.
			- `spawner.gd` is part of this scene.
	5. [`Spawner.ready()`](<spawner.gd#`_ready()`>) — `is_server()` is false, calls [`_request_player_spawn.rpc_id(1)`](<spawner.gd#`_request_player_spawn()`>)
			- [`_request_player_spawn.rpc_id(1)`](<spawner.gd#`_request_player_spawn()`>)

**On the server, handling the RPC:**
10. `Spawner._request_player_spawn()` — reads `new_peer_id` from `get_remote_sender_id()`
11. _(catch-up loop)_ `Spawner._create_player_node.rpc_id(new_peer_id, ...)` — once per already-existing player, targeted only at the new client
12. `Spawner._spawn_player_for_peer(new_peer_id)` — picks a spawn position
13. `Spawner._create_player_node.rpc(peer_id, pos)` — broadcast to all peers
14. _(catch-up loop)_ `Spawner._rpc_spawn_enemy.rpc_id(new_peer_id, data)` — once per live enemy, targeted only at the new client

**On every peer (including the joining client), for the new player node:**

15. `Spawner._create_player_node()` — instantiates `player.tscn`, sets name, calls `set_multiplayer_authority(peer_id)`, calls `add_child()`
16. `Player._ready()` — registers self in `Global.players[peer_id]`, awaits one frame
17. `Player._init_player()` — sets sprite/health, enables or disables camera+HUD based on `is_multiplayer_authority()`, instantiates starting weapon

**On the joining client only, for each existing enemy (catch-up):**
18. `Spawner._rpc_spawn_enemy(data)` — once per live enemy received from step 14enemy(data)` — once per live enemy received from step 14
