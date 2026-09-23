#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// River/road edge strips (3D), child in world_3d.tscn next to TerrainComponent3D. Mirrors
/// the AOI-filtered NearbyTerrainTiles rows (child binder, replay on — persistent view
/// table) into a (q, r) → HexTile cache; each row's river_mask/road_mask bits mark the
/// hex edges carrying a river or a road (one bit per edge in HEX_NEIGHBOR_DELTAS order,
/// both hexes of an edge carry the mirrored bit). Row handlers only flag dirty; _Process
/// rebuilds the strip meshes at most once per frame (mask rows arrive in bulk at startup).
/// wall_mask is reserved server-side and always 0, so nothing is rendered for it yet.
///
/// Geometry/ownership: for every cached hex and every set river/road bit, one quad is
/// emitted along the shared edge between the hex and its (wrapped) neighbor — midpoint of
/// the two canonical centers, perpendicular to the center-to-center axis, half-length
/// outerRadius/2 (the side length of a regular pointy-top hex). Both hexes carry the bit,
/// so edge direction d is only drawn by the d &lt; 3 owner; when that owner is outside the
/// AOI (neighbor missing from the cache) the d ≥ 3 side draws it instead. Every edge is
/// drawn exactly once and AOI holes still seal. Y is the smooth surface height at the
/// midpoint when the hexes carry height payloads (ElevationMapComponent
/// .TryGetSurfaceHeightAt), otherwise the average of both hexes' elevations (missing
/// neighbor: this hex's level) times WorldHeightPerLevel — plus a small lift above the
/// chunk-mesh tops; a road rides slightly higher than a river so a crossing stays
/// visible. Strips are placed with TorusMath.NearestCandidate against the camera (the
/// ChunkAprons reference), so they follow whichever torus copy the terrain is drawing.
///
/// Visual-only: one reused MeshInstance3D per surface (ArrayMesh rebuilt on change),
/// unshaded opaque materials (river blue, road tan), no collision, shadows off, no
/// gameplay queries and no reducers.
/// </summary>
public partial class TerrainRoadsComponent3D : Node3DComponent
{
	// Axial neighbor deltas in server HEX_NEIGHBOR_DELTAS order — bit d is the edge toward
	// neighbor d. Direction d is world angle 60°·d (pointy-top hexes): d=0 is +X.
	private static readonly Vector2I[] NeighborDeltas =
	{
		new(1, 0), new(0, 1), new(-1, 1), new(-1, 0), new(0, -1), new(1, -1),
	};

	// Same fallback as TerrainComponent3D before the first MapConfig row arrives.
	private const float DefaultHexOuterRadius = 96f;
	private const float Sqrt3 = 1.7320508075688772f;

	// Strip half-widths as fractions of the hex outer radius (river wider than road).
	private const float RiverHalfWidthFactor = 0.35f;
	private const float RoadHalfWidthFactor = 0.18f;
	// Tiny Y lift above the chunk-mesh tops (elevation × WorldHeightPerLevel) so the flat
	// strips don't z-fight the terrain surface; road above river so crossings read.
	private const float RiverLift = 0.30f;
	private const float RoadLift = 0.45f;

	private TableBinderComponent _terrainTilesBinder = null!;
	private TableBinderComponent _mapConfigBinder = null!;
	private MeshInstance3D _riverStrips = null!;
	private MeshInstance3D _roadStrips = null!;

	// One HexTile row per hex (write_hex_tile inserts one), so a plain hex → row map is exact.
	private readonly Dictionary<(int Q, int R), HexTile> _tiles = new();
	private bool _dirty;

	private float _outerRadius = DefaultHexOuterRadius;
	private int _chunkHexRadius;
	private int _chunkCols;
	private int _chunkRows;

	// Camera chunk sentinel forces a rebuild once the camera or MapConfig first arrives, and
	// on later chunk crossings (the nearest torus copy can change with the camera).
	private Vector2I _lastCameraChunk = new(int.MinValue, int.MinValue);

