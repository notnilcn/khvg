/* Importing, declaring, assigning things.
```c# */
#nullable enable
using System;
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

// Autoload singleton. Subscribes to PlayerPosition table updates and:
//   - Spawns a visual node for each remote SpacetimeDB player (archer sprite)
//   - Extrapolates each remote player's position every frame using dead-reckoning:
//     pos = snapshot.Pos + snapshot.MoveDir * snapshot.Speed * elapsed
//   - Lerps the visual node toward the extrapolated point to absorb correction drift
//   - Removes nodes when the player disconnects
//
// Call OnGameSceneLoaded() from Spawner._Ready() so players already in the DB
// get their visual nodes when the game map loads (OnInsert already fired in lobby).
public partial class StdbPlayerSync : Node
{
    public static StdbPlayerSync Instance { get; private set; } = null!; // ^Instance

    private const string GameScenePath = "res://VS-Like/Maps/Woods/woods_map.tscn";

    // Archer sprite so STDB-synced ghosts are visually distinct from the local dwarf.
    private const string RemoteModelPath = "res://VS-Like/Assets/model_presets/player_model.tscn";
    // TODO: change this to some 3d model stuff
    

    private readonly Dictionary<string, Node2D> _remoteNodes = new();
    private readonly Dictionary<string, SyncSnapshot> _snapshots = new(); // ^-snapshots

    public IReadOnlyDictionary<string, Node2D> RemoteNodes => _remoteNodes;
/*
```
*/

/*
# `_Ready()`
```c# */
	public override void _Ready()
    {
        Instance = this;

        var mgr = SpacetimeDBManager.Instance;
        if (mgr.IsStdbConnected)
            _HookCallbacks();
        else
            mgr.ConnectedToServer += _OnConnectedToServer;
    }
/*
```
*/

/*
# Callbacks and stuff
```c# */
    private void _OnConnectedToServer(string _) // ^-OnConnectedToServer
    {
        SpacetimeDBManager.Instance.ConnectedToServer -= _OnConnectedToServer;
        _HookCallbacks();
    }

    private void _HookCallbacks() // ^-HookCallbacks
    {
        var mgr = SpacetimeDBManager.Instance;
        var conn = mgr.Conn!;
        conn.Db.PlayerPosition.OnInsert += _OnInsert;
        conn.Db.PlayerPosition.OnUpdate += _OnUpdate;
        conn.Db.PlayerPosition.OnDelete += _OnDelete;

        // If the subscription is already applied (game scene loaded after STDB connected),
        // catch up immediately. Otherwise wait for the signal.
        if (mgr.IsSubscriptionReady)
            _CatchUpIfInGameScene();
        else
            mgr.SubscriptionReady += _OnSubscriptionReady;

        GD.Print("[StdbSync] Hooked PlayerPosition callbacks");
    }

    private void _OnSubscriptionReady() // ^-OnSubscriptionReady
    {
        SpacetimeDBManager.Instance.SubscriptionReady -= _OnSubscriptionReady;
        _CatchUpIfInGameScene();
    }

    private void _CatchUpIfInGameScene() // ^-CatchUpIfInGameScene
    {
        if (GetTree().CurrentScene?.SceneFilePath == GameScenePath) // Deferred: It probably makes more sense to connect this function to a signal rather than checking if gamescenepath is the currentscene.
            OnGameSceneLoaded();
    }

    public void OnGameSceneLoaded() // ^OnGameSceneLoaded
    {
        var conn = SpacetimeDBManager.Instance.Conn;
        if (conn == null) return;

        foreach (var row in conn.Db.PlayerPosition.Iter())
        {
            if (_IsLocal(row.PlayerId)) continue;
            var key = row.PlayerId.ToString();
            if (_remoteNodes.ContainsKey(key)) continue;
            _StoreSnapshot(key, row);
            _SpawnRemote(row.PlayerId, row.X, row.Y);
        }
    }
/*
```
*/

/*
# Per-frame: dead-reckoning extrapolation
```c# */
    public override void _Process(double delta) // ^-Process
    {
        long nowMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

        foreach (var (key, node) in _remoteNodes) // Deferred: You could probably vectorize this calculation rather than loop through every player on screen. Updating the node.GlobalPosition would have to still be sequential though.
        {
            if (!GodotObject.IsInstanceValid(node)) continue;
            if (!_snapshots.TryGetValue(key, out var snap)) continue;

            var extrapolated = Simulator.Extrapolate(snap, nowMicros, maxElapsed: 2f); // ^calculate-extrapolated-position
            node.GlobalPosition = node.GlobalPosition.Lerp(extrapolated, (float)delta);
        }
    }
/*
```
*/

/*
# Table callbacks
```c# */
    private void _OnInsert(EventContext ctx, PlayerPosition row) // ^-OnInsert
    {
        if (_IsLocal(row.PlayerId)) return;
        if (GetTree().CurrentScene?.SceneFilePath != GameScenePath) return;

        var key = row.PlayerId.ToString();
        _StoreSnapshot(key, row);
        _SpawnRemote(row.PlayerId, row.X, row.Y);
        GD.Print($"[StdbSync] Remote player joined: {_Short(row.PlayerId)}");
    }

