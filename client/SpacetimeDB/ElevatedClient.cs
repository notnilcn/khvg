/*
```c# */
#nullable enable
using Godot;

/// Autoload activated by the --elevated-client command-line flag.
/// Starts the WebSocket server and loads the game scene directly, bypassing the lobby.
/// SpacetimeDBManager detects the same flag and uses a dedicated token file
/// (.bmmo_token_elevated) so the elevated identity is stable across restarts.
public partial class ElevatedClient : Node
{
	public static bool IsActive { get; private set; }

	private const string GameScenePath = "res://VS-Like/Maps/Woods/woods_map.tscn";
/*
```
*/

/*
# `_Ready()`
```c# */
	public override void _Ready()
	{
		foreach (var arg in OS.GetCmdlineArgs())
		{
			if (arg != "--elevated-client") continue;
			IsActive = true;
			break;
		}

		if (!IsActive) return;

		GD.Print("[ElevatedClient] --elevated-client detected.");

		// Defer so the lobby scene finishes its own _Ready before we replace it.
		Callable.From(_LoadGameScene).CallDeferred();
	}
/*
```
*/

/*
```c# */
	private void _LoadGameScene()
	{
		GD.Print("[ElevatedClient] Loading game scene, bypassing lobby.");
		GetTree().ChangeSceneToFile(GameScenePath);
	}
/*
```
*/
}
