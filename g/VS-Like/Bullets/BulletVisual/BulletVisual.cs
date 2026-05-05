using Godot;
/*
```c# */
public partial class BulletVisual : Node2D
{
    private const float Radius = 16.0f;
    private static readonly Color BulletColor = new(1.0f, 0.85f, 0.1f);

    public override void _Draw()
    {
        GD.Print($"[BulletVisual] _Draw at GlobalPosition={GlobalPosition}");
        DrawCircle(Vector2.Zero, Radius, BulletColor);
    }

    public override void _Ready()
    {
        QueueRedraw();
    }
}
/*
```
*/
