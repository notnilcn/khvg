// Portions derived from godot-hex-map (bab38bd) - MIT. See THIRD_PARTY_NOTICES.md.
#nullable enable
using Godot;
using System;
using System.Collections;
using System.Collections.Generic;

// Port of godot-hex-map's core cell-id + plane-bounded iterator model
// (src/core/cell_id.*, iter_axial/radial/cube.*, planes.*). The source's tests/ are the
// behavioral spec and this file mirrors that math statement-for-statement; the Rust mirror
// (server/spacetimedb/src/world/hex_region.rs) carries the ported test assertions, and both
// copies must change together. A region is a bounding box over the axial coordinates: an
// enabled plane expands by `radius`, a disabled plane pins to the center coordinate — so
// HexPlanes.QRS is a flat hex disk, HexPlanes.All a hex prism, HexPlanes.YQR a plane wedge,
// etc. Iteration order is deterministic (r fastest, then q, then y; odd-r rows for cube) but
// is NOT distance-sorted.
//
// Wrap-aware variants live at the bottom (HexRegion.WrapCell / HexRegion.Wrapped): the shape is
// generated in unwrapped axial space so a client AoE preview stays contiguous around the
// caster, and canonicalization through HexMath.WrapHex is opt-in for server-style membership.
public readonly struct HexMapCellId : IEquatable<HexMapCellId>
{
	private const float Sqrt3 = 1.7320508075688772f;
	private const float Sqrt3_2 = 0.8660254037844386f;

	public static readonly HexMapCellId Zero = new HexMapCellId(0, 0, 0);
	public static readonly HexMapCellId Invalid = new HexMapCellId(int.MaxValue, int.MaxValue, int.MaxValue);

	// axial coordinates
	public readonly int Q;
	public readonly int R;

	// y coordinate
	public readonly int Y;

	public HexMapCellId(int q, int r, int y)
	{
		Q = q;
		R = r;
		Y = y;
	}

	// Vector3I maps to (q, y, r) like the source — reordered on the way in and out.
	public HexMapCellId(Vector3I vector) : this(vector.X, vector.Z, vector.Y) { }

	public Vector3I ToVector3I() => new Vector3I(Q, Y, R);

	// calculate the s coordinate for converting from axial to cube coordinates
	public int S => -Q - R;

	// true when q/r/y all fit the source's int16 Key packing range
	public bool InBounds =>
		Q >= short.MinValue && Q <= short.MaxValue &&
		R >= short.MinValue && R <= short.MaxValue &&
		Y >= short.MinValue && Y <= short.MaxValue;

	public static HexMapCellId operator +(HexMapCellId a, HexMapCellId b) => new HexMapCellId(a.Q + b.Q, a.R + b.R, a.Y + b.Y);
	public static HexMapCellId operator -(HexMapCellId a, HexMapCellId b) => new HexMapCellId(a.Q - b.Q, a.R - b.R, a.Y - b.Y);
	public static HexMapCellId operator *(HexMapCellId a, int m) => new HexMapCellId(a.Q * m, a.R * m, a.Y * m);
	public static HexMapCellId operator -(HexMapCellId a) => new HexMapCellId(-a.Q, -a.R, -a.Y);

	public static bool operator ==(HexMapCellId a, HexMapCellId b) => a.Q == b.Q && a.R == b.R && a.Y == b.Y;
	public static bool operator !=(HexMapCellId a, HexMapCellId b) => !(a == b);

	public bool Equals(HexMapCellId other) => this == other;
	public override bool Equals(object? obj) => obj is HexMapCellId other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Q, R, Y);
	public override string ToString() => $"{{ .q = {Q}, .r = {R}, .y = {Y} }}";

	// calculate the distance between two cells in cell units
	public int Distance(HexMapCellId other)
	{
		long dq = (long)other.Q - Q;
		long dr = (long)other.R - R;
		long dy = (long)other.Y - Y;
		long hexDistance = (Math.Abs(dq) + Math.Abs(dq + dr) + Math.Abs(dr)) / 2;

		// Distance is the number of **faces** we traverse to get to the other node. It is
		// calculated this way because allowing diagonal traversal up or down results in
		// neighbors(radius=N) being a prism instead of a rough sphere.
		return (int)(hexDistance + Math.Abs(dy));
	}

	// based on https://observablehq.com/@jrus/hexround — the source's round() is
	// half-away-from-zero, so MidpointRounding.AwayFromZero is required here (plain
	// MathF.Round is banker's rounding and would disagree on exact ties).
	public static HexMapCellId AxialRound(float qIn, float rIn)
	{
		int q = (int)MathF.Round(qIn, MidpointRounding.AwayFromZero);
		int r = (int)MathF.Round(rIn, MidpointRounding.AwayFromZero);

		float qRem = qIn - q;
		float rRem = rIn - r;

		if (MathF.Abs(qRem) >= MathF.Abs(rRem))
			q += (int)MathF.Round(0.5f * rRem + qRem, MidpointRounding.AwayFromZero);
		else
			r += (int)MathF.Round(0.5f * qRem + rRem, MidpointRounding.AwayFromZero);

		return new HexMapCellId(q, r, 0);
	}

	// convert an x/z point on a unit hex grid (height = 1, radius = 1) into a cell id
	// https://www.redblobgames.com/grids/hexagons/#pixel-to-hex
	public static HexMapCellId FromUnitPoint(Vector3 point)
	{
		float q = Sqrt3 / 3f * point.X - 1f / 3f * point.Z;
		float r = 2f / 3f * point.Z;
		HexMapCellId cellId = AxialRound(q, r);

		return new HexMapCellId(
			cellId.Q,
			cellId.R,
			(int)MathF.Round(point.Y, MidpointRounding.AwayFromZero));
	}

	// pixel center of this cell on a unit hex grid https://www.redblobgames.com/grids/hexagons/#hex-to-pixel
	public Vector3 UnitCenter() => new Vector3(Sqrt3 * Q + Sqrt3_2 * R, Y, 1.5f * R);

	// convert to odd-r coordinates https://www.redblobgames.com/grids/hexagons/#conversions-offset
	public Vector3I ToOddR() => new Vector3I(Q + (R - (R & 1)) / 2, Y, R);

	public static HexMapCellId FromOddR(Vector3I oddR) =>
		new HexMapCellId(oddR.X - (oddR.Z - (oddR.Z & 1)) / 2, oddR.Z, oddR.Y);

	// rotate the cell about the y-axis relative to another cell; the rotation happens in this
	// cell's y plane (the center's own y is ignored, like the source)
	// @param steps 60 degree steps to rotate
	// @param center cell to rotate about; default origin
	public HexMapCellId Rotate(int steps, HexMapCellId center = default)
	{
		center = new HexMapCellId(center.Q, center.R, Y);
		HexMapCellId pos = this - center;
		steps %= 6;
		if (steps < 0) steps += 6;
		return steps switch
		{
			0 => this,
			1 => new HexMapCellId(-pos.S, -pos.Q, 0) + center,
			2 => new HexMapCellId(pos.R, pos.S, 0) + center,
			3 => new HexMapCellId(-pos.Q, -pos.R, 0) + center,
			4 => new HexMapCellId(pos.S, pos.Q, 0) + center,
			_ => new HexMapCellId(-pos.R, -pos.S, 0) + center,
		};
	}

	// get all cells within radius of this cell, along the provided planes
	public HexMapIterRadial GetNeighbors(int radius = 1, HexPlanes planes = HexPlanes.All, bool includeCenter = false) =>
		new HexMapIterRadial(this, radius, !includeCenter, planes);
}

