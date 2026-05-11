using Godot;

public partial class DropsDetection : Area2D
{
    private void _OnAreaEntered(Area2D area)
    {
        if (area is Drop drop && drop.CanAttract)
            drop.StartAttracting = true;
    }
}
