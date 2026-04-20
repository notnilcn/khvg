using Godot;

public partial class Weapon : Node2D
{
    [Signal] public delegate void FireEventHandler();
    [Signal] public delegate void UpgradedEventHandler();

    private ResItem _resource;
    public ResItem Resource
    {
        get => _resource;
        set => _SetResource(value);
    }

    public int UpgradeLevel { get; set; } = 1;
    public float Damage { get; set; }
    public float FireRate { get; set; }
    public bool AttachToRotation { get; set; }

    public Timer FireTimer { get; private set; }
    private AudioStreamWav _audio;
    private AudioStreamPlayer _audioPlayer;

    public bool IsOwnerWeapon { get; set; } = false;
    // True only for the starting weapon — created on all peers by _InitPlayer so _RpcFire is safe to broadcast.
    // Chest-equipped weapons exist only on the owning peer; broadcasting _RpcFire for them breaks the scene path cache.
    public bool IsReplicated  { get; set; } = false;

    public override void _Ready()
    {
        FireTimer = new Timer();
        FireTimer.Autostart = true;
        FireTimer.WaitTime = Resource?.FireRate ?? 1.0f;
        FireTimer.Timeout += _FireWeapon;
        AddChild(FireTimer);

        if (Resource != null && !Resource.IsPassive)
        {
            _audioPlayer = new AudioStreamPlayer();
            _audioPlayer.Stream = Resource.Audio;
            AddChild(_audioPlayer);
        }
    }

    private void _SetResource(ResItem res)
    {
        _resource = res;
        if (res == null) return;
        Damage            = res.Damage;
        FireRate          = res.FireRate;
        AttachToRotation  = res.AttachToRotation;
        if (!res.IsPassive)
            _audio = res.Audio;
    }

    private void _FireWeapon()
    {
        if (!IsOwnerWeapon)
            return;

        if (Resource != null && !Resource.IsPassive && _audioPlayer != null)
        {
            _audioPlayer.PitchScale = (float)GD.RandRange(0.95, 1.05);
            _audioPlayer.Play();
        }

        EmitSignal(SignalName.Fire);

        if (Multiplayer.HasMultiplayerPeer() && IsReplicated)
            Rpc(nameof(_RpcFire));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _RpcFire()
    {
        EmitSignal(SignalName.Fire);
    }

    public void UpgradeWeapon()
    {
        UpgradeLevel++;
        EmitSignal(SignalName.Upgraded);
    }

    public virtual void RemovePassiveEffects(Player player) { }
}
