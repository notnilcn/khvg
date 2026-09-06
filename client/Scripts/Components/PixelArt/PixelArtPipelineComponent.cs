#nullable enable
using Godot;
using System;

/// <summary>
/// 3D pixel-art pipeline driver (3dpixelart addon), declared in world_3d.tscn. Creates
/// the GDScript PixelArtPipeline3D node (untyped interop — same caveat as
/// BulletSpawnerComponent: no compile-time checking, typos surface at runtime) and pushes
/// every export into it: palette LUT, banded cloud shadows, and the god-ray compositor
/// pass. Also owns the visible sky-cloud sheet (fed the same noise/scale/wind/threshold/
/// bands, so the clouds overhead match the shadows and rays) and per-object pixelization
/// via PixelizeObject — CharacterModel3D calls it for every 3D character model, and
/// PixelizeRoots lets the editor pixelize whole declared subtrees.
///
/// Cloud shadows are not limited to pixelized materials: the shared MST terrain material
/// and the water material are registered with the pipeline's register_cloud_material, so
/// the same sun-projected, optionally banded coverage darkens the ground and the ocean
/// (their shaders implement the coverage themselves — pixelized characters get it from
/// the addon's pixel-art material).
///
/// Requires the Forward+ or Mobile renderer (the addon is RenderingDevice compute); when
/// the RenderingDevice is unavailable (Compatibility/web) the pipeline degrades to a
/// warning and the game renders normally. The plugin's PixelArtCameraSnap is deliberately
/// not used: it only helps orthographic cameras and warns on perspective ones.
/// </summary>
public partial class PixelArtPipelineComponent : Component
{
    private const string PipelineScriptPath = "res://addons/3dpixelart/nodes/pixel_art_pipeline_3d.gd";
    private const string PixelArtObjectScriptPath = "res://addons/3dpixelart/nodes/pixel_art_object_3d.gd";
    private const string SkyCloudsShaderPath = "res://Assets/Shaders/sky_clouds.gdshader";

    /// The one pipeline in the world_3d scene; null in 2D mode (world_3d never loads).
    public static PixelArtPipelineComponent? Instance { get; private set; }

    [ExportGroup("Pipeline")]
    [Export] public bool PipelineEnabled { get; set; } = true;
    [Export] public Camera3D? Camera { get; set; }
    [Export] public DirectionalLight3D? Sun { get; set; }
    /// Palette LUT baked by the addon's PixelPalette resource; null = no grading.
    [Export] public Texture2D? GlobalPaletteLut { get; set; }
    /// 0 = off, 1 = anchor map, 2 = metadata buffer, 3 = metadata depth.
    [Export(PropertyHint.Range, "0,3")] public int DebugView { get; set; } = 0;

    [ExportGroup("Pixelization")]
    /// CharacterModel3D routes every 3D character model through PixelizeObject.
    [Export] public bool PixelizeCharacters { get; set; } = true;
    /// Whole subtrees to pixelize on ready (each MeshInstance3D inside gets the
    /// pixel-art material + a PixelArtObject3D config node).
    [Export] public Godot.Collections.Array<NodePath> PixelizeRoots { get; set; } = new();
    /// Macro-pixel size in screen pixels (1-5).
    [Export(PropertyHint.Range, "1,5")] public int PixelSize { get; set; } = 3;
    /// Outline color; alpha is the outline opacity.
    [Export] public Color OutlineColor { get; set; } = Colors.Black;

    [ExportGroup("Clouds")]
    [Export] public bool CloudsEnabled { get; set; } = true;
    /// Coverage noise; null generates the addon demo's default (Perlin 0.02, seed 7).
    [Export] public Texture2D? CloudNoise { get; set; }
    /// Height of the imaginary cloud plane the shadows/rays project from; also the
    /// height of the visible sky-cloud sheet. Kept above the camera's max height so
    /// the sheet is never between the camera and the gameplay.
    [Export] public float CloudHeight { get; set; } = 400f;
    /// World-units-per-noise-UV scale. The generated noise has ~5 features per texture
    /// period, so 0.0015 gives ~130-unit cloud blobs (several hexes across). The old
    /// 0.015 default made ~13-unit confetti that read as white noise over the map.
    [Export] public float CloudNoiseScale { get; set; } = 0.0015f;
    /// Noise value above which a cloud fully blocks the sun (shadow cores, sky-sheet
    /// blobs, god-ray gaps). The shadow penumbra ramps up to this from
    /// CloudThreshold - CloudShadowSoftness.
    [Export(PropertyHint.Range, "0,1")] public float CloudThreshold { get; set; } = 0.75f;
    /// Noise quantization for the sky sheet and god rays (≤1 disables). The ground
    /// shadows use CloudShadowLevels instead.
    [Export] public float CloudBands { get; set; } = 3f;
    [Export] public Vector2 CloudWind { get; set; } = new(0.01f, 0.004f);
    [Export] public float CloudPlaneSize { get; set; } = 3000f;
    [Export] public Color CloudColor { get; set; } = Colors.White;

