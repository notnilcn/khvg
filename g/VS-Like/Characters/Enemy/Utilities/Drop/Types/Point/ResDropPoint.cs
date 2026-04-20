using Godot;

[GlobalClass]
public partial class ResDropPoint : ResDrop
{
    public override void DropCollected(Player player)
    {
        player.Points += 10 * player.ExtraExp;
    }
}
