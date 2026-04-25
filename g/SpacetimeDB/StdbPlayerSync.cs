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
    public static StdbPlayerSync Instance { get; private set; } = null!;

    private const string GameScenePath = "res://VS-Like/Maps/Woods/woods_map.tscn";

    // Archer sprite so STDB-synced ghosts are visually distinct from the local dwarf.
    private const string RemoteTexturePath = "res://VS-Like/Characters/Player/Types/Archer/sprite_archer.tres";

    // Dead-reckoning snapshot stored on each PlayerPosition update.
    private readonly record struct Snapshot(
        Vector2 Pos,
        float MoveX,
        float MoveY,
        float Speed,
        long EventTimeMicros);

    private readonly Dictionary<string, Node2D> _remoteNodes = new();
    private readonly Dictionary<string, Snapshot> _snapshots = new();

    public IReadOnlyDictionary<string, Node2D> RemoteNodes => _remoteNodes;

    // Returns the STDB identity string for a given player node.
    // Works for both remote ghost nodes and the local player node.
    public string? GetIdentityForNode(Node2D node)
    {
        foreach (var (id, n) in _remoteNodes)
            if (n == node) return id;
        if (node is Player)
            return SpacetimeDBManager.Instance.LocalIdentity?.ToString();
        return null;
    }

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

    private void _OnSubscriptionReady()
    {
        SpacetimeDBManager.Instance.SubscriptionReady -= _OnSubscriptionReady;
        _CatchUpIfInGameScene();
    }

    private void _CatchUpIfInGameScene()
    {
        if (GetTree().CurrentScene?.SceneFilePath == GameScenePath)
            OnGameSceneLoaded();
    }

    public void OnGameSceneLoaded()
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

    // ── Per-frame: dead-reckoning extrapolation ───────────────────────────────

    public override void _Process(double delta)
    {
        long nowMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

        foreach (var (key, node) in _remoteNodes)
        {
            if (!GodotObject.IsInstanceValid(node)) continue;
            if (!_snapshots.TryGetValue(key, out var snap)) continue;

            // Clamp elapsed to 2s so a stale snapshot doesn't extrapolate off to infinity.
            float elapsed = System.Math.Clamp(
                (nowMicros - snap.EventTimeMicros) / 1_000_000f, 0f, 2f);

            var extrapolated = snap.Pos + new Vector2(snap.MoveX, snap.MoveY) * snap.Speed * elapsed;
            node.GlobalPosition = node.GlobalPosition.Lerp(extrapolated, (float)delta * 10f);
        }
    }

    // ── Table callbacks ───────────────────────────────────────────────────────

    private void _OnInsert(EventContext ctx, PlayerPosition row)
    {
        if (_IsLocal(row.PlayerId)) return;
        if (GetTree().CurrentScene?.SceneFilePath != GameScenePath) return;

        var key = row.PlayerId.ToString();
        _StoreSnapshot(key, row);
        _SpawnRemote(row.PlayerId, row.X, row.Y);
        GD.Print($"[StdbSync] Remote player joined: {_Short(row.PlayerId)}");
    }

    private void _OnUpdate(EventContext ctx, PlayerPosition oldRow, PlayerPosition newRow)
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

    private void _OnDelete(EventContext ctx, PlayerPosition row)
    {
        var key = row.PlayerId.ToString();
        _snapshots.Remove(key);
        if (!_remoteNodes.TryGetValue(key, out var node)) return;
        if (GodotObject.IsInstanceValid(node))
            node.QueueFree();
        _remoteNodes.Remove(key);
        GD.Print($"[StdbSync] Remote player left: {_Short(row.PlayerId)}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void _StoreSnapshot(string key, PlayerPosition row)
    {
        _snapshots[key] = new Snapshot(
            new Vector2(row.X, row.Y),
            row.MoveX, row.MoveY, row.Speed,
            row.EventTime.MicrosecondsSinceUnixEpoch);
    }

    private void _SpawnRemote(SpacetimeDB.Identity id, float x, float y)
    {
        var scene = GetTree().CurrentScene;
        if (scene == null) return;

        var key = id.ToString();
        var node = new Node2D();
        node.Name = $"StdbRemote_{_Short(id)}";
        node.GlobalPosition = new Vector2(x, y);

        var texture = GD.Load<Texture2D>(RemoteTexturePath);
        var sprite = new Sprite2D();
        sprite.Texture = texture;
        sprite.Position = new Vector2(0, -8);
        sprite.FlipH = true;
        node.AddChild(sprite);

        scene.CallDeferred(Node.MethodName.AddChild, node);
        _remoteNodes[key] = node;
    }

    private bool _IsLocal(SpacetimeDB.Identity id)
    {
        var local = SpacetimeDBManager.Instance.LocalIdentity;
        return local.HasValue && id == local.Value;
    }

    private static string _Short(SpacetimeDB.Identity id)
    {
        var s = id.ToString();
        return s.Length >= 8 ? s[..8] : s;
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
        conn.Db.PlayerPosition.OnInsert -= _OnInsert;
        conn.Db.PlayerPosition.OnUpdate -= _OnUpdate;
        conn.Db.PlayerPosition.OnDelete -= _OnDelete;
    }
}
