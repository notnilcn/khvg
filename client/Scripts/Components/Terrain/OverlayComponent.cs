#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Overlay hex layer of a TileComponent (z=1): same (Rotation, TextureId) batches as
/// the ground layer, but each instance is scaled around the hex center the mesh is built
/// around, so a smaller overlay stays centered in its hex. Per-instance colors carry the same
/// elevation tint as the ground layer.
/// </summary>
public partial class OverlayComponent : TerrainLayerComponent
{
    public void SetInstances(Dictionary<(int Rotation, string TextureId), List<(Vector2 Position, float Scale, Color Tint)>> grouped)
    {
        var current = new List<string>(grouped.Count);
        foreach (var (key, positions) in grouped)
        {
            var (mesh, texture) = Terrain.GetHexMeshAndTexture(key.Rotation, key.TextureId);
            var leafKey = $"{key.Rotation}/{key.TextureId}";
            var multimesh = GetOrCreateLeaf(leafKey, mesh, texture, useColors: true).Multimesh!;
            multimesh.InstanceCount = positions.Count;
            for (var i = 0; i < positions.Count; i++)
            {
                var (position, scale, tint) = positions[i];
                multimesh.SetInstanceTransform2D(i, new Transform2D(0f, new Vector2(scale, scale), 0f, position));
                multimesh.SetInstanceColor(i, tint);
            }
            MarkActive(leafKey);
            current.Add(leafKey);
        }
        FinishPass(current);
    }
}
