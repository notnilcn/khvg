#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;

/// <summary>
/// Owns the subscription waves (base/lobby/game) and their applied/error handlers, plus
/// the MapConfig lap vectors those subscriptions deliver. Subscribes the base and lobby
/// waves when the sibling ConnectionComponent connects; LobbyComponent and
/// EntitySpawnerComponent switch waves on join/death (logic moved out of GameManager.cs).
/// </summary>
public partial class SubscriptionComponent : Component
{
    /// MapConfig torus lap vectors, mirrored from the subscribed row (zero before it arrives).
    public Vector2 LapQ { get; private set; } = Vector2.Zero;
    public Vector2 LapR { get; private set; } = Vector2.Zero;

    private SubscriptionHandle? lobbySub;
    private SubscriptionHandle? gameSub;

    protected override void OnRegistered()
    {
        if (GetSibling<ConnectionComponent>() is { } connection)
            connection.Connected += OnConnected;
    }

    private void OnConnected()
    {
        var conn = GetSibling<ConnectionComponent>()?.Conn;
        if (conn == null) return;

        conn.Db.MapConfig.OnInsert += (_, cfg) => { LapQ = new Vector2(cfg.LapQX, cfg.LapQY); LapR = new Vector2(cfg.LapRX, cfg.LapRY); };
        conn.Db.MapConfig.OnUpdate += (_, _, cfg) => { LapQ = new Vector2(cfg.LapQX, cfg.LapQY); LapR = new Vector2(cfg.LapRX, cfg.LapRY); };

        // Base wave: static catalog tables every screen needs.
        conn.SubscriptionBuilder()
            .AddQuery(q => q.From.AllTextures())
            .AddQuery(q => q.From.AllItems())
            .AddQuery(q => q.From.AllEnchantments())
            .Subscribe();

        SubscribeLobby();
    }

    public void SubscribeLobby()
    {
        lobbySub = GetSibling<ConnectionComponent>()?.Conn?.SubscriptionBuilder()
            .AddQuery(q => q.From.LocalLobbyPlayer())
            .AddQuery(q => q.From.LocalPlayerProfiles())
            .OnApplied(OnLobbySubApplied)
            .Subscribe();
    }

    public void SubscribeGame()
    {
        gameSub = GetSibling<ConnectionComponent>()?.Conn?.SubscriptionBuilder()
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
        GD.PrintErr($"[SubscriptionComponent] Game subscription error: {e.Message}");
    }

    private void OnLobbySubApplied(SubscriptionEventContext ctx)
    {
        GD.Print("[SubscriptionComponent] Lobby subscription ready");
    }

    private void OnGameSubApplied(SubscriptionEventContext ctx)
    {
        GD.Print("[SubscriptionComponent] Game subscription ready");
    }
}
