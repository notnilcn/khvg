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

    // Bridge snake_case .tres names (GDScript-generated) → PascalCase C# properties.
    // The source generator registers PropertyNames as PascalCase, so snake_case keys from
    // old .tres files never match and fall through to this _Set override.
    public override bool _Set(StringName property, Variant value)
    {
        switch (property.ToString())
        {
            case "weapon_scene":          WeaponScene          = value.As<PackedScene>();    return true;
            case "item_name":             ItemName             = value.AsString();            return true;
            case "item_description":      ItemDescription      = value.AsString();            return true;
            case "sprite":                Sprite               = value.As<Texture2D>();       return true;
            case "audio":                 Audio                = value.As<AudioStreamWav>();  return true;
            case "item_type":             ItemTypeValue        = (ItemType)value.AsInt32();   return true;
            case "is_passive":            IsPassive            = value.AsBool();              return true;
            case "damage":                Damage               = value.AsSingle();            return true;
            case "fire_rate":             FireRate             = value.AsSingle();            return true;
            case "attach_to_rotation":    AttachToRotation     = value.AsBool();              return true;
            case "bonus_speed_mult":      BonusSpeedMult       = value.AsSingle();            return true;
            case "bonus_exp_mult":        BonusExpMult         = value.AsSingle();            return true;
            case "bonus_damage_mult":     BonusDamageMult      = value.AsSingle();            return true;
            case "bonus_defense":         BonusDefense         = value.AsInt32();             return true;
            case "bonus_max_health":      BonusMaxHealth       = value.AsSingle();            return true;
            case "upgrade_description_2": UpgradeDescription2  = value.AsString();            return true;
            case "upgrade_description_3": UpgradeDescription3  = value.AsString();            return true;
        }
        return base._Set(property, value);
    }
}
