#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Root of game.tscn ("Main") and the IEntity that the scene-level components register
/// with: TableSubscriber, CatalogComponent,
/// EntitySpawnerComponent, LobbyComponent, and the camera/overlay components. Holds no
/// logic of its own — the static facade below delegates to those components (and to the
/// DatabaseConnector autoload) so callers whose own entity registry can't see them
/// (spawned entities, UI, debug overlays) can still reach the connection, catalogs, and
/// spawner lookups without node-path walks.
/// </summary>
public partial class GameManager : Node2D, IEntity
{
	private static GameManager? instance;

	// IEntity — GameManager needs Node2D (game.tscn root), so it implements the interface
	// directly with its own registry; same pattern as LocalPlayer/Enemy.
	private readonly EntityRegistry componentRegistry = new();
	public void RegisterComponent(IComponent component) => componentRegistry.Register(component);
	public void UnregisterComponent(IComponent component) => componentRegistry.Unregister(component);
	public IComponent? GetComponent(Type type) => componentRegistry.Get(type);
	public T? GetComponent<T>() where T : IComponent => componentRegistry.Get(typeof(T)) is T match ? match : default;

	public override void _Ready() => instance = this;

	public override void _ExitTree()
	{
		if (instance == this) instance = null;
	}

	// ── Static facade over the registered components ─────────────────────────

	private static T? Get<T>() where T : class, IComponent =>
		IsInstanceValid(instance) ? instance!.GetComponent<T>() : null;

	public static DbConnection? Conn => DatabaseConnector.Instance?.Conn;
	public static string Username => DatabaseConnector.Instance?.Username ?? "";
	public static Vector2 LapQ => Get<TableSubscriber>()?.LapQ ?? Vector2.Zero;
	public static Vector2 LapR => Get<TableSubscriber>()?.LapR ?? Vector2.Zero;
	public static int EnemyCount => Get<EntitySpawnerComponent>()?.EnemyCount ?? 0;

	public static bool IsLocal(Identity id) => DatabaseConnector.Instance?.IsLocal(id) ?? false;
	public static Enemy? GetEnemy(ulong enemyId) => Get<EntitySpawnerComponent>()?.GetEnemy(enemyId);
	public static string? GetResPath(string textureId) => Get<CatalogComponent>()?.GetResPath(textureId);
	public static Item? GetItem(string itemId) => Get<CatalogComponent>()?.GetItem(itemId);
	public static Enchantment? GetEnchantment(string enchantmentId) => Get<CatalogComponent>()?.GetEnchantment(enchantmentId);
	public static IEnumerable<Enchantment> GetEnchantments() => Get<CatalogComponent>()?.GetEnchantments() ?? Enumerable.Empty<Enchantment>();

	/// Forwarded from the CatalogComponent; subscribe/unsubscribe are no-ops once Main is gone.
	public static event Action? EnchantmentsChanged
	{
		add { if (Get<CatalogComponent>() is { } catalog) catalog.EnchantmentsChanged += value; }
		remove { if (Get<CatalogComponent>() is { } catalog) catalog.EnchantmentsChanged -= value; }
	}

	public override void _Input(InputEvent @event)
	{
		if (Input.IsActionJustPressed("toggle_3d"))
		{
			GetNode<CanvasLayer>("3D").Visible = !GetNode<CanvasLayer>("3D").Visible;
		}
	}
}
