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

    /// Shallow-pitch under-world fix, mode pick. False (default): the ortho
    /// window slides up via Camera3D.VOffset so its bottom edge clears the
    /// surface — the player drifts lower in the frame the shallower the pitch
    /// ("see more ahead"). True: the ortho Size shrinks instead so the bottom
    /// edge clears the surface with the player kept dead-center — the world
    /// zooms in as the pitch drops (at pitch −10 / distance 300 / pivot 80 the
    /// Size fits to ~260 instead of 460; at pivot level 0, ~98).
    [Export] public bool AutoZoomAtShallowPitch { get; set; } = false;

    // Ortho frustum height per unit of rig zoom distance: 2*tan(75deg/2) keeps the
    // framing identical to the old perspective setup (fov 75) at the same arm
    // length, so scroll-zoom feels unchanged. Camera3D ignores Size in perspective
    // mode, so driving it unconditionally is a no-op if projection ever goes back.
    private const float OrthoSizePerDistance = 1.5347f;

    // Clearance the frustum's bottom edge keeps above the world surface after
    // the shallow-pitch compensation below (VOffset window shift, or the
    // auto-zoom Size fit): clears the water's WaterLevel 0.9 plus its 0.15
    // wave crests with headroom, so bottom-edge rays always land on
    // tiles/terrain instead of passing under them into the void.
    private const float SurfaceClearance = 4f;

    private PhantomCamera3D? _pcam3D;
    private Camera3D? _camera3D;
    private CameraRigComponent? _rig;

    protected override void OnRegistered()
    {
        var pcam3Dnode = GetNode<Node3D>("PhantomCamera3D");
        _pcam3D = pcam3Dnode.AsPhantomCamera3D();
        _camera3D = GetNode<Camera3D>("Camera3D");
        ApplySeaSkyGround();
    }

    /// Below-horizon sky hemisphere reads as open sea. WaterComponent3D's AbyssOcean
    /// plane catches the below-world rays that shallow ortho pitches produce, so this
    /// is a pure fallback: it only shows if a ray ever slips past every water plane
    /// (e.g. camera rig exports widened past the bounds derived on AbyssDrop).
    /// Set in code (not only in world_3d.tscn, which declares the same values):
    /// the scene loader was observed applying ground_horizon_color while leaving
    /// ground_bottom_color at its default, so this enforces both every run.
    private void ApplySeaSkyGround()
    {
        var env = GetNode<WorldEnvironment>("WorldEnvironment");
        if (env.Environment?.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
        {
            sky.GroundBottomColor = new Color(0.03f, 0.12f, 0.22f);
            sky.GroundHorizonColor = new Color(0.08f, 0.25f, 0.45f);
        }
        else GD.PushWarning("World3DComponent: WorldEnvironment sky material is not a ProceduralSkyMaterial — below-horizon rays keep the default ground gradient.");
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
        // Lift the orbit pivot above the follow target's origin (the model's feet) by the
        // rig's fixed height offset — e.g. to orbit around the torso instead of the ground.
        _pcam3D.FollowOffset = new Vector3(0f, _rig.PivotHeightOffset, 0f);
        // Dolly zoom does nothing to an ortho frustum, so zoom drives Size instead.
        _camera3D.Size = _rig.Distance * OrthoSizePerDistance;
        // A shallow ortho pitch slides the bottom of the frustum under the world
        // surface: the bottom-edge ray at the pivot depth sits at pivotY +
        // Distance·sin(pitch) − Size/2·cos(pitch), which dips below y=0 below
        // ~34° at default zoom (any pitch/zoom at pivot level 0). Those
        // under-world rays hit nothing and render the sky's ground half as the
        // flat "clipping" band along the bottom of shallow views. Two fixes,
        // picked by AutoZoomAtShallowPitch — both verified against the ortho
        // camera's real behavior (FrustumOffset is perspective-only and does
        // not move ortho rays; VOffset shifts the window along camera-up for
        // every ray):
        // - default — VOffset slides the window up until the bottom edge
        //   clears the surface. Trade: the player sits lower in the frame the
        //   shallower the pitch (bounded — worst case ≈ 265 px of 720 at pitch
        //   −10, distance 1200, pivot level 0), the classic ortho "see more
        //   ahead". VOffset is in camera-up units whose world-Y component is
        //   cos(pitch), hence the divisor.
        // - AutoZoomAtShallowPitch — shrink Size instead, keeping the player
        //   centered while the world zooms in as the pitch drops. The fit
        //   solves bottom ≥ SurfaceClearance for Size; the 1u floor guards a
        //   negative fit if PitchMax is ever widened past −10° (sinθ < sin10°
        //   can under-run the clearance at close zoom).
        float pitchRad = -Mathf.DegToRad(_rig.PitchDegrees); // positive-down
        float cosPitch = Mathf.Cos(pitchRad);
        float pivotY = (_pcam3D.FollowTarget?.GlobalPosition.Y ?? 0f) + _pcam3D.FollowOffset.Y;
        if (AutoZoomAtShallowPitch)
        {
            float fitted = 2f * (_rig.Distance * Mathf.Sin(pitchRad) + pivotY - SurfaceClearance) / Mathf.Max(cosPitch, 0.05f);
            _camera3D.Size = Mathf.Max(Mathf.Min(_rig.Distance * OrthoSizePerDistance, fitted), 1f);
            _camera3D.VOffset = 0f;
        }
        else
        {
            _camera3D.Size = _rig.Distance * OrthoSizePerDistance;
            float need = _camera3D.Size * 0.5f * cosPitch - _rig.Distance * Mathf.Sin(pitchRad) - pivotY + SurfaceClearance;
            _camera3D.VOffset = need > 0f ? need / Mathf.Max(cosPitch, 0.05f) : 0f;
        }
    }

    /// Subtree character models are parented under so the PixelizerApplier3D there
    /// pixelizes them (declared under PixelizerManager3D in world_3d.tscn). Falls
    /// back to null when the applier is absent (defensive; the component still
    /// renders un-pixelized).
    public Node3D? CharacterRoot => GetNodeOrNull<Node3D>("PixelizerManager3D/CharactersPixelApplier");

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

    // MST chunk StaticBody3D collision layers (the addon sets collision_layer = 17: layers 1
    // and 5). Water/sky/backing planes and the visual-only chunk aprons carry no colliders,
    // so a layer-filtered downward ray hits terrain or nothing.
    private const uint TerrainCollisionMask = 17;
    // The ray spans every possible terrain height (the elevation field tops out at
    // ELEVATION_LEVELS × WORLD_HEIGHT_PER_LEVEL; unclamped stored heights sit far below this).
    private const float SurfaceRayTopY = 20000f;
    private const float SurfaceRayBottomY = -20000f;

    /// World-space Y of the rendered terrain surface under a canonical world-2D position:
    /// a downward ray against the MST chunk trimesh collision — the same geometry the
    /// pixelizer demo's CharacterBody3D slides on, smooth slopes and walls included. Null
    /// when no terrain collider is there yet (chunks still building) — callers fall back to
    /// the per-hex height. Pass a canonical (lap-wrapped) position: only the canonical lap's
    /// chunks carry collision, the aprons are visual-only.
    public float? SurfaceHeightAt(Vector2 world2D)
    {
        var from = new Vector3(world2D.X, SurfaceRayTopY, world2D.Y);
        var to = new Vector3(world2D.X, SurfaceRayBottomY, world2D.Y);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(from, to, TerrainCollisionMask));
        if (hit.Count == 0) return null;
        return ((Vector3)hit["position"]).Y;
    }
}
