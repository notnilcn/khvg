#nullable enable
using Godot;
using SpacetimeDB;

/// <summary>
/// Pickup trigger for Drop entities: when the local player enters the area and the pickup
/// lock has expired, reports the PickupDrop reducer. The collision shape and the
/// pickup-lock Timer (for drops the local player just dropped) are declared as children
/// in pickup_component.tscn; logic moved out of Drop.cs.
/// </summary>
public partial class PickupComponent : AreaComponent
{
    /// The server row id reported to the PickupDrop reducer.
    public ulong DropId { get; private set; }

    /// True while the pickup lock is running (drops the local player just dropped).
    public bool PickupLocked => lockTimer != null && !lockTimer.IsStopped();

    private Timer? lockTimer;

    protected override void OnRegistered()
    {
        lockTimer = GetNode<Timer>("PickupLockTimer");
        BodyEntered += OnBodyEntered;
    }

    public override void _ExitTree()
    {
        BodyEntered -= OnBodyEntered;
        base._ExitTree();
    }

    /// Mirrors the server row: remembers the drop id and starts the pickup lock when the
    /// local player is the one who dropped it.
    public void SetFromServer(ulong dropId, Identity? droppedBy)
    {
        DropId = dropId;
        if (droppedBy is { } by && GameManager.IsLocal(by))
            lockTimer?.Start();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not LocalPlayer || PickupLocked) return;
        GameManager.Conn?.Reducers.PickupDrop(DropId);
    }
}
