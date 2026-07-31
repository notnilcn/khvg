#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;

/// <summary>
/// Owns the SpacetimeDB connection for the Main entity: builds the DbConnection on
/// registration, pumps FrameTick() every frame, and disconnects on exit. Sibling
/// components hook <see cref="Connected"/> to wire their table callbacks and
/// subscription waves (logic moved out of GameManager.cs).
/// </summary>
public partial class ConnectionComponent : Component
{
    /// Fired once the connection is established; siblings then wire their table callbacks.
    [Signal] public delegate void ConnectedEventHandler();

    [Export] public string Host { get; set; } = "http://127.0.0.1:3000";
    [Export] public string DbName { get; set; } = "bullethell";

    public DbConnection? Conn { get; private set; }
    public Identity? LocalIdentity { get; private set; }
    public string Username { get; private set; } = "";

    protected override void OnRegistered() => Connect(Host, DbName);

    public override void _Process(double delta) => Conn?.FrameTick();

    public void Connect(string host, string dbName)
    {
        // Scoped per-host: a token minted by one SpacetimeDB instance (e.g. local)
        // is signed with that instance's key and gets rejected (401) by another
        // (e.g. maincloud), so local/maincloud tokens can't share one file.
        var hostTag = host.Replace("://", "_").Replace(":", "_").Replace("/", "_");
        AuthToken.Init(OS.GetUserDataDir() + $"/.bullethell_token_{hostTag}");
        Conn = DbConnection.Builder()
            .WithUri(host)
            .WithDatabaseName(dbName)
            .WithToken(AuthToken.Token)
            .OnConnect(OnConnected)
            .OnConnectError(OnConnectError)
            .OnDisconnect(OnDisconnected)
            .Build();
    }

    private void OnConnected(DbConnection conn, Identity identity, string token)
    {
        LocalIdentity = identity;
        AuthToken.SaveToken(token);

        conn.Db.LocalLobbyPlayer.OnInsert += (_, p) => Username = p.Username;
        conn.Db.LocalLobbyPlayer.OnUpdate += (_, _, p) => Username = p.Username;

        EmitSignal(SignalName.Connected);
    }

    private void OnConnectError(Exception e)
    {
        GD.PrintErr($"[ConnectionComponent] Connection error: {e.Message}");
    }

    private void OnDisconnected(DbConnection conn, Exception? e)
    {
        if (e != null)
            GD.PrintErr($"[ConnectionComponent] Disconnected: {e.Message}");
    }

    public bool IsLocal(Identity id) => LocalIdentity.HasValue && id == LocalIdentity.Value;

    public override void _ExitTree()
    {
        if (Conn?.IsActive == true)
            Conn.Disconnect();
        base._ExitTree();
    }
}
