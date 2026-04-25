#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

// Autoload singleton. Subscribes to PlayerPosition table updates and:
//   - Spawns a visual node for each remote SpacetimeDB player (archer sprite)
//   - Buffers position updates by DEBUG_DELAY_MS so the STDB archer is visibly
//     behind the Godot-multiplayer dwarf, making both sync channels easy to compare
//   - Lerps buffered nodes toward their delayed target each frame
//   - Removes them when the player disconnects
//
// Call OnGameSceneLoaded() from Spawner._Ready() so players already in the DB
// get their visual nodes when the game map loads (OnInsert already fired in lobby).
public partial class StdbPlayerSync : Node
{
    public static StdbPlayerSync Instance { get; private set; } = null!;

    private const string GameScenePath = "res://VS-Like/Maps/Woods/woods_map.tscn";

    // Archer sprite so STDB-synced ghosts are visually distinct from the local dwarf.
    private const string RemoteTexturePath = "res://VS-Like/Characters/Player/Types/Archer/sprite_archer.tres";

    // Artificial delay (ms) applied to every incoming position update.
    // Set to 0 to disable. Makes it obvious the STDB path is working independently.
    private const ulong DebugDelayMs = 1000;

    private readonly Dictionary<string, Node2D> _remoteNodes = new();
    private readonly Dictionary<string, Vector2> _targetPositions = new();

    // (applyAtMs, playerKey, worldPosition) — drained in _Process when applyAtMs <= now
    private readonly Queue<(ulong ApplyAt, string Key, Vector2 Pos)> _pendingMoves = new();

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
            _SpawnRemote(row.PlayerId, row.X, row.Y);
        }
    }

    // ── Per-frame: drain delay queue, then lerp ───────────────────────────────

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();
        while (_pendingMoves.TryPeek(out var next) && now >= next.ApplyAt)
        {
            _pendingMoves.Dequeue();
            _targetPositions[next.Key] = next.Pos;
        }

        foreach (var (key, node) in _remoteNodes)
        {
            if (!GodotObject.IsInstanceValid(node)) continue;
            if (!_targetPositions.TryGetValue(key, out var target)) continue;
            node.GlobalPosition = node.GlobalPosition.Lerp(target, (float)delta * 10f);
        }
    }

    // ── Table callbacks ───────────────────────────────────────────────────────

    private void _OnInsert(EventContext ctx, PlayerPosition row)
    {
        if (_IsLocal(row.PlayerId)) return;
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
            _targetPositions.Remove(key);
            return;
        }
        _pendingMoves.Enqueue((Time.GetTicksMsec() + DebugDelayMs, key, new Vector2(newRow.X, newRow.Y)));
    }

    private void _OnDelete(EventContext ctx, PlayerPosition row)
    {
        var key = row.PlayerId.ToString();
        _targetPositions.Remove(key);
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

        var pos = new Vector2(x, y);
        var key = id.ToString();

        var node = new Node2D();
        node.Name = $"StdbRemote_{_Short(id)}";
        node.GlobalPosition = pos;

        var texture = GD.Load<Texture2D>(RemoteTexturePath);
        var sprite = new Sprite2D();
        sprite.Texture = texture;
        sprite.Position = new Vector2(0, -8);
        sprite.FlipH = true;
        node.AddChild(sprite);

        scene.CallDeferred(Node.MethodName.AddChild, node);
        _remoteNodes[key] = node;
        _targetPositions[key] = pos;
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
