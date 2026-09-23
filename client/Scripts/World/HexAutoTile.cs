// Ported from godot-hex-map (bab38bd) - MIT. See THIRD_PARTY_NOTICES.md.
#nullable enable
using System;
using System.Collections.Generic;

// Deterministic, engine-free port of godot-hex-map's auto-tile rule engine
// (src/auto_tiled_node/auto_tiled_node.{h,cpp}). The executable spec is the source's
// demo/test/test_auto_tiled_node.gd (there are no C++ tests for it); the temp harness
// %TEMP%\opencode\hexautotile_test\ ports every rule test plus the exhaustive per-cell
// rotation test. PLAN.md §4.5: the resolver is purely cosmetic - server-owned types/masks go
// in, cosmetic variants come out, and no gameplay decision may read from it.
//
// The source is a Node3D that reads a HexMapInt node's cell values and writes a HexMapTiled
// node; here the input map and the output results are plain (q, r, y) dictionaries/lists so
// the resolver can be driven straight from subscribed rows.
//
// Deliberate fixes over the source (bug guards only; everything else is a
// statement-for-statement port, including the ring masks and the ordered first-match rules):
//   - ulong mask arithmetic uses 1UL: the source's `1 << i` for i >= 32 is undefined C++ and
//     wraps on x86 (silently aliasing cell 34 with cell 2).
//   - A rule with no pattern cells (HasPattern == false) is inert. The source's match()
//     returns true for such a rule on the first rotation (pattern_index_max is -1, so its
//     loop body never runs), which paints its tile over every candidate cell - a silent
//     foot-gun. If a fallback rule is wanted, set at least one cell (e.g. origin NotEmpty).
//   - Candidate expansion collects into a separate set (existing cells ∪ one search-pad pass)
//     instead of inserting into the HashSet being iterated, and candidates are sorted by
//     (q, r, y) so ApplyRules output is reproducible.
//   - Unknown ids in the rules order list are skipped. The source indexes the rules map
//     with the id (Godot's HashMap::operator[] default-inserts), so the miss adds an
//     enabled, all-disabled rule that then matches every candidate and clears it.
//   - GetCell for an offset outside the 35-cell pattern returns State InvalidOffset, and
//     mask/pad derivation and Match ignore any InvalidOffset cell instead of treating it
//     as set.
//
// An orientation is the source's HexMapTileOrientation upright rotation: 0 (Upright0)
// through 5 (Upright300), each step +60 degrees counter-clockwise, tried in ascending order
// by Match() - the source's Rule::match() orientation.rotate(1) order.

// Rule cell states (source Rule::CellState).
public enum AutoTileCellState : byte
{
	// Cell is ignored when matching (source RULE_CELL_STATE_DISABLED).
	Disabled = 0,
	// Cell must be empty when matching (source RULE_CELL_STATE_EMPTY).
	Empty = 1,
	// Cell must not be empty when matching (source RULE_CELL_STATE_NOT_EMPTY).
	NotEmpty = 2,
	// Cell must have exactly Type (source RULE_CELL_STATE_TYPE).
	Type = 3,
	// Cell must not have Type (source RULE_CELL_STATE_NOT_TYPE).
	NotType = 4,
	// Returned by GetCell for an offset outside the 35-cell pattern (source
	// RULE_CELL_INVALID_OFFSET); never stored in a pattern.
	InvalidOffset = 0xff,
}

// One cell of a rule pattern (source Rule::Cell).
public readonly struct HexAutoTileCell
{
	public AutoTileCellState State { get; }
	public ushort Type { get; }

	public HexAutoTileCell(AutoTileCellState state, ushort type = 0)
	{
		State = state;
		Type = type;
	}

	public override string ToString() =>
		State == AutoTileCellState.Type || State == AutoTileCellState.NotType
			? $"{State}({Type})"
			: State.ToString();
}

