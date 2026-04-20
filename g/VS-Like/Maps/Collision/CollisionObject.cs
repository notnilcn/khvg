using Godot;

public partial class CollisionObject : StaticBody2D
{
    private const int MinRadius = 350;

    private Timer _timer;

    public override void _Ready()
    {
        _timer = GetNode<Timer>("Timer");
    }

    public void RepositionObject()
    {
        GlobalPosition = _GetRandomSpawnPos(Global.Instance.GetLocalPlayer().GlobalPosition);
    }

    private static Vector2 _GetRandomSpawnPos(Vector2 globalPos)
    {
        float angle    = GD.Randf() * Mathf.Tau;
        float distance = MinRadius + GD.Randf() * 100;
        return globalPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }

    private void _OnVisibleOnScreenNotifier2DScreenEntered()
    {
        if (!_timer.IsStopped())
            _timer.Stop();
    }

    private void _OnVisibleOnScreenNotifier2DScreenExited()
    {
        _timer.Start();
    }

    private void _OnTimerTimeout()
    {
        RepositionObject();
    }
}
