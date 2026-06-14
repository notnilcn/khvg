#nullable enable
using Godot;
using SpacetimeDB;

public partial class Drop : Node2D
{
    private const float PickupLockDuration = 5f;

    [Export] public Area2D PickupArea { get; set; } = null!;

    public ulong DropId { get; set; }
    public string ItemId { get; set; } = "";
    public Identity? DroppedBy { get; set; }

    public bool PickupLocked => pickupLockTimer > 0f;

    private float pickupLockTimer;

    public override void _Ready()
    {
        var item = GameManager.Instance.GetItem(ItemId);
        var resPath = item != null ? GameManager.Instance.GetResPath(item.TextureId) : null;
        var sprite = GetNodeOrNull<Sprite2D>("Texture");
        if (resPath != null && sprite != null)
            sprite.Texture = GD.Load<Texture2D>(resPath);

        if (DroppedBy is { } droppedBy && GameManager.Instance.IsLocal(droppedBy))
            pickupLockTimer = PickupLockDuration;

        PickupArea.BodyEntered += OnBodyEntered;
        PickupArea.BodyExited += OnBodyExited;
    }

    public override void _Process(double delta)
    {
        if (pickupLockTimer > 0f)
            pickupLockTimer -= (float)delta;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not LocalPlayer || PickupLocked) return;
        GameManager.Instance.Conn?.Reducers.PickupDrop(DropId);
    }

    private void OnBodyExited(Node2D body)
    {
    }

    public override void _ExitTree()
    {
        PickupArea.BodyEntered -= OnBodyEntered;
        PickupArea.BodyExited -= OnBodyExited;
    }
}
