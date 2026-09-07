#nullable enable
using Godot;
using PhantomCamera;
// Do NOT try to combine World3DComponent with Camera2DPresenterComponent. They are separate for a reason, and trying to merge them will cause more problems than it solves.

// 3D presenter for the shared CameraRigComponent. Reads the rig each frame and drives the
// PhantomCamera3D spring arm (rotation + length). Holds no camera state and no input — all of
// that lives in the rig, which is in the main scene tree rather than this SubViewport (input
// events, especially mouse motion, never reach a node inside the SubViewport). Also owns the
// follow-target wiring. Registers with the GameManager entity through the SubViewport
// ancestor walk, so the rig is a sibling component. The PhantomCamera3D's collision_mask is
// pinned to 0 in the scene — its third-person SpringArm3D defaults to mask 1, and the MST
// terrain chunks put their trimesh collision on layer 1 (among others), so with collision on
// the arm would shorten itself against the hexes and fight the arcball pitch/zoom.
public partial class World3DComponent : Node3DComponent
{
    [Export] public float DefaultModelScale = 50f;

    // Ortho frustum height per unit of rig zoom distance: 2*tan(75deg/2) keeps the
    // framing identical to the old perspective setup (fov 75) at the same arm
    // length, so scroll-zoom feels unchanged. Camera3D ignores Size in perspective
    // mode, so driving it unconditionally is a no-op if projection ever goes back.
    private const float OrthoSizePerDistance = 1.5347f;

    private PhantomCamera3D? _pcam3D;
    private Camera3D? _camera3D;
    private CameraRigComponent? _rig;

    protected override void OnRegistered()
    {
        var pcam3Dnode = GetNode<Node3D>("PhantomCamera3D");
        _pcam3D = pcam3Dnode.AsPhantomCamera3D();
        _camera3D = GetNode<Camera3D>("Camera3D");
    }

    protected override void OnEntityReady()
    {
        _rig = GetSibling<CameraRigComponent>();
    }

    public override void _Process(double delta)
    {
        if (_pcam3D == null || _camera3D == null || _rig == null || LocalPlayer.Local == null) return;
        // 2D RotationOffset is clockwise in a y-down space; 3D yaw about +Y is counter-clockwise,
        // so negate. This sign convention is stated once, here, and nowhere else.
        _pcam3D.SetThirdPersonRotationDegrees(new Vector3(_rig.PitchDegrees, -Mathf.RadToDeg(_rig.Yaw), 0f));
        _pcam3D.SpringLength = _rig.Distance;
        // Dolly zoom does nothing to an ortho frustum, so zoom drives Size instead.
        _camera3D.Size = _rig.Distance * OrthoSizePerDistance;
    }

    public void SetCameraFollowTarget(Node3D model)
    {
        if (_pcam3D != null) _pcam3D.FollowTarget = model;
    }
    public void ClearCameraFollowTarget() => _pcam3D?.Node3D.Call("erase_follow_target");

    /// Intersects the cursor ray with the ground plane at height groundY, returned in
    /// world 2D coordinates ((x, z) → (x, y) — the inverse of CharacterModel3D's mapping).
    /// Null when the ray can't hit the plane (camera looking at/above the horizon). The
    /// root window's canvas-space mouse position is mapped into this SubViewport through
    /// the stretched full-rect container (input never reaches the SubViewport itself).
    public Vector2? CursorGroundIntersection(float groundY)
    {
        if (_camera3D == null) return null;
        if (GetViewport() is not SubViewport viewport) return null;
        if (viewport.GetParent() is not SubViewportContainer container) return null;
        var rect = container.GetRect();
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f) return null;

        var mouseRoot = GetTree().Root.GetMousePosition();
        var mouseSub = (mouseRoot - rect.Position) * ((Vector2)viewport.Size / rect.Size);

        var from = _camera3D.ProjectRayOrigin(mouseSub);
        var dir = _camera3D.ProjectRayNormal(mouseSub);
        if (dir.Y >= -0.0001f) return null; // parallel to the ground or looking up
        float t = (groundY - from.Y) / dir.Y;
        if (t < 0f) return null; // plane behind the camera
        var hit = from + dir * t;
        return new Vector2(hit.X, hit.Z);
    }
}