// Which axes a region expands along (mirrors the source's HexMapPlanes). An enabled plane
// expands by radius from the center; a disabled plane pins to the center coordinate.
[Flags]
public enum HexPlanes
{
	None = 0,
	Y = 1 << 0,
	Q = 1 << 1,
	R = 1 << 2,
	S = 1 << 3,
	All = Y | Q | R | S,
	QRS = Q | R | S,
	YRS = Y | R | S,
	YQS = Y | Q | S,
	YQR = Y | Q | R,
}

// HexMap cell iterator using axial coordinates to define a volume to iterate. The walk is
// r fastest, then q, then y, skipping any cell outside the s range or the int16 bounds —
// the source's deterministic (not distance-sorted) order.
public struct HexMapIterAxial : IEnumerator<HexMapCellId>, IEnumerable<HexMapCellId>
{
	private int _yMin, _yMax, _qMin, _qMax, _rMin, _rMax, _sMin, _sMax;
	private readonly HexMapCellId _center;
	private HexMapCellId _cell;
	private bool _started;

	public HexMapIterAxial(HexMapCellId center, int radius, HexPlanes planes)
	{
		_center = center;

		// the source takes an unsigned radius; a negative value would be unrepresentable there
		if (radius < 0) radius = 0;

		if ((planes & HexPlanes.Y) != 0) { _yMin = center.Y - radius; _yMax = center.Y + radius; }
		else { _yMin = _yMax = center.Y; }

		if ((planes & HexPlanes.Q) != 0) { _qMin = center.Q - radius; _qMax = center.Q + radius; }
		else { _qMin = _qMax = center.Q; }

		if ((planes & HexPlanes.R) != 0) { _rMin = center.R - radius; _rMax = center.R + radius; }
		else { _rMin = _rMax = center.R; }

		if ((planes & HexPlanes.S) != 0) { _sMin = center.S - radius; _sMax = center.S + radius; }
		else { _sMin = _sMax = center.S; }

		_cell = new HexMapCellId(_qMin, _rMin, _yMin);
		_started = false;
		AdvanceUntilValid();
	}

