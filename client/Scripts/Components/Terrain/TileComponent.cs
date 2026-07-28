#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Per-chunk terrain scaffolding: the entity root of tile_component.tscn, holding the four layer
/// components (Ground/Overlay/DecorShadow/Decor) the server's TriangleTile/HexDecor rows populate.
/// Instances are pooled by the TerrainComponent — one per visible chunk — which is why they are
/// created in code; everything inside the chunk is declared in the scene. Populate() distributes
/// one chunk's batched wedge/decor instances into the layers.
/// </summary>
public partial class TileComponent : Node2D, IEntity
{
    // IEntity — TileComponent needs Node2D, so it implements the interface directly with its own
    // registry; same pattern as LocalPlayer/Enemy/Drop.
    private readonly EntityRegistry componentRegistry = new();
    public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
    public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
    public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
    public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

    /// Owning terrain system; set by TerrainComponent before the instance is added to the tree.
    public TerrainComponent Terrain { get; set; } = null!;

    /// Chunk this instance currently renders; null while parked in the pool.
    public long? ChunkIndex { get; private set; }

    public void Populate(
        long chunkIndex,
        Dictionary<(int TriIndex, int Rotation, string TextureId), List<Vector2>> ground,
        Dictionary<(int TriIndex, int Rotation, string TextureId), List<(Vector2 Position, float Scale)>> overlay,
        Dictionary<string, List<(Vector2 Position, float RotationRadians, int HexR)>> decorShadow,
        Dictionary<string, List<(Vector2 Position, float RotationRadians, int HexR)>> decor)
    {
        ChunkIndex = chunkIndex;
        GetComponent<GroundComponent>()!.SetInstances(ground);
        GetComponent<OverlayComponent>()!.SetInstances(overlay);
        GetComponent<DecorShadowComponent>()!.SetInstances(decorShadow);
        GetComponent<DecorComponent>()!.SetInstances(decor);
    }

    /// Zeroes all layers and parks the instance back in the pool (chunk left the AOI).
    public void Clear()
    {
        ChunkIndex = null;
        GetComponent<GroundComponent>()?.FinishPass(System.Array.Empty<string>());
        GetComponent<OverlayComponent>()?.FinishPass(System.Array.Empty<string>());
        GetComponent<DecorShadowComponent>()?.FinishPass(System.Array.Empty<string>());
        GetComponent<DecorComponent>()?.FinishPass(System.Array.Empty<string>());
    }

    /// Frees every pooled batch leaf — called when the hex outer radius changes and all cached
    /// meshes are stale.
    public void ClearMeshes()
    {
        GetComponent<GroundComponent>()?.ClearLeaves();
        GetComponent<OverlayComponent>()?.ClearLeaves();
        GetComponent<DecorShadowComponent>()?.ClearLeaves();
        GetComponent<DecorComponent>()?.ClearLeaves();
    }
}
