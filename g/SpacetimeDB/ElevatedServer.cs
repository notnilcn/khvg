#nullable enable
using Godot;

/// Autoload activated by the --elevated-server command-line flag.
/// Starts the WebSocket server and loads the game scene directly, bypassing the lobby.
/// SpacetimeDBManager detects the same flag and uses a dedicated token file
/// (.bmmo_token_elevated) so the elevated identity is stable across restarts.
public partial class ElevatedServer : Node
{
    public static bool IsActive { get; private set; }

    private const string GameScenePath = "res://VS-Like/Maps/Woods/woods_map.tscn";

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineArgs())
        {
            if (arg != "--elevated-server") continue;
            IsActive = true;
            break;
        }

        if (!IsActive) return;

        GD.Print("[ElevatedServer] --elevated-server detected. Starting WebSocket server...");
        Network.Instance.StartServer();

        // Defer so the lobby scene finishes its own _Ready before we replace it.
        Callable.From(_LoadGameScene).CallDeferred();
    }

    private void _LoadGameScene()
    {
        GD.Print("[ElevatedServer] Loading game scene, bypassing lobby.");
        GetTree().ChangeSceneToFile(GameScenePath);
    }
}
