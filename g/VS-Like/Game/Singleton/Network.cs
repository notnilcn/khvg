/*
Network code
```c# */
using Godot;

public partial class Network : Node
{
    public static Network Instance { get; private set; }

    [Signal] public delegate void PlayerJoinedEventHandler(int peerId);
    [Signal] public delegate void PlayerLeftEventHandler(int peerId);
    [Signal] public delegate void ConnectedToServerEventHandler();
    [Signal] public delegate void ConnectionFailedEventHandler();

    public const int DefaultPort = 9999;
    public const float HitValidationRange = 350.0f;

    public bool IsActive { get; private set; } = false;
/*
```
*/

/*
# `_Ready()`
its obvious ig
```c# */
    public override void _Ready()
    {
        Instance = this;
    }
/*
```
*/



/*
# `StartServer(port)`
its obvious ig
```c# */
    public bool StartServer(int port = DefaultPort)
    {
        var peer = new WebSocketMultiplayerPeer();
        var err = peer.CreateServer(port);
        if (err != Error.Ok)
        {
            GD.PushError($"Network: failed to create server on port {port} (error {err})");
            return false;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += _OnPeerConnected;
        Multiplayer.PeerDisconnected += _OnPeerDisconnected;

        IsActive = true;
        GD.Print("Network: server listening on port ", port);
        return true;
    }
/*
```
*/



/*
# `JoinServer(address, port)`
its obvious ig
```c# */
    public void JoinServer(string address, int port = DefaultPort)
    {
        var peer = new WebSocketMultiplayerPeer();
        var url = $"ws://{address}:{port}";
        var err = peer.CreateClient(url);
        if (err != Error.Ok)
        {
            GD.PushError($"Network: failed to connect to {url} (error {err})");
            EmitSignal(SignalName.ConnectionFailed);
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.ConnectedToServer += _OnConnectedToServer;
        Multiplayer.ConnectionFailed += _OnConnectionFailed;
        Multiplayer.ServerDisconnected += _OnServerDisconnected;

        IsActive = true;
        GD.Print("Network: connecting to ", url);
    }
/*
```
*/



/*
# `DisconnectFromGame()`
its obvious ig
```c# */
    public void DisconnectFromGame()
    {
        if (Multiplayer.MultiplayerPeer != null)
        {
            Multiplayer.MultiplayerPeer.Close();
            Multiplayer.MultiplayerPeer = null;
        }
        IsActive = false;
    }
/*
```
*/



/*
# `IsServer() & GetPeerId()`
just 2 obvious functions
```c# */
    public bool IsServer() => Multiplayer.IsServer();
    public int GetPeerId() => Multiplayer.GetUniqueId();
/*
```
*/



/*
# `_OnPeerConnected(peerId)`
its obvious ig
```c# */
    private void _OnPeerConnected(long peerId)
    {
        GD.Print("Network: peer connected — id ", peerId);
        EmitSignal(SignalName.PlayerJoined, (int)peerId);
    }
/*
```
*/


/*
# `_OnPeerDisconnected(peerId)`
its obvious ig
```c# */
    private void _OnPeerDisconnected(long peerId)
    {
        GD.Print("Network: peer disconnected — id ", peerId);
        EmitSignal(SignalName.PlayerLeft, (int)peerId);
    }
/*
```
*/


/*
# `_OnConnectedToServer()`
```c# */
    private void _OnConnectedToServer()
    {
        GD.Print("Network: connected to server, our peer_id = ", GetPeerId());
        EmitSignal(SignalName.ConnectedToServer);
    }
/*
```
*/



/*
# `_OnConnectedToServer()`
```c# */
    private void _OnConnectionFailed()
    {
        GD.Print("Network: connection failed");
        EmitSignal(SignalName.ConnectionFailed);
        IsActive = false;
    }
/*
```
*/

/*
# `_OnServerDisconnected()`
```c# */
    private void _OnServerDisconnected()
    {
        GD.Print("Network: server disconnected");
        IsActive = false;
    }
/*
```
*/
}