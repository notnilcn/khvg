#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

public partial class TerrainManager : Node2D
{
	private const float Sqrt3 = 1.7320508075688772f;
	private const float DefaultHexOuterRadius = 96.0f;
	private static readonly Color FallbackColor = new(0.2f, 0.5f, 0.2f, 1f);

	private float _outerRadius = DefaultHexOuterRadius;
	private bool _subscribed;

	private readonly Dictionary<ulong, TriangleTile> _tilesById = new();
	private readonly Dictionary<(int Q, int R), List<TriangleTile>> _tilesByHex = new();
	private readonly Dictionary<string, Texture2D> _textureCache = new();

	// Mesh geometry only depends on (TriIndex, Rotation, TextureId), never on which layer it's
	// drawn in, so base and overlay batches share this cache. Batches themselves can't be shared
	// — overlay needs a higher ZIndex and per-instance scale, base doesn't.
	private readonly Dictionary<(int TriIndex, int Rotation, string TextureId), ArrayMesh> _meshCache = new();
	private readonly Dictionary<(int TriIndex, int Rotation, string TextureId), MultiMeshInstance2D> _batches = new();
	private readonly HashSet<(int TriIndex, int Rotation, string TextureId)> _activeBatchKeys = new();
	private readonly Dictionary<(int TriIndex, int Rotation, string TextureId), MultiMeshInstance2D> _overlayBatches = new();
	private readonly HashSet<(int TriIndex, int Rotation, string TextureId)> _activeOverlayBatchKeys = new();

	private Vector2 _camPos = Vector2.Zero;
	private Vector2 _camZoom = Vector2.One;
	private bool _dirty;

	public override void _Ready()
	{
		ZIndex = -10;
	}

	public override void _Process(double delta)
	{
		if (!_subscribed)
			TrySubscribe();

		var cam = GetViewport().GetCamera2D();
		if (cam == null) return;

		var camMoved = cam.GlobalPosition != _camPos || cam.Zoom != _camZoom;
		if (camMoved)
		{
			_camPos = cam.GlobalPosition;
			_camZoom = cam.Zoom;
		}

		// Tile-change callbacks only set _dirty — actually rebuilding here means a whole burst
		// of inserts/deletes from one chunk crossing collapses into a single rebuild per frame,
		// instead of one rebuild per individual tile event.
		if (camMoved || _dirty)
		{
			_dirty = false;
			RebuildVisibleInstances();
		}
	}

	private void TrySubscribe()
	{
		var conn = GameManager.Instance?.Conn;
		if (conn == null) return;

		conn.Db.MapConfig.OnInsert += (_, row) => { _outerRadius = row.HexOuterRadius; InvalidateMeshes(); };
		conn.Db.MapConfig.OnUpdate += (_, _, row) => { _outerRadius = row.HexOuterRadius; InvalidateMeshes(); };
		var cfg = conn.Db.MapConfig.Id.Find(0u);
		if (cfg != null)
			_outerRadius = cfg.HexOuterRadius;

		conn.Db.NearbyTerrainTiles.OnInsert += OnTileInsert;
		conn.Db.NearbyTerrainTiles.OnUpdate += OnTileUpdate;
		conn.Db.NearbyTerrainTiles.OnDelete += OnTileDelete;

		foreach (var tile in conn.Db.NearbyTerrainTiles.Iter())
			AddTile(tile);

		_subscribed = true;
		RebuildVisibleInstances();
	}

	private void AddTile(TriangleTile tile)
	{
		_tilesById[tile.TileId] = tile;
		var key = (tile.HexQ, tile.HexR);
		if (!_tilesByHex.TryGetValue(key, out var list))
			_tilesByHex[key] = list = new List<TriangleTile>();
		list.Add(tile);
	}

	private void RemoveTile(ulong tileId)
	{
		if (!_tilesById.TryGetValue(tileId, out var tile)) return;
		_tilesById.Remove(tileId);
		var key = (tile.HexQ, tile.HexR);
		if (!_tilesByHex.TryGetValue(key, out var list)) return;
		list.RemoveAll(t => t.TileId == tileId);
		if (list.Count == 0)
			_tilesByHex.Remove(key);
	}

	private void OnTileInsert(EventContext _, TriangleTile tile)
	{
		AddTile(tile);
		_dirty = true;
	}

	private void OnTileUpdate(EventContext _, TriangleTile old, TriangleTile tile)
	{
		RemoveTile(old.TileId);
		AddTile(tile);
		_dirty = true;
	}

	private void OnTileDelete(EventContext _, TriangleTile tile)
	{
		RemoveTile(tile.TileId);
		_dirty = true;
	}

	// Outer radius only changes in dev — wipe cached meshes/batches so the next rebuild
	// recreates everything at the new scale instead of patching stale Multimesh state.
	private void InvalidateMeshes()
	{
		_meshCache.Clear();
		foreach (var batch in _batches.Values)
			batch.QueueFree();
		_batches.Clear();
		_activeBatchKeys.Clear();
		foreach (var batch in _overlayBatches.Values)
			batch.QueueFree();
		_overlayBatches.Clear();
		_activeOverlayBatchKeys.Clear();
		RebuildVisibleInstances();
	}

