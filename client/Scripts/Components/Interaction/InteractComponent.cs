#nullable enable
using Godot;

/// <summary>
/// Interaction zone for Chest entities: tracks the local player standing inside and
/// toggles the chest's panel when the "interact" action is pressed. The panel closes when
/// the player steps out. The collision shape is declared as a child in chest.tscn (the
/// DamageReceivingComponent pattern — the area itself carries no shape).
/// </summary>
public partial class InteractComponent : AreaComponent
{
    private bool playerInside;

    protected override void OnRegistered()
    {
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    public override void _ExitTree()
    {
        BodyEntered -= OnBodyEntered;
        BodyExited -= OnBodyExited;
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        if (playerInside && Input.IsActionJustPressed("interact"))
            (Entity as Chest)?.TogglePanel();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is LocalPlayer) playerInside = true;
    }

    private void OnBodyExited(Node2D body)
    {
        if (body is not LocalPlayer) return;
        playerInside = false;
        (Entity as Chest)?.ClosePanel();
    }
}
