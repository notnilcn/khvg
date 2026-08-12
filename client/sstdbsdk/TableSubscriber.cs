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
    public static readonly string[] BaseTables = { "AllTextures", "AllItems", "AllEnchantments" };

    /// Lobby wave: profile selection before joining the world.
    public static readonly string[] LobbyTables = { "LocalLobbyPlayer", "LocalPlayerProfiles" };

    /// Game wave: everything the live world needs.
    public static readonly string[] GameTables =
    {
        "LocalPlayer", "LocalPlayerData", "LocalPlayerStats", "LocalPlayerInventory",
        "LocalPlayerPosition", "LocalPlayerActiveProfile", "LocalPlayerProfiles",
        "NearbyRemotePlayers", "NearbyRemotePlayersProfiles", "NearbyEnemies",
        "NearbyLootDrops", "NearbyTerrainTiles", "NearbyHexDecor", "EnemyTemplates",
        "BulletPatternEvent", "BulletControlEvent", "MapConfig",
    };

    /// Every table name across all waves — TableBinderComponent's dropdown source.
    public static IEnumerable<string> AllSubscribedTables =>
        BaseTables.Concat(LobbyTables).Concat(GameTables).Distinct();

    /// MapConfig torus lap vectors, mirrored from the subscribed row (zero before it arrives).
    public Vector2 LapQ { get; private set; } = Vector2.Zero;
    public Vector2 LapR { get; private set; } = Vector2.Zero;

    private SubscriptionHandle? lobbySub;
    private SubscriptionHandle? gameSub;

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

        conn.Db.MapConfig.OnInsert += (_, cfg) => { LapQ = new Vector2(cfg.LapQX, cfg.LapQY); LapR = new Vector2(cfg.LapRX, cfg.LapRY); };
        conn.Db.MapConfig.OnUpdate += (_, _, cfg) => { LapQ = new Vector2(cfg.LapQX, cfg.LapQY); LapR = new Vector2(cfg.LapRX, cfg.LapRY); };

        conn.SubscriptionBuilder().Subscribe(TablesSql(BaseTables));

        SubscribeLobby();
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
        lobbySub?.Unsubscribe();
        lobbySub = null;
    }

    public void UnsubscribeGame()
    {
        gameSub?.Unsubscribe();
        gameSub = null;
    }

    private void OnGameSubError(ErrorContext ctx, Exception e)
    {
        GD.PrintErr($"[TableSubscriber] Game subscription error: {e.Message}");
    }

    private void OnLobbySubApplied(SubscriptionEventContext ctx)
    {
        GD.Print("[TableSubscriber] Lobby subscription ready");
    }

    private void OnGameSubApplied(SubscriptionEventContext ctx)
    {
        GD.Print("[TableSubscriber] Game subscription ready");
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