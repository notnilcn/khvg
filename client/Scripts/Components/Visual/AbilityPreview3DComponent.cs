#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Charged-ability AoE preview (3D), child of World3DComponent in world_3d.tscn: while the
/// local player holds a charge (AbilityChargeComponent) on a Spell ability, highlights the
/// hex cells a conservative hex-region approximation of that spell's shape would cover,
/// centered on the cursor-aimed target and laid on the terrain surface. Pure visuals — it
/// reads the server-echoed charge, the client catalog (Item behaviors) and the ElevationMap;
/// it calls no reducers and touches no gameplay state. Hidden when not charging or when the
/// charged item has no Spell shape (e.g. a charged Teleport). 3D-only by construction: the
/// component is declared in world_3d.tscn, which GameManager only instantiates in 3D mode.
///
/// This is a PREVIEW, not the hit test: the server's continuous
/// player/methods.rs::spell_shape_contains stays authoritative and unchanged, and every
/// region below is deliberately a superset of the true shape (a partially covered hex is
/// highlighted):
///   Circle — the wrapped radial disk whose hex radius reaches every cell whose center is
///            inside the circle plus the ring of cells that only partially overlap it.
///   Line   — a plane-bounded axial box around the segment, filtered by world-space
///            distance-to-segment (cells whose circumradius straddles the capsule stay in).
///   Cone   — a disk around the caster filtered by a capsule-style test with one
///            circumradius of slack behind the apex, past the tip, and to each side.
/// Region cells are generated in node space (so the preview stays contiguous around the
/// player across torus seams), canonicalized and de-duplicated with
/// HexRegion.WrapCell/.Wrapped, then placed back in node space at their nearest lap copy
/// (TorusMath.NearestCandidate) — the wrapped-render pattern the other world_3d components
/// use for canonical lookups. Tiles are pooled MeshInstance3Ds carrying one shared
/// unshaded translucent material; they have no collision.
/// </summary>
public partial class AbilityPreview3DComponent : Node3DComponent
{
	/// Fallback used only until the MapConfig row replays; the live world's HexOuterRadius
	/// (48 today) replaces it. Hex math only — elevation lookups key on canonical q/r.
	private const float DefaultHexOuterRadius = 48.0f;

	/// Pointy-top adjacency: the nearest possible center-to-center distance between two
	/// cells one hex step apart is √3 × outerRadius (straight axial steps are exactly that),
	/// which is what turns a world-space reach into a hex-ring count.
	private static readonly float Sqrt3 = Mathf.Sqrt(3f);

	[ExportGroup("Highlight")]
	/// RGBA of the pooled highlight tiles (alpha < 1 keeps the terrain readable).
	[Export] public Color HighlightColor { get; set; } = new(0.3f, 0.85f, 1.0f, 0.35f);

	/// Tile top's Y lift above the hex's terrain surface, keeping the fill off the MST tops.
	[Export] public float SurfaceOffset { get; set; } = 0.6f;

	/// How far the prism sinks below its top face, so the fill still reads on a raised tile
	/// whose neighbours climb above it.
	[Export] public float TileDepth { get; set; } = 4.0f;

	private TableBinderComponent _mapConfigBinder = null!;

	private float _outerRadius = DefaultHexOuterRadius;
	private int _chunkHexRadius;
	private int _chunkCols;
	private int _chunkRows;

	private readonly List<MeshInstance3D> _tiles = new();
	private StandardMaterial3D? _material;
	private ArrayMesh? _mesh;
	private float _meshRadius = -1f;
	private int _nextTile;
	private bool _showing;

	private int _lastSlot = -1;
	private Vector2 _lastCursor = new(float.MaxValue, float.MaxValue);
	private bool _dirty = true;

	public override void _Ready()
	{
		base._Ready();
		_mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
		if (GameManager.ElevationMap is { } map) map.ElevationChanged += OnElevationChanged;
	}

