using Godot;

[GlobalClass]
public partial class ResEnemy : Resource
{
    [Export] public string EnemyName { get; set; } = "";
    [Export] public Texture2D Sprite { get; set; }
    [Export] public float Radius { get; set; } = 7.0f;

    [Export] public int Health { get; set; } = 10;
    [Export] public int Speed { get; set; } = 50;
    [Export] public int Damage { get; set; } = 5;

    [Export] public float BulletDamage { get; set; } = 0.0f;
    [Export] public float BulletSpeed { get; set; } = 150.0f;
    [Export] public float BulletFireInterval { get; set; } = 2.0f;
}
