#nullable enable
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 3D pixel-art pipeline driver (Pixelizer3D addon), declared in world_3d.tscn.
/// The addon is now node-based: the scene declares PixelizerManager3D,
/// PixelizerSkyClouds3D (cloud provider + visible deck), PixelizerGodRays3D and
/// the PixelizerApplier3D subtrees (characters, bullets). This component no
/// longer creates any of those; it resolves the scene nodes and:
///
///   - pushes the pipeline-level exports (pixel size, outline colour, debug view)
///     and the cloud exports into the scene nodes,
///   - registers the MST terrain's shared material with the manager's
///     register_cloud_material so the terrain darkens under the same cloud field
///     (the water is addon-owned now and self-registers),
///   - keeps the god-ray tuning exports wired to the PixelizerGodRays3D provider
///     (the manager pushes them onto its ray pass).
///
/// Cloud rendering itself lives in PixelizerSkyClouds3D: it owns the 15-field
/// uniform contract and feeds the manager via build_cloud_params(), so the deck,
/// the shadows and the god rays all read one field. This component never builds
/// a sky sheet any more.
///
/// The addon interop is untyped GDScript (same caveat as BulletSpawnerComponent:
/// typos surface only at runtime). Requires Forward+ or Mobile (RenderingDevice
/// compute); on Compatibility/web the pipeline degrades to a warning and the game
/// renders normally.
///
/// Camera snap: the world camera is perspective, so the scene pins
/// PixelizerManager3D.snap_camera = false (the addon's snap is ortho-only).
/// </summary>
public partial class PixelArtPipelineComponent : Component
{
	/// The one pipeline in the world_3d scene; null in 2D mode (world_3d never loads).
	public static PixelArtPipelineComponent? Instance { get; private set; }

	[ExportGroup("Pipeline")]
	[Export] public bool PipelineEnabled { get; set; } = true;
	[Export] public DirectionalLight3D? Sun { get; set; }
	/// Palette LUT baked by the addon's palette resource; null = no grading.
	[Export] public Texture2D? GlobalPaletteLut { get; set; }
	/// 0 = normal, 1 = anchor map, 2 = metadata, 3 = linear depth, 4 = colour copy,
	/// 5 = shifted colour, 6 = occlusion flags, 7/8 = raw depth, 9 = revert flag.
	[Export(PropertyHint.Range, "0,9")] public int DebugView { get; set; } = 0;
	/// Snap the camera to the texel lattice via h_offset/v_offset. Off by default:
	/// the world camera is perspective and the addon's snap is ortho-only.
	[Export] public bool CameraSnap { get; set; } = false;

	[ExportGroup("Pixelization")]
	/// Manager default macro-pixel size; the appliers inherit it (`pixel_size = -1`).
	[Export(PropertyHint.Range, "1,5")] public int PixelSize { get; set; } = 3;
	/// Outline colour pushed to every scene-declared applier (live-updates via
	/// the applier's outline colour setter).
	[Export] public Color OutlineColor { get; set; } = Colors.Black;

	[ExportGroup("Clouds")]
	[Export] public bool CloudsEnabled { get; set; } = true;
	/// Coverage noise; null lets PixelizerSkyClouds3D generate its seamless default.
	[Export] public Texture2D? CloudNoise { get; set; }
	/// Height of the imaginary cloud plane the pixelizer samples (deck uses the
	/// same value unless PixelizerSkyClouds3D.height overrides it).
	[Export] public float CloudHeight { get; set; } = 400f;
	/// World-units-per-noise-UV scale (~5 features per texture period).
	[Export] public float CloudNoiseScale { get; set; } = 0.0015f;
	/// Noise value above which a cloud fully blocks the sun.
	[Export(PropertyHint.Range, "0,1")] public float CloudThreshold { get; set; } = 0.6f;
	/// Noise quantization for the sky sheet and god rays (≤1 disables).
	[Export] public float CloudBands { get; set; } = 3f;
	/// Coverage curve exponent: 1 = unchanged, higher = narrower cloud cores.
	[Export(PropertyHint.Range, "0.1,4.0,0.05")] public float CloudTightness { get; set; } = 1f;
	/// Amplitude falloff per FBM octave for the procedural detail.
	[Export(PropertyHint.Range, "0.1,1.0,0.05")] public float CloudOctaveDrop { get; set; } = 0.5f;
	/// Erodes the ray-march cloud gaps; 0 = unchanged.
	[Export(PropertyHint.Range, "0,0.45,0.01")] public float CloudGapErosion { get; set; } = 0f;
	/// Mix procedural FBM detail into the texture noise (0 = texture only).
	[Export(PropertyHint.Range, "0,1,0.05")] public float CloudDetailStrength { get; set; } = 0f;
	[Export] public Vector2 CloudWind { get; set; } = new(0.01f, 0.004f);
	/// Visible deck extent; must exceed the largest ortho view (one quad, big is cheap).
	[Export] public float CloudPlaneSize { get; set; } = 16000f;
	[Export] public Color CloudColor { get; set; } = Colors.White;

