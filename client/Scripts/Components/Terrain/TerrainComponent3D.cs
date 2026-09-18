#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Runtime hex terrain (3D), child in world_3d.tscn. Builds the whole canonical lap of
/// MarchingSquaresTerrain chunks (ChunkCols × ChunkRows from the MapConfig row, via a
/// child binder — the HexGridOverlay3DComponent pattern) and feeds per-hex elevation
/// levels from the GameManager.ElevationMap facade into chunk.draw_elevation, so raised
/// hexes render as stepped columns with cliff walls. level_height is locked to
/// ElevationMapComponent.WorldHeightPerLevel, so mesh tops land exactly where
/// GetWorldHeightAt puts characters and cursor raycasts.
///
/// The terrain's hex_size/level_height/chunk_radius are storage-only properties whose
/// setters regen existing chunks (a no-op while empty), so they are set before any chunk
/// is added. add_new_chunk's plugin arg is null-safe (add_chunk guards on it). Chunks
/// are marked data-dirty on add, which makes the terrain's _deferred_enter_tree skip its
/// force_batch_update (the texture shader params) — so we call force_batch_update
/// ourselves after building the grid.
///
/// Known limitations:
/// - Only the canonical lap (0..cols × 0..rows chunks) is built; across the torus seam
///   the edge chunks read void neighbors (cliff walls at the world edge) — same
///   unwrapped rendering as the 3D overlay. The server position-wrap makes the far
///   side correct once crossed.
/// - Elevation rows are AOI-filtered (NearbyBuildingTiles, 2-chunk ring): distant chunks
///   render flat at level 0 until the player approaches — same data scope as the 2D
///   terrain.
/// - Server HexTile ground/overlay textures are a per-hex 2D sprite system with
///   no 1:1 mapping onto MST texture slots; the terrain uses the plugin's default
///   ground texture for now.
/// </summary>
public partial class TerrainComponent3D : Node3DComponent
{
	// Axial chunk-neighbor deltas (server neighbor-delta order, same table as
	// MSTHexMath.NEIGHBOR_OFFSETS) — border walls read the neighbor chunk's hex_data.
	private static readonly Vector2I[] ChunkNeighborOffsets =
	{
		new(1, 0), new(0, 1), new(-1, 1), new(-1, 0), new(0, -1), new(1, -1),
	};

	private Node3D _terrain = null!; // the MarchingSquaresTerrain child (GDScript, untyped interop)
	private TableBinderComponent _mapConfigBinder = null!;

	private readonly Dictionary<Vector2I, Node3D> _chunkNodes = new();
	private readonly Dictionary<(int Q, int R), int> _applied = new();
	private bool _chunksBuilt;
	private bool _elevationDirty;

	// Dark duplicate of each chunk mesh hung below the real tops (see
	// RefreshShells). Shared unshaded soil material, created on first use.
	// 10u catches buried sightlines starting up to 10 below the tops (the
	// observed hole band) while staying far above the below-world ray
	// origins at pitch <= ~20, which must keep missing it so the verified
	// sea-horizon look is preserved.
	private const float ShellDrop = 10f;
	private StandardMaterial3D? _shellMaterial;

	private int _chunkRadius;
	private int _chunkCols;
	private int _chunkRows;
	private (int Radius, int Cols, int Rows, float HexSize)? _builtConfig;

	public override void _Ready()
	{
		base._Ready();
		_terrain = GetNode<Node3D>("MarchingSquaresTerrain");
		_mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
		if (GameManager.ElevationMap is { } map) map.ElevationChanged += OnElevationChanged;
	}

	public override void _ExitTree()
	{
		if (GameManager.ElevationMap is { } map) map.ElevationChanged -= OnElevationChanged;
		base._ExitTree();
	}

	private void OnElevationChanged() => _elevationDirty = true;

	// --- TableBinderComponent signal handlers (wired in world_3d.tscn) ---
	// The binder has ReplayExistingRows on, so a row already in the client cache comes
	// through the same insert path — no separate Id.Find() replay here.

