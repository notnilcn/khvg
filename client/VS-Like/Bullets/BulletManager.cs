/*
```c# */
using Godot;
using Godot.Collections;
using System.Collections.Generic;

public partial class BulletManager : Node
{
    public static BulletManager Instance { get; private set; }
/*
```
*/

/*
# Bullet pattern enums
```c# */
    public const int PatternLinear    = 0;
    public const int PatternSineWave  = 1;
    public const int PatternSimulated = 2;
/*
```
*/

/*
# Bullet drawer
```c# */
    private static readonly PackedScene BulletVisualScene =
        GD.Load<PackedScene>("res://VS-Like/Bullets/BulletVisual/bullet_visual.tscn");

    private const int PoolSize     = 500;
    private const float BulletRadius = 4.0f;
    private const float PlayerRadius = 8.0f;

    private readonly List<Dictionary> _active = new();
    private readonly System.Collections.Generic.Dictionary<int, Dictionary> _byId = new();
    private int _nextId = 0;

    private readonly List<Node2D> _pool    = new();
    private readonly System.Collections.Generic.Dictionary<int, Node2D> _visuals = new();
/*
```
*/


/*
# `_Ready()`
```c# */
    public override void _Ready()
    {
        Instance = this;
        for (int i = 0; i < PoolSize; i++)
        {
            var v = BulletVisualScene.Instantiate<Node2D>();
            v.Visible = false;
            AddChild(v);
            _pool.Add(v);
        }
    }
/*
```
*/

/*
```c# */
    public override void _Process(double delta)
    {
        float now = Time.GetTicksMsec() * 0.001f;
        _ServerTick(now, (float)delta);
        _UpdateVisuals(now);
    }
/*
```
*/

/*
```c# */
    public void ServerSpawnLinear(Vector2 spawnPos, Vector2 direction, float speed, float damage, float lifetime = 4.0f)
    {
        _Spawn(new Dictionary
        {
            { "type",      PatternLinear },
            { "spawn_pos", spawnPos },
            { "direction", direction.Normalized() },
            { "speed",     speed },
            { "damage",    damage },
            { "lifetime",  lifetime },
            { "amplitude", 0.0f },
            { "frequency", 0.0f },
            { "sim_pos",   spawnPos },
        });
    }
/*
```
*/

/*
```c# */
    public void ServerSpawnSine(Vector2 spawnPos, Vector2 direction, float speed, float damage,
        float amplitude, float frequency, float lifetime = 4.0f)
    {
        _Spawn(new Dictionary
        {
            { "type",      PatternSineWave },
            { "spawn_pos", spawnPos },
            { "direction", direction.Normalized() },
            { "speed",     speed },
            { "damage",    damage },
            { "lifetime",  lifetime },
            { "amplitude", amplitude },
            { "frequency", frequency },
            { "sim_pos",   spawnPos },
        });
    }
/*
```
*/

/*
```c# */
    public static Vector2 GetPosition(Dictionary data, float elapsed)
    {
        var sp = data["spawn_pos"].AsVector2();
        var d  = data["direction"].AsVector2();
        int type = data["type"].AsInt32();

        if (type == PatternSineWave)
        {
            var perp = new Vector2(-d.Y, d.X);
            return sp
                + d    * data["speed"].AsSingle()     * elapsed
                + perp * data["amplitude"].AsSingle()
                       * Mathf.Sin(data["frequency"].AsSingle() * Mathf.Tau * elapsed);
        }
        if (type == PatternSimulated)
            return data["sim_pos"].AsVector2();
        // Linear
        return sp + d * data["speed"].AsSingle() * elapsed;
    }
/*
```
*/

/*
```c# */
    private void _ServerTick(float now, float delta)
    {
        var toRemove = new List<int>();

        foreach (var data in _active)
        {
            int id = data["id"].AsInt32();
            if (toRemove.Contains(id))
                continue;

            float elapsed = now - data["spawn_time"].AsSingle();
            if (elapsed >= data["lifetime"].AsSingle())
            {
                toRemove.Add(id);
                continue;
            }

            var cur  = GetPosition(data, elapsed);
            var prev = GetPosition(data, Mathf.Max(elapsed - delta, 0.0f));

            foreach (var player in Global.Instance.Players.Values)
            {
                if (_SweptHit(prev, cur, player.GlobalPosition))
                {
                    player.Hurt(data["damage"].AsSingle());
                    toRemove.Add(id);
                    break;
                }
            }
        }

        _DespawnMany(toRemove);
    }
/*
```
*/

/*
```c# */
    private static bool _SweptHit(Vector2 a, Vector2 b, Vector2 centre)
    {
        var ab     = b - a;
        float lenSq = ab.LengthSquared();
        float t     = 0.0f;
        if (lenSq > 0.0f)
            t = Mathf.Clamp((centre - a).Dot(ab) / lenSq, 0.0f, 1.0f);
        return (a + ab * t).DistanceTo(centre) < BulletRadius + PlayerRadius;
    }
/*
```
*/

/*
```c# */
    private void _DespawnMany(List<int> ids)
    {
        if (ids.Count == 0)
            return;
        foreach (int bid in ids)
        {
            _active.RemoveAll(d => d["id"].AsInt32() == bid);
            _byId.Remove(bid);
            _HideBullet(bid);
        }
    }
/*
```
*/

/*
```c# */
    private void _Spawn(Dictionary data)
    {
        data["id"]         = _nextId;
        data["spawn_time"] = Time.GetTicksMsec() * 0.001f;
        _nextId++;
        _active.Add(data);
        _byId[data["id"].AsInt32()] = data;
        _ShowBullet(data);
    }
/*
```
*/

/*
```c# */
    public void UpdateSimulated(int id, Vector2 pos)
    {
        if (_byId.TryGetValue(id, out var data))
            data["sim_pos"] = pos;
    }
/*
```
*/

/*
```c# */
    private void _UpdateVisuals(float now)
    {
        foreach (var kvp in _visuals)
        {
            if (!_byId.TryGetValue(kvp.Key, out var data))
                continue;
            float elapsed = now - data["spawn_time"].AsSingle();
            kvp.Value.GlobalPosition = GetPosition(data, elapsed);
        }
    }
/*
```
*/

/*
```c# */
    private void _ShowBullet(Dictionary data)
    {
        if (_pool.Count == 0)
            return;
        var v = _pool[_pool.Count - 1];
        _pool.RemoveAt(_pool.Count - 1);
        v.Visible = true;
        v.GlobalPosition = data["spawn_pos"].AsVector2();
        _visuals[data["id"].AsInt32()] = v;
    }
/*
```
*/

/*
```c# */
    private void _HideBullet(int id)
    {
        if (!_visuals.TryGetValue(id, out var v))
            return;
        v.Visible = false;
        _visuals.Remove(id);
        _pool.Add(v);
    }
/*
```
*/
}
