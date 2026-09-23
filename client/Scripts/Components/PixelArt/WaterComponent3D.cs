#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// Hex ocean (3D), child in world_3d.tscn: any hex with NO server terrain rows gets a
/// water tile — land is the set of hexes present in NearbyTerrainTiles (via a child
/// binder, replay on — persistent view table; hexes are ref-counted on their single
/// ground row). Tiles are pooled around the player in a ring — the
/// HexGridOverlay3DComponent pattern. Hexes beyond the AOI read as missing, so the
/// ring edge is open ocean.
///
/// Tiles are the addon's PixelizerWater3D nodes (one shared ShaderMaterial built
/// from res://addons/pixelizer_3d/shaders/water.gdshader), each given a per-tile
/// `coast` instance uniform (1.0 = touches land, 0.5 = one hex away, 0 = open ocean)
/// and a deterministic `wave_phase`. The addon shader is deliberately OPAQUE:
/// pixelized objects only write depth at anchor fragments, so any transparent
/// surface between the camera and a character draws over it and "dissolves" it into
/// anchor dots. The addon node registers the shared material with the manager, so
/// cloud shadows continue across the coastline.
///
/// Note: PixelizerWater3D builds a square PlaneMesh, so tiles are square (sized to
/// cover their hex plus overlap) rather than hex-shaped — the accepted trade of
/// adopting the addon water node.
///
/// Tiles float at WaterLevel above the level-0 terrain surface (MST renders missing
/// hexes flat at y=0, so the water must sit above it to be seen). A large BackingOcean
/// quad (plus a deep AbyssOcean catcher) shares the same material so the horizon is
/// always water and shallow-pitch below-world rays land on ocean instead of the sky.
/// </summary>
public partial class WaterComponent3D : Node3DComponent
{
    /// Same fallback as ElevationMapComponent before the first MapConfig row arrives.
    private const float DefaultHexOuterRadius = 96.0f;
    private const string AddonWaterScriptPath = "res://addons/pixelizer_3d/effects/pixelizer_water_3d.gd";
    private const string AddonWaterShaderPath = "res://addons/pixelizer_3d/shaders/water.gdshader";

    // Static open-ocean quad under the whole world (far beyond the tile ring and the
    // terrain lap), so the horizon is always water — never sky/void past a hard edge.
    // Shares the tile material (its `coast` instance uniform defaults to 0 = open
    // ocean) so cloud shadows register onto it for free. One flat quad: no vertex
    // waves matter at this scale, and the near-field ring tiles cover it.
    private const float BackingSize = 30000f;
    // The tile shader's vertex waves dip 0.15 below WaterLevel, so the backing
    // must sit deeper than the trough bottoms or it pokes through as dark dots.
    private const float BackingDrop = 0.3f;

    // Second catcher below the backing — a safety net behind World3DComponent's
    // VOffset window shift, which already keeps the frustum's bottom edge above
    // the surface at every legal rig pose. If a ray ever still slips under the
    // backing (rig exports widened past the bounds derived here, or the shift
    // disabled), this plane is what it lands on: the rig's shallowest legal
    // view (CameraRigComponent PitchMax −10°, DistanceMax 1200, World3DComponent's
    // Size = Distance × 1.5347) puts the frustum's bottom-edge ray origin at
    // pivot + 1200·sin(10°) − (1.5347·1200/2)·cos(10°) ≈ pivot − 685, and the
    // pivot never sits below the level-0 terrain tops (y=0). Rays that start
    // under a plane can never descend into it, so without a catcher those
    // rays fly under the backing, under the terrain lap, and render the sky's
    // ground half as a flat dead band. Sunk 800 (115 below the worst origin)
    // so they land on open ocean instead. Same size/material as the backing
    // (`coast` also defaults to 0).
    private const float AbyssDrop = 800f;

