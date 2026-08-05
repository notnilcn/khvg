#nullable enable
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Local-player position sync, both directions. Inbound: the child
/// LocalPlayerPositionBinder (declared in local_player.tscn, signals wired in the editor)
/// delivers LocalPlayerPosition rows — the replayed first row is the initial placement,
/// later updates only hard-correct on real desync. Outbound: ReportMovement fires on a
/// fixed interval from _PhysicsProcess (logic moved out of LocalPlayer.cs).
/// </summary>
public partial class PositionSyncComponent : Component
{
    /// If even the nearest wrapped copy of the server's position is farther than this,
    /// it's a real desync (lag/rubber-band/correction), not a routine wrap — hard-correct.
    [Export] public float WrapSnapThreshold { get; set; } = 50f;

    [Export] public float ReportInterval { get; set; } = 0.1f;

    private TableBinderComponent positionBinder = null!;
    private float reportTimer;

    /// The entity root this component moves (the IEntity ancestor cast to Node2D).
    private Node2D? EntityNode => Entity as Node2D;

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
        reportTimer += (float)delta;
        if (reportTimer >= ReportInterval)
        {
            reportTimer = 0f;
            GameManager.Conn?.Reducers.ReportMovement(node.GlobalPosition.X, node.GlobalPosition.Y, node.Rotation);
        }
    }
}
