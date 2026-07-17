#nullable enable
using Godot;
using PhantomCamera;

// 2D presenter for the shared CameraRig. Reads CameraRig each frame and writes the local player's
// PhantomCamera2D. Holds no camera state and no input — all of that lives in CameraRig.
//
// Kept deliberately separate from WorldRenderer3D (the 3D presenter); do not merge them.
public partial class CameraController2D : Node2D
{
    public static CameraController2D? Instance { get; private set; }
    private PhantomCamera2D? _localPlayerPCam2D;

    public override void _Ready()
    {
        Instance = this;
    }

    public void RegisterCamera(Node2D pcamNode)
    {
        _localPlayerPCam2D = pcamNode.AsPhantomCamera2D();
        _localPlayerPCam2D.Priority = 60;
        GD.Print($"CameraController2D: registered PhantomCamera2D. Offset: {_localPlayerPCam2D?.RotationOffset}. Zoom: {_localPlayerPCam2D?.Zoom}");
    }

    public void UnregisterCamera()
    {
        if (_localPlayerPCam2D != null) _localPlayerPCam2D.Priority = 0;
        _localPlayerPCam2D = null;
    }

    public override void _Process(double delta)
    {
        if (_localPlayerPCam2D == null || CameraRig.Instance == null) return;
        _localPlayerPCam2D.RotationOffset = CameraRig.Instance.Yaw;
        var zoom = CameraRig.Instance.Zoom2D;
        _localPlayerPCam2D.Zoom = new Vector2(zoom, zoom);
    }
}
