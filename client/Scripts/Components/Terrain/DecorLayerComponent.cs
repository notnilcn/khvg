#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Shared base for the two decor layers of a TileComponent (DecorShadow at z=2, Decor at z=3):
/// one MultiMesh batch per texture, instances sorted by hex row so nearer (larger world-Y) decor
/// draws after, covering farther decor of the same texture — cross-texture decor sorting (e.g.
/// tree vs. rock) needs a shared atlas per next-steps.md, out of scope here.
/// </summary>
public abstract partial class DecorLayerComponent : TerrainLayerComponent
{
    public void SetInstances(Dictionary<string, List<(Vector2 Position, float RotationRadians, int HexR)>> grouped)
    {
        var current = new List<string>(grouped.Count);
        foreach (var (textureId, instances) in grouped)
        {
            instances.Sort((a, b) => a.HexR.CompareTo(b.HexR));
            var (mesh, texture) = Terrain.GetDecorMeshAndTexture(textureId);
            var multimesh = GetOrCreateLeaf(textureId, mesh, texture).Multimesh!;
            multimesh.InstanceCount = instances.Count;
            for (var i = 0; i < instances.Count; i++)
                multimesh.SetInstanceTransform2D(i, new Transform2D(instances[i].RotationRadians, instances[i].Position));
            MarkActive(textureId);
            current.Add(textureId);
        }
        FinishPass(current);
    }
}
