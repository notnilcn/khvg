#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// Per-profile explored-map memory layer (3D), child of World3DComponent in world_3d.tscn
/// beside TerrainRoadsComponent3D. Mirrors the per-caller local_player_explored view — one
/// PlayerExploredChunk row per revealed terrain AOI chunk, each carrying that chunk's hex
/// snapshots (q/r, elevation, masks, texture ids) — into a chunk_index → row cache (child
/// LocalPlayerExploredBinder, replay on — persistent view table).
///
/// Live-vs-explored selection: the sibling NearbyTerrainTiles binder caches the live AOI
/// rows; during rebuild the live chunk set is recomputed from that cache (ToLowerRes per
/// tile). Any explored chunk whose wrapped (chunk_q, chunk_r) is in the live set is skipped
/// — the live terrain/roads/water components already draw those hexes — so this layer only
/// ever draws explored memory OUTSIDE the live AOI and never overlaps live terrain. The
/// pair comparison is equivalent to the row's spiral chunk_index (the server keys rows by
/// spiral_chunk_index over the same wrapped chunk coords) and avoids a spiral-index round
/// trip. Snapshots exist because world generation is static after internal_generate_world;
/// this component reads rows only — no gameplay queries, no reducers, no state.
///
/// Geometry/look: one MeshInstance3D with a rebuilt ArrayMesh — per hex a flat pointy-top
/// top face at the snapshot elevation plus a short skirt (MemoryTileDepth) so the tile
/// reads from the camera's shallow pitch. Tiles are placed at their canonical
/// HexMath.HexToWorld3D position, Y = elevation × WorldHeightPerLevel + MemoryLift, then
/// TorusMath.NearestCandidate against the camera XZ so the memory map follows the same
/// torus copy as the mirrored terrain near seams. Unshaded StandardMaterial3D, dark
/// desaturated memory green; vertex colors brighten the top face with elevation level
/// (skirt darker), no collision, shadows off. Snapshot texture ids/rotations are cached but
/// not mapped to 3D slots yet — same constraint as TerrainComponent3D.
///
/// Null-safe: no camera, no MapConfig row, or no explored rows → nothing rendered. Row
/// handlers only flag dirty; _Process rebuilds at most once per frame (and on camera chunk
/// crossings, since the nearest torus copy can change).
/// </summary>
public partial class ExploredMapComponent3D : Node3DComponent
{
	// Ring vertices at 60° steps from +X (pointy-top, the AbilityPreview3D /
	// HexMath.HexToWorld3D convention).
	private const int HexSides = 6;

	// Same fallback as TerrainComponent3D before the first MapConfig row arrives.
	private const float DefaultHexOuterRadius = 96f;

	// Skirt depth below the top face: a small lip, not a full prism — cheapest read that
	// still shows the tile from the camera's shallow pitch.
	private const float MemoryTileDepth = 1.5f;

	// Lift above the hex's true elevation surface (MST level_height is
	// WorldHeightPerLevel), so the flat tops don't z-fight the terrain at level 0.
	private const float MemoryLift = 0.35f;

	// Skirt tint multiplier on the elevation-ramped top tint.
	private const float SkirtTint = 0.5f;

	private TableBinderComponent _exploredBinder = null!;
	private TableBinderComponent _terrainTilesBinder = null!;
	private TableBinderComponent _mapConfigBinder = null!;
	private MeshInstance3D _tiles = null!;

	private readonly Dictionary<long, PlayerExploredChunk> _explored = new();
	// Live AOI tile rows (one HexTile row per hex) — only used to recompute the live chunk
	// set at rebuild time.
	private readonly Dictionary<(int Q, int R), HexTile> _liveTiles = new();
	private bool _dirty;

	private float _outerRadius = DefaultHexOuterRadius;
	private int _chunkHexRadius;
	private bool _mapConfigReady;

	// Camera chunk sentinel forces a rebuild once the camera or MapConfig first arrives, and
	// on later chunk crossings (the nearest torus copy can change with the camera).
	private Vector2I _lastCameraChunk = new(int.MinValue, int.MinValue);