    [ExportGroup("Cloud Shadows")]
    /// How dark the ground gets under a full cloud (0 = no shadow, 1 = black).
    [Export(PropertyHint.Range, "0,1")] public float CloudShadowStrength { get; set; } = 0.55f;
    /// Multi-level banded shadows (hard contour rings) vs smooth soft shadows.
    [Export] public bool CloudShadowBandingEnabled { get; set; } = true;
    /// Darkness levels when banding: 1 = one hard shadow, 2-4 = contour rings.
    [Export(PropertyHint.Range, "1,8")] public int CloudShadowLevels { get; set; } = 3;
    /// Width of the penumbra below CloudThreshold the shadow ramps across
    /// (0 = hard edge, 1 = spans the whole noise range). Banded mode distributes
    /// its levels over this range.
    [Export(PropertyHint.Range, "0,1")] public float CloudShadowSoftness { get; set; } = 0.35f;
    /// Darken the MST terrain under clouds (its shader is not pixelized, so the
    /// pipeline feeds it the same cloud uniforms via register_cloud_material).
    [Export] public bool CloudShadowsOnTerrain { get; set; } = true;
    /// Darken the water under clouds (same registration as the terrain).
    [Export] public bool CloudShadowsOnWater { get; set; } = true;
    /// Node holding the MarchingSquaresTerrain script; its shared terrain_material
    /// receives the cloud uniforms.
    [Export] public NodePath? TerrainMaterialNode { get; set; } = "../TerrainComponent3D/MarchingSquaresTerrain";
    /// WaterComponent3D node; its shared water material receives the cloud uniforms.
    [Export] public NodePath? WaterMaterialNode { get; set; } = "../WaterComponent3D";