// A single pattern rule (source Rule): matches a 35-cell pattern of cell states and yields a
// tile id plus the first orientation that matched.
public sealed class HexAutoTileRule
{
	// Source Rule::ID_NOT_SET (USHRT_MAX); -1 for the int id space.
	public int Id { get; internal set; } = HexAutoTile.NoRuleId;

	// Tile to set when the rule matches; the source default and "clear" value is -1.
	public int Tile { get; set; } = -1;

	// Disabled rules are skipped by ApplyRules (source Rule::enabled).
	public bool Enabled { get; set; } = true;

	// Bitmask of pattern cells that must be fetched for a match, including whole rotation
	// rings (source Rule::cell_mask).
	public ulong CellMask { get; private set; }

	// Highest pattern index that Match() iterates (source Rule::pattern_index_max).
	public int PatternIndexMax { get; private set; } = -1;

	// How to pad the candidate search space to reach the nearest cell that must be set when
	// the origin cell must be empty (source Rule::search_pad). Radius -1 means no padding.
	public int SearchPadDeltaY { get; private set; }
	public int SearchPadRadius { get; private set; } = -1;

	// False when no cell in the pattern is set; such a rule is inert (see file header).
	public bool HasPattern => PatternIndexMax >= 0;

	// Cell pattern, indexed by HexAutoTile.CellOffsets (source Rule::pattern).
	private readonly HexAutoTileCell[] _pattern = new HexAutoTileCell[HexAutoTile.PatternCellCount];

	// Clear a cell so it is ignored when matching (source Rule::clear_cell).
	public void ClearCell((int Q, int R, int Y) offset) =>
		SetPatternCell(offset, new HexAutoTileCell(AutoTileCellState.Disabled));

	// Require a cell to be (or not be) a specific type when matching (source
	// Rule::set_cell_type). The type is truncated to 16 bits like the source's uint16_t.
	public void SetCellType((int Q, int R, int Y) offset, int type, bool invert = false) =>
		SetPatternCell(offset, new HexAutoTileCell(
			invert ? AutoTileCellState.NotType : AutoTileCellState.Type, (ushort)type));

	// Require a cell to be empty (or not empty) when matching (source Rule::set_cell_empty).
	public void SetCellEmpty((int Q, int R, int Y) offset, bool invert = false) =>
		SetPatternCell(offset, new HexAutoTileCell(
			invert ? AutoTileCellState.NotEmpty : AutoTileCellState.Empty));

	// Details for one cell of the pattern; State is InvalidOffset when the offset is not one
	// of the pattern's 35 cells (source Rule::get_cell).
	public HexAutoTileCell GetCell((int Q, int R, int Y) offset)
	{
		int index = HexAutoTile.FindCellIndex(offset);
		return index < 0 ? new HexAutoTileCell(AutoTileCellState.InvalidOffset) : _pattern[index];
	}

	// True when this rule matches the 35 cell values, false otherwise; orientation is the
	// first upright rotation (0..5) that matched (source Rule::match).
	//
	// cellValues are indexed by pattern position: cellValues[i] is the value at
	// origin + CellOffsets[i], or HexAutoTile.EmptyValue when the cell has no value.
	//
	// Port note: the source compares pattern[PatternIndex[rotation][i]] against cellValues[i]
	// (i is the upright pattern index), fails all rotations early on a central-cell (index
	// < 5) mismatch, and tries rotations 0..5 in order.
	public bool Match(int[] cellValues, out int orientation)
	{
		if (cellValues == null) throw new ArgumentNullException(nameof(cellValues));
		if (cellValues.Length < HexAutoTile.PatternCellCount)
			throw new ArgumentException(
				$"expected at least {HexAutoTile.PatternCellCount} cell values", nameof(cellValues));

		orientation = HexAutoTile.NoOrientation;

		// Bug guard: an empty pattern would otherwise "match" every candidate.
		if (!HasPattern) return false;

		for (int rotation = 0; rotation < HexAutoTile.OrientationCount; rotation++)
		{
			byte[] patternIndex = HexAutoTile.PatternIndexTables[rotation];
			bool matched = true;

			for (int i = 0; i <= PatternIndexMax; i++)
			{
				HexAutoTileCell cell = _pattern[patternIndex[i]];

				switch (cell.State)
				{
					case AutoTileCellState.Disabled:
					case AutoTileCellState.InvalidOffset:
						continue;
					case AutoTileCellState.Empty:
						if (cellValues[i] == HexAutoTile.EmptyValue) continue;
						break;
					case AutoTileCellState.NotEmpty:
						if (cellValues[i] != HexAutoTile.EmptyValue) continue;
						break;
					case AutoTileCellState.Type:
						if (cellValues[i] == cell.Type) continue;
						break;
					case AutoTileCellState.NotType:
						if (cellValues[i] != cell.Type) continue;
						break;
				}

				// Mismatch. The first five pattern cells are all at q = r = 0, so a mismatch
				// there fails every rotation (source early return).
				if (i < 5) return false;

				matched = false;
				break;
			}

			if (matched)
			{
				orientation = rotation;
				return true;
			}
		}

		return false;
	}

