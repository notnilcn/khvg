#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

public class ResolvedSlot
{
	public InventorySlot? Slot { get; init; }
	public Item? Item { get; init; }
	public bool IsEmpty => Item == null;
}

public partial class LocalPlayer : CharacterBody2D, IEntity
{
	[Signal] public delegate void InventoryChangedEventHandler();
	[Signal] public delegate void AimSettingsChangedEventHandler();
	[Signal] public delegate void StatsChangedEventHandler();
	[Export] public PackedScene? KnightScene { get; set; }

	public static LocalPlayer? Local { get; private set; }

	// IEntity — LocalPlayer can't derive from Entity (it needs CharacterBody2D), so it
	// implements the interface directly with its own registry. Component children
	// (Stats, Health, Faction, Combat…) register themselves here from their _Ready.
	private readonly EntityRegistry componentRegistry = new();
	public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
	public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
	public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
	public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

	public string Username { get; private set; } = "";
	public string ProfileName { get; private set; } = "";

	// Stat/hp values live in the Health/Stats components (mirrors of server rows); these
	// pass-throughs keep UI readers (e.g. StatsSidebarComponent) stable. GetComponent works
	// before this node's _Ready because component children register during their own _Ready.
	private HealthComponent? HealthComponent => GetComponent<HealthComponent>();
	private StatsComponent? StatsComponent => GetComponent<StatsComponent>();

	public uint Level { get; private set; }
	public uint Hp => (uint)(HealthComponent?.Hp ?? 0);
	public uint MaxHp => (uint)(HealthComponent?.MaxHp ?? 0);
	public int Strength => StatsComponent?.GetValue(StatKind.Strength) ?? 0;
	public int Wisdom => StatsComponent?.GetValue(StatKind.Wisdom) ?? 0;
	public int Dexterity => StatsComponent?.GetValue(StatKind.Dexterity) ?? 0;
	public int Defense => StatsComponent?.GetValue(StatKind.Defense) ?? 0;
	public int Vitality => StatsComponent?.GetValue(StatKind.Vitality) ?? 0;
	public int Speed => StatsComponent?.GetValue(StatKind.Speed) ?? 0;

	private const float SpeedPerStat = 10f;
	private const float WrapSnapThreshold = 50f;

	private readonly List<InventorySlot> inventorySlots = new();
	private CharacterModel3D? _model3D;
	private AnimatedSprite2D sprite = null!;
	private float reportTimer = 0f;
	private const float ReportInterval = 0.1f;

	// Camera components of the Main entity, cached in _Ready (per-frame access in
	// _PhysicsProcess makes an ancestor/registry walk per call wasteful).
	private World3DComponent? world3D;
	private Camera2DPresenterComponent? camera2D;
	private CameraRigComponent? cameraRig;

	// Slot 0: weapon only
	public Item? EquippedWeapon => GetSlotItem(0, GameManager.GetItem);

	// Slots 1-4: consumables only hotbar
	public IReadOnlyList<Item?> HotbarSlots => BuildTypedSlots(1, 4, GameManager.GetItem);

	// Slots 5-8: accessories only
	public IReadOnlyList<Item?> AccessorySlots => BuildTypedSlots(5, 4, GameManager.GetItem);

	// Slots 9-12: armor only
	public IReadOnlyList<Item?> ArmorSlots => BuildTypedSlots(9, 4, GameManager.GetItem);

	// Slots 13-14: artifact only
	public IReadOnlyList<Item?> ArtifactSlots => BuildTypedSlots(13, 2, GameManager.GetItem);

	// Slots 15-22: general mixed
	public IReadOnlyList<ResolvedSlot> GeneralSlots => BuildResolvedSlots(15, 8);

	// Slots that count as worn equipment (enchantable): 0 weapon, 5-8 accessories, 9-12 armor, 13-14 artifacts
	public static bool IsEquipmentSlot(int index) => index == 0 || (index >= 5 && index <= 14);

