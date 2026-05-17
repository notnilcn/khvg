/* Importing, declaring, assigning things.
```c# */
#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Types;
using EnemyRow = SpacetimeDB.Types.Enemy;

// Autoload singleton. Subscribes to EnemyState table updates and handles enemy
// spawning/despawning/correction identically on every client — host included.
//
// Every machine gets enemies exclusively from OnInsert and loses them from OnDelete.
// The host's only special role (handled in Spawner, not here) is calling the reducers.
//
// Call OnGameSceneLoaded(spawner) from Spawner._Ready() so enemies already in the DB
// get their nodes when the game map loads (OnInsert already fired in lobby).
public partial class StdbEnemySync : Node
{
    public static StdbEnemySync Instance { get; private set; } = null!;

    private const string GameScenePath = "res://VS-Like/Maps/Woods/woods_map.tscn";
    private const float SoftCorrectionThreshold = 50f;
    private const float HardSnapThreshold       = 200f;

    private static readonly PackedScene EnemyScene =
        GD.Load<PackedScene>("res://VS-Like/Characters/Enemy/enemy.tscn");

    private readonly Dictionary<ulong, Enemy>       _enemyNodes = new();
    private readonly Dictionary<ulong, SyncSnapshot> _snapshots  = new();
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
	private void _OnConnectedToServer(string _)
    {
        SpacetimeDBManager.Instance.ConnectedToServer -= _OnConnectedToServer;
        _HookCallbacks();
    }

    private void _HookCallbacks() // ^-HookCallbacks
    {
        var mgr  = SpacetimeDBManager.Instance;
        var conn = mgr.Conn!;
        conn.Db.Enemy.OnInsert += _OnInsert;
        conn.Db.Enemy.OnUpdate += _OnUpdate;
        conn.Db.Enemy.OnDelete += _OnDelete;

        if (mgr.IsSubscriptionReady)
            _CatchUpIfInGameScene();
        else
            mgr.SubscriptionReady += _OnSubscriptionReady;

        GD.Print("[StdbEnemySync] Hooked Enemy callbacks");
    }

    private void _OnSubscriptionReady() // ^-OnSubscriptionReady
    {
        SpacetimeDBManager.Instance.SubscriptionReady -= _OnSubscriptionReady;
        _CatchUpIfInGameScene();
    }

    private void _CatchUpIfInGameScene() // ^-CatchUpIfInGameScene
    {
        if (GetTree().CurrentScene?.SceneFilePath == GameScenePath)
        {
            var spawner = _FindSpawner();
            if (spawner != null)
                OnGameSceneLoaded(spawner);
        }
    }

    // Called by Spawner._Ready() — spawns nodes for enemies already in the DB.
    public void OnGameSceneLoaded(Spawner spawner) // ^OnGameSceneLoaded
    {
        var conn = SpacetimeDBManager.Instance.Conn;
        if (conn == null) return;

        foreach (var row in conn.Db.Enemy.Iter())
        {
            if (_enemyNodes.ContainsKey(row.EnemyId)) continue;
            _SpawnFromRow(row, spawner);
        }
    }
/*
```
*/

/*
# Per-frame: smooth correction lerp
```c# */
    public override void _Process(double delta)
    {
        long nowMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

        foreach (var (id, snap) in _snapshots)
        {
            if (!_enemyNodes.TryGetValue(id, out var enemy)) continue;
            if (!GodotObject.IsInstanceValid(enemy)) continue;

            var extrapolated = Simulator.Extrapolate(snap, nowMicros, maxElapsed: 1f); // ^calculate-extrapolated-position

            float drift = enemy.GlobalPosition.DistanceTo(extrapolated);
            if (drift >= HardSnapThreshold)
                enemy.GlobalPosition = extrapolated;
            else if (drift >= SoftCorrectionThreshold)
                enemy.GlobalPosition = enemy.GlobalPosition.Lerp(extrapolated, (float)delta * 5f);
			//enemy.GlobalPosition = enemy.GlobalPosition.Lerp(extrapolated, (float)delta);
        }
    }
/*
```
*/

/*
# Table callbacks
```c# */
    private void _OnInsert(EventContext ctx, EnemyRow row) // ^-OnInsert
    {
        if (GetTree().CurrentScene?.SceneFilePath != GameScenePath) return;
        if (_enemyNodes.ContainsKey(row.EnemyId)) return;

        var spawner = _FindSpawner();
        if (spawner == null) return;

        _SpawnFromRow(row, spawner);
        GD.Print($"[StdbEnemySync] Enemy spawned: {row.EnemyId}");
    }

    private void _OnUpdate(EventContext ctx, EnemyRow oldRow, EnemyRow newRow) // ^-OnUpdate
    {
        if (_enemyNodes.TryGetValue(newRow.EnemyId, out var enemy) && GodotObject.IsInstanceValid(enemy))
            enemy.CurrHealth = newRow.Hp;
    }

    private void _OnDelete(EventContext ctx, EnemyRow row) // ^-OnDelete
    {
        ulong id = row.EnemyId;
        _snapshots.Remove(id);

        if (!_enemyNodes.TryGetValue(id, out var enemy))
        {
            _enemyNodes.Remove(id);
            return;
        }
        _enemyNodes.Remove(id);

        if (GodotObject.IsInstanceValid(enemy))
            enemy.QueueFree();

        GD.Print($"[StdbEnemySync] Enemy despawned: {id}");
    }
/*
```
*/

/*
# Helpers
```c# */
    private void _SpawnFromRow(EnemyRow row, Spawner spawner)
    {
        var resEnemy = GD.Load<ResEnemy>(row.ResPath);
        if (resEnemy == null)
        {
            GD.PrintErr($"[StdbEnemySync] Failed to load ResEnemy from '{row.ResPath}'");
            return;
        }

        var enemy            = EnemyScene.Instantiate<Enemy>();
        enemy.ResEnemy       = resEnemy;
        enemy.IsElite        = row.IsElite;
        enemy.Immortal       = row.Immortal;
        enemy.GlobalPosition = new Vector2(row.SpawnX, row.SpawnY);
        enemy.EnemyId        = (int)row.EnemyId;
        enemy.Name           = $"Enemy_{(int)row.EnemyId}";
        enemy.IsStdbSpawned  = true;

        if (ElevatedClient.IsActive)
            spawner.RegisterStdbEnemy(enemy);

        spawner.CallDeferred(Node.MethodName.AddChild, enemy);
        _enemyNodes[row.EnemyId] = enemy;
    }

    private Spawner? _FindSpawner()
    {
        var scene = GetTree().CurrentScene;
        if (scene == null) return null;
        foreach (var child in scene.GetChildren())
            if (child is Spawner s) return s;
        return null;
    }

    public override void _ExitTree()
    {
        var mgr = SpacetimeDBManager.Instance;
        if (mgr != null)
        {
            mgr.ConnectedToServer -= _OnConnectedToServer;
            mgr.SubscriptionReady -= _OnSubscriptionReady;
        }
        var conn = mgr?.Conn;
        if (conn == null) return;
        conn.Db.Enemy.OnInsert -= _OnInsert;
        conn.Db.Enemy.OnUpdate -= _OnUpdate;
        conn.Db.Enemy.OnDelete -= _OnDelete;
    }
/*
```
*/
}
