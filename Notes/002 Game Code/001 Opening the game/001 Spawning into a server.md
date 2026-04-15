# **After opening the game & clicking join server**:
3. Engine loads [`lobby.tscn`](lobby.tscn) as the root scene — no code triggers this, it's the engine doing it directly 
	1. [`lobby.gd`](<lobby.gd>) is attached to the root node of that scene, so [`lobby._ready()`](<lobby.gd#`_ready()`>) fires automatically as part of scene initialization
4. [`lobby._ready()`](<lobby.gd#`_ready()`>) fires a bunch of functions which all define the lobby ui. Go to [000 Opening the game & then clicking Host Game or Join Game](<000 Opening the game & then clicking Host Game or Join Game>) to see what each button does.
5. [When a client clicks "Join Game"](<000 Opening the game & then clicking Host Game or Join Game#**When a client clicks "Join Game" **>) the client sends [`_request_player_spawn.rpc_id(1)`](<spawner.gd#`_request_player_spawn()`>) to the server
		- The `.rpc_id(1)` thing just specifies who to send the request to. An alternative is `.rpc()` which sends it to everyone that the client is connected to (which should be just the server, however, it's still good to specify. If the server were to call `.rpc()`, then it'd be calling to every client since the server is connected to every client).
		- The `@rpc("any_peer", "call_remote", "reliable")` specifies that the [`_request_player_spawn()`](<spawner.gd#`_request_player_spawn()`>) function can be called by (`"any_peer"`), that the function only be ran on the receiver (`"call_remote"`), and that the sending/receiving protocol should be reliable (`"reliable"`).
	1. 
6. 

