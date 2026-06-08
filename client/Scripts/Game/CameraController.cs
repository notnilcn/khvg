#nullable enable
using Godot;
using PhantomCamera;

public partial class CameraController : Node
{
    [Export] public float YawSpeed             = 90f;
    [Export] public float MouseRotateSensitivity = 0.005f;
    public static CameraController? Instance { get; private set; }
    private Node2D?   _cameraTarget;
    private PhantomCamera2D? _pcam2D;
    private LocalPlayer? _followTarget;
    private bool _isSnapping = false;
    private float _snapDist;
    private bool _isMousePosEast;
    public override void _Ready()
    {
        Instance = this;
        var pcamNode = GetParent().GetNode<Node2D>("PhantomCamera2D");
        _cameraTarget = GetParent().GetNode<Node2D>("CameraTarget");
        _pcam2D = pcamNode.AsPhantomCamera2D();
    }

    public void SetCameraFollowTarget(LocalPlayer player) => _followTarget = player;
    public void ClearCameraFollowTarget() => _followTarget = null;

    public float GetRotationOffset() => _pcam2D?.RotationOffset ?? 0f;

    public override void _Process(double delta)
    {
        if (_pcam2D == null) return;
        if (_followTarget != null && _cameraTarget != null)
        {
            var p = _followTarget.GlobalPosition;
            _cameraTarget.GlobalPosition = new Vector2(p.X, p.Y);
        }

        if (Input.IsActionPressed("Camera_Left"))
            _pcam2D.RotationOffset -= Mathf.DegToRad(YawSpeed * (float)delta);
        if (Input.IsActionPressed("Camera_Right"))
            _pcam2D.RotationOffset += Mathf.DegToRad(YawSpeed * (float)delta);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Input(InputEvent e)
    {
        if (_pcam2D == null || LocalPlayer.Local == null) return;

        if (Input.IsActionJustPressed("Reset_Camera"))
        {
            GD.Print($"Camera rotation reset. Previous offset: {_pcam2D.RotationOffset}");
            _pcam2D.RotationOffset = 0f;
        }

        var playerCanvasPosition = LocalPlayer.Local.GetGlobalTransformWithCanvas().Origin;
        var mousePosition = LocalPlayer.Local.GetViewport().GetMousePosition();
        var angle = ( playerCanvasPosition.AngleToPoint(mousePosition) + Mathf.Pi*5/2) % (Mathf.Pi * 2);
        var center = GetViewport().GetVisibleRect().Size / 2f;

        { // Camera snapping north logic
            if (Input.IsActionJustPressed("Camera_Snap_So_That_Cursor_Is_North"))
            {
                var currentoffset = _pcam2D.RotationOffset;
                _pcam2D.RotationOffset += angle;
                
                _snapDist = Mathf.Max((mousePosition - LocalPlayer.Local.GetGlobalTransformWithCanvas().Origin).Length(), 1f) * _pcam2D.Zoom.Y;
                // GD.Print($"Snapping camera so that cursor is north. Angle to cursor: {angle}. Current offset: {currentoffset}. New offset: {_pcam2D.RotationOffset}. Original Cursor Pos: {LocalPlayer.Local.mousePosition}. Dist: {dist}. New Cursor Pos: {center + new Vector2(0f, -dist)}");
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }

            if (Input.IsActionPressed("Camera_Snap_So_That_Cursor_Is_North") && e is InputEventMouseMotion mm)
            {
                var move = mm.Relative.X * MouseRotateSensitivity;
                _pcam2D.RotationOffset += move;
            }

            if (Input.IsActionJustReleased("Camera_Snap_So_That_Cursor_Is_North"))
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().WarpMouse(center + new Vector2(0f, -_snapDist));
            }
        }

        { // Camera snapping east west logic
            if (Input.IsActionJustPressed("Camera_Snap_So_That_Cursor_Is_East_Or_West"))
            {
                _isMousePosEast = angle < Mathf.Pi;
                var currentoffset = _pcam2D.RotationOffset;
                _pcam2D.RotationOffset += angle + (_isMousePosEast ? -Mathf.Pi/2 : Mathf.Pi/2);
                
                _snapDist = Mathf.Max((mousePosition - LocalPlayer.Local.GetGlobalTransformWithCanvas().Origin).Length(), 1f) * _pcam2D.Zoom.Y;
                // GD.Print($"Snapping camera so that cursor is north. Angle to cursor: {angle}. Current offset: {currentoffset}. New offset: {_pcam2D.RotationOffset}. Original Cursor Pos: {LocalPlayer.Local.mousePosition}. Dist: {dist}. New Cursor Pos: {center + new Vector2(0f, -dist)}");
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }

            if (Input.IsActionPressed("Camera_Snap_So_That_Cursor_Is_East_Or_West") && e is InputEventMouseMotion mm)
            {
                var move = mm.Relative.Y * MouseRotateSensitivity;
                _pcam2D.RotationOffset += _isMousePosEast ? move : -move;
            }

            if (Input.IsActionJustReleased("Camera_Snap_So_That_Cursor_Is_East_Or_West"))
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().WarpMouse(center + new Vector2(_isMousePosEast ? _snapDist : -_snapDist, 0f));
            }
        }

        if (Input.IsActionJustPressed("Send_Debug_Message"))
        {
            // _pcam2D.RotationOffset += Mathf.DegToRad(45f);
            GD.Print($"Offset: {_pcam2D.RotationOffset}\nMouse pos: {mousePosition}\nPlayer pos: {playerCanvasPosition}\nangle: {(playerCanvasPosition.AngleToPoint(mousePosition) + Mathf.Pi*5/2) % (Mathf.Pi * 2)}");
        }
    }
}
