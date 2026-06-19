#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Linq;

public partial class Enemy : CharacterBody2D
{
    public ulong EnemyId { get; set; }
    public uint Hp { get; private set; }
    public byte Phase { get; private set; }
    public bool IsElite { get; private set; }

    private const float LerpSpeed = 10f;
    private Vector2 canonicalPosition;
    private AnimatedSprite2D? sprite;

    public override void _Ready()
    {
        canonicalPosition = GlobalPosition;

        var conn = GameManager.Instance.Conn;
        if (conn == null) return;

        conn.Db.NearbyEnemies.OnUpdate += OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert += OnBulletPatternInsert;

        foreach (var row in conn.Db.NearbyEnemies.Iter())
        {
            if (row.EnemyId != EnemyId) continue;
            canonicalPosition = new Vector2(row.X, row.Y);
            GlobalPosition = NearestRenderPosition();
            Hp = row.Hp;
            Phase = row.Phase;
            IsElite = row.IsElite;
            sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
            var template = conn.Db.EnemyTemplates.Iter().FirstOrDefault(t => t.TemplateId == row.TemplateId);
            if (template != null && sprite != null)
            {
                var resPath = GameManager.Instance.GetResPath(template.TextureId);
                if (resPath != null)
                    sprite.SpriteFrames = GD.Load<SpriteFrames>(resPath);
            }
            break;
        }
    }

    public override void _Process(double delta)
    {
        var targetPosition = NearestRenderPosition();
        var moving = GlobalPosition.DistanceSquaredTo(targetPosition) > 1f;
        GlobalPosition = GlobalPosition.Lerp(targetPosition, LerpSpeed * (float)delta);
        if (sprite?.SpriteFrames != null)
            sprite.Play(moving ? "Walk" : "Idle");
    }

    // The reference for "nearest" is our own render position rather than the camera directly —
    // cheaper, and equivalent since the camera always tracks the local player closely.
    private Vector2 NearestRenderPosition() =>
        TorusMath.NearestCandidate(canonicalPosition, GlobalPosition, GameManager.Instance.LapQ, GameManager.Instance.LapR);

    private void OnEnemyUpdate(EventContext _, SpacetimeDB.Types.Enemy old, SpacetimeDB.Types.Enemy enemy)
    {
        if (enemy.EnemyId != EnemyId) return;
        canonicalPosition = new Vector2(enemy.X, enemy.Y);
        Hp = enemy.Hp;
        Phase = enemy.Phase;
    }

    private void OnBulletPatternInsert(EventContext _, BulletPatternEvent bulletPattern)
    {
        if (bulletPattern.EnemyId != EnemyId) return;
        BulletManager.Instance.SpawnEnemyBullet(bulletPattern);
    }

    public override void _ExitTree()
    {
        var conn = GameManager.Instance.Conn;
        if (conn == null) return;
        conn.Db.NearbyEnemies.OnUpdate -= OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert -= OnBulletPatternInsert;
    }
}
