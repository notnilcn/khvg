#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Linq;

/// <summary>
/// A remote player's puppet entity. Glue only: subscribes to the player's position rows
/// and forwards them to the InterpolationComponent; the RemoteVisualComponent owns the
/// sprite (SpriteFrames from the profile row, Walk/Idle from the interpolation state).
/// </summary>
public partial class RemotePlayer : Node2D, IEntity
{
	public Identity PlayerId  { get; set; }
	public ulong    ProfileId { get; set; }

	// IEntity — RemotePlayer needs Node2D, so it implements the interface directly with
	// its own registry; same pattern as LocalPlayer/Enemy.
	private readonly EntityRegistry componentRegistry = new();
	public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
	public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
	public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
	public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

	private const float SpeedPerStat = 4f;

	public override void _Ready()
	{
		var conn = GameManager.Conn;
		if (conn == null) return;
		conn.Db.NearbyRemotePlayers.OnUpdate += OnPositionUpdate;

		var profile = conn.Db.NearbyRemotePlayersProfiles.Iter()
			.FirstOrDefault(p => p.ProfileId == ProfileId);
		if (profile != null)
			GetComponent<RemoteVisualComponent>()?.SetTexture(profile.TextureId);
	}

	private void OnPositionUpdate(EventContext _, PlayerPosition old, PlayerPosition position)
	{
		if (position.PlayerId != PlayerId) return;

		var stats = GameManager.Conn?.Db.PlayerStats.ProfileId.Find(ProfileId);
		float speed = (stats?.Speed ?? 0) * SpeedPerStat;
		var velocity = new Vector2(Mathf.Cos(position.Rotation), Mathf.Sin(position.Rotation)) * speed;
		GetComponent<InterpolationComponent>()?.SetTarget(new Vector2(position.X, position.Y), position.Rotation, velocity);
	}

	public override void _ExitTree()
	{
		var conn = GameManager.Conn;
		if (conn == null) return;
		conn.Db.NearbyRemotePlayers.OnUpdate -= OnPositionUpdate;
	}
}
