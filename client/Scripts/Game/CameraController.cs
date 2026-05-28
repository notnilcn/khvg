#nullable enable
using Godot;
using PhantomCamera;

public partial class CameraController : Node
{
    private PhantomCamera3D? _cam;

    private float _zoom      = 300f;
    private const float ZoomMin   = 50f;
    private const float ZoomMax   = 1200f;
    private const float ZoomSpeed = 3f;
    private const float MouseSens = 0.3f;
    private const float PitchMin  = -89f;
    private const float PitchMax  = -10f;
    private const float YawSpeed  = 90f;

    public override void _Ready()
    {
        Callable.From(InitCam).CallDeferred();
    }

    private void InitCam()
    {
        var pcamNode = World3d.Instance?.GetNode<Node3D>("PhantomCamera3D");
        if (pcamNode == null) return;
        _cam = pcamNode.AsPhantomCamera3D();
        _cam?.SetThirdPersonRotationDegrees(new Vector3(-90f, 0f, 0f));
    }

    public override void _Input(InputEvent e)
    {
        if (_cam == null) return;

        if (Input.IsActionJustPressed("Zoom_In"))
            _zoom = Mathf.Clamp(_zoom - 20f, ZoomMin, ZoomMax);
        if (Input.IsActionJustPressed("Zoom_Out"))
            _zoom = Mathf.Clamp(_zoom + 20f, ZoomMin, ZoomMax);

        if (Input.IsActionPressed("Arcball_Camera_Controls") && e is InputEventMouseMotion mm)
        {
            var rot = _cam.GetThirdPersonRotationDegrees();
            rot.X -= mm.Relative.Y * MouseSens;
            rot.X  = Mathf.Clamp(rot.X, PitchMin, PitchMax);
            rot.Y -= mm.Relative.X * MouseSens;
            rot.Y  = Mathf.Wrap(rot.Y, 0f, 360f);
            _cam.SetThirdPersonRotationDegrees(rot);
        }

        if (Input.IsActionJustPressed("Switch_To_Top_Down"))
        {
            var rot = _cam.GetThirdPersonRotationDegrees();
            rot.X = -90f;
            _cam.SetThirdPersonRotationDegrees(rot);
        }
    }

    public override void _Process(double delta)
    {
        if (_cam == null) return;

        var next = Mathf.Lerp(_cam.SpringLength, _zoom, ZoomSpeed * (float)delta);
        _cam.SpringLength   = next;
        _cam.FollowDistance = next;

        if (!Input.IsActionPressed("Arcball_Camera_Controls"))
        {
            float yaw = 0f;
            if (Input.IsActionPressed("Camera_Left")) yaw += 1f;
            if (Input.IsActionPressed("Camera_Right")) yaw -= 1f;
            if (yaw != 0f)
            {
                var rot = _cam.GetThirdPersonRotationDegrees();
                rot.Y = Mathf.Wrap(rot.Y + yaw * YawSpeed * (float)delta, 0f, 360f);
                _cam.SetThirdPersonRotationDegrees(rot);
            }
        }
    }
}