	private void RebuildVisibleInstances()
	{
		var cam = GetViewport().GetCamera2D();
		if (cam == null) return;

		var half = GetViewportRect().Size / cam.Zoom;
		var worldMin = _camPos - half;
		var worldMax = _camPos + half;
		var margin = _outerRadius * 2f;

		var lapQ = GameManager.Instance?.LapQ ?? Vector2.Zero;
		var lapR = GameManager.Instance?.LapR ?? Vector2.Zero;

		var grouped = new Dictionary<(int TriIndex, int Rotation, string TextureId), List<Vector2>>();
		var overlayGrouped = new Dictionary<(int TriIndex, int Rotation, string TextureId), List<(Vector2 Position, float Scale)>>();

		// Iterate everything cached (not a camera-window hex lookup) since chunks near a torus
		// seam are stored at their canonical (raw, far-away) position — we need to pick whichever
		// wrapped copy of each hex is actually near the camera before we can cull it.
		foreach (var ((q, r), tiles) in _tilesByHex)
		{
			var canonicalCenter = HexToWorld(q, r);
			var center = TorusMath.NearestCandidate(canonicalCenter, _camPos, lapQ, lapR);
			if (center.X < worldMin.X - margin || center.X > worldMax.X + margin ||
				center.Y < worldMin.Y - margin || center.Y > worldMax.Y + margin)
				continue;

			foreach (var tile in tiles)
			{
				var key = (tile.TriIndex, tile.Rotation, tile.TextureId);
				if (!grouped.TryGetValue(key, out var positions))
					grouped[key] = positions = new List<Vector2>();
				positions.Add(center);

				if (string.IsNullOrEmpty(tile.OverlayTextureId)) continue;
				var overlayKey = (tile.TriIndex, tile.OverlayRotation, tile.OverlayTextureId);
				if (!overlayGrouped.TryGetValue(overlayKey, out var overlayPositions))
					overlayGrouped[overlayKey] = overlayPositions = new List<(Vector2, float)>();
				overlayPositions.Add((center, tile.OverlayScale));
			}
		}

		foreach (var (key, positions) in grouped)
		{
			var multimesh = GetOrCreateBatch(_batches, key.TriIndex, key.Rotation, key.TextureId, zIndex: 0).Multimesh;
			multimesh.InstanceCount = positions.Count;
			for (var i = 0; i < positions.Count; i++)
				multimesh.SetInstanceTransform2D(i, new Transform2D(0f, positions[i]));
			_activeBatchKeys.Add(key);
		}

		// Overlays scale around their wedge's own centroid (not the hex-center local origin the
		// mesh is built around), so a smaller overlay stays visually centered in its triangle
		// instead of shrinking toward one corner.
		foreach (var (key, positions) in overlayGrouped)
		{
			var multimesh = GetOrCreateBatch(_overlayBatches, key.TriIndex, key.Rotation, key.TextureId, zIndex: 1).Multimesh;
			multimesh.InstanceCount = positions.Count;
			var centroid = GetWedgeCentroid(key.TriIndex);
			for (var i = 0; i < positions.Count; i++)
			{
				var (position, scale) = positions[i];
				multimesh.SetInstanceTransform2D(i, new Transform2D(0f, new Vector2(scale, scale), 0f, position - scale * centroid));
			}
			_activeOverlayBatchKeys.Add(key);
		}

		// Zero out batches that had instances last rebuild but none this time, so they stop
		// drawing stale positions instead of being destroyed and recreated every pass.
		_activeBatchKeys.RemoveWhere(key =>
		{
			if (grouped.ContainsKey(key)) return false;
			_batches[key].Multimesh.InstanceCount = 0;
			return true;
		});
		_activeOverlayBatchKeys.RemoveWhere(key =>
		{
			if (overlayGrouped.ContainsKey(key)) return false;
			_overlayBatches[key].Multimesh.InstanceCount = 0;
			return true;
		});
	}

	private MultiMeshInstance2D GetOrCreateBatch(Dictionary<(int TriIndex, int Rotation, string TextureId), MultiMeshInstance2D> batches, int triIndex, int rotation, string textureId, int zIndex)
	{
		var key = (triIndex, rotation, textureId);
		if (batches.TryGetValue(key, out var existing))
			return existing;

		var (baseTexture, uvRegion) = ResolveTexture(GetTexture(textureId));
		var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, Mesh = GetOrBuildMesh(triIndex, rotation, textureId, uvRegion) };
		var batch = new MultiMeshInstance2D { Multimesh = multimesh, ZIndex = zIndex };

		if (baseTexture != null)
			batch.Texture = baseTexture;
		else
			batch.Modulate = FallbackColor;

