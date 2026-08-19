#nullable enable
using Godot;

/// <summary>
/// Lerps the owning entity's GlobalPosition/Rotation toward server-supplied targets —
/// the shared home of the lerp logic RemotePlayer and Enemy used to carry inline.
/// The torus wrap is handled by picking the nearest wrapped candidate of the target
/// each frame, so routine wraps lerp smoothly without ever snapping. RemotePlayer
/// additionally passes the server's reported velocity (reconstructed from
/// movement_direction + movement_speed; zero when idle), and each frame the target is
/// advanced by velocity * time-since-last-update — dead reckoning (velocity
/// extrapolation between snapshots) blended with the lerp. Screen rotation arrives
/// separately via SetScreenRotationTarget (it lives in its own server table on a faster
/// cadence). RemotePlayer also sets WrapSnapThreshold (hard correction on real
/// desync); Enemy uses position targets only.
/// </summary>
public partial class InterpolationComponent : Component
{
    /// Lerp weight per second (multiplied by delta) for position and rotation.
    [Export] public float LerpSpeed { get; set; } = 10f;

    /// If &gt; 0, SetTarget snaps the entity instantly when even the nearest wrapped copy
    /// of the target is farther than this — a real desync (lag/teleport), not a routine
    /// wrap (which the per-frame candidate pick already handles smoothly).
    [Export] public float WrapSnapThreshold { get; set; } = 0f;

    /// True while the entity is more than 1 unit from its current target (drives Walk/Idle).
    public bool Moving { get; private set; }

    private Vector2 canonicalPosition;
    private Vector2 snapVelocity;
    private float timeSinceSnap;
    private float targetScreenRotation;

    /// The entity root this component moves (the IEntity ancestor cast to Node2D).
    private Node2D? EntityNode => Entity as Node2D;

    protected override void OnRegistered()
    {
        if (EntityNode is not { } node) return;
        canonicalPosition = node.GlobalPosition;
        targetScreenRotation = node.Rotation;
    }

    /// Sets a new position target; rotation target and extrapolation velocity are unchanged.
    public void SetTarget(Vector2 position) => SetTarget(position, Vector2.Zero);

    /// Sets a new position target, optionally with the server's reported velocity
    /// (used to extrapolate the target between updates). Screen rotation is set
    /// separately via SetScreenRotationTarget — it arrives from its own table on a
    /// faster cadence.
    public void SetTarget(Vector2 position, Vector2 velocity)
    {
        canonicalPosition = position;
        snapVelocity = velocity;
        timeSinceSnap = 0f;

        if (WrapSnapThreshold <= 0f || EntityNode is not { } node) return;
        var nearest = TorusMath.NearestCandidate(canonicalPosition, node.GlobalPosition, GameManager.LapQ, GameManager.LapR);
        if (node.GlobalPosition.DistanceSquaredTo(nearest) > WrapSnapThreshold * WrapSnapThreshold)
            node.GlobalPosition = nearest;
    }

    /// Sets a new screen-rotation target; position target and extrapolation velocity
    /// are unchanged.
    public void SetScreenRotationTarget(float screenRotation)
    {
        targetScreenRotation = screenRotation;
    }

    /// Hard-sets both the entity position and the interpolation target (initial placement).
    public void SnapTo(Vector2 position)
    {
        canonicalPosition = position;
        snapVelocity = Vector2.Zero;
        timeSinceSnap = 0f;
        if (EntityNode is { } node)
            node.GlobalPosition = TorusMath.NearestCandidate(position, node.GlobalPosition, GameManager.LapQ, GameManager.LapR);
    }

    public override void _Process(double delta)
    {
        if (EntityNode is not { } node) return;
        timeSinceSnap += (float)delta;
        var canonicalExtrapolated = canonicalPosition + snapVelocity * timeSinceSnap;
        var nearest = TorusMath.NearestCandidate(canonicalExtrapolated, node.GlobalPosition, GameManager.LapQ, GameManager.LapR);
        Moving = node.GlobalPosition.DistanceSquaredTo(nearest) > 1f;
        node.GlobalPosition = node.GlobalPosition.Lerp(nearest, LerpSpeed * (float)delta);
        node.Rotation = Mathf.LerpAngle(node.Rotation, targetScreenRotation, LerpSpeed * (float)delta);
    }
}
