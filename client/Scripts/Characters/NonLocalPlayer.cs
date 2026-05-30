#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Linq;

public partial class NonLocalPlayer : Node2D
{
	[Export] public PackedScene? PlayerModelScene { get; set; }

	public Identity PlayerId { get; set; }
	public ulong ProfileId { get; set; }

	private const float LerpSpeed = 10f;

	private Vector2 targetPosition;
	private float targetRotation;
	private CharacterModel3D? model;
	private AnimatedSprite2D? sprite;

	public override void _Ready()
	{
		targetPosition = GlobalPosition;
		targetRotation = Rotation;

		sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");

		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.NearbyRemotePlayers.OnUpdate += OnPositionUpdate;

		if (PlayerModelScene != null && World3d.Instance != null)
			model = new CharacterModel3D(PlayerModelScene, GlobalPosition, World3d.Instance.Scene3D);

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
		var moving = GlobalPosition.DistanceSquaredTo(targetPosition) > 1f;
		GlobalPosition = GlobalPosition.Lerp(targetPosition, LerpSpeed * (float)delta);
		Rotation = Mathf.LerpAngle(Rotation, targetRotation, LerpSpeed * (float)delta);
		model?.SyncFrom2D(GlobalPosition, targetRotation);
		if (sprite?.SpriteFrames != null)
			sprite.Play(moving ? "Walk" : "Idle");
	}

	private void OnPositionUpdate(EventContext _, PlayerPosition old, PlayerPosition position)
	{
		if (position.PlayerId != PlayerId) return;
		targetPosition = new Vector2(position.X, position.Y);
		targetRotation = position.Rotation;
	}

	public override void _ExitTree()
	{
		model?.Dispose();
		var conn = GameManager.Instance.Conn;
		if (conn == null) return;
		conn.Db.NearbyRemotePlayers.OnUpdate -= OnPositionUpdate;
	}
}
