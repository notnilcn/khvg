using Godot;
using PhantomCamera;

public partial class CameraController2D : Node2D
{
	[Export] public float YawSpeed             = 90f;
    [Export] public float MouseRotateSensitivity = 0.005f;
	[Export] public float MaxPhantomCameraPriority = 60f; // 60 because it's a highly composite number.
    public static CameraController2D Instance { get; private set; }
    private PhantomCamera2D _pcam2D;
    private bool _isSnapping = false;
    private float _snapDist;
    private bool _isMousePosEast;
    public override void _Ready()
    {
        Instance = this;
    }

    public void RegisterCamera(Node2D pcamNode)
    {
        _pcam2D = pcamNode.AsPhantomCamera2D();
        GD.Print($"CameraController2D: registered PhantomCamera2D. Offset: {_pcam2D?.RotationOffset}. Zoom: {_pcam2D?.Zoom}");
    }

    public void UnregisterCamera()
    {
        _pcam2D = null;
    }
	public float GetRotationOffset() => _pcam2D?.RotationOffset ?? 0f;

	public override void _Process(double delta)
    {
        if (_pcam2D == null) return;

        if (Input.IsActionPressed("Camera_Left"))
		{
			_pcam2D.RotationOffset -= Mathf.DegToRad(YawSpeed * (float)delta);
		}
        if (Input.IsActionPressed("Camera_Right"))
		{
			_pcam2D.RotationOffset += Mathf.DegToRad(YawSpeed * (float)delta);
		}
    }

	public override void _Input(InputEvent e)
    {
        if (_pcam2D == null || LocalPlayer.Local == null) return;

        if (Input.IsActionJustPressed("Zoom_In"))
            // _zoom = Mathf.Clamp(_zoom - 20f, ZoomMin, ZoomMax);
            _pcam2D.Zoom *= 1.1f;
        if (Input.IsActionJustPressed("Zoom_Out"))
            // _zoom = Mathf.Clamp(_zoom + 20f, ZoomMin, ZoomMax);
            _pcam2D.Zoom *= 0.9f;

        if (Input.IsActionJustPressed("Reset_Camera_Yaw"))
        {
            GD.Print($"Camera rotation reset. Previous offset: {_pcam2D.RotationOffset}. Zoom: {_pcam2D.Zoom}");
            _pcam2D.RotationOffset = 0f;
        }

        var playerCanvasPosition = LocalPlayer.Local.GetGlobalTransformWithCanvas().Origin;
        var mousePosition = GetViewport().GetMousePosition();
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