	[ExportGroup("Cloud Shadows")]
	/// How dark the ground gets under a full cloud (0 = no shadow, 1 = black).
	[Export(PropertyHint.Range, "0,1")] public float CloudShadowStrength { get; set; } = 0.55f;
	/// Multi-level banded shadows (hard contour rings) vs smooth soft shadows.
	[Export] public bool CloudShadowBandingEnabled { get; set; } = true;
	/// Darkness levels when banding: 1 = one hard shadow, 2-4 = contour rings.
	[Export(PropertyHint.Range, "1,8")] public int CloudShadowLevels { get; set; } = 3;
	/// Width of the penumbra below CloudThreshold the shadow ramps across.
	[Export(PropertyHint.Range, "0,1")] public float CloudShadowSoftness { get; set; } = 0.35f;
	/// Darken the MST terrain under clouds (its shader is not pixelized, so the
	/// pipeline feeds it the same cloud uniforms via register_cloud_material).
	[Export] public bool CloudShadowsOnTerrain { get; set; } = true;
	/// Node holding the MarchingSquaresTerrain script; its shared terrain_material
	/// receives the cloud uniforms.
	[Export] public NodePath? TerrainMaterialNode { get; set; } = "../TerrainComponent3D/MarchingSquaresTerrain";

	[ExportGroup("God Rays")]
	[Export] public bool GodRaysEnabled { get; set; } = true;
	[Export(PropertyHint.Range, "4,64")] public int RaySteps { get; set; } = 24;
	[Export] public float RayMaxDistance { get; set; } = 600f;
	[Export] public float RayIntensity { get; set; } = 0.5f;
	[Export] public float RayDecay { get; set; } = 2f;
	/// Stepped banding of the rays (pixel look); ≤1 disables quantization.
	[Export] public float RayQuantizeBands { get; set; } = 4f;
	[Export(PropertyHint.Range, "0,1")] public float RayDustStrength { get; set; } = 0.15f;
	[Export] public float RayNearFadeStart { get; set; } = 0f;
	[Export] public float RayNearFadeRange { get; set; } = 0f;
	[Export] public float RayNearFadePower { get; set; } = 1f;
	[Export] public float RayDepthCutoff { get; set; } = 0f;
	[Export] public float RaySurfaceFade { get; set; } = 0f;
	[Export] public float RayMaxStack { get; set; } = 10f;
	[Export] public float RayQuantum { get; set; } = 0f;
	[Export] public float RayDistanceFalloff { get; set; } = 0f;

	private Node? _manager; // GDScript PixelizerManager3D (untyped interop)
	private Node? _skyClouds; // GDScript PixelizerSkyClouds3D (cloud provider + deck)
	private Node? _godRays; // GDScript PixelizerGodRays3D (ray provider + toggle)
	private HashSet<string>? _managerProperties; // properties the loaded manager declares
	private HashSet<string>? _godRayProperties; // properties the loaded god-ray node declares
	private bool _godRaysSupported; // addon revision ships get_god_ray_pass()
	private bool _godRayTuningApplied;
	private bool _terrainCloudsRegistered;

