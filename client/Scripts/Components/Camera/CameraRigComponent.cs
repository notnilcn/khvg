#nullable enable
using Godot;

// Single source of truth for the shared camera: yaw, pitch, and zoom-distance. Both the 2D
// presenter (Camera2DPresenterComponent) and the 3D presenter (World3DComponent) read this every
// frame and write their own PhantomCamera — neither owns state and neither writes back. All
// camera input is handled here.
//
// This lives in the MAIN scene tree (a component of Main, next to DebugOverlay) — deliberately
// NOT inside the 3D SubViewport. A node inside the SubViewport only receives input the
// SubViewportContainer forwards to it, so mouse-motion events never arrive there; that is why the
// old arcball, wired inside World3DComponent, silently did nothing.
public partial class CameraRigComponent : Component
{
    // ── Tuning ──────────────────────────────────────────────────────────────
    [Export] public float YawSpeed = 90f;    // deg/s for Camera_Left/Right
    [Export] public float MouseSens = 0.3f;   // deg per pixel, arcball drag
    [Export] public float MouseRotateSensitivity = 0.005f;// rad per pixel, snap drag
    [Export] public float PitchMin = -90f;
    [Export] public float PitchMax = -10f;
    [Export] public float DefaultPitch = -90f;   // straight-down top-down view
    [Export] public float DistanceMin = 50f;
    [Export] public float DistanceMax = 1200f;
    [Export] public float ZoomStep = 1.1f;   // multiplicative per scroll tick
    [Export] public float ZoomReferenceDistance = 300f;   // Distance that maps to 2D zoom 1.0

    // ── Canonical state ─────────────────────────────────────────────────────
    // Yaw is the one true heading. It IS the 2D PhantomCamera2D.RotationOffset (radians,
    // clockwise in Godot's y-down 2D space). The 3D presenter negates it for +Y rotation.
    public float Yaw { get; private set; }
    public float PitchDegrees { get; private set; }  // 3D spring-arm pitch; unused by 2D
    public float Distance { get; private set; }  // 3D spring_length; drives 2D zoom

    // 2D zoom factor derived from Distance (larger zoom = closer in Godot 2D, so it's inverse).
    public float Zoom2D => ZoomReferenceDistance / Distance;

    private Vector2 _lastMousePos;
    private float _snapDist;
    private bool _isMousePosEast;

    protected override void OnRegistered()
    {
        PitchDegrees = DefaultPitch;
        Distance = ZoomReferenceDistance;
    }

    public override void _Process(double delta)
    {
        if (LocalPlayer.Local == null) return;

        if (Input.IsActionPressed("Camera_Left"))
            Yaw -= Mathf.DegToRad(YawSpeed * (float)delta);
        if (Input.IsActionPressed("Camera_Right"))
            Yaw += Mathf.DegToRad(YawSpeed * (float)delta);
    }

    public override void _Input(InputEvent e)
    {
        if (LocalPlayer.Local == null) return;

        // ── Zoom (drives spring_length in 3D, pcam zoom in 2D) ───────────────
        if (Input.IsActionJustPressed("Zoom_In"))
            Distance = Mathf.Clamp(Distance / ZoomStep, DistanceMin, DistanceMax);
        if (Input.IsActionJustPressed("Zoom_Out"))
            Distance = Mathf.Clamp(Distance * ZoomStep, DistanceMin, DistanceMax);

        // ── Resets ───────────────────────────────────────────────────────────
        if (Input.IsActionJustPressed("Reset_Camera_Yaw"))
            Yaw = 0f;
        if (Input.IsActionJustPressed("Reset_Camera_Pitch"))
            PitchDegrees = DefaultPitch;

        // ── Arcball (hold + drag): yaw + pitch ───────────────────────────────
        if (Input.IsActionJustPressed("Arcball_Camera_Controls"))
        {
            _lastMousePos = GetViewport().GetMousePosition();
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        if (Input.IsActionPressed("Arcball_Camera_Controls") && e is InputEventMouseMotion arcball)
        {
            Yaw += Mathf.DegToRad(arcball.Relative.X * MouseSens);
            PitchDegrees = Mathf.Clamp(PitchDegrees - arcball.Relative.Y * MouseSens, PitchMin, PitchMax);
        }
        if (Input.IsActionJustReleased("Arcball_Camera_Controls"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().WarpMouse(_lastMousePos);
        }

        // ── Cursor-relative snapping (2D-screen oriented, but only touches Yaw) ─
        var playerCanvasPosition = LocalPlayer.Local.GetGlobalTransformWithCanvas().Origin;
        var mousePosition = GetViewport().GetMousePosition();
        var angle = (playerCanvasPosition.AngleToPoint(mousePosition) + Mathf.Pi * 5 / 2) % (Mathf.Pi * 2);
        var center = GetViewport().GetVisibleRect().Size / 2f;
        { // Snap so that cursor is north
            if (Input.IsActionJustPressed("Camera_Snap_So_That_Cursor_Is_North"))
            {
                Yaw += angle;
                _snapDist = Mathf.Max((mousePosition - playerCanvasPosition).Length(), 1f) * Zoom2D;
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
            if (Input.IsActionPressed("Camera_Snap_So_That_Cursor_Is_North") && e is InputEventMouseMotion northDrag)
            {
                Yaw += northDrag.Relative.X * MouseRotateSensitivity;
            }
            if (Input.IsActionJustReleased("Camera_Snap_So_That_Cursor_Is_North"))
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().WarpMouse(center + new Vector2(0f, -_snapDist));
            }
        }

        { // Snap so that cursor is east or west
            if (Input.IsActionJustPressed("Camera_Snap_So_That_Cursor_Is_East_Or_West"))
            {
                _isMousePosEast = angle < Mathf.Pi;
                Yaw += angle + (_isMousePosEast ? -Mathf.Pi / 2 : Mathf.Pi / 2);
                _snapDist = Mathf.Max((mousePosition - playerCanvasPosition).Length(), 1f) * Zoom2D;
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
            if (Input.IsActionPressed("Camera_Snap_So_That_Cursor_Is_East_Or_West") && e is InputEventMouseMotion ewDrag)
            {
                var move = ewDrag.Relative.Y * MouseRotateSensitivity;
                Yaw += _isMousePosEast ? move : -move;
            }
            if (Input.IsActionJustReleased("Camera_Snap_So_That_Cursor_Is_East_Or_West"))
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().WarpMouse(center + new Vector2(_isMousePosEast ? _snapDist : -_snapDist, 0f));
            }
        }
    }
}
