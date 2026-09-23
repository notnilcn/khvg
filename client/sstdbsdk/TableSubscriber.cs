#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Owns the subscription waves (base/lobby/game) and their applied/error handlers, plus
/// the MapConfig lap vectors those subscriptions deliver. Subscribes the base and lobby
/// waves when the DatabaseConnector autoload connects; LobbyComponent and
/// EntitySpawnerComponent switch waves on join/death (logic moved out of GameManager.cs).
///
/// The wave contents are the static BaseTables/LobbyTables/GameTables name lists below —
/// the single source of truth for "what is subscribed", also used by TableBinderComponent
/// to populate its table-name dropdown. Names are the PascalCase RemoteTables field names.
/// </summary>
public partial class TableSubscriber : Component
{
	/// Base wave: static catalog tables every screen needs.
	public static readonly string[] BaseTables = { "AllTextures", "AllItems", "AllEnchantments", "AllAnimations" };

	/// Lobby wave: profile selection before joining the world.
	public static readonly string[] LobbyTables = { "LocalLobbyPlayer", "LocalPlayerProfiles" };

	/// Game wave: everything the live world needs.
	public static readonly string[] GameTables =
	{
		"LocalPlayer", "LocalPlayerData", "LocalPlayerStats", "LocalPlayerInventory",
		"LocalPlayerPosition", "LocalPlayerActiveProfile", "LocalPlayerProfiles",
		"LocalPlayerStatAllocation",
		"NearbyRemotePlayers", "NearbyRemotePlayersProfiles", "NearbyRemotePlayerRotations", "NearbyEnemies",
		"NearbyLootDrops", "NearbyTerrainTiles", "NearbyHexDecor", "EnemyTemplates",
		// AOI-filtered BuildingTile rows (one per hex, carries the elevation level) —
		// drives ElevationMapComponent. Persistent view table: binder replay OK.
		"NearbyBuildingTiles",
		// AOI chest views — NearbyChests drives chest spawning, NearbyChestItems the open
		// chest panel's contents (both persistent view tables: binder replay OK).
		"NearbyChests", "NearbyChestItems",
		// Per-caller explored-map snapshots (PLAN §4.2) — one row per revealed terrain AOI
		// chunk, carrying the chunk's hex payload; drives ExploredMapComponent3D's memory
		// layer. Persistent view table: binder replay OK.
		"LocalPlayerExplored",
		"BulletPatternEvent", "BulletControlEvent", "MapConfig",
		// Raw/unfiltered spell-visual event table (P5) — same known scaling debt as the
		// other event tables; drives SpellVisualComponent. Event table: replay stays OFF.
		"AbilityVisualEvent",
		// Raw public status table (no view) — acceptable at co-op scale; drives
		// StatusBarComponent (filtered to the local profile there).
		"ActiveStatusEffect",
		// Raw public enemy-debuff table (P2 rows, subscribed at P6) — same co-op-scale
		// raw subscription as ActiveStatusEffect; drives EnemyDebuffIndicatorComponent
		// (filtered per enemy instance there). Persistent table: binder replay OK.
		"ActiveEnemyStatusEffect",
		// Raw public ground-zone table (P3) — same co-op-scale raw subscription as
		// ActiveStatusEffect; drives ZoneRendererComponent.
		"GroundZone",
		// Raw public charge table (P4, no view) — one row per charging player, filtered
		// to the local profile by AbilityChargeComponent. Persistent table, replay OK.
		"AbilityCharge",
		// Raw public channeled-action table (Phase 1 progressive actions, no view) — one
		// row per channel, filtered per profile/target in code. Persistent: replay OK.
		"ChanneledAction",
		// Per-caller knowledge view (Phase 1 discovery gating) — drives the ItemSidebar
		// ??? masking. Persistent view table: binder replay OK.
		"LocalPlayerKnowledge",
		// Per-caller trade session view (Phase 3) — drives the trade offer panel.
		// Persistent view table: binder replay OK.
		"LocalTradeSession",
		// Global territory view (Phase 4 claims-lite) — drives hex tint overlays.
		// Persistent view table: binder replay OK.
		"AllTerritories",
		// Global channel list (Phase 5 chat) — drives the channel picker.
		// Persistent view table: binder replay OK.
		"AllChatChannels",
		// Per-caller chat history (Phase 5 — member channels only, bounded by the
		// retention sweep; never full-table replay). Persistent view: replay OK.
		"LocalChatMessages",
	};

	/// Every table name across all waves — TableBinderComponent's dropdown source.
	public static IEnumerable<string> AllSubscribedTables =>
		BaseTables.Concat(LobbyTables).Concat(GameTables).Distinct();

