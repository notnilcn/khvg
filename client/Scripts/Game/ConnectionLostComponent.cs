#nullable enable
using Godot;

/// <summary>
/// The "connection lost" panel, declared inline under the UI CanvasLayer in game.tscn
/// (hidden by default). Shown from the DatabaseConnector autoload's Disconnected signal;
/// first tears down every spawned entity (EntitySpawnerComponent.DespawnAll) so nothing
/// stale survives the dead subscription. The reconnect button rebuilds the connection
/// with the saved AuthToken (DatabaseConnector.Connect) and returns through the main
/// menu — no auto-rejoin; the fresh game scene re-runs the subscription waves off the
/// Connected signal (TableSubscriber handles both connect orders already).
/// </summary>
public partial class ConnectionLostComponent : ControlComponent
{
    protected override void OnRegistered()
    {
        Hide();
        if (DatabaseConnector.Instance != null)
            DatabaseConnector.Instance.Disconnected += OnDisconnected;
    }

    private void OnDisconnected()
    {
        GetSibling<EntitySpawnerComponent>()?.DespawnAll();
        Show();
    }

    // Wired to the Reconnect button's pressed signal in game.tscn.
    private void OnReconnectPressed()
    {
        DatabaseConnector.Instance?.Connect();
        GetTree().ChangeSceneToFile("res://Scenes/main_menu.tscn");
    }

    public override void _ExitTree()
    {
        if (DatabaseConnector.Instance != null)
            DatabaseConnector.Instance.Disconnected -= OnDisconnected;
        base._ExitTree();
    }
}
