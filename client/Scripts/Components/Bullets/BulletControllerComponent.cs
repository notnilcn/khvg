#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Player abilities that manipulate live enemy bullets: DeleteNear, SplitNear and
/// AttractNear near a point, DeleteInRect and SplitInRect as a rectangle from the caster
/// toward the cursor (DeleteInRect deletes, SplitInRect splits), and SpawnAttractZone, a
/// lingering black hole that keeps dragging nearby bullets toward itself for its duration.
/// All share the proximity queries over the live DirectionalBullets2D instances tracked by
/// BulletSpawnerComponent, which is why they live in one component.
/// Casts are networked by relaying the cast itself: the caster applies the effect locally
/// (optimistic) and the server appends a BulletControlEvent row; every other client applies
/// the same effect on row insert via the child BulletControlEventBinder (own echoes are
/// skipped via cast_by). Each client resolves the proximity query against its own bullets,
/// so edge-of-radius results can differ slightly. DeleteRect/SplitRect events encode the
/// rectangle as origin (x, y) + far end (target_x, target_y) + width (radius); every client
/// also draws the black rectangle, so the visual replicates with the cast. Attract events
/// carry a duration: 0 is a one-shot pull, &gt; 0 spawns the lingering black hole (visual
/// included) on every client.
/// Driven by the real ability system: ability items with a DeleteBullets/SplitBullets/
/// AttractBullets/DeleteBulletsInRect AbilityEffect call the public methods below from
/// LocalPlayerInventoryComponent.TryActivateAbility, and activate_ability appends the
/// event server-side (radius/dims from the item, cursor target clamped to cast range).
/// </summary>
public partial class BulletControllerComponent : Component
{
	[Export] public float Radius = 120f;
	[Export] public int SplitCount = 4;
	[Export] public float SplitSpread = Mathf.Pi / 3f;
	[Export] public float SplitLifetime = 3f;
	[Export] public float SplitDefaultSpeed = 150f;
	[Export] public float HomingSmoothing = 5f;
	[Export] public float AttractTickInterval = 0.2f;

	/// A live black hole: keeps re-applying AttractNear until TimeLeft runs out.
	private class AttractZone
	{
		public Vector2 Center;
		public Vector2 Target;
		public float Radius;
		public float TimeLeft;
		public float TickAccumulator;
		public Polygon2D Visual = null!;
	}

	private readonly List<AttractZone> attractZones = [];
	private BulletSpawnerComponent spawner = null!;
	private TableBinderComponent bulletControlEventBinder = null!;

	protected override Type[] GetRequiredComponents() => [typeof(BulletSpawnerComponent)];

	public override void _Ready()
	{
		base._Ready();
		bulletControlEventBinder = GetNode<TableBinderComponent>("BulletControlEventBinder");
	}

	protected override void OnEntityReady() => spawner = GetSibling<BulletSpawnerComponent>()!;

	/// BulletControlEventBinder RowInserted handler (wired in game.tscn). Replays another
	/// player's cast locally; the caster's own echo is skipped (already applied optimistically).
	private void OnBulletControlEventRow()
	{
		var row = (BulletControlEvent)bulletControlEventBinder.LastRow!;
		if (GameManager.IsLocal(row.CastBy)) return;
		var point = new Vector2(row.X, row.Y);
		if (row.Kind is BulletControlKind.Delete) DeleteNear(point, row.Radius);
		else if (row.Kind is BulletControlKind.Split) SplitNear(point, row.Radius);
		else if (row.Kind is BulletControlKind.Attract)
		{
			var target = new Vector2(row.TargetX, row.TargetY);
			if (row.Duration > 0f) SpawnAttractZone(point, row.Radius, target, row.Duration);
			else AttractNear(point, row.Radius, target);
		}
		else if (row.Kind is BulletControlKind.DeleteRect)
			DeleteInRect(point, new Vector2(row.TargetX, row.TargetY), row.Radius);
		else if (row.Kind is BulletControlKind.SplitRect)
			SplitInRect(point, new Vector2(row.TargetX, row.TargetY), row.Radius);
	}

