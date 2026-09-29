#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Runtime hex terrain (3D), child in world_3d.tscn. The MST child is switched to the fork's
/// HEX_RINGS grid type: one MarchingSquaresTerrain chunk per server chunk, each a low-res hex
/// owning its disk of micro-hexes (the server hexmod layout). Wrap is enabled from MapConfig
/// (wrap_chunk_cols/rows), so seam cell lookups read the opposite edge's cells and chunk meshes
/// are seamless across the torus seam; the ChunkAprons mirror pool extends the rendered lap
/// around the camera (whole chunk meshes translated by lap vectors).
///
/// Heights: the NearbyBuildingTiles rows carry the per-hex world-unit height
/// (BuildingTile.height — the addon's height_map value). A replaying binder caches them and the
/// dirty-flag sync pushes changed heights into the owning chunks via
/// chunk.draw_height(local_x, local_y, height), then regenerates only the chunks whose cells
/// changed plus their axial neighbours (walls read the neighbour chunk's cells). Chunks use
/// merge_mode SPHERICAL, so small height deltas render as ramps instead of walls.
///
/// Known limitations:
/// - Height rows are AOI-filtered (NearbyBuildingTiles, 2-chunk ring): distant chunks render
///   flat at 0 until the player approaches — same data scope as the 2D terrain.
/// - Server HexTile ground/overlay textures are a per-hex 2D sprite system with no 1:1 mapping
///   onto MST texture slots; the terrain uses the plugin's default ground texture for now.
/// - River/road edge masks are not drawn here — the sibling TerrainRoadsComponent3D renders
///   them as flat strips; wall_mask is always 0 today.
/// </summary>
public partial class TerrainComponent3D : Node3DComponent
{
	// Axial chunk-neighbor deltas (server HEX_NEIGHBOR_DELTAS order, same table as the addon's
	// MarchingSquaresHexRingsGrid.NEIGHBOR_OFFSETS) — a cell change and the chunks whose walls
	// read it must regenerate.
	private static readonly Vector2I[] ChunkNeighborOffsets =
	{
		new(1, 0), new(0, 1), new(-1, 1), new(-1, 0), new(0, -1), new(1, -1),
	};

	// The addon's MarchingSquaresTerrain.GridType.HEX_RINGS (4th enum value).
	private const int GridTypeHexRings = 3;
	// The addon's MarchingSquaresCellChunk.Mode.SPHERICAL (threshold 20 world units): steps up
	// to 20 become 45-degree ramps, anything steeper stays a wall.
	private const int MergeModeSpherical = 4;
	// dimensions.y is only a shader hint (the cell count comes from chunk_hex_radius).
	private const int DimensionsHeightHint = 32;

	private static readonly float Sqrt3 = Mathf.Sqrt(3f);

	private Node3D _terrain = null!; // the MarchingSquaresTerrain child (GDScript, untyped interop)
	private TableBinderComponent _mapConfigBinder = null!;
	private TableBinderComponent _buildingTilesBinder = null!;

	private readonly Dictionary<Vector2I, Node3D> _chunkNodes = new();
	// Server height rows by canonical hex (world units), mirrored from the binder.
	private readonly Dictionary<(int Q, int R), int> _heights = new();
	// Last height pushed per canonical hex — the draw_height analogue of _applied.
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

	// Chunk apron mirrors (see RefreshAprons): a pool of visual-only MeshInstance3D copies
	// of canonical chunks placed at non-zero lap offsets around the camera.
	private const int ApronPoolSize = 12;
	private const float ApronCameraMoveThreshold = 16f;
	// Lap-offset candidates in lap-vector units: every (a, b) in {-1, 0, 1}² except (0, 0) —
	// the canonical chunk node itself supplies the unshifted copy.
	private static readonly Vector2I[] ApronLapOffsets =
	{
		new(1, 0), new(1, 1), new(0, 1), new(-1, 1),
		new(-1, 0), new(-1, -1), new(0, -1), new(1, -1),
	};

