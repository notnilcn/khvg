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

	/// Fired when the connection drops (not on deliberate shutdown). The game scene's
	/// ConnectionLostComponent shows its reconnect panel off this.
	[Signal] public delegate void DisconnectedEventHandler();

	/// The autoload singleton. Null only before the autoload's _Ready or after shutdown.
	public static DatabaseConnector? Instance { get; private set; }

	[Export] public string Host { get; set; } = "http://127.0.0.1:3000"; // "https://khvgames.com/server" // "https://maincloud.spacetimedb.com"
	[Export] public string DbName { get; set; } = "bullethell";
	[Export] public string TokenAppend { get; set; } = "";

	public DbConnection? Conn { get; private set; }
	public Identity? LocalIdentity { get; private set; }
	public string Username { get; private set; } = "";
	private string _authTokenKey = "";

	public override void _Ready()
	{
		Instance = this;

		// "--p1", "--p2", ... in the launch args appends "_p1", "_p2", ... to the
		// auth token so parallel run instances get distinct identities.
		foreach (var arg in OS.GetCmdlineArgs())
		{
			if (IsPlayerArg(arg)) { TokenAppend = "_" + arg.TrimStart('-'); break; }
		}
		if (TokenAppend == "")
		{
			foreach (var arg in OS.GetCmdlineUserArgs())
			{
				if (IsPlayerArg(arg)) { TokenAppend = "_" + arg.TrimStart('-'); break; }
			}
		}

		Connect();
	}

	// REGRESSION GUARD: must match the whole arg ("--p1", "--p2", ...) — a plain
	// StartsWith("--p") also matches Godot's own --path, which made every editor-run
	// instance share "_path" as its auth token key.
	private static bool IsPlayerArg(string arg)
	{
		if (arg.Length <= 3 || !arg.StartsWith("--p")) return false;
		for (int i = 3; i < arg.Length; i++)
			if (!char.IsDigit(arg[i])) return false;
		return true;
	}

	public override void _Process(double delta)
	{
		// FrameTick dispatches every queued table/reducer callback — one throwing handler
		// must not kill the pump, or all network processing stops for the session.
		try
		{
			Conn?.FrameTick();
		}
		catch (Exception e)
		{
			GD.PrintErr($"[DatabaseConnector] FrameTick exception (kept pumping): {e}");
		}
	}

	public void Connect()
	{
		// Each SpacetimeDB host gets its own token because tokens are signed
		// by the individual instance.
		_authTokenKey = Host.Replace("://", "_")
						.Replace(":", "_")
						.Replace("/", "_");

		AuthToken.TryGetToken(_authTokenKey+TokenAppend, out var token);

		Conn = DbConnection.Builder()
			.WithUri(Host)
			.WithDatabaseName(DbName)
			.WithToken(token)
			.OnConnect(OnConnected)
			.OnConnectError(OnConnectError)
			.OnDisconnect(OnDisconnected)
			.Build();
	}

	private void OnConnected(DbConnection conn, Identity identity, string token)
	{
		LocalIdentity = identity;
		AuthToken.SaveToken(token, _authTokenKey+TokenAppend);

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
		// _ExitTree disconnects deliberately — no Disconnected signal (no panel) for that.
		if (_shuttingDown) return;
		EmitSignal(SignalName.Disconnected);
	}

	public bool IsLocal(Identity id) => LocalIdentity.HasValue && id == LocalIdentity.Value;

	private bool _shuttingDown;

	public override void _ExitTree()
	{
		_shuttingDown = true;
		if (Conn?.IsActive == true)
			Conn.Disconnect();
		if (Instance == this) Instance = null;
	}
}
