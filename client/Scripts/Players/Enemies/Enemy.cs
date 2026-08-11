#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Linq;

public partial class Enemy : CharacterBody2D, IEntity
{
    /// 3D model mirrored into the World3DComponent viewport (same pattern as LocalPlayer's KnightScene).
    [Export] public PackedScene? SkeletonScene { get; set; }

    public ulong EnemyId { get; set; }
    public byte Phase { get; private set; }
    public bool IsElite { get; private set; }

    // IEntity — Enemy can't derive from Entity (it needs CharacterBody2D), so it implements
    // the interface directly with its own registry; same pattern as LocalPlayer.
    private readonly EntityRegistry componentRegistry = new();
    public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
    public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
    public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
    public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

    private HealthComponent? HealthComponent => GetComponent<HealthComponent>();
    private StatsComponent? StatsComponent => GetComponent<StatsComponent>();

    private InterpolationComponent? interpolation;
    private AnimatedSprite2D? sprite;
    private uint maxHp;

    private CharacterModel3D? _model3D;

    private TableBinderComponent nearbyEnemiesBinder = null!;
    private TableBinderComponent bulletPatternEventBinder = null!;

    public override void _Ready()
    {
        // Component children register themselves in their own _Ready, before this one runs.
        interpolation = GetComponent<InterpolationComponent>();

        nearbyEnemiesBinder = GetNode<TableBinderComponent>("NearbyEnemiesBinder");
        bulletPatternEventBinder = GetNode<TableBinderComponent>("BulletPatternEventBinder");

        var world3D = this.GetAncestor<GameManager>()?.GetComponent<World3DComponent>();
        if (SkeletonScene != null && world3D != null)
            _model3D = new CharacterModel3D(SkeletonScene, GlobalPosition, world3D);
    }

    public override void _Process(double delta)
    {
        var moving = interpolation?.Moving ?? false;
        if (sprite?.SpriteFrames != null)
            sprite.Play(moving ? "Walk" : "Idle");
        _model3D?.SyncFrom2D(GlobalPosition, Rotation);
    }

    public override void _ExitTree() => _model3D?.Dispose();

    // --- TableBinderComponent signal handlers (wired in default_enemy.tscn) ---
    // The NearbyEnemies binder has ReplayExistingRows on, so a row already in the client
    // cache comes through the same insert path — no separate Iter() replay here.

    private void OnEnemyRowInserted()
    {
        var row = (SpacetimeDB.Types.Enemy)nearbyEnemiesBinder.LastRow!;
        if (row.EnemyId != EnemyId) return;
        interpolation?.SnapTo(new Vector2(row.X, row.Y));
        Phase = row.Phase;
        IsElite = row.IsElite;
        sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        var template = GameManager.Conn?.Db.EnemyTemplates.Iter().FirstOrDefault(t => t.TemplateId == row.TemplateId);
        if (template != null && sprite != null)
        {
            var resPath = GameManager.GetResPath(template.TextureId);
            if (resPath != null)
                sprite.SpriteFrames = GD.Load<SpriteFrames>(resPath);
        }
        // The server defines the component values: max_hp from the template, hp from the live row.
        maxHp = template?.MaxHp ?? row.Hp;
        HealthComponent?.SetFromServer(row.Hp, maxHp);
        if (HealthComponent != null && StatsComponent != null)
            StatsComponent.RegisterStat(StatKind.Hp, HealthComponent.Health);
    }

    private void OnEnemyRowUpdated()
    {
        var enemy = (SpacetimeDB.Types.Enemy)nearbyEnemiesBinder.LastRow!;
        if (enemy.EnemyId != EnemyId) return;
        interpolation?.SetTarget(new Vector2(enemy.X, enemy.Y));
        HealthComponent?.SetFromServer(enemy.Hp, maxHp);
        Phase = enemy.Phase;
    }

    private void OnBulletPatternRowInserted()
    {
        var bulletPattern = (BulletPatternEvent)bulletPatternEventBinder.LastRow!;
        if (bulletPattern.EnemyId != EnemyId) return;
        BulletManager.Instance.SpawnEnemyBullet(bulletPattern);
    }
}
