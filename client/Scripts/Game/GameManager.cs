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
	public Vector2 LapQ { get; private set; } = Vector2.Zero;
	public Vector2 LapR { get; private set; } = Vector2.Zero;

	[Export] public string Host { get; set; } = "http://127.0.0.1:3000";
	[Export] public string DbName { get; set; } = "bullethell";

	[Export] public PackedScene LocalPlayerScene { get; set; } = null!;
	[Export] public PackedScene NonLocalPlayerScene { get; set; } = null!;
	[Export] public PackedScene EnemyScene { get; set; } = null!;
	[Export] public PackedScene DropScene { get; set; } = null!;
	[Export] public PackedScene BulletManagerScene { get; set; } = null!;
	[Export] public PackedScene PlayerProfileScene { get; set; } = null!;

	private LocalPlayer? localPlayer;
	private BulletManager? bulletManager;
	private TerrainManager? terrainOverlay;
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
	private SubscriptionHandle? gameSub;

	public override void _Ready()
	{
		if (Instance != null) return;
		Instance = this;
		Connect(Host, DbName);
		ShowLobby();
		AddChild(new DebugOverlay());
		if (Conn == null) return;
		Conn.Db.LocalPlayerProfiles.OnInsert += (ctx, profile) => 
		{
			Control profile_panel = PlayerProfileScene.Instantiate<Control>();
			profile_panel.GetNode<Label>("Name").Text = profile.ProfileName;
			profile_panel.GetNode<Label>("ID").Text = profile.ProfileId.ToString();
			profile_panel.GetNode<Button>("Join").Pressed += () => OnJoinPressed(profile);
			profile_panel.GetNode<Button>("Delete").Pressed += () => OnDeletePressed(profile);
			GetNode("CharSlots/ScrollContainer/VBoxContainer").AddChild(profile_panel);
			GD.Print($"Successfully synced a profile: {profile.ProfileName}, TextureID: {profile.TextureId}");
		};
	}

	public override void _Process(double delta)
	{
		Conn?.FrameTick();
	}

	// ── Lobby ─────────────────────────────────────────────────────────────────

	public void ShowLobby()
	{
		GetNode<Control>("CharSlots").Show();
	}

	private void HideLobby()
	{
		GetNode<Control>("CharSlots").Hide();
	}

	private void OnJoinPressed(PlayerProfile profile)
	{
		if (Conn == null || GetNode<Control>("CharSlots/CreateProfilePanel").Visible) return;
		// var profile = Conn.Db.LocalPlayerProfiles.Iter()
		// 	.FirstOrDefault(p => p.ProfileName == "Knight");
		// if (profile == null)
		// {
		// 	GD.PrintErr("[GameManager] Knight profile not found — not yet synced or missing.");
		// 	return;
		// }
		HideLobby();
		Conn.Reducers.JoinWorld(profile.ProfileId);
		SubscribeGame();
		lobbySub?.Unsubscribe();
		lobbySub = null;
	}

	private void OnDeletePressed(PlayerProfile profile)
	{
		if (Conn == null || GetNode<Control>("CharSlots/CreateProfilePanel").Visible) return;
		Conn.Reducers.DeleteProfile(profile.ProfileId);
		foreach (Control x in GetNode<Control>("CharSlots/ScrollContainer/VBoxContainer").GetChildren()){
			if (x.GetNode<Label>("ID").Text.ToInt() == (int)profile.ProfileId) x.QueueFree();
		}
	}

	private void OnCreatePanelPressed()
	{
		GetNode<Control>("CharSlots/CreateProfilePanel").Visible = !GetNode<Control>("CharSlots/CreateProfilePanel").Visible;
	}

	private void OnCreatePressed()
	{
		if (Conn == null || !GetNode<Control>("CharSlots/CreateProfilePanel").Visible) return;
		if (GetNode<LineEdit>("CharSlots/CreateProfilePanel/Panel/LineEdit").Text.Length > 0) Conn.Reducers.CreateProfile(GetNode<LineEdit>("CharSlots/CreateProfilePanel/Panel/LineEdit").Text, "Knight");
		GetNode<Control>("CharSlots/CreateProfilePanel").Hide();
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
		conn.Db.NearbyLootDrops.OnInsert += OnDropInsert;
		conn.Db.NearbyLootDrops.OnDelete += OnDropDelete;

		conn.Db.MapConfig.OnInsert += (_, cfg) => { LapQ = new Vector2(cfg.LapQX, cfg.LapQY); LapR = new Vector2(cfg.LapRX, cfg.LapRY); };
		conn.Db.MapConfig.OnUpdate += (_, _, cfg) => { LapQ = new Vector2(cfg.LapQX, cfg.LapQY); LapR = new Vector2(cfg.LapRX, cfg.LapRY); };

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

		SubscribeLobby();
	}

	private void SubscribeLobby()
	{
		lobbySub = Conn!.SubscriptionBuilder()
			.AddQuery(q => q.From.LocalLobbyPlayer())
			.AddQuery(q => q.From.LocalPlayerProfiles())
			.OnApplied(OnLobbySubApplied)
			.Subscribe();
	}

	private void SubscribeGame()
	{
		gameSub = Conn!.SubscriptionBuilder()
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
			.AddQuery(q => q.From.NearbyLootDrops())
			.AddQuery(q => q.From.NearbyTerrainTiles())
			.AddQuery(q => q.From.NearbyHexDecor())
			.AddQuery(q => q.From.EnemyTemplates())
			.AddQuery(q => q.From.BulletPatternEvent())
			.AddQuery(q => q.From.MapConfig())
			.OnApplied(OnGameSubApplied)
			.OnError(OnGameSubError)
			.Subscribe();
	}

	private void OnGameSubError(ErrorContext ctx, Exception e)
	{
		GD.PrintErr($"[GameManager] Game subscription error: {e.Message}");
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
		if (terrainOverlay == null)
		{
			terrainOverlay = new TerrainManager();
			GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, terrainOverlay);
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
		if (terrainOverlay != null && IsInstanceValid(terrainOverlay))
			terrainOverlay.QueueFree();
		terrainOverlay = null;

		if (Conn?.IsActive != true) return;

		ShowLobby();
		SubscribeLobby();
		gameSub?.Unsubscribe();
		gameSub = null;
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
		GD.Print("Drop Inserted");
		if (drops.ContainsKey(lootDrop.DropId)) return;
		GD.Print("Drop Being Instantiated");
		var node = DropScene.Instantiate<Drop>();
		node.DropId = lootDrop.DropId;
		node.ItemId = lootDrop.ItemId;
		node.DroppedBy = lootDrop.DroppedBy;
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
