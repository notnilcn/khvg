#nullable enable
using Godot;

public partial class World3d : Node3D
{
    public static World3d? Instance { get; private set; }

    public const float DefaultModelScale = 50f;

    public Node3D Scene3D => this;

    public float CameraYaw => _camera3D?.Rotation.Y ?? 0f;

    private Node3D?   _cameraTarget;
    private Camera3D? _camera3D;
    private LocalPlayer? _followTarget;

    public override void _Ready()
    {
        Instance = this;
        _cameraTarget = GetNode<Node3D>("CameraTarget");
        _camera3D     = GetNode<Camera3D>("Camera3D");
    }

    public override void _Process(double _delta)
    {
        if (_followTarget != null && _cameraTarget != null)
        {
            var p = _followTarget.GlobalPosition;
            _cameraTarget.GlobalPosition = new Vector3(p.X, 0f, p.Y);
        }
    }

    public void SetCameraFollowTarget(LocalPlayer player) => _followTarget = player;
    public void ClearCameraFollowTarget() => _followTarget = null;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
}
