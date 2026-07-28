#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Ground wedge layer of a TileComponent (z=0): one MultiMesh batch per
/// (TriIndex, Rotation, TextureId), plain hex-center transforms.
/// </summary>
public partial class GroundComponent : TerrainLayerComponent
{
    public void SetInstances(Dictionary<(int TriIndex, int Rotation, string TextureId), List<Vector2>> grouped)
    {
        var current = new List<string>(grouped.Count);
        foreach (var (key, positions) in grouped)
        {
            var (mesh, texture) = Terrain.GetWedgeMeshAndTexture(key.TriIndex, key.Rotation, key.TextureId);
            var leafKey = $"{key.TriIndex}/{key.Rotation}/{key.TextureId}";
            var multimesh = GetOrCreateLeaf(leafKey, mesh, texture).Multimesh!;
            multimesh.InstanceCount = positions.Count;
            for (var i = 0; i < positions.Count; i++)
                multimesh.SetInstanceTransform2D(i, new Transform2D(0f, positions[i]));
            MarkActive(leafKey);
            current.Add(leafKey);
        }
        FinishPass(current);
    }
}
