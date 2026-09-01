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
/// </summary>
public partial class ElevationMapComponent : Component
{
    /// Raised on any elevation row change so TerrainComponent can re-tint.
    public event Action? ElevationChanged;

    /// Same fallback as TerrainComponent before the first MapConfig row arrives.
    private const float DefaultHexOuterRadius = 96.0f;

    private readonly Dictionary<(int Q, int R), int> _elevationByHex = new();
    private float _outerRadius = DefaultHexOuterRadius;

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

    /// Elevation level of one hex; hexes with no row (outside the AOI) read as level 0.
    public int GetElevation(int q, int r) => _elevationByHex.GetValueOrDefault((q, r), 0);

    /// Elevation level of the hex containing a world position.
    public int GetElevationAt(Vector2 worldPos)
    {
        var hex = HexMath.WorldToHex(worldPos.X, worldPos.Y, _outerRadius);
        return GetElevation(hex.X, hex.Y);
    }

    // --- TableBinderComponent signal handlers (wired in game.tscn) ---

    private void OnMapConfigRow()
    {
        _outerRadius = ((MapConfig)_mapConfigBinder.LastRow!).HexOuterRadius;
    }

    private void OnBuildingTileRowInserted() => SetTile((BuildingTile)_buildingTilesBinder.LastRow!);

    private void OnBuildingTileRowUpdated() => SetTile((BuildingTile)_buildingTilesBinder.LastRow!);

    private void SetTile(BuildingTile tile)
    {
        _elevationByHex[(tile.HexQ, tile.HexR)] = tile.Elevation;
        ElevationChanged?.Invoke();
    }

    private void OnBuildingTileRowDeleted()
    {
        var tile = (BuildingTile)_buildingTilesBinder.LastDeletedRow!;
        if (_elevationByHex.Remove((tile.HexQ, tile.HexR)))
            ElevationChanged?.Invoke();
    }
}
