# **Before you open the game**
0. [StdbPlayerSync.cs](<StdbPlayerSync.cs>) is in the [`project.godot`](<project.godot>) autoload, so it's ran [before you open the game](<000 Autoload, Spacetimedb, and the elevated client.md#**Before you open the game**>)
1. [`StdbPlayerSync._Ready()`](<StdbPlayerSync.cs#`_Ready()`>) runs because it has stage 4 lung cancer.
	1. Saves the current StdbPlayerSync object to the `{c#}StdbPlayerSync.Instance` variable so that other scripts can access the object. [I have notes if this is confusing](<Stupid notes.md#^Instance-this>).
	2. Checks if `{c#}SpacetimeDBManager.Instance.IsStdbConnected` is true
		1. If true, call [`StdbPlayerSync._HookCallbacks()`](<StdbPlayerSync.cs#^-HookCallbacks>)
		2. If false, connect the [`StdbPlayerSync._OnConnectedToServer`](<StdbPlayerSync.cs#^-OnConnectedToServer>) function to the [`SpacetimeDBManager.Instance.ConnectedToServer`](<SpacetimeDBManager.cs#^ConnectedToServer>) signal.
			1. When the signal fires, [`StdbPlayerSync._OnConnectedToServer`](<StdbPlayerSync.cs#^-OnConnectedToServer>) runs which just disconnects the signal and calls [`StdbPlayerSync._HookCallbacks()`](<StdbPlayerSync.cs#^-HookCallbacks>)
2. [`StdbPlayerSync._HookCallbacks()`](<StdbPlayerSync.cs#^-HookCallbacks>) runs because it plays league of legends (mind cancer)
	1. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` signal to [`StdbPlayerSync._OnInsert`](<StdbPlayerSync.cs#^-OnInsert>)
	2. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnUpdate` signal to [`StdbPlayerSync._OnUpdate`](<StdbPlayerSync.cs#^-OnUpdate>)
	3. Connects the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnDelete` signal to [`StdbPlayerSync._OnDelete`](<StdbPlayerSync.cs#^-OnDelete>)
		- `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` is generated from the [module bindings](<g/SpacetimeDB/module_bindings/Tables/PlayerPosition.g.cs>). The PlayerPositionHandle has the same outputs as the `{c#}_OnInsert` function, so C# knows to pass the outputs to the `{c#}_OnInsert` function whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` function fires. The `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` is constructed in the spacetimedb sdk. I'm too lazy to go into anymore details on this.
	4. Checks if `{c#}SpacetimeDBManager.Instance.IsSubscriptionReady` is true
		1. If true, call [`_CatchUpIfInGameScene()`](<StdbPlayerSync.cs#^-CatchUpIfInGameScene>)
		2. If false, connect the `{c#}SpacetimeDBManager.Instance.SubscriptionReady` signa
3. 

