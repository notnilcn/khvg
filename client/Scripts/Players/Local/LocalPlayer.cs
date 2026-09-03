#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// The local player's CharacterBody2D root: WASD physics, sprite Walk/Idle, 3D-model sync,
/// and camera registration. Server-table concerns live in child components declared in
/// local_player.tscn, each fed by its own TableBinderComponent (TerrainComponent pattern):
/// PositionSyncComponent (position rows + ReportMovement), LocalPlayerDataComponent
/// (data/stats rows → Health/Stats mirrors), LocalPlayerInventoryComponent (slot state),
/// LocalPlayerProfileComponent (active profile → SpriteFrames). The signals and
/// pass-through properties below keep UI readers (StatsSidebar,
/// InventoryPanel, CombatComponent) on one stable surface.
/// </summary>
public partial class LocalPlayer : CharacterBody2D, IEntity
{
	[Signal] public delegate void InventoryChangedEventHandler();
	[Signal] public delegate void AimSettingsChangedEventHandler();
	[Signal] public delegate void StatsChangedEventHandler();

	public static LocalPlayer? Local { get; private set; }

	// IEntity — LocalPlayer can't derive from Entity (it needs CharacterBody2D), so it
	// implements the interface directly with its own registry. Component children
	// (Stats, Health, Faction, Combat…) register themselves here from their _Ready.
	private readonly EntityRegistry componentRegistry = new();
	public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
	public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
	public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
	public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

	// Raised by the data components; the signals themselves stay on the entity so UI
	// readers keep one stable source.
	internal void RaiseInventoryChanged() => EmitSignal(SignalName.InventoryChanged);
	internal void RaiseAimSettingsChanged() => EmitSignal(SignalName.AimSettingsChanged);
	internal void RaiseStatsChanged() => EmitSignal(SignalName.StatsChanged);

	public string Username => GameManager.Username;

	// Stat/hp/inventory/profile values live in the child components (mirrors of server
	// rows); these pass-throughs keep UI readers (e.g. StatsSidebar) stable.
	// GetComponent works before this node's _Ready because component children register
	// during their own _Ready.
	private HealthComponent? HealthComponent => GetComponent<HealthComponent>();
	private StatsComponent? StatsComponent => GetComponent<StatsComponent>();
	private LocalPlayerDataComponent? DataComponent => GetComponent<LocalPlayerDataComponent>();
	private LocalPlayerInventoryComponent? InventoryState => GetComponent<LocalPlayerInventoryComponent>();
	private LocalPlayerProfileComponent? ProfileComponent => GetComponent<LocalPlayerProfileComponent>();

	public uint Level => DataComponent?.Level ?? 0;
	public string ProfileName => ProfileComponent?.ProfileName ?? "";
	public uint Hp => (uint)(HealthComponent?.Hp ?? 0);
	public uint MaxHp => (uint)(HealthComponent?.MaxHp ?? 0);
	public int Strength => StatsComponent?.GetValue(StatKind.Strength) ?? 0;
	public int Wisdom => StatsComponent?.GetValue(StatKind.Wisdom) ?? 0;
	public int Dexterity => StatsComponent?.GetValue(StatKind.Dexterity) ?? 0;
	public int DamageDealer => StatsComponent?.GetValue(StatKind.DamageDealer) ?? 0;
	public int Supporter => StatsComponent?.GetValue(StatKind.Supporter) ?? 0;
	public int Artisan => StatsComponent?.GetValue(StatKind.Artisan) ?? 0;
	// Modifier-only stats live on the data row, not in StatsComponent.
	public int Defense => DataComponent?.Defense ?? 0;
	public uint UnspentPoints => DataComponent?.UnspentPoints ?? 0;

	// Move speed is a server-resolved stat (PlayerData.base_speed, item-only — not
	// allocatable). The 100f fallback only applies before the first data row arrives.
	public float BaseSpeed => DataComponent?.BaseSpeed ?? 100f;

	/// P4 grapple / elevation falls: while an external mover (the grapple or fall tween)
	/// owns the position, input movement is suspended and velocity pinned to zero so
	/// MoveAndSlide doesn't fight the externally-set position.
	public bool ExternalMovementActive { get; set; }

	private CharacterModel3D? _model3D;
	// res_path3d of the live 3D model, so repeat profile rows for the same texture don't rebuild it.
	private string? _model3DPath;
	// Profile texture waiting on world3D to be cached in _Ready (profile rows can land first).
	private string? _pendingModelTextureId;
	private AnimatedSprite2D sprite = null!;
	private PositionSyncComponent? positionSync;
	private ElevationMapComponent? elevationMap;