	public HexMapCellId Center => _center;
	public HexMapCellId Current => _cell;
	object IEnumerator.Current => _cell;

	internal void Advance()
	{
		if (_cell.R < _rMax) _cell = new HexMapCellId(_cell.Q, _cell.R + 1, _cell.Y);
		else if (_cell.Q < _qMax) _cell = new HexMapCellId(_cell.Q + 1, _rMin, _cell.Y);
		else if (_cell.Y < _yMax) _cell = new HexMapCellId(_qMin, _rMin, _cell.Y + 1);
		else _cell = HexMapCellId.Invalid;
	}

	internal void AdvanceUntilValid()
	{
		// the source loop computes S on the INVALID sentinel and breaks on the next step;
		// checking first is equivalent and keeps S off int.MaxValue
		while (_cell != HexMapCellId.Invalid &&
			(_cell.S < _sMin || _cell.S > _sMax || !_cell.InBounds))
		{
			Advance();
		}
	}

	// the source iterator's operator++ is advance-then-yield, but the constructor already
	// positioned on the first valid cell — the first MoveNext yields it, later ones advance
	public bool MoveNext()
	{
		if (!_started)
		{
			_started = true;
			return _cell != HexMapCellId.Invalid;
		}
		if (_cell == HexMapCellId.Invalid) return false;
		Advance();
		AdvanceUntilValid();
		return _cell != HexMapCellId.Invalid;
	}

	public void Reset() => throw new NotSupportedException();
	public void Dispose() { }

	// mirrors the source's begin(): rewind to the bounds origin. Returns a copy, so foreach
	// on an already-partially-iterated value still sees the whole region.
	public HexMapIterAxial GetEnumerator()
	{
		var iter = this;
		iter._cell = new HexMapCellId(iter._qMin, iter._rMin, iter._yMin);
		iter._started = false;
		iter.AdvanceUntilValid();
		return iter;
	}

	IEnumerator<HexMapCellId> IEnumerable<HexMapCellId>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Axial iterator filtered by cell distance (plane + |dy|: a prism, not a ball). Mirrors the
// source's HexMapIterRadial, which owns a HexMapIterAxial and prunes its walk.
public struct HexMapIterRadial : IEnumerator<HexMapCellId>, IEnumerable<HexMapCellId>
{
	private HexMapIterAxial _axial;
	private readonly int _radius;
	private readonly bool _excludeCenter;
	private bool _started;

