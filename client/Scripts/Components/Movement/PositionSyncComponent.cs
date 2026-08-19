#nullable enable
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Local-player movement sync, both directions. Inbound: the child
/// LocalPlayerPositionBinder (declared in local_player.tscn, signals wired in the editor)
/// delivers LocalPlayerPosition rows — the replayed first row is the initial placement,
/// later updates only hard-correct on real desync. Outbound: ReportMovement fires on a
/// fixed interval (or, when ReportInterval is -1, only on movement-key press/release
/// edges) carrying the *actual* movement as an angle + scalar speed (zero speed when
/// idle — remote puppets reconstruct velocity from the two for dead reckoning, so
/// inventing it from facing caused the constant-drift bug), and ReportScreenRotation
/// fires on its own faster timer whenever facing changes (screen rotation lives in its
/// own server table so camera rotation can stream at a higher cadence).
///
/// Also owns the player's speed controls: increase_move_speed/decrease_move_speed
/// (Ctrl+scroll) adjust a persistent scale in SpeedStep increments (clamped
/// [MinSpeedScale, 1] of the server-resolved BaseSpeed), and self_slow (hold R)
/// multiplies SelfSlowFactor on top. LocalPlayer reads CurrentSpeed each physics frame.
/// </summary>
public partial class PositionSyncComponent : Component
{
    /// If even the nearest wrapped copy of the server's position is farther than this,
    /// it's a real desync (lag/rubber-band/correction), not a routine wrap — hard-correct.
    [Export] public float WrapSnapThreshold { get; set; } = 50f;

    /// Seconds between timed movement reports. Set to -1 for event-driven mode: no
    /// timer, movement is reported only on movement-key press/release edges (and on
    /// speed-scale/self_slow changes) — a held key sends nothing until it's released.
    [Export] public float ReportInterval { get; set; } = 0.1f;

    /// Screen rotation reports on its own faster cadence (camera rotation needs it), and
    /// only when facing actually changed beyond RotationEpsilon.
    [Export] public float RotationReportInterval { get; set; } = 0.033f;
    [Export] public float RotationEpsilon { get; set; } = 0.001f;

    /// Persistent speed-scale step for increase/decrease_move_speed, and its floor.
    [Export] public float SpeedStep { get; set; } = 0.1f;
    [Export] public float MinSpeedScale { get; set; } = 0.1f;

    /// Speed multiplier while self_slow is held (bullet-hell focus mode).
    [Export] public float SelfSlowFactor { get; set; } = 0.4f;

    /// Current movement speed in world units/sec: the server-resolved base speed scaled
    /// by the player's persistent speed setting and the hold-to-slow factor.
    public float CurrentSpeed => BaseSpeed * speedScale * (SlowHeld ? SelfSlowFactor : 1f);

    private TableBinderComponent positionBinder = null!;
    private float reportTimer;
    private float rotationTimer;
    private float lastReportedScreenRotation = float.NaN;
    private float lastMovementDirection;
    private float speedScale = 1f;
    private bool reportNextFrame;

    /// The entity root this component moves (the IEntity ancestor cast to Node2D).
    private Node2D? EntityNode => Entity as Node2D;

    private float BaseSpeed => (Entity as LocalPlayer)?.BaseSpeed ?? 100f;
    private static bool SlowHeld => Input.IsActionPressed("self_slow");

    public override void _Ready()
    {
        base._Ready();
        positionBinder = GetNode<TableBinderComponent>("LocalPlayerPositionBinder");
    }

    // --- TableBinderComponent signal handlers (wired in local_player.tscn) ---
    // The binder has ReplayExistingRows on, so a row already in the client cache comes
    // through the same insert path — no separate Iter() replay needed.

    private void OnPositionRowInserted()
    {
        if (EntityNode is not { } node) return;
        var position = (PlayerPosition)positionBinder.LastRow!;
        node.GlobalPosition = new Vector2(position.X, position.Y);
    }

    private void OnPositionRowUpdated()
    {
        if (EntityNode is not { } node) return;
        // We never force our own GlobalPosition to match the server's wrapped report directly —
        // it stays a continuous, unbounded local reference frame (our own physics already
        // integrate it that way). We only check whether the nearest *wrapped copy* of the
        // server's position is still far from us: if even that's far, it's a real desync
        // (lag/rubber-band/correction), not a routine wrap, and worth a hard correction.
        var row = (PlayerPosition)positionBinder.LastRow!;
        var serverPos = new Vector2(row.X, row.Y);
        var nearest = TorusMath.NearestCandidate(serverPos, node.GlobalPosition, GameManager.LapQ, GameManager.LapR);
        if (node.GlobalPosition.DistanceSquaredTo(nearest) > WrapSnapThreshold * WrapSnapThreshold)
        {
            GD.Print($"[Desync] {node.GlobalPosition} → {nearest} (dist={node.GlobalPosition.DistanceTo(nearest):F1})");
            node.GlobalPosition = nearest;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (EntityNode is not { } node) return;

        // Deferred one frame so node.Velocity already reflects the new speed (LocalPlayer
        // integrates earlier in the frame, before this child runs).
        if (reportNextFrame)
        {
            reportNextFrame = false;
            ReportNow(node);
        }

        // Speed controls: Ctrl+scroll adjusts the persistent scale. A change (or a
        // self_slow edge) triggers an immediate report so remotes see the new velocity
        // without waiting for the 10Hz tick.
        if (Input.IsActionJustPressed("increase_move_speed") && speedScale < 1f)
        {
            speedScale = Mathf.Min(1f, speedScale + SpeedStep);
            reportNextFrame = true;
        }
        if (Input.IsActionJustPressed("decrease_move_speed") && speedScale > MinSpeedScale)
        {
            speedScale = Mathf.Max(MinSpeedScale, speedScale - SpeedStep);
            reportNextFrame = true;
        }
        if (Input.IsActionJustPressed("self_slow") || Input.IsActionJustReleased("self_slow"))
            reportNextFrame = true;

        // Movement-key edges. In event-driven mode (ReportInterval < 0) these are the
        // only movement reports — holding a key sends nothing, pressing and releasing
        // each send exactly one. In timer mode they just make starts/stops/direction
        // changes immediate instead of waiting for the next tick.
        if (Input.IsActionJustPressed("Left") || Input.IsActionJustPressed("Right") ||
            Input.IsActionJustPressed("Up") || Input.IsActionJustPressed("Down") ||
            Input.IsActionJustReleased("Left") || Input.IsActionJustReleased("Right") ||
            Input.IsActionJustReleased("Up") || Input.IsActionJustReleased("Down"))
            reportNextFrame = true;

        reportTimer += (float)delta;
        if (reportTimer >= ReportInterval)
            ReportNow(node);

        rotationTimer += (float)delta;
        if (rotationTimer >= RotationReportInterval)
        {
            rotationTimer = 0f;
            if (float.IsNaN(lastReportedScreenRotation) || Mathf.Abs(node.Rotation - lastReportedScreenRotation) > RotationEpsilon)
            {
                lastReportedScreenRotation = node.Rotation;
                GameManager.Conn?.Reducers.ReportScreenRotation(node.Rotation);
            }
        }
    }

    private void ReportNow(Node2D node)
    {
        reportTimer = 0f;
        var velocity = (node as CharacterBody2D)?.Velocity ?? Vector2.Zero;
        var speed = velocity.Length();
        if (speed > 0.001f)
            lastMovementDirection = velocity.Angle(); // keep last facing direction while idle
        GameManager.Conn?.Reducers.ReportMovement(node.GlobalPosition.X, node.GlobalPosition.Y, lastMovementDirection, speed);
    }
}
