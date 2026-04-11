# **When you open the game**:
1. You launch the game (F5 or exported binary)
2. The Godot engine starts, reads `project.godot`
3. Engine loads [`lobby.tscn`](lobby.tscn) as the root scene — no code triggers this, it's the engine doing it directly 
4. [`lobby.gd`](<lobby.gd>) is attached to the root node of that scene, so `lobby._ready()` fires automatically as part of scene initialization

---
# **When you click "Host Game":**
1. [`lobby._on_host_pressed()`](<lobby.gd#`_on_host_pressed()`>)
	1. [`lobby._set_ui_enabled(false)`](<lobby.gd#`_set_ui_enabled(enabled)`>)
		- Disables lobby ui
	2. [`Network.start_server()`](<network.gd#`start_server(port)`>)
		- creates [`WebSocketMultiplayerPeer`](<network.gd#`start_server(port)`>) via peer = WebSocketMultiplayerPeer.new()
		- calls [`peer.create_server()`](<network.gd#`start_server(port)`>), assigns it to [`multiplayer.multiplayer_peer`](<network.gd#`start_server(port)`>). You don't need to understand this.
		- connects [`multiplayer.peer_connected`](<network.gd#`start_server(port)`>) → [`_on_peer_connected`](<network.gd#`start_server(port)`>).
			- connecting [`multiplayer.peer_connected`](<network.gd#`start_server(port)`>) → [`_on_peer_connected`](<network.gd#`start_server(port)`>) just connects the multiplayer API to the [`player_joined`](<network.gd#`player_joined()`>) signal.
		- connects [`multiplayer.peer_disconnected`](<network.gd#`start_server(port)`>) → [`_on_peer_disconnected`](<network.gd#`start_server(port)`>).
			- connecting [`multiplayer.peer_disconnected`](<network.gd#`start_server(port)`>) → [`_on_peer_disconnected`](<network.gd#`start_server(port)`>) just connects the multiplayer API to the [`player_left`](<network.gd#`player_left()`>) signal.
	3. [`lobby._load_game()`](<lobby.gd#`_load_game()`>)
		1. `get_tree().change_scene_to_file("woods_map.tscn")` — Godot loads the woods_map scene
2. `Spawner._ready()` — `is_server()` is true, `for peer_id in multiplayer.get_peers(): _notify_scene_reload.rpc_id(peer_id)`. connects Network.player_left → `_on_player_left`
	- 
Note: the host gets **no player node**. The comment on line 65 says "Dedicated server — no player node for peer 1", which contradicts `network.gd`'s doc comment saying it's a listen-server. That looks like an unresolved gap.

---

**When a client clicks "Join Game":**

1. [`lobby._on_join_pressed()`](<lobby.gd#`_on_join_pressed()`>)
	1. [`lobby._set_ui_enabled(false)`](<lobby.gd#`_set_ui_enabled(enabled)`>)
		- Disables lobby ui.
	2. [`Network.join_server()`](<network.gd#`join_server(address, port)`>)
		- creates a new `WebSocketMultiplayerPeer` and assigns it to `peer`
		- calls `peer.create_client(url)`
			- This is a low-level Godot Multiplayer api. You don't really need to know how it works.
		- assigns the `WebSocketMultiplayerPeer` to `multiplayer.multiplayer_peer`
		- connects `connected_to_server` → `_on_connected_to_server`
		- connects `connection_failed` → `_on_connection_failed`
		- connects `server_disconnected` → `_on_server_disconnected`
	3. `_[WebSocket handshake — async, no code runs]_`
	4. `Network._on_connected_to_server()` — emits `Network.connected_to_server`
	5. `lobby._on_connected()` — receives that signal
	6. `lobby._load_game()`
	7. `get_tree().change_scene_to_file("woods_map.tscn")` — Godot loads the scene
	8. `Spawner._ready()` — `is_server()` is false, calls `_request_player_spawn.rpc_id(1)`

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