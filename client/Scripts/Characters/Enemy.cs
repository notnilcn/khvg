#nullable enable
using Godot;
using SpacetimeDB.Types;
using EnemyRow = SpacetimeDB.Types.Enemy;

public partial class Enemy : Node2D
{
    public ulong EnemyId { get; set; }
    public uint  Hp      { get; private set; }
    public byte  Phase   { get; private set; }
    public bool  IsElite { get; private set; }

    public override void _Ready()
    {
        var conn = GameManager.Instance.Conn;
        if (conn == null) return;

        conn.Db.NearbyEnemies.OnUpdate      += OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert += OnBulletPatternInsert;

        foreach (var row in conn.Db.NearbyEnemies.Iter())
        {
            if (row.EnemyId != EnemyId) continue;
            Hp      = row.Hp;
            Phase   = row.Phase;
            IsElite = row.IsElite;
            break;
        }
    }

    private void OnEnemyUpdate(EventContext ctx, EnemyRow old, EnemyRow row)
    {
        if (row.EnemyId != EnemyId) return;
        GlobalPosition = new Vector2(row.X, row.Y);
        Hp    = row.Hp;
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
        conn.Db.NearbyEnemies.OnUpdate      -= OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert -= OnBulletPatternInsert;
    }
}
