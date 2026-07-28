#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// Spawns and despawns the server-row entities — LocalPlayer (+ the BulletManager),
/// RemotePlayers, Enemies, Drops — from table OnInsert/OnDelete callbacks, and tracks
/// them in lookup dictionaries. Instantiation stays in code because the count is
/// data-driven by server rows (logic moved out of GameManager.cs).
/// </summary>
public partial class EntitySpawnerComponent : Component
{
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

    public int EnemyCount => enemies.Count;

    public Enemy? GetEnemy(ulong enemyId) => enemies.TryGetValue(enemyId, out var node) ? node : null;

    protected override void OnRegistered()
    {
        if (GetSibling<ConnectionComponent>() is { } connection)
            connection.Connected += OnConnected;
    }

    private void OnConnected()
    {
        var conn = GetSibling<ConnectionComponent>()?.Conn;
        if (conn == null) return;

        conn.Db.LocalPlayer.OnInsert += OnLocalPlayerInsert;
        conn.Db.LocalPlayer.OnDelete += OnLocalPlayerDelete;
        conn.Db.NearbyRemotePlayers.OnInsert += OnNearbyRemotePlayerInsert;
        conn.Db.NearbyRemotePlayers.OnDelete += OnNearbyRemotePlayerDelete;
        conn.Db.NearbyEnemies.OnInsert += OnEnemyInsert;
        conn.Db.NearbyEnemies.OnDelete += OnEnemyDelete;
        conn.Db.NearbyLootDrops.OnInsert += OnDropInsert;
        conn.Db.NearbyLootDrops.OnDelete += OnDropDelete;
    }

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

        if (GetSibling<ConnectionComponent>()?.Conn?.IsActive != true) return;

        GetSibling<LobbyComponent>()?.ShowLobby();
        GetSibling<SubscriptionComponent>()?.SubscribeLobby();
        GetSibling<SubscriptionComponent>()?.UnsubscribeGame();
    }

    private void OnNearbyRemotePlayerInsert(EventContext _, PlayerPosition position)
    {
        var key = position.PlayerId.ToString();
        if (GetSibling<ConnectionComponent>()?.IsLocal(position.PlayerId) == true || remotePlayers.ContainsKey(key)) return;
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
}
