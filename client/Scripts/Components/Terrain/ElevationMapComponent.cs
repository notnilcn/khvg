#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Per-hex elevation levels, child of the game root (declared inline in game.tscn with
/// signals wired in the editor): mirrors the AOI-filtered NearbyBuildingTiles rows —
/// exactly one per hex, elevation stamped by world generation — into a (q, r) → level
/// dictionary. The hex outer radius comes from a child MapConfig binder (the
/// HexGridOverlayComponent pattern); the levels from a child NearbyBuildingTiles binder
/// (replay on — persistent view table). Everything elevation-aware (LocalPlayer movement
/// prediction, bullet bands, the terrain tint) reads through GetElevation/GetElevationAt
/// or the GameManager.ElevationMap facade.
///
/// The same rows also carry 7 world-unit height samples [center, c0..c5] (MST corner
/// order — MSTHexMath.hex_corner_offset), cached per hex and exposed as a smooth
/// triangle-fan interpolation via TryGetSurfaceHeightAt. GetWorldHeightAt prefers that
/// smooth surface and falls back to GetElevationAt * WorldHeightPerLevel when the hex has
/// no payload; GetElevation/GetElevationAt stay level-based gameplay authorities.
/// </summary>
public partial class ElevationMapComponent : Component
{
    /// Raised on any elevation row change so TerrainComponent can re-tint.
    public event Action? ElevationChanged;

    /// Same fallback as TerrainComponent before the first MapConfig row arrives.
    private const float DefaultHexOuterRadius = 96.0f;

    /// Barycentric slack so a point exactly on a fan seam still lands in a triangle.
    private const float SampleWeightsEpsilon = 1e-4f;

    /// 3D world height (Y units) per elevation level. Client-only visual mapping — the 3D
    /// terrain must use this same step height once it renders elevation (today only the
    /// debug overlay stretches columns, by its own factor). Used by cursor ground raycasts.
    public const float WorldHeightPerLevel = 16f;

    private readonly Dictionary<(int Q, int R), int> _elevationByHex = new();
    // 7 world-unit Y samples per canonical hex: [center, c0..c5] in the MST corner order
    // (MSTHexMath.hex_corner_offset: corner d at angle 60°·d − 30° in the XZ plane).
    private readonly Dictionary<(int Q, int R), int[]> _heightsByHex = new();
    // World XZ offsets of the 6 corners for the current outer radius.
    private readonly Vector2[] _cornerOffsets = new Vector2[6];
    private float _outerRadius = DefaultHexOuterRadius;
    private int _chunkHexRadius;
    private int _chunkCols;
    private int _chunkRows;

    /// Hex outer radius from the subscribed MapConfig row (needed by the fall tween's
    /// landing distance as well as WorldToHex).
    public float OuterRadius => _outerRadius;

    private TableBinderComponent _mapConfigBinder = null!;
    private TableBinderComponent _buildingTilesBinder = null!;

    public override void _Ready()
    {
        base._Ready();
        _mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
        _buildingTilesBinder = GetNode<TableBinderComponent>("NearbyBuildingTilesBinder");
        UpdateCornerOffsets();
    }

    // Corner d of a pointy-top hex: world angle 60°·d − 30° at the outer radius. Lockstep
    // with MSTHexMath.hex_corner_offset — the order the server packed the height samples in.
    private void UpdateCornerOffsets()
    {
        for (int d = 0; d < 6; d++)
        {
            float angle = Mathf.DegToRad(60f * d - 30f);
            _cornerOffsets[d] = new Vector2(Mathf.Cos(angle) * _outerRadius, Mathf.Sin(angle) * _outerRadius);
        }
    }

    /// Elevation level of one hex; hexes with no row (outside the AOI) read as level 0.
    public int GetElevation(int q, int r) => _elevationByHex.GetValueOrDefault((q, r), 0);

    /// Elevation level of the hex containing a world position. The position is a node/global
    /// position that can drift up to a lap from canonical, so it is wrapped (lap vectors from
    /// MapConfig) before the hex lookup — GetElevation's keys are server-canonical.
    public int GetElevationAt(Vector2 worldPos)
    {
        var canonical = TorusMath.WrapPosition(worldPos, _chunkHexRadius, _chunkCols, _chunkRows, _outerRadius, GameManager.LapQ, GameManager.LapR);
        var hex = HexMath.WorldToHex(canonical.X, canonical.Y, _outerRadius);
        return GetElevation(hex.X, hex.Y);
    }

