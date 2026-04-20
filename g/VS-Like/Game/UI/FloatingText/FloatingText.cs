using Godot;
using Godot.Collections;

public partial class FloatingText : Label
{
    [Export] public Dictionary<string, Color> Colors { get; set; } = new();

    public int FloatingNumber { get; set; } = 55;

    public override bool _Set(StringName property, Variant value)
    {
        if (property.ToString() == "colors")
        {
            Colors = value.As<Dictionary<string, Color>>();
            return true;
        }
        return base._Set(property, value);
    }

    public override async void _Ready()
    {
        _SetColor();
        Text = FloatingNumber.ToString();

        var tween = GetTree().CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "scale", new Vector2(2.0f, 2.0f), 0.3);
        tween.TweenProperty(this, "scale", new Vector2(1.0f, 1.0f), 0.3);
        await ToSignal(tween, Tween.SignalName.Finished);
        QueueFree();
    }

    private void _SetColor()
    {
        if (Colors == null || Colors.Count == 0)
            return;
        if (FloatingNumber >= 18)
            AddThemeColorOverride("font_color", Colors["Very High"]);
        else if (FloatingNumber >= 14)
            AddThemeColorOverride("font_color", Colors["High"]);
        else
            AddThemeColorOverride("font_color", Colors["Normal"]);
    }
}
