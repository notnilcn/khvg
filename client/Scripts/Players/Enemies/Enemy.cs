#nullable enable
using Godot;
using System.Linq;

public partial class Enemy : CharacterBody2D, IEntity
{
    public ulong EnemyId { get; set; }
    public byte Phase { get; private set; }
    public bool IsElite { get; private set; }
    /// 0–100 stagger bar, mirrored from the row for EnemyDebuffIndicatorComponent.
    public float Stagger { get; private set; }

    // IEntity — Enemy can't derive from Entity (it needs CharacterBody2D), so it implements
    // the interface directly with its own registry; same pattern as LocalPlayer.
    private readonly EntityRegistry componentRegistry = new();
    public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
    public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
    public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
    public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

    private HealthComponent? HealthComponent => GetComponent<HealthComponent>();

    private InterpolationComponent? interpolation;
    private AnimatedSprite2D? sprite;
    private uint maxHp;

    private CharacterModel3D? _model3D;
    private World3DComponent? world3D;

    private TableBinderComponent nearbyEnemiesBinder = null!;

    public override void _Ready()
    {
        // Component children register themselves in their own _Ready, before this one runs.
        interpolation = GetComponent<InterpolationComponent>();

        nearbyEnemiesBinder = GetNode<TableBinderComponent>("NearbyEnemiesBinder");

        world3D = this.GetAncestor<GameManager>()?.GetComponent<World3DComponent>();
    }

    public override void _Process(double delta)
    {
        var moving = interpolation?.Moving ?? false;
        // 3D mode hides the sprite and never loads the template SpriteFrames — the
        // scene's placeholder frames have no Walk/Idle animations to play.
        if (!GameVisuals.Is3D && sprite?.SpriteFrames != null)
            sprite.Play(moving ? "Walk" : "Idle");
        _model3D?.SyncFrom2D(GlobalPosition, Rotation, GameManager.ElevationMap?.GetWorldHeightAt(GlobalPosition) ?? 0f);
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
        Stagger = row.Stagger;
        sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        var template = GameManager.Conn?.Db.EnemyTemplates.Iter().FirstOrDefault(t => t.TemplateId == row.TemplateId);
        // 3D mode renders only the catalog-driven model below — skip the 2D sprite entirely.
        if (GameVisuals.Is3D)
        {
            if (sprite != null) sprite.Visible = false;
        }
        else if (template != null && sprite != null)
        {
            var resPath = GameManager.GetResPath2D(template.TextureId);
            if (resPath != null)
                sprite.SpriteFrames = GD.Load<SpriteFrames>(resPath);
        }
        // 3D model from the template texture's catalog res_path3d (loaded here, not
        // scene-declared, so 2D mode never loads the FBX). No World3DComponent in 2D mode.
        if (template != null && world3D != null)
        {
            var resPath3D = GameManager.GetResPath3D(template.TextureId);
            if (resPath3D != null)
                _model3D = new CharacterModel3D(GD.Load<PackedScene>(resPath3D), GlobalPosition, world3D.CharacterRoot ?? world3D);
        }
        // The server defines the component values: max_hp from the template, hp from the live row.
        maxHp = template?.MaxHp ?? row.Hp;
        HealthComponent?.SetFromServer(row.Hp, maxHp);
    }

    private void OnEnemyRowUpdated()
    {
        var enemy = (SpacetimeDB.Types.Enemy)nearbyEnemiesBinder.LastRow!;
        if (enemy.EnemyId != EnemyId) return;
        var velocity = Vector2.FromAngle(enemy.MovementDirection) * enemy.MovementSpeed;
        interpolation?.SetTarget(new Vector2(enemy.X, enemy.Y), velocity);
        HealthComponent?.SetFromServer(enemy.Hp, maxHp);
        Phase = enemy.Phase;
        Stagger = enemy.Stagger;
    }
}
