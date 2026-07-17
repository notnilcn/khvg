#nullable enable
using Godot;
using System;

public class CharacterModel3D : IDisposable
{
    public Node3D Node;
    private bool _disposed;

    public CharacterModel3D(PackedScene scene, Vector2 pos2D, Node3D parent, float scale = 50f)
    {
        Node = scene.Instantiate<Node3D>();
        Node.Scale = Vector3.One * scale;
        parent.AddChild(Node);
        Node.GlobalPosition = new Vector3(pos2D.X, 0f, pos2D.Y);
    }

    public void SyncFrom2D(Vector2 pos, float yaw2D)
    {
        if (_disposed) return;
        Node.GlobalPosition = new Vector3(pos.X, 0f, pos.Y);
        // 2D rotation is clockwise from screen-up; 3D Y-rotation is counter-clockwise from +Z.
        Node.Rotation = new Vector3(0f, -yaw2D, 0f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(Node))
            Node.QueueFree();
    }
}
