using Godot;

[GlobalClass]
public partial class ResEnemy : Resource
{
    [Export] public string EnemyName { get; set; } = "";
    [Export] public Texture2D Sprite { get; set; }
    [Export] public float Radius { get; set; } = 7.0f;

    [Export] public int Health { get; set; } = 10;
    [Export] public int Speed { get; set; } = 50;
    [Export] public int Damage { get; set; } = 5;

    [Export] public float BulletDamage { get; set; } = 0.0f;
    [Export] public float BulletSpeed { get; set; } = 150.0f;
    [Export] public float BulletFireInterval { get; set; } = 2.0f;

    public override bool _Set(StringName property, Variant value)
    {
        switch (property.ToString())
        {
            case "enemy_name":            EnemyName           = value.AsString();       return true;
            case "sprite":                Sprite              = value.As<Texture2D>();  return true;
            case "radius":                Radius              = value.AsSingle();       return true;
            case "health":                Health              = value.AsInt32();        return true;
            case "speed":                 Speed               = value.AsInt32();        return true;
            case "damage":                Damage              = value.AsInt32();        return true;
            case "bullet_damage":         BulletDamage        = value.AsSingle();       return true;
            case "bullet_speed":          BulletSpeed         = value.AsSingle();       return true;
            case "bullet_fire_interval":  BulletFireInterval  = value.AsSingle();       return true;
        }
        return base._Set(property, value);
    }
}
