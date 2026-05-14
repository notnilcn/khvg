using Godot;

[GlobalClass]
public partial class ResPlayer : Resource
{
    [Export] public string PlayerName { get; set; } = "";
    [Export] public string ModelName { get; set; } = "Knight";

    [Export] public float MaxHealth { get; set; } = 100.0f;
    [Export] public float MaxSpeed { get; set; } = 100.0f;
    [Export] public ResItem ResWeapon { get; set; }

    public override bool _Set(StringName property, Variant value)
    {
        switch (property.ToString())
        {
            case "player_name":  PlayerName = value.AsString();        return true;
            case "model_name":   ModelName  = value.AsString();         return true;
            case "max_health":   MaxHealth  = value.AsSingle();        return true;
            case "max_speed":    MaxSpeed   = value.AsSingle();        return true;
            case "res_weapon":   ResWeapon  = value.As<ResItem>();     return true;
        }
        return base._Set(property, value);
    }
}
