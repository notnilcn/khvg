#nullable enable
using Godot;
using System;

public class CharacterModel3D : IDisposable
{
    private readonly Node3D _node;
    private bool _disposed;

    public CharacterModel3D(PackedScene scene, Vector2 pos2D, Node3D parent, float scale = World3d.DefaultModelScale)
    {
        _node = scene.Instantiate<Node3D>();
        _node.Scale = Vector3.One * scale;
        parent.AddChild(_node);
        _node.GlobalPosition = new Vector3(pos2D.X, 0f, pos2D.Y);
    }

    public void SyncFrom2D(Vector2 pos, float yaw2D)
    {
        if (_disposed) return;
        _node.GlobalPosition = new Vector3(pos.X, 0f, pos.Y);
        // 2D rotation is clockwise from screen-up; 3D Y-rotation is counter-clockwise from +Z.
        _node.Rotation = new Vector3(0f, -yaw2D, 0f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_node))
            _node.QueueFree();
    }
}
