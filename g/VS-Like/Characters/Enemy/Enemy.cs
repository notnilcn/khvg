using Godot;
using System.Collections.Generic;

public partial class Enemy : Sprite2D
{
    [Signal] public delegate void DiedEventHandler();
    [Signal] public delegate void RequestDespawnEventHandler(int eid);

    private static readonly PackedScene DropScene =
        GD.Load<PackedScene>("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn");
    private static readonly PackedScene FloatingTextScene =
        GD.Load<PackedScene>("res://VS-Like/Game/UI/FloatingText/floating_text.tscn");

    private AudioStreamPlayer2D _audioPlayer2D;

    private Rid _object;
    private Rid _shape;

    public ResEnemy ResEnemy    { get; set; }
    public float CurrHealth     { get; set; }
    public int Speed            { get; set; }
    public float Damage         { get; set; }
    public ResDrop ResDrop      { get; set; }
    public bool IsElite         { get; set; } = false;
    public bool Immortal        { get; set; } = false;
    public int EnemyId          { get; set; } = -1;

    private Vector2 _direction;
    private Node2D _target;

    private float _bulletCooldown = 0.0f;

    private const float HitTimingTolerance = 0.7f;
    private const float MinFireRate        = 0.05f;
    private readonly Dictionary<int, float> _lastHitTimes = new();

    public override async void _Ready()
    {
        _audioPlayer2D = GetNode<AudioStreamPlayer2D>("AudioPlayer2D");

        Texture = ResEnemy.Sprite;

        if (IsElite)
        {
            CurrHealth = ResEnemy.Health * 10;
            Material   = GD.Load<Material>("res://VS-Like/Characters/Enemy/Utilities/Shader/material_elite.tres");
            ResDrop    = GD.Load<ResDrop>("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Chest/res_chest.tres");
        }
        else
        {
            CurrHealth = ResEnemy.Health;
            ResDrop    = GD.Load<ResDrop>("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Point/res_point.tres");
        }

        Speed  = ResEnemy.Speed;
        Damage = ResEnemy.Damage;

        _SetupSync();

        if (Multiplayer.IsServer())
        {
            await _SetupPhysics();
            _target = _GetNearestPlayer();
        }
        else
        {
            _SetupClientHitbox();
        }
    }

    private void _SetupClientHitbox()
    {
        var body              = new StaticBody2D();
        body.Name              = "Hitbox";
        body.CollisionLayer    = 0b0100;
        body.CollisionMask     = 0;
        var shapeNode          = new CollisionShape2D();
        var circle             = new CircleShape2D();
        circle.Radius          = ResEnemy.Radius;
        shapeNode.Shape        = circle;
        body.AddChild(shapeNode);
        AddChild(body);
    }

    private void _SetupSync()
    {
        var sync   = new MultiplayerSynchronizer();
        sync.Name  = "Sync";
        var config = new SceneReplicationConfig();
        config.AddProperty(new NodePath(":position"));
        sync.ReplicationConfig = config;
        AddChild(sync);
    }

    private async System.Threading.Tasks.Task _SetupPhysics()
    {
        _object = PhysicsServer2D.BodyCreate();
        _shape  = PhysicsServer2D.CircleShapeCreate();

        PhysicsServer2D.ShapeSetData(_shape, ResEnemy.Radius);
        PhysicsServer2D.BodyAddShape(_object, _shape);
        PhysicsServer2D.BodySetSpace(_object, GetWorld2D().Space);
        PhysicsServer2D.BodySetState(
            _object, PhysicsServer2D.BodyState.Transform, new Transform2D(0, GlobalPosition));
        PhysicsServer2D.BodySetMode(_object, PhysicsServer2D.BodyMode.Rigid);
        PhysicsServer2D.BodySetParam(_object, PhysicsServer2D.BodyParameter.GravityScale, 0.0f);
        PhysicsServer2D.BodySetCollisionLayer(_object, 0b0100);
        PhysicsServer2D.BodySetCollisionMask(_object, 0b0101);

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        PhysicsServer2D.BodyAttachObjectInstanceId(_object, GetInstanceId());
    }