	private readonly List<MeshInstance3D> _aprons = new();
	private readonly List<(float DistSq, Vector3 Pos, MeshInstance3D Chunk)> _apronCandidates = new();
	private Node3D? _apronsRoot;
	private Vector3 _lastApronCameraPos;
	private bool _apronsPlaced;

	private int _chunkRadius;
	private int _chunkCols;
	private int _chunkRows;
	private (int Radius, int Cols, int Rows, float HexSize)? _builtConfig;

	public override void _Ready()
	{
		base._Ready();
		_terrain = GetNode<Node3D>("MarchingSquaresTerrain");
		_mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
		_buildingTilesBinder = GetNode<TableBinderComponent>("NearbyBuildingTilesBinder");
		if (GameManager.ElevationMap is { } map) map.ElevationChanged += OnElevationChanged;
	}

	public override void _ExitTree()
	{
		if (GameManager.ElevationMap is { } map) map.ElevationChanged -= OnElevationChanged;
		base._ExitTree();
	}

	private void OnElevationChanged() => _elevationDirty = true;

	// --- TableBinderComponent signal handlers (wired in world_3d.tscn) ---
	// The binder has ReplayExistingRows on, so rows already in the client cache come through
	// the same insert path — no separate Id.Find() replay here.

	private void OnMapConfigRow()
	{
		var cfg = (MapConfig)_mapConfigBinder.LastRow!;
		var key = (cfg.ChunkHexRadius, cfg.ChunkCols, cfg.ChunkRows, cfg.HexOuterRadius);
		if (_chunksBuilt && key == _builtConfig) return;
		_builtConfig = key;
		_chunkRadius = cfg.ChunkHexRadius;
		_chunkCols = cfg.ChunkCols;
		_chunkRows = cfg.ChunkRows;

		// Apron mirrors reference chunk meshes; drop them before the old chunks are freed.
		HideAprons();
		_apronsPlaced = false;

		// Clear a previous grid (config changed mid-session).
		foreach (var chunk in _chunkNodes.Values) chunk.QueueFree();
		_chunkNodes.Clear();
		_applied.Clear(); // row cache _heights survives; every rebuilt chunk re-pushes
		_terrain.Set("chunks", new Godot.Collections.Dictionary());

		// Global settings before any chunk exists. One MST chunk per server chunk (hex-ring
		// lattice): the cell count comes from chunk_hex_radius, cell_size.x is the hex center
		// spacing (sqrt(3) * outer radius), and wrap_chunk_cols/rows make seam lookups
		// canonicalize into the lap.
		_terrain.Set("grid_type", GridTypeHexRings);
		_terrain.Set("chunk_hex_radius", _chunkRadius);
		_terrain.Set("cell_size", new Vector2(cfg.HexOuterRadius * Sqrt3, cfg.HexOuterRadius * Sqrt3));
		_terrain.Set("dimensions", new Vector3I(2 * _chunkRadius + 1, DimensionsHeightHint, 2 * _chunkRadius + 1));
		_terrain.Set("wrap_chunk_cols", cfg.ChunkCols);
		_terrain.Set("wrap_chunk_rows", cfg.ChunkRows);

		for (int cq = 0; cq < _chunkCols; cq++)
			for (int cr = 0; cr < _chunkRows; cr++)
				_terrain.Call("add_new_chunk", cq, cr, default(Variant)); // null plugin arg

		// Snapshot the terrain's chunks dictionary (chunk coords → chunk node).
		foreach (var kv in (Godot.Collections.Dictionary)_terrain.Get("chunks"))
			_chunkNodes[(Vector2I)kv.Key] = (Node3D)kv.Value.AsGodotObject();

		// Ramp small height steps instead of stepping walls.
		foreach (var chunk in _chunkNodes.Values)
			chunk.Set("merge_mode", MergeModeSpherical);

		// add_new_chunk marks chunks data-dirty, which makes the terrain's deferred enter-tree
		// skip its own force_batch_update — apply the texture shader params ourselves.
		_terrain.Call("force_batch_update");

		_chunksBuilt = true;
		_elevationDirty = true; // full sync: _applied is empty, so every cell differs
	}

