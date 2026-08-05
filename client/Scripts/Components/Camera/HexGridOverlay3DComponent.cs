#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Hex grid debug overlay (3D), child in world_3d.tscn. Registers with the GameManager
/// entity through the SubViewport ancestor walk. The per-hex Label3Ds stay code-created
/// (debug-only, data-driven count).
/// </summary>
public partial class HexGridOverlay3DComponent : Node3DComponent
{
	private const float Sqrt3 = 1.7320508075688772f;
	private const int DefaultChunkHexRadius = 5;
	private const float DefaultHexOuterRadius = 32.0f;

	// Chunk coloring: (cq*2 + cr) % 3  →  0=purple, 1=pink, 2=grey
	// Verifies: (0,0)→0 purple, (0,1)→1 pink, (1,0)→2 grey, adjacent chunks always differ.
	[Export] public PackedScene? PurpleTileScene { get; set; }
	[Export] public PackedScene? PinkTileScene { get; set; }
	[Export] public PackedScene? GreyTileScene { get; set; }

	// Multiply by _outerRadius to get the tile's world-space footprint.
	// Set to 1.0 if the tile's native outer radius == 1 Godot unit; adjust otherwise.
	[Export] public float TileScaleMultiplier { get; set; } = 1.0f;

	// Hex-ring radius around the player that gets populated with tiles.
	[Export] public int ViewRadiusHexes { get; set; } = 12;

	// When true, each tile gets a Label3D showing its hex/chunk coords.
	[Export] public bool ShowTileLabels { get; set; } = true;

	private float _outerRadius = DefaultHexOuterRadius;
	private int _chunkRadius = DefaultChunkHexRadius;

	private readonly Dictionary<Vector2I, Node3D> _tiles = new();
	private readonly Dictionary<Vector2I, Label3D> _labels = new();
	private Vector3 _lastRefreshPos = new(float.MaxValue, 0f, float.MaxValue);

	// Child binder (declared in world_3d.tscn) feeding MapConfig rows.
	private TableBinderComponent _mapConfigBinder = null!;

	// Indexed by coloring formula result (0,1,2).
	private PackedScene?[] _scenes = Array.Empty<PackedScene?>();

	protected override void OnRegistered()
	{
		_scenes = new PackedScene?[] { PurpleTileScene, PinkTileScene, GreyTileScene };
	}

	public override void _Ready()
	{
		base._Ready();
		_mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
	}

	public override void _Process(double _delta)
	{
		var player = LocalPlayer.Local;
		if (player == null) return;

		var p2 = player.GlobalPosition;    // Vector2 from CharacterBody2D
		var pos = new Vector3(p2.X, 0f, p2.Y);
		if (pos.DistanceSquaredTo(_lastRefreshPos) > _outerRadius * _outerRadius)
		{
			_lastRefreshPos = pos;
			RefreshTiles(pos);
		}
	}

	public override void _ExitTree()
	{
		ClearAll();
		base._ExitTree();
	}

	// --- TableBinderComponent signal handlers (wired in world_3d.tscn) ---
	// The binder has ReplayExistingRows on, so a row already in the client cache comes
	// through the same insert path — no separate Id.Find() replay here.

	private void OnMapConfigRow()
	{
		var cfg = (MapConfig)_mapConfigBinder.LastRow!;
		_chunkRadius = cfg.ChunkHexRadius;
		_outerRadius = cfg.HexOuterRadius;
		ClearAll();
	}

	// ── Tile management ───────────────────────────────────────────────────────

	private void RefreshTiles(Vector3 center)
	{
		var ch = WorldToHex(center.X, center.Z);
		int R = ViewRadiusHexes;

		// Build the set of hexes that should be visible (hex ring of radius R).
		var needed = new HashSet<Vector2I>();
		for (int dq = -R; dq <= R; dq++)
			for (int dr = Mathf.Max(-R, -dq - R); dr <= Mathf.Min(R, -dq + R); dr++)
				needed.Add(new Vector2I(ch.X + dq, ch.Y + dr));

		// Remove tiles no longer in range.
		var toRemove = new List<Vector2I>();
		foreach (var key in _tiles.Keys)
			if (!needed.Contains(key)) toRemove.Add(key);
		foreach (var key in toRemove)
		{
			_tiles[key].QueueFree();
			_tiles.Remove(key);
			if (_labels.TryGetValue(key, out var lbl)) { lbl.QueueFree(); _labels.Remove(key); }
		}

		// Spawn tiles that are newly in range.
		foreach (var hex in needed)
			if (!_tiles.ContainsKey(hex))
				SpawnTile(hex.X, hex.Y);
	}

