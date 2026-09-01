#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// Spawns and despawns the server-row entities — LocalPlayer, RemotePlayers, Enemies,
/// Drops, Chests — from child TableBinderComponent row signals (declared inline in game.tscn,
/// signals wired in the editor; ReplayExistingRows covers rows
/// already in the client cache), and tracks them in lookup dictionaries. Instantiation stays
/// in code because the count is data-driven by server rows (logic moved out of GameManager.cs).
/// The BulletManager is not row-driven, so it lives directly in game.tscn instead.
/// </summary>
public partial class EntitySpawnerComponent : Component
{
    [Export] public PackedScene LocalPlayerScene { get; set; } = null!;
    [Export] public PackedScene NonLocalPlayerScene { get; set; } = null!;
    [Export] public PackedScene EnemyScene { get; set; } = null!;
    [Export] public PackedScene DropScene { get; set; } = null!;
    [Export] public PackedScene ChestScene { get; set; } = null!;

    private LocalPlayer? localPlayer;
    private readonly Dictionary<string, RemotePlayer> remotePlayers = new();
    private readonly Dictionary<ulong, Enemy> enemies = new();
    private readonly Dictionary<ulong, Drop> drops = new();
    private readonly Dictionary<ulong, Chest> chests = new();

    /// Nodes whose deferred AddChild hasn't fired yet — a row delete arriving first must
    /// cancel the pending add (DespawnNode) instead of QueueFreeing a not-yet-added node.
    private readonly HashSet<Node> pendingAdds = new();

    /// Child binders (declared inline in game.tscn) feeding the spawn tables.
    private TableBinderComponent localPlayerBinder = null!;
    private TableBinderComponent nearbyRemotePlayersBinder = null!;
    private TableBinderComponent nearbyEnemiesBinder = null!;
    private TableBinderComponent nearbyLootDropsBinder = null!;
    private TableBinderComponent nearbyChestsBinder = null!;

    public int EnemyCount => enemies.Count;

    public Enemy? GetEnemy(ulong enemyId) => enemies.TryGetValue(enemyId, out var node) ? node : null;

    /// Deferred AddChild via this component: the parent scene is resolved at execution
    /// time (not captured at insert time, when a scene change in between would leave the
    /// deferred call pointing at a freed scene), and pendingAdds lets a row delete cancel
    /// the add before it fires.
    private void SpawnDeferred(Node node)
    {
        pendingAdds.Add(node);
        CallDeferred(nameof(AddSpawnedChild), node);
    }

    private void AddSpawnedChild(Node node)
    {
        // A delete that arrived before this deferred call already cancelled (and freed) it.
        if (!pendingAdds.Remove(node) || !IsInstanceValid(node)) return;
        var parent = GetTree().CurrentScene;
        if (parent == null) { node.Free(); return; }
        parent.AddChild(node);
    }

    /// Shared by every delete handler and by DespawnAll: a node whose deferred AddChild is
    /// still pending is freed directly (it never entered the tree); one already in the tree
    /// is QueueFreed.
    private void DespawnNode(Node node)
    {
        if (pendingAdds.Remove(node))
            node.Free();
        else if (IsInstanceValid(node))
            node.QueueFree();
    }

    /// Frees every tracked entity and cancels every pending deferred add. Invoked on every
    /// subscription-wave swap (join/leave/death), on disconnect (ConnectionLostComponent),
    /// and on _ExitTree, so a stale id can never wedge a later respawn behind the
    /// ContainsKey guards below.
    public void DespawnAll()
    {
        if (localPlayer != null)
        {
            DespawnNode(localPlayer);
            localPlayer = null;
        }
        foreach (var node in remotePlayers.Values) DespawnNode(node);
        remotePlayers.Clear();
        foreach (var node in enemies.Values) DespawnNode(node);
        enemies.Clear();
        foreach (var node in drops.Values) DespawnNode(node);
        drops.Clear();
        foreach (var node in chests.Values) DespawnNode(node);
        chests.Clear();
    }

    public override void _ExitTree()
    {
        // Scene exit frees the in-tree entities by itself; this matters for the pending
        // deferred adds, which are NOT in the tree and would otherwise leak.
        DespawnAll();
        base._ExitTree();
    }

