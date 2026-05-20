#nullable enable
using Godot;
using SpacetimeDB.Types;

public partial class LocalPlayer : CharacterBody2D
{
	public static LocalPlayer? Local { get; private set; }

	public uint Level    { get; private set; }
	public uint Hp       { get; private set; }
	public uint MaxHp    { get; private set; }
	public int Strength  { get; private set; }
	public int Wisdom    { get; private set; }
	public int Dexterity { get; private set; }
	public int Defense   { get; private set; }
	public int Vitality  { get; private set; }
	public int Speed     { get; private set; }

	private const float SpeedPerStat = 4f;

	private AnimatedSprite2D _sprite = null!;
	private float _reportTimer       = 0f;
	private const float ReportInterval = 0.1f;

	public override void _Ready()
	{
		Local   = this;
		_sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");

		var conn = GameManager.Instance.Conn;
		if (conn == null) return;

		conn.Db.LocalPlayerData.OnUpdate  += OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate += OnStatsUpdate;

		foreach (var row in conn.Db.LocalPlayerData.Iter())
			ApplyData(row);
		foreach (var row in conn.Db.LocalPlayerStats.Iter())
			ApplyStats(row);
	}

	public override void _PhysicsProcess(double delta)
	{
		var input = Input.GetVector("Left", "Right", "Up", "Down").Normalized();
		Rotation = GlobalPosition.AngleToPoint(GetGlobalMousePosition());

		Velocity = input * Speed * SpeedPerStat;
		MoveAndSlide();

		_sprite.Play(input != Vector2.Zero ? "Move" : "Idle");

		_reportTimer += (float)delta;
		if (_reportTimer >= ReportInterval)
		{
			_reportTimer = 0f;
			GameManager.Instance.Conn?.Reducers.ReportMovement(GlobalPosition.X, GlobalPosition.Y, Rotation);
		}
	}

	private void OnDataUpdate(EventContext ctx, PlayerData old, PlayerData row) => ApplyData(row);
	private void OnStatsUpdate(EventContext ctx, PlayerStats old, PlayerStats row) => ApplyStats(row);

	private void ApplyData(PlayerData row)
	{
		Level = row.Level;
		Hp    = row.Hp;
		MaxHp = row.MaxHp;
	}

	private void ApplyStats(PlayerStats row)
	{
		Strength  = row.Strength;
		Wisdom    = row.Wisdom;
		Dexterity = row.Dexterity;
		Defense   = row.Defense;
		Vitality  = row.Vitality;
		Speed     = row.Speed;
	}

	public override void _ExitTree()
	{
		if (Local == this) Local = null;
		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.LocalPlayerData.OnUpdate  -= OnDataUpdate;
		conn.Db.LocalPlayerStats.OnUpdate -= OnStatsUpdate;
	}
}
