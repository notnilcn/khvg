#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Types;

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

    private readonly Dictionary<ulong, Enemy>    _enemyNodes = new();
    private readonly Dictionary<ulong, Snapshot> _snapshots  = new();

    private readonly record struct Snapshot(
        Vector2 Pos, float MoveX, float MoveY, float Speed, long EventTimeMicros);

    public override void _Ready()
    {
        Instance = this;

        var mgr = SpacetimeDBManager.Instance;
        if (mgr.IsStdbConnected)
            _HookCallbacks();
        else
            mgr.ConnectedToServer += _OnConnectedToServer;
    }

    private void _OnConnectedToServer(string _)
    {
        SpacetimeDBManager.Instance.ConnectedToServer -= _OnConnectedToServer;
        _HookCallbacks();
    }

    private void _HookCallbacks()
    {
        var mgr  = SpacetimeDBManager.Instance;
        var conn = mgr.Conn!;
        conn.Db.EnemyState.OnInsert += _OnInsert;
        conn.Db.EnemyState.OnUpdate += _OnUpdate;
        conn.Db.EnemyState.OnDelete += _OnDelete;

        if (mgr.IsSubscriptionReady)
            _CatchUpIfInGameScene();
        else
            mgr.SubscriptionReady += _OnSubscriptionReady;

        GD.Print("[StdbEnemySync] Hooked EnemyState callbacks");
    }

    private void _OnSubscriptionReady()
    {
        SpacetimeDBManager.Instance.SubscriptionReady -= _OnSubscriptionReady;
        _CatchUpIfInGameScene();
    }

    private void _CatchUpIfInGameScene()
    {
        if (GetTree().CurrentScene?.SceneFilePath == GameScenePath)
        {
            var spawner = _FindSpawner();
            if (spawner != null)
                OnGameSceneLoaded(spawner);
        }
    }

    // Called by Spawner._Ready() — spawns nodes for enemies already in the DB.
    public void OnGameSceneLoaded(Spawner spawner)
    {
        var conn = SpacetimeDBManager.Instance.Conn;
        if (conn == null) return;

        foreach (var row in conn.Db.EnemyState.Iter())
        {
            if (_enemyNodes.ContainsKey(row.EnemyId)) continue;
            _SpawnFromRow(row, spawner);
        }
    }

    // ── Per-frame: smooth correction lerp ────────────────────────────────────────

    public override void _Process(double delta)
    {
        long nowMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

        foreach (var (id, snap) in _snapshots)
        {
            if (!_enemyNodes.TryGetValue(id, out var enemy)) continue;
            if (!GodotObject.IsInstanceValid(enemy)) continue;

            float elapsed = Math.Clamp(
                (nowMicros - snap.EventTimeMicros) / 1_000_000f, 0f, 1f);
            var extrapolated = snap.Pos + new Vector2(snap.MoveX, snap.MoveY) * snap.Speed * elapsed;

            float drift = enemy.GlobalPosition.DistanceTo(extrapolated);
            if (drift >= HardSnapThreshold)
                enemy.GlobalPosition = extrapolated;
            else if (drift >= SoftCorrectionThreshold)
                enemy.GlobalPosition = enemy.GlobalPosition.Lerp(extrapolated, (float)delta * 5f);
        }
    }

    // ── Table callbacks ───────────────────────────────────────────────────────────

    private void _OnInsert(EventContext ctx, EnemyState row)
    {
        if (GetTree().CurrentScene?.SceneFilePath != GameScenePath) return;
        if (_enemyNodes.ContainsKey(row.EnemyId)) return;

        var spawner = _FindSpawner();
        if (spawner == null) return;

        _SpawnFromRow(row, spawner);
        GD.Print($"[StdbEnemySync] Enemy spawned: {row.EnemyId}");
    }

    private void _OnUpdate(EventContext ctx, EnemyState oldRow, EnemyState newRow)
    {
        _snapshots[newRow.EnemyId] = new Snapshot(
            new Vector2(newRow.X, newRow.Y),
            newRow.MoveX, newRow.MoveY, newRow.Speed,
            newRow.EventTime.MicrosecondsSinceUnixEpoch);

        if (_enemyNodes.TryGetValue(newRow.EnemyId, out var enemy) && GodotObject.IsInstanceValid(enemy))
            enemy.StdbTargetId = newRow.TargetId;
    }

    private void _OnDelete(EventContext ctx, EnemyState row)
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

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private void _SpawnFromRow(EnemyState row, Spawner spawner)
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
        enemy.GlobalPosition = new Vector2(row.X, row.Y);
        enemy.EnemyId        = (int)row.EnemyId;
        enemy.Name           = $"Enemy_{(int)row.EnemyId}";
        enemy.IsStdbSpawned      = true;
        enemy.StdbTargetId       = row.TargetId;
        enemy.BulletPatternType  = row.BulletPatternType;

        // The host needs its Die/Died signals connected so deaths trigger DespawnEnemy reducer.
        if (Multiplayer.IsServer())
            spawner.RegisterStdbEnemy(enemy);

        spawner.CallDeferred(Node.MethodName.AddChild, enemy);
        _enemyNodes[row.EnemyId] = enemy;

        _snapshots[row.EnemyId] = new Snapshot(
            new Vector2(row.X, row.Y),
            row.MoveX, row.MoveY, row.Speed,
            row.EventTime.MicrosecondsSinceUnixEpoch);
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
        conn.Db.EnemyState.OnInsert -= _OnInsert;
        conn.Db.EnemyState.OnUpdate -= _OnUpdate;
        conn.Db.EnemyState.OnDelete -= _OnDelete;
    }
}