    private void _OnUpdate(EventContext ctx, PlayerPosition oldRow, PlayerPosition newRow) // ^-OnUpdate
    {
        var key = newRow.PlayerId.ToString();
        if (!_remoteNodes.TryGetValue(key, out var node)) return;
        if (!GodotObject.IsInstanceValid(node))
        {
            _remoteNodes.Remove(key);
            _snapshots.Remove(key);
            return;
        }
        _StoreSnapshot(key, newRow);
    }

    private void _OnDelete(EventContext ctx, PlayerPosition row) // ^-OnDelete
    {
        var key = row.PlayerId.ToString();
        _snapshots.Remove(key);
        if (!_remoteNodes.TryGetValue(key, out var node)) return;
        if (GodotObject.IsInstanceValid(node))
            node.QueueFree();
        _remoteNodes.Remove(key);
        GD.Print($"[StdbSync] Remote player left: {_Short(row.PlayerId)}");
    }
/*
```
*/

/*
# Helpers
```c# */
    private void _StoreSnapshot(string key, PlayerPosition row) // ^StoreSnapshot
    {
        var dir = new Vector2(row.MoveX, row.MoveY).Normalized();
        _snapshots[key] = new SyncSnapshot(
            new Vector2(row.X, row.Y),
            dir.X, dir.Y, row.Speed,
            row.EventTime.MicrosecondsSinceUnixEpoch);
    }

    private void _SpawnRemote(SpacetimeDB.Identity id, float x, float y) // ^SpawnRemote
    {
        var scene = GetTree().CurrentScene;
        GD.PrintS("scene", scene);
        if (scene == null) return;

        var key = id.ToString();
        var node = new Node2D();
        node.Name = $"StdbRemote_{_Short(id)}";
        node.GlobalPosition = new Vector2(x, y);

        var model = GD.Load<PackedScene>(RemoteModelPath).Instantiate();
        model.GetNode("SubViewportContainer/SubViewport/WorldEnvironment/Archer");
        node.AddChild(model);

        scene.CallDeferred(Node.MethodName.AddChild, node);
        _remoteNodes[key] = node;
    }

    private bool _IsLocal(SpacetimeDB.Identity id) // ^IsLocal
    {
        var local = SpacetimeDBManager.Instance.LocalIdentity;
        return local.HasValue && id == local.Value;
    }

    private static string _Short(SpacetimeDB.Identity id) // ^Short
    {
        var s = id.ToString();
        return s.Length >= 8 ? s[..8] : s;
    }

    public override void _ExitTree() // ^ExitTree
    {
        var mgr = SpacetimeDBManager.Instance;
        if (mgr != null)
        {
            mgr.ConnectedToServer -= _OnConnectedToServer;
            mgr.SubscriptionReady -= _OnSubscriptionReady;
        }
        var conn = mgr?.Conn;
        if (conn == null) return;
        conn.Db.PlayerPosition.OnInsert -= _OnInsert;
        conn.Db.PlayerPosition.OnUpdate -= _OnUpdate;
        conn.Db.PlayerPosition.OnDelete -= _OnDelete;
    }

	// Returns the STDB identity string for a given player node.
    // Works for both remote ghost nodes and the local player node.
    public string? GetIdentityForNode(Node2D node) // ^GetIdentityForNode
    {
        foreach (var (id, n) in _remoteNodes)
            if (n == node) return id;
        if (node is Player)
            return SpacetimeDBManager.Instance.LocalIdentity?.ToString();
        return null;
    }
/*
```
*/
}
