using Godot;

public partial class PassiveItemWeapon : Weapon
{
    public override void _Ready()
    {
        base._Ready();
        if (!IsOwnerWeapon)
            return;
        var player = Global.Instance.GetLocalPlayer();
        if (player == null)
            return;
        _Apply(player, 1.0f);
    }

    public override void RemovePassiveEffects(Player player)
    {
        _Apply(player, -1.0f);
    }

    private void _Apply(Player player, float sign)
    {
        player.ExtraSpeed  += Resource.BonusSpeedMult  * sign;
        player.ExtraExp    += Resource.BonusExpMult    * sign;
        player.ExtraDamage += Resource.BonusDamageMult * sign;
        player.MaxHealth   += Resource.BonusMaxHealth  * sign;
        player.Defense     += (int)(Resource.BonusDefense * sign);
    }
}