    // Axial hex deltas (same table as TerrainComponent3D.ChunkNeighborOffsets) for
    // ring 1 and ring 2 — the coast rings a water tile checks for land.
    private static readonly Vector2I[] Ring1Offsets =
    {
        new(1, 0), new(0, 1), new(-1, 1), new(-1, 0), new(0, -1), new(1, -1),
    };
    private static readonly Vector2I[] Ring2Offsets =
    {
        new(2, 0), new(1, 1), new(0, 2), new(-1, 2), new(-2, 2), new(-2, 1),
        new(-2, 0), new(-1, -1), new(0, -2), new(1, -2), new(2, -2), new(2, -1),
    };

    [ExportGroup("Water")]
    [Export] public bool WaterEnabled { get; set; } = true;
    /// Hex-ring radius around the player that gets water tiles for missing hexes.
    [Export] public int ViewRadiusHexes { get; set; } = 10;
    /// World Y of the water surface. Level-0 terrain tops at y=0, so this must be
    /// above 0 to be visible; raised hexes (16/level) tower out of the water.
    [Export] public float WaterLevel { get; set; } = 0.6f;
    /// Square tile edge as a multiple of the hex circumradius (2.0 covers the hex
    /// with overlap to hide seams between neighbours).
    [Export(PropertyHint.Range, "1.5,3.0")] public float TileSizeMultiplier { get; set; } = 2.0f;
    /// Plane subdivisions per tile (vertex-wave resolution).
    [Export(PropertyHint.Range, "1,24")] public int HexSubdivisions { get; set; } = 8;
    /// Noise texture; null generates the addon demo's default (Perlin 0.08, seed 3).
    [Export] public Texture2D? WaterNoise { get; set; }

    [ExportGroup("Colors")]
    [Export] public Color ShallowColor { get; set; } = new(0.35f, 0.75f, 0.80f);
    [Export] public Color DeepColor { get; set; } = new(0.08f, 0.25f, 0.45f);
    [Export] public Color FoamColor { get; set; } = new(0.90f, 0.97f, 1.00f);
    /// Quantisation of the deep→shallow ramp (hard bands, pixel-art style).
    [Export] public float DepthLevels { get; set; } = 4f;

    [ExportGroup("Waves")]
    [Export] public float WaveHeight { get; set; } = 0.15f;
    [Export] public float WaveFrequency { get; set; } = 0.25f;
    [Export] public float WaveSpeed { get; set; } = 1.2f;

    [ExportGroup("Foam")]
    /// Foam coverage on a fully coastal hex (ring-2 hexes get half).
    [Export(PropertyHint.Range, "0,1")] public float FoamAmount { get; set; } = 0.45f;
    [Export] public float FoamNoiseScale { get; set; } = 0.10f;
    [Export] public Vector2 FoamScroll { get; set; } = new(0.05f, 0.03f);

    [ExportGroup("Whitecaps")]
    [Export] public float CapThreshold { get; set; } = 0.88f;
    [Export] public float CapNoiseScale { get; set; } = 0.02f;
    [Export] public Vector2 CapScroll { get; set; } = new(0.06f, -0.04f);

    private readonly Dictionary<(int Q, int R), int> _landRowCounts = new();
    private readonly Dictionary<Vector2I, MeshInstance3D> _tiles = new();

    private TableBinderComponent _terrainTilesBinder = null!;
    private TableBinderComponent _mapConfigBinder = null!;
    private GDScript _waterScript = null!;
    private ShaderMaterial _sharedMaterial = null!;
    private Node? _manager;

    private float _outerRadius = DefaultHexOuterRadius;
    private int _chunkHexRadius;
    private int _chunkCols;
    private int _chunkRows;
    private Vector2I _lastCenterHex = new(int.MaxValue, int.MaxValue);
    private bool _dirty = true;

    public override void _Ready()
    {
        base._Ready();
        _terrainTilesBinder = GetNode<TableBinderComponent>("NearbyTerrainTilesBinder");
        _mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
        if (!WaterEnabled) return;
        _waterScript = GD.Load<GDScript>(AddonWaterScriptPath);
        _manager = GetNodeOrNull("../PixelizerManager3D");
        BuildSharedMaterial();
        BuildBackingOcean();
        BuildAbyssOcean();
    }

