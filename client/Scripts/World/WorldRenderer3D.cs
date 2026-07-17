#nullable enable
using Godot;
using PhantomCamera;
// Do NOT try to combine WorldRenderer3D with CameraController2D. They are separate for a reason, and trying to merge them will cause more problems than it solves.

// 3D presenter for the shared CameraRig. Reads CameraRig each frame and drives the PhantomCamera3D
// spring arm (rotation + length). Holds no camera state and no input — all of that lives in
// CameraRig, which is in the main scene tree rather than this SubViewport (input events, especially
// mouse motion, never reach a node inside the SubViewport). Also owns the follow-target wiring.
public partial class WorldRenderer3D : Node3D
{
    [Export] public float DefaultModelScale = 50f;
    public static WorldRenderer3D? Instance { get; private set; }
    private PhantomCamera3D? _pcam3D;

    public override void _Ready()
    {
        Instance = this;
        var pcam3Dnode = GetNode<Node3D>("PhantomCamera3D");
        _pcam3D = pcam3Dnode.AsPhantomCamera3D();
    }

    public override void _Process(double delta)
    {
        if (_pcam3D == null || CameraRig.Instance == null || LocalPlayer.Local == null) return;
        var rig = CameraRig.Instance;
        // 2D RotationOffset is clockwise in a y-down space; 3D yaw about +Y is counter-clockwise,
        // so negate. This sign convention is stated once, here, and nowhere else.
        _pcam3D.SetThirdPersonRotationDegrees(new Vector3(rig.PitchDegrees, -Mathf.RadToDeg(rig.Yaw), 0f));
        _pcam3D.SpringLength = rig.Distance;
    }

    public void SetCameraFollowTarget(Node3D model)
    {
        if (_pcam3D != null) _pcam3D.FollowTarget = model;
    }
    public void ClearCameraFollowTarget() => _pcam3D?.Node3D.Call("erase_follow_target");

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
}