	// Recompute CellMask, PatternIndexMax, and the search pad from the pattern (source
	// Rule::update_internal). Cell setters and AddRule call this; it stays public so callers
	// can verify derived state.
	public void UpdateInternal()
	{
		CellMask = 0;
		PatternIndexMax = -1;

		for (int i = 0; i < HexAutoTile.PatternCellCount; i++)
		{
			AutoTileCellState state = _pattern[i].State;
			if (state == AutoTileCellState.Disabled || state == AutoTileCellState.InvalidOffset)
				continue;

			// The first five cells are at q = r = 0 (rotation has no effect on them), so each
			// is marked individually. Any set cell in an outer ring requires every cell of
			// that ring to be fetched because rotation can move the constraint anywhere on it.
			if (i < 5)
			{
				CellMask |= 1UL << i;
			}
			else if (i < 11)
			{
				CellMask |= 0b111111UL << 5;
				i = 10;
			}
			else if (i < 17)
			{
				CellMask |= 0b111111UL << 11;
				i = 16;
			}
			else if (i < 23)
			{
				CellMask |= 0b111111UL << 17;
				i = 22;
			}
			else
			{
				CellMask |= 0xFFFUL << 23;
				i = HexAutoTile.PatternCellCount - 1;
			}

			PatternIndexMax = i;
		}

		// The search pad is only needed when the origin cell must be empty: candidates are
		// normally the cells that have a value, so the search space has to grow to reach
		// cells whose origin is unset but whose nearest required cell is set.
		SearchPadDeltaY = 0;
		SearchPadRadius = -1;
		if (_pattern[0].State != AutoTileCellState.Empty) return;

		for (int i = 0; i < HexAutoTile.PatternCellCount; i++)
		{
			AutoTileCellState state = _pattern[i].State;
			if (state == AutoTileCellState.Disabled || state == AutoTileCellState.Empty ||
				state == AutoTileCellState.InvalidOffset)
				continue;

			// i == 0 is unreachable here (origin is Empty above); kept for parity with the
			// source's branch.
			if (i == 0) continue;
			else if (i == 1) { SearchPadDeltaY = -1; SearchPadRadius = 0; break; }
			else if (i == 2) { SearchPadDeltaY = 1; SearchPadRadius = 0; break; }
			else if (i == 3) { SearchPadDeltaY = -2; SearchPadRadius = 0; break; }
			else if (i == 4) { SearchPadDeltaY = 2; SearchPadRadius = 0; break; }
			else if (i < 11) { SearchPadDeltaY = 0; SearchPadRadius = 1; break; }
			else if (i < 17) { SearchPadDeltaY = -1; SearchPadRadius = 1; break; }
			else if (i < 23) { SearchPadDeltaY = 1; SearchPadRadius = 1; break; }
			else { SearchPadDeltaY = 0; SearchPadRadius = 2; break; }
		}
	}

