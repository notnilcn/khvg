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
/// (BuildingTile.height — the addon's height_map value) plus the addon's per-cell smooth flag
/// (BuildingTile.smooth — its smooth_map value). A replaying binder caches them and the
/// dirty-flag sync pushes changed cells into the owning chunks via
/// chunk.draw_height(local_x, local_y, height) and chunk.draw_smooth(local_x, local_y, smooth),
/// then regenerates only the chunks whose cells changed plus their axial neighbours (walls and
/// smooth corner heights read the neighbour chunk's cells). Chunks use merge_mode CUBIC with
/// merge_threshold 0, so the addon's ramp branch never fires: every height difference between
/// non-smooth cells renders as a vertical wall between flat cell tops. Slopes come only from
/// the smooth-flagged cells' corner blending.
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
	// The addon's MarchingSquaresCellChunk.Mode.CUBIC (only its default threshold would matter,
	// and we override that below).
	private const int MergeModeCubic = 0;
	// The addon's merge branch is `step <= merge_threshold` -> 45-degree ramp, else wall.
	// Pinned to 0 so every positive step is a wall: flat-topped hexes with crisp vertical
	// steps, no skirt slants along small height differences. Smooth-flagged cells blend via
	// their corner heights regardless of this. Raise it to re-enable ramps up to that height.
	private const float MergeRampThreshold = 0f;
	// dimensions.y is only a shader hint (the cell count comes from chunk_hex_radius).
	private const int DimensionsHeightHint = 32;

	private static readonly float Sqrt3 = Mathf.Sqrt(3f);

	private Node3D _terrain = null!; // the MarchingSquaresTerrain child (GDScript, untyped interop)
	private TableBinderComponent _mapConfigBinder = null!;
	private TableBinderComponent _buildingTilesBinder = null!;

	private readonly Dictionary<Vector2I, Node3D> _chunkNodes = new();
	// Server terrain cells by canonical hex, mirrored from the binder: world-unit height plus
	// the addon's smooth (bridge-blended top) flag.
	private readonly Dictionary<(int Q, int R), (int Height, bool Smooth)> _cells = new();
	// Last height/smooth pushed per canonical hex — the draw_height/draw_smooth analogue of
	// _applied.
	private readonly Dictionary<(int Q, int R), (int Height, bool Smooth)> _applied = new();
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
	// of canonical chunks placed at non-zero lap offsets around the camera. The pool grows
	// on demand to cover every candidate inside the camera frustum; the cap only guards
	// against pathological growth.
	private const int ApronPoolLimit = 64;
	private const float ApronCameraMoveThreshold = 16f;
	// Conservative bounding-sphere radius of one chunk mirror for the coverage test: the
	// hex disk spans ~208u and its tops reach the elevation ceiling, so 320u keeps the test
	// from dropping a chunk whose mesh pokes into the frustum.
	private const float ApronCullRadius = 320f;
	// Lap-offset candidates in lap-vector units: every (a, b) in {-1, 0, 1}² except (0, 0) —
	// the canonical chunk node itself supplies the unshifted copy.
	private static readonly Vector2I[] ApronLapOffsets =
	{
		new(1, 0), new(1, 1), new(0, 1), new(-1, 1),
		new(-1, 0), new(-1, -1), new(0, -1), new(1, -1),
	};

	private readonly List<MeshInstance3D> _aprons = new();
	private readonly List<(float DistSq, Vector3 Pos, MeshInstance3D Chunk, (Vector2I Chunk, int Offset) Key)> _apronCandidates = new();
	// Stable assignment: a candidate identity keeps its mirror across refreshes instead of
	// being reshuffled by the distance sort; released mirrors wait on the free list.
	private readonly Dictionary<(Vector2I Chunk, int Offset), MeshInstance3D> _apronByKey = new();
	private readonly List<MeshInstance3D> _freeAprons = new();
	private readonly HashSet<(Vector2I, int)> _apronKeep = new();
	private readonly List<(Vector2I, int)> _staleApronKeys = new();
	private Node3D? _apronsRoot;
	private Vector3 _lastApronCameraPos;
	private float _lastApronCameraSize = -1f;
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
		_applied.Clear(); // row cache _cells survives; every rebuilt chunk re-pushes
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

		// Flat-topped cells with vertical walls: merge_threshold 0 disables the addon's
		// 45-degree ramp branch, so no small height difference gets a slant skirt. Set
		// merge_mode first — its setter resets merge_threshold to the enum default.
		foreach (var chunk in _chunkNodes.Values)
		{
			chunk.Set("merge_mode", MergeModeCubic);
			chunk.Set("merge_threshold", MergeRampThreshold);
		}

		// add_new_chunk marks chunks data-dirty, which makes the terrain's deferred enter-tree
		// skip its own force_batch_update — apply the texture shader params ourselves.
		_terrain.Call("force_batch_update");

		_chunksBuilt = true;
		_elevationDirty = true; // full sync: _applied is empty, so every cell differs
	}

	private void OnBuildingTileRowInserted() => SetCell((BuildingTile)_buildingTilesBinder.LastRow!);

	private void OnBuildingTileRowUpdated() => SetCell((BuildingTile)_buildingTilesBinder.LastRow!);

	private void OnBuildingTileRowDeleted()
	{
		var tile = (BuildingTile)_buildingTilesBinder.LastDeletedRow!;
		if (_cells.Remove((tile.HexQ, tile.HexR)))
			_elevationDirty = true;
	}

	private void SetCell(BuildingTile tile)
	{
		_cells[(tile.HexQ, tile.HexR)] = (tile.Height, tile.Smooth);
		_elevationDirty = true;
	}

	public override void _Process(double _delta)
	{
		if (_elevationDirty && _chunksBuilt)
		{
			_elevationDirty = false;
			SyncTerrain();
		}

		if (GetViewport()?.GetCamera3D() is { } camera
			&& (!_apronsPlaced
				|| _lastApronCameraPos.DistanceSquaredTo(camera.GlobalPosition) > ApronCameraMoveThreshold * ApronCameraMoveThreshold
				|| Mathf.Abs(_lastApronCameraSize - camera.Size) > 0.01f))
			RefreshAprons();
	}

	// Pushes the cached per-hex heights and smooth flags into every chunk in one pass. Only
	// cells whose height or smooth flag changed since the last sync are written
	// (draw_height/draw_smooth), each written cell dirties its chunk plus the axial
	// chunk-neighbours (their walls read this chunk's cells, and their smooth corner heights
	// average the cells around a corner), and every dirty chunk regenerates its mesh once.
	private void SyncTerrain()
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
					// Hexes outside the AOI have no row yet: flat 0, no smooth (same as before).
					var cell = _cells.TryGetValue((global.X, global.Y), out var cached) ? cached : default;
					if (_applied.TryGetValue((global.X, global.Y), out var prev) && prev == cell) continue;
					_applied[(global.X, global.Y)] = cell;
					chunk.Call("draw_height", lx, lz, (float)cell.Height);
					chunk.Call("draw_smooth", lx, lz, cell.Smooth);

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
	// Shared unshaded soil material for the shells (created on first use).
	private StandardMaterial3D ShellMaterial => _shellMaterial ??= new StandardMaterial3D
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		AlbedoColor = new Color(0.15f, 0.13f, 0.09f),
	};

	private void RefreshShells()
	{
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
			shell.MaterialOverride = ShellMaterial;
		}
	}

	// The canonical MST lap only contains the cols × rows server chunks, while the local
	// player's node can drift up to a lap past it before the server wraps the stored position.
	// This pool extends the rendered lap around the camera: for every canonical chunk, each of
	// the 8 non-zero lap offsets (a*lapQ + b*lapR) is a candidate mirror position, and every
	// candidate whose chunk bounds intersect the camera frustum is mirrored. The earlier
	// nearest-12-by-distance selection let visible images just outside the pool pop in and out
	// as the camera orbited (the flickering-chunks bug), because some nearer candidates sat
	// behind the camera and consumed slots; frustum coverage makes every visible image render.
	// Mirrors share the canonical chunk's Mesh (re-assigned on every refresh, so
	// regenerate_mesh is picked up for free) and each carries an UnderShell copy like
	// RefreshShells. They are visual-only: no collision, no gameplay queries, and they live
	// under a ChunkAprons child of this component, never in terrain.chunks. Ring chunks are
	// the server's low-res chunk hexes, so a chunk mesh translated by a lap vector is exactly
	// the terrain at that periodic position. The canonical chunk node itself is the unshifted
	// (0, 0) copy.
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
		var frustum = camera.GetFrustum();

		_apronCandidates.Clear();
		foreach (var (chunkCoords, chunk) in _chunkNodes)
		{
			var chunkPos = chunk.GlobalPosition;
			var chunkMesh = (MeshInstance3D)chunk;
			for (int i = 0; i < ApronLapOffsets.Length; i++)
			{
				var offset = ApronLapOffsets[i];
				var pos = chunkPos + lapQ3 * offset.X + lapR3 * offset.Y;
				if (!InFrustum(frustum, pos)) continue;
				_apronCandidates.Add((cameraPos.DistanceSquaredTo(pos), pos, chunkMesh, (chunkCoords, i)));
			}
		}
		_apronCandidates.Sort(static (a, b) => a.DistSq.CompareTo(b.DistSq));

		_apronKeep.Clear();
		int count = Mathf.Min(_apronCandidates.Count, ApronPoolLimit);
		for (int i = 0; i < count; i++)
		{
			var candidate = _apronCandidates[i];
			var apron = AcquireApron(candidate.Key);
			if (apron is null) continue;
			apron.Visible = true;
			apron.GlobalPosition = candidate.Pos;
			if (apron.Mesh != candidate.Chunk.Mesh) apron.Mesh = candidate.Chunk.Mesh;
			apron.CastShadow = candidate.Chunk.CastShadow;
			var shell = apron.GetNode<MeshInstance3D>("UnderShell");
			if (shell.Mesh != candidate.Chunk.Mesh) shell.Mesh = candidate.Chunk.Mesh;
			_apronKeep.Add(candidate.Key);
		}

		// Release assignments that left the frustum (or the cap): hide and return to the pool.
		_staleApronKeys.Clear();
		foreach (var key in _apronByKey.Keys)
			if (!_apronKeep.Contains(key)) _staleApronKeys.Add(key);
		foreach (var key in _staleApronKeys)
		{
			var apron = _apronByKey[key];
			apron.Visible = false;
			_apronByKey.Remove(key);
			_freeAprons.Add(apron);
		}

		_lastApronCameraPos = cameraPos;
		_lastApronCameraSize = camera.Size;
		_apronsPlaced = true;
	}

	// Bounding-sphere coverage test against the camera frustum. Godot's frustum planes have
	// outward normals (positive signed distance = outside), so a candidate is culled only
	// when its sphere is fully outside a plane; Godot's own per-instance culling still
	// applies to whatever the pool actually places.
	private static bool InFrustum(Godot.Collections.Array<Plane> frustum, Vector3 pos)
	{
		foreach (var plane in frustum)
			if (plane.DistanceTo(pos) > ApronCullRadius) return false;
		return true;
	}

	// The mirror already assigned to `key` if it is still valid, else a free one, else a newly
	// created one (up to the pool limit). Null when the pool is exhausted.
	private MeshInstance3D? AcquireApron((Vector2I Chunk, int Offset) key)
	{
		if (_apronByKey.TryGetValue(key, out var existing) && GodotObject.IsInstanceValid(existing))
			return existing;

		MeshInstance3D? apron = null;
		if (_freeAprons.Count > 0)
		{
			apron = _freeAprons[^1];
			_freeAprons.RemoveAt(_freeAprons.Count - 1);
		}
		else if (_aprons.Count < ApronPoolLimit)
		{
			apron = CreateApron();
		}
		if (apron is null) return null;
		_apronByKey[key] = apron;
		return apron;
	}

	// Creates one mirror: a plain MeshInstance3D under the apron root (a plain Node3D child of
	// this component, NOT the MST node, so mirrors can never enter terrain.chunks or the chunk
	// save/registration paths), holding an UnderShell child exactly like RefreshShells.
	private MeshInstance3D CreateApron()
	{
		if (_apronsRoot is null)
		{
			_apronsRoot = new Node3D { Name = "ChunkAprons" };
			AddChild(_apronsRoot);
		}
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
			MaterialOverride = ShellMaterial,
		});
		_apronsRoot.AddChild(apron);
		_aprons.Add(apron);
		return apron;
	}

	// Hides every pool mirror (used when wrapping is off, lap vectors are unavailable, or the
	// grid is being rebuilt) and resets the stable assignments; the pool itself is reused.
	private void HideAprons()
	{
		foreach (var apron in _aprons) apron.Visible = false;
		_apronByKey.Clear();
		_freeAprons.Clear();
		_freeAprons.AddRange(_aprons);
	}

	// Positive modulo for wrapping, matching server wrap_chunk_coords / the addon's wrap_chunk.
	private static int PosMod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