	// Reused vertex/index buffers — cleared per rebuild so a steady state allocates no lists.
	private readonly List<Vector3> _riverVertices = new();
	private readonly List<int> _riverIndices = new();
	private readonly List<Vector3> _roadVertices = new();
	private readonly List<int> _roadIndices = new();

	public override void _Ready()
	{
		base._Ready();
		_terrainTilesBinder = GetNode<TableBinderComponent>("NearbyTerrainTilesBinder");
		_mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
		_riverStrips = GetNode<MeshInstance3D>("RiverStrips");
		_roadStrips = GetNode<MeshInstance3D>("RoadStrips");
		_riverStrips.MaterialOverride = CreateStripMaterial(new Color(0.16f, 0.42f, 0.85f));
		_roadStrips.MaterialOverride = CreateStripMaterial(new Color(0.60f, 0.47f, 0.30f));

		// Elevation rows can arrive after the tile rows that carry the masks — re-height
		// the strips when any level changes (the TerrainComponent3D pattern).
		if (GameManager.ElevationMap is { } map) map.ElevationChanged += OnElevationChanged;
	}

	public override void _ExitTree()
	{
		if (GameManager.ElevationMap is { } map) map.ElevationChanged -= OnElevationChanged;
		base._ExitTree();
	}

	private void OnElevationChanged() => _dirty = true;

	public override void _Process(double _delta)
	{
		// Follow the camera's chunk so the strip placement tracks the torus copy the terrain
		// aprons draw around. A missing camera simply keeps the last rebuild.
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
	// Both binders replay existing rows, so cached rows come through the insert path.

	private void OnMapConfigRow()
	{
		var cfg = (MapConfig)_mapConfigBinder.LastRow!;
		_outerRadius = cfg.HexOuterRadius;
		_chunkHexRadius = cfg.ChunkHexRadius;
		_chunkCols = cfg.ChunkCols;
		_chunkRows = cfg.ChunkRows;
		_dirty = true;
	}

	private void OnTerrainTileRowInserted()
	{
		var tile = (HexTile)_terrainTilesBinder.LastRow!;
		_tiles[(tile.HexQ, tile.HexR)] = tile;
		_dirty = true;
	}

	private void OnTerrainTileRowUpdated()
	{
		var oldTile = (HexTile)_terrainTilesBinder.LastOldRow!;
		_tiles.Remove((oldTile.HexQ, oldTile.HexR));
		var tile = (HexTile)_terrainTilesBinder.LastRow!;
		_tiles[(tile.HexQ, tile.HexR)] = tile;
		_dirty = true;
	}

	private void OnTerrainTileRowDeleted()
	{
		var tile = (HexTile)_terrainTilesBinder.LastDeletedRow!;
		_tiles.Remove((tile.HexQ, tile.HexR));
		_dirty = true;
	}

	// Rebuilds both strip meshes from the cache. Reference for the torus copy pick is the
	// camera (mirrors ChunkAprons), falling back to the local player.
	private void Rebuild()
	{
		_riverVertices.Clear();
		_riverIndices.Clear();
		_roadVertices.Clear();
		_roadIndices.Clear();

		var reference = Vector2.Zero;
		if (GetViewport()?.GetCamera3D() is { } camera)
			reference = new Vector2(camera.GlobalPosition.X, camera.GlobalPosition.Z);
		else if (LocalPlayer.Local is { } player)
			reference = player.GlobalPosition;

		var lapQ = GameManager.LapQ;
		var lapR = GameManager.LapR;
		var origin = GlobalPosition;

		foreach (var ((q, r), tile) in _tiles)
		{
			byte riverMask = tile.RiverMask;
			byte roadMask = tile.RoadMask;
			if (riverMask == 0 && roadMask == 0) continue;

			var center = HexMath.HexToWorld3D(q, r, _outerRadius);
			int elevationA = GameManager.ElevationMap?.GetElevation(q, r) ?? 0;

			for (int d = 0; d < 6; d++)
			{
				bool hasRiver = (riverMask & (1 << d)) != 0;
				bool hasRoad = (roadMask & (1 << d)) != 0;
				if (!hasRiver && !hasRoad) continue;

				var delta = NeighborDeltas[d];
				var neighbor = HexMath.WrapHex(q + delta.X, r + delta.Y, _chunkHexRadius, _chunkCols, _chunkRows);
				bool neighborCached = _tiles.ContainsKey((neighbor.X, neighbor.Y));

				// Ownership: the d < 3 hex draws every shared edge (d+3 ≥ 3 for the partner);
				// only when that owner is outside the AOI does the d ≥ 3 side draw it, so each
				// edge renders exactly once and missing-neighbor edges still seal.
				if (d >= 3 && neighborCached) continue;

				int elevationB = neighborCached
					? GameManager.ElevationMap?.GetElevation(neighbor.X, neighbor.Y) ?? 0
					: elevationA;

				// Shared edge: midpoint of the two centers, edge direction perpendicular to
				// the center-to-center axis, half-length = half the shared side (side length
				// equals the outer radius for a regular pointy-top hex).
				float angle = Mathf.DegToRad(60f * d);
				var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));   // unit center → neighbor
				var perp = new Vector2(-dir.Y, dir.X);                        // unit along the edge
				var midCanonical = new Vector2(center.X, center.Z) + dir * (Sqrt3 * 0.5f * _outerRadius);
				var mid = TorusMath.NearestCandidate(midCanonical, reference, lapQ, lapR);

				// Seat the midpoint on the smooth surface when the hexes have height payloads;
				// otherwise fall back to the average stepped level (the pre-heights behavior).
				float baseY = GameManager.ElevationMap is { } elevationMap
					&& elevationMap.TryGetSurfaceHeightAt(midCanonical, out float surfaceY)
						? surfaceY
						: (elevationA + elevationB) * 0.5f * ElevationMapComponent.WorldHeightPerLevel;
				float halfLength = _outerRadius * 0.5f;

				if (hasRiver)
					AddStrip(_riverVertices, _riverIndices, mid, dir, perp,
						_outerRadius * RiverHalfWidthFactor, halfLength, baseY + RiverLift, origin);
				if (hasRoad)
					AddStrip(_roadVertices, _roadIndices, mid, dir, perp,
						_outerRadius * RoadHalfWidthFactor, halfLength, baseY + RoadLift, origin);
			}
		}