	public override void _Ready()
	{
		Local  = this;
		sprite = GetNode<AnimatedSprite2D>("Sprite");

		var gameManager = this.GetAncestor<GameManager>();
		world3D   = gameManager?.GetComponent<World3DComponent>();
		camera2D  = gameManager?.GetComponent<Camera2DPresenterComponent>();
		cameraRig = gameManager?.GetComponent<CameraRigComponent>();

		if (KnightScene != null && world3D != null)
		{
			_model3D = new CharacterModel3D(KnightScene, GlobalPosition, world3D);
			world3D.SetCameraFollowTarget(_model3D.Node);
		}

		var pcamNode = GetNode<Node2D>("%LocalPlayerPhantomCamera2D");
		camera2D?.RegisterCamera(pcamNode);

		// comedot's shared-Stat pattern: the Stats component lists the same Stat instance
		// the Health component owns, so all observers see one hp value.
		if (HealthComponent != null && StatsComponent != null)
			StatsComponent.RegisterStat(StatKind.Hp, HealthComponent.Health);

		var conn = GameManager.Conn;
		if (conn == null) return;

		conn.Db.LocalPlayerData.OnUpdate            += OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate           += OnStatsUpdate;
		conn.Db.LocalPlayerPosition.OnUpdate        += OnPositionUpdate;
		conn.Db.LocalPlayerInventory.OnUpdate       += OnInventoryUpdate;
		conn.Db.LocalPlayerActiveProfile.OnUpdate   += OnActiveProfileUpdate;

		Username = GameManager.Username;
		foreach (var row in conn.Db.LocalPlayerData.Iter())
			ApplyData(row);
		foreach (var row in conn.Db.LocalPlayerStats.Iter())
			ApplyStats(row);
		foreach (var row in conn.Db.LocalPlayerInventory.Iter())
			ApplyInventory(row);
		foreach (var row in conn.Db.LocalPlayerPosition.Iter())
			GlobalPosition = new Vector2(row.X, row.Y);

		foreach (var profile in conn.Db.LocalPlayerActiveProfile.Iter())
		{
			ProfileName = profile.ProfileName;
			var resPath = GameManager.GetResPath(profile.TextureId);
			if (resPath != null)
				sprite.SpriteFrames = GD.Load<SpriteFrames>(resPath);
			EmitSignal("AimSettingsChanged");
		}
	}


	public override void _PhysicsProcess(double delta)
	{
		var raw = Input.GetVector("Left", "Right", "Up", "Down").Normalized();
		float yaw  = cameraRig?.Yaw ?? 0f;
		var rot2d = new Vector2(raw.X, raw.Y).Rotated(yaw);
		var input = new Vector2(rot2d.X, rot2d.Y);

		// Rotation = GlobalPosition.AngleToPoint(GetGlobalMousePosition());

		Velocity = input * Speed * SpeedPerStat;

		if (Input.IsActionPressed("Left") || Input.IsActionPressed("Right") || Input.IsActionPressed("Up") || Input.IsActionPressed("Down"))
		{
			Rotation = yaw;
		}


		MoveAndSlide();
		_model3D?.SyncFrom2D(GlobalPosition, Rotation);

		if (sprite.SpriteFrames != null)
			sprite.Play(input != Vector2.Zero ? "Walk" : "Idle");

		reportTimer += (float)delta;
		if (reportTimer >= ReportInterval)
		{
			reportTimer = 0f;
			GameManager.Conn?.Reducers.ReportMovement(GlobalPosition.X, GlobalPosition.Y, Rotation);
		}
	}

	private void OnDataUpdate(EventContext _, PlayerData old, PlayerData data) => ApplyData(data);
	private void OnStatsUpdate(EventContext _, PlayerStats old, PlayerStats stats) => ApplyStats(stats);
	private void OnActiveProfileUpdate(EventContext _, PlayerProfile old, PlayerProfile profile) => EmitSignal("AimSettingsChanged");

