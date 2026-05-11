using Godot;

[GlobalClass]
public partial class ResDropPoint : ResDrop
{
    public override void DropCollected(Player player)
    {
        float gain = 10f * player.ExtraExp;
        player.Points += gain;
        SpacetimeDBManager.Instance?.Conn?.Reducers.GainXp((uint)Mathf.Max(1f, Mathf.RoundToInt(gain)));
    }
}
