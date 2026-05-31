#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Linq;

public partial class LocalPlayer : CharacterBody2D
{
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

	private const float SpeedPerStat      = 4f;
	private const float WrapSnapThreshold = 50f;

	private AnimatedSprite2D sprite = null!;
	private float reportTimer       = 0f;
	private const float ReportInterval = 0.1f;

	public override void _Ready()
	{
		Local  = this;
		sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");

		var conn = GameManager.Instance.Conn;
		if (conn == null) return;

		conn.Db.LocalPlayerData.OnUpdate     += OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate    += OnStatsUpdate;
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
		var input = Input.GetVector("Left", "Right", "Up", "Down").Normalized();

		Rotation = GlobalPosition.AngleToPoint(GetGlobalMousePosition());

		Velocity = input * Speed * SpeedPerStat;
		MoveAndSlide();

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
		Hp    = data.Hp;
		MaxHp = data.MaxHp;
	}

	private void ApplyStats(PlayerStats stats)
	{
		Strength  = stats.Strength;
		Wisdom    = stats.Wisdom;
		Dexterity = stats.Dexterity;
		Defense   = stats.Defense;
		Vitality  = stats.Vitality;
		Speed     = stats.Speed;
	}

	public override void _ExitTree()
	{
		if (Local == this) Local = null;
		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.LocalPlayerData.OnUpdate     -= OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate    -= OnStatsUpdate;
		conn.Db.LocalPlayerPosition.OnUpdate -= OnPositionUpdate;
	}
}
