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
/// pass-through properties below keep UI readers (StatsSidebarComponent,
/// InventoryComponent, CombatComponent) on one stable surface.
/// </summary>
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

    // Raised by the data components; the signals themselves stay on the entity so UI
    // readers keep one stable source.
    internal void RaiseInventoryChanged() => EmitSignal(SignalName.InventoryChanged);
    internal void RaiseAimSettingsChanged() => EmitSignal(SignalName.AimSettingsChanged);
    internal void RaiseStatsChanged() => EmitSignal(SignalName.StatsChanged);

    public string Username => GameManager.Username;

    // Stat/hp/inventory/profile values live in the child components (mirrors of server
    // rows); these pass-throughs keep UI readers (e.g. StatsSidebarComponent) stable.
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
    public int Defense => StatsComponent?.GetValue(StatKind.Defense) ?? 0;
    public int Vitality => StatsComponent?.GetValue(StatKind.Vitality) ?? 0;
    public int Speed => StatsComponent?.GetValue(StatKind.Speed) ?? 0;

    private const float SpeedPerStat = 10f;

    private CharacterModel3D? _model3D;
    private AnimatedSprite2D sprite = null!;

    // Camera components of the Main entity, cached in _Ready (per-frame access in
    // _PhysicsProcess makes an ancestor/registry walk per call wasteful).
    private World3DComponent? world3D;
    private Camera2DPresenterComponent? camera2D;
    private CameraRigComponent? cameraRig;

    // Slot 0: weapon only
    public Item? EquippedWeapon => InventoryState?.EquippedWeapon;

    // Slots 1-4: consumables only hotbar
    public IReadOnlyList<Item?> HotbarSlots => InventoryState?.HotbarSlots ?? [];

    // Slots 5-8: accessories only
    public IReadOnlyList<Item?> AccessorySlots => InventoryState?.AccessorySlots ?? [];

    // Slots 9-12: armor only
    public IReadOnlyList<Item?> ArmorSlots => InventoryState?.ArmorSlots ?? [];

    // Slots 13-14: artifact only
    public IReadOnlyList<Item?> ArtifactSlots => InventoryState?.ArtifactSlots ?? [];

    // Slots 15-22: general mixed
    public IReadOnlyList<ResolvedSlot> GeneralSlots => InventoryState?.GeneralSlots ?? [];

    // Slots that count as worn equipment (enchantable): 0 weapon, 5-8 accessories, 9-12 armor, 13-14 artifacts
    public static bool IsEquipmentSlot(int index) => LocalPlayerInventoryComponent.IsEquipmentSlot(index);

    public ResolvedSlot ResolveSlotAt(int index) => InventoryState?.ResolveSlotAt(index) ?? new ResolvedSlot();

    public string? GetSlotItemId(int index) => InventoryState?.GetSlotItemId(index);

    public override void _Ready()
    {
        Local = this;
        sprite = GetNode<AnimatedSprite2D>("Sprite");

        var gameManager = this.GetAncestor<GameManager>();
        world3D = gameManager?.GetComponent<World3DComponent>();
        camera2D = gameManager?.GetComponent<Camera2DPresenterComponent>();
        cameraRig = gameManager?.GetComponent<CameraRigComponent>();

        if (KnightScene != null && world3D != null)
        {
            _model3D = new CharacterModel3D(KnightScene, GlobalPosition, world3D);
            world3D.SetCameraFollowTarget(_model3D.Node);
        }

        var pcamNode = GetNode<Node2D>("%LocalPlayerPhantomCamera2D");
        camera2D?.RegisterCamera(pcamNode);
    }


    public override void _PhysicsProcess(double delta)
    {
        var raw = Input.GetVector("Left", "Right", "Up", "Down").Normalized();
        float yaw = cameraRig?.Yaw ?? 0f;
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
    }

    public override void _ExitTree()
    {
        if (Local == this) Local = null;
        _model3D?.Dispose();
        if (IsInstanceValid(world3D)) world3D!.ClearCameraFollowTarget();
        if (IsInstanceValid(camera2D)) camera2D!.UnregisterCamera();
    }
}
