#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class GameManager : Node
{
	public static GameManager Instance { get; private set; } = null!;

	public DbConnection? Conn { get; private set; }
	public Identity? LocalIdentity { get; private set; }

	[Export] public string Host { get; set; } = "http://127.0.0.1:3000";
	[Export] public string DbName { get; set; } = "bullethell";

	[Export] public PackedScene LocalPlayerScene { get; set; } = null!;
	[Export] public PackedScene NonLocalPlayerScene { get; set; } = null!;
	[Export] public PackedScene EnemyScene { get; set; } = null!;
	[Export] public PackedScene DropScene { get; set; } = null!;
	[Export] public PackedScene BulletManagerScene { get; set; } = null!;

	private LocalPlayer? localPlayer;
	private BulletManager? bulletManager;
	private readonly Dictionary<string, NonLocalPlayer> remotePlayers = new();
	private readonly Dictionary<ulong, Enemy> enemies = new();
	private readonly Dictionary<ulong, Drop> drops = new();
	private readonly Dictionary<string, string> textureCache = new();

	private CanvasLayer? lobbyLayer;
	private Button? joinButton;
	private SubscriptionHandle? lobbySub;

	public override void _Ready()
	{
		if (Instance != null) return;
		Instance = this;
		Connect(Host, DbName);
		ShowLobby();
	}

	public override void _Process(double delta)
	{
		Conn?.FrameTick();
	}

	// ── Lobby ─────────────────────────────────────────────────────────────────

	public void ShowLobby()
	{
		lobbyLayer = new CanvasLayer();
		joinButton = new Button { Text = "Join World" };
		joinButton.Pressed += OnJoinPressed;
		lobbyLayer.AddChild(joinButton);
		AddChild(lobbyLayer);
	}

	private void HideLobby()
	{
		if (lobbyLayer == null) return;
		joinButton!.Disabled = true;
		lobbyLayer.Visible = false;
		lobbyLayer.QueueFree();
		lobbyLayer = null;
		joinButton = null;
	}

	private void OnJoinPressed()
	{
		if (Conn == null) return;
		var profile = Conn.Db.LocalPlayerProfiles.Iter()
			.FirstOrDefault(p => p.ProfileName == "Knight");
		if (profile == null)
		{
			GD.PrintErr("[GameManager] Knight profile not found — not yet synced or missing.");
			return;
		}
		HideLobby();
		Conn.Reducers.JoinWorld(profile.ProfileId);
		SubscribeGame();
		lobbySub?.Unsubscribe();
		lobbySub = null;
	}

	// ── Connection ────────────────────────────────────────────────────────────

	public void Connect(string host, string dbName)
	{
		Conn = DbConnection.Builder()
			.WithUri(host)
			.WithDatabaseName(dbName)
			.WithToken(null)
			.OnConnect(OnConnected)
			.OnConnectError(OnConnectError)
			.OnDisconnect(OnDisconnected)
			.Build();
	}

	private void OnConnected(DbConnection conn, Identity identity, string token)
	{
		LocalIdentity = identity;
		//AuthToken.SaveToken(token);

		conn.Db.LocalPlayer.OnInsert += OnLocalPlayerInsert;
		conn.Db.LocalPlayer.OnDelete += OnLocalPlayerDelete;
		conn.Db.NearbyRemotePlayers.OnInsert += OnNearbyRemotePlayerInsert;
		conn.Db.NearbyRemotePlayers.OnDelete += OnNearbyRemotePlayerDelete;
		conn.Db.NearbyEnemies.OnInsert += OnEnemyInsert;
		conn.Db.NearbyEnemies.OnDelete += OnEnemyDelete;
		conn.Db.LootDrop.OnInsert += OnDropInsert;
		conn.Db.LootDrop.OnDelete += OnDropDelete;

		conn.Db.AllTextures.OnInsert += (_, texture) => textureCache[texture.TextureId] = texture.ResPath;
		conn.Db.AllTextures.OnUpdate += (_, _, texture) => textureCache[texture.TextureId] = texture.ResPath;
		conn.Db.AllTextures.OnDelete += (_, texture) => textureCache.Remove(texture.TextureId);

		conn.SubscriptionBuilder()
			.AddQuery(q => q.From.AllTextures())
			.Subscribe();

		lobbySub = conn.SubscriptionBuilder()
			.AddQuery(q => q.From.LocalLobbyPlayer())
			.AddQuery(q => q.From.LocalPlayerProfiles())
			.OnApplied(OnLobbySubApplied)
			.Subscribe();
	}

	private void SubscribeGame()
	{
		Conn!.SubscriptionBuilder()
			.AddQuery(q => q.From.LocalPlayer())
			.AddQuery(q => q.From.LocalPlayerData())
			.AddQuery(q => q.From.LocalPlayerStats())
			.AddQuery(q => q.From.LocalPlayerInventory())
			.AddQuery(q => q.From.LocalPlayerPosition())
			.AddQuery(q => q.From.LocalPlayerProfiles())
			.AddQuery(q => q.From.NearbyRemotePlayers())
			.AddQuery(q => q.From.NearbyRemotePlayersProfiles())
			.AddQuery(q => q.From.NearbyEnemies())
			.AddQuery(q => q.From.EnemyTemplates())
			.AddQuery(q => q.From.BulletPatternEvent())
			.AddQuery(q => q.From.MapConfig())
			.OnApplied(OnGameSubApplied)
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

	private void OnLobbySubApplied(SubscriptionEventContext ctx)
	{
		GD.Print("[GameManager] Lobby subscription ready");
	}

	private void OnGameSubApplied(SubscriptionEventContext ctx)
	{
		GD.Print("[GameManager] Game subscription ready");
	}

	// ── Entity lifecycle ──────────────────────────────────────────────────────

	private void OnLocalPlayerInsert(EventContext _, LoggedInPlayer loggedInPlayer)
	{
		if (localPlayer == null)
		{
			localPlayer = LocalPlayerScene.Instantiate<LocalPlayer>();
			GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, localPlayer);
		}
		if (bulletManager == null)
		{
			bulletManager = BulletManagerScene.Instantiate<BulletManager>();
			GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, bulletManager);
		}
	}

	private void OnLocalPlayerDelete(EventContext _, LoggedInPlayer loggedInPlayer)
	{
		if (localPlayer != null && IsInstanceValid(localPlayer))
			localPlayer.QueueFree();
		localPlayer = null;
		if (bulletManager != null && IsInstanceValid(bulletManager))
			bulletManager.QueueFree();
		bulletManager = null;
	}

	private void OnNearbyRemotePlayerInsert(EventContext _, PlayerPosition position)
	{
		var key = position.PlayerId.ToString();
		if (IsLocal(position.PlayerId) || remotePlayers.ContainsKey(key)) return;
		var node = NonLocalPlayerScene.Instantiate<NonLocalPlayer>();
		node.PlayerId = position.PlayerId;
		node.ProfileId = position.ProfileId;
		node.GlobalPosition = new Vector2(position.X, position.Y);
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, node);
		remotePlayers[key] = node;
	}

	private void OnNearbyRemotePlayerDelete(EventContext _, PlayerPosition position)
	{
		var key = position.PlayerId.ToString();
		if (!remotePlayers.TryGetValue(key, out var node)) return;
		remotePlayers.Remove(key);
		if (IsInstanceValid(node))
			node.QueueFree();
	}

	private void OnEnemyInsert(EventContext _, SpacetimeDB.Types.Enemy enemy)
	{
		if (enemies.ContainsKey(enemy.EnemyId)) return;
		var node = EnemyScene.Instantiate<Enemy>();
		node.EnemyId = enemy.EnemyId;
		node.GlobalPosition = new Vector2(enemy.X, enemy.Y);
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, node);
		enemies[enemy.EnemyId] = node;
	}

	private void OnEnemyDelete(EventContext _, SpacetimeDB.Types.Enemy enemy)
	{
		if (!enemies.TryGetValue(enemy.EnemyId, out var node)) return;
		enemies.Remove(enemy.EnemyId);
		if (IsInstanceValid(node))
			node.QueueFree();
	}

	private void OnDropInsert(EventContext _, LootDrop lootDrop)
	{
		if (drops.ContainsKey(lootDrop.DropId)) return;
		var node = DropScene.Instantiate<Drop>();
		node.DropId = lootDrop.DropId;
		node.GlobalPosition = new Vector2(lootDrop.X, lootDrop.Y);
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, node);
		drops[lootDrop.DropId] = node;
	}

	private void OnDropDelete(EventContext _, LootDrop lootDrop)
	{
		if (!drops.TryGetValue(lootDrop.DropId, out var node)) return;
		drops.Remove(lootDrop.DropId);
		if (IsInstanceValid(node))
			node.QueueFree();
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	public bool IsLocal(Identity id) => LocalIdentity.HasValue && id == LocalIdentity.Value;

	public string? GetResPath(string textureId) =>
		textureCache.TryGetValue(textureId, out var path) ? path : null;

	public override void _ExitTree()
	{
		if (Conn?.IsActive == true)
			Conn.Disconnect();
	}
}
