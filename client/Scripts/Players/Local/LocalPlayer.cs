#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

public class ResolvedSlot
{
	public InventorySlot? Slot { get; init; }
	public Item? Item { get; init; }
	public Weapon? Weapon { get; init; }
	public Artifact? Artifact { get; init; }
	public Consumable? Consumable { get; init; }
	public Armor? Armor { get; init; }
	public Accessory? Accessory { get; init; }
	public bool IsEmpty => Item == null;
}

public partial class LocalPlayer : CharacterBody2D
{
	[Signal] public delegate void InventoryChangedEventHandler();
	[Signal] public delegate void AimSettingsChangedEventHandler();

	public static LocalPlayer? Local { get; private set; }

	public string Username { get; private set; } = "";
	public string ProfileName { get; private set; } = "";

	public uint Level { get; private set; }
	public uint Hp { get; private set; }
	public uint MaxHp { get; private set; }
	public int Strength { get; private set; }
	public int Wisdom { get; private set; }
	public int Dexterity { get; private set; }
	public int Defense { get; private set; }
	public int Vitality { get; private set; }
	public int Speed { get; private set; }

	private const float SpeedPerStat = 4f;
	private const float WrapSnapThreshold = 50f;

	private readonly List<InventorySlot> inventorySlots = new();

	private AnimatedSprite2D sprite = null!;
	private Area2D bulletCollider = null!;
	private float reportTimer = 0f;
	private const float ReportInterval = 0.1f;

	// Slot 0: weapon only
	public Weapon? EquippedWeapon => GetSlotItem(0, GameManager.Instance.GetWeapon);

	// Slots 1-5: ability/consumable mixed
	public IReadOnlyList<ResolvedSlot> HotbarSlots => BuildResolvedSlots(1, 5);

	// Slots 6-9: accessories only
	public IReadOnlyList<Accessory?> AccessorySlots => BuildTypedSlots(6, 4, GameManager.Instance.GetAccessory);

	// Slots 10-13: armor only
	public IReadOnlyList<Armor?> ArmorSlots => BuildTypedSlots(10, 4, GameManager.Instance.GetArmor);

	// Slots 14-22: general mixed
	public IReadOnlyList<ResolvedSlot> GeneralSlots => BuildResolvedSlots(14, 9);

	public override void _Ready()
	{
		Local  = this;
		sprite = GetNode<AnimatedSprite2D>("Sprite");
		bulletCollider = GetNode<Area2D>("BulletCollider");

		var conn = GameManager.Instance.Conn;
		if (conn == null) return;

		conn.Db.LocalPlayerData.OnUpdate            += OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate           += OnStatsUpdate;
		conn.Db.LocalPlayerPosition.OnUpdate        += OnPositionUpdate;
		conn.Db.LocalPlayerInventory.OnUpdate       += OnInventoryUpdate;
		conn.Db.LocalPlayerActiveProfile.OnUpdate   += OnActiveProfileUpdate;

		Username = GameManager.Instance.Username;
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
			var resPath = GameManager.Instance.GetResPath(profile.TextureId);
			if (resPath != null)
				sprite.SpriteFrames = GD.Load<SpriteFrames>(resPath);
			EmitSignal("AimSettingsChanged");
		}
			
	}

	public override void _PhysicsProcess(double delta)
	{
		var input = Input.GetVector("Left", "Right", "Up", "Down").Normalized();

		Rotation = GlobalPosition.AngleToPoint(GetGlobalMousePosition());

		Velocity = input * Speed * SpeedPerStat;
		MoveAndSlide();

		if (sprite.SpriteFrames != null)
			sprite.Play(input != Vector2.Zero ? "Walk" : "Idle");

		reportTimer += (float)delta;
		if (reportTimer >= ReportInterval)
		{
			reportTimer = 0f;
			GameManager.Instance.Conn?.Reducers.ReportMovement(GlobalPosition.X, GlobalPosition.Y, Rotation);
		}
	}

	private void OnDataUpdate(EventContext _, PlayerData old, PlayerData data) => ApplyData(data);
	private void OnStatsUpdate(EventContext _, PlayerStats old, PlayerStats stats) => ApplyStats(stats);
	private void OnActiveProfileUpdate(EventContext _, PlayerProfile old, PlayerProfile profile) => EmitSignal("AimSettingsChanged");

	private void OnPositionUpdate(EventContext _, PlayerPosition old, PlayerPosition position)
	{
		var serverPos = new Vector2(position.X, position.Y);
		if (GlobalPosition.DistanceSquaredTo(serverPos) > WrapSnapThreshold * WrapSnapThreshold)
		{
			GD.Print($"[Wrap] {GlobalPosition} → {serverPos} (dist={GlobalPosition.DistanceTo(serverPos):F1})");
			GlobalPosition = serverPos;
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
		Hp    = data.Hp;
		MaxHp = data.MaxHp;
	}

	private void ApplyStats(PlayerStats stats)
	{
		Strength  = stats.Strength;
		Wisdom    = stats.Wisdom;
		Dexterity = stats.Dexterity;
		Defense   = stats.Defense;
		Vitality  = stats.Vitality;
		Speed     = stats.Speed;
	}

	private IReadOnlyList<ResolvedSlot> BuildResolvedSlots(int startIndex, int count) =>
		Enumerable.Range(startIndex, count).Select(ResolveSlotAt).ToList();

	private IReadOnlyList<T?> BuildTypedSlots<T>(int startIndex, int count, System.Func<string, T?> resolver) where T : class =>
		Enumerable.Range(startIndex, count).Select(i => GetSlotItem(i, resolver)).ToList();

	private ResolvedSlot ResolveSlotAt(int index)
	{
		if (index >= inventorySlots.Count) return new ResolvedSlot();
		var slot = inventorySlots[index];
		if (slot.ItemId == null) return new ResolvedSlot { Slot = slot };
		var item = GameManager.Instance.GetItem(slot.ItemId);
		if (item == null) return new ResolvedSlot { Slot = slot };
		return new ResolvedSlot {
			Slot       = slot,
			Item       = item,
			Weapon     = item.ItemType == ItemType.Weapon     ? GameManager.Instance.GetWeapon(slot.ItemId)     : null,
			Artifact   = item.ItemType == ItemType.Ability    ? GameManager.Instance.GetArtifact(slot.ItemId)   : null,
			Consumable = item.ItemType == ItemType.Consumable ? GameManager.Instance.GetConsumable(slot.ItemId) : null,
			Armor      = item.ItemType == ItemType.Armor      ? GameManager.Instance.GetArmor(slot.ItemId)      : null,
			Accessory  = item.ItemType == ItemType.Accessory  ? GameManager.Instance.GetAccessory(slot.ItemId)  : null,
		};
	}

	private T? GetSlotItem<T>(int index, System.Func<string, T?> resolver) where T : class
	{
		if (index >= inventorySlots.Count) return null;
		var itemId = inventorySlots[index].ItemId;
		return itemId != null ? resolver(itemId) : null;
	}

	public override void _ExitTree()
	{
		if (Local == this) Local = null;
		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.LocalPlayerData.OnUpdate            -= OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate           -= OnStatsUpdate;
		conn.Db.LocalPlayerPosition.OnUpdate        -= OnPositionUpdate;
		conn.Db.LocalPlayerInventory.OnUpdate       -= OnInventoryUpdate;
		conn.Db.LocalPlayerActiveProfile.OnUpdate   -= OnActiveProfileUpdate;
	}
}