	public override void _ExitTree()
	{
		if (GameManager.ElevationMap is { } map) map.ElevationChanged -= OnElevationChanged;
		base._ExitTree();
	}

	public override void _Process(double _delta)
	{
		var player = LocalPlayer.Local;
		if (player == null) { HideTiles(); return; }
		if (player.GetComponent<AbilityChargeComponent>() is not { Charging: true, ChargedSlot: >= 0 } charge)
		{
			HideTiles();
			return;
		}
		var spell = ResolveChargedSpell(player, charge.ChargedSlot);
		if (spell == null) { HideTiles(); return; }

		var cursor = player.GetCursorWorldPosition();
		if (_showing && !_dirty && charge.ChargedSlot == _lastSlot
			&& cursor.DistanceSquaredTo(_lastCursor) < 0.25f) return;

		_lastSlot = charge.ChargedSlot;
		_lastCursor = cursor;
		_dirty = false;
		Rebuild(player, charge.ChargedSlot, spell, cursor);
	}

	// --- TableBinderComponent signal handlers (wired in world_3d.tscn) ---
	// The binder replays existing rows, so a cached MapConfig comes through the insert path.

	private void OnMapConfigRow()
	{
		var cfg = (MapConfig)_mapConfigBinder.LastRow!;
		_outerRadius = cfg.HexOuterRadius;
		_chunkHexRadius = cfg.ChunkHexRadius;
		_chunkCols = cfg.ChunkCols;
		_chunkRows = cfg.ChunkRows;
		_dirty = true;
	}

	// Elevation rows stream in/out with the AOI and redraw surface heights only on the next
	// cursor move otherwise — flag the next frame dirty while a preview is up.
	private void OnElevationChanged() => _dirty = true;

	/// The charged head slot's Spell params, or null when its item has no Spell behavior
	/// (charged Teleports, missing items, empty slots) — the preview has nothing to show.
	private static SpellParams? ResolveChargedSpell(LocalPlayer player, int headSlot)
	{
		var item = player.GetComponent<LocalPlayerInventoryComponent>()?.ResolveSlotAt(headSlot).Item;
		if (item == null) return null;
		foreach (var behavior in item.Behaviors)
			if (behavior is ItemBehavior.Ability(var ability) && ability.Effect is AbilityEffect.Spell(var p))
				return p;
		return null;
	}

	// ── Preview build ─────────────────────────────────────────────────────────

	private void Rebuild(LocalPlayer player, int headSlot, SpellParams spell, Vector2 cursor)
	{
		EnsureAssets();
		var origin = player.GlobalPosition;
		int position = Mathf.Clamp(headSlot - LocalPlayerInventoryComponent.AbilitySlotStart, 0, ServerConstants.AbilityPositionMultipliers.Length - 1);
		float multiplier = ServerConstants.AbilityPositionMultipliers[position];
		var cells = BuildRegionCells(spell.Shape, origin, cursor, multiplier);

		_nextTile = 0;
		foreach (var cell in cells.Wrapped(_chunkHexRadius, _chunkCols, _chunkRows))
		{
			int height = GameManager.ElevationMap?.GetHeight(cell.Q, cell.R) ?? 0;
			var canonical = HexMath.HexToWorld(cell.Q, cell.R, _outerRadius);
			var lifted = TorusMath.NearestCandidate(canonical, origin, GameManager.LapQ, GameManager.LapR);
			AcquireTile().Position = new Vector3(lifted.X, height + SurfaceOffset, lifted.Y);
		}
		for (int i = _nextTile; i < _tiles.Count; i++) _tiles[i].Visible = false;
		_showing = true;
	}

