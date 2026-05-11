using Godot;

[GlobalClass]
public partial class ResDropFood : ResDrop
{
    public override void DropCollected(Player player)
    {
        player.Health += 20;
    }
}
