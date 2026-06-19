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

	private Vector2 _camPos = Vector2.Zero;
	private Vector2 _camZoom = Vector2.One;

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
		if (cam.GlobalPosition != _camPos || cam.Zoom != _camZoom)
		{
			_camPos = cam.GlobalPosition;
			_camZoom = cam.Zoom;
			QueueRedraw();
		}
	}

	private void TrySubscribe()
	{
		var conn = GameManager.Instance?.Conn;
		if (conn == null) return;

		conn.Db.MapConfig.OnInsert += (_, row) => { _outerRadius = row.HexOuterRadius; QueueRedraw(); };
		conn.Db.MapConfig.OnUpdate += (_, _, row) => { _outerRadius = row.HexOuterRadius; QueueRedraw(); };
		var cfg = conn.Db.MapConfig.Id.Find(0u);
		if (cfg != null)
			_outerRadius = cfg.HexOuterRadius;

		conn.Db.NearbyTerrainTiles.OnInsert += OnTileInsert;
		conn.Db.NearbyTerrainTiles.OnUpdate += OnTileUpdate;
		conn.Db.NearbyTerrainTiles.OnDelete += OnTileDelete;

		foreach (var tile in conn.Db.NearbyTerrainTiles.Iter())
			AddTile(tile);

		_subscribed = true;
		QueueRedraw();
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
		QueueRedraw();
	}

	private void OnTileUpdate(EventContext _, TriangleTile old, TriangleTile tile)
	{
		RemoveTile(old.TileId);
		AddTile(tile);
		QueueRedraw();
	}

	private void OnTileDelete(EventContext _, TriangleTile tile)
	{
		RemoveTile(tile.TileId);
		QueueRedraw();
	}

	public override void _Draw()
	{
		var cam = GetViewport().GetCamera2D();
		if (cam == null) return;

		var half = GetViewportRect().Size / cam.Zoom;
		var worldMin = _camPos - half;
		var worldMax = _camPos + half;
		var margin = _outerRadius * 2f;

		var lapQ = GameManager.Instance?.LapQ ?? Vector2.Zero;
		var lapR = GameManager.Instance?.LapR ?? Vector2.Zero;

		// Iterate everything cached (not a camera-window hex lookup) since chunks near a torus
		// seam are stored at their canonical (raw, far-away) position — we need to pick whichever
		// wrapped copy of each hex is actually near the camera before we can cull/draw it.
		foreach (var ((q, r), tiles) in _tilesByHex)
		{
			var canonicalCenter = HexToWorld(q, r);
			var center = TorusMath.NearestCandidate(canonicalCenter, _camPos, lapQ, lapR);
			if (center.X < worldMin.X - margin || center.X > worldMax.X + margin ||
				center.Y < worldMin.Y - margin || center.Y > worldMax.Y + margin)
				continue;

			foreach (var tile in tiles)
				DrawTriangle(tile, center);
		}
	}

	private void DrawTriangle(TriangleTile tile, Vector2 center)
	{
		float angleA = Mathf.DegToRad(30f + 60f * tile.TriIndex);
		float angleB = Mathf.DegToRad(30f + 60f * ((tile.TriIndex + 1) % 6));
		var vA = center + _outerRadius * new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA));
		var vB = center + _outerRadius * new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB));
		Vector2[] points = [center, vA, vB];

		var texture = GetTexture(tile.TextureId);
		if (texture == null)
		{
			DrawPolygon(points, [FallbackColor]);
			return;
		}

		// Square textures don't tile naturally onto a triangular wedge — sample the texture as
		// if it covered the whole hex, so each wedge shows the slice geometrically under it.
		// `rotation` (0/1/2 = 0/120/240 deg) spins that sampling to vary the look from one texture.
		float rotOffset = Mathf.DegToRad(120f * tile.Rotation);
		Vector2 UvFor(float angle) => new Vector2(0.5f, 0.5f) + 0.5f * new Vector2(Mathf.Cos(angle - rotOffset), Mathf.Sin(angle - rotOffset));
		Vector2[] uvs = [new Vector2(0.5f, 0.5f), UvFor(angleA), UvFor(angleB)];
		Color[] white = [Colors.White];

		DrawPolygon(points, white, uvs, texture);
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