	/// MapConfig torus lap vectors, mirrored from the subscribed row (zero before it arrives).
	/// Static because the MapConfig row is a world singleton and every InterpolationComponent
	/// reads these per entity per frame — one shared copy saves a GameManager facade lookup
	/// (registry scan) per read.
	public static Vector2 LapQ { get; private set; } = Vector2.Zero;
	public static Vector2 LapR { get; private set; } = Vector2.Zero;

	private SubscriptionHandle? lobbySub;
	private SubscriptionHandle? gameSub;

	/// Raised when the lobby subscription wave is applied (all profile rows cached) —
	/// LobbyComponent's --charN auto-join waits for this before counting profiles.
	public event Action? LobbyApplied;

	/// True once the lobby wave has been applied (a re-shown lobby's rows are already cached).
	public bool LobbySubActive => lobbySub?.IsActive ?? false;

	protected override void OnRegistered()
	{
		var connector = DatabaseConnector.Instance;
		if (connector == null) return;
		// The autoload can finish connecting before Main loads — handle both orders.
		if (connector.Conn?.IsActive == true) OnConnected();
		else connector.Connected += OnConnected;
	}

	private void OnConnected()
	{
		var conn = DatabaseConnector.Instance?.Conn;
		if (conn == null) return;

		conn.Db.MapConfig.OnInsert += OnMapConfigInsert;
		conn.Db.MapConfig.OnUpdate += OnMapConfigUpdate;

		conn.SubscriptionBuilder().Subscribe(TablesSql(BaseTables));

		SubscribeLobby();
	}

	private void OnMapConfigInsert(EventContext ctx, MapConfig cfg) => SetLap(cfg);

	private void OnMapConfigUpdate(EventContext ctx, MapConfig oldCfg, MapConfig cfg) => SetLap(cfg);

	private void SetLap(MapConfig cfg)
	{
		LapQ = new Vector2(cfg.LapQX, cfg.LapQY);
		LapR = new Vector2(cfg.LapRX, cfg.LapRY);
	}

	public void SubscribeLobby()
	{
		lobbySub = DatabaseConnector.Instance?.Conn?.SubscriptionBuilder()
			.OnApplied(OnLobbySubApplied)
			.Subscribe(TablesSql(LobbyTables));
	}

	public void SubscribeGame()
	{
		gameSub = DatabaseConnector.Instance?.Conn?.SubscriptionBuilder()
			.OnApplied(OnGameSubApplied)
			.OnError(OnGameSubError)
			.Subscribe(TablesSql(GameTables));
	}

	public void UnsubscribeLobby()
	{
		if (lobbySub?.IsEnded == false) lobbySub.Unsubscribe();
		lobbySub = null;
	}

	public void UnsubscribeGame()
	{
		if (gameSub?.IsEnded == false) gameSub.Unsubscribe();
		gameSub = null;
	}

	private void OnGameSubError(ErrorContext ctx, Exception e)
	{
		GD.PrintErr($"[TableSubscriber] Game subscription error: {e.Message}");
	}

	private void OnLobbySubApplied(SubscriptionEventContext ctx)
	{
		GD.Print("[TableSubscriber] Lobby subscription ready");
		LobbyApplied?.Invoke();
	}

	private void OnGameSubApplied(SubscriptionEventContext ctx)
	{
		GD.Print("[TableSubscriber] Game subscription ready");
	}

	public override void _ExitTree()
	{
		// Static state must not outlive the scene — a fresh TableSubscriber would otherwise
		// inherit stale vectors until the next MapConfig row arrives.
		LapQ = Vector2.Zero;
		LapR = Vector2.Zero;

		var connector = DatabaseConnector.Instance;
		if (connector != null)
			connector.Connected -= OnConnected;

		if (connector?.Conn is { IsActive: true } conn)
		{
			conn.Db.MapConfig.OnInsert -= OnMapConfigInsert;
			conn.Db.MapConfig.OnUpdate -= OnMapConfigUpdate;
			UnsubscribeLobby();
			UnsubscribeGame();
		}
		else
		{
			// Dead or rebuilt connection (disconnect path): the old hooks die with the old
			// DbConnection, just drop the handles.
			lobbySub = null;
			gameSub = null;
		}

		base._ExitTree();
	}

	private static string[] TablesSql(string[] tables) => tables.Select(TableSql).ToArray();

	/// "SELECT * FROM ..." for a PascalCase RemoteTables field name, via the generated From API
	/// (so the SQL stays correct if a table is renamed server-side and bindings regenerate).
	private static string TableSql(string tableName)
	{
		var method = typeof(From).GetMethod(tableName)
			?? throw new InvalidOperationException($"[TableSubscriber] Unknown table '{tableName}' — not in the generated module bindings");
		var query = method.Invoke(new From(), null)!;
		return (string)query.GetType().GetMethod("ToSql")!.Invoke(query, null)!;
	}
}
