/*
Drop code.
```c# */
using Godot;

public partial class Drop : Area2D
{
    private Sprite2D _sprite2D;
    private AudioStreamPlayer _audioPlayer;

    public bool CanAttract      { get; set; } = true;
    public bool StartAttracting { get; set; } = false;
    private const float AttractSpeed = 300.0f;

    [Export] public ResDrop Resource { get; set; }
/*
```
*/

/*
# `_Ready()`
This is kinda obvious
```c# */
    public override void _Ready()
    {
        _sprite2D   = GetNode<Sprite2D>("Sprite2D");
        _audioPlayer = GetNode<AudioStreamPlayer>("AudioPlayer");

        CanAttract         = Resource.CanAttract;
        _sprite2D.Texture  = Resource.Sprite;
        if (Resource.Audio != null)
            _audioPlayer.Stream = Resource.Audio;
    }
/*
```
*/

/*
# `_Process(delta)`
This is kinda obvious
```c# */
    public override void _Process(double delta)
    {
        if (!StartAttracting)
            return;

        var localPlayer = Global.Instance.GetLocalPlayer();
        if (localPlayer == null)
            return;

        var toPlayer = localPlayer.GlobalPosition - GlobalPosition;
        GlobalPosition += toPlayer.Normalized() * Mathf.Min(AttractSpeed * (float)delta, toPlayer.Length());
    }
/*
```
*/

/*
# `_OnBodyEntered(body)`
This is kinda obvious
```c# */
    private async void _OnBodyEntered(Node2D body)
    {
        if (body is not Player player)
            return;

        if (!player.IsLocalPlayer)
            return;

        Visible = false;
        Resource.DropCollected(player);

        if (Resource.Audio != null)
        {
            _audioPlayer.PitchScale = (float)GD.RandRange(0.95, 1.05);
            _audioPlayer.Play();
            await ToSignal(_audioPlayer, AudioStreamPlayer.SignalName.Finished);
        }

        QueueFree();
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
        QueueFree();
    }
/*
```
*/
}
