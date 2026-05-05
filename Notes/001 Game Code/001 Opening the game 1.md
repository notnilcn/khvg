A lot of this will eventually be overhauled.

In the final game, what I want the lobby to have is:
- Server list
	- Players will be able to switch to different servers, similar to RotMG.
	- Items in a player's vault will be available in the [lore-based safe hub](<Overviews.md#**There are 3 different lore-based safe hubs**>) that they chose, or the default [outcasted magician's safe hub](<Overviews.md#**01 Safe hub that belongs to the outcasted magician dude**>) vault if they haven't chosen one yet. Items in a guild vault aren't transferrable between servers.
- Character selection
	- Player slots will be purchasable like in RotMG. Players will be able to have 2 by default.
	- There will be a character creator thing (COMPLETELY SHELVED UNTIL WE HAVE A MINIMUM VIABLE PRODUCT).
		- Just be lazy and encode a bunch of different nose/hair/eye/head/eyebrow/mouth/skull shapes continuously and let the player play around with the sliders. Make it too customizable.
			- I feel like there should be a mathematical way to calculate the appropriate texture maps to allow players to colour their characters freely as well.
- Settings
---
# Summary of [000](<000 Autoloads, Spacetimedb.cs, and the elevated client>)
Autoloaded a bunch of shit.

---
# **When you open the game**:
1. You launch the game (F5 or exported binary)
2. The Godot engine starts, reads [`project.godot`](<project.godot>)
3. The main scene is [`lobby.tscn`](lobby.tscn) so the engine loads [`lobby.tscn`](lobby.tscn) as the first scene
	1. [`Lobby.cs`](<lobby.cs>) is attached to the root node of that scene
	2. [`Lobby._Ready()`](<lobby.cs#`_Ready()`>) automatically fires because it's a special godot function
		- [`Lobby._Ready()`](<lobby.cs#`_Ready()`>) defines where all the buttons go and how the ui looks. The buttons are linked to functions that do things.
---
# **When you click "Play Game":**
1. [`lobby._OnPlayPressed()`](<lobby.cs#`_OnPlayPressed()`>)
	1. `get_tree().change_scene_to_file("`[`woods_map.tscn`](<woods_map.tscn>)`")` — Godot loads the woods_map scene.
		- `spawner.cs` is part of this scene.
	2. [`Spawner.ready()`](<Spawner.cs#`_ready()`>) — `is_server()` is false, calls [`_request_player_spawn.rpc_id(1)`](<Spawner.cs#`_request_player_spawn()`>)
			- [`_request_player_spawn.rpc_id(1)`](<Spawner.cs#`_request_player_spawn()`>)