		AddChild(batch);
		batches[key] = batch;
		return batch;
	}

	// The mesh's local origin (Vector2.Zero) is the hex center, not the wedge's own centroid —
	// fine for the base layer (always full scale), but scaling an overlay around the hex center
	// would shrink it toward one corner instead of staying centered in its own triangle.
	private (Vector2 A, Vector2 B) GetWedgeVertices(int triIndex)
	{
		float angleA = Mathf.DegToRad(30f + 60f * triIndex);
		float angleB = Mathf.DegToRad(30f + 60f * ((triIndex + 1) % 6));
		return (_outerRadius * new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA)),
			_outerRadius * new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB)));
	}

	private Vector2 GetWedgeCentroid(int triIndex)
	{
		var (vA, vB) = GetWedgeVertices(triIndex);
		return (vA + vB) / 3f; // third vertex (hex center) is the local origin, Vector2.Zero
	}

	private ArrayMesh GetOrBuildMesh(int triIndex, int rotation, string textureId, Rect2 uvRegion)
	{
		var key = (triIndex, rotation, textureId);
		if (_meshCache.TryGetValue(key, out var cached))
			return cached;

		var (vA, vB) = GetWedgeVertices(triIndex);
		var localVerts = new[] { Vector2.Zero, vA, vB };

		// Fit the texture to THIS wedge's own bounding box, not the whole hex, so the wedge crops
		// into its own full-image fit instead of a thin pie-slice shared across all 6 wedges. A
		// triangle only has 3 vertices to a square's 4 corners, so at most half the image is ever
		// reachable this way (the other diagonal half just isn't addressable by 3 UV points) — but
		// that's still far more than the old hex-wide slice. Trade-off: a whole hex now shows this
		// same crop repeated/rotated 6 times instead of one continuous image — intentional, per
		// Teo's call to prioritize more-texture-per-wedge over hex-wide continuity.
		var boxMin = new Vector2(Mathf.Min(localVerts[0].X, Mathf.Min(localVerts[1].X, localVerts[2].X)), Mathf.Min(localVerts[0].Y, Mathf.Min(localVerts[1].Y, localVerts[2].Y)));
		var boxMax = new Vector2(Mathf.Max(localVerts[0].X, Mathf.Max(localVerts[1].X, localVerts[2].X)), Mathf.Max(localVerts[0].Y, Mathf.Max(localVerts[1].Y, localVerts[2].Y)));
		var boxCenter = (boxMin + boxMax) * 0.5f;
		var boxHalfSize = (boxMax - boxMin) * 0.5f;

		// `rotation` (0/1/2 = 0/120/240 deg) spins which portion of the texture lands in the
		// wedge, to vary the look from one shared texture instead of always showing the same crop.
		float rotOffset = Mathf.DegToRad(120f * rotation);
		Vector2 UvFor(Vector2 point)
		{
			var boxRelative = (point - boxCenter) / boxHalfSize; // -1..1 within the wedge's box
			var rotated = boxRelative.Rotated(rotOffset);
			return new Vector2(0.5f, 0.5f) + 0.5f * rotated;
		}
		// AtlasTexture's region crop isn't honored by MultiMeshInstance2D's raw mesh UVs (only
		// Sprite2D/draw_polygon-style paths remap for it), so we bake the crop into the UVs
		// ourselves and bind the underlying sheet instead of the AtlasTexture wrapper.
		Vector2 RemapUv(Vector2 local) => uvRegion.Position + local * uvRegion.Size;

		var vertices = new[] { Vector3.Zero, new Vector3(vA.X, vA.Y, 0f), new Vector3(vB.X, vB.Y, 0f) };
		var uvs = new[] { RemapUv(UvFor(localVerts[0])), RemapUv(UvFor(localVerts[1])), RemapUv(UvFor(localVerts[2])) };

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_meshCache[key] = mesh;
		return mesh;
	}

	private Texture2D? GetTexture(string textureId)
	{
		if (string.IsNullOrEmpty(textureId)) return null;
		if (_textureCache.TryGetValue(textureId, out var cached)) return cached;
		var resPath = GameManager.Instance?.GetResPath(textureId);
		if (resPath == null) return null;
		var tex = GD.Load<Texture2D>(resPath);
		_textureCache[textureId] = tex;
		return tex;
	}

	private static (Texture2D? Base, Rect2 UvRegion) ResolveTexture(Texture2D? texture)
	{
		if (texture is AtlasTexture atlas && atlas.Atlas != null)
		{
			var size = atlas.Atlas.GetSize();
			var region = atlas.Region;
			return (atlas.Atlas, new Rect2(region.Position / size, region.Size / size));
		}
		return (texture, new Rect2(0f, 0f, 1f, 1f));
	}

	private Vector2 HexToWorld(int q, int r)
	{
		float R = _outerRadius;
		return new Vector2(R * (Sqrt3 * q + Sqrt3 * 0.5f * r), R * 1.5f * r);
	}

	public override void _ExitTree()
	{
		var conn = GameManager.Instance?.Conn;
		if (conn == null) return;
		conn.Db.NearbyTerrainTiles.OnInsert -= OnTileInsert;
		conn.Db.NearbyTerrainTiles.OnUpdate -= OnTileUpdate;
		conn.Db.NearbyTerrainTiles.OnDelete -= OnTileDelete;
	}
}