    [ExportGroup("God Rays")]
    [Export] public bool GodRaysEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "4,64")] public int RaySteps { get; set; } = 24;
    [Export] public float RayMaxDistance { get; set; } = 600f;
    [Export] public float RayIntensity { get; set; } = 0.5f;
    [Export] public float RayDecay { get; set; } = 2f;
    /// Stepped banding of the rays (pixel look); ≤1 disables quantization.
    [Export] public float RayQuantizeBands { get; set; } = 4f;
    [Export(PropertyHint.Range, "0,1")] public float RayDustStrength { get; set; } = 0.15f;

    private Node? _pipeline; // GDScript PixelArtPipeline3D (untyped interop)
    private Texture2D? _cloudNoise; // export or the generated default
    private MeshInstance3D? _skyClouds;
    private bool _godRayTuningApplied;
    private bool _terrainCloudsRegistered;
    private bool _waterCloudsRegistered;
    private int _nextOutlineId = 1;

    protected override void OnRegistered()
    {
        Instance = this;
        if (!PipelineEnabled) return;
        if (RenderingServer.GetRenderingDevice() == null)
        {
            GD.PushWarning("PixelArtPipelineComponent: RenderingDevice unavailable (Compatibility renderer) — the 3D pixel-art pipeline needs Forward+ or Mobile; rendering unmodified.");
            return;
        }

        _cloudNoise = CloudNoise ?? GenerateNoiseTexture(256, 0.02f, 7);

        _pipeline = (Node)GD.Load<GDScript>(PipelineScriptPath).New();
        _pipeline.Name = "PixelArtPipeline3D";
        // PhantomCameraHost updates the Camera3D at process_priority 300 — the pipeline
        // must sync its metadata camera AFTER that, or the metadata viewport renders
        // from last frame's camera whenever the camera chases the player, and moving
        // objects dissolve into their anchor-dot grid (stale anchor map + depth).
        _pipeline.ProcessPriority = 301;
        _pipeline.Set("camera", Camera);
        _pipeline.Set("global_palette_lut", GlobalPaletteLut);
        _pipeline.Set("debug_view", DebugView);
        _pipeline.Set("clouds_enabled", CloudsEnabled);
        _pipeline.Set("cloud_noise", _cloudNoise);
        _pipeline.Set("cloud_sun", Sun);
        _pipeline.Set("cloud_height", CloudHeight);
        _pipeline.Set("cloud_noise_scale", CloudNoiseScale);
        _pipeline.Set("cloud_threshold", CloudThreshold);
        _pipeline.Set("cloud_bands", CloudBands);
        _pipeline.Set("cloud_wind", CloudWind);
        _pipeline.Set("cloud_shadow_strength", CloudShadowStrength);
        _pipeline.Set("cloud_shadow_banding_enabled", CloudShadowBandingEnabled);
        _pipeline.Set("cloud_shadow_levels", (float)CloudShadowLevels);
        _pipeline.Set("cloud_shadow_softness", CloudShadowSoftness);
        _pipeline.Set("god_rays_enabled", GodRaysEnabled);
        AddChild(_pipeline);

        RegisterCloudMaterials();
        if (CloudsEnabled) CreateSkyClouds();
        foreach (var path in PixelizeRoots)
            if (GetNodeOrNull<Node3D>(path) is { } root)
                PixelizeObject(root);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        base._ExitTree();
    }

    public override void _Process(double _delta)
    {
        if (_pipeline == null) return;

        // The water material is built in WaterComponent3D._Ready, which runs after
        // our OnRegistered (tree order), so registration retries until it lands.
        if (!_terrainCloudsRegistered || !_waterCloudsRegistered)
            RegisterCloudMaterials();

        // The god-ray pass is created in the pipeline's lazy _setup (once it sees the
        // camera), so tuning is applied on the first frame the pass exists.
        if (!_godRayTuningApplied)
        {
            if (_pipeline.Call("get_god_ray_pass").AsGodotObject() is { } pass)
            {
                ApplyGodRayTuning(pass);
                _godRayTuningApplied = true;
            }
        }

        if (_skyClouds != null && Camera != null)
        {
            var camPos = Camera.GlobalPosition;
            _skyClouds.GlobalPosition = new Vector3(camPos.X, CloudHeight, camPos.Z);
        }
    }

    // ── Runtime tweaks (debug overlay's 3dpixelart panel) ───────────────────

    private void ApplyGodRayTuning(GodotObject pass)
    {
        pass.Set("ray_steps", RaySteps);
        pass.Set("ray_max_distance", RayMaxDistance);
        pass.Set("ray_intensity", RayIntensity);
        pass.Set("ray_decay", RayDecay);
        pass.Set("ray_quantize_bands", RayQuantizeBands);
        pass.Set("ray_dust_strength", RayDustStrength);
    }

    /// Re-pushes every runtime-editable export into the pipeline, the god-ray
    /// pass, the sky-cloud sheet, and every live PixelArtObject3D config node.
    /// The debug overlay's 3dpixelart panel edits the exports, then calls this.
    /// Creation-time exports (PipelineEnabled, PixelizeRoots, CloudNoise, the
    /// material-registration toggles) are not covered.
    public void ApplyRuntimeTweaks()
    {
        if (_pipeline == null) return;

        // The GDScript setters push the cloud uniforms to all registered
        // materials (pixelized + terrain/water) and the god-ray pass.
        _pipeline.Set("debug_view", DebugView);
        _pipeline.Set("clouds_enabled", CloudsEnabled);
        _pipeline.Set("cloud_height", CloudHeight);
        _pipeline.Set("cloud_noise_scale", CloudNoiseScale);
        _pipeline.Set("cloud_threshold", CloudThreshold);
        _pipeline.Set("cloud_bands", CloudBands);
        _pipeline.Set("cloud_wind", CloudWind);
        _pipeline.Set("cloud_shadow_strength", CloudShadowStrength);
        _pipeline.Set("cloud_shadow_banding_enabled", CloudShadowBandingEnabled);
        _pipeline.Set("cloud_shadow_levels", (float)CloudShadowLevels);
        _pipeline.Set("cloud_shadow_softness", CloudShadowSoftness);
        _pipeline.Set("god_rays_enabled", GodRaysEnabled);

        if (_pipeline.Call("get_god_ray_pass").AsGodotObject() is { } pass)
        {
            ApplyGodRayTuning(pass);
            _godRayTuningApplied = true;
        }
        else
        {
            _godRayTuningApplied = false; // _Process re-applies once the pass exists
        }

        if (CloudsEnabled && _skyClouds == null) CreateSkyClouds();
        else if (!CloudsEnabled && _skyClouds != null)
        {
            _skyClouds.QueueFree();
            _skyClouds = null;
        }
        if (_skyClouds != null)
        {
            if (_skyClouds.Mesh is PlaneMesh plane)
                plane.Size = new Vector2(CloudPlaneSize, CloudPlaneSize);
            if (_skyClouds.MaterialOverride is ShaderMaterial skyMaterial)
            {
                skyMaterial.SetShaderParameter("cloud_color", CloudColor);
                skyMaterial.SetShaderParameter("noise_scale", CloudNoiseScale);
                skyMaterial.SetShaderParameter("threshold", CloudThreshold);
                skyMaterial.SetShaderParameter("bands", CloudBands);
                skyMaterial.SetShaderParameter("wind", CloudWind);
            }
        }

        // PixelSize/OutlineColor only reach new pixelizations via PixelizeObject;
        // the PixelArtObject3D setters apply them to live objects too.
        foreach (var node in GetTree().GetNodesInGroup("pixel_art_object"))
        {
            node.Set("pixel_size", PixelSize);
            node.Set("outline_color", OutlineColor);
        }
    }

    // Feeds the cloud uniforms to the non-pixelized world materials (MST terrain,
    // water) through the pipeline's register_cloud_material, so their shaders darken
    // under the same clouds as the pixelized characters. Safe to call repeatedly —
    // each side registers at most once.
    private void RegisterCloudMaterials()
    {
        if (_pipeline == null) return;

        if (CloudShadowsOnTerrain && !_terrainCloudsRegistered
            && TerrainMaterialNode != null
            && GetNodeOrNull(TerrainMaterialNode) is { } terrain
            && terrain.Get("terrain_material").AsGodotObject() is ShaderMaterial terrainMaterial)
        {
            _pipeline.Call("register_cloud_material", terrainMaterial);
            _terrainCloudsRegistered = true;
        }

        if (CloudShadowsOnWater && !_waterCloudsRegistered
            && WaterMaterialNode != null
            && GetNodeOrNull(WaterMaterialNode) is WaterComponent3D water)
        {
            if (water.WaterMaterial is { } waterMaterial)
                _pipeline.Call("register_cloud_material", waterMaterial);
            // Either registered, or water is disabled and there is nothing to
            // register — stop retrying in both cases.
            _waterCloudsRegistered = water.WaterMaterial != null || !water.WaterEnabled;
        }
    }

    // ── Per-object pixelization ───────────────────────────────────────────────

    /// Pixelizes every MeshInstance3D under root: assigns the addon's pixel-art material
    /// (carrying the previous albedo over) and attaches a PixelArtObject3D config node.
    /// All meshes under one root share one outline id, so a model outlines as a whole
    /// with no internal seams; successive roots get distinct ids.
    public void PixelizeObject(Node3D root)
    {
        if (_pipeline == null) return;
        var script = GD.Load<GDScript>(PixelArtObjectScriptPath);
        int outlineId = _nextOutlineId;
        _nextOutlineId = _nextOutlineId % 254 + 1;
        PixelizeRecursive(root, script, outlineId);
    }

    private void PixelizeRecursive(Node node, GDScript script, int outlineId)
    {
        if (node is MeshInstance3D mi)
            AttachPixelArtObject(mi, script, outlineId);
        foreach (var child in node.GetChildren())
            PixelizeRecursive(child, script, outlineId);
    }

    private void AttachPixelArtObject(MeshInstance3D mi, GDScript script, int outlineId)
    {
        // Already pixelized (e.g. a root nested inside another PixelizeRoots entry).
        if (mi.GetNodeOrNull("PixelArtObject3D") != null) return;

        // PixelArtObject3D replaces the material wholesale — carry the albedo over
        // first or the mesh turns untextured white.
        Color? albedo = null;
        Texture2D? albedoTexture = null;
        if (mi.GetActiveMaterial(0) is BaseMaterial3D old)
        {
            albedo = old.AlbedoColor;
            albedoTexture = old.AlbedoTexture;
        }

        var config = (Node3D)script.New();
        config.Name = "PixelArtObject3D";
        config.Set("pixel_size", PixelSize);
        config.Set("outline_id", outlineId);
        config.Set("outline_color", OutlineColor);
        mi.AddChild(config); // its _ready targets the parent and assigns the material

        if (mi.MaterialOverride is ShaderMaterial material)
        {
            if (albedo.HasValue) material.SetShaderParameter("base_color", albedo.Value);
            if (albedoTexture != null) material.SetShaderParameter("albedo_texture", albedoTexture);
        }
    }

    // ── Sky clouds ────────────────────────────────────────────────────────────

    private void CreateSkyClouds()
    {
        _skyClouds = new MeshInstance3D
        {
            Name = "SkyClouds",
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new PlaneMesh { Size = new Vector2(CloudPlaneSize, CloudPlaneSize) },
        };
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(SkyCloudsShaderPath) };
        material.SetShaderParameter("cloud_noise", _cloudNoise);
        material.SetShaderParameter("cloud_color", CloudColor);
        material.SetShaderParameter("noise_scale", CloudNoiseScale);
        material.SetShaderParameter("threshold", CloudThreshold);
        material.SetShaderParameter("bands", CloudBands);
        material.SetShaderParameter("wind", CloudWind);
        _skyClouds.MaterialOverride = material;
        AddChild(_skyClouds);
    }

    private static ImageTexture GenerateNoiseTexture(int size, float frequency, int seed)
    {
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = frequency,
            Seed = seed,
        };
        return ImageTexture.CreateFromImage(noise.GetImage(size, size));
    }
}
