#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Types;

// Autoload singleton. Subscribes to BulletPattern table and manages bullet visuals on all clients.
// On the elevated server it also runs hit detection and calls DamagePlayer / DespawnBullet.
public partial class StdbBulletSync : Node
{
    public static StdbBulletSync? Instance { get; private set; }

    // Parametric constants — must match what the elevated server uses.
    public const byte PatternNone     = 0;
    public const byte PatternStraight = 1;
    public const byte PatternSine     = 2;

    private const float BulletSpeed    = 50f;
    private const float SineAmplitude  = 80f;
    private const float SineFrequency  = 1f;
    private const float BulletRadius   = 4f;
    private const float PlayerRadius   = 8f;
    private const int   MaxBulletSlots = 1024;
    private const int   BulletDamage   = 10;
    private const float BulletLifetime = 5f;

    private static readonly PackedScene BulletVisualScene =
        GD.Load<PackedScene>("res://VS-Like/Bullets/BulletVisual/bullet_visual.tscn");

    // Visual pool — pre-allocated to avoid per-bullet instantiation.
    private readonly List<Node2D> _pool = new();

    // slot → visual node currently showing this bullet
    private readonly Dictionary<ushort, Node2D> _visuals = new();

    // slot → (originX, originY, patternType, localSpawnTime)
    private readonly Dictionary<ushort, BulletData> _bullets = new();

    // Elevated server only: when (local seconds) each slot expires.
    private readonly Dictionary<ushort, float> _expiry = new();

    // Elevated server only: slots we've already called DespawnBullet on (awaiting OnDelete).
    private readonly HashSet<ushort> _pendingDespawn = new();

    // Ring-buffer slot counter, elevated server only.
    private ushort _nextSlot;

    private readonly record struct BulletData(float Ox, float Oy, byte Pattern, float SpawnTime);

    public override void _Ready()
    {
        Instance = this;

        for (int i = 0; i < 128; i++)
        {
            var v = BulletVisualScene.Instantiate<Node2D>();
            v.Visible = false;
            AddChild(v);
            _pool.Add(v);
        }

        var mgr = SpacetimeDBManager.Instance;
        if (mgr.IsStdbConnected)
            _HookCallbacks();
        else
            mgr.ConnectedToServer += _OnConnected;
    }

    private void _OnConnected(string _)
    {
        SpacetimeDBManager.Instance.ConnectedToServer -= _OnConnected;
        _HookCallbacks();
    }

    private void _HookCallbacks()
    {
        var mgr  = SpacetimeDBManager.Instance;
        var conn = mgr.Conn!;
        conn.Db.BulletPattern.OnInsert += _OnInsert;
        conn.Db.BulletPattern.OnUpdate += _OnUpdate;
        conn.Db.BulletPattern.OnDelete += _OnDelete;
        GD.Print("[StdbBulletSync] Hooked BulletPattern callbacks");

        if (mgr.IsSubscriptionReady)
            _CatchUp();
        else
            mgr.SubscriptionReady += _OnSubscriptionReady;
    }

    private void _OnSubscriptionReady()
    {
        SpacetimeDBManager.Instance.SubscriptionReady -= _OnSubscriptionReady;
        _CatchUp();
    }

    private void _CatchUp()
    {
        var conn = SpacetimeDBManager.Instance.Conn;
        if (conn == null) return;
        foreach (var row in conn.Db.BulletPattern.Iter())
        {
            if (!_bullets.ContainsKey(row.SlotId))
                _SpawnVisual(row);
        }
    }

    private void _OnInsert(EventContext ctx, BulletPattern row)
    {
        GD.Print($"[StdbBulletSync] OnInsert slot={row.SlotId} pattern={row.PatternType} origin=({row.OriginX},{row.OriginY})");
        _SpawnVisual(row);
    }

    private void _OnUpdate(EventContext ctx, BulletPattern old, BulletPattern row)
    {
        // Slot reuse: tear down the old bullet and start the new one.
        _TeardownVisual(old.SlotId);
        _SpawnVisual(row);
    }

    private void _OnDelete(EventContext ctx, BulletPattern row)
    {
        _pendingDespawn.Remove(row.SlotId);
        _TeardownVisual(row.SlotId);
    }

    private void _SpawnVisual(BulletPattern row)
    {
        if (_pool.Count == 0)
        {
            GD.PrintErr("[StdbBulletSync] Pool exhausted — no visual node available");
            return;
        }
        var v = _pool[^1];
        _pool.RemoveAt(_pool.Count - 1);
        v.GlobalPosition = new Vector2(row.OriginX, row.OriginY);
        v.Visible = true;
        v.QueueRedraw();
        _visuals[row.SlotId] = v;
        _bullets[row.SlotId] = new BulletData(row.OriginX, row.OriginY, row.PatternType, Time.GetTicksMsec() * 0.001f);
        GD.Print($"[StdbBulletSync] Visual spawned slot={row.SlotId} at {v.GlobalPosition}");
    }

