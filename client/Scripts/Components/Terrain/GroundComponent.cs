#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Ground hex layer of a TileComponent (z=0): one MultiMesh batch per
/// (Rotation, TextureId), plain hex-center transforms. Per-instance colors
/// carry the hex's elevation tint (the batch leaves are created with use_colors on).
/// </summary>
public partial class GroundComponent : TerrainLayerComponent
{
    public void SetInstances(Dictionary<(int Rotation, string TextureId), List<(Vector2 Position, Color Tint)>> grouped)
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
                var (position, tint) = positions[i];
                multimesh.SetInstanceTransform2D(i, new Transform2D(0f, position));
                multimesh.SetInstanceColor(i, tint);
            }
            MarkActive(leafKey);
            current.Add(leafKey);
        }
        FinishPass(current);
    }
}
