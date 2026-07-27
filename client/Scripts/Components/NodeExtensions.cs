#nullable enable
using Godot;

public static class NodeExtensions
{
    /// <summary>
    /// First ancestor (walking GetParent()) of type T, or null. Unlike Node.Owner this works
    /// for nodes inside an instanced sub-scene that need a node from the parent scene.
    /// </summary>
    public static T? GetAncestor<T>(this Node node) where T : Node
    {
        for (var current = node.GetParent(); current != null; current = current.GetParent())
            if (current is T match)
                return match;
        return null;
    }
}
