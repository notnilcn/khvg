using Godot;

[GlobalClass]
public partial class ResItem : Resource
{
    public enum ItemType
    {
        Weapon    = 0,
        Ability   = 1,
        Armor     = 2,
        Accessory = 3,
    }

    [Export] public PackedScene WeaponScene { get; set; }

    [Export] public string ItemName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string ItemDescription { get; set; } = "";
    [Export] public Texture2D Sprite { get; set; }
    [Export] public AudioStreamWav Audio { get; set; }

    [Export] public ItemType ItemTypeValue { get; set; } = ItemType.Weapon;
    [Export] public bool IsPassive { get; set; } = false;

    [Export] public float Damage { get; set; } = 0.0f;
    [Export] public float FireRate { get; set; } = 1.0f;
    [Export] public bool AttachToRotation { get; set; } = false;

    [Export] public float BonusSpeedMult { get; set; } = 0.0f;
    [Export] public float BonusExpMult { get; set; } = 0.0f;
    [Export] public float BonusDamageMult { get; set; } = 0.0f;
    [Export] public int BonusDefense { get; set; } = 0;
    [Export] public float BonusMaxHealth { get; set; } = 0.0f;

    [Export(PropertyHint.MultilineText)] public string UpgradeDescription2 { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string UpgradeDescription3 { get; set; } = "";
}