	private void OnMapConfigRow()
	{
		var cfg = (MapConfig)_mapConfigBinder.LastRow!;
		var key = (cfg.ChunkHexRadius, cfg.ChunkCols, cfg.ChunkRows, cfg.HexOuterRadius);
		if (_chunksBuilt && key == _builtConfig) return;
		_builtConfig = key;
		_chunkRadius = cfg.ChunkHexRadius;
		_chunkCols = cfg.ChunkCols;
		_chunkRows = cfg.ChunkRows;

		// Clear a previous grid (config changed mid-session).
		foreach (var chunk in _chunkNodes.Values) chunk.QueueFree();
		_chunkNodes.Clear();
		_applied.Clear();
		_terrain.Set("chunks", new Godot.Collections.Dictionary());

		// Global settings before any chunk exists (the setters regen all chunks; empty = no-op).
		_terrain.Set("hex_size", cfg.HexOuterRadius);
		_terrain.Set("level_height", ElevationMapComponent.WorldHeightPerLevel);
		_terrain.Set("chunk_radius", cfg.ChunkHexRadius);

		for (int cq = 0; cq < _chunkCols; cq++)
			for (int cr = 0; cr < _chunkRows; cr++)
				_terrain.Call("add_new_chunk", cq, cr, default(Variant)); // null plugin arg

		// Snapshot the terrain's chunks dictionary (Vector2i coords → chunk node).
		foreach (var kv in (Godot.Collections.Dictionary)_terrain.Get("chunks"))
			_chunkNodes[(Vector2I)kv.Key] = (Node3D)kv.Value.AsGodotObject();

		// add_new_chunk marks chunks data-dirty, which makes _deferred_enter_tree return
		// before its own force_batch_update — apply the texture shader params ourselves.
		_terrain.Call("force_batch_update");

		_chunksBuilt = true;
		_elevationDirty = true; // full sync: _applied is empty, so every hex differs
	}

	public override void _Process(double _delta)
	{
		if (!_elevationDirty || !_chunksBuilt) return;
		_elevationDirty = false;
		SyncElevation();
	}

	// Pushes the ElevationMap's per-hex levels into every chunk. Only hexes whose level
	// changed since the last sync are written; each written hex dirties its chunk plus
	// the in-grid axial chunk-neighbors (border walls read neighbor hex_data), and every
	// dirty chunk regenerates its mesh once.
	private void SyncElevation()
	{
		var map = GameManager.ElevationMap;
		var dirtyChunks = new HashSet<Vector2I>();
		int R = _chunkRadius;

		foreach (var (chunkCoords, chunk) in _chunkNodes)
		{
			var center = HexMath.ChunkCenterHex(chunkCoords.X, chunkCoords.Y, R);
			for (int dq = -R; dq <= R; dq++)
				for (int dr = Mathf.Max(-R, -dq - R); dr <= Mathf.Min(R, -dq + R); dr++)
				{
					int gq = center.X + dq, gr = center.Y + dr;
					int level = map?.GetElevation(gq, gr) ?? 0; // 0 outside the AOI
					if (_applied.TryGetValue((gq, gr), out int prev) && prev == level) continue;
					_applied[(gq, gr)] = level;
					chunk.Call("draw_elevation", new Vector2I(dq, dr), level);
					dirtyChunks.Add(chunkCoords);
					foreach (var offset in ChunkNeighborOffsets)
						dirtyChunks.Add(chunkCoords + offset);
				}
		}

		foreach (var coords in dirtyChunks)
			if (_chunkNodes.TryGetValue(coords, out var chunk))
				chunk.Call("regenerate_mesh");

		RefreshShells();
	}

	// Every chunk mesh is a hollow shell (tops + cliff walls, no interior or
	// bottom). At shallow pitches the ortho frustum slices foreground hills
	// open: bottom-edge ray origins dip below the hilltops, and those
	// below-top sightlines thread under the tops, through the backface-culled
	// step risers, and land on the BackingOcean — water-blue pixels framed by
	// walls that read as "clipped ground" (proven pixel-identical with the
	// terrain hidden). A dark duplicate of each chunk mesh hung ShellDrop
	// below the tops terminates those rays on earth instead. Default
	// backface culling is kept on the duplicates, so below-world rays at
	// pitch <= ~20 still miss them from underneath and the verified
	// sea-horizon look is preserved; ShellDrop is orders of magnitude above
	// ortho depth precision, so normal views never see the duplicates. The
	// duplicates are visual-only (no collision), share each chunk's mesh (no
	// extra geometry memory), and are runtime children of game-only nodes, so
	// the editor never saves them.
	private void RefreshShells()
	{
		_shellMaterial ??= new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.15f, 0.13f, 0.09f),
		};
		foreach (var (coords, chunk) in _chunkNodes)
		{
			if (chunk.GetNodeOrNull("UnderShell") is not MeshInstance3D shell)
			{
				shell = new MeshInstance3D
				{
					Name = "UnderShell",
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
					Position = new Vector3(0f, -ShellDrop, 0f),
				};
				chunk.AddChild(shell);
			}
			shell.Mesh = ((MeshInstance3D)chunk).Mesh;
		}
	}
}
