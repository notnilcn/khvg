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
	public string Username { get; private set; } = "";

	[Export] public string Host { get; set; } = "http://127.0.0.1:3000";
	[Export] public string DbName { get; set; } = "bullethell";

	[Export] public PackedScene LocalPlayerScene { get; set; } = null!;
	[Export] public PackedScene NonLocalPlayerScene { get; set; } = null!;
	[Export] public PackedScene EnemyScene { get; set; } = null!;
	[Export] public PackedScene DropScene { get; set; } = null!;
	[Export] public PackedScene BulletManagerScene { get; set; } = null!;

	private LocalPlayer? localPlayer;
	private BulletManager? bulletManager;
	private readonly Dictionary<string, RemotePlayer> remotePlayers = new();
	private readonly Dictionary<ulong, Enemy> enemies = new();
	private readonly Dictionary<ulong, Drop> drops = new();
	private readonly Dictionary<string, string> textureCache = new();
	private readonly Dictionary<string, SpacetimeDB.Types.Item> itemCache = new();
	private readonly Dictionary<string, SpacetimeDB.Types.Weapon> weaponCache = new();
	private readonly Dictionary<string, SpacetimeDB.Types.Armor> armorCache = new();
	private readonly Dictionary<string, SpacetimeDB.Types.Artifact> artifactCache = new();
	private readonly Dictionary<string, SpacetimeDB.Types.Accessory> accessoryCache = new();
	private readonly Dictionary<string, SpacetimeDB.Types.Consumable> consumableCache = new();

	private CanvasLayer? lobbyLayer;
	private Button? joinButton;
	private SubscriptionHandle? lobbySub;

	public override void _Ready()
	{
		if (Instance != null) return;
		Instance = this;
		Connect(Host, DbName);
		ShowLobby();
		AddChild(new DebugOverlay());
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

		conn.Db.LocalLobbyPlayer.OnInsert += (_, p) => Username = p.Username;
		conn.Db.LocalLobbyPlayer.OnUpdate += (_, _, p) => Username = p.Username;
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

		conn.Db.AllItems.OnInsert += (_, item) => itemCache[item.ItemId] = item;
		conn.Db.AllItems.OnUpdate += (_, _, item) => itemCache[item.ItemId] = item;
		conn.Db.AllItems.OnDelete += (_, item) => itemCache.Remove(item.ItemId);

		conn.Db.AllWeapons.OnInsert += (_, weapon) => weaponCache[weapon.WeaponId] = weapon;
		conn.Db.AllWeapons.OnUpdate += (_, _, weapon) => weaponCache[weapon.WeaponId] = weapon;
		conn.Db.AllWeapons.OnDelete += (_, weapon) => weaponCache.Remove(weapon.WeaponId);

		conn.Db.AllArmor.OnInsert += (_, armor) => armorCache[armor.ArmorId] = armor;
		conn.Db.AllArmor.OnUpdate += (_, _, armor) => armorCache[armor.ArmorId] = armor;
		conn.Db.AllArmor.OnDelete += (_, armor) => armorCache.Remove(armor.ArmorId);

		conn.Db.AllArtifacts.OnInsert += (_, artifact) => artifactCache[artifact.ArtifactId] = artifact;
		conn.Db.AllArtifacts.OnUpdate += (_, _, artifact) => artifactCache[artifact.ArtifactId] = artifact;
		conn.Db.AllArtifacts.OnDelete += (_, artifact) => artifactCache.Remove(artifact.ArtifactId);

		conn.Db.AllAccessories.OnInsert += (_, accessory) => accessoryCache[accessory.AccessoryId] = accessory;
		conn.Db.AllAccessories.OnUpdate += (_, _, accessory) => accessoryCache[accessory.AccessoryId] = accessory;
		conn.Db.AllAccessories.OnDelete += (_, accessory) => accessoryCache.Remove(accessory.AccessoryId);

		conn.Db.AllConsumables.OnInsert += (_, consumable) => consumableCache[consumable.ConsumableId] = consumable;
		conn.Db.AllConsumables.OnUpdate += (_, _, consumable) => consumableCache[consumable.ConsumableId] = consumable;
		conn.Db.AllConsumables.OnDelete += (_, consumable) => consumableCache.Remove(consumable.ConsumableId);

		conn.SubscriptionBuilder()
			.AddQuery(q => q.From.AllTextures())
			.AddQuery(q => q.From.AllItems())
			.AddQuery(q => q.From.AllWeapons())
			.AddQuery(q => q.From.AllArmor())
			.AddQuery(q => q.From.AllArtifacts())
			.AddQuery(q => q.From.AllAccessories())
			.AddQuery(q => q.From.AllConsumables())
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
			.AddQuery(q => q.From.LocalPlayerActiveProfile())
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
		var node = NonLocalPlayerScene.Instantiate<RemotePlayer>();
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
	public Enemy? GetEnemy(ulong enemyId) => enemies.TryGetValue(enemyId, out var node) ? node : null;
	public int EnemyCount => enemies.Count;

	public string? GetResPath(string textureId) =>
		textureCache.TryGetValue(textureId, out var path) ? path : null;

	public Item? GetItem(string itemId) => itemCache.TryGetValue(itemId, out var item) ? item : null;
	public Weapon? GetWeapon(string itemId) => weaponCache.TryGetValue(itemId, out var weapon) ? weapon : null;
	public Armor? GetArmor(string itemId) => armorCache.TryGetValue(itemId, out var armor) ? armor : null;
	public Artifact? GetArtifact(string itemId) => artifactCache.TryGetValue(itemId, out var artifact) ? artifact : null;
	public Accessory? GetAccessory(string itemId) => accessoryCache.TryGetValue(itemId, out var accessory) ? accessory : null;
	public Consumable? GetConsumable(string itemId) => consumableCache.TryGetValue(itemId, out var consumable) ? consumable : null;

	public override void _ExitTree()
	{
		if (Conn?.IsActive == true)
			Conn.Disconnect();
	}
}