    public override void _Process(double delta)
    {
        if (!Multiplayer.IsServer())
            return;

        _MoveCharacter();
        _TickBulletFire((float)delta);

        var newTrans = (Transform2D)PhysicsServer2D.BodyGetState(_object, PhysicsServer2D.BodyState.Transform);
        GlobalPosition = newTrans.Origin;

        _target = _GetNearestPlayer();

        if (_DistanceToNearestPlayer() >= 600)
            QueueFree();
    }

    private void _TickBulletFire(float delta)
    {
        if (ResEnemy.BulletDamage <= 0.0f || _target == null)
            return;
        _bulletCooldown -= delta;
        if (_bulletCooldown > 0.0f)
            return;
        _bulletCooldown = ResEnemy.BulletFireInterval;
        var dir = GlobalPosition.DirectionTo(_target.GlobalPosition);
        BulletManager.Instance.ServerSpawnLinear(GlobalPosition, dir, ResEnemy.BulletSpeed, ResEnemy.BulletDamage);
    }

    private void _MoveCharacter()
    {
        if (_target == null) return;
        _direction = GlobalPosition.DirectionTo(_target.GlobalPosition).Normalized();
        PhysicsServer2D.BodySetState(
            _object, PhysicsServer2D.BodyState.LinearVelocity, _direction * Speed);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReportHit(float damageAmount, float fireRate)
    {
        if (damageAmount <= 0 || damageAmount > 500)
            return;

        int callerId = Multiplayer.GetRemoteSenderId();
        if (callerId == 0) callerId = Multiplayer.GetUniqueId(); // direct local call (server)
        if (!Global.Instance.Players.TryGetValue(callerId, out var callerPlayer))
            return;

        if (GlobalPosition.DistanceTo(callerPlayer.GlobalPosition) > Network.HitValidationRange)
            return;

        float effectiveRate = Mathf.Max(fireRate, MinFireRate);
        float now           = Time.GetTicksMsec() * 0.001f;
        float lastTime      = _lastHitTimes.TryGetValue(callerId, out var lt) ? lt : float.NegativeInfinity;
        if ((now - lastTime) < effectiveRate * HitTimingTolerance)
            return;

        _lastHitTimes[callerId] = now;
        Hurt(damageAmount);
    }

    public void Hurt(float dmg)
    {
        _audioPlayer2D.Play();
        if (Immortal) return;
        CurrHealth -= dmg;
        if (CurrHealth <= 0)
            Die();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ShowFloatingText(int dmg)
    {
        var floatingText = FloatingTextScene.Instantiate<FloatingText>();
        floatingText.GlobalPosition = GlobalPosition;
        floatingText.FloatingNumber = dmg;
        GetTree().CurrentScene.CallDeferred("add_child", floatingText);
    }

    private void Die()
    {
        if (IsElite || GD.Randf() > 0.25f)
            SpawnDrop();
        EmitSignal(SignalName.RequestDespawn, EnemyId);
    }

    public void SpawnDrop()
    {
        GetParent<Spawner>().Rpc(nameof(Spawner._RpcSpawnDrop), ResDrop.ResourcePath, GlobalPosition);
    }

    private Player _GetNearestPlayer()
    {
        Player nearest      = null;
        float nearestDist   = float.PositiveInfinity;
        foreach (var player in Global.Instance.Players.Values)
        {
            float dist = GlobalPosition.DistanceTo(player.GlobalPosition);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest     = player;
            }
        }
        return nearest;
    }

    private float _DistanceToNearestPlayer()
    {
        float minDist = float.PositiveInfinity;
        foreach (var player in Global.Instance.Players.Values)
        {
            float d = GlobalPosition.DistanceTo(player.GlobalPosition);
            if (d < minDist)
                minDist = d;
        }
        return minDist;
    }

    public override void _ExitTree()
    {
        EmitSignal(SignalName.Died);
        if (_object.IsValid)
            PhysicsServer2D.FreeRid(_object);
        if (_shape.IsValid)
            PhysicsServer2D.FreeRid(_shape);
    }
}
