using Godot;

public partial class BulletFactory : Node
{
    public override void _Ready()
    {
        Global.BulletFactory = GetParent();
    }
}