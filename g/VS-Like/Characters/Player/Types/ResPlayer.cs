using Godot;

[GlobalClass]
public partial class ResPlayer : Resource
{
    [Export] public string PlayerName { get; set; } = "";
    [Export] public Texture2D Sprite { get; set; }

    [Export] public float MaxHealth { get; set; } = 100.0f;
    [Export] public float MaxSpeed { get; set; } = 100.0f;
    [Export] public ResItem ResWeapon { get; set; }
}
