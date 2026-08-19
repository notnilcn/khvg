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
		var ch = HexMath.WorldToHex(center.X, center.Z, _outerRadius);
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
		var chunk = HexMath.ToLowerRes(q, r, _chunkRadius);
		int colorIdx = ((chunk.X * 2 + chunk.Y) % 3 + 3) % 3;
		var scene = _scenes.Length > colorIdx ? _scenes[colorIdx] : null;
		if (scene == null) return;

		var tile = scene.Instantiate<Node3D>();
		float s = _outerRadius * TileScaleMultiplier;
		tile.Position = HexMath.HexToWorld3D(q, r, _outerRadius);
		tile.Scale = new Vector3(s, 1f, s);
		AddChild(tile);
		_tiles[new Vector2I(q, r)] = tile;

		if (!ShowTileLabels) return;

		long spiralIdx = HexMath.SpiralIndex(chunk.X, chunk.Y);
		var label = new Label3D();
		label.Text = $"({q},{r})\nc({chunk.X},{chunk.Y}) i={spiralIdx}";
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.PixelSize = 0.3f;
		label.FontSize = 14;
		label.Modulate = Colors.White;
		label.OutlineSize = 2;
		label.OutlineModulate = Colors.Black;
		label.Position = HexMath.HexToWorld3D(q, r, _outerRadius) + new Vector3(0f, 0.15f, 0f);
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
}
