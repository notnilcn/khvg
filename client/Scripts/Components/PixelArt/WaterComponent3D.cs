#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// Hex ocean (3D), child in world_3d.tscn: any hex with NO server terrain rows gets a
/// water tile — land is the set of hexes present in NearbyTerrainTiles (via a child
/// binder, replay on — persistent view table; hexes are ref-counted because a hex has
/// several wedge rows). Water tiles are pooled MeshInstance3Ds of one shared
/// code-generated subdivided hexagon mesh (the subdivision feeds the shader's vertex
/// waves), refreshed in a ring around the local player — the HexGridOverlay3DComponent
/// pattern. Hexes beyond the AOI read as missing, so the ring edge is open ocean.
///
/// The material runs a coastal-banded derivative of the 3dpixelart addon's demo water
/// shader (Assets/Shaders/water.gdshader — the addon ships no water node). The shader
/// is deliberately OPAQUE: pixelized objects only write depth at anchor fragments, so
/// any transparent surface between the camera and a character draws over it and
/// "dissolves" it into anchor dots. The demo's depth-texture fade/foam is replaced by
/// a per-tile `coast` instance uniform (1.0 = touches land, 0.5 = one hex away, 0 =
/// open ocean) computed from the land set — the underwater terrain is flat level-0
/// everywhere, so a depth readout carries nothing the coast rings don't carry better.
/// Tiles float at WaterLevel above the level-0 terrain surface (MST renders missing
/// hexes flat at y=0, so the water must sit above it to be seen).
/// </summary>
public partial class WaterComponent3D : Node3DComponent
{
    /// Same fallback as ElevationMapComponent before the first MapConfig row arrives.
    private const float DefaultHexOuterRadius = 96.0f;
    private const string WaterShaderPath = "res://Assets/Shaders/water.gdshader";

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
    /// Fraction of the hex footprint the tile covers, hiding seams between neighbors.
    [Export(PropertyHint.Range, "0.9,1.0")] public float TileInset { get; set; } = 0.995f;
    /// Concentric subdivision rings of the shared hex mesh (vertex-wave resolution).
    [Export(PropertyHint.Range, "1,24")] public int HexSubdivisions { get; set; } = 12;
    /// Noise texture; null generates the addon demo's default (Perlin 0.08, seed 3).
    [Export] public Texture2D? WaterNoise { get; set; }

    [ExportGroup("Colors")]
    [Export] public Color ShallowColor { get; set; } = new(0.35f, 0.75f, 0.80f);
    [Export] public Color DeepColor { get; set; } = new(0.08f, 0.25f, 0.45f);
    [Export] public Color FoamColor { get; set; } = new(0.90f, 0.97f, 1.00f);

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
    private ShaderMaterial _material = null!;
    private ArrayMesh _mesh = null!;

    /// The shared water material, null until BuildMaterial runs in _Ready.
    /// PixelArtPipelineComponent registers it for cloud-shadow uniforms.
    public ShaderMaterial? WaterMaterial => _material;

    private float _outerRadius = DefaultHexOuterRadius;
    private Vector2I _lastCenterHex = new(int.MaxValue, int.MaxValue);
    private bool _dirty = true;

    public override void _Ready()
    {
        base._Ready();
        _terrainTilesBinder = GetNode<TableBinderComponent>("NearbyTerrainTilesBinder");
        _mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
        if (!WaterEnabled) return;
        BuildMaterial();
        BuildMesh();
    }

    public override void _Process(double _delta)
    {
        if (!WaterEnabled || _material == null) return;
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
        _outerRadius = ((MapConfig)_mapConfigBinder.LastRow!).HexOuterRadius;
        ClearTiles();
        _dirty = true;
    }

    private void OnTerrainTileRowInserted() => AddLand((TriangleTile)_terrainTilesBinder.LastRow!);

    private void OnTerrainTileRowUpdated()
    {
        RemoveLand((TriangleTile)_terrainTilesBinder.LastOldRow!);
        AddLand((TriangleTile)_terrainTilesBinder.LastRow!);
    }

    private void OnTerrainTileRowDeleted() => RemoveLand((TriangleTile)_terrainTilesBinder.LastDeletedRow!);

    private void AddLand(TriangleTile tile)
    {
        var key = (tile.HexQ, tile.HexR);
        _landRowCounts[key] = _landRowCounts.GetValueOrDefault(key) + 1;
        if (_landRowCounts[key] == 1) _dirty = true; // hex became land: may lose a water tile
    }