	protected override void OnRegistered()
	{
		Instance = this;
		if (!PipelineEnabled) return;
		if (RenderingServer.GetRenderingDevice() == null)
		{
			GD.PushWarning("PixelArtPipelineComponent: RenderingDevice unavailable (Compatibility renderer) — the 3D pixel-art pipeline needs Forward+ or Mobile; rendering unmodified.");
			return;
		}

		_manager = GetNodeOrNull("../PixelizerManager3D");
		_skyClouds = GetNodeOrNull("../PixelizerSkyClouds3D");
		_godRays = GetNodeOrNull("../PixelizerGodRays3D");
		if (_manager == null)
		{
			GD.PushWarning("PixelArtPipelineComponent: no PixelizerManager3D node in world_3d.tscn — 3D pixelization is inert.");
			return;
		}

		_godRaysSupported = _manager.HasMethod("get_god_ray_pass");
		if (Sun != null) SetManager("sun", Sun);
		PushPipelineSettings();
		PushSkyCloudSettings();
		PushGodRayToggle();

		RegisterCloudMaterials();
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		base._ExitTree();
	}

	public override void _Process(double _delta)
	{
		if (_manager == null) return;

		// The MST terrain material is built in TerrainComponent3D.MarchingSquaresTerrain._Ready,
		// which runs after our OnRegistered (tree order), so registration retries until it lands.
		if (!_terrainCloudsRegistered)
			RegisterCloudMaterials();

		// The ray tuning is authored on the PixelizerGodRays3D node, which the
		// manager's registered ray provider pulls every frame; push the exports
		// onto it once on the first tick. Addon revisions without god rays simply
		// keep the exports inert.
		if (_godRaysSupported && !_godRayTuningApplied)
		{
			ApplyGodRayTuning();
			_godRayTuningApplied = true;
		}
	}

	// ── Runtime tweaks (debug overlay's Pixelizer3D panel) ──────────────────

	private void ApplyGodRayTuning()
	{
		SetGodRays("ray_steps", RaySteps);
		SetGodRays("ray_max_distance", RayMaxDistance);
		SetGodRays("ray_intensity", RayIntensity);
		SetGodRays("ray_decay", RayDecay);
		SetGodRays("ray_quantize_bands", RayQuantizeBands);
		SetGodRays("ray_dust_strength", RayDustStrength);
		SetGodRays("ray_near_fade_start", RayNearFadeStart);
		SetGodRays("ray_near_fade_range", RayNearFadeRange);
		SetGodRays("ray_near_fade_power", RayNearFadePower);
		SetGodRays("ray_depth_cutoff", RayDepthCutoff);
		SetGodRays("ray_surface_fade", RaySurfaceFade);
		SetGodRays("ray_max_stack", RayMaxStack);
		SetGodRays("ray_quantum", RayQuantum);
		SetGodRays("ray_distance_falloff", RayDistanceFalloff);
	}

	// Pushes a value into the loaded god-ray node only when its script declares
	// the property, so older addon revisions (which only expose `enabled`) do not
	// log on Set.
	private void SetGodRays(string name, Variant value)
	{
		if (_godRays == null) return;
		if (_godRayProperties == null)
		{
			_godRayProperties = new HashSet<string>();
			foreach (var property in _godRays.GetPropertyList())
				if (property.ContainsKey("name"))
					_godRayProperties.Add(property["name"].AsString());
		}
		if (_godRayProperties.Contains(name)) _godRays.Set(name, value);
	}

	/// Re-pushes every runtime-editable export into the scene nodes. The debug
	/// overlay's panel edits the exports, then calls this. Creation-time exports
	/// (PipelineEnabled, CloudNoise, the terrain-registration toggle) are not
	/// covered.
	public void ApplyRuntimeTweaks()
	{
		if (_manager == null) return;

		PushPipelineSettings();
		PushSkyCloudSettings();
		PushGodRayToggle();

		if (_godRaysSupported)
		{
			ApplyGodRayTuning();
			_godRayTuningApplied = true;
		}
	}