	public HexMapIterRadial(HexMapCellId center, int radius, bool excludeCenter, HexPlanes planes)
	{
		if (radius < 0) radius = 0;
		_axial = new HexMapIterAxial(center, radius, planes);
		_radius = radius;
		_excludeCenter = excludeCenter;
		_started = false;
		AdvanceUntilValid();
	}

	public HexMapCellId Center => _axial.Center;
	public HexMapCellId Current => _axial.Current;
	object IEnumerator.Current => _axial.Current;

	private void AdvanceUntilValid()
	{
		// the source loop also spins when the cell is INVALID; checking first is equivalent
		// and keeps Distance off the INVALID sentinel
		while (_axial.Current != HexMapCellId.Invalid &&
			((_excludeCenter && _axial.Current == _axial.Center) ||
				_axial.Center.Distance(_axial.Current) > _radius))
		{
			_axial.Advance();
			_axial.AdvanceUntilValid();
		}
	}

	public bool MoveNext()
	{
		if (!_started)
		{
			_started = true;
			return _axial.Current != HexMapCellId.Invalid;
		}
		if (_axial.Current == HexMapCellId.Invalid) return false;
		_axial.Advance();
		_axial.AdvanceUntilValid();
		AdvanceUntilValid();
		return _axial.Current != HexMapCellId.Invalid;
	}

	public void Reset() => throw new NotSupportedException();
	public void Dispose() { }

	public HexMapIterRadial GetEnumerator()
	{
		var iter = this;
		iter._axial = _axial.GetEnumerator();
		iter._started = false;
		iter.AdvanceUntilValid();
		return iter;
	}

	IEnumerator<HexMapCellId> IEnumerable<HexMapCellId>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Cube-space rectangle iterator: projects two continuous unit-space points onto odd-r rows and
// walks the rows x fastest, then odd-r z, then y. The odd-r corner x-shifts handle the two
// diagonally adjacent cells a vertical boundary can slice through (see the source's table in
// iter_cube.cpp); this is the quad/lasso selection path.
public struct HexMapIterCube : IEnumerator<HexMapCellId>, IEnumerable<HexMapCellId>
{
	private static readonly Vector3I InvalidPos = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);

	private readonly Vector3 _a;
	private readonly Vector3 _b;
	private readonly Vector3I _min;
	private readonly Vector3I _max;
	private Vector3I _pos;
	private readonly int _minXShiftEven, _minXShiftOdd;
	private readonly int _maxXShiftEven, _maxXShiftOdd;
	private int _maxX;
	private bool _started;

	public HexMapIterCube(Vector3 a, Vector3 b)
	{
		// the source sorts the two points lexicographically before doing anything else
		if (LessThan(b, a)) { Vector3 tmp = a; a = b; b = tmp; }
		_a = a;
		_b = b;

		HexMapCellId minCell = HexMapCellId.FromUnitPoint(a);
		HexMapCellId maxCell = HexMapCellId.FromUnitPoint(b);
		float minXCenter = minCell.UnitCenter().X;
		float maxXCenter = maxCell.UnitCenter().X;

		_min = _pos = minCell.ToOddR();
		_max = maxCell.ToOddR();

		// The location of the region corner within its cell matters for hex cells, in x
		// specifically: a vertical line through a hex cell exits through the southwest or
		// southeast cell depending on which side of the cell center it passes. The even/odd
		// row shifts below encode the source's table:
		// | min/max row parity | x vs cell center | even-row mod | odd-row mod |
		// | even               | x <  center      | 0           | -1          |
		// | even               | x >= center      | 0           | 0           |
		// | odd                | x <= center      | 0           | 0           |
		// | odd                | x >  center      | +1          | 0           |
		int minXShiftEven = 0, minXShiftOdd = 0;
		int maxXShiftEven = 0, maxXShiftOdd = 0;

		if ((_min.Z & 1) == 1 && _a.X >= minXCenter) minXShiftEven = 1;
		else if ((_min.Z & 1) == 0 && _a.X < minXCenter) minXShiftOdd = -1;

		if ((_max.Z & 1) == 1 && _b.X > maxXCenter) maxXShiftEven = 1;
		else if ((_max.Z & 1) == 0 && _b.X <= maxXCenter) maxXShiftOdd = -1;

		_minXShiftEven = minXShiftEven;
		_minXShiftOdd = minXShiftOdd;
		_maxXShiftEven = maxXShiftEven;
		_maxXShiftOdd = maxXShiftOdd;
		_maxX = _max.X + RowShift(maxXShiftEven, maxXShiftOdd, _pos.Z);
		_started = false;

		if (!minCell.InBounds) Advance();
	}

