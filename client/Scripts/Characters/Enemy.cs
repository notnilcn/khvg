#nullable enable
using Godot;
using SpacetimeDB.Types;
using EnemyRow = SpacetimeDB.Types.Enemy;

public partial class Enemy : CharacterBody2D
{
    [Export] public PackedScene? EnemyModelScene { get; set; }

    public ulong EnemyId { get; set; }
    public uint Hp { get; private set; }
    public byte Phase { get; private set; }
    public bool IsElite { get; private set; }

    private const float LerpSpeed = 10f;
    private Vector2 _targetPosition;
    private CharacterModel3D? _model;

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

        if (EnemyModelScene != null && World3d.Instance != null)
            _model = new CharacterModel3D(EnemyModelScene, GlobalPosition, World3d.Instance.Scene3D);
    }

    public override void _Process(double delta)
    {
        GlobalPosition = GlobalPosition.Lerp(_targetPosition, LerpSpeed * (float)delta);
        _model?.SyncFrom2D(GlobalPosition, Rotation);
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
        _model?.Dispose();
        var conn = GameManager.Instance.Conn;
        if (conn == null) return;
        conn.Db.NearbyEnemies.OnUpdate -= OnEnemyUpdate;
        conn.Db.BulletPatternEvent.OnInsert -= OnBulletPatternInsert;
    }
}