	/// Every enabled live enemy bullet (instance, index) whose position is within radius of point.
	private IEnumerable<(GodotObject Instance, int Index)> FindBulletsNear(Vector2 point, float radius)
	{
		float radiusSq = radius * radius;
		foreach (var inst in spawner.LiveEnemyBullets)
		{
			if (!GodotObject.IsInstanceValid(inst)) continue;
			int count = inst.Call("get_amount_bullets").AsInt32();
			if (count == 0) continue;
			var transforms = inst.Call("all_bullets_get_transforms").AsGodotArray<Transform2D>();
			for (int i = 0; i < count && i < transforms.Count; i++)
			{
				if (!inst.Call("is_bullet_status_enabled", i).AsBool()) continue;
				if (point.DistanceSquaredTo(transforms[i].Origin) <= radiusSq)
					yield return (inst, i);
			}
		}
	}

	/// Every enabled live enemy bullet (instance, index) inside the rectangle from origin
	/// to end with the given full width (the rectangle abilities' hitbox). Same torus-naive iteration
	/// shape as FindBulletsNear.
	private IEnumerable<(GodotObject Instance, int Index)> FindBulletsInRect(Vector2 origin, Vector2 end, float width)
	{
		var axis = end - origin;
		float length = axis.Length();
		if (length < 0.001f) yield break;
		var dir = axis / length;
		float halfWidth = width * 0.5f;
		foreach (var inst in spawner.LiveEnemyBullets)
		{
			if (!GodotObject.IsInstanceValid(inst)) continue;
			int count = inst.Call("get_amount_bullets").AsInt32();
			if (count == 0) continue;
			var transforms = inst.Call("all_bullets_get_transforms").AsGodotArray<Transform2D>();
			for (int i = 0; i < count && i < transforms.Count; i++)
			{
				if (!inst.Call("is_bullet_status_enabled", i).AsBool()) continue;
				var rel = transforms[i].Origin - origin;
				float along = rel.Dot(dir);
				if (along < 0f || along > length) continue;
				float across = Mathf.Abs(rel.Dot(dir.Orthogonal()));
				if (across <= halfWidth)
					yield return (inst, i);
			}
		}
	}

	/// Current speed of one bullet, or null when the plugin's BulletSpeedData2D can't be read.
	private static float? GetBulletSpeed(GodotObject inst, int index)
	{
		var speedData = inst.Call("get_bullet_speed_data", index).AsGodotObject();
		if (speedData == null) return null;
		var value = speedData.Get("speed");
		return value.VariantType == Variant.Type.Nil ? null : value.AsSingle();
	}

	public void DeleteNear(Vector2 point, float radius)
	{
		foreach (var (inst, index) in FindBulletsNear(point, radius))
			inst.Call("disable_bullet", index);
	}

	/// Deletes every bullet inside the rectangle and draws it. Runs on the caster
	/// (optimistic) and on every other client via the BulletControlEvent echo, so the
	/// black rectangle replicates with the cast.
	public void DeleteInRect(Vector2 origin, Vector2 end, float width)
	{
		foreach (var (inst, index) in FindBulletsInRect(origin, end, width))
			inst.Call("disable_bullet", index);
		SpawnRectVisual(origin, end, width);
	}

	/// The black rectangle, drawn for everyone who sees the cast (DeleteInRect and SplitInRect
	/// alike): a code-created Polygon2D (data-driven count, like HitZones) under the parent
	/// BulletManager (z_index 1 keeps it above entities), flashed briefly then freed.
	private void SpawnRectVisual(Vector2 origin, Vector2 end, float width)
	{
		var axis = end - origin;
		if (axis.Length() < 0.001f) return;
		var offset = axis.Orthogonal().Normalized() * (width * 0.5f);
		var rect = new Polygon2D
		{
			Polygon = [origin - offset, origin + offset, end + offset, end - offset],
			Color = new Color(0f, 0f, 0f, 0.85f),
		};
		GetParent().AddChild(rect);
		var tween = rect.CreateTween();
		tween.TweenProperty(rect, "modulate:a", 0f, 0.2);
		tween.TweenCallback(Callable.From(rect.QueueFree));
	}

