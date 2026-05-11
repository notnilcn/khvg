/* Importing, declaring, assigning things.
```c# */
#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;

/// Autoload singleton. Owns the SpacetimeDB connection and calls FrameTick every frame.
/// Access via SpacetimeDBManager.Instance or the autoload name from GDScript.
public partial class SpacetimeDBManager : Node
{

	public static SpacetimeDBManager Instance { get; private set; } = null!;

	// Verify this URI by running: spacetime server list
	private const string Host = "wss://maincloud.spacetimedb.com";
	private const string DbName = "bmmo-0nzf1";

	public DbConnection? Conn { get; private set; }
	public bool IsStdbConnected => Conn?.IsActive ?? false;
	public Identity? LocalIdentity { get; private set; }
	public bool IsSubscriptionReady { get; private set; }

	[Signal] public delegate void ConnectedToServerEventHandler(string identity);
	[Signal] public delegate void DisconnectedFromServerEventHandler();
	[Signal] public delegate void SubscriptionReadyEventHandler();
/*
```
*/

/*
# `_Ready()`
```c# */
	public override void _Ready()
	{
		Instance = this;

		// --elevated-client  → dedicated token so the elevated identity is stable.
		// --p2, --p3, etc.   → separate token for local multiplayer testing.
		string suffix = "";
		foreach (var arg in OS.GetCmdlineArgs())
		{
			if (arg == "--elevated-client") { suffix = "_elevated"; break; } // ^--elevated-client
			if (arg.StartsWith("--p"))      { suffix = "_" + arg[2..]; break; }
		}
		AuthToken.Init(OS.GetUserDataDir() + "/.bmmo_token" + suffix);

		try
		{
			Conn = DbConnection.Builder()
				.WithUri(Host)
				.WithDatabaseName(DbName)
				.WithToken(AuthToken.Token)
				.OnConnect(OnConnected) // Connects to the OnConnected function. This function is available here cus the _Ready() function is special
				.OnConnectError(OnConnectError)
				.OnDisconnect(OnDisconnected)
				.Build();
		}
		catch (Exception e)
		{
			GD.PrintErr($"[STDB] Failed to build connection: {e.Message}");
		}
	}
/*
```
*/

/*
```c# */
	public override void _Process(double delta)
	{
		// All SpacetimeDB callbacks fire synchronously here on the main thread.
		Conn?.FrameTick();
	}
/*
```
*/

/*
```c# */
	public override void _ExitTree()
	{
		if (Conn?.IsActive == true)
			Conn.Disconnect();
	}
/*
```
*/

/*
# Connection callbacks
```c# */

	private void OnConnected(DbConnection conn, Identity identity, string token) // ^OnConnected
	{
		LocalIdentity = identity;
		GD.Print($"[STDB] Connected. Identity: {identity}");
		AuthToken.SaveToken(token);

		if (ElevatedClient.IsActive)
			conn.Reducers.ClaimElevated(); // ^claim-elevated-client-spot

		// Emit before subscribing so listeners can register table callbacks
		// before initial OnInsert events arrive (mirrors blackholio GameManager pattern).
		EmitSignal(SignalName.ConnectedToServer, identity.ToString()); // ^ConnectedToServer

		conn.SubscriptionBuilder()
			.OnApplied(OnSubscriptionApplied)
			.SubscribeToAllTables();
	}

	private void OnConnectError(Exception e) // ^OnConnectError
	{
		GD.PrintErr($"[STDB] Connection error: {e.Message}");
	}

	private void OnDisconnected(DbConnection conn, Exception? e) // ^OnDisconnected
	{
		if (e != null)
			GD.PrintErr($"[STDB] Disconnected with error: {e.Message}");
		else
			GD.Print("[STDB] Disconnected cleanly.");

		EmitSignal(SignalName.DisconnectedFromServer);
	}

	private void OnSubscriptionApplied(SubscriptionEventContext ctx) // ^OnSubscriptionApplied
	{
		IsSubscriptionReady = true;
		GD.Print("[STDB] Subscription ready — table data available.");
		EmitSignal(SignalName.SubscriptionReady); // ^SubscriptionReady
	}
/*
```
*/
}
