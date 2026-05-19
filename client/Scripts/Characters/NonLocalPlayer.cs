#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class NonLocalPlayer : Node2D
{
	public Identity PlayerId { get; set; }

	private const float LerpSpeed = 10f;

	private Vector2 _targetPosition;
	private float   _targetRotation;

	public override void _Ready()
	{
		_targetPosition = GlobalPosition;
		_targetRotation = Rotation;

		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.NearbyRemotePlayers.OnUpdate += OnPositionUpdate;
	}

	public override void _Process(double delta)
	{
		GlobalPosition = GlobalPosition.Lerp(_targetPosition, LerpSpeed * (float)delta);
		Rotation       = Mathf.LerpAngle(Rotation, _targetRotation, LerpSpeed * (float)delta);
	}

	private void OnPositionUpdate(EventContext ctx, PlayerPosition old, PlayerPosition row)
	{
		if (row.PlayerId != PlayerId) return;
		_targetPosition = new Vector2(row.X, row.Y);
		_targetRotation = row.Rotation;
	}

	public override void _ExitTree()
	{
		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.NearbyRemotePlayers.OnUpdate -= OnPositionUpdate;
	}
}