	/// Disables each nearby bullet and respawns it as a fan of SplitCount pellets around its
	/// direction. Hits are collected first because the query must not run while mutating.
	public void SplitNear(Vector2 point, float radius) => SplitHits(FindBulletsNear(point, radius));

	/// The Split Orb's cast: splits every bullet inside the rectangle and draws it, mirroring
	/// DeleteInRect. Runs on the caster (optimistic) and on every other client via the
	/// BulletControlEvent echo, so the black rectangle replicates with the cast.
	public void SplitInRect(Vector2 origin, Vector2 end, float width)
	{
		SplitHits(FindBulletsInRect(origin, end, width));
		SpawnRectVisual(origin, end, width);
	}

	/// Disables each hit bullet and respawns it as a fan of SplitCount pellets around its
	/// direction. Hits are collected first because the query must not run while mutating.
	private void SplitHits(IEnumerable<(GodotObject Instance, int Index)> query)
	{
		var hits = new List<(GodotObject Inst, int Index)>(query);
		foreach (var (inst, index) in hits)
		{
			if (!GodotObject.IsInstanceValid(inst)) continue;
			if (!inst.Call("is_bullet_status_enabled", index).AsBool()) continue;
			var transform = inst.Call("get_bullet_transform", index).AsTransform2D();
			float speed = GetBulletSpeed(inst, index) ?? SplitDefaultSpeed;
			var customData = inst.Get("bullets_custom_data").As<BulletData>();
			inst.Call("disable_bullet", index);
			spawner.SpawnEnemyBulletFan(transform.Origin, transform.Rotation, SplitCount, SplitSpread, speed, SplitLifetime, customData);
		}
	}

	/// Pushes a global-position homing target (the blackhole) onto every nearby bullet.
	public void AttractNear(Vector2 point, float radius, Vector2 target)
	{
		foreach (var (inst, index) in FindBulletsNear(point, radius))
		{
			inst.Set("homing_smoothing", HomingSmoothing);
			inst.Set("homing_update_interval", 0.0);
			inst.Set("homing_take_control_of_texture_rotation", true);
			inst.Call("bullet_homing_push_back_global_position_target", index, target);
		}
	}

	/// The Attract Orb's cast: a black hole at point that lingers for duration seconds,
	/// re-applying AttractNear every AttractTickInterval so bullets entering the radius
	/// later get caught too. Runs on the caster (optimistic) and on every other client via
	/// the BulletControlEvent echo, so the black circle replicates with the cast.
	public void SpawnAttractZone(Vector2 point, float radius, Vector2 target, float duration)
	{
		const int CirclePoints = 24;
		var polygon = new Vector2[CirclePoints];
		for (int i = 0; i < CirclePoints; i++)
		{
			float angle = Mathf.Tau * i / CirclePoints;
			polygon[i] = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}
		var circle = new Polygon2D
		{
			Polygon = polygon,
			Color = new Color(0f, 0f, 0f, 0.85f),
		};
		GetParent().AddChild(circle);
		attractZones.Add(new AttractZone
		{
			Center = point,
			Target = target,
			Radius = radius,
			TimeLeft = duration,
			Visual = circle,
		});
		AttractNear(point, radius, target); // instant first pull
	}

	/// Ticks the live black holes: periodic re-attraction, then a fade + free on expiry.
	public override void _Process(double delta)
	{
		for (int i = attractZones.Count - 1; i >= 0; i--)
		{
			var zone = attractZones[i];
			zone.TickAccumulator += (float)delta;
			if (zone.TickAccumulator >= AttractTickInterval)
			{
				zone.TickAccumulator = 0f;
				AttractNear(zone.Center, zone.Radius, zone.Target);
			}
			zone.TimeLeft -= (float)delta;
			if (zone.TimeLeft > 0f) continue;
			attractZones.RemoveAt(i);
			if (!GodotObject.IsInstanceValid(zone.Visual)) continue;
			var tween = zone.Visual.CreateTween();
			tween.TweenProperty(zone.Visual, "modulate:a", 0f, 0.2);
			tween.TweenCallback(Callable.From(zone.Visual.QueueFree));
		}
	}
}
