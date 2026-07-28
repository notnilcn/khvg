#nullable enable
using Godot;
using PhantomCamera;

// 2D presenter for the shared CameraRigComponent. Reads the rig each frame and writes the local
// player's PhantomCamera2D. Holds no camera state and no input — all of that lives in the rig.
//
// Kept deliberately separate from World3DComponent (the 3D presenter); do not merge them.
public partial class Camera2DPresenterComponent : Node2DComponent
{
    private PhantomCamera2D? _localPlayerPCam2D;
    private CameraRigComponent? _rig;

    protected override void OnEntityReady()
    {
        _rig = GetSibling<CameraRigComponent>();
    }

    public void RegisterCamera(Node2D pcamNode)
    {
        _localPlayerPCam2D = pcamNode.AsPhantomCamera2D();
        _localPlayerPCam2D.Priority = 60;
        GD.Print($"Camera2DPresenterComponent: registered PhantomCamera2D. Offset: {_localPlayerPCam2D?.RotationOffset}. Zoom: {_localPlayerPCam2D?.Zoom}");
    }

    public void UnregisterCamera()
    {
        // The pcam is a child of LocalPlayer and may already be freed by the time this runs on death.
        if (IsInstanceValid(_localPlayerPCam2D?.Node2D)) _localPlayerPCam2D!.Priority = 0;
        _localPlayerPCam2D = null;
    }

    public override void _Process(double delta)
    {
        // _localPlayerPCam2D can be a dangling reference to a freed node (e.g. between LocalPlayer
        // death and the next RegisterCamera); IsInstanceValid guards against ObjectDisposedException.
        if (!IsInstanceValid(_localPlayerPCam2D?.Node2D) || _rig == null) return;
        _localPlayerPCam2D!.RotationOffset = _rig.Yaw;
        var zoom = _rig.Zoom2D;
        _localPlayerPCam2D.Zoom = new Vector2(zoom, zoom);
    }
}