    private void RemoveLand(TriangleTile tile)
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
        // player, minus every hex the server says exists.
        var needed = new HashSet<Vector2I>();
        for (int dq = -R; dq <= R; dq++)
            for (int dr = Mathf.Max(-R, -dq - R); dr <= Mathf.Min(R, -dq + R); dr++)
            {
                var hex = new Vector2I(center.X + dq, center.Y + dr);
                if (!_landRowCounts.ContainsKey((hex.X, hex.Y)))
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
    private float ComputeCoast(int q, int r)
    {
        foreach (var d in Ring1Offsets)
            if (_landRowCounts.ContainsKey((q + d.X, r + d.Y))) return 1.0f;
        foreach (var d in Ring2Offsets)
            if (_landRowCounts.ContainsKey((q + d.X, r + d.Y))) return 0.5f;
        return 0.0f;
    }

    private void SpawnTile(int q, int r)
    {
        var pos = HexMath.HexToWorld3D(q, r, _outerRadius);
        float s = _outerRadius * TileInset;
        var tile = new MeshInstance3D
        {
            Mesh = _mesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(pos.X, WaterLevel, pos.Z),
            Scale = new Vector3(s, 1f, s),
        };
        tile.SetInstanceShaderParameter("coast", ComputeCoast(q, r));
        AddChild(tile);
        _tiles[new Vector2I(q, r)] = tile;
    }

    private void ClearTiles()
    {
        foreach (var tile in _tiles.Values) tile.QueueFree();
        _tiles.Clear();
        _lastCenterHex = new Vector2I(int.MaxValue, int.MaxValue);
    }

    // ── Material + mesh ───────────────────────────────────────────────────────

    private void BuildMaterial()
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>(WaterShaderPath) };
        _material.SetShaderParameter("water_noise", WaterNoise ?? GenerateNoiseTexture());
        _material.SetShaderParameter("shallow_color", ShallowColor);
        _material.SetShaderParameter("deep_color", DeepColor);
        _material.SetShaderParameter("foam_color", FoamColor);
        _material.SetShaderParameter("wave_height", WaveHeight);
        _material.SetShaderParameter("wave_frequency", WaveFrequency);
        _material.SetShaderParameter("wave_speed", WaveSpeed);
        _material.SetShaderParameter("foam_amount", FoamAmount);
        _material.SetShaderParameter("foam_noise_scale", FoamNoiseScale);
        _material.SetShaderParameter("foam_scroll", FoamScroll);
        _material.SetShaderParameter("cap_threshold", CapThreshold);
        _material.SetShaderParameter("cap_noise_scale", CapNoiseScale);
        _material.SetShaderParameter("cap_scroll", CapScroll);
    }

    // Shared unit-hexagon mesh (outer radius 1, pointy-top matching HexMath), subdivided
    // into HexSubdivisions concentric rings so the water shader's vertex waves have
    // geometry to move. Built as six triangular sectors on an (a, b) lattice; sector
    // borders duplicate vertices, but identical world positions displace identically,
    // so no cracks open up.
    private void BuildMesh()
    {
        int n = Mathf.Max(HexSubdivisions, 1);
        var corners = new Vector3[6];
        for (int k = 0; k < 6; k++)
        {
            float angle = Mathf.DegToRad(30f + 60f * k);
            corners[k] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int vertexCount = 0;
        for (int s = 0; s < 6; s++)
        {
            var c0 = corners[s];
            var c1 = corners[(s + 1) % 6];
            // Lattice points P(a, b) = (a*c0 + b*c1) / n, a >= 0, b >= 0, a+b <= n.
            var idx = new int[n + 1][];
            for (int a = 0; a <= n; a++)
            {
                idx[a] = new int[n + 1 - a];
                for (int b = 0; b <= n - a; b++)
                {
                    idx[a][b] = vertexCount++;
                    st.SetNormal(Vector3.Up);
                    st.AddVertex((c0 * a + c1 * b) / n);
                }
            }
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n - a; b++)
                {
                    st.AddIndex(idx[a][b]); st.AddIndex(idx[a][b + 1]); st.AddIndex(idx[a + 1][b]);
                    if (b < n - a - 1)
                    {
                        st.AddIndex(idx[a + 1][b]); st.AddIndex(idx[a][b + 1]); st.AddIndex(idx[a + 1][b + 1]);
                    }
                }
        }
        _mesh = st.Commit();
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
