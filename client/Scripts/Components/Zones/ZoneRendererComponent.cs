#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Renders every GroundZone row (P3 — the witch doctor's circles and the wizard's spire)
/// as a code-created Polygon2D circle, colored per ZoneKind (burst variants and DamageShare
/// kept visually distinct). Rows are inserted by the CastZone ability and deleted when the
/// zone expires or its owner dies/leaves, so the row lifecycle IS the visual lifecycle.
///
/// Fed by the child GroundZoneBinder with ReplayExistingRows = true: GroundZone is a
/// PERSISTENT table, not an event table — replaying cached rows on bind is exactly what a
/// late-joining (or scene-re-entering) client needs to see zones that are still alive.
/// This is the opposite of the BulletControlEvent rule, where replay would re-fire the
/// table's whole cast history (see TableBinderComponent).
///
/// RowUpdated is deliberately NOT wired: the server's 1 Hz tick rewrites `remaining` every
/// second, and nothing about the visual (position/radius/kind) ever changes.
/// </summary>
public partial class ZoneRendererComponent : Component
{
	private readonly Dictionary<ulong, Polygon2D> zones = new();
	private TableBinderComponent zoneBinder = null!;

	public override void _Ready()
	{
		base._Ready();
		zoneBinder = GetNode<TableBinderComponent>("GroundZoneBinder");
	}

	/// GroundZoneBinder RowInserted handler (wired in game.tscn).
	private void OnZoneRow()
	{
		var row = (GroundZone)zoneBinder.LastRow!;
		if (zones.ContainsKey(row.ZoneId)) return;
		zones[row.ZoneId] = MakeCirclePolygon(new Vector2(row.X, row.Y), row.Radius, ZoneColor(row.Kind));
	}

	/// GroundZoneBinder RowDeleted handler (wired in game.tscn).
	private void OnZoneRowDeleted()
	{
		var row = (GroundZone)zoneBinder.LastDeletedRow!;
		if (zones.Remove(row.ZoneId, out var circle) && GodotObject.IsInstanceValid(circle))
			circle.QueueFree();
	}

	/// The zone circle, parented under the root (data-driven count, like the bullet
	/// controller's zone visuals). ZIndex -1 keeps it a ground decal: above the terrain
	/// (-10), below the entities (0).
	private Polygon2D MakeCirclePolygon(Vector2 center, float radius, Color color)
	{
		const int CirclePoints = 32;
		var polygon = new Vector2[CirclePoints];
		for (int i = 0; i < CirclePoints; i++)
		{
			float angle = Mathf.Tau * i / CirclePoints;
			polygon[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}
		var circle = new Polygon2D
		{
			Polygon = polygon,
			Color = color,
			ZIndex = -1,
		};
		GetParent().AddChild(circle);
		return circle;
	}

	private static Color ZoneColor(ZoneKind kind) => kind switch
	{
		ZoneKind.DebuffImmunity => new Color(0.2f, 0.9f, 0.9f, 0.25f),  // teal
		ZoneKind.DamageReduction => new Color(0.3f, 0.5f, 1f, 0.25f),    // blue
		ZoneKind.HpFloor => new Color(1f, 0.85f, 0.2f, 0.25f),           // gold
		ZoneKind.HealOverTime => new Color(0.3f, 0.9f, 0.4f, 0.25f),     // green
		ZoneKind.BurstHeal => new Color(0.7f, 1f, 0.5f, 0.35f),          // bright green (burst)
		ZoneKind.DamageBuff => new Color(1f, 0.6f, 0.2f, 0.25f),         // orange
		ZoneKind.EnemyDot => new Color(0.7f, 0.3f, 0.9f, 0.25f),         // purple
		ZoneKind.EnemyBurst => new Color(1f, 0.25f, 0.4f, 0.35f),        // red (burst)
		ZoneKind.EnemySlow => new Color(0.5f, 0.8f, 1f, 0.25f),          // ice blue
		ZoneKind.EnemyVulnerability => new Color(1f, 1f, 0.3f, 0.25f),   // yellow
		ZoneKind.DamageShare => new Color(1f, 0.5f, 0.8f, 0.3f),         // pink (distinct)
		_ => new Color(1f, 1f, 1f, 0.2f),
	};
}
