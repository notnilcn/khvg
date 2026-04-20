using Godot;

[GlobalClass]
public partial class ResDrop : Resource
{
    [Export] public Texture2D Sprite { get; set; }
    [Export] public AudioStreamWav Audio { get; set; }
    [Export] public bool CanAttract { get; set; } = false;

    public virtual void DropCollected(Player player) { }
}