    /// Smooth terrain surface height (world Y) at a world position, from the hex's 7 height
    /// samples [center, c0..c5] (already world-unit Y values). The position is wrapped into
    /// the canonical lap and rounded to its axial hex; the point is then located in one of
    /// the 6 fan triangles (center, corner d, corner d+1) and barycentrically interpolated.
    /// Corner d follows the MSTHexMath.hex_corner_offset convention (angle 60°·d − 30° in the
    /// XZ plane) — the same convention the server packed the samples in, so two hexes sharing
    /// an edge interpolate their shared corner samples to identical values along that edge
    /// (crack-free). Returns false when the hex has no payload (outside the AOI) — callers
    /// fall back to the stepped level height.
    public bool TryGetSurfaceHeightAt(Vector2 worldPos, out float height)
    {
        var canonical = TorusMath.WrapPosition(worldPos, _chunkHexRadius, _chunkCols, _chunkRows, _outerRadius, GameManager.LapQ, GameManager.LapR);
        var hex = HexMath.WorldToHex(canonical.X, canonical.Y, _outerRadius);
        if (!_heightsByHex.TryGetValue((hex.X, hex.Y), out var samples) || samples.Length != 7)
        {
            height = 0f;
            return false;
        }

        var center = HexMath.HexToWorld(hex.X, hex.Y, _outerRadius);
        var p = new Vector2(canonical.X - center.X, canonical.Y - center.Y);

        for (int d = 0; d < 6; d++)
        {
            var cornerA = _cornerOffsets[d];
            var cornerB = _cornerOffsets[(d + 1) % 6];
            float denom = Cross(cornerA, cornerB);
            if (Mathf.IsZeroApprox(denom)) continue;

            // Barycentric weights of p in the CCW fan triangle (center, cornerA, cornerB).
            float wA = Cross(p, cornerB) / denom;
            float wB = Cross(cornerA, p) / denom;
            if (wA < -SampleWeightsEpsilon || wB < -SampleWeightsEpsilon || wA + wB > 1f + SampleWeightsEpsilon)
                continue;

            height = samples[0]
                + wA * (samples[1 + d] - samples[0])
                + wB * (samples[1 + ((d + 1) % 6)] - samples[0]);
            return true;
        }

        // Only reachable on a fan seam under float error — the center sample is the safe pick.
        height = samples[0];
        return true;
    }

    /// 3D world height of the terrain surface at a world position: the smooth interpolated
    /// surface when the hex carries height samples, otherwise the stepped level height.
    public float GetWorldHeightAt(Vector2 worldPos)
        => TryGetSurfaceHeightAt(worldPos, out float surfaceY) ? surfaceY : GetElevationAt(worldPos) * WorldHeightPerLevel;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // --- TableBinderComponent signal handlers (wired in game.tscn) ---

    private void OnMapConfigRow()
    {
        var cfg = (MapConfig)_mapConfigBinder.LastRow!;
        _outerRadius = cfg.HexOuterRadius;
        _chunkHexRadius = cfg.ChunkHexRadius;
        _chunkCols = cfg.ChunkCols;
        _chunkRows = cfg.ChunkRows;
        UpdateCornerOffsets();
    }

    private void OnBuildingTileRowInserted() => SetTile((BuildingTile)_buildingTilesBinder.LastRow!);

    private void OnBuildingTileRowUpdated() => SetTile((BuildingTile)_buildingTilesBinder.LastRow!);

    private void SetTile(BuildingTile tile)
    {
        _elevationByHex[(tile.HexQ, tile.HexR)] = tile.Elevation;
        if (tile.Heights is { Count: 7 })
        {
            var samples = new int[7];
            for (int i = 0; i < 7; i++) samples[i] = tile.Heights[i];
            _heightsByHex[(tile.HexQ, tile.HexR)] = samples;
        }
        else
        {
            _heightsByHex.Remove((tile.HexQ, tile.HexR));
        }
        ElevationChanged?.Invoke();
    }

    private void OnBuildingTileRowDeleted()
    {
        var tile = (BuildingTile)_buildingTilesBinder.LastDeletedRow!;
        bool elevationRemoved = _elevationByHex.Remove((tile.HexQ, tile.HexR));
        bool heightsRemoved = _heightsByHex.Remove((tile.HexQ, tile.HexR));
        if (elevationRemoved || heightsRemoved)
            ElevationChanged?.Invoke();
    }
}
