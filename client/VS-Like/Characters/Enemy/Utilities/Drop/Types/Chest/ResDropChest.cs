using Godot;

[GlobalClass]
public partial class ResDropChest : ResDrop
{
    public override void DropCollected(Player player)
    {
        player.ChestUi.OpenChest();
    }
}
