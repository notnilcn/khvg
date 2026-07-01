A lot of this will eventually be overhauled.

In the final game, what I want the lobby to have is:
- Server list
	- Players will be able to switch to different servers, similar to RotMG.
	- Items in a player's vault will be available in the [lore-based safe hub](<99 (outdated) Game Design Document.md#**There are 3 different lore-based safe hubs**>) that they chose, or the default [outcasted magician's safe hub](<99 (outdated) Game Design Document.md#**01 Safe hub that belongs to the outcasted magician dude**>) vault if they haven't chosen one yet. Items in a guild vault aren't transferrable between servers.
- Character selection
	- Player slots will be purchasable like in RotMG. Players will be able to have 2 by default.
	- There will be a character creator thing (COMPLETELY SHELVED UNTIL WE HAVE A MINIMUM VIABLE PRODUCT).
		- Just be lazy and encode a bunch of different nose/hair/eye/head/eyebrow/mouth/skull shapes continuously and let the player play around with the sliders. Make it too customizable.
			- I feel like there should be a mathematical way to calculate the appropriate texture maps to allow players to colour their characters freely as well.
- Settings
---
# **When you open the game**:
1. You launch the game (F5 or exported binary)
2. The Godot engine starts, reads [`project.godot`](<project.godot>)
3. The main scene is [`lobby.tscn`](lobby.tscn) so the engine loads [`lobby.tscn`](lobby.tscn) as the first scene
	1. [`Lobby.cs`](<lobby.cs>) is attached to the root node of that scene
	2. [`Lobby._Ready()`](<lobby.cs#`_Ready()`>) automatically fires because it's a special godot function
		- [`Lobby._Ready()`](<lobby.cs#`_Ready()`>) defines where all the buttons go and how the ui looks. The buttons are linked to functions that do things.
---
# **When you click "Host Game":**
1. The button linked to "Host Game" is [`lobby._OnHostPressed()`](<lobby.cs#`_OnHostPressed()`>)
	1. [`lobby._SetUiEnabled(false)`](<lobby.cs#`_SetUiEnabled(enabled)`>)
		- Disables lobby ui
	2. [`Network.StartServer()`](<network.cs#`StartServer(port)`>)
			- Assign `peer = WebSocketMultiplayerPeer.new()`. This is a low-level godot multiplayer api that you don't need to understand.
			- Attempt to create a server via `peer.CreateServer(port)` and return error if error.
			- Hands peer over to [`multiplayer.MultiplayerPeer`](<002 Godot Multiplayer.md#`multiplayer`>). You don't need to understand this.
			- Connect the low-level [`multiplayer.PeerConnected`](<002 Godot Multiplayer.md#`multiplayer`>) API to the [`player_joined`](<network.cs#`player_joined()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
			- Connect the low-level godot [`multiplayer.PeerDisconnected`](<002 Godot Multiplayer.md#`multiplayer`>) API to the [`player_left`](<network.cs#`player_left()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
	3. [`lobby._LoadGame()`](<lobby.cs#`_LoadGame()`>)
		1. `get_tree().change_scene_to_file("`[`woods_map.tscn`](<woods_map.tscn>)`")` — Godot loads the woods_map scene.
			- [`Spawner.cs`](<Spawner.cs>) is in the [`woods_map.tscn`](<woods_map.tscn>) scene, so it is loaded as well.
2. [`Spawner.ready()`](<Spawner.cs#`_ready()`>) runs because is a special Godot function that runs when the scene and all of its children are loaded.
	1. `multiplayer.is_server()` is true, so finish setting up the server.
		- The `multiplayer` variable is inherited from `Node2d`, which inherits it from `Node`.
	2. Connect [`Network.player_left()`](<network.cs#`player_left()`>) → `_on_player_left`
Note: the host gets **no player node**. The comment on line 65 says "Dedicated server — no player node for peer 1", which contradicts `network.cs`'s doc comment saying it's a listen-server. That looks like an unresolved gap.
---
# **When a client clicks "Join Game":**
1. [`lobby._on_join_pressed()`](<lobby.cs#`_on_join_pressed()`>)
	1. [`lobby._set_ui_enabled(false)`](<lobby.cs#`_set_ui_enabled(enabled)`>)
		- Disables lobby ui.
	2. [`Network.join_server()`](<network.cs#`join_server(address, port)`>)
			- Assign `peer = WebSocketMultiplayerPeer`. This is a low-level godot multiplayer api that you don't need to understand.
			- Attempt to connect to the server using peer.create_client(url) and return error if error. 
			- Hands peer over to [`multiplayer.multiplayer_peer`](<002 Godot Multiplayer.md#`multiplayer`>). You don't need to understand this.
				- I think this initiates the handshake. A signal is emitted depending on the result of the handshake. I'm not sure but you don't really need to understand this. ==**I am aware that it kind of doesn't make sense that we're initiating the handshake before defining how the low-level APIs are connected to the signals**==. You don't need to understand how it works.
			- connect the low-level [`multiplayer.connected_to_server`](<002 Godot Multiplayer.md#`multiplayer`>) API to the [`_on_connected_to_server`](<network.cs#`_on_connected_to_server()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
			- connect the low-level godot [`multiplayer.connection_failed`](<002 Godot Multiplayer.md#`multiplayer`>) API to the [`_on_connection_failed`](<network.cs#`_on_connection_failed()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
			- connect the low-level godot [`multiplayer.server_disconnected`](<002 Godot Multiplayer.md#`multiplayer`>) API to the [`_on_server_disconnected`](<network.cs#`_on_server_disconnected()`>) signal.
				- This makes it so that you don't have to call the multiplayer package/class whenever you want to interact with the server.
	3. [`Network._on_connected_to_server()`](<network.cs#`_on_connected_to_server()`>) emits [`Network.connected_to_server()`](<network.cs#`connected_to_server()`>) (assuming handshake is successful)
	4. [`lobby._load_game()`](<lobby.cs#`_load_game()`>) runs after player connects to server
			- [`lobby._load_game()`](<lobby.cs#`_load_game()`>) runs because [`lobby._ready()`](<lobby.cs#`_ready()`>) connects the [`lobby._on_connected()`](<lobby.cs#`_on_connected()`>) function to the [`Network.connected_to_server()`](<network.cs#`connected_to_server()`>) signal.
		1. `get_tree().change_scene_to_file("`[`woods_map.tscn`](<woods_map.tscn>)`")` — Godot loads the woods_map scene.
			- `spawner.cs` is part of this scene.
	5. [`Spawner.ready()`](<Spawner.cs#`_ready()`>) — `is_server()` is false, calls [`_request_player_spawn.rpc_id(1)`](<Spawner.cs#`_request_player_spawn()`>)
			- [`_request_player_spawn.rpc_id(1)`](<Spawner.cs#`_request_player_spawn()`>)

