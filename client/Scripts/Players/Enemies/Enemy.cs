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
    private Vector2 targetPosition;
    private AnimatedSprite2D? sprite;

    public override void _Ready()
    {
        targetPosition = GlobalPosition;

        var conn = GameManager.Instance.Conn;
        if (conn == null) return;

        conn.Db.NearbyEnemies.OnUpdate += OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert += OnBulletPatternInsert;

        foreach (var row in conn.Db.NearbyEnemies.Iter())
        {
            if (row.EnemyId != EnemyId) continue;
            targetPosition = new Vector2(row.X, row.Y);
            GlobalPosition = targetPosition;
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
        var moving = GlobalPosition.DistanceSquaredTo(targetPosition) > 1f;
        GlobalPosition = GlobalPosition.Lerp(targetPosition, LerpSpeed * (float)delta);
        if (sprite?.SpriteFrames != null)
            sprite.Play(moving ? "Walk" : "Idle");
    }

    private void OnEnemyUpdate(EventContext _, SpacetimeDB.Types.Enemy old, SpacetimeDB.Types.Enemy enemy)
    {
        if (enemy.EnemyId != EnemyId) return;
        targetPosition = new Vector2(enemy.X, enemy.Y);
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