	private IEnumerable<HexMapCellId> BuildRegionCells(SpellShape shape, Vector2 origin, Vector2 cursor, float multiplier) => shape switch
	{
		SpellShape.Circle(var c) => CircleCells(origin, cursor, c.Radius * multiplier),
		SpellShape.Line(var l) => SegmentCells(origin, origin + AimDirection(origin, cursor) * l.Length, l.Width * 0.5f),
		SpellShape.Cone(var c) => ConeCells(origin, cursor, c.Length, c.Spread),
		_ => Array.Empty<HexMapCellId>(),
	};

	/// Wrapped radial disk around the clamped cursor: a cell at hex distance d has its
	/// center at least d·√3·outerRadius away, so floor((worldRadius + outerRadius) /
	/// (√3·outerRadius)) covers every cell whose center is inside the circle AND every cell
	/// that only partially overlaps it (center within worldRadius + circumradius).
	private IEnumerable<HexMapCellId> CircleCells(Vector2 origin, Vector2 cursor, float worldRadius)
	{
		var clamped = ClampToTargetRange(origin, cursor);
		var hex = HexMath.WorldToHex(clamped.X, clamped.Y, _outerRadius);
		int radius = Mathf.FloorToInt((worldRadius + _outerRadius) / (Sqrt3 * _outerRadius));
		return new HexMapCellId(hex.X, hex.Y, 0).GetNeighbors(Mathf.Max(radius, 0), HexPlanes.QRS, includeCenter: true);
	}

	/// Plane-bounded axial box covering both endpoints plus the width pad (HexMapIterAxial
	/// over the Q/R/S planes, centered on the segment midpoint), filtered by the world-space
	/// distance from each cell center to the segment: a cell stays when its circumradius
	/// reaches the capsule, so edge-straddling cells are highlighted.
	private IEnumerable<HexMapCellId> SegmentCells(Vector2 a, Vector2 b, float halfWidth)
	{
		var hexA = HexMath.WorldToHex(a.X, a.Y, _outerRadius);
		var hexB = HexMath.WorldToHex(b.X, b.Y, _outerRadius);
		var cellA = new HexMapCellId(hexA.X, hexA.Y, 0);
		var cellB = new HexMapCellId(hexB.X, hexB.Y, 0);
		var center = HexMapCellId.AxialRound((cellA.Q + cellB.Q) * 0.5f, (cellA.R + cellB.R) * 0.5f);
		int widthPad = Mathf.CeilToInt((halfWidth + _outerRadius) / (Sqrt3 * _outerRadius));
		int radius = center.Distance(cellA) + center.Distance(cellB) + widthPad + 1;
		foreach (var cell in new HexMapIterAxial(center, radius, HexPlanes.QRS))
		{
			var pos = HexMath.HexToWorld(cell.Q, cell.R, _outerRadius);
			if (DistanceToSegment(pos, a, b) <= halfWidth + _outerRadius) yield return cell;
		}
	}

	/// Disk-bounded conservative cone around the caster: cells inside the
	/// (length + circumradius) reach, kept when no more than one circumradius behind the
	/// apex, no more than one circumradius past the tip, and within the cone's half-width
	/// plus one circumradius of side slack — the apex cell and every edge-straddling cell
	/// stay highlighted.
	private IEnumerable<HexMapCellId> ConeCells(Vector2 origin, Vector2 cursor, float length, float spread)
	{
		var dir = AimDirection(origin, cursor);
		float tanHalf = Mathf.Tan(Mathf.Min(spread * 0.5f, 1.5f));
		var hex = HexMath.WorldToHex(origin.X, origin.Y, _outerRadius);
		int radius = Mathf.FloorToInt((length + _outerRadius) / (Sqrt3 * _outerRadius)) + 1;
		foreach (var cell in new HexMapCellId(hex.X, hex.Y, 0).GetNeighbors(Mathf.Max(radius, 1), HexPlanes.QRS, includeCenter: true))
		{
			var rel = HexMath.HexToWorld(cell.Q, cell.R, _outerRadius) - origin;
			float along = rel.Dot(dir);
			if (along < -_outerRadius || along > length + _outerRadius) continue;
			float across = Mathf.Abs(rel.X * dir.Y - rel.Y * dir.X);
			if (across <= Mathf.Max(along, 0f) * tanHalf + _outerRadius) yield return cell;
		}
	}