    public override void _Ready()
    {
        base._Ready();
        localPlayerBinder = GetNode<TableBinderComponent>("LocalPlayerBinder");
        nearbyRemotePlayersBinder = GetNode<TableBinderComponent>("NearbyRemotePlayersBinder");
        nearbyEnemiesBinder = GetNode<TableBinderComponent>("NearbyEnemiesBinder");
        nearbyLootDropsBinder = GetNode<TableBinderComponent>("NearbyLootDropsBinder");
        nearbyChestsBinder = GetNode<TableBinderComponent>("NearbyChestsBinder");
    }

    // --- TableBinderComponent signal handlers (wired in game.tscn) ---

    private void OnLocalPlayerInsert()
    {
        if (localPlayer == null)
        {
            localPlayer = LocalPlayerScene.Instantiate<LocalPlayer>();
            SpawnDeferred(localPlayer);
        }
        // The LocalPlayer row is the join's success signal — only now does the lobby hide.
        GetSibling<LobbyComponent>()?.HideLobby();
    }

    private void OnLocalPlayerDelete()
    {
        DespawnAll();

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
        SpawnDeferred(node);
        remotePlayers[key] = node;
    }

    private void OnNearbyRemotePlayerDelete()
    {
        var position = (PlayerPosition)nearbyRemotePlayersBinder.LastDeletedRow!;
        var key = position.PlayerId.ToString();
        if (!remotePlayers.TryGetValue(key, out var node)) return;
        remotePlayers.Remove(key);
        DespawnNode(node);
    }

    private void OnEnemyInsert()
    {
        var enemy = (SpacetimeDB.Types.Enemy)nearbyEnemiesBinder.LastRow!;
        if (enemies.ContainsKey(enemy.EnemyId)) return;
        var node = EnemyScene.Instantiate<Enemy>();
        node.EnemyId = enemy.EnemyId;
        node.GlobalPosition = new Vector2(enemy.X, enemy.Y);
        SpawnDeferred(node);
        enemies[enemy.EnemyId] = node;
    }

    private void OnEnemyDelete()
    {
        var enemy = (SpacetimeDB.Types.Enemy)nearbyEnemiesBinder.LastDeletedRow!;
        if (!enemies.TryGetValue(enemy.EnemyId, out var node)) return;
        enemies.Remove(enemy.EnemyId);
        DespawnNode(node);
    }

    private void OnDropInsert()
    {
        var lootDrop = (LootDrop)nearbyLootDropsBinder.LastRow!;
        if (drops.ContainsKey(lootDrop.DropId)) return;
        var node = DropScene.Instantiate<Drop>();
        node.DropId = lootDrop.DropId;
        node.ItemId = lootDrop.ItemId;
        node.DroppedBy = lootDrop.DroppedBy;
        node.GlobalPosition = new Vector2(lootDrop.X, lootDrop.Y);
        SpawnDeferred(node);
        drops[lootDrop.DropId] = node;
    }

    private void OnDropDelete()
    {
        var lootDrop = (LootDrop)nearbyLootDropsBinder.LastDeletedRow!;
        if (!drops.TryGetValue(lootDrop.DropId, out var node)) return;
        drops.Remove(lootDrop.DropId);
        DespawnNode(node);
    }

    private void OnChestInsert()
    {
        var chest = (SpacetimeDB.Types.Chest)nearbyChestsBinder.LastRow!;
        if (chests.ContainsKey(chest.ChestId)) return;
        var node = ChestScene.Instantiate<Chest>();
        node.ChestId = chest.ChestId;
        node.GlobalPosition = new Vector2(chest.X, chest.Y);
        SpawnDeferred(node);
        chests[chest.ChestId] = node;
    }

    private void OnChestDelete()
    {
        var chest = (SpacetimeDB.Types.Chest)nearbyChestsBinder.LastDeletedRow!;
        if (!chests.TryGetValue(chest.ChestId, out var node)) return;
        chests.Remove(chest.ChestId);
        DespawnNode(node);
    }
}