		ApplyStripMesh(_riverStrips, _riverVertices, _riverIndices);
		ApplyStripMesh(_roadStrips, _roadVertices, _roadIndices);
	}

	// One quad straddling the edge line: extended ±halfWidth along the center→neighbor axis
	// and ±halfLength along the edge. Vertices are world-space minus the component origin.
	private static void AddStrip(
		List<Vector3> vertices, List<int> indices,
		Vector2 mid, Vector2 dir, Vector2 perp,
		float halfWidth, float halfLength, float y, Vector3 origin)
	{
		var width = dir * halfWidth;
		var length = perp * halfLength;
		int baseIndex = vertices.Count;

		vertices.Add(new Vector3(mid.X + width.X + length.X, y, mid.Y + width.Y + length.Y) - origin);
		vertices.Add(new Vector3(mid.X + width.X - length.X, y, mid.Y + width.Y - length.Y) - origin);
		vertices.Add(new Vector3(mid.X - width.X - length.X, y, mid.Y - width.Y - length.Y) - origin);
		vertices.Add(new Vector3(mid.X - width.X + length.X, y, mid.Y - width.Y + length.Y) - origin);

		indices.Add(baseIndex);
		indices.Add(baseIndex + 1);
		indices.Add(baseIndex + 2);
		indices.Add(baseIndex);
		indices.Add(baseIndex + 2);
		indices.Add(baseIndex + 3);
	}

	private static void ApplyStripMesh(MeshInstance3D instance, List<Vector3> vertices, List<int> indices)
	{
		if (indices.Count == 0)
		{
			instance.Mesh = null;
			instance.Visible = false;
			return;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		instance.Mesh = mesh;
		instance.Visible = true;
	}

	private static StandardMaterial3D CreateStripMaterial(Color color) => new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		AlbedoColor = color,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};
}
