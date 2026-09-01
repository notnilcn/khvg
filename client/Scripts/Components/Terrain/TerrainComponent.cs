#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// Terrain system, child of Main: renders NearbyTerrainTiles/NearbyHexDecor/MapConfig rows
/// through a pool of TileComponent scenes — one per visible chunk, pre-warmed to
/// the AOI ring count (AoiChunkRadius, matching the server's DEFAULT_TERRAIN_AOI_CHUNK_RADIUS).
/// Table rows arrive via three child TableBinderComponents (declared inline in game.tscn,
/// signals wired in the editor); row callbacks only set a dirty flag, and one rebuild per frame
/// collapses burst chunk crossings.
///
/// This component owns the shared caches (wedge/decor meshes, textures) and the per-hex
/// wrap-near-camera + cull math; the TileComponents own the actual MultiMesh batch leaves. Mesh
/// geometry only depends on (TriIndex, Rotation, TextureId), never on which chunk or layer draws
/// it, so one cache serves every chunk.
///
/// Performance note: batching per chunk duplicates a leaf when two chunks share the same
/// (TriIndex, Rotation, TextureId) key, but biome coherence keeps the key count per chunk small.
/// If that ever regresses against the old global batching, the fallback is one AOI-wide
/// TileComponent — Populate/Clear take whole grouped dictionaries and make no per-chunk
/// assumptions, so only the grouping loop below would change.
/// </summary>
public partial class TerrainComponent : Node2DComponent
{
    private const float DefaultHexOuterRadius = 96.0f;
    /// Mirrors the server's DEFAULT_CHUNK_HEX_RADIUS; the MapConfig row overrides it on arrival.
    private const int DefaultChunkHexRadius = 2;
    private static readonly Color FallbackColor = new(0.2f, 0.5f, 0.2f, 1f);

    /// AOI radius in chunk rings around the player, mirroring the server's
    /// DEFAULT_TERRAIN_AOI_CHUNK_RADIUS (= 2). Only used to pre-warm the tile pool; the pool
    /// grows on demand if more chunks are ever visible at once.
    [Export] public int AoiChunkRadius { get; set; } = 2;

    /// tile_component.tscn — the per-chunk scaffolding the pool instantiates.
    [Export] public PackedScene TileScene { get; set; } = null!;

    /// Child binders (declared inline in game.tscn) feeding MapConfig / tile / decor rows.
    private TableBinderComponent _mapConfigBinder = null!;
    private TableBinderComponent _terrainTilesBinder = null!;
    private TableBinderComponent _hexDecorBinder = null!;

    private float _outerRadius = DefaultHexOuterRadius;
    private int _chunkHexRadius = DefaultChunkHexRadius;

    private readonly Dictionary<ulong, TriangleTile> _tilesById = new();
    private readonly Dictionary<(int Q, int R), List<TriangleTile>> _tilesByHex = new();
    private readonly Dictionary<string, Texture2D> _textureCache = new();
    private readonly Dictionary<(int TriIndex, int Rotation, string TextureId), ArrayMesh> _meshCache = new();

    private readonly Dictionary<ulong, HexDecor> _decorById = new();
    // Decor is a plain rectangular sprite, not a UV-cropped wedge, so unlike _meshCache its mesh
    // only depends on which texture it is — rotation/position are per-instance transforms, not
    // separate UVs.
    private readonly Dictionary<string, ArrayMesh> _decorMeshCache = new();

    private readonly List<TileComponent> _tilePool = new();
    private readonly Dictionary<long, TileComponent> _activeTiles = new();

    // Rebuild grouping buffers, clear-and-reused across rebuilds: chunk keys, batch keys and
    // their lists stay allocated (bounded by the world's chunk count) and only their contents
    // reset, so a steady-state rebuild allocates nothing.
    private readonly Dictionary<long, Dictionary<(int TriIndex, int Rotation, string TextureId), List<(Vector2 Position, Color Tint)>>> _groundByChunk = new();
    private readonly Dictionary<long, Dictionary<(int TriIndex, int Rotation, string TextureId), List<(Vector2 Position, float Scale, Color Tint)>>> _overlayByChunk = new();
    private readonly Dictionary<long, Dictionary<string, List<(Vector2 Position, float RotationRadians, int HexR)>>> _shadowByChunk = new();
    private readonly Dictionary<long, Dictionary<string, List<(Vector2 Position, float RotationRadians, int HexR)>>> _decorByChunk = new();
    private readonly HashSet<long> _presentChunks = new();