	// Camera components of the Main entity, cached in _Ready (per-frame access in
	// _PhysicsProcess makes an ancestor/registry walk per call wasteful).
	private World3DComponent? world3D;
	private Camera2DPresenterComponent? camera2D;
	private CameraRigComponent? cameraRig;

	// Slot 0: weapon only
	public Item? EquippedWeapon => InventoryState?.EquippedWeapon;

	// Slots 1-4: consumables only hotbar
	public IReadOnlyList<Item?> HotbarSlots => InventoryState?.HotbarSlots ?? [];

	// Slot 5: accessory only
	public IReadOnlyList<Item?> AccessorySlots => InventoryState?.AccessorySlots ?? [];

	// Slot 6: armor only
	public IReadOnlyList<Item?> ArmorSlots => InventoryState?.ArmorSlots ?? [];

	// Slots 7-30: general mixed (backpack)
	public IReadOnlyList<ResolvedSlot> GeneralSlots => InventoryState?.GeneralSlots ?? [];

	// Slots 32-37: abilities (artifacts are 1-cost abilities)
	public IReadOnlyList<ResolvedSlot> AbilitySlots => InventoryState?.AbilitySlots ?? [];

	// Slots that count as worn equipment (enchantable): 0 weapon, 5 accessory, 6 armor, 32-37 abilities
	public static bool IsEquipmentSlot(int index) => LocalPlayerInventoryComponent.IsEquipmentSlot(index);

	public ResolvedSlot ResolveSlotAt(int index) => InventoryState?.ResolveSlotAt(index) ?? new ResolvedSlot();

	public string? GetSlotItemId(int index) => InventoryState?.GetSlotItemId(index);

	public override void _Ready()
	{
		Local = this;
		sprite = GetNode<AnimatedSprite2D>("Sprite");
		sprite.Visible = !GameVisuals.Is3D; // 3D mode renders the catalog-driven model, not the 2D sprite
		positionSync = GetComponent<PositionSyncComponent>();

		var gameManager = this.GetAncestor<GameManager>();
		world3D = gameManager?.GetComponent<World3DComponent>();
		camera2D = gameManager?.GetComponent<Camera2DPresenterComponent>();
		cameraRig = gameManager?.GetComponent<CameraRigComponent>();
		elevationMap = gameManager?.GetComponent<ElevationMapComponent>();

		if (_pendingModelTextureId != null)
			SetModel3DTexture(_pendingModelTextureId);

		var pcamNode = GetNode<Node2D>("%LocalPlayerPhantomCamera2D");
		camera2D?.RegisterCamera(pcamNode);
	}

	/// Creates/recreates the 3D model from the profile texture's catalog res_path3d (loaded
	/// here, not scene-declared, so 2D mode never loads the FBX). No-op in 2D mode (no
	/// World3DComponent) or when the texture has no 3D representation. Called by
	/// LocalPlayerProfileComponent when the active profile row arrives.
	public void SetModel3DTexture(string textureId)
	{
		_pendingModelTextureId = textureId;
		if (world3D == null) return;
		var path = GameManager.GetResPath3D(textureId);
		if (path == null || (_model3D != null && path == _model3DPath)) return;
		_model3D?.Dispose();
		_model3DPath = path;
		_model3D = new CharacterModel3D(GD.Load<PackedScene>(path), GlobalPosition, world3D);
		world3D.SetCameraFollowTarget(_model3D.Node);
	}


	/// Cursor position in world 2D space. 2D mode: the camera-transformed mouse position.
	/// 3D mode: a ray from the SubViewport's Camera3D through the cursor, intersected with
	/// the ground plane at the terrain elevation — refined once against the elevation of the
	/// hex the ray first hits, so aiming onto raised ground lands on it. Falls back to the
	/// 2D mouse position when the ray can't hit the ground (camera at the horizon).
	public Vector2 GetCursorWorldPosition()
	{
		if (world3D == null || elevationMap == null)
			return GetGlobalMousePosition();

		var hit = world3D.CursorGroundIntersection(elevationMap.GetWorldHeightAt(GlobalPosition));
		if (hit == null) return GetGlobalMousePosition();
		// Re-intersect at the elevation of the hex actually under the cursor.
		return world3D.CursorGroundIntersection(elevationMap.GetWorldHeightAt(hit.Value)) ?? hit.Value;
	}

