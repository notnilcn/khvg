#nullable enable
using Godot;

public partial class Drop : Area2D
{
    public ulong DropId { get; set; }

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not LocalPlayer) return;
        var conn = GameManager.Instance.Conn;
        conn?.Reducers.PickupDrop(DropId);
    }

    public override void _ExitTree()
    {
        BodyEntered -= OnBodyEntered;
    }
}