	private void SpawnTile(int q, int r)
	{
		var chunk = ToLowerRes(q, r, _chunkRadius);
		int colorIdx = ((chunk.X * 2 + chunk.Y) % 3 + 3) % 3;
		var scene = _scenes.Length > colorIdx ? _scenes[colorIdx] : null;
		if (scene == null) return;

		var tile = scene.Instantiate<Node3D>();
		float s = _outerRadius * TileScaleMultiplier;
		tile.Position = HexToWorld3D(q, r);
		tile.Scale = new Vector3(s, 1f, s);
		AddChild(tile);
		_tiles[new Vector2I(q, r)] = tile;

		if (!ShowTileLabels) return;

		long spiralIdx = HexSpiralIndex(chunk.X, chunk.Y);
		var label = new Label3D();
		label.Text = $"({q},{r})\nc({chunk.X},{chunk.Y}) i={spiralIdx}";
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.PixelSize = 0.3f;
		label.FontSize = 14;
		label.Modulate = Colors.White;
		label.OutlineSize = 2;
		label.OutlineModulate = Colors.Black;
		label.Position = HexToWorld3D(q, r) + new Vector3(0f, 0.15f, 0f);
		label.RotationDegrees = new Vector3(-90f, 0f, 0f);
		AddChild(label);
		_labels[new Vector2I(q, r)] = label;
	}

	private void ClearAll()
	{
		foreach (var t in _tiles.Values) t.QueueFree();
		foreach (var l in _labels.Values) l.QueueFree();
		_tiles.Clear();
		_labels.Clear();
		_lastRefreshPos = new Vector3(float.MaxValue, 0f, float.MaxValue);
	}

	// ── Hex math (mirrors server methods.rs) ─────────────────────────────────

	// Pointy-top: server (x,y) ↔ Godot (x, 0, z).
	private Vector3 HexToWorld3D(int q, int r)
	{
		float R = _outerRadius;
		return new Vector3(
			R * (Sqrt3 * q + Sqrt3 * 0.5f * r),
			0f,
			R * 1.5f * r);
	}

	private Vector2I WorldToHex(float wx, float wz)
	{
		float R = _outerRadius;
		float q = (wx * Sqrt3 / 3f - wz / 3f) / R;
		float r = wz * 2f / 3f / R;
		float s = -q - r;
		float rq = MathF.Round(q);
		float rr = MathF.Round(r);
		float rs = MathF.Round(s);
		if (MathF.Abs(rq - q) > MathF.Abs(rr - r) && MathF.Abs(rq - q) > MathF.Abs(rs - s))
			rq = -rr - rs;
		else if (MathF.Abs(rr - r) > MathF.Abs(rs - s))
			rr = -rq - rs;
		return new Vector2I((int)rq, (int)rr);
	}

	// Port of hexx Hex::to_lower_res. Maps fine hex (q,r) → chunk (cq,cr).
	private static Vector2I ToLowerRes(int q, int r, int radius)
	{
		int s = -q - r;
		float area = 3f * radius * (radius + 1) + 1f;
		int shift = 3 * radius + 2;
		int a = Mathf.FloorToInt((r + shift * q) / area);
		int b = Mathf.FloorToInt((s + shift * r) / area);
		int c = Mathf.FloorToInt((q + shift * s) / area);
		return new Vector2I(
			Mathf.FloorToInt((1f + a - b) / 3f),
			Mathf.FloorToInt((1f + b - c) / 3f));
	}

	// Direct port of the server's spiral_chunk_index in methods.rs.
	// Analytical arm detection — no ring walk, exact match guaranteed.
	private static long HexSpiralIndex(int cq, int cr)
	{
		int x = cq;
		int z = cr;
		int y = -cq - cr;
		int rho = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
		if (rho == 0) return 0L;
		long b = 3L * rho * (rho - 1) + 1;
		long arm, step;
		if (z == -rho && x > 0) { arm = 0; step = y; }
		else if (y == rho && x > -rho) { arm = 1; step = -x; }
		else if (x == -rho && z < rho) { arm = 2; step = z; }
		else if (z == rho && x < 0) { arm = 3; step = x + rho; }
		else if (y == -rho && x >= 0 && x < rho) { arm = 4; step = x; }
		else { arm = 5; step = y + rho; }
		return b + arm * rho + step;
	}
}