    public override void _Process(double _delta)
    {
        if (!WaterEnabled || _waterScript == null) return;
        var player = LocalPlayer.Local;
        if (player == null) return;

        var p = player.GlobalPosition; // Vector2 from CharacterBody2D
        var hex = HexMath.WorldToHex(p.X, p.Y, _outerRadius);
        if (!_dirty && hex == _lastCenterHex) return;
        _dirty = false;
        _lastCenterHex = hex;
        RefreshTiles(hex);
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
        ClearTiles();
        _dirty = true;
    }

    private void OnTerrainTileRowInserted() => AddLand((HexTile)_terrainTilesBinder.LastRow!);

    private void OnTerrainTileRowUpdated()
    {
        RemoveLand((HexTile)_terrainTilesBinder.LastOldRow!);
        AddLand((HexTile)_terrainTilesBinder.LastRow!);
    }

    private void OnTerrainTileRowDeleted() => RemoveLand((HexTile)_terrainTilesBinder.LastDeletedRow!);

    private void AddLand(HexTile tile)
    {
        var key = (tile.HexQ, tile.HexR);
        _landRowCounts[key] = _landRowCounts.GetValueOrDefault(key) + 1;
        if (_landRowCounts[key] == 1) _dirty = true; // hex became land: may lose a water tile
    }

    private void RemoveLand(HexTile tile)
    {
        var key = (tile.HexQ, tile.HexR);
        if (!_landRowCounts.TryGetValue(key, out int count)) return;
        if (count <= 1)
        {
            _landRowCounts.Remove(key);
            _dirty = true; // hex became water
        }
        else _landRowCounts[key] = count - 1;
    }

    // ── Tile management ───────────────────────────────────────────────────────

    private void RefreshTiles(Vector2I center)
    {
        int R = ViewRadiusHexes;

        // The set of water hexes that should be visible: the hex ring around the
        // player, minus every hex the server says exists. The ring is generated in
        // node space (tile positions stay node-space so the ring follows the player),
        // but land rows are canonical — wrap each hex before the membership test.
        var needed = new HashSet<Vector2I>();
        for (int dq = -R; dq <= R; dq++)
            for (int dr = Mathf.Max(-R, -dq - R); dr <= Mathf.Min(R, -dq + R); dr++)
            {
                var hex = new Vector2I(center.X + dq, center.Y + dr);
                var canonical = CanonicalHex(hex);
                if (!_landRowCounts.ContainsKey((canonical.X, canonical.Y)))
                    needed.Add(hex);
            }

        var toRemove = new List<Vector2I>();
        foreach (var key in _tiles.Keys)
            if (!needed.Contains(key)) toRemove.Add(key);
        foreach (var key in toRemove)
        {
            _tiles[key].QueueFree();
            _tiles.Remove(key);
        }

        foreach (var hex in needed)
            if (!_tiles.ContainsKey(hex))
                SpawnTile(hex.X, hex.Y);

        // Land rows stream in/out with the AOI — refresh coast rings on surviving
        // tiles (cheap: two ring lookups per tile, only on a dirty refresh).
        foreach (var (hex, tile) in _tiles)
            tile.SetInstanceShaderParameter("coast", ComputeCoast(hex.X, hex.Y));
    }

    // 1.0 when a ring-1 neighbor is land, 0.5 when only ring-2 reaches land, else 0.
    // q,r is a node-space hex; the land rows are canonical, so wrap before looking up.
    private float ComputeCoast(int q, int r)
    {
        var canonical = CanonicalHex(new Vector2I(q, r));
        foreach (var d in Ring1Offsets)
            if (_landRowCounts.ContainsKey((canonical.X + d.X, canonical.Y + d.Y))) return 1.0f;
        foreach (var d in Ring2Offsets)
            if (_landRowCounts.ContainsKey((canonical.X + d.X, canonical.Y + d.Y))) return 0.5f;
        return 0.0f;
    }

    // Map a node-space hex into the canonical lap for lookups against server (canonical)
    // land rows. Tile positions and dictionary keys stay node-space.
    private Vector2I CanonicalHex(Vector2I hex) =>
        HexMath.WrapHex(hex.X, hex.Y, _chunkHexRadius, _chunkCols, _chunkRows);

