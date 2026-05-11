# **Before you open the game**
0. [StdbPlayerSync.cs](<StdbPlayerSync.cs>) is in the [`project.godot`](<project.godot>) autoload, so it's ran [before you open the game](<000 Autoloads, Spacetimedb.cs, and the elevated client.md#**Before you open the game**>)
1. [`StdbPlayerSync._Ready()`](<StdbPlayerSync.cs#`_Ready()`>) runs because it has stage 4 lung cancer.
	1. Saves the current StdbPlayerSync object to the `{c#}StdbPlayerSync.Instance` variable so that other scripts can access the object. [I have notes if this is confusing](<Stupid notes.md#^Instance-this>).
	2. Checks if `{c#}SpacetimeDBManager.Instance.IsStdbConnected` is true
		1. If true, call [`StdbPlayerSync._HookCallbacks()`](<StdbPlayerSync.cs#^-HookCallbacks>)
		2. If false, connect the [`SpacetimeDBManager.Instance.ConnectedToServer`](<SpacetimeDBManager.cs#^ConnectedToServer>) signal to the [`StdbPlayerSync._OnConnectedToServer`](<StdbPlayerSync.cs#^-OnConnectedToServer>) function.
			1. When the signal fires, [`StdbPlayerSync._OnConnectedToServer`](<StdbPlayerSync.cs#^-OnConnectedToServer>) runs which just disconnects the signal and calls [`StdbPlayerSync._HookCallbacks()`](<StdbPlayerSync.cs#^-HookCallbacks>)
2. [`StdbPlayerSync._HookCallbacks()`](<StdbPlayerSync.cs#^-HookCallbacks>) runs because it plays league of legends (mind cancer)
	1. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` [event](<Stupid notes.md#Delegates and events>) to [`StdbPlayerSync._OnInsert`](<StdbPlayerSync.cs#^-OnInsert>) ^1
	2. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnUpdate` [event](<Stupid notes.md#Delegates and events>) to [`StdbPlayerSync._OnUpdate`](<StdbPlayerSync.cs#^-OnUpdate>) ^2
	3. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnDelete` [event](<Stupid notes.md#Delegates and events>) to [`StdbPlayerSync._OnDelete`](<StdbPlayerSync.cs#^-OnDelete>) ^3
		- Whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` event fires, the corresponding `{c#}EventContext`, and `{c#}PlayerPosition` variables are passed into the [`StdbPlayerSync._OnInsert`](<StdbPlayerSync.cs#^-OnInsert>) function.
		- Go read [Delegates and events](<Stupid notes.md#Delegates and events>) if you wanna know why the syntax for connecting Spacetimedb functions is the same as the syntax for connecting Godot signals. They use the same underlying stuff.
	4. Checks if `{c#}SpacetimeDBManager.Instance.IsSubscriptionReady` is true
		1. If true, call [`_CatchUpIfInGameScene()`](<StdbPlayerSync.cs#^-CatchUpIfInGameScene>)
		2. If false, connect the [`SpacetimeDBManager.Instance.SubscriptionReady`](<SpacetimeDBManager.cs#^SubscriptionReady>) signal to the [`StdbPlayerSync._OnSubscriptionReady()`](<StdbPlayerSync.cs#^-OnSubscriptionReady>) function.
			1. When the signal fires, [`StdbPlayerSync._OnSubscriptionReady()`](<StdbPlayerSync.cs#^-OnSubscriptionReady>) runs which disconnects the signal and calls [`StdbPlayerSync._CatchUpIfInGameScene()
3. [`_CatchUpIfInGameScene()`](<StdbPlayerSync.cs#^-CatchUpIfInGameScene>) checks if the current scene path is the gamescenepath and runs [`OnGameSceneLoaded()`](<StdbPlayerSync.cs#^OnGameSceneLoaded>) if true.
4. [`OnGameSceneLoaded()`](<StdbPlayerSync.cs#^OnGameSceneLoaded>) iterates through every player in the `{c#}conn.Db.PlayerPosition` table, checks if it is the current player, if true then gets the playerId and checks if a local node exists for that playerId, if true then runs [`_StoreSnapshot(key, row)`](<StdbPlayerSync.cs#^StoreSnapshot>) and [`_SpawnRemote(row.PlayerId, row.X, row.Y)`](<StdbPlayerSync.cs#^SpawnRemote>), which spawns a local player for every playerId that's in the local table.
---
# [`StdbPlayerSync._Process(delta)`](<StdbPlayerSync.cs#^-Process>) **(the function that does the actual dead reckoning interpolation)**
0. The `{c#}_Process(delta)` function is a special Godot function that runs automatically once every frame. The time since the last frame is passed in as `{c#}delta`.
1. Save `{c#}DateTimeOffset.UtcNow` to `{c#}nowMicros`.
2. For each node in `{c#}_remoteNodes`
	1. Check if the player's node instance is valid and if they are in the [`_snapshots`](<StdbPlayerSync.cs#^-snapshots>) dictionary (this dictionary only contains play. If both true, then continue.
	2. Clamp the elapsed time to 2 seconds so that weird interpolation shit doesn't happen.
	3. [`Calculate the player's extrapolated position`](<StdbPlayerSync.cs#^calculate-extrapolated-position>) using [`Simulator.Extrapolate`](<Simulator.cs#^Extrapolate>)
	4. Locally set the player's GlobalPosition equal to the lerp between the player's current GlobalPosition and their extrapolated position
---
# [`StdbPlayerSync._OnInsert`](<StdbPlayerSync.cs#^-OnInsert>)
0. This runs whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` event runs.
1. Stores the inserted row values in the snapshot dictionary via the [`_StoreSnapshot(key, row)`](<StdbPlayerSync.cs#^-_StoreSnapshot>) function and spawns a player for that inserted row.

# [`StdbPlayerSync._OnUpdate`](<StdbPlayerSync.cs#^-OnUpdate>)
0. This runs whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnUpdate` event runs.
1. Stores the updated row values in the snapshot dictionary via the [`_StoreSnapshot(key, row)`](<StdbPlayerSync.cs#^-_StoreSnapshot>) function. The dead reckoning interpolation automatically reads these updated row values every frame.

# [`StdbPlayerSync._OnDelete`](<StdbPlayerSync.cs#^-OnDelete>)
0. This runs whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnDelete` event runs.
1. Removes the deleted row from the snapshot dictionary, free that player's node, and prints out that the player left the Spacetimedb server.