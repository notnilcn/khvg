#nullable enable
using Godot;
using System;
using System.Linq;

public partial class DebugOverlay : CanvasLayer
{
	private Label label = null!;
	private Button pixelArtButton = null!;
	private Control? pixelArtPanel;
	private Button characterAssetsButton = null!;
	private Button characterAnimationsButton = null!;
	private Control? characterAssetsPanel;
	private Control? characterAnimationsPanel;
	private double accumulator;
	private const double UpdateInterval = 0.25;

	// Same fallback as ElevationMapComponent before the first MapConfig row arrives.
	private const float DefaultHexOuterRadius = 96.0f;

	public override void _Ready()
	{
		label = GetNode<Label>("Label");
		pixelArtButton = GetNode<Button>("PixelArtButton");
		pixelArtButton.Pressed += OnPixelArtButtonPressed;
		characterAssetsButton = GetNode<Button>("CharacterAssetsButton");
		characterAssetsButton.Pressed += OnCharacterAssetsButtonPressed;
		characterAnimationsButton = GetNode<Button>("CharacterAnimationsButton");
		characterAnimationsButton.Pressed += OnCharacterAnimationsButtonPressed;
	}

	public override void _Input(InputEvent @event)
	{
		if (Input.IsActionJustPressed("toggle_debug_overlay"))
		{
			Visible = !Visible;
		}
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;

		accumulator += delta;
		if (accumulator < UpdateInterval) return;
		accumulator = 0.0;

		// Null in 2D mode (world_3d never loads) — the panel tweaks the 3D pipeline only.
		pixelArtButton.Disabled = PixelArtPipelineComponent.Instance == null;
		// Character asset/animation swapping needs the live 3D model.
		var canDriveModel = GameVisuals.Is3D && LocalPlayer.Local != null;
		characterAssetsButton.Disabled = !canDriveModel;
		characterAnimationsButton.Disabled = !canDriveModel;

		var fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
		var frameTimeMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
		var memoryMb = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024.0 * 1024.0);
		var objectCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);
		var nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		var drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
		var enemyCount = GameManager.EnemyCount;
		var conn = GameManager.Conn;
		var connected = conn?.IsActive == true;

		label.Text =
			$"FPS: {fps:0} ({frameTimeMs:0.00} ms)\n" +
			$"Memory: {memoryMb:0.0} MB\n" +
			$"Objects: {objectCount:0}  Nodes: {nodeCount:0}  Draw Calls: {drawCalls:0}\n" +
			$"Enemies: {enemyCount}\n" +
			$"Connection: {(connected ? "connected" : "disconnected")} ({GameManager.Username})\n" +
			PlayerInfo(conn);

		// One-shot read of the already-subscribed MapConfig row (RemotePlayer's profile
		// lookup pattern) — the chunk radius is needed to resolve the player's chunk.
		static string PlayerInfo(SpacetimeDB.Types.DbConnection? conn)
		{
			var player = LocalPlayer.Local;
			if (player == null || !IsInstanceValid(player))
				return "Player: not in world";

			var pos = player.GlobalPosition;
			var elevationMap = GameManager.ElevationMap;
			var outerRadius = elevationMap?.OuterRadius ?? DefaultHexOuterRadius;
			var hex = HexMath.WorldToHex(pos.X, pos.Y, outerRadius);
			var elevation = elevationMap?.GetElevation(hex.X, hex.Y) ?? 0;
			var yaw = player.Rotation;

			var chunk = "n/a (no MapConfig)";
			var cfg = conn?.Db.MapConfig.Iter().FirstOrDefault();
			if (cfg != null)
			{
				var c = HexMath.ToLowerRes(hex.X, hex.Y, cfg.ChunkHexRadius);
				chunk = $"({c.X}, {c.Y}) #{HexMath.SpiralIndex(c.X, c.Y)}";
			}

			return
				$"Position: ({pos.X:0.0}, {pos.Y:0.0})  Velocity: ({player.Velocity.X:0.0}, {player.Velocity.Y:0.0})\n" +
				$"Hex: ({hex.X}, {hex.Y})  Elevation: {elevation}\n" +
				$"Chunk: {chunk}\n" + 
				$"Yaw: {yaw:0.00}";
		}
	}

	// ── 3dpixelart tweak panel ──────────────────────────────────────────────

	private void OnPixelArtButtonPressed()
	{
		var pipeline = PixelArtPipelineComponent.Instance;
		if (pipeline == null) return;

		// Built lazily: world_3d (and thus the pipeline) is instantiated by
		// GameManager._Ready, after this overlay's own _Ready.
		pixelArtPanel ??= BuildPixelArtPanel(pipeline);
		pixelArtPanel.Visible = !pixelArtPanel.Visible;
	}

	// Rows are data-driven (one per runtime-editable PixelArtPipelineComponent
	// export), so the panel is built in code rather than declared in game.tscn.
	private Control BuildPixelArtPanel(PixelArtPipelineComponent p)
	{
		var panel = new PanelContainer
		{
			Name = "PixelArtPanel",
			Visible = false,
			AnchorLeft = 1.0f,
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			OffsetLeft = -360.0f,
			OffsetTop = 126.0f,
			OffsetRight = -8.0f,
			OffsetBottom = -8.0f,
		};
		var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		panel.AddChild(scroll);
		var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		rows.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(rows);

		void Header(string text)
		{
			var header = new Label { Text = text };
			header.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.4f));
			rows.AddChild(header);
		}

		void Slider(string text, float min, float max, float step, Func<float> get, Action<float> set)
		{
			var format = step >= 1.0f ? "0" : step >= 0.1f ? "0.0" : step >= 0.01f ? "0.00" : "0.0000";
			var value = new Label { Text = get().ToString(format), CustomMinimumSize = new Vector2(56, 0) };
			var slider = new HSlider
			{
				MinValue = min,
				MaxValue = max,
				Step = step,
				Value = get(),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			slider.ValueChanged += v =>
			{
				set((float)v);
				value.Text = ((float)v).ToString(format);
				p.ApplyRuntimeTweaks();
			};
			var row = new HBoxContainer();
			row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(130, 0) });
			row.AddChild(slider);
			row.AddChild(value);
			rows.AddChild(row);
		}

		void Toggle(string text, Func<bool> get, Action<bool> set)
		{
			var check = new CheckBox { Text = text, ButtonPressed = get() };
			check.Toggled += on =>
			{
				set(on);
				p.ApplyRuntimeTweaks();
			};
			rows.AddChild(check);
		}

		void ColorRow(string text, Func<Color> get, Action<Color> set)
		{
			var picker = new ColorPickerButton { Color = get(), EditAlpha = true, CustomMinimumSize = new Vector2(64, 0) };
			picker.ColorChanged += c =>
			{
				set(c);
				p.ApplyRuntimeTweaks();
			};
			var row = new HBoxContainer();
			row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(130, 0) });
			row.AddChild(picker);
			rows.AddChild(row);
		}

		Header("Pipeline");
		Slider("Debug View", 0, 3, 1, () => p.DebugView, v => p.DebugView = (int)v);

		Header("Pixelization");
		Slider("Pixel Size", 1, 5, 1, () => p.PixelSize, v => p.PixelSize = (int)v);
		ColorRow("Outline Color", () => p.OutlineColor, c => p.OutlineColor = c);

		Header("Clouds");
		Toggle("Clouds Enabled", () => p.CloudsEnabled, b => p.CloudsEnabled = b);
		Slider("Cloud Height", 50, 1000, 10, () => p.CloudHeight, v => p.CloudHeight = v);
		Slider("Noise Scale", 0.0001f, 0.02f, 0.0001f, () => p.CloudNoiseScale, v => p.CloudNoiseScale = v);
		Slider("Threshold", 0, 1, 0.01f, () => p.CloudThreshold, v => p.CloudThreshold = v);
		Slider("Bands (0 = off)", 0, 10, 1, () => p.CloudBands, v => p.CloudBands = v);
		Slider("Tightness", 0.1f, 4.0f, 0.05f, () => p.CloudTightness, v => p.CloudTightness = v);
		Slider("Octave Drop", 0.1f, 1.0f, 0.05f, () => p.CloudOctaveDrop, v => p.CloudOctaveDrop = v);
		Slider("Gap Erosion", 0, 0.45f, 0.01f, () => p.CloudGapErosion, v => p.CloudGapErosion = v);
		Slider("Detail Strength", 0, 1, 0.05f, () => p.CloudDetailStrength, v => p.CloudDetailStrength = v);
		Slider("Wind X", -0.1f, 0.1f, 0.001f, () => p.CloudWind.X, v => p.CloudWind = new Vector2(v, p.CloudWind.Y));
		Slider("Wind Y", -0.1f, 0.1f, 0.001f, () => p.CloudWind.Y, v => p.CloudWind = new Vector2(p.CloudWind.X, v));
		Slider("Plane Size", 500, 10000, 100, () => p.CloudPlaneSize, v => p.CloudPlaneSize = v);
		ColorRow("Cloud Color", () => p.CloudColor, c => p.CloudColor = c);

		Header("Cloud Shadows");
		Slider("Strength", 0, 1, 0.01f, () => p.CloudShadowStrength, v => p.CloudShadowStrength = v);
		Toggle("Banding Enabled", () => p.CloudShadowBandingEnabled, b => p.CloudShadowBandingEnabled = b);
		Slider("Levels", 1, 8, 1, () => p.CloudShadowLevels, v => p.CloudShadowLevels = (int)v);
		Slider("Softness", 0, 1, 0.01f, () => p.CloudShadowSoftness, v => p.CloudShadowSoftness = v);

		Header("God Rays");
		Toggle("God Rays Enabled", () => p.GodRaysEnabled, b => p.GodRaysEnabled = b);
		Slider("Ray Steps", 4, 64, 1, () => p.RaySteps, v => p.RaySteps = (int)v);
		Slider("Max Distance", 10, 2000, 10, () => p.RayMaxDistance, v => p.RayMaxDistance = v);
		Slider("Intensity", 0, 2, 0.01f, () => p.RayIntensity, v => p.RayIntensity = v);
		Slider("Decay", 0, 10, 0.1f, () => p.RayDecay, v => p.RayDecay = v);
		Slider("Quantize Bands", 0, 16, 1, () => p.RayQuantizeBands, v => p.RayQuantizeBands = v);
		Slider("Dust Strength", 0, 1, 0.01f, () => p.RayDustStrength, v => p.RayDustStrength = v);
		Slider("Near Fade Start", 0, 500, 5, () => p.RayNearFadeStart, v => p.RayNearFadeStart = v);
		Slider("Near Fade Range", 0, 500, 5, () => p.RayNearFadeRange, v => p.RayNearFadeRange = v);
		Slider("Near Fade Power", 0.1f, 4, 0.1f, () => p.RayNearFadePower, v => p.RayNearFadePower = v);
		Slider("Depth Cutoff", 0, 2000, 10, () => p.RayDepthCutoff, v => p.RayDepthCutoff = v);
		Slider("Surface Fade", 0, 200, 1, () => p.RaySurfaceFade, v => p.RaySurfaceFade = v);
		Slider("Max Stack", 0, 20, 0.5f, () => p.RayMaxStack, v => p.RayMaxStack = v);
		Slider("Quantum", 0, 10, 0.1f, () => p.RayQuantum, v => p.RayQuantum = v);
		Slider("Distance Falloff", 0, 0.05f, 0.001f, () => p.RayDistanceFalloff, v => p.RayDistanceFalloff = v);

		AddChild(panel);
		return panel;
	}

	// ── Character asset / animation swap panels ─────────────────────────────

	// Directories scanned for clickable character scenes (the Universal packs share one rig,
	// so the grafted catalog clips animate any of them).
	private static readonly string[] CharacterAssetDirs =
	{
		"res://Assets/3D/characters",
		"res://Assets/3D/Universal Base Characters[Standard]/Base Characters/Godot - UE",
		"res://Assets/3D/Modular Character Outfits - Fantasy[Standard]/Exports/glTF (Godot-Unreal)/Outfits",
		"res://Assets/3D/Universal Animation Library 2[Standard]/Female Mannequin/Unreal-Godot",
	};
	private static readonly string[] CharacterAssetExtensions = { "gltf", "glb", "fbx" }; // GetExtension() has no dot

	private void OnCharacterAssetsButtonPressed()
	{
		characterAssetsPanel ??= BuildCharacterAssetsPanel();
		characterAssetsPanel.Visible = !characterAssetsPanel.Visible;
		if (characterAssetsPanel.Visible)
		{
			pixelArtPanel?.Hide();
			characterAnimationsPanel?.Hide();
		}
	}

	private void OnCharacterAnimationsButtonPressed()
	{
		characterAnimationsPanel ??= BuildCharacterAnimationsPanel();
		characterAnimationsPanel.Visible = !characterAnimationsPanel.Visible;
		if (characterAnimationsPanel.Visible)
		{
			pixelArtPanel?.Hide();
			characterAssetsPanel?.Hide();
			// Catalog rows land after the base subscription applies — repopulate per open.
			RebuildAnimationPanelRows();
		}
	}

	private static PanelContainer CreateSidePanel(string name, out VBoxContainer rows)
	{
		var panel = new PanelContainer
		{
			Name = name,
			Visible = false,
			AnchorLeft = 1.0f,
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			OffsetLeft = -420.0f,
			OffsetTop = 126.0f,
			OffsetRight = -8.0f,
			OffsetBottom = -8.0f,
		};
		var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		panel.AddChild(scroll);
		rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		rows.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(rows);
		return panel;
	}

	private static Label MakeHeader(string text)
	{
		var header = new Label { Text = text };
		header.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.4f));
		return header;
	}

	private Control BuildCharacterAssetsPanel()
	{
		var panel = CreateSidePanel("CharacterAssetsPanel", out var rows);
		foreach (var dir in CharacterAssetDirs)
		{
			using var d = DirAccess.Open(dir);
			if (d == null) continue;
			var files = d.GetFiles()
				.Where(f => CharacterAssetExtensions.Contains(f.GetExtension().ToLowerInvariant()))
				.OrderBy(f => f)
				.ToArray();
			if (files.Length == 0) continue;
			rows.AddChild(MakeHeader(dir.GetFile()));
			foreach (var file in files)
			{
				var path = $"{dir}/{file}";
				var button = new Button { Text = file.GetBaseName().GetFile(), TooltipText = path, Alignment = HorizontalAlignment.Left };
				button.Pressed += () => LocalPlayer.Local?.SetCharacterAsset(path);
				rows.AddChild(button);
			}
		}
		AddChild(panel);
		return panel;
	}

	private VBoxContainer animationRows = null!;

	private Control BuildCharacterAnimationsPanel()
	{
		var panel = CreateSidePanel("CharacterAnimationsPanel", out animationRows);
		AddChild(panel);
		return panel;
	}

	private void RebuildAnimationPanelRows()
	{
		foreach (var child in animationRows.GetChildren())
			child.QueueFree();

		var player = LocalPlayer.Local;
		var entries = GameManager.GetAnimations().OrderBy(e => e.AnimationId).ToArray();
		if (entries.Length == 0)
		{
			animationRows.AddChild(new Label { Text = "No AllAnimations rows yet." });
			return;
		}
		var ids = entries.Select(e => e.AnimationId).ToArray();

		void MappingRow(string text, Func<string> get, Action<string> set)
		{
			var option = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			foreach (var id in ids) option.AddItem(id);
			option.Selected = Array.IndexOf(ids, get());
			option.ItemSelected += i => set(ids[i]);
			var row = new HBoxContainer();
			row.AddChild(new Label { Text = text, CustomMinimumSize = new Vector2(70, 0) });
			row.AddChild(option);
			animationRows.AddChild(row);
		}

		if (player != null)
		{
			animationRows.AddChild(MakeHeader("State Mapping"));
			MappingRow("Idle", () => player.IdleAnimation, v => player.IdleAnimation = v);
			MappingRow("Walk", () => player.WalkAnimation, v => player.WalkAnimation = v);
			MappingRow("Fire", () => player.FireAnimation, v => player.FireAnimation = v);
			MappingRow("Interact", () => player.InteractAnimation, v => player.InteractAnimation = v);
		}

		animationRows.AddChild(MakeHeader("Clips (click to play)"));
		foreach (var entry in entries)
		{
			var id = entry.AnimationId;
			var button = new Button
			{
				Text = id,
				TooltipText = $"{entry.Kind} — {entry.ClipName} @ {entry.ResPath.GetFile()}",
				Alignment = HorizontalAlignment.Left,
			};
			button.Pressed += () => LocalPlayer.Local?.PlayCharacterAnimation(id);
			animationRows.AddChild(button);
		}
	}
}
