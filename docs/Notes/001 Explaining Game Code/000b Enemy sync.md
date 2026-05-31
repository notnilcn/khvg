# **Before you open the game**
0. [StdbEnemySync.cs](<StdbEnemySync.cs>) is in the [`project.godot`](<project.godot>) autoload, so it's ran [before you open the game](<000 Autoload, Spacetimedb, and the elevated client.md#**Before you open the game**>)
1. [`StdbEnemySync._Ready()`](<StdbEnemySync.cs#`_Ready()`>) runs because it has stage 4 lung cancer.
	1. Saves the current StdbEnemySync object to the `{c#}StdbEnemySync.Instance` variable so that other scripts can access the object. [I have notes if this is confusing](<Stupid notes.md#^Instance-this>).
	2. Checks if `{c#}SpacetimeDBManager.Instance.IsStdbConnected` is true
		1. If true, call [`StdbEnemySync._HookCallbacks()`](<StdbEnemySync.cs#^-HookCallbacks>)
		2. If false, connect the [`SpacetimeDBManager.Instance.ConnectedToServer`](<SpacetimeDBManager.cs#^ConnectedToServer>) signal to the [`StdbEnemySync._OnConnectedToServer`](<StdbEnemySync.cs#^-OnConnectedToServer>) function.
			1. When the signal fires, [`StdbEnemySync._OnConnectedToServer`](<StdbEnemySync.cs#^-OnConnectedToServer>) runs which just disconnects the signal and calls [`StdbEnemySync._HookCallbacks()`](<StdbEnemySync.cs#^-HookCallbacks>)
2. [`StdbEnemySync._HookCallbacks()`](<StdbEnemySync.cs#^-HookCallbacks>) runs because it plays league of legends (mind cancer)
	1. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` [event](<Stupid notes.md#Delegates and events>) to [`StdbEnemySync._OnInsert`](<StdbEnemySync.cs#^-OnInsert>) ^1
	2. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnUpdate` [event](<Stupid notes.md#Delegates and events>) to [`StdbEnemySync._OnUpdate`](<StdbEnemySync.cs#^-OnUpdate>) ^2
	3. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnDelete` [event](<Stupid notes.md#Delegates and events>) to [`StdbEnemySync._OnDelete`](<StdbEnemySync.cs#^-OnDelete>) ^3
		- Whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` event fires, the corresponding `{c#}EventContext`, and `{c#}PlayerPosition` variables are passed into the [`StdbEnemySync._OnInsert`](<StdbEnemySync.cs#^-OnInsert>) function.
		- Go read [Delegates and events](<Stupid notes.md#Delegates and events>) if you wanna know why the syntax for connecting Spacetimedb functions is the same as the syntax for connecting Godot signals. They use the same underlying stuff.
	4. Checks if `{c#}SpacetimeDBManager.Instance.IsSubscriptionReady` is true
		1. If true, call [`_CatchUpIfInGameScene()`](<StdbEnemySync.cs#^-CatchUpIfInGameScene>)
		2. If false, connect the [`SpacetimeDBManager.Instance.SubscriptionReady`](<SpacetimeDBManager.cs#^SubscriptionReady>) signal to the [`StdbEnemySync._OnSubscriptionReady()`](<StdbEnemySync.cs#^-OnSubscriptionReady>) function.
			1. When the signal fires, [`StdbEnemySync._OnSubscriptionReady()`](<StdbEnemySync.cs#^-OnSubscriptionReady>) runs which disconnects the signal and calls [`StdbEnemySync._CatchUpIfInGameScene()
3. [`_CatchUpIfInGameScene()`](<StdbEnemySync.cs#^-CatchUpIfInGameScene>) checks if the current scene path is the gamescenepath and runs [`OnGameSceneLoaded()`](<StdbEnemySync.cs#^OnGameSceneLoaded>) if true.
4. [`OnGameSceneLoaded()`](<StdbEnemySync.cs#^OnGameSceneLoaded>) iterates through every player in the `{c#}conn.Db.PlayerPosition` table, checks if it is the current player, if true then gets the playerId and checks if a local node exists for that playerId, if true then runs [`_StoreSnapshot(key, row)`](<StdbEnemySync.cs#^StoreSnapshot>) and [`_SpawnRemote(row.PlayerId, row.X, row.Y)`](<StdbEnemySync.cs#^SpawnRemote>), which spawns a local player for every playerId that's in the local table.
---
# [`StdbEnemySync._Process(delta)`](<StdbEnemySync.cs#^-Process>) **(the function that does the actual dead reckoning interpolation)**
0. The `{c#}_Process(delta)` function is a special Godot function that runs automatically once every frame. The time since the last frame is passed in as `{c#}delta`.
1. Save `{c#}DateTimeOffset.UtcNow` to `{c#}nowMicros`.
2. For each node in `{c#}_remoteNodes`
	1. Check if the player's node instance is valid and if they are in the [`_snapshots`](<StdbEnemySync.cs#^-snapshots>) dictionary (this dictionary only contains enemies). If both true, then continue.
	2. Clamp the elapsed time to 1 second so that weird interpolation shit doesn't happen.
	3. [`Calculate the enemy's extrapolated position`](<StdbEnemySync.cs#^calculate-extrapolated-position>) using [`Simulator.Extrapolate`](<Simulator.cs#^Extrapolate>)
	4. Locally set the enemy's GlobalPosition equal to the lerp between the enemy's current GlobalPosition and their extrapolated position
---
# [`StdbEnemySync._OnInsert`](<StdbEnemySync.cs#^-OnInsert>)
0. This runs whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` event runs.
1. Stores the inserted row values in the snapshot dictionary via the [`_StoreSnapshot(key, row)`](<StdbEnemySync.cs#^-_StoreSnapshot>) function and spawns a player for that inserted row.

# [`StdbEnemySync._OnUpdate`](<StdbEnemySync.cs#^-OnUpdate>)
0. This runs whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnUpdate` event runs.
1. Stores the updated row values in the snapshot dictionary via the [`_StoreSnapshot(key, row)`](<StdbEnemySync.cs#^-_StoreSnapshot>) function. The dead reckoning interpolation automatically reads these updated row values every frame.

# [`StdbEnemySync._OnDelete`](<StdbEnemySync.cs#^-OnDelete>)
0. This runs whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnDelete` event runs.
1. Removes the deleted row from the snapshot dictionary, free that player's node, and prints out that the player left the Spacetimedb server.