    private void SpawnTile(int q, int r)
    {
        var pos = HexMath.HexToWorld3D(q, r, _outerRadius);
        var tile = CreateWaterNode(
            new Vector3(pos.X, WaterLevel, pos.Z),
            _outerRadius * TileSizeMultiplier,
            HexSubdivisions,
            ComputeCoast(q, r),
            WavePhase(q, r));
        AddChild(tile);
        _tiles[new Vector2I(q, r)] = tile;
    }

    private void ClearTiles()
    {
        foreach (var tile in _tiles.Values) tile.QueueFree();
        _tiles.Clear();
        _lastCenterHex = new Vector2I(int.MaxValue, int.MaxValue);
    }

    // ── Addon water nodes ─────────────────────────────────────────────────────

    // Instantiate a PixelizerWater3D (GDScript) as an untyped MeshInstance3D. Its
    // exports are read in _ready, so every property (especially shared_material,
    // size, coast, wave_phase) is set before AddChild.
    private MeshInstance3D CreateWaterNode(Vector3 position, float size, int subdivisions, float coast, float wavePhase)
    {
        var node = (MeshInstance3D)_waterScript.New();
        node.Position = position;
        node.Set("shared_material", _sharedMaterial);
        if (_manager != null) node.Set("manager", _manager);
        node.Set("size", size);
        node.Set("subdivisions", subdivisions);
        node.Set("coast", coast);
        node.Set("wave_phase", wavePhase);
        return node;
    }

    // Deterministic per-hex animation offset so neighbouring tiles do not move in
    // lockstep (the addon's own demo uses random phases).
    private static float WavePhase(int q, int r) => ((q * 13 + r * 7) % 100) / 100f * Mathf.Tau;

    private void BuildSharedMaterial()
    {
        _sharedMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(AddonWaterShaderPath) };
        _sharedMaterial.SetShaderParameter("water_noise", WaterNoise ?? GenerateNoiseTexture());
        _sharedMaterial.SetShaderParameter("shallow_color", ShallowColor);
        _sharedMaterial.SetShaderParameter("deep_color", DeepColor);
        _sharedMaterial.SetShaderParameter("foam_color", FoamColor);
        _sharedMaterial.SetShaderParameter("depth_levels", DepthLevels);
        _sharedMaterial.SetShaderParameter("wave_height", WaveHeight);
        _sharedMaterial.SetShaderParameter("wave_frequency", WaveFrequency);
        _sharedMaterial.SetShaderParameter("wave_speed", WaveSpeed);
        _sharedMaterial.SetShaderParameter("foam_amount", FoamAmount);
        _sharedMaterial.SetShaderParameter("foam_noise_scale", FoamNoiseScale);
        _sharedMaterial.SetShaderParameter("foam_scroll", FoamScroll);
        _sharedMaterial.SetShaderParameter("cap_threshold", CapThreshold);
        _sharedMaterial.SetShaderParameter("cap_noise_scale", CapNoiseScale);
        _sharedMaterial.SetShaderParameter("cap_scroll", CapScroll);
    }

    private void BuildBackingOcean()
    {
        var backing = CreateWaterNode(
            new Vector3(0f, WaterLevel - BackingDrop, 0f), BackingSize, 1, 0f, 0f);
        backing.Name = "BackingOcean";
        AddChild(backing);
    }

    // See AbyssDrop: below-world rays at shallow pitches land here (open ocean
    // via the same material) instead of the sky's ground half.
    private void BuildAbyssOcean()
    {
        var abyss = CreateWaterNode(
            new Vector3(0f, WaterLevel - AbyssDrop, 0f), BackingSize, 1, 0f, 0f);
        abyss.Name = "AbyssOcean";
        AddChild(abyss);
    }

    private static ImageTexture GenerateNoiseTexture()
    {
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.08f,
            Seed = 3,
        };
        return ImageTexture.CreateFromImage(noise.GetImage(128, 128));
    }
}
