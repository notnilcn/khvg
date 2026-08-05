#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// Spawns and despawns the server-row entities — LocalPlayer (+ the BulletManager),
/// RemotePlayers, Enemies, Drops — from child TableBinderComponent row signals (declared in
/// entity_spawner_component.tscn, signals wired in the editor; ReplayExistingRows covers rows
/// already in the client cache), and tracks them in lookup dictionaries. Instantiation stays
/// in code because the count is data-driven by server rows (logic moved out of GameManager.cs).
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

    /// Child binders (declared in entity_spawner_component.tscn) feeding the spawn tables.
    private TableBinderComponent localPlayerBinder = null!;
    private TableBinderComponent nearbyRemotePlayersBinder = null!;
    private TableBinderComponent nearbyEnemiesBinder = null!;
    private TableBinderComponent nearbyLootDropsBinder = null!;

    public int EnemyCount => enemies.Count;

    public Enemy? GetEnemy(ulong enemyId) => enemies.TryGetValue(enemyId, out var node) ? node : null;

    public override void _Ready()
    {
        base._Ready();
        localPlayerBinder = GetNode<TableBinderComponent>("LocalPlayerBinder");
        nearbyRemotePlayersBinder = GetNode<TableBinderComponent>("NearbyRemotePlayersBinder");
        nearbyEnemiesBinder = GetNode<TableBinderComponent>("NearbyEnemiesBinder");
        nearbyLootDropsBinder = GetNode<TableBinderComponent>("NearbyLootDropsBinder");
    }

    // --- TableBinderComponent signal handlers (wired in entity_spawner_component.tscn) ---

    private void OnLocalPlayerInsert()
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

    private void OnLocalPlayerDelete()
    {
        if (localPlayer != null && IsInstanceValid(localPlayer))
            localPlayer.QueueFree();
        localPlayer = null;
        if (bulletManager != null && IsInstanceValid(bulletManager))
            bulletManager.QueueFree();
        bulletManager = null;

        if (DatabaseConnector.Instance?.Conn?.IsActive != true) return;

        GetSibling<LobbyComponent>()?.ShowLobby();
        GetSibling<TableSubscriber>()?.SubscribeLobby();
        GetSibling<TableSubscriber>()?.UnsubscribeGame();
    }

    private void OnNearbyRemotePlayerInsert()
    {
        var position = (PlayerPosition)nearbyRemotePlayersBinder.LastRow!;
        var key = position.PlayerId.ToString();
        if (DatabaseConnector.Instance?.IsLocal(position.PlayerId) == true || remotePlayers.ContainsKey(key)) return;
        var node = NonLocalPlayerScene.Instantiate<RemotePlayer>();
        node.PlayerId = position.PlayerId;
        node.ProfileId = position.ProfileId;
        node.GlobalPosition = new Vector2(position.X, position.Y);
        GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, node);
        remotePlayers[key] = node;
    }

    private void OnNearbyRemotePlayerDelete()
    {
        var position = (PlayerPosition)nearbyRemotePlayersBinder.LastDeletedRow!;
        var key = position.PlayerId.ToString();
        if (!remotePlayers.TryGetValue(key, out var node)) return;
        remotePlayers.Remove(key);
        if (IsInstanceValid(node))
            node.QueueFree();
    }

    private void OnEnemyInsert()
    {
        var enemy = (SpacetimeDB.Types.Enemy)nearbyEnemiesBinder.LastRow!;
        if (enemies.ContainsKey(enemy.EnemyId)) return;
        var node = EnemyScene.Instantiate<Enemy>();
        node.EnemyId = enemy.EnemyId;
        node.GlobalPosition = new Vector2(enemy.X, enemy.Y);
        GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, node);
        enemies[enemy.EnemyId] = node;
    }

    private void OnEnemyDelete()
    {
        var enemy = (SpacetimeDB.Types.Enemy)nearbyEnemiesBinder.LastDeletedRow!;
        if (!enemies.TryGetValue(enemy.EnemyId, out var node)) return;
        enemies.Remove(enemy.EnemyId);
        if (IsInstanceValid(node))
            node.QueueFree();
    }

    private void OnDropInsert()
    {
        var lootDrop = (LootDrop)nearbyLootDropsBinder.LastRow!;
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

    private void OnDropDelete()
    {
        var lootDrop = (LootDrop)nearbyLootDropsBinder.LastDeletedRow!;
        if (!drops.TryGetValue(lootDrop.DropId, out var node)) return;
        drops.Remove(lootDrop.DropId);
        if (IsInstanceValid(node))
            node.QueueFree();
    }
}
