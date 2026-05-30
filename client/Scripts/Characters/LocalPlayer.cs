#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Linq;

public partial class LocalPlayer : CharacterBody2D
{
	[Export] public PackedScene? KnightScene { get; set; }

	public static LocalPlayer? Local { get; private set; }

	public uint Level { get; private set; }
	public uint Hp { get; private set; }
	public uint MaxHp { get; private set; }
	public int Strength { get; private set; }
	public int Wisdom { get; private set; }
	public int Dexterity { get; private set; }
	public int Defense { get; private set; }
	public int Vitality { get; private set; }
	public int Speed { get; private set; }

	private const float SpeedPerStat = 4f;
	// Any server correction larger than this is a torus wrap, not latency jitter.
	// Max legitimate drift = MaxSpeed * ReportInterval ≈ 100 * 0.1 = 10 units.
	private const float WrapSnapThreshold = 50f;

	private CharacterModel3D? model;
	private AnimatedSprite2D sprite = null!;
	private float reportTimer = 0f;
	private const float ReportInterval = 0.1f;

	public override void _Ready()
	{
		Local = this;
		sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		if (KnightScene != null && World3d.Instance != null)
			model = new CharacterModel3D(KnightScene, GlobalPosition, World3d.Instance.Scene3D);
		World3d.Instance?.SetCameraFollowTarget(this);

		var conn = GameManager.Instance.Conn;
		if (conn == null) return;

		conn.Db.LocalPlayerData.OnUpdate += OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate += OnStatsUpdate;
		conn.Db.LocalPlayerPosition.OnUpdate += OnPositionUpdate;

		foreach (var row in conn.Db.LocalPlayerData.Iter())
			ApplyData(row);
		foreach (var row in conn.Db.LocalPlayerStats.Iter())
			ApplyStats(row);

		var loggedIn = conn.Db.LocalPlayer.Iter().FirstOrDefault();
		if (loggedIn != null)
		{
			var profile = conn.Db.LocalPlayerProfiles.Iter()
				.FirstOrDefault(p => p.ProfileId == loggedIn.ProfileId);
			if (profile != null)
			{
				var resPath = GameManager.Instance.GetResPath(profile.TextureId);
				if (resPath != null)
					sprite.SpriteFrames = GD.Load<SpriteFrames>(resPath);
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		var raw = Input.GetVector("Left", "Right", "Up", "Down").Normalized();
		float yaw = World3d.Instance?.CameraYaw ?? 0f;
		var rot3d = new Vector3(raw.X, 0f, raw.Y).Rotated(Vector3.Up, yaw);
		var input = new Vector2(rot3d.X, rot3d.Z);

		Rotation = GlobalPosition.AngleToPoint(GetGlobalMousePosition());

		Velocity = input * Speed * SpeedPerStat;
		MoveAndSlide();
		model?.SyncFrom2D(GlobalPosition, Rotation);

		if (sprite.SpriteFrames != null)
			sprite.Play(input != Vector2.Zero ? "Walk" : "Idle");

		reportTimer += (float)delta;
		if (reportTimer >= ReportInterval)
		{
			reportTimer = 0f;
			GameManager.Instance.Conn?.Reducers.ReportMovement(GlobalPosition.X, GlobalPosition.Y, Rotation);
		}
	}

	private void OnDataUpdate(EventContext _, PlayerData old, PlayerData data) => ApplyData(data);
	private void OnStatsUpdate(EventContext _, PlayerStats old, PlayerStats stats) => ApplyStats(stats);

	private void OnPositionUpdate(EventContext _, PlayerPosition old, PlayerPosition position)
	{
		var serverPos = new Vector2(position.X, position.Y);
		if (GlobalPosition.DistanceSquaredTo(serverPos) > WrapSnapThreshold * WrapSnapThreshold)
			GlobalPosition = serverPos;
	}

	private void ApplyData(PlayerData data)
	{
		Level = data.Level;
		Hp = data.Hp;
		MaxHp = data.MaxHp;
	}

	private void ApplyStats(PlayerStats stats)
	{
		Strength = stats.Strength;
		Wisdom = stats.Wisdom;
		Dexterity = stats.Dexterity;
		Defense = stats.Defense;
		Vitality = stats.Vitality;
		Speed = stats.Speed;
	}

	public override void _ExitTree()
	{
		model?.Dispose();
		if (Local == this) Local = null;
		World3d.Instance?.ClearCameraFollowTarget();
		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.LocalPlayerData.OnUpdate -= OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate -= OnStatsUpdate;
		conn.Db.LocalPlayerPosition.OnUpdate -= OnPositionUpdate;
	}
}
