/*
CollisionObject code.
```c# */
using Godot;

public partial class CollisionObject : StaticBody2D
{
    private const int MinRadius = 350;

    private Timer _timer;

/*
```
*/

/*
# `_Ready()`
This is kinda obvious
```c# */
    public override void _Ready()
    {
        _timer = GetNode<Timer>("Timer");
    }
/*
```
*/

/*
# `_RepositionObject()`
This is kinda obvious
```c# */
    public void RepositionObject()
    {
        GlobalPosition = _GetRandomSpawnPos(Global.Instance.GetLocalPlayer().GlobalPosition);
    }
/*
```
*/

/*
# `_GetRandomSpawnPos(globalPos)`
This is kinda obvious
```c# */
    private static Vector2 _GetRandomSpawnPos(Vector2 globalPos)
    {
        float angle    = GD.Randf() * Mathf.Tau;
        float distance = MinRadius + GD.Randf() * 100;
        return globalPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }
/*
```
*/

/*
# `_OnVisibleOnScreenNotifier2DScreenEntered`
This is kinda obvious
```c# */
    private void _OnVisibleOnScreenNotifier2DScreenEntered()
    {
        if (!_timer.IsStopped())
            _timer.Stop();
    }
/*
```
*/

/*
# `_OnVisibleOnScreenNotifier2DScreenExited`
This is kinda obvious
```c# */
    private void _OnVisibleOnScreenNotifier2DScreenExited()
    {
        _timer.Start();
    }
/*
```
*/

/*
# `_OnTimerTimeout()`
This is kinda obvious
```c# */
    private void _OnTimerTimeout()
    {
        RepositionObject();
    }
/*
```
*/

}
