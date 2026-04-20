using Godot;

[GlobalClass]
public partial class ResDrop : Resource
{
    [Export] public Texture2D Sprite { get; set; }
    [Export] public AudioStreamWav Audio { get; set; }
    [Export] public bool CanAttract { get; set; } = false;

    public virtual void DropCollected(Player player) { }

    public override bool _Set(StringName property, Variant value)
    {
        switch (property.ToString())
        {
            case "sprite":       Sprite      = value.As<Texture2D>();      return true;
            case "audio":        Audio       = value.As<AudioStreamWav>(); return true;
            case "can_attract":  CanAttract  = value.AsBool();             return true;
        }
        return base._Set(property, value);
    }
}