	// Store a cell at a pattern offset; throws on an offset outside the 35-cell pattern
	// (the source errors out via ERR_FAIL_COND_MSG).
	private void SetPatternCell((int Q, int R, int Y) offset, HexAutoTileCell cell)
	{
		int index = HexAutoTile.FindCellIndex(offset);
		if (index < 0)
			throw new ArgumentOutOfRangeException(nameof(offset), offset,
				"offset is not one of the 35 rule pattern cells");
		_pattern[index] = cell;
		UpdateInternal();
	}
}

// The rule resolver (source HexMapAutoTiledNode): owns an ordered set of rules and applies
// them to an int-valued hex map, producing tile/orientation assignments for the cells that
// matched. Cells with no matching rule are left absent from the result.
public sealed class HexAutoTile
{
	public const int PatternCellCount = 35;
	public const int OrientationCount = 6;
	// Value sentinel for "this cell has no value" (source int_node value -1).
	public const int EmptyValue = -1;
	// "No rule id" sentinel for the int id space; the source uses Rule::ID_NOT_SET (USHRT_MAX).
	public const int NoRuleId = -1;
	// Match() sets this when no orientation matched; never a returned orientation.
	public const int NoOrientation = -1;

	// Offset of each cell in the rule pattern from the origin cell (source Rule::CellOffsets,
	// auto_tiled_node.h:116-161). The order is load-bearing:
	//   - the five q = r = 0 cells come first so a central mismatch can fail early,
	//   - then radius-1 cells (y = 0, y = 1, y = -1) so the search space can skip radius-2
	//     fetches when no radius-2 cell is set,
	//   - then radius-2, y = 0 cells.
	public static readonly (int Q, int R, int Y)[] CellOffsets =
	{
		// radius = 0, whole column, y = 0, 1, -1, 2, -2
		(0, 0, 0),   // 0: origin
		(0, 0, 1),
		(0, 0, -1),
		(0, 0, 2),
		(0, 0, -2),

		// radius = 1, starting at southwest corner, counter-clockwise
		(-1, 1, 0),  // 5
		(0, 1, 0),
		(1, 0, 0),
		(1, -1, 0),
		(0, -1, 0),
		(-1, 0, 0),  // 10

		// radius = 1, y = 1
		(-1, 1, 1),  // 11
		(0, 1, 1),
		(1, 0, 1),
		(1, -1, 1),
		(0, -1, 1),
		(-1, 0, 1),  // 16

		// radius = 1, y = -1
		(-1, 1, -1), // 17
		(0, 1, -1),
		(1, 0, -1),
		(1, -1, -1),
		(0, -1, -1),
		(-1, 0, -1), // 22

		// radius = 2, starting at southwest corner, counter-clockwise
		(-2, 2, 0),  // 23
		(-1, 2, 0),
		(0, 2, 0),
		(1, 1, 0),
		(2, 0, 0),   // 27
		(2, -1, 0),
		(2, -2, 0),
		(1, -2, 0),
		(0, -2, 0),  // 31
		(-1, -1, 0),
		(-2, 0, 0),
		(-2, 1, 0),  // 34
	};

	// Rotation permutation tables: PatternIndexTables[rotation][i] is the pattern index to
	// compare against cell value i (source Rule::PatternIndex, auto_tiled_node.h:174-217).
	// The tables are generated from the rotation formula - radius-1 blocks rotate by one
	// step per rotation (`B + ((k - r) mod 6)`), the radius-2 block by two (`23 + ((k - 2r)
	// mod 12)`) - and asserted equal to the tabulated values by the test harness.
	public static readonly byte[][] PatternIndexTables = BuildPatternIndexTables();

	private readonly Dictionary<int, HexAutoTileRule> _rules = new Dictionary<int, HexAutoTileRule>();
	private readonly List<int> _rulesOrder = new List<int>();

	// All defined rules, keyed by id (source HexMapAutoTiledNode::rules).
	public IReadOnlyDictionary<int, HexAutoTileRule> Rules => _rules;