	/// The server clamps the cast cursor to MaxAbilityTargetRange around the caster
	/// (player/reducers.rs::apply_ability_effect); the preview clamps identically so the
	/// highlighted circle lands where the cast will.
	private static Vector2 ClampToTargetRange(Vector2 origin, Vector2 cursor)
	{
		var axis = cursor - origin;
		return axis.Length() > ServerConstants.MaxAbilityTargetRange
			? origin + axis.Normalized() * ServerConstants.MaxAbilityTargetRange
			: cursor;
	}

	/// Line/Cone aim direction, with the server's (and the optimistic flash's) zero-length
	/// fallback so a cursor sitting exactly on the caster still previews something.
	private static Vector2 AimDirection(Vector2 origin, Vector2 cursor)
	{
		var axis = cursor - origin;
		return axis.LengthSquared() > 0.000001f ? axis.Normalized() : Vector2.Right;
	}

	private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
	{
		var ab = b - a;
		float lengthSq = ab.LengthSquared();
		if (lengthSq < 0.000001f) return point.DistanceTo(a);
		float t = Mathf.Clamp((point - a).Dot(ab) / lengthSq, 0f, 1f);
		return point.DistanceTo(a + ab * t);
	}

	// ── Pooled tiles + shared assets ──────────────────────────────────────────

	private void EnsureAssets()
	{
		if (_material == null)
		{
			_material = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				DisableReceiveShadows = true,
			};
		}
		_material.AlbedoColor = HighlightColor;

		if (_mesh == null || !Mathf.IsEqualApprox(_meshRadius, _outerRadius))
		{
			_mesh = BuildHexPrism(_outerRadius, TileDepth);
			_meshRadius = _outerRadius;
			foreach (var tile in _tiles) tile.Mesh = _mesh;
		}
	}

	/// One shared hex-prism mesh: a pointy-top hexagon top at local y=0 (vertex along +X,
	/// matching HexMath's q axis), the same ring sunk TileDepth below, and side quads. The
	/// material is unshaded and double-sided, so winding and normals carry no meaning.
	private static ArrayMesh BuildHexPrism(float radius, float depth)
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		var top = new Vector3[6];
		var bottom = new Vector3[6];
		for (int i = 0; i < 6; i++)
		{
			float angle = Mathf.Tau * i / 6f;
			top[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
			bottom[i] = top[i] - new Vector3(0f, depth, 0f);
		}
		for (int i = 0; i < 6; i++)
		{
			int j = (i + 1) % 6;
			st.AddVertex(Vector3.Zero); st.AddVertex(top[j]); st.AddVertex(top[i]);
			st.AddVertex(new Vector3(0f, -depth, 0f)); st.AddVertex(bottom[i]); st.AddVertex(bottom[j]);
			st.AddVertex(top[i]); st.AddVertex(top[j]); st.AddVertex(bottom[j]);
			st.AddVertex(top[i]); st.AddVertex(bottom[j]); st.AddVertex(bottom[i]);
		}
		return st.Commit();
	}

	private MeshInstance3D AcquireTile()
	{
		if (_nextTile < _tiles.Count)
		{
			var pooled = _tiles[_nextTile++];
			pooled.Visible = true;
			return pooled;
		}
		var tile = new MeshInstance3D
		{
			Mesh = _mesh,
			MaterialOverride = _material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(tile);
		_tiles.Add(tile);
		_nextTile++;
		return tile;
	}

	private void HideTiles()
	{
		if (!_showing) return;
		foreach (var tile in _tiles) tile.Visible = false;
		_showing = false;
		_lastSlot = -1;
		_dirty = true;
	}
}