    private void _TeardownVisual(ushort slotId)
    {
        _bullets.Remove(slotId);
        if (!_visuals.TryGetValue(slotId, out var v)) return;
        v.Visible = false;
        _visuals.Remove(slotId);
        _pool.Add(v);
    }

    public override void _Process(double delta)
    {
        float now = Time.GetTicksMsec() * 0.001f;

        foreach (var kv in _visuals)
        {
            if (!_bullets.TryGetValue(kv.Key, out var b)) continue;
            kv.Value.GlobalPosition = GetPosition(b.Ox, b.Oy, b.Pattern, now - b.SpawnTime);
        }

        if (ElevatedServer.IsActive || Multiplayer.IsServer())
            _ElevatedTick(now);
    }

    private void _ElevatedTick(float now)
    {
        var conn = SpacetimeDBManager.Instance.Conn;
        if (conn == null) return;

        var toRemove = new HashSet<ushort>();

        // Expiry check.
        foreach (var kv in _expiry)
            if (now >= kv.Value)
                toRemove.Add(kv.Key);

        // Hit detection: collect player positions once per tick.
        var players = new List<PlayerPosition>();
        foreach (var p in conn.Db.PlayerPosition.Iter())
            players.Add(p);

        long nowMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

        foreach (var kv in _bullets)
        {
            if (toRemove.Contains(kv.Key) || _pendingDespawn.Contains(kv.Key)) continue;
            var bulletPos = GetPosition(kv.Value.Ox, kv.Value.Oy, kv.Value.Pattern, now - kv.Value.SpawnTime);
            foreach (var ppos in players)
            {
                float dt = Math.Max((nowMicros - ppos.EventTime.MicrosecondsSinceUnixEpoch) * 1e-6f, 0f);
                var playerPos = new Vector2(
                    ppos.X + ppos.MoveX * ppos.Speed * dt,
                    ppos.Y + ppos.MoveY * ppos.Speed * dt);
                if (bulletPos.DistanceTo(playerPos) < BulletRadius + PlayerRadius)
                {
                    conn.Reducers.DamagePlayer(ppos.PlayerId, BulletDamage);
                    toRemove.Add(kv.Key);
                    break;
                }
            }
        }

        foreach (var slotId in toRemove)
        {
            _expiry.Remove(slotId);
            _pendingDespawn.Add(slotId);
            conn.Reducers.DespawnBullet(slotId);
        }
    }

    // Called by Enemy._TickStdbBulletFire on the server (elevated or host in dev mode).
    public void FireBullet(Vector2 origin, byte patternType)
    {
        var conn = SpacetimeDBManager.Instance.Conn;
        if (conn == null)
        {
            GD.Print("[StdbBulletSync] FireBullet: no STDB connection");
            return;
        }

        ushort slot = _nextSlot;
        _nextSlot = (ushort)((_nextSlot + 1) % MaxBulletSlots);

        float expireAt = Time.GetTicksMsec() * 0.001f + BulletLifetime;
        _expiry[slot] = expireAt;

        GD.Print($"[StdbBulletSync] FireBullet slot={slot} pattern={patternType} origin={origin}");
        conn.Reducers.FireBullet(slot, patternType, origin.X, origin.Y, origin.X, origin.Y - 50f, BulletLifetime);
    }

    // Deterministic parametric position — must match server-side math exactly.
    public static Vector2 GetPosition(float ox, float oy, byte pattern, float elapsed)
    {
        if (pattern == PatternSine)
            return new Vector2(
                ox + SineAmplitude * Mathf.Sin(SineFrequency * Mathf.Tau * elapsed),
                oy - BulletSpeed * elapsed);
        // PatternStraight (and anything unknown defaults to straight)
        return new Vector2(ox, oy - BulletSpeed * elapsed);
    }

    public override void _ExitTree()
    {
        var mgr = SpacetimeDBManager.Instance;
        if (mgr != null)
        {
            mgr.ConnectedToServer   -= _OnConnected;
            mgr.SubscriptionReady   -= _OnSubscriptionReady;
        }
        var conn = mgr?.Conn;
        if (conn == null) return;
        conn.Db.BulletPattern.OnInsert -= _OnInsert;
        conn.Db.BulletPattern.OnUpdate -= _OnUpdate;
        conn.Db.BulletPattern.OnDelete -= _OnDelete;
    }
}
