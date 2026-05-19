#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using EnemyRow = SpacetimeDB.Types.Enemy;
using LootDropRow = SpacetimeDB.Types.LootDrop;
using PlayerRow = SpacetimeDB.Types.Player;

public partial class GameManager : Node
{
	public static GameManager Instance { get; private set; } = null!;

	public DbConnection? Conn { get; private set; }
	public Identity? LocalIdentity { get; private set; }

	[Export] public string Host   { get; set; } = "http://127.0.0.1:3000";
	[Export] public string DbName { get; set; } = "bullethell";

	[Export] public PackedScene LocalPlayerScene    { get; set; } = null!;
	[Export] public PackedScene NonLocalPlayerScene { get; set; } = null!;
	[Export] public PackedScene EnemyScene          { get; set; } = null!;
	[Export] public PackedScene DropScene           { get; set; } = null!;

	private LocalPlayer? _localPlayer;
	private readonly Dictionary<string, NonLocalPlayer> _remotePlayers = new();
	private readonly Dictionary<ulong, Enemy>           _enemies       = new();
	private readonly Dictionary<ulong, Drop>            _drops         = new();

	private CanvasLayer? _lobbyLayer;
	private Button?      _joinButton;

	public override void _Ready()
	{
		if (Instance != null) return;
		Instance = this;
		ShowLobby();
	}

	public override void _Process(double delta)
	{
		Conn?.FrameTick();
	}

	// ── Lobby ─────────────────────────────────────────────────────────────────

	public void ShowLobby()
	{
		_lobbyLayer = new CanvasLayer();
		_joinButton = new Button { Text = "Join World" };
		_joinButton.Pressed += OnJoinPressed;
		_lobbyLayer.AddChild(_joinButton);
		AddChild(_lobbyLayer);
	}

	private void HideLobby()
	{
		if (_lobbyLayer == null) return;
		_joinButton!.Disabled = true;
		_lobbyLayer.Visible   = false;
		_lobbyLayer.QueueFree();
		_lobbyLayer = null;
		_joinButton = null;
	}

	private void OnJoinPressed()
	{
		HideLobby();
		Connect(Host, DbName);
	}

	// ── Connection ────────────────────────────────────────────────────────────

	public void Connect(string host, string dbName)
	{
		AuthToken.Init(OS.GetUserDataDir() + "/.bullethell_token_");
		AuthToken.Init();
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

		conn.Db.LocalPlayer.OnInsert          += OnLocalPlayerInsert;
		conn.Db.LocalPlayer.OnDelete          += OnLocalPlayerDelete;
		conn.Db.NearbyRemotePlayers.OnInsert  += OnNearbyRemotePlayerInsert;
		conn.Db.NearbyRemotePlayers.OnDelete  += OnNearbyRemotePlayerDelete;
		conn.Db.NearbyEnemies.OnInsert        += OnEnemyInsert;
		conn.Db.NearbyEnemies.OnDelete        += OnEnemyDelete;
		conn.Db.LootDrop.OnInsert             += OnDropInsert;
		conn.Db.LootDrop.OnDelete             += OnDropDelete;

		conn.SubscriptionBuilder()
			.AddQuery(q => q.From.LocalPlayer())
			.AddQuery(q => q.From.LocalPlayerData())
			.AddQuery(q => q.From.LocalPlayerStats())
			.AddQuery(q => q.From.LocalPlayerInventory())
			.AddQuery(q => q.From.LocalPlayerPosition())
			.AddQuery(q => q.From.NearbyRemotePlayers())
			.AddQuery(q => q.From.NearbyEnemies())
			.AddQuery(q => q.From.EnemyTemplates())
			.AddQuery(q => q.From.BulletPatternEvent())
			.OnApplied(OnSubscriptionApplied)
			.Subscribe();
	}

	private void OnConnectError(Exception e)
	{
		GD.PrintErr($"[GameManager] Connection error: {e.Message}");
	}

	private void OnDisconnected(DbConnection conn, Exception? e)
	{
		if (e != null)
			GD.PrintErr($"[GameManager] Disconnected: {e.Message}");
	}

	private void OnSubscriptionApplied(SubscriptionEventContext ctx)
	{
		GD.Print("[GameManager] Subscription ready");
		// TODO: read initial rows from client cache here (e.g. catch up entities
		// that already existed before subscription was applied).
	}

	// ── Entity lifecycle ──────────────────────────────────────────────────────

	private void OnLocalPlayerInsert(EventContext ctx, PlayerRow row)
	{
		if (_localPlayer != null) return;
		_localPlayer = LocalPlayerScene.Instantiate<LocalPlayer>();
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, _localPlayer);
	}

	private void OnLocalPlayerDelete(EventContext ctx, PlayerRow row)
	{
		if (_localPlayer != null && IsInstanceValid(_localPlayer))
			_localPlayer.QueueFree();
		_localPlayer = null;
	}

	private void OnNearbyRemotePlayerInsert(EventContext ctx, PlayerPosition row)
	{
		var key = row.PlayerId.ToString();
		if (IsLocal(row.PlayerId) || _remotePlayers.ContainsKey(key)) return;
		var node = NonLocalPlayerScene.Instantiate<NonLocalPlayer>();
		node.PlayerId       = row.PlayerId;
		node.GlobalPosition = new Vector2(row.X, row.Y);
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, node);
		_remotePlayers[key] = node;
	}

	private void OnNearbyRemotePlayerDelete(EventContext ctx, PlayerPosition row)
	{
		var key = row.PlayerId.ToString();
		if (!_remotePlayers.TryGetValue(key, out var node)) return;
		_remotePlayers.Remove(key);
		if (IsInstanceValid(node))
			node.QueueFree();
	}

	private void OnEnemyInsert(EventContext ctx, EnemyRow row)
	{
		if (_enemies.ContainsKey(row.EnemyId)) return;
		var enemy = EnemyScene.Instantiate<Enemy>();
		enemy.EnemyId = row.EnemyId;
		enemy.GlobalPosition = new Vector2(row.X, row.Y);
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, enemy);
		_enemies[row.EnemyId] = enemy;
	}

	private void OnEnemyDelete(EventContext ctx, EnemyRow row)
	{
		if (!_enemies.TryGetValue(row.EnemyId, out var enemy)) return;
		_enemies.Remove(row.EnemyId);
		if (IsInstanceValid(enemy))
			enemy.QueueFree();
	}

	private void OnDropInsert(EventContext ctx, LootDropRow row)
	{
		if (_drops.ContainsKey(row.DropId)) return;
		var drop = DropScene.Instantiate<Drop>();
		drop.DropId = row.DropId;
		drop.GlobalPosition = new Vector2(row.X, row.Y);
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, drop);
		_drops[row.DropId] = drop;
	}

	private void OnDropDelete(EventContext ctx, LootDropRow row)
	{
		if (!_drops.TryGetValue(row.DropId, out var drop)) return;
		_drops.Remove(row.DropId);
		if (IsInstanceValid(drop))
			drop.QueueFree();
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	public bool IsLocal(Identity id) => LocalIdentity.HasValue && id == LocalIdentity.Value;

	public override void _ExitTree()
	{
		if (Conn?.IsActive == true)
			Conn.Disconnect();
	}
}