	// Order the rules are applied in (source HexMapAutoTiledNode::rules_order). May contain
	// ids that no longer resolve; ApplyRules skips those.
	public IReadOnlyList<int> RulesOrder => _rulesOrder;

	// Add a rule, returning its id, or NoRuleId when the supplied id is already in use.
	//
	// A rule with Id == NoRuleId gets the next free id: max(id) + 1 over the order list
	// (source add_rule). The resolver holds the rule instance, not a copy, so later changes
	// through the rule's setters are reflected; Enabled/Tile are read at ApplyRules time.
	public int AddRule(HexAutoTileRule rule)
	{
		if (rule == null) throw new ArgumentNullException(nameof(rule));

		int id = rule.Id;
		if (id == NoRuleId)
		{
			id = 0;
			foreach (int existing in _rulesOrder)
				if (existing >= id)
					id = existing + 1;
		}

		if (id < 0 || _rules.ContainsKey(id)) return NoRuleId;

		rule.Id = id;
		rule.UpdateInternal();
		_rules[id] = rule;
		_rulesOrder.Add(id);
		return id;
	}

	// Replace the rule stored under rule.Id; false when no rule has that id (source
	// update_rule).
	public bool UpdateRule(HexAutoTileRule rule)
	{
		if (rule == null) throw new ArgumentNullException(nameof(rule));
		if (!_rules.ContainsKey(rule.Id)) return false;

		rule.UpdateInternal();
		_rules[rule.Id] = rule;
		return true;
	}

	// Remove a rule by id, also dropping it from the order list (source delete_rule).
	public bool RemoveRule(int id)
	{
		bool removed = _rules.Remove(id);
		_rulesOrder.Remove(id);
		return removed;
	}

	// The rule with this id, or null (source get_rule).
	public HexAutoTileRule? GetRule(int id) =>
		_rules.TryGetValue(id, out HexAutoTileRule? rule) ? rule : null;

	// Replace the order the rules are applied in (source set_rules_order). Ids that do not
	// resolve to a rule are kept but skipped by ApplyRules, so rules added later under those
	// ids still participate.
	public void SetRulesOrder(IEnumerable<int> ids)
	{
		if (ids == null) throw new ArgumentNullException(nameof(ids));

		_rulesOrder.Clear();
		_rulesOrder.AddRange(ids);
	}

	// Apply the rules to the int map, returning one result per matched cell:
	// (cell, tile, orientation). Cells with no matching rule are absent from the list, and
	// a rule whose tile is -1 produces a result with Tile == -1 (a clear). Results are
	// ordered by (q, r, y).
	//
	// The search space is every cell that has a value, padded per the rules' search pads so
	// rules with an empty origin can match cells with no value (source apply_rules).
	public List<((int Q, int R, int Y) Cell, int Tile, int Orientation)> ApplyRules(
		IReadOnlyDictionary<(int Q, int R, int Y), ushort> intMap)
	{
		if (intMap == null) throw new ArgumentNullException(nameof(intMap));

		var output = new List<((int Q, int R, int Y) Cell, int Tile, int Orientation)>();

		// Union the cell masks from all rules to know which neighboring cells must be
		// fetched. Merge the search pads at the same time: cellPadding[i] is the largest
		// radius any rule needs for existing cells shifted by (-2 + i) in y.
		ulong cellMask = 0;
		int[] cellPadding = { -1, -1, -1, -1, -1 };
		foreach (HexAutoTileRule rule in _rules.Values)
		{
			// Inert all-disabled rules contribute nothing (see file header).
			if (!rule.HasPattern) continue;

			cellMask |= rule.CellMask;

			int index = 2 + rule.SearchPadDeltaY;
			if (index < 0 || index >= cellPadding.Length) continue;
			if (rule.SearchPadRadius > cellPadding[index]) cellPadding[index] = rule.SearchPadRadius;
		}

		// Candidates are the defined cells plus, for each y-shift a rule needs, the cells
		// within that rule's nearest-required-cell radius around each defined cell.
		var candidates = new HashSet<(int Q, int R, int Y)>();
		foreach ((int Q, int R, int Y) key in intMap.Keys)
		{
			candidates.Add(key);
			for (int i = 0; i < cellPadding.Length; i++)
			{
				int radius = cellPadding[i];
				if (radius < 0) continue;
				AddPlaneNeighbors(candidates, key.Q, key.R, key.Y + (-2 + i), radius);
			}
		}

		// Deterministic order for reproducible output arrays.
		var ordered = new List<(int Q, int R, int Y)>(candidates);
		ordered.Sort(CompareCells);

		int[] values = new int[PatternCellCount];
		foreach ((int Q, int R, int Y) cell in ordered)
		{
			// Fetch only the cells some rule needs, treating missing cells as empty.
			for (int i = 0; i < PatternCellCount; i++)
			{
				if ((cellMask & (1UL << i)) == 0)
				{
					values[i] = EmptyValue;
					continue;
				}

				(int Q, int R, int Y) offset = CellOffsets[i];
				values[i] = intMap.TryGetValue(
					(cell.Q + offset.Q, cell.R + offset.R, cell.Y + offset.Y), out ushort value)
						? value
						: EmptyValue;
			}

			// First match in rules order wins; no match leaves the cell absent.
			foreach (int id in _rulesOrder)
			{
				if (!_rules.TryGetValue(id, out HexAutoTileRule? rule)) continue;
				if (!rule.Enabled || !rule.HasPattern) continue;

				if (rule.Match(values, out int orientation))
				{
					output.Add((cell, rule.Tile, orientation));
					break;
				}
			}
		}

		return output;
	}

