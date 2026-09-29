#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Per-hex heights, child of the game root (declared inline in game.tscn with signals wired
/// in the editor): mirrors the AOI-filtered NearbyBuildingTiles rows — exactly one per hex,
/// world-unit height stamped by world generation (unclamped; this is the MarchingSquares addon's
/// height_map value, passed straight to chunk.draw_height) — into a (q, r) → height dictionary.
/// The hex outer radius comes from a child MapConfig binder (the HexGridOverlayComponent
/// pattern); the heights come from a child NearbyBuildingTiles binder (replay on — persistent
/// view table). Everything height/elevation-aware reads through this component or the
/// GameManager.ElevationMap facade.
///
/// Gameplay still thinks in discrete elevation levels: GetElevation derives
/// floor(height / WorldHeightPerLevel), lockstep with the server's
/// world::reducers::height_to_level. GetWorldHeightAt returns the raw stored height — the
/// cell top characters and cursor raycasts stand on (the addon's merge ramps are visual only).
/// Top-level reads the derived level (bullet bands, walkability, tint).
/// </summary>
public partial class ElevationMapComponent : Component
{
    /// Raised on any height row change so TerrainComponent can re-tint and TerrainComponent3D
    /// can re-push cell heights.
    public event Action? ElevationChanged;

    /// Same fallback as TerrainComponent before the first MapConfig row arrives.
    private const float DefaultHexOuterRadius = 96.0f;

    /// 3D world height (Y units) per elevation level. Client-only visual mapping — the 3D
    /// terrain cells are drawn at the stored raw heights, and levels are derived for gameplay.
    /// Lockstep with the server's main::global::WORLD_HEIGHT_PER_LEVEL.
    public const float WorldHeightPerLevel = 16f;

    private readonly Dictionary<(int Q, int R), int> _heightByHex = new();
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
    }

    /// Raw world-unit height of one hex; hexes with no row (outside the AOI) read as 0.
    public int GetHeight(int q, int r) => _heightByHex.GetValueOrDefault((q, r), 0);

    /// Elevation level derived from the stored height: floor(height / WorldHeightPerLevel).
    /// Lockstep with the server's world::reducers::height_to_level.
    public int GetElevation(int q, int r)
        => Mathf.FloorToInt(GetHeight(q, r) / WorldHeightPerLevel);

    /// Raw height of the hex containing a world position. The position is a node/global
    /// position that can drift up to a lap from canonical, so it is wrapped (lap vectors from
    /// MapConfig) before the hex lookup — the height keys are server-canonical.
    public int GetHeightAt(Vector2 worldPos)
    {
        var canonical = TorusMath.WrapPosition(worldPos, _chunkHexRadius, _chunkCols, _chunkRows, _outerRadius, GameManager.LapQ, GameManager.LapR);
        var hex = HexMath.WorldToHex(canonical.X, canonical.Y, _outerRadius);
        return GetHeight(hex.X, hex.Y);
    }

    /// Elevation level of the hex containing a world position (the wrapped lookup above).
    public int GetElevationAt(Vector2 worldPos)
    {
        var canonical = TorusMath.WrapPosition(worldPos, _chunkHexRadius, _chunkCols, _chunkRows, _outerRadius, GameManager.LapQ, GameManager.LapR);
        var hex = HexMath.WorldToHex(canonical.X, canonical.Y, _outerRadius);
        return GetElevation(hex.X, hex.Y);
    }

    /// 3D world height of the terrain surface at a world position: the hex's stored height.
    public float GetWorldHeightAt(Vector2 worldPos) => GetHeightAt(worldPos);

    // --- TableBinderComponent signal handlers (wired in game.tscn) ---

    private void OnMapConfigRow()
    {
        var cfg = (MapConfig)_mapConfigBinder.LastRow!;
        _outerRadius = cfg.HexOuterRadius;
        _chunkHexRadius = cfg.ChunkHexRadius;
        _chunkCols = cfg.ChunkCols;
        _chunkRows = cfg.ChunkRows;
    }

    private void OnBuildingTileRowInserted() => SetTile((BuildingTile)_buildingTilesBinder.LastRow!);

    private void OnBuildingTileRowUpdated() => SetTile((BuildingTile)_buildingTilesBinder.LastRow!);

    private void SetTile(BuildingTile tile)
    {
        _heightByHex[(tile.HexQ, tile.HexR)] = tile.Height;
        ElevationChanged?.Invoke();
    }

    private void OnBuildingTileRowDeleted()
    {
        var tile = (BuildingTile)_buildingTilesBinder.LastDeletedRow!;
        if (_heightByHex.Remove((tile.HexQ, tile.HexR)))
            ElevationChanged?.Invoke();
    }
}