	private void OnBuildingTileRowInserted() => SetHeight((BuildingTile)_buildingTilesBinder.LastRow!);

	private void OnBuildingTileRowUpdated() => SetHeight((BuildingTile)_buildingTilesBinder.LastRow!);

	private void OnBuildingTileRowDeleted()
	{
		var tile = (BuildingTile)_buildingTilesBinder.LastDeletedRow!;
		if (_heights.Remove((tile.HexQ, tile.HexR)))
			_elevationDirty = true;
	}

	private void SetHeight(BuildingTile tile)
	{
		_heights[(tile.HexQ, tile.HexR)] = tile.Height;
		_elevationDirty = true;
	}

	public override void _Process(double _delta)
	{
		if (_elevationDirty && _chunksBuilt)
		{
			_elevationDirty = false;
			SyncHeights();
		}

		if (GetViewport()?.GetCamera3D() is { } camera
			&& (!_apronsPlaced || _lastApronCameraPos.DistanceSquaredTo(camera.GlobalPosition) > ApronCameraMoveThreshold * ApronCameraMoveThreshold))
			RefreshAprons();
	}

	// Pushes the cached per-hex heights into every chunk in one pass. Only cells whose height
	// changed since the last sync are written (draw_height), each written cell dirties its chunk
	// plus the axial chunk-neighbours (their walls read this chunk's cells), and every dirty
	// chunk regenerates its mesh once.
	private void SyncHeights()
	{
		var dirtyChunks = new HashSet<Vector2I>();
		int r = _chunkRadius;
		int side = 2 * r + 1;

		foreach (var (chunkCoords, chunk) in _chunkNodes)
		{
			for (int lz = 0; lz < side; lz++)
				for (int lx = 0; lx < side; lx++)
				{
					if (!HasLocal(lx, lz, r)) continue; // bounding-box corners are not cells
					var global = GlobalOfLocal(chunkCoords, lx, lz, r);
					// Hexes outside the AOI have no row yet: flat 0 (same as before).
					int height = _heights.TryGetValue((global.X, global.Y), out int cached) ? cached : 0;
					if (_applied.TryGetValue((global.X, global.Y), out int prev) && prev == height) continue;
					_applied[(global.X, global.Y)] = height;
					chunk.Call("draw_height", lx, lz, (float)height);

					dirtyChunks.Add(chunkCoords);
					foreach (var offset in ChunkNeighborOffsets)
					{
						var neighborCoords = chunkCoords + offset;
						// A seam-adjacent cell change must dirty the wrapped partner chunk
						// too: its walls read this cell through the wrapped lookup.
						if (_chunkCols > 0 && _chunkRows > 0)
							neighborCoords = new Vector2I(PosMod(neighborCoords.X, _chunkCols), PosMod(neighborCoords.Y, _chunkRows));
						dirtyChunks.Add(neighborCoords);
					}
				}
		}

		foreach (var coords in dirtyChunks)
			if (_chunkNodes.TryGetValue(coords, out var chunk))
				chunk.Call("regenerate_mesh");

		RefreshShells();
		RefreshAprons();
	}

	// Bounding-box local cell test, lockstep with the addon's
	// MarchingSquaresHexRingsGrid.has_local (a chunk hex disk excludes the box corners).
	private static bool HasLocal(int lx, int lz, int radius)
	{
		int dq = lx - radius;
		int dr = lz - radius;
		return Math.Abs(dq) <= radius && Math.Abs(dr) <= radius && Math.Abs(dq + dr) <= radius;
	}

