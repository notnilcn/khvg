/* Importing, declaring, assigning things.
```c# */
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

	// Ring-buffer slot counter, elevated server only.
	private ushort _nextSlot;

	private readonly record struct BulletData(float Ox, float Oy, byte Pattern, float SpawnTime);
/*
```
*/

/*
# `_Ready()`
```c# */
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
/*
```
*/

/*
```c# */
	private void _OnConnected(string _)
	{
		SpacetimeDBManager.Instance.ConnectedToServer -= _OnConnected;
		_HookCallbacks();
	}

	private void _HookCallbacks()
	{
		GD.Print("[StdbBulletSync] Ready (BulletPattern table not yet implemented)");
	}

private void _SpawnVisual(float ox, float oy, ushort slotId, byte pattern)
	{
		if (_pool.Count == 0)
		{
			GD.PrintErr("[StdbBulletSync] Pool exhausted — no visual node available");
			return;
		}
		var v = _pool[^1];
		_pool.RemoveAt(_pool.Count - 1);
		v.GlobalPosition = new Vector2(ox, oy);
		v.Visible = true;
		v.QueueRedraw();
		_visuals[slotId] = v;
		_bullets[slotId] = new BulletData(ox, oy, pattern, Time.GetTicksMsec() * 0.001f);
		GD.Print($"[StdbBulletSync] Visual spawned slot={slotId} at {v.GlobalPosition}");
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

		if (ElevatedClient.IsActive)
			_ElevatedTick(now);
	}
/*
```
*/

/*
```c# */
	private void _ElevatedTick(float now)
	{
		var conn = SpacetimeDBManager.Instance.Conn;
		if (conn == null) return;

		var toRemove = new HashSet<ushort>();

		foreach (var kv in _expiry)
			if (now >= kv.Value)
				toRemove.Add(kv.Key);

		var players = new List<PlayerPosition>();
		foreach (var p in conn.Db.PlayerPosition.Iter())
			players.Add(p);

		foreach (var kv in _bullets)
		{
			if (toRemove.Contains(kv.Key)) continue;
			var bulletPos = GetPosition(kv.Value.Ox, kv.Value.Oy, kv.Value.Pattern, now - kv.Value.SpawnTime);
			foreach (var ppos in players)
			{
				if (bulletPos.DistanceTo(new Vector2(ppos.X, ppos.Y)) < BulletRadius + PlayerRadius)
				{
					toRemove.Add(kv.Key);
					break;
				}
			}
		}

		foreach (var slotId in toRemove)
		{
			_expiry.Remove(slotId);
			_TeardownVisual(slotId);
		}
	}
/*
```
*/

/*
```c# */
	// Called by Enemy._TickStdbBulletFire on the elevated client.
	public void FireBullet(Vector2 origin, byte patternType)
	{
		ushort slot = _nextSlot;
		_nextSlot = (ushort)((_nextSlot + 1) % MaxBulletSlots);

		_expiry[slot] = Time.GetTicksMsec() * 0.001f + BulletLifetime;
		_SpawnVisual(origin.X, origin.Y, slot, patternType);
		GD.Print($"[StdbBulletSync] FireBullet slot={slot} pattern={patternType} origin={origin}");
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
			mgr.ConnectedToServer -= _OnConnected;
		}
		// BulletPattern table not yet implemented — no callbacks to unsubscribe.
	}
/*
```
*/
}
