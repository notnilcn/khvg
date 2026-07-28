#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Linq;

public partial class Enemy : CharacterBody2D, IEntity
{
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

    public override void _Ready()
    {
        // Component children register themselves in their own _Ready, before this one runs.
        interpolation = GetComponent<InterpolationComponent>();

        var conn = GameManager.Conn;
        if (conn == null) return;

        conn.Db.NearbyEnemies.OnUpdate += OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert += OnBulletPatternInsert;

        foreach (var row in conn.Db.NearbyEnemies.Iter())
        {
            if (row.EnemyId != EnemyId) continue;
            interpolation?.SnapTo(new Vector2(row.X, row.Y));
            Phase = row.Phase;
            IsElite = row.IsElite;
            sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
            var template = conn.Db.EnemyTemplates.Iter().FirstOrDefault(t => t.TemplateId == row.TemplateId);
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
            break;
        }
    }

    public override void _Process(double delta)
    {
        var moving = interpolation?.Moving ?? false;
        if (sprite?.SpriteFrames != null)
            sprite.Play(moving ? "Walk" : "Idle");
    }

    private void OnEnemyUpdate(EventContext _, SpacetimeDB.Types.Enemy old, SpacetimeDB.Types.Enemy enemy)
    {
        if (enemy.EnemyId != EnemyId) return;
        interpolation?.SetTarget(new Vector2(enemy.X, enemy.Y));
        HealthComponent?.SetFromServer(enemy.Hp, maxHp);
        Phase = enemy.Phase;
    }

    private void OnBulletPatternInsert(EventContext _, BulletPatternEvent bulletPattern)
    {
        if (bulletPattern.EnemyId != EnemyId) return;
        BulletManager.Instance.SpawnEnemyBullet(bulletPattern);
    }

    public override void _ExitTree()
    {
        var conn = GameManager.Conn;
        if (conn == null) return;
        conn.Db.NearbyEnemies.OnUpdate -= OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert -= OnBulletPatternInsert;
    }
}
