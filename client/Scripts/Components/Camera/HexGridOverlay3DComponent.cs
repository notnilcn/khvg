#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Hex grid debug overlay (3D), child in world_3d.tscn. Registers with the GameManager
/// entity through the SubViewport ancestor walk. The per-hex Label3Ds stay code-created
/// (debug-only, data-driven count). Tile columns are Y-stretched by their hex's elevation
/// level (ElevationMapComponent via the GameManager facade, re-applied on ElevationChanged).
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

	// Extra Y-scale multiplier per elevation level (from ElevationMapComponent):
	// a hex at level N stands (1 + N * HeightScalePerLevel)× its native height.
	[Export] public float HeightScalePerLevel { get; set; } = 0.5f;

	// When true, each tile gets a Label3D showing its hex/chunk coords.
	[Export] public bool ShowTileLabels { get; set; } = true;

	private float _outerRadius = DefaultHexOuterRadius;
	private int _chunkRadius = DefaultChunkHexRadius;
	private int _chunkCols;
	private int _chunkRows;

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
		if (GameManager.ElevationMap is { } map) map.ElevationChanged += OnElevationChanged;
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
		if (GameManager.ElevationMap is { } map) map.ElevationChanged -= OnElevationChanged;
		ClearAll();
		base._ExitTree();
	}

	// Elevation rows stream in/out with the AOI — re-raise the surviving tiles' columns.
	private void OnElevationChanged()
	{
		foreach (var (hex, tile) in _tiles)
			ApplyElevation(hex.X, hex.Y, tile, _labels.GetValueOrDefault(hex));
	}

	// --- TableBinderComponent signal handlers (wired in world_3d.tscn) ---
	// The binder has ReplayExistingRows on, so a row already in the client cache comes
	// through the same insert path — no separate Id.Find() replay here.

	private void OnMapConfigRow()
	{
		var cfg = (MapConfig)_mapConfigBinder.LastRow!;
		_chunkRadius = cfg.ChunkHexRadius;
		_outerRadius = cfg.HexOuterRadius;
		_chunkCols = cfg.ChunkCols;
		_chunkRows = cfg.ChunkRows;
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
		tile.Position = HexMath.HexToWorld3D(q, r, _outerRadius);
		AddChild(tile);
		_tiles[new Vector2I(q, r)] = tile;

		Label3D? label = null;
		if (ShowTileLabels)
		{
			label = new Label3D();
			label.HorizontalAlignment = HorizontalAlignment.Center;
			label.PixelSize = 0.3f;
			label.FontSize = 14;
			label.Modulate = Colors.White;
			label.OutlineSize = 2;
			label.OutlineModulate = Colors.Black;
			label.RotationDegrees = new Vector3(-90f, 0f, 0f);
			AddChild(label);
			_labels[new Vector2I(q, r)] = label;
		}

		ApplyElevation(q, r, tile, label);
	}

	// Stretches the tile column to its elevation level and floats the label above the
	// column top. The KayKit hex meshes hang BELOW their origin (top face at local y=0),
	// so a bare Y scale grows the column downward into the ground — the tile is lifted
	// by the extra height instead, keeping the base fixed and raising the top face.
	private void ApplyElevation(int q, int r, Node3D tile, Label3D? label)
	{
		// q,r is the raw node-space hex (tile position and keys stay node-space so the ring
		// follows the player); elevation rows and chunk ids are server-canonical — wrap for
		// both the lookup and the label text.
		var canonical = HexMath.WrapHex(q, r, _chunkRadius, _chunkCols, _chunkRows);
		int level = GameManager.ElevationMap?.GetElevation(canonical.X, canonical.Y) ?? 0;
		float s = _outerRadius * TileScaleMultiplier;
		float yScale = 1f + level * HeightScalePerLevel;

		// Tile-space mesh bounds at scale 1 (native height is derived, not hardcoded).
		Aabb localBox = new();
		foreach (var child in tile.GetChildren())
			if (child is MeshInstance3D mi) { localBox = mi.Transform * mi.GetAabb(); break; }

		tile.Scale = new Vector3(s, yScale, s);
		float lift = localBox.Position.Y * (1f - yScale);
		var basePos = HexMath.HexToWorld3D(q, r, _outerRadius);
		tile.Position = basePos + new Vector3(0f, lift, 0f);

		if (label == null) return;
		var chunk = HexMath.ToLowerRes(canonical.X, canonical.Y, _chunkRadius);
		label.Text = $"({canonical.X},{canonical.Y})\nc({chunk.X},{chunk.Y}) i={HexMath.SpiralIndex(chunk.X, chunk.Y)} e={level}";
		float topY = lift + localBox.End.Y * yScale;
		label.Position = basePos + new Vector3(0f, topY + 0.15f, 0f);
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