	public HexMapCellId Current => _pos == InvalidPos ? HexMapCellId.Invalid : HexMapCellId.FromOddR(_pos);
	object IEnumerator.Current => Current;

	private static bool LessThan(Vector3 a, Vector3 b) =>
		a.X != b.X ? a.X < b.X : (a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z);

	private static int RowShift(int even, int odd, int z) => (z & 1) == 0 ? even : odd;

	internal void Advance()
	{
		do
		{
			if (_pos.X < _maxX)
			{
				_pos = new Vector3I(_pos.X + 1, _pos.Y, _pos.Z);
			}
			else if (_pos.Z < _max.Z)
			{
				int nextZ = _pos.Z + 1;
				_pos = new Vector3I(_min.X + RowShift(_minXShiftEven, _minXShiftOdd, nextZ), _pos.Y, nextZ);
				_maxX = _max.X + RowShift(_maxXShiftEven, _maxXShiftOdd, nextZ);
			}
			else if (_pos.Y < _max.Y)
			{
				_pos = new Vector3I(_min.X, _pos.Y + 1, _min.Z);
			}
			else
			{
				_pos = InvalidPos;
				break;
			}
		} while (!HexMapCellId.FromOddR(_pos).InBounds);
	}

	public bool MoveNext()
	{
		if (!_started)
		{
			_started = true;
			return _pos != InvalidPos;
		}
		if (_pos == InvalidPos) return false;
		Advance();
		return _pos != InvalidPos;
	}

	public void Reset() => throw new NotSupportedException();
	public void Dispose() { }

	public HexMapIterCube GetEnumerator() => new HexMapIterCube(_a, _b);

	IEnumerator<HexMapCellId> IEnumerable<HexMapCellId>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Wrap-aware variants. The shape is generated in unwrapped axial space (so a client AoE
// preview around the player stays visually contiguous across a seam), and canonicalization
// through HexMath.WrapHex is opt-in for server-style membership queries. Lockstep sibling:
// server hex_region.rs (wrap_cell / WrappedRegion), both built on the same wrap helper.
public static class HexRegion
{
	// Canonicalize a cell's q/r into the map lap, preserving its y level. Mirrors the server's
	// wrap_hex_coords, including the "non-positive grid size is not a torus" pass-through.
	public static HexMapCellId WrapCell(HexMapCellId cell, int chunkHexRadius, int cols, int rows)
	{
		Vector2I wrapped = HexMath.WrapHex(cell.Q, cell.R, chunkHexRadius, cols, rows);
		return new HexMapCellId(wrapped.X, wrapped.Y, cell.Y);
	}

	// Canonicalize every cell of a region query and drop duplicates: a region wider than the
	// lap can reach the same torus point from two different off-lap neighbors, and shape
	// queries must not touch it twice. First-seen order is preserved.
	public static IEnumerable<HexMapCellId> Wrapped(this IEnumerable<HexMapCellId> cells, int chunkHexRadius, int cols, int rows)
	{
		var seen = new HashSet<HexMapCellId>();
		foreach (HexMapCellId cell in cells)
		{
			HexMapCellId wrapped = WrapCell(cell, chunkHexRadius, cols, rows);
			if (seen.Add(wrapped)) yield return wrapped;
		}
	}
}