	private void PushPipelineSettings()
	{
		SetManager("pixel_size", PixelSize);
		SetManager("outline_color", OutlineColor);
		SetManager("camera_snap", CameraSnap); // old-revision name
		SetManager("snap_camera", CameraSnap); // new-revision name
		SetManager("debug_view", DebugView);
		SetManager("global_palette_lut", GlobalPaletteLut!);
		if (GlobalPaletteLut != null && _managerProperties != null
			&& !_managerProperties.Contains("global_palette_lut"))
			GD.PushWarning("PixelArtPipelineComponent: this Pixelizer3D revision has no palette LUT support — GlobalPaletteLut ignored.");

		// Outline colour reaches live appliers through their own outline colour
		// setter (which live-refreshes the subtree); pixel-size changes reach them
		// on the rescan below.
		foreach (var applier in GetAppliers())
		{
			applier.Set("outline_color", OutlineColor);
			applier.Call("rescan");
		}
	}

	// The manager's cloud provider builds the shared cloud field; push the
	// component's cloud exports onto it. PixelizerSkyClouds3D re-reads these
	// every frame (the manager re-pushes on a signature change), so plain Set is
	// enough — no explicit re-push.
	private void PushSkyCloudSettings()
	{
		if (_skyClouds == null) return;
		_skyClouds.Set("clouds_enabled", CloudsEnabled);
		_skyClouds.Set("cloud_noise", CloudNoise!);
		_skyClouds.Set("cloud_height", CloudHeight);
		_skyClouds.Set("cloud_noise_scale", CloudNoiseScale);
		_skyClouds.Set("cloud_threshold", CloudThreshold);
		_skyClouds.Set("cloud_bands", CloudBands);
		_skyClouds.Set("cloud_tightness", CloudTightness);
		_skyClouds.Set("cloud_octave_drop", CloudOctaveDrop);
		_skyClouds.Set("cloud_gap_erosion", CloudGapErosion);
		_skyClouds.Set("cloud_detail_strength", CloudDetailStrength);
		_skyClouds.Set("cloud_wind", CloudWind);
		_skyClouds.Set("cloud_shadow_strength", CloudShadowStrength);
		_skyClouds.Set("cloud_shadow_banding_enabled", CloudShadowBandingEnabled);
		_skyClouds.Set("cloud_shadow_levels", CloudShadowLevels);
		_skyClouds.Set("cloud_shadow_softness", CloudShadowSoftness);
		_skyClouds.Set("plane_size", CloudPlaneSize);
		_skyClouds.Set("cloud_color", CloudColor);
		_skyClouds.Set("deck_enabled", CloudsEnabled);
	}

	private void PushGodRayToggle()
	{
		if (_godRays != null) _godRays.Set("enabled", GodRaysEnabled);
	}

	// Enumerate the scene-declared appliers so runtime tweaks can live-refresh them.
	private List<Node> GetAppliers()
	{
		var appliers = new List<Node>();
		if (_manager == null) return appliers;
		foreach (var child in _manager.GetChildren())
			if (child.HasMethod("rescan"))
				appliers.Add(child);
		return appliers;
	}

	// Feeds the cloud uniforms to the non-pixelized MST terrain through the
	// manager's register_cloud_material, so the ground darkens under the same
	// clouds as the pixelized characters. The addon water self-registers.
	// Safe to call repeatedly.
	private void RegisterCloudMaterials()
	{
		if (_manager == null || !CloudShadowsOnTerrain || _terrainCloudsRegistered) return;
		if (TerrainMaterialNode != null
			&& GetNodeOrNull(TerrainMaterialNode) is { } terrain
			&& terrain.Get("terrain_material").AsGodotObject() is ShaderMaterial terrainMaterial)
		{
			_manager.Call("register_cloud_material", terrainMaterial);
			_terrainCloudsRegistered = true;
		}
	}

	// Pushes a value into the loaded manager only when the manager script actually
	// declares the property. Object.Set on a missing property logs an error, so
	// unsupported exports are skipped instead.
	private void SetManager(string name, Variant value)
	{
		if (_manager == null) return;
		if (_managerProperties == null)
		{
			_managerProperties = new HashSet<string>();
			foreach (var property in _manager.GetPropertyList())
				if (property.ContainsKey("name"))
					_managerProperties.Add(property["name"].AsString());
		}
		if (_managerProperties.Contains(name)) _manager.Set(name, value);
	}
}