	// Global axial hex of a chunk-local bbox cell, using the shared ChunkCenterHex mirror
	// (server world/hex.rs::chunk_center_hex).
	private static Vector2I GlobalOfLocal(Vector2I chunkCoords, int lx, int lz, int radius)
	{
		var center = HexMath.ChunkCenterHex(chunkCoords.X, chunkCoords.Y, radius);
		return new Vector2I(center.X + lx - radius, center.Y + lz - radius);
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

	// The canonical MST lap only contains the cols × rows server chunks, while the local
	// player's node can drift up to a lap past it before the server wraps the stored position.
	// This pool extends the rendered lap around the camera: for every canonical chunk, each of
	// the 8 non-zero lap offsets (a*lapQ + b*lapR) is a candidate mirror position, and the
	// pool's nearest N candidates win. Mirrors share the canonical chunk's Mesh (re-assigned on
	// every refresh, so regenerate_mesh is picked up for free) and each carries an UnderShell
	// copy like RefreshShells. They are visual-only: no collision, no gameplay queries, and
	// they live under a ChunkAprons child of this component, never in terrain.chunks. Ring
	// chunks are the server's low-res chunk hexes, so a chunk mesh translated by a lap vector
	// is exactly the terrain at that periodic position. The canonical chunk node itself is the
	// unshifted (0, 0) copy.
	private void RefreshAprons()
	{
		if (!_chunksBuilt || _chunkCols <= 0 || _chunkRows <= 0)
		{
			HideAprons();
			return;
		}
		var camera = GetViewport()?.GetCamera3D();
		var lapQ = GameManager.LapQ;
		var lapR = GameManager.LapR;
		if (camera is null || (lapQ == Vector2.Zero && lapR == Vector2.Zero))
		{
			HideAprons();
			return;
		}

		var lapQ3 = new Vector3(lapQ.X, 0f, lapQ.Y);
		var lapR3 = new Vector3(lapR.X, 0f, lapR.Y);
		var cameraPos = camera.GlobalPosition;

		EnsureApronPool();

		_apronCandidates.Clear();
		foreach (var chunk in _chunkNodes.Values)
		{
			var chunkPos = chunk.GlobalPosition;
			var chunkMesh = (MeshInstance3D)chunk;
			foreach (var offset in ApronLapOffsets)
			{
				var pos = chunkPos + lapQ3 * offset.X + lapR3 * offset.Y;
				_apronCandidates.Add((cameraPos.DistanceSquaredTo(pos), pos, chunkMesh));
			}
		}
		_apronCandidates.Sort(static (a, b) => a.DistSq.CompareTo(b.DistSq));

		int used = 0;
		foreach (var candidate in _apronCandidates)
		{
			if (used == _aprons.Count) break;
			var apron = _aprons[used++];
			apron.Visible = true;
			apron.GlobalPosition = candidate.Pos;
			apron.Mesh = candidate.Chunk.Mesh;
			apron.CastShadow = candidate.Chunk.CastShadow;
			apron.GetNode<MeshInstance3D>("UnderShell").Mesh = candidate.Chunk.Mesh;
		}
		for (; used < _aprons.Count; used++) _aprons[used].Visible = false;

		_lastApronCameraPos = cameraPos;
		_apronsPlaced = true;
	}

	// Lazily creates the apron root (a plain Node3D child of this component, NOT the MST node,
	// so mirrors can never enter terrain.chunks or the chunk save/registration paths) and the
	// mirror pool, each mirror holding an UnderShell child exactly like RefreshShells.
	private void EnsureApronPool()
	{
		if (_apronsRoot is null)
		{
			_apronsRoot = new Node3D { Name = "ChunkAprons" };
			AddChild(_apronsRoot);
		}
		while (_aprons.Count < ApronPoolSize)
		{
			var apron = new MeshInstance3D
			{
				Name = $"ChunkApron{_aprons.Count}",
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Visible = false,
			};
			apron.AddChild(new MeshInstance3D
			{
				Name = "UnderShell",
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Position = new Vector3(0f, -ShellDrop, 0f),
			});
			_apronsRoot.AddChild(apron);
			_aprons.Add(apron);
		}
	}

	// Hides every pool mirror (used when wrapping is off, lap vectors are unavailable, or the
	// grid is being rebuilt); the pool itself is reused.
	private void HideAprons()
	{
		foreach (var apron in _aprons) apron.Visible = false;
	}

	// Positive modulo for wrapping, matching server wrap_chunk_coords / the addon's wrap_chunk.
	private static int PosMod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
