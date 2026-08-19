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
	public Identity PlayerId { get; set; }
	public ulong ProfileId { get; set; }

	// IEntity — RemotePlayer needs Node2D, so it implements the interface directly with
	// its own registry; same pattern as LocalPlayer/Enemy.
	private readonly EntityRegistry componentRegistry = new();
	public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
	public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
	public IComponent? GetComponent(System.Type type) => componentRegistry.Get(type);
	public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

	// Velocity is reconstructed from the server row's movement_direction + movement_speed
	// (real, reported; speed is 0 when the player is idle) — never invented from facing,
	// which caused the constant-drift bug.

	/// Child binders (declared in non_local_player.tscn) feeding NearbyRemotePlayers
	/// position rows and NearbyRemotePlayerRotations screen-rotation rows.
	private TableBinderComponent positionBinder = null!;
	private TableBinderComponent rotationBinder = null!;

	public override void _Ready()
	{
		positionBinder = GetNode<TableBinderComponent>("NearbyRemotePlayersBinder");
		rotationBinder = GetNode<TableBinderComponent>("NearbyRemotePlayerRotationsBinder");

		var conn = GameManager.Conn;
		if (conn == null) return;

		var profile = conn.Db.NearbyRemotePlayersProfiles.Iter()
			.FirstOrDefault(p => p.ProfileId == ProfileId);
		if (profile != null)
			GetComponent<RemoteVisualComponent>()?.SetTexture(profile.TextureId);
	}

	// --- TableBinderComponent signal handlers (wired in non_local_player.tscn) ---

	private void OnPositionRowUpdated()
	{
		var position = (PlayerPosition)positionBinder.LastRow!;
		if (position.PlayerId != PlayerId) return;

		var velocity = Vector2.FromAngle(position.MovementDirection) * position.MovementSpeed;
		GetComponent<InterpolationComponent>()?.SetTarget(new Vector2(position.X, position.Y), velocity);
	}

	private void OnRotationRowUpdated()
	{
		var rotation = (PlayerRotation)rotationBinder.LastRow!;
		if (rotation.PlayerId != PlayerId) return;

		GetComponent<InterpolationComponent>()?.SetScreenRotationTarget(rotation.ScreenRotation);
	}
}