    // Sentinel chunk coords force a rebuild on the first frame.
    private Vector2I _camChunk = new(int.MinValue, int.MinValue);
    private Vector2 _camPos = Vector2.Zero;
    private Vector2 _camZoom = Vector2.One;
    private bool _dirty;

    /// Hex ring count for radius r: 1 + 6·(1 + … + r).
    private static int RingChunkCount(int radius) => 1 + 3 * radius * (radius + 1);

    public override void _Ready()
    {
        base._Ready();
        _mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
        _terrainTilesBinder = GetNode<TableBinderComponent>("TerrainTilesBinder");
        _hexDecorBinder = GetNode<TableBinderComponent>("HexDecorBinder");

        // 3D mode renders no 2D terrain: stop the rebuild loop and never build the tile
        // pool, so no res_path2d ground/decor texture is ever loaded. Row handlers still
        // fire but only set the dirty flag, which nothing consumes.
        if (GameVisuals.Is3D)
        {
            Visible = false;
            SetProcess(false);
        }
    }

    protected override void OnEntityReady()
    {
        if (GameVisuals.Is3D) return; // no tile pool in 3D mode (see _Ready)
        // Pre-warm the pool so the first subscription burst doesn't hitch on instantiation.
        // Data-driven count, so code instantiation is deliberate; the instances' contents are
        // entirely declared in tile_component.tscn.
        for (var i = 0; i < RingChunkCount(AoiChunkRadius); i++)
            _tilePool.Add(CreateTile());

        // Re-tint on any elevation row change. The dirty-flag path (one rebuild per
        // frame) is enough — mesh geometry is elevation-independent, so InvalidateMeshes'
        // cache wipe would be pure churn during the binder's replay burst.
        if (GameManager.ElevationMap is { } elevationMap)
            elevationMap.ElevationChanged += OnElevationChanged;
    }

    private void OnElevationChanged() => _dirty = true;

    public override void _ExitTree()
    {
        if (GameManager.ElevationMap is { } elevationMap)
            elevationMap.ElevationChanged -= OnElevationChanged;
        base._ExitTree();
    }

    // Elevation tint ramp: level 0 darkened, top level slightly brightened, multiplied
    // with the ground/overlay textures via the MultiMesh per-instance colors.
    private static readonly Color ElevationTintLow = new(0.5f, 0.5f, 0.5f);
    private static readonly Color ElevationTintHigh = new(1.1f, 1.1f, 1.1f);

    private static Color ElevationTint(int elevation) =>
        ElevationTintLow.Lerp(ElevationTintHigh,
            Mathf.Clamp(elevation / (float)(ServerConstants.ElevationLevels - 1), 0f, 1f));

    public override void _Process(double delta)
    {
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;

        // Rebuilding on any camera motion meant walking rebuilt every visible tile every frame.
        // Pixel motion within a chunk can't change which chunks are visible, so the camera path
        // only triggers on a chunk crossing (or a zoom change, which resizes the cull window).
        var camHex = HexMath.WorldToHex(cam.GlobalPosition.X, cam.GlobalPosition.Y, _outerRadius);
        var camChunk = HexMath.ToLowerRes(camHex.X, camHex.Y, _chunkHexRadius);
        var camMoved = camChunk != _camChunk || cam.Zoom != _camZoom;

        // Tile-change callbacks only set _dirty — actually rebuilding here means a whole burst
        // of inserts/deletes from one chunk crossing collapses into a single rebuild per frame,
        // instead of one rebuild per individual tile event.
        if (camMoved || _dirty)
        {
            _camChunk = camChunk;
            _camPos = cam.GlobalPosition;
            _camZoom = cam.Zoom;
            _dirty = false;
            RebuildVisibleInstances();
        }
    }