	// Reused vertex buffers — cleared per rebuild so a steady state allocates no lists.
	private readonly List<Vector3> _vertices = new();
	private readonly List<Color> _colors = new();
	private readonly List<int> _indices = new();

	public override void _Ready()
	{
		base._Ready();
		_exploredBinder = GetNode<TableBinderComponent>("LocalPlayerExploredBinder");
		_terrainTilesBinder = GetNode<TableBinderComponent>("NearbyTerrainTilesBinder");
		_mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
		_tiles = GetNode<MeshInstance3D>("MemoryTiles");
		_tiles.MaterialOverride = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.22f, 0.26f, 0.24f),
			VertexColorUseAsAlbedo = true,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
		};
	}

	public override void _Process(double _delta)
	{
		// Follow the camera's chunk so the memory tiles track the torus copy the terrain
		// draws around. A missing camera simply keeps the last rebuild.
		if (GetViewport()?.GetCamera3D() is { } camera)
		{
			var camHex = HexMath.WorldToHex(camera.GlobalPosition.X, camera.GlobalPosition.Z, _outerRadius);
			var camChunk = _chunkHexRadius > 0
				? HexMath.ToLowerRes(camHex.X, camHex.Y, _chunkHexRadius)
				: camHex;
			if (camChunk != _lastCameraChunk)
			{
				_lastCameraChunk = camChunk;
				_dirty = true;
			}
		}

		if (!_dirty) return;
		_dirty = false;
		Rebuild();
	}

	// --- TableBinderComponent signal handlers (wired in world_3d.tscn) ---
	// All three binders replay existing rows, so cached rows come through the insert path.

	private void OnMapConfigRow()
	{
		var cfg = (MapConfig)_mapConfigBinder.LastRow!;
		_outerRadius = cfg.HexOuterRadius;
		_chunkHexRadius = cfg.ChunkHexRadius;
		_mapConfigReady = true;
		_dirty = true;
	}

	private void OnExploredRowInserted()
	{
		var row = (PlayerExploredChunk)_exploredBinder.LastRow!;
		_explored[row.ChunkIndex] = row;
		_dirty = true;
	}

	private void OnExploredRowUpdated()
	{
		var oldRow = (PlayerExploredChunk)_exploredBinder.LastOldRow!;
		_explored.Remove(oldRow.ChunkIndex);
		var row = (PlayerExploredChunk)_exploredBinder.LastRow!;
		_explored[row.ChunkIndex] = row;
		_dirty = true;
	}

	private void OnExploredRowDeleted()
	{
		var row = (PlayerExploredChunk)_exploredBinder.LastDeletedRow!;
		if (_explored.Remove(row.ChunkIndex)) _dirty = true;
	}

	private void OnTerrainTileRowInserted()
	{
		var tile = (HexTile)_terrainTilesBinder.LastRow!;
		_liveTiles[(tile.HexQ, tile.HexR)] = tile;
		_dirty = true;
	}

	private void OnTerrainTileRowUpdated()
	{
		var oldTile = (HexTile)_terrainTilesBinder.LastOldRow!;
		_liveTiles.Remove((oldTile.HexQ, oldTile.HexR));
		var tile = (HexTile)_terrainTilesBinder.LastRow!;
		_liveTiles[(tile.HexQ, tile.HexR)] = tile;
		_dirty = true;
	}

	private void OnTerrainTileRowDeleted()
	{
		var tile = (HexTile)_terrainTilesBinder.LastDeletedRow!;
		_liveTiles.Remove((tile.HexQ, tile.HexR));
		_dirty = true;
	}

	// Rebuilds the memory mesh from the explored cache, skipping chunks whose live AOI rows
	// are cached (the live terrain is authoritative there). Reference for the torus copy
	// pick is the camera; without one (or before MapConfig) nothing is rendered.
	private void Rebuild()
	{
		var camera = GetViewport()?.GetCamera3D();
		if (!_mapConfigReady || camera == null)
		{
			ClearMesh();
			return;
		}

		// Live chunk set from the tile cache: rebuilds are dirty-driven, so recomputing it
		// here is cheaper than maintaining it incrementally across row events.
		var liveChunks = new HashSet<(int Q, int R)>();
		foreach (var (q, r) in _liveTiles.Keys)
		{
			var chunk = _chunkHexRadius > 0 ? HexMath.ToLowerRes(q, r, _chunkHexRadius) : new Vector2I(q, r);
			liveChunks.Add((chunk.X, chunk.Y));
		}

		var reference = new Vector2(camera.GlobalPosition.X, camera.GlobalPosition.Z);
		var lapQ = GameManager.LapQ;
		var lapR = GameManager.LapR;
		var origin = GlobalPosition;
		float maxLevel = Mathf.Max(ServerConstants.ElevationLevels - 1, 1);

		_vertices.Clear();
		_colors.Clear();
		_indices.Clear();

		foreach (var row in _explored.Values)
		{
			if (liveChunks.Contains((row.ChunkQ, row.ChunkR))) continue;

			foreach (var hex in row.Hexes)
			{
				var center = HexMath.HexToWorld3D(hex.HexQ, hex.HexR, _outerRadius);
				var placed = TorusMath.NearestCandidate(new Vector2(center.X, center.Z), reference, lapQ, lapR);
				float y = hex.Elevation * ElevationMapComponent.WorldHeightPerLevel + MemoryLift;
				float level = Mathf.Clamp(hex.Elevation / maxLevel, 0f, 1f);
				AddTile(placed.X, y, placed.Y, 0.85f + 0.35f * level, origin);
			}
		}

		ApplyMesh();
	}

	// One flat top face plus a skirt hanging MemoryTileDepth below it. Top vertices carry
	// the elevation-brightened tint, skirt vertices the darker one (unshaded material with
	// vertex color as albedo). Vertices are world-space minus the component origin, the
	// ArrayMesh convention the sibling TerrainRoadsComponent3D uses.
	private void AddTile(float cx, float y, float cz, float brightness, Vector3 origin)
	{
		int centerIndex = _vertices.Count;
		var topTint = new Color(brightness, brightness, brightness);
		var skirtTint = new Color(brightness * SkirtTint, brightness * SkirtTint, brightness * SkirtTint);

		_vertices.Add(new Vector3(cx, y, cz) - origin);
		_colors.Add(topTint);

		int topBase = _vertices.Count;
		for (int i = 0; i < HexSides; i++)
		{
			float angle = Mathf.Tau * i / HexSides;
			_vertices.Add(new Vector3(cx + Mathf.Cos(angle) * _outerRadius, y, cz + Mathf.Sin(angle) * _outerRadius) - origin);
			_colors.Add(topTint);
		}

		int bottomBase = _vertices.Count;
		for (int i = 0; i < HexSides; i++)
		{
			float angle = Mathf.Tau * i / HexSides;
			_vertices.Add(new Vector3(cx + Mathf.Cos(angle) * _outerRadius, y - MemoryTileDepth, cz + Mathf.Sin(angle) * _outerRadius) - origin);
			_colors.Add(skirtTint);
		}

		for (int i = 0; i < HexSides; i++)
		{
			int j = (i + 1) % HexSides;
			int topI = topBase + i;
			int topJ = topBase + j;
			int bottomI = bottomBase + i;
			int bottomJ = bottomBase + j;

			_indices.Add(centerIndex);
			_indices.Add(topI);
			_indices.Add(topJ);

			_indices.Add(topI);
			_indices.Add(bottomI);
			_indices.Add(bottomJ);
			_indices.Add(topI);
			_indices.Add(bottomJ);
			_indices.Add(topJ);
		}
	}

	private void ApplyMesh()
	{
		if (_indices.Count == 0)
		{
			ClearMesh();
			return;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_tiles.Mesh = mesh;
		_tiles.Visible = true;
	}

	private void ClearMesh()
	{
		_tiles.Mesh = null;
		_tiles.Visible = false;
	}
}
