**When you open the game**:
1. You launch the game (F5 or exported binary)
2. The Godot engine starts, reads `project.godot`
3. Engine loads `lobby.tscn` as the root scene — no code triggers this, it's the engine doing it directly
4. `lobby.gd` is attached to the root node of that scene, so `lobby._ready()` fires automatically as part of scene initialization

---

**When the host clicks "Host Game":**
1. `lobby._on_host_pressed()`
2. `lobby._set_ui_enabled(false)`
	- Disables lobby ui
3. `Network.start_server()` — creates `WebSocketMultiplayerPeer`, calls `peer.create_server()`, assigns it to `multiplayer.multiplayer_peer`, connects `peer_connected` → `_on_peer_connected` and `peer_disconnected` → `_on_peer_disconnected`
	- peer = WebSocketMultiplayerPeer.new() just creates a new variable of that type.
	- peer.create_server(port) creates a server. You don't really need to know much beyond that.
	- the multiplayer variable/attribute is part of the MultiplayerAPI class, which is a parent of the node class.
4. `lobby._load_game()`
	- This is in the lobby.on_host_pressed() function
5. `get_tree().change_scene_to_file("woods_map.tscn")` — Godot loads the scene
	- stc
6. `Spawner._ready()` — `is_server()` is true, `for peer_id in multiplayer.get_peers(): _notify_scene_reload.rpc_id(peer_id)`. connects [[network.gd|Network]].player_left → `_on_player_left`
	- 
Note: the host gets **no player node**. The comment on line 65 says "Dedicated server — no player node for peer 1", which contradicts `network.gd`'s doc comment saying it's a listen-server. That looks like an unresolved gap.

---

**When a client clicks "Join Game":**

1. `lobby._on_join_pressed()`
2. `lobby._set_ui_enabled(false)`
	- Disable lobby ui. The reason why we do this is because connecting to the server might take a while, so we want to disable the join server button while they're loading in.
3. `Network.join_server(address)` — creates `WebSocketMultiplayerPeer`, calls `peer.create_client(url)`, assigns to `multiplayer.multiplayer_peer`, connects `connected_to_server` → `_on_connected_to_server`, `connection_failed` → `_on_connection_failed`, `server_disconnected` → `_on_server_disconnected`
	- 
4. _[WebSocket handshake — async, no code runs]_
5. `Network._on_connected_to_server()` — emits `Network.connected_to_server`
6. `lobby._on_connected()` — receives that signal
7. `lobby._load_game()`
8. `get_tree().change_scene_to_file("woods_map.tscn")` — Godot loads the scene
9. `Spawner._ready()` — `is_server()` is false, calls `_request_player_spawn.rpc_id(1)`

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

18. `Spawner._rpc_spawn_enemy(data)` — once per live enemy received from step 14