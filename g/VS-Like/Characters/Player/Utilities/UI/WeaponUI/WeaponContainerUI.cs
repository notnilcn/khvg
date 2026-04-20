using Godot;

public partial class WeaponContainerUI : VBoxContainer
{
    private static readonly PackedScene WeaponImageScene =
        GD.Load<PackedScene>("res://VS-Like/Characters/Player/Utilities/UI/WeaponUI/weapon_image.tscn");

    private HBoxContainer _activesCont;
    private HBoxContainer _passivesCont;

    public override void _Ready()
    {
        _activesCont  = GetNode<HBoxContainer>("ActivesCont");
        _passivesCont = GetNode<HBoxContainer>("PassivesCont");
    }

    public void UpdateEquipment(Player player)
    {
        _Clear(_activesCont);
        _Clear(_passivesCont);

        if (player.EquippedWeapon != null)
            _AddImage(_activesCont, player.EquippedWeapon.Resource);
        if (player.EquippedAbility != null)
            _AddImage(_activesCont, player.EquippedAbility.Resource);

        foreach (var acc in player.EquippedAccessories)
            _AddImage(_passivesCont, acc.Resource);
    }

    private static void _Clear(HBoxContainer container)
    {
        foreach (var child in container.GetChildren())
            child.QueueFree();
    }

    private static void _AddImage(HBoxContainer container, ResItem res)
    {
        var img = WeaponImageScene.Instantiate<WeaponImage>();
        img.Resource = res;
        container.AddChild(img);
    }

    public void AddWeapon(Weapon weapon) { }
    public void RemoveWeapon(Weapon weapon) { }
    public void UpdateWeapon(Weapon weapon) { }
}
