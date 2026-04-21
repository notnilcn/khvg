#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

// Autoload singleton. Subscribes to PlayerPosition table updates and:
//   - Spawns a visual node for each remote SpacetimeDB player
//   - Moves those nodes as the server pushes 20 Hz position ticks
//   - Removes them when the player disconnects
//
// Call OnGameSceneLoaded() from Spawner._Ready() so players already in the DB
// get their visual nodes when the game map loads (OnInsert already fired in lobby).
public partial class StdbPlayerSync : Node
{
    public static StdbPlayerSync Instance { get; private set; } = null!;

    private const string GameScenePath = "res://VS-Like/Maps/Woods/woods_map.tscn";

    private readonly Dictionary<string, Node2D> _remoteNodes = new();

    public override void _Ready()
    {
        Instance = this;

        var mgr = SpacetimeDBManager.Instance;
        if (mgr.IsSubscriptionReady)
            _HookCallbacks();
        else
            mgr.SubscriptionReady += _OnSubscriptionReady;
    }

    private void _OnSubscriptionReady() => _HookCallbacks();

    private void _HookCallbacks()
    {
        var conn = SpacetimeDBManager.Instance.Conn!;
        conn.Db.PlayerPosition.OnInsert += _OnInsert;
        conn.Db.PlayerPosition.OnUpdate += _OnUpdate;
        conn.Db.PlayerPosition.OnDelete += _OnDelete;
        GD.Print("[StdbSync] Hooked PlayerPosition callbacks");
    }

    // Called by Spawner._Ready() so that players already present in the DB
    // (whose OnInsert fired while we were in the lobby) get visual nodes now.
    public void OnGameSceneLoaded()
    {
        var conn = SpacetimeDBManager.Instance.Conn;
        if (conn == null) return;

        foreach (var row in conn.Db.PlayerPosition.Iter())
        {
            if (_IsLocal(row.PlayerId)) continue;
            var key = row.PlayerId.ToString();
            if (_remoteNodes.ContainsKey(key)) continue;
            _SpawnRemote(row.PlayerId, row.X, row.Y);
        }
    }

    // ── Table callbacks ───────────────────────────────────────────────────────

    private void _OnInsert(EventContext ctx, PlayerPosition row)
    {
        if (_IsLocal(row.PlayerId)) return;

        // Only add visual nodes while the game map is active.
        // OnGameSceneLoaded() handles players that arrived before the map loaded.
        if (GetTree().CurrentScene?.SceneFilePath != GameScenePath) return;

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
            return;
        }
        node.GlobalPosition = new Vector2(newRow.X, newRow.Y);
    }

    private void _OnDelete(EventContext ctx, PlayerPosition row)
    {
        var key = row.PlayerId.ToString();
        if (!_remoteNodes.TryGetValue(key, out var node)) return;
        if (GodotObject.IsInstanceValid(node))
            node.QueueFree();
        _remoteNodes.Remove(key);
        GD.Print($"[StdbSync] Remote player left: {_Short(row.PlayerId)}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void _SpawnRemote(SpacetimeDB.Identity id, float x, float y)
    {
        var scene = GetTree().CurrentScene;
        if (scene == null) return;

        var node = new Node2D();
        node.Name = $"StdbRemote_{_Short(id)}";
        node.GlobalPosition = new Vector2(x, y);

        // Simple blue rectangle as a placeholder visual for remote players.
        var poly = new Polygon2D();
        poly.Color = new Color(0.2f, 0.55f, 1.0f, 0.85f);
        poly.Polygon = new[] { new Vector2(-8, -12), new Vector2(8, -12), new Vector2(8, 12), new Vector2(-8, 12) };
        node.AddChild(poly);

        scene.AddChild(node);
        _remoteNodes[id.ToString()] = node;
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
        var conn = SpacetimeDBManager.Instance?.Conn;
        if (conn == null) return;
        conn.Db.PlayerPosition.OnInsert -= _OnInsert;
        conn.Db.PlayerPosition.OnUpdate -= _OnUpdate;
        conn.Db.PlayerPosition.OnDelete -= _OnDelete;
    }
}
