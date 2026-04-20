using Godot;

public partial class Hurtbox : Area2D
{
    private Timer _timer;
    private AudioStreamPlayer _audioPlayer;

    public override void _Ready()
    {
        _timer       = GetNode<Timer>("Timer");
        _audioPlayer = GetNode<AudioStreamPlayer>("AudioPlayer");
    }

    private void _OnBodyEntered(Node2D body)
    {
        if (body is not Enemy enemy)
            return;

        if (!Multiplayer.IsServer())
            return;

        _HurtOwnerPlayer(enemy.Damage);
        _timer.Start();
    }

    private void _OnTimerTimeout()
    {
        if (!Multiplayer.IsServer())
            return;

        var overlapping = GetOverlappingBodies();
        if (overlapping.Count == 0)
            return;

        foreach (var body in overlapping)
        {
            if (body is Enemy enemy)
                _HurtOwnerPlayer(enemy.Damage);
        }
        _timer.Start();
    }

    private void _HurtOwnerPlayer(float dmg)
    {
        var player = _GetOwnerPlayer();
        if (player == null)
            return;

        _audioPlayer.Play();
        player.Rpc(nameof(Player.Hurt), dmg);
    }

    private Player _GetOwnerPlayer()
    {
        Node node = this;
        while (node != null)
        {
            if (node is Player p) return p;
            node = node.GetParent();
        }
        return null;
    }
}