    // --- TableBinderComponent signal handlers (wired in game.tscn) ---
    // Each binder has ReplayExistingRows on, so rows already in the client cache come through
    // the same insert path — no separate Iter() replay here.

    private void OnMapConfigRow()
    {
        var cfg = (MapConfig)_mapConfigBinder.LastRow!;
        _outerRadius = cfg.HexOuterRadius;
        _chunkHexRadius = cfg.ChunkHexRadius;
        InvalidateMeshes();
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

    private void OnTileRowInserted()
    {
        AddTile((TriangleTile)_terrainTilesBinder.LastRow!);
        _dirty = true;
    }

    private void OnTileRowUpdated()
    {
        RemoveTile(((TriangleTile)_terrainTilesBinder.LastOldRow!).TileId);
        AddTile((TriangleTile)_terrainTilesBinder.LastRow!);
        _dirty = true;
    }

    private void OnTileRowDeleted()
    {
        RemoveTile(((TriangleTile)_terrainTilesBinder.LastDeletedRow!).TileId);
        _dirty = true;
    }

    private void OnDecorRowInserted()
    {
        var decor = (HexDecor)_hexDecorBinder.LastRow!;
        _decorById[decor.DecorId] = decor;
        _dirty = true;
    }

    private void OnDecorRowUpdated()
    {
        var decor = (HexDecor)_hexDecorBinder.LastRow!;
        _decorById[decor.DecorId] = decor;
        _dirty = true;
    }

    private void OnDecorRowDeleted()
    {
        _decorById.Remove(((HexDecor)_hexDecorBinder.LastDeletedRow!).DecorId);
        _dirty = true;
    }

    // Outer radius only changes in dev — wipe cached meshes/batches so the next rebuild
    // recreates everything at the new scale instead of patching stale Multimesh state.
    private void InvalidateMeshes()
    {
        _meshCache.Clear();
        _decorMeshCache.Clear();
        foreach (var tile in _activeTiles.Values)
            tile.ClearMeshes();
        foreach (var tile in _tilePool)
            tile.ClearMeshes();
        RebuildVisibleInstances();
    }

    private TileComponent CreateTile()
    {
        var tile = TileScene.Instantiate<TileComponent>();
        tile.Terrain = this;
        AddChild(tile);
        return tile;
    }

    private TileComponent AcquireTile(long chunkIndex)
    {
        if (_activeTiles.TryGetValue(chunkIndex, out var active))
            return active;

        TileComponent tile;
        if (_tilePool.Count > 0)
        {
            var last = _tilePool.Count - 1;
            tile = _tilePool[last];
            _tilePool.RemoveAt(last);
        }
        else
        {
            // More visible chunks than the AOI pre-warm predicted — grow on demand.
            tile = CreateTile();
        }
        _activeTiles[chunkIndex] = tile;
        return tile;
    }

    private void RebuildVisibleInstances()
    {
        if (GameVisuals.Is3D) return; // 3D mode builds no 2D tiles (see _Ready)
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;

        var half = GetViewportRect().Size / cam.Zoom;
        var worldMin = _camPos - half;
        var worldMax = _camPos + half;
        var margin = _outerRadius * 2f;

        var lapQ = GameManager.LapQ;
        var lapR = GameManager.LapR;

        // Reset the pooled grouping buffers (contents only — the dictionaries and lists
        // themselves are reused; Populate consumes them synchronously, so reuse is safe).
        ResetGroups(_groundByChunk);
        ResetGroups(_overlayByChunk);
        ResetGroups(_shadowByChunk);
        ResetGroups(_decorByChunk);

        // Iterate everything cached (not a camera-window hex lookup) since chunks near a torus
        // seam are stored at their canonical (raw, far-away) position — we need to pick whichever
        // wrapped copy of each hex is actually near the camera before we can cull it.
        foreach (var ((q, r), tiles) in _tilesByHex)
        {
            var canonicalCenter = HexMath.HexToWorld(q, r, _outerRadius);
            var center = TorusMath.NearestCandidate(canonicalCenter, _camPos, lapQ, lapR);
            if (center.X < worldMin.X - margin || center.X > worldMax.X + margin ||
                center.Y < worldMin.Y - margin || center.Y > worldMax.Y + margin)
                continue;

            // One elevation lookup per hex: the minimal cosmetic cue (2D only — this
            // whole method early-outs in 3D mode) tints ground/overlay instances from a
            // dark shade at level 0 up to slightly-past-white at the top level.
            var tint = ElevationTint(GameManager.ElevationMap?.GetElevation(q, r) ?? 0);

            foreach (var tile in tiles)
            {
                var ground = GetChunkGroup(_groundByChunk, tile.ChunkIndex);
                var key = (tile.TriIndex, tile.Rotation, tile.TextureId);
                if (!ground.TryGetValue(key, out var positions))
                    ground[key] = positions = new List<(Vector2, Color)>();
                positions.Add((center, tint));

                if (string.IsNullOrEmpty(tile.OverlayTextureId)) continue;
                var overlay = GetChunkGroup(_overlayByChunk, tile.ChunkIndex);
                var overlayKey = (tile.TriIndex, tile.OverlayRotation, tile.OverlayTextureId);
                if (!overlay.TryGetValue(overlayKey, out var overlayPositions))
                    overlay[overlayKey] = overlayPositions = new List<(Vector2, float, Color)>();
                overlayPositions.Add((center, tile.OverlayScale, tint));
            }
        }

        foreach (var decor in _decorById.Values)
        {
            var canonicalCenter = HexMath.HexToWorld(decor.HexQ, decor.HexR, _outerRadius);
            var center = TorusMath.NearestCandidate(canonicalCenter, _camPos, lapQ, lapR);
            if (center.X < worldMin.X - margin || center.X > worldMax.X + margin ||
                center.Y < worldMin.Y - margin || center.Y > worldMax.Y + margin)
                continue;

            var rotationRadians = Mathf.DegToRad(decor.RotationDegrees);
            AddDecorInstance(GetChunkGroup(_decorByChunk, decor.ChunkIndex), decor.TextureId, center, rotationRadians, decor.HexR);
            AddDecorInstance(GetChunkGroup(_shadowByChunk, decor.ChunkIndex), decor.ShadowTextureId, center, rotationRadians, decor.HexR);
        }

        // Hand each present chunk's batches to its TileComponent; park tiles whose chunk left
        // the AOI (their layers are zeroed, so nothing stale keeps drawing). The pooled buffers
        // retain chunks/batch keys whose lists came up empty this pass, so presence is decided
        // by content, not by key existence.
        _presentChunks.Clear();
        CollectPresentChunks(_groundByChunk);
        CollectPresentChunks(_overlayByChunk);
        CollectPresentChunks(_shadowByChunk);
        CollectPresentChunks(_decorByChunk);
        foreach (var chunk in _presentChunks)
        {
            AcquireTile(chunk).Populate(
                chunk,
                _groundByChunk.GetValueOrDefault(chunk, EmptyGround),
                _overlayByChunk.GetValueOrDefault(chunk, EmptyOverlay),
                _shadowByChunk.GetValueOrDefault(chunk, EmptyDecor),
                _decorByChunk.GetValueOrDefault(chunk, EmptyDecor));
        }

        _releaseBuffer.Clear();
        foreach (var (chunk, tile) in _activeTiles)
            if (!_presentChunks.Contains(chunk))
                _releaseBuffer.Add(chunk);
        foreach (var chunk in _releaseBuffer)
        {
            var tile = _activeTiles[chunk];
            _activeTiles.Remove(chunk);
            tile.Clear();
            _tilePool.Add(tile);
        }
    }

    private readonly List<long> _releaseBuffer = new();

    private static readonly Dictionary<(int TriIndex, int Rotation, string TextureId), List<(Vector2 Position, Color Tint)>> EmptyGround = new();
    private static readonly Dictionary<(int TriIndex, int Rotation, string TextureId), List<(Vector2 Position, float Scale, Color Tint)>> EmptyOverlay = new();
    private static readonly Dictionary<string, List<(Vector2 Position, float RotationRadians, int HexR)>> EmptyDecor = new();

    private static Dictionary<TKey, List<TValue>> GetChunkGroup<TKey, TValue>(Dictionary<long, Dictionary<TKey, List<TValue>>> byChunk, long chunkIndex) where TKey : notnull
    {
        if (!byChunk.TryGetValue(chunkIndex, out var grouped))
            byChunk[chunkIndex] = grouped = new Dictionary<TKey, List<TValue>>();
        return grouped;
    }

    // Resets a pooled grouping buffer for the next rebuild without freeing it: every list keeps
    // its capacity and every chunk/batch key stays, so steady-state rebuilds reuse all of it.
    private static void ResetGroups<TKey, TValue>(Dictionary<long, Dictionary<TKey, List<TValue>>> byChunk) where TKey : notnull
    {
        foreach (var grouped in byChunk.Values)
            foreach (var list in grouped.Values)
                list.Clear();
    }

    private void CollectPresentChunks<TKey, TValue>(Dictionary<long, Dictionary<TKey, List<TValue>>> byChunk) where TKey : notnull
    {
        foreach (var (chunk, grouped) in byChunk)
            if (HasInstances(grouped))
                _presentChunks.Add(chunk);
    }

    private static bool HasInstances<TKey, TValue>(Dictionary<TKey, List<TValue>> grouped) where TKey : notnull
    {
        foreach (var list in grouped.Values)
            if (list.Count > 0)
                return true;
        return false;
    }

    private static void AddDecorInstance(Dictionary<string, List<(Vector2 Position, float RotationRadians, int HexR)>> grouped, string textureId, Vector2 position, float rotationRadians, int hexR)
    {
        if (string.IsNullOrEmpty(textureId)) return;
        if (!grouped.TryGetValue(textureId, out var list))
            grouped[textureId] = list = new List<(Vector2, float, int)>();
        list.Add((position, rotationRadians, hexR));
    }

    /// Mesh + texture for one wedge batch key, from the shared caches (used by the ground and
    /// overlay layers of every TileComponent).
    public (ArrayMesh Mesh, Texture2D? Texture) GetWedgeMeshAndTexture(int triIndex, int rotation, string textureId)
    {
        var (baseTexture, uvRegion) = ResolveTexture(GetTexture(textureId));
        return (GetOrBuildMesh(triIndex, rotation, textureId, uvRegion), baseTexture);
    }

    /// Mesh + texture for one decor batch key, from the shared caches (used by both decor layers).
    public (ArrayMesh Mesh, Texture2D? Texture) GetDecorMeshAndTexture(string textureId)
    {
        var rawTexture = GetTexture(textureId);
        var (resolvedTexture, uvRegion) = ResolveTexture(rawTexture);
        return (GetOrBuildDecorMesh(textureId, rawTexture?.GetSize() ?? Vector2.Zero, uvRegion), resolvedTexture);
    }

    // Decor is a plain rectangular sprite (not a UV-cropped wedge), anchored bottom-center — the
    // prop's "feet" — rather than its geometric center, so a placement's rotation swings the
    // sprite around its ground-contact point and a tall canopy doesn't appear to float off the
    // hex center it's planted at.
    private ArrayMesh GetOrBuildDecorMesh(string textureId, Vector2 size, Rect2 uvRegion)
    {
        if (_decorMeshCache.TryGetValue(textureId, out var cached))
            return cached;

        var halfWidth = size.X / 2f;
        var vertices = new[]
    {
            new Vector3(-halfWidth, -size.Y, 0f), new Vector3(halfWidth, -size.Y, 0f),
            new Vector3(halfWidth, 0f, 0f), new Vector3(-halfWidth, 0f, 0f),
        };
        var uvs = new[]
    {
            uvRegion.Position, uvRegion.Position + new Vector2(uvRegion.Size.X, 0f),
            uvRegion.Position + uvRegion.Size, uvRegion.Position + new Vector2(0f, uvRegion.Size.Y),
        };
        var indices = new[] { 0, 1, 2, 0, 2, 3 };

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _decorMeshCache[textureId] = mesh;
        return mesh;
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

    public Vector2 GetWedgeCentroid(int triIndex)
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
        var resPath = GameManager.GetResPath2D(textureId);
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
}
