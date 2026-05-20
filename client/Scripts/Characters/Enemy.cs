#nullable enable
using Godot;
using SpacetimeDB.Types;
using EnemyRow = SpacetimeDB.Types.Enemy;

public partial class Enemy : CharacterBody2D
{
    public ulong EnemyId { get; set; }
    public uint Hp { get; private set; }
    public byte Phase { get; private set; }
    public bool IsElite { get; private set; }

    private const float LerpSpeed = 10f;
    private Vector2 _targetPosition;

    public override void _Ready()
    {
        _targetPosition = GlobalPosition;

        var conn = GameManager.Instance.Conn;
        if (conn == null) return;

        conn.Db.NearbyEnemies.OnUpdate += OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert += OnBulletPatternInsert;

        foreach (var row in conn.Db.NearbyEnemies.Iter())
        {
            if (row.EnemyId != EnemyId) continue;
            _targetPosition = new Vector2(row.X, row.Y);
            GlobalPosition = _targetPosition;
            Hp = row.Hp;
            Phase = row.Phase;
            IsElite = row.IsElite;
            break;
        }
    }

    public override void _Process(double delta)
    {
        GlobalPosition = GlobalPosition.Lerp(_targetPosition, LerpSpeed * (float)delta);
    }

    private void OnEnemyUpdate(EventContext ctx, EnemyRow old, EnemyRow row)
    {
        if (row.EnemyId != EnemyId) return;
        _targetPosition = new Vector2(row.X, row.Y);
        Hp = row.Hp;
        Phase = row.Phase;
    }

    private void OnBulletPatternInsert(EventContext ctx, BulletPatternEvent evt)
    {
        if (evt.EnemyId != EnemyId) return;
        BulletManager.Instance.Spawn(evt);
    }

    public override void _ExitTree()
    {
        var conn = GameManager.Instance.Conn;
        if (conn == null) return;
        conn.Db.NearbyEnemies.OnUpdate -= OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert -= OnBulletPatternInsert;
    }
}
