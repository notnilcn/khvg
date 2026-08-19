#nullable enable
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Bullet entity root, declared inline in game.tscn, and the IEntity for the bullet components. Owns the
/// reference to the BlastBullets2D BulletFactory2D child (a GDExtension node — stays a
/// plain child, reached via [Export] NodePath instead of GetChild(0)); spawning lives in
/// BulletSpawnerComponent and overlap routing in BulletHitRouterComponent. Keeps a static
/// Instance plus spawn pass-throughs because external entities (the local player's
/// CombatComponent, Enemy puppets) have no registry path to this entity.
/// </summary>
public partial class BulletManager : Node2D, IEntity
{
    public static BulletManager Instance { get; private set; } = null!;

    /// The BlastBullets2D factory node (GDExtension — stays a plain child).
    [Export] public Node BlastBullets { get; set; } = null!;

    // IEntity — BulletManager needs Node2D, so it implements the interface directly with
    // its own registry; same pattern as LocalPlayer/Enemy.
    private readonly EntityRegistry componentRegistry = new();
    public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
    public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
    public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
    public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

    public override void _Ready() => Instance = this;

    public void SpawnPlayerBullet(Vector2 origin, float angle, float lifetime, float speed, string textureId) =>
        GetComponent<BulletSpawnerComponent>()?.SpawnPlayerBullet(origin, angle, lifetime, speed, textureId);

    public void SpawnPlayerBullets(Vector2 origin, float[] angles, float lifetime, float speed, string textureId) =>
        GetComponent<BulletSpawnerComponent>()?.SpawnPlayerBullets(origin, angles, lifetime, speed, textureId);

    public void SpawnEnemyBullet(BulletPatternEvent bulletPattern) =>
        GetComponent<BulletSpawnerComponent>()?.SpawnEnemyBullet(bulletPattern);

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null!;
    }
}
