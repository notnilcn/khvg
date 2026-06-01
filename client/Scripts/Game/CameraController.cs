#nullable enable
using Godot;
using PhantomCamera;

public partial class CameraController : Node
{
    // private PhantomCamera3D? _cam;

    private PhantomCamera2D? _cam2D;

    private float _zoom           = 300f;
    private const float ZoomDefault  = 300f;
    private const float ZoomMin      = 50f;
    private const float ZoomMax      = 1200f;
    private const float ZoomStep     = 20f;
    private const float ZoomSpeed    = 3f;
    private const float MouseSens    = 0.3f;
    private const float PitchMin     = -89f;
    private const float PitchMax     = -10f;
    private const float YawSpeed     = 90f;
    // public static float CameraYaw { get; set; }

    public override void _Ready()
    {
        Callable.From(InitCam).CallDeferred();
    }

    private void InitCam()
    {
        _cam2D = LocalPlayer.Local?.GetNode<PhantomCamera2D>("PhantomCamera2D");
        // var pcamNode = World3D.Instance?.GetNode<Node3D>("PhantomCamera3D");
        // if (pcamNode == null) return;
        // _cam = pcamNode.AsPhantomCamera3D();
        // _cam?.SetThirdPersonRotationDegrees(new Vector3(-90f, 0f, 0f));
    }

    public override void _Input(InputEvent e)
    {
        // if (Input.IsActionJustPressed("Zoom_In"))
        //     _zoom = Mathf.Clamp(_zoom - ZoomStep, ZoomMin, ZoomMax);
        // if (Input.IsActionJustPressed("Zoom_Out"))
        //     _zoom = Mathf.Clamp(_zoom + ZoomStep, ZoomMin, ZoomMax);

        // if (_cam == null) return;

        // if (Input.IsActionPressed("Arcball_Camera_Controls") && e is InputEventMouseMotion mm)
        // {
        //     var rot = _cam.GetThirdPersonRotationDegrees();
        //     rot.X -= mm.Relative.Y * MouseSens;
        //     rot.X  = Mathf.Clamp(rot.X, PitchMin, PitchMax);
        //     rot.Y -= mm.Relative.X * MouseSens;
        //     rot.Y  = Mathf.Wrap(rot.Y, 0f, 360f);
        //     _cam.SetThirdPersonRotationDegrees(rot);
        //     CameraYaw = Mathf.DegToRad(rot.Y);
        // }

        // if (Input.IsActionJustPressed("Switch_To_Top_Down"))
        // {
        //     var rot = _cam.GetThirdPersonRotationDegrees();
        //     rot.X = -90f;
        //     _cam.SetThirdPersonRotationDegrees(rot);
        // }
    }

    public override void _Process(double delta)
    {
        // 2D zoom: Camera2D.Zoom is inversely proportional to _zoom (spring length).
        // At _zoom=ZoomDefault the 2D view is 1:1; zooming in shrinks _zoom and enlarges the 2D view.
        // var cam2D = LocalPlayer.Local?.GetNodeOrNull<Camera2D>("Camera2D");
        // if (cam2D != null)
        // {
        //     float target2D = ZoomDefault / _zoom;
        //     float cur2D    = Mathf.Lerp(cam2D.Zoom.X, target2D, ZoomSpeed * (float)delta);
        //     cam2D.Zoom     = new Vector2(cur2D, cur2D);
        // }

        // if (_cam == null) return;

        // var next = Mathf.Lerp(_cam.SpringLength, _zoom, ZoomSpeed * (float)delta);
        // _cam.SpringLength   = next;
        // _cam.FollowDistance = next;

        // if (!Input.IsActionPressed("Arcball_Camera_Controls"))
        // {
        //     float yaw = 0f;
        //     if (Input.IsActionPressed("Camera_Left")) yaw += 1f;
        //     if (Input.IsActionPressed("Camera_Right")) yaw -= 1f;
        //     if (yaw != 0f)
        //     {
        //         var rot = _cam.GetThirdPersonRotationDegrees();
        //         rot.Y = Mathf.Wrap(rot.Y + yaw * YawSpeed * (float)delta, 0f, 360f);
        //         _cam.SetThirdPersonRotationDegrees(rot);
        //         LocalPlayer.CameraYaw = Mathf.DegToRad(rot.Y);
        //     }
        // }
        
        // if (_cam2D == null) return;
        // if (Input.IsActionPressed("Camera_Left"))
        // {
        //     GD.Print("Camera Left");
        //     _cam2D.RotationOffset -= Mathf.DegToRad(YawSpeed * (float)delta);
        // }
        // if (Input.IsActionPressed("Camera_Right"))
        // {
        //     GD.Print("Camera Right");
        //     _cam2D.RotationOffset += Mathf.DegToRad(YawSpeed * (float)delta);
        // }
    }
}
