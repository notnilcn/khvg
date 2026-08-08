#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;

/// <summary>
/// Owns the SpacetimeDB connection: builds the DbConnection on _Ready, pumps FrameTick()
/// every frame, and disconnects on exit. Registered as the DatabaseConnector autoload
/// (project.godot), so any node in any scene reaches it through <see cref="Instance"/> —
/// no entity-registry sibling lookup needed. Consumers hook <see cref="Connected"/> to wire
/// their table callbacks and subscription waves (guard against the connection already being
/// active first — the autoload connects before Main loads).
/// </summary>
public partial class DatabaseConnector : Node
{
    /// Fired once the connection is established; consumers then wire their table callbacks.
    [Signal] public delegate void ConnectedEventHandler();

    /// The autoload singleton. Null only before the autoload's _Ready or after shutdown.
    public static DatabaseConnector? Instance { get; private set; }

    [Export] public string Host { get; set; } = "http://127.0.0.1:3000"; // "https://maincloud.spacetimedb.com"
    [Export] public string DbName { get; set; } = "bullethell";

    public DbConnection? Conn { get; private set; }
    public Identity? LocalIdentity { get; private set; }
    public string Username { get; private set; } = "";

    public override void _Ready()
{
        Instance = this;
        Connect(Host, DbName);
    }

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
        GD.PrintErr($"[DatabaseConnector] Connection error: {e.Message}");
    }

    private void OnDisconnected(DbConnection conn, Exception? e)
    {
        if (e != null)
            GD.PrintErr($"[DatabaseConnector] Disconnected: {e.Message}");
    }

    public bool IsLocal(Identity id) => LocalIdentity.HasValue && id == LocalIdentity.Value;

    public override void _ExitTree()
{
        if (Conn?.IsActive == true)
            Conn.Disconnect();
        if (Instance == this) Instance = null;
    }
}
