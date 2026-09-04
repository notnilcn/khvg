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

	public override void _Ready()
	{
		instance = this;

		// Explicit visual loading: each mode loads only its own assets.
		// 3D picked on the main menu → instantiate world_3d.tscn into the 3D layer's
		// SubViewport and suppress the whole 2D world: the 2D bullets are hidden (their
		// simulation stays alive — hit reporting and the Bullets3D mirror read it),
		// and TerrainComponent/entity sprites gate themselves on GameVisuals.Is3D so no
		// res_path2d world asset is ever loaded.
		// 2D picked → the whole 3D CanvasLayer goes away, so the SubViewport never renders,
		// no 3D asset loads, and GetComponent<World3DComponent>() returns null everywhere
		// (the existing null guards then skip all 3D model creation).
		var layer3D = GetNodeOrNull<CanvasLayer>("3D");
		if (layer3D != null)
		{
			if (GameVisuals.Is3D)
			{
				var world3DScene = GD.Load<PackedScene>("res://Scenes/world_3d.tscn");
				layer3D.GetNode<SubViewport>("SubViewportContainer/SubViewport").AddChild(world3DScene.Instantiate());
				GetNode<Node2D>("BulletManager").Visible = false;
			}
			else
			{
				layer3D.QueueFree();
			}
		}
	}

	public override void _ExitTree()
	{
		if (instance == this) instance = null;
	}

	// ── Static facade over the registered components ─────────────────────────

	private static T? Get<T>() where T : class, IComponent =>
		IsInstanceValid(instance) ? instance!.GetComponent<T>() : null;

	public static DbConnection? Conn => DatabaseConnector.Instance?.Conn;
	public static string Username => DatabaseConnector.Instance?.Username ?? "";
	public static Vector2 LapQ => TableSubscriber.LapQ;
	public static Vector2 LapR => TableSubscriber.LapR;
	public static int EnemyCount => Get<EntitySpawnerComponent>()?.EnemyCount ?? 0;

	public static bool IsLocal(Identity id) => DatabaseConnector.Instance?.IsLocal(id) ?? false;
	public static Enemy? GetEnemy(ulong enemyId) => Get<EntitySpawnerComponent>()?.GetEnemy(enemyId);
	public static string? GetResPath2D(string textureId) => Get<CatalogComponent>()?.GetResPath2D(textureId);
	public static string? GetResPath3D(string textureId) => Get<CatalogComponent>()?.GetResPath3D(textureId);
	public static Item? GetItem(string itemId) => Get<CatalogComponent>()?.GetItem(itemId);
	public static Enchantment? GetEnchantment(string enchantmentId) => Get<CatalogComponent>()?.GetEnchantment(enchantmentId);
	public static IEnumerable<Enchantment> GetEnchantments() => Get<CatalogComponent>()?.GetEnchantments() ?? Enumerable.Empty<Enchantment>();
	public static SpellVisualComponent? SpellVisuals => Get<SpellVisualComponent>();
	public static ElevationMapComponent? ElevationMap => Get<ElevationMapComponent>();

	/// Forwarded from the CatalogComponent; subscribe/unsubscribe are no-ops once Main is gone.
	public static event Action? EnchantmentsChanged
	{
		add { if (Get<CatalogComponent>() is { } catalog) catalog.EnchantmentsChanged += value; }
		remove { if (Get<CatalogComponent>() is { } catalog) catalog.EnchantmentsChanged -= value; }
	}

	public static long CatalogVersion => Get<CatalogComponent>()?.CatalogVersion ?? 0;

	/// Forwarded from the CatalogComponent; subscribe/unsubscribe are no-ops once Main is gone.
	public static event Action? CatalogChanged
	{
		add { if (Get<CatalogComponent>() is { } catalog) catalog.CatalogChanged += value; }
		remove { if (Get<CatalogComponent>() is { } catalog) catalog.CatalogChanged -= value; }
	}
}