	// The index of a pattern cell offset, or -1 when the offset is not one of the 35 cells
	// (source Rule::get_pattern_index).
	public static int FindCellIndex((int Q, int R, int Y) offset)
	{
		for (int i = 0; i < PatternCellCount; i++)
			if (CellOffsets[i] == offset)
				return i;
		return -1;
	}

	// get_neighbors(radius, QRS, include_center: true): every axial cell within hex distance
	// `radius` of (q, r) at the same y, plus the center. The order is the source's axial
	// (r fastest) order; ApplyRules sorts its candidates separately.
	private static void AddPlaneNeighbors(
		HashSet<(int Q, int R, int Y)> set, int q, int r, int y, int radius)
	{
		for (int dq = -radius; dq <= radius; dq++)
		{
			int rMin = Math.Max(-radius, -dq - radius);
			int rMax = Math.Min(radius, -dq + radius);
			for (int dr = rMin; dr <= rMax; dr++)
				set.Add((q + dq, r + dr, y));
		}
	}

	private static int CompareCells((int Q, int R, int Y) a, (int Q, int R, int Y) b)
	{
		int compare = a.Q.CompareTo(b.Q);
		if (compare != 0) return compare;
		compare = a.R.CompareTo(b.R);
		return compare != 0 ? compare : a.Y.CompareTo(b.Y);
	}

	private static byte[][] BuildPatternIndexTables()
	{
		var tables = new byte[OrientationCount][];
		for (int rotation = 0; rotation < OrientationCount; rotation++)
		{
			var table = new byte[PatternCellCount];
			for (int i = 0; i < PatternCellCount; i++) table[i] = (byte)i;

			// Each radius-1 ring (6 cells) rotates one step per orientation; the radius-2
			// ring (12 cells) rotates two steps per orientation.
			for (int k = 0; k < 6; k++)
			{
				int offset6 = ((k - rotation) % 6 + 6) % 6;
				table[5 + k] = (byte)(5 + offset6);
				table[11 + k] = (byte)(11 + offset6);
				table[17 + k] = (byte)(17 + offset6);
			}
			for (int k = 0; k < 12; k++)
			{
				int offset12 = ((k - 2 * rotation) % 12 + 12) % 12;
				table[23 + k] = (byte)(23 + offset12);
			}

			tables[rotation] = table;
		}
		return tables;
	}
}
