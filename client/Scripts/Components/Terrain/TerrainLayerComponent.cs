#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Base for the four terrain layer components inside a TileComponent (Ground/Overlay/
/// DecorShadow/Decor). Owns the layer's pooled MultiMeshInstance2D batch leaves. The leaves are
/// created in code because which (TriIndex, Rotation, TextureId) batches exist is data-driven by
/// the server's terrain rows — everything else about the layer (the node, its ZIndex relative to
/// the other layers) is declared in tile_component.tscn. Leaves that go inactive are zeroed, not
/// freed, so chunk crossings reuse them instead of reallocating every pass.
/// </summary>
public abstract partial class TerrainLayerComponent : Node2DComponent
{
    protected static readonly Color FallbackColor = new(0.2f, 0.5f, 0.2f, 1f);

    private readonly Dictionary<string, MultiMeshInstance2D> _leaves = new();
    private readonly HashSet<string> _activeKeys = new();

    /// The owning terrain system (mesh/texture caches live there, shared across all chunks).
    protected TerrainComponent Terrain => ((TileComponent)Entity!).Terrain;

    protected MultiMeshInstance2D GetOrCreateLeaf(string key, Mesh mesh, Texture2D? texture)
    {
        if (_leaves.TryGetValue(key, out var existing))
            return existing;

        var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, Mesh = mesh };
        var leaf = new MultiMeshInstance2D { Multimesh = multimesh };

        if (texture != null)
            leaf.Texture = texture;
        else
            leaf.Modulate = FallbackColor;

        AddChild(leaf);
        _leaves[key] = leaf;
        return leaf;
    }

    protected void MarkActive(string key) => _activeKeys.Add(key);

    /// Zero out leaves that had instances last pass but none this one, so they stop drawing
    /// stale positions instead of being destroyed and recreated every pass.
    public void FinishPass(ICollection<string> currentKeys)
    {
        _activeKeys.RemoveWhere(key =>
    {
        if (currentKeys.Contains(key)) return false;
        _leaves[key].Multimesh!.InstanceCount = 0;
        return true;
    });
    }

    /// Frees all leaves — used when the hex outer radius changes and every cached mesh is stale.
    public void ClearLeaves()
    {
        foreach (var leaf in _leaves.Values)
            leaf.QueueFree();
        _leaves.Clear();
        _activeKeys.Clear();
    }
}
