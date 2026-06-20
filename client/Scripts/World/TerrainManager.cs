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

	private readonly Dictionary<(int TriIndex, int Rotation, string TextureId), ArrayMesh> _meshCache = new();
	private readonly Dictionary<(int TriIndex, int Rotation, string TextureId), MultiMeshInstance2D> _batches = new();
	private readonly HashSet<(int TriIndex, int Rotation, string TextureId)> _activeBatchKeys = new();

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
			}
		}

		foreach (var (key, positions) in grouped)
		{
			var multimesh = GetOrCreateBatch(key.TriIndex, key.Rotation, key.TextureId).Multimesh;
			multimesh.InstanceCount = positions.Count;
			for (var i = 0; i < positions.Count; i++)
				multimesh.SetInstanceTransform2D(i, new Transform2D(0f, positions[i]));
			_activeBatchKeys.Add(key);
		}

		// Zero out batches that had instances last rebuild but none this time, so they stop
		// drawing stale positions instead of being destroyed and recreated every pass.
		_activeBatchKeys.RemoveWhere(key =>
		{
			if (grouped.ContainsKey(key)) return false;
			_batches[key].Multimesh.InstanceCount = 0;
			return true;
		});
	}

	private MultiMeshInstance2D GetOrCreateBatch(int triIndex, int rotation, string textureId)
	{
		var key = (triIndex, rotation, textureId);
		if (_batches.TryGetValue(key, out var existing))
			return existing;

		var (baseTexture, uvRegion) = ResolveTexture(GetTexture(textureId));
		var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, Mesh = GetOrBuildMesh(triIndex, rotation, textureId, uvRegion) };
		var batch = new MultiMeshInstance2D { Multimesh = multimesh };

		if (baseTexture != null)
			batch.Texture = baseTexture;
		else
			batch.Modulate = FallbackColor;

		AddChild(batch);
		_batches[key] = batch;
		return batch;
	}

	private ArrayMesh GetOrBuildMesh(int triIndex, int rotation, string textureId, Rect2 uvRegion)
	{
		var key = (triIndex, rotation, textureId);
		if (_meshCache.TryGetValue(key, out var cached))
			return cached;

		float angleA = Mathf.DegToRad(30f + 60f * triIndex);
		float angleB = Mathf.DegToRad(30f + 60f * ((triIndex + 1) % 6));
		var vA = _outerRadius * new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA));
		var vB = _outerRadius * new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB));

		// Square textures don't tile naturally onto a triangular wedge — sample the texture as
		// if it covered the whole hex, so each wedge shows the slice geometrically under it.
		// `rotation` (0/1/2 = 0/120/240 deg) spins that sampling to vary the look from one texture.
		float rotOffset = Mathf.DegToRad(120f * rotation);
		Vector2 LocalUvFor(float angle) => new Vector2(0.5f, 0.5f) + 0.5f * new Vector2(Mathf.Cos(angle - rotOffset), Mathf.Sin(angle - rotOffset));
		// AtlasTexture's region crop isn't honored by MultiMeshInstance2D's raw mesh UVs (only
		// Sprite2D/draw_polygon-style paths remap for it), so we bake the crop into the UVs
		// ourselves and bind the underlying sheet instead of the AtlasTexture wrapper.
		Vector2 RemapUv(Vector2 local) => uvRegion.Position + local * uvRegion.Size;

		var vertices = new[] { Vector3.Zero, new Vector3(vA.X, vA.Y, 0f), new Vector3(vB.X, vB.Y, 0f) };
		var uvs = new[] { RemapUv(new Vector2(0.5f, 0.5f)), RemapUv(LocalUvFor(angleA)), RemapUv(LocalUvFor(angleB)) };

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
