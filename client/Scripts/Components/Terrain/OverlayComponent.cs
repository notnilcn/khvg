#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Overlay wedge layer of a TileComponent (z=1): same (TriIndex, Rotation, TextureId) batches as
/// the ground layer, but each instance is scaled around its wedge's own centroid (not the
/// hex-center local origin the mesh is built around), so a smaller overlay stays visually
/// centered in its triangle instead of shrinking toward one corner.
/// </summary>
public partial class OverlayComponent : TerrainLayerComponent
{
    public void SetInstances(Dictionary<(int TriIndex, int Rotation, string TextureId), List<(Vector2 Position, float Scale)>> grouped)
    {
        var current = new List<string>(grouped.Count);
        foreach (var (key, positions) in grouped)
        {
            var (mesh, texture) = Terrain.GetWedgeMeshAndTexture(key.TriIndex, key.Rotation, key.TextureId);
            var leafKey = $"{key.TriIndex}/{key.Rotation}/{key.TextureId}";
            var multimesh = GetOrCreateLeaf(leafKey, mesh, texture).Multimesh!;
            multimesh.InstanceCount = positions.Count;
            var centroid = Terrain.GetWedgeCentroid(key.TriIndex);
            for (var i = 0; i < positions.Count; i++)
            {
                var (position, scale) = positions[i];
                multimesh.SetInstanceTransform2D(i, new Transform2D(0f, new Vector2(scale, scale), 0f, position - scale * centroid));
            }
            MarkActive(leafKey);
            current.Add(leafKey);
        }
        FinishPass(current);
    }
}
