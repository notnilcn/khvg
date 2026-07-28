#nullable enable
using Godot;
using PhantomCamera;
// Do NOT try to combine World3DComponent with Camera2DPresenterComponent. They are separate for a reason, and trying to merge them will cause more problems than it solves.

// 3D presenter for the shared CameraRigComponent. Reads the rig each frame and drives the
// PhantomCamera3D spring arm (rotation + length). Holds no camera state and no input — all of
// that lives in the rig, which is in the main scene tree rather than this SubViewport (input
// events, especially mouse motion, never reach a node inside the SubViewport). Also owns the
// follow-target wiring. Registers with the GameManager entity through the SubViewport
// ancestor walk, so the rig is a sibling component.
public partial class World3DComponent : Node3DComponent
{
    [Export] public float DefaultModelScale = 50f;

    private PhantomCamera3D? _pcam3D;
    private CameraRigComponent? _rig;

    protected override void OnRegistered()
    {
        var pcam3Dnode = GetNode<Node3D>("PhantomCamera3D");
        _pcam3D = pcam3Dnode.AsPhantomCamera3D();
    }

    protected override void OnEntityReady()
    {
        _rig = GetSibling<CameraRigComponent>();
    }

    public override void _Process(double delta)
    {
        if (_pcam3D == null || _rig == null || LocalPlayer.Local == null) return;
        // 2D RotationOffset is clockwise in a y-down space; 3D yaw about +Y is counter-clockwise,
        // so negate. This sign convention is stated once, here, and nowhere else.
        _pcam3D.SetThirdPersonRotationDegrees(new Vector3(_rig.PitchDegrees, -Mathf.RadToDeg(_rig.Yaw), 0f));
        _pcam3D.SpringLength = _rig.Distance;
    }

    public void SetCameraFollowTarget(Node3D model)
    {
        if (_pcam3D != null) _pcam3D.FollowTarget = model;
    }
    public void ClearCameraFollowTarget() => _pcam3D?.Node3D.Call("erase_follow_target");
}
