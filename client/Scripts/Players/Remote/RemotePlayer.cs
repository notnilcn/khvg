#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Linq;

public partial class RemotePlayer : Node2D
{
	public Identity PlayerId  { get; set; }
	public ulong    ProfileId { get; set; }

	private const float LerpSpeed         = 10f;
	private const float WrapSnapThreshold = 50f;
	private const float SpeedPerStat      = 4f;

	private Vector2 canonicalPosition;
	private Vector2 snapVelocity;
	private float   timeSinceSnap;
	private float   targetRotation;
	private AnimatedSprite2D? sprite;

	public override void _Ready()
	{
		canonicalPosition = GlobalPosition;
		snapVelocity   = Vector2.Zero;
		timeSinceSnap  = 0f;
		targetRotation = Rotation;

		sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");

		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.NearbyRemotePlayers.OnUpdate += OnPositionUpdate;

		var profile = conn.Db.NearbyRemotePlayersProfiles.Iter()
			.FirstOrDefault(p => p.ProfileId == ProfileId);
		if (profile != null)
		{
			var resPath = GameManager.Instance.GetResPath(profile.TextureId);
			if (resPath != null && sprite != null)
				sprite.SpriteFrames = GD.Load<SpriteFrames>(resPath);
		}
	}

	public override void _Process(double delta)
	{
		timeSinceSnap += (float)delta;
		var canonicalExtrapolated = canonicalPosition + snapVelocity * timeSinceSnap;
		var nearest = TorusMath.NearestCandidate(canonicalExtrapolated, GlobalPosition, GameManager.Instance.LapQ, GameManager.Instance.LapR);
		var moving = GlobalPosition.DistanceSquaredTo(nearest) > 1f;
		GlobalPosition = GlobalPosition.Lerp(nearest, LerpSpeed * (float)delta);
		Rotation       = Mathf.LerpAngle(Rotation, targetRotation, LerpSpeed * (float)delta);
		if (sprite?.SpriteFrames != null)
			sprite.Play(moving ? "Walk" : "Idle");
	}

	private void OnPositionUpdate(EventContext _, PlayerPosition old, PlayerPosition position)
	{
		if (position.PlayerId != PlayerId) return;

		canonicalPosition = new Vector2(position.X, position.Y);
		timeSinceSnap  = 0f;
		targetRotation = position.Rotation;

		// Only an instant correction if even the nearest wrapped copy is still far away — a real
		// desync (lag/teleport), not a routine wrap (which the per-frame candidate pick already
		// handles smoothly without ever needing to snap).
		var nearest = TorusMath.NearestCandidate(canonicalPosition, GlobalPosition, GameManager.Instance.LapQ, GameManager.Instance.LapR);
		if (GlobalPosition.DistanceSquaredTo(nearest) > WrapSnapThreshold * WrapSnapThreshold)
			GlobalPosition = nearest;

		var stats = GameManager.Instance.Conn?.Db.PlayerStats.ProfileId.Find(ProfileId);
		float speed  = (stats?.Speed ?? 0) * SpeedPerStat;
		snapVelocity = new Vector2(Mathf.Cos(position.Rotation), Mathf.Sin(position.Rotation)) * speed;
	}

	public override void _ExitTree()
	{
		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.NearbyRemotePlayers.OnUpdate -= OnPositionUpdate;
	}
}
