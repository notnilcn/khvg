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
/// A second replaying binder consumes the same NearbyBuildingTiles rows for their 7
/// world-unit height samples [center, c0..c5] and pushes them via chunk.draw_hex_heights
/// (empty payload = the fork's flat fallback); elevation levels keep going to
/// draw_elevation in the same sync pass because walls/levels still need them.
///
/// The terrain's hex_size/level_height/chunk_radius are storage-only properties whose
/// setters regen existing chunks (a no-op while empty), so they are set before any chunk
/// is added. add_new_chunk's plugin arg is null-safe (add_chunk guards on it). Chunks
/// are marked data-dirty on add, which makes the terrain's _deferred_enter_tree skip its
/// force_batch_update (the texture shader params) — so we call force_batch_update
/// ourselves after building the grid.
///
/// Known limitations:
/// - Only the canonical lap (0..cols × 0..rows chunks) is built. Seam reads are wrap-aware
///   (wrap_chunk_cols/rows from MapConfig) and the ChunkAprons pool mirrors the lap around
///   the camera, but mirrors are visual-only — gameplay stays in canonical space.
/// - Elevation rows are AOI-filtered (NearbyBuildingTiles, 2-chunk ring): distant chunks
///   render flat at level 0 until the player approaches — same data scope as the 2D
///   terrain.
/// - Server HexTile ground/overlay textures are a per-hex 2D sprite system with
///   no 1:1 mapping onto MST texture slots; the terrain uses the plugin's default
///   ground texture for now.
/// - River/road edge masks (HexTile.river_mask/road_mask) are not drawn here — the sibling
///   TerrainRoadsComponent3D renders them as flat strips; wall_mask is always 0 today.
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
	private TableBinderComponent _buildingTilesBinder = null!;

	private readonly Dictionary<Vector2I, Node3D> _chunkNodes = new();
	private readonly Dictionary<(int Q, int R), int> _applied = new();
	// Server height payloads by canonical hex: 7 world-unit Y samples [center, c0..c5] in
	// MST corner order. Empty/absent payloads are simply missing keys (fork falls back flat).
	private readonly Dictionary<(int Q, int R), int[]> _heights = new();
	// Last payload pushed per hex — the draw_hex_heights analogue of _applied.
	private readonly Dictionary<(int Q, int R), int[]> _appliedHeights = new();
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

		// Apron mirrors reference chunk meshes; drop them before the old chunks are freed.
		HideAprons();
		_apronsPlaced = false;

		// Clear a previous grid (config changed mid-session).
		foreach (var chunk in _chunkNodes.Values) chunk.QueueFree();
		_chunkNodes.Clear();
		_applied.Clear();
		_appliedHeights.Clear(); // row cache _heights survives; every rebuilt chunk re-pushes
		_terrain.Set("chunks", new Godot.Collections.Dictionary());

		// Global settings before any chunk exists (the setters regen all chunks; empty = no-op).
		_terrain.Set("hex_size", cfg.HexOuterRadius);
		_terrain.Set("level_height", ElevationMapComponent.WorldHeightPerLevel);
		_terrain.Set("chunk_radius", cfg.ChunkHexRadius);
		// Torus lap sizes: seam hex lookups canonicalize into the lap (server wrap semantics).
		_terrain.Set("wrap_chunk_cols", cfg.ChunkCols);
		_terrain.Set("wrap_chunk_rows", cfg.ChunkRows);

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

	private void OnBuildingTileRowInserted() => SetHeights((BuildingTile)_buildingTilesBinder.LastRow!);

	private void OnBuildingTileRowUpdated() => SetHeights((BuildingTile)_buildingTilesBinder.LastRow!);

	private void OnBuildingTileRowDeleted()
	{
		var tile = (BuildingTile)_buildingTilesBinder.LastDeletedRow!;
		if (_heights.Remove((tile.HexQ, tile.HexR)))
			_elevationDirty = true;
	}

	// Copies a row's 7 world-unit height samples (center + 6 corners, MST corner order) into
	// the cache; a payload of any other length is treated as absent (fork falls back flat).
	private void SetHeights(BuildingTile tile)
	{
		if (tile.Heights is { Count: 7 })
		{
			var samples = new int[7];
			for (int i = 0; i < 7; i++) samples[i] = tile.Heights[i];
			_heights[(tile.HexQ, tile.HexR)] = samples;
		}
		else
		{
			_heights.Remove((tile.HexQ, tile.HexR));
		}
		_elevationDirty = true;
	}

	public override void _Process(double _delta)
	{
		if (_elevationDirty && _chunksBuilt)
		{
			_elevationDirty = false;
			SyncElevation();
		}

		if (GetViewport()?.GetCamera3D() is { } camera
			&& (!_apronsPlaced || _lastApronCameraPos.DistanceSquaredTo(camera.GlobalPosition) > ApronCameraMoveThreshold * ApronCameraMoveThreshold))
			RefreshAprons();
	}

	// Pushes the ElevationMap's per-hex levels and the cached height payloads into every
	// chunk in one pass. Only hexes whose level or 7 samples changed since the last sync
	// are written — levels to draw_elevation (walls/levels stay stepped), samples to
	// draw_hex_heights (the smooth ridge tops) — and each written hex dirties its chunk
	// plus the in-grid axial chunk-neighbors (border walls read neighbor hex_data), and
	// every dirty chunk regenerates its mesh once.
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
					bool changed = false;

					int level = map?.GetElevation(gq, gr) ?? 0; // 0 outside the AOI
					if (!_applied.TryGetValue((gq, gr), out int prev) || prev != level)
					{
						_applied[(gq, gr)] = level;
						chunk.Call("draw_elevation", new Vector2I(dq, dr), level);
						changed = true;
					}

					// A 7-value int[] marshals to the fork's PackedInt32Array; an empty array
					// clears a hex that lost its payload back to the flat fallback, while a hex
					// that never had one is skipped entirely.
					_heights.TryGetValue((gq, gr), out var heights);
					_appliedHeights.TryGetValue((gq, gr), out var appliedHeights);
					if (!HeightsEqual(appliedHeights, heights))
					{
						if (heights is null)
							_appliedHeights.Remove((gq, gr));
						else
							_appliedHeights[(gq, gr)] = (int[])heights.Clone();
						chunk.Call("draw_hex_heights", new Vector2I(dq, dr), heights ?? Array.Empty<int>());
						changed = true;
					}

					if (!changed) continue;
					dirtyChunks.Add(chunkCoords);
					foreach (var offset in ChunkNeighborOffsets)
					{
						var neighborCoords = chunkCoords + offset;
						// A seam-adjacent hex change must dirty the wrapped partner chunk too:
						// its border wall reads this hex through the wrapped get_hex_global.
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

	// Element-wise comparison of two 7-sample payloads; both null is equal, one null is not.
	private static bool HeightsEqual(int[]? a, int[]? b)
	{
		if (ReferenceEquals(a, b)) return true;
		if (a is null || b is null) return false;
		return a.AsSpan().SequenceEqual(b.AsSpan());
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

	// The canonical MST lap only contains 0..cols × 0..rows chunks, while the local player's
	// node can drift up to a lap past it before the server wraps the stored position. This
	// pool extends the rendered lap around the camera: for every canonical chunk, each of the
	// 8 non-zero lap offsets (a*lapQ + b*lapR) is a candidate mirror position, and the pool's
	// nearest N candidates win. Mirrors share the canonical chunk's Mesh (re-assigned on every
	// refresh, so regenerate_mesh is picked up for free) and each carries an UnderShell copy
	// like RefreshShells. They are visual-only: no collision, no gameplay queries, and they
	// live under a ChunkAprons child of this component, never in terrain.chunks. The canonical
	// chunk node itself is the unshifted (0, 0) copy.
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

	// Positive modulo for wrapping, matching server wrap_chunk_coords / MSTHexMath.wrap_chunk.
	private static int PosMod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