	private void OnPositionUpdate(EventContext _, PlayerPosition old, PlayerPosition position)
	{
		// We never force our own GlobalPosition to match the server's wrapped report directly —
		// it stays a continuous, unbounded local reference frame (our own physics already
		// integrate it that way). We only check whether the nearest *wrapped copy* of the
		// server's position is still far from us: if even that's far, it's a real desync
		// (lag/rubber-band/correction), not a routine wrap, and worth a hard correction.
		var serverPos = new Vector2(position.X, position.Y);
		var nearest = TorusMath.NearestCandidate(serverPos, GlobalPosition, GameManager.LapQ, GameManager.LapR);
		if (GlobalPosition.DistanceSquaredTo(nearest) > WrapSnapThreshold * WrapSnapThreshold)
		{
			GD.Print($"[Desync] {GlobalPosition} → {nearest} (dist={GlobalPosition.DistanceTo(nearest):F1})");
			GlobalPosition = nearest;
		}
	}

	private void OnInventoryUpdate(EventContext _, PlayerInventory old, PlayerInventory inventory) => ApplyInventory(inventory);

	private void ApplyInventory(PlayerInventory inventory)
	{
		inventorySlots.Clear();
		inventorySlots.AddRange(inventory.Slots);
		EmitSignal("InventoryChanged");
	}

	private void ApplyData(PlayerData data)
	{
		Level = data.Level;
		HealthComponent?.SetFromServer(data.Hp, data.MaxHp);
		EmitSignal("StatsChanged");
	}

	private void ApplyStats(PlayerStats stats)
	{
		StatsComponent?.SetFromServer(StatKind.Strength, stats.Strength);
		StatsComponent?.SetFromServer(StatKind.Wisdom, stats.Wisdom);
		StatsComponent?.SetFromServer(StatKind.Dexterity, stats.Dexterity);
		StatsComponent?.SetFromServer(StatKind.Defense, stats.Defense);
		StatsComponent?.SetFromServer(StatKind.Vitality, stats.Vitality);
		StatsComponent?.SetFromServer(StatKind.Speed, stats.Speed);
		EmitSignal("StatsChanged");
	}

	private IReadOnlyList<ResolvedSlot> BuildResolvedSlots(int startIndex, int count) =>
		Enumerable.Range(startIndex, count).Select(ResolveSlotAt).ToList();

	private IReadOnlyList<T?> BuildTypedSlots<T>(int startIndex, int count, System.Func<string, T?> resolver) where T : class =>
		Enumerable.Range(startIndex, count).Select(i => GetSlotItem(i, resolver)).ToList();

	public ResolvedSlot ResolveSlotAt(int index)
	{
		if (index >= inventorySlots.Count) return new ResolvedSlot();
		var slot = inventorySlots[index];
		if (slot.ItemId == null) return new ResolvedSlot { Slot = slot };
		var item = GameManager.GetItem(slot.ItemId);
		return new ResolvedSlot { Slot = slot, Item = item };
	}

	private T? GetSlotItem<T>(int index, System.Func<string, T?> resolver) where T : class
	{
		if (index >= inventorySlots.Count) return null;
		var itemId = inventorySlots[index].ItemId;
		return itemId != null ? resolver(itemId) : null;
	}

	public string? GetSlotItemId(int index) => index < inventorySlots.Count ? inventorySlots[index].ItemId : null;

	public override void _ExitTree()
	{
		if (Local == this) Local = null;
		var conn = GameManager.Conn;
		if (conn == null) return;
		conn.Db.LocalPlayerData.OnUpdate            -= OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate           -= OnStatsUpdate;
		conn.Db.LocalPlayerPosition.OnUpdate        -= OnPositionUpdate;
		conn.Db.LocalPlayerInventory.OnUpdate       -= OnInventoryUpdate;
		conn.Db.LocalPlayerActiveProfile.OnUpdate   -= OnActiveProfileUpdate;
		_model3D?.Dispose();
		if (IsInstanceValid(world3D)) world3D!.ClearCameraFollowTarget();
		if (IsInstanceValid(camera2D)) camera2D!.UnregisterCamera();
	}
}