	public override void _PhysicsProcess(double delta)
	{
		var raw = Input.GetVector("Left", "Right", "Up", "Down").Normalized();
		float yaw = cameraRig?.Yaw ?? 0f;
		var rot2d = new Vector2(raw.X, raw.Y).Rotated(yaw);
		var input = new Vector2(rot2d.X, rot2d.Y);

		// Twin-stick facing: the character always turns toward the cursor. Rotation drives
		// the sprite/3D-model facing, CombatComponent's aim, and the screen-rotation report.
		Rotation = GlobalPosition.AngleToPoint(GetCursorWorldPosition());

		Velocity = ExternalMovementActive ? Vector2.Zero : input * (positionSync?.CurrentSpeed ?? BaseSpeed);

		ApplyElevationPrediction(delta);

		MoveAndSlide();
		_model3D?.SyncFrom2D(GlobalPosition, Rotation);

		// HasAnimation: in 3D mode the placeholder frames never load (hidden sprite), and
		// in 2D mode the profile frames arrive after the first physics frames.
		var anim = input != Vector2.Zero ? "Walk" : "Idle";
		if (sprite.SpriteFrames != null && sprite.SpriteFrames.HasAnimation(anim))
			sprite.Play(anim);
	}

	// Per-hex elevation prediction (server re-validates in report_movement — uphill
	// rejects are anti-cheat enforcement; this keeps the honest path from ever sending
	// one). Skipped entirely while an external mover owns the position. Steps within
	// ±MAX_WALKABLE_*_DELTA are walked normally; only steeper deltas block or fall.
	private void ApplyElevationPrediction(double delta)
	{
		if (ExternalMovementActive || elevationMap == null || Velocity == Vector2.Zero) return;

		var pos = GlobalPosition;
		int e0 = elevationMap.GetElevationAt(pos);
		var next = pos + Velocity * (float)delta;
		int e1 = elevationMap.GetElevationAt(next);

		if (e1 - e0 > ServerConstants.MaxWalkableUpDelta)
		{
			// Uphill cliff: blocked. Axis-separated slide — keep each velocity component
			// whose own step stays within the walkable rise, zero the rest, so the
			// player slides along the cliff face instead of stopping dead.
			bool keepX = elevationMap.GetElevationAt(new Vector2(next.X, pos.Y)) - e0 <= ServerConstants.MaxWalkableUpDelta;
			bool keepY = elevationMap.GetElevationAt(new Vector2(pos.X, next.Y)) - e0 <= ServerConstants.MaxWalkableUpDelta;
			if (keepX && keepY)
			{
				// Corner case: both single-axis steps are legal but the combined diagonal
				// lands on the too-high hex (the cliff's corner). Keeping both would walk
				// straight up — keep only the dominant axis so the player slides around it.
				if (Mathf.Abs(Velocity.X) >= Mathf.Abs(Velocity.Y)) keepY = false;
				else keepX = false;
			}
			Velocity = new Vector2(keepX ? Velocity.X : 0f, keepY ? Velocity.Y : 0f);
		}
		else if (e0 - e1 > ServerConstants.MaxWalkableDownDelta)
		{
			StartFallTween(e0 - e1);
		}
	}

	/// Walking off a cliff (a drop steeper than MAX_WALKABLE_DOWN_DELTA): tween ~0.75 hex
	/// past the boundary along the current velocity over FALL_SECONDS_PER_LEVEL per level
	/// dropped (mirrored in ServerConstants; the server applies the fall damage — only
	/// when the drop exceeds FALL_SAFE_DROP — and enforces the same lockout via
	/// falling_until).
	/// Same external-mover pattern as the grapple tween in LocalPlayerInventoryComponent:
	/// input movement is suspended and the landing position is reported only at the end
	/// (timed reports are suppressed while ExternalMovementActive).
	private void StartFallTween(int levels)
	{
		var origin = GlobalPosition;
		var destination = origin + Velocity.Normalized() * (elevationMap!.OuterRadius * 0.75f);
		float duration = ServerConstants.FallSecondsPerLevel * levels;

		var sync = positionSync;
		ExternalMovementActive = true;
		var tween = CreateTween();
		tween.TweenMethod(Callable.From<Vector2>(pos =>
		{
			if (!GodotObject.IsInstanceValid(this)) return;
			GlobalPosition = pos;
			Velocity = Vector2.Zero;
		}), origin, destination, duration);
		// Cosmetic dip (darken then recover) over the same duration as the fall.
		var dip = new Color(0.55f, 0.55f, 0.55f);
		tween.Parallel().TweenProperty(this, "modulate", dip, duration * 0.5f);
		tween.Parallel().TweenProperty(this, "modulate", Colors.White, duration * 0.5f).SetDelay(duration * 0.5f);
		tween.Finished += () =>
		{
			if (!GodotObject.IsInstanceValid(this)) return;
			Velocity = Vector2.Zero;
			ExternalMovementActive = false;
			sync?.RequestMovementReport(); // landing report carries the fall displacement
		};
	}

	public override void _ExitTree()
	{
		if (Local == this) Local = null;
		_model3D?.Dispose();
		if (IsInstanceValid(world3D)) world3D!.ClearCameraFollowTarget();
		if (IsInstanceValid(camera2D)) camera2D!.UnregisterCamera();
	}
}
