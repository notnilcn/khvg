// Portions derived from godot-hex-map (bab38bd) - MIT. See THIRD_PARTY_NOTICES.md.
//! Hex region library: cell ids and plane-bounded iterators ported from godot-hex-map
//! (`src/core/cell_id.*`, `src/core/iter_axial/radial/cube.*`, `src/core/planes.*`; the
//! source's `tests/` are the behavioral spec and are ported below).
//!
//! A cell is `(q, r, y)` with `s = -q - r`. A region is a bounding box in those four axial
//! coordinates: an enabled plane expands by `radius` from the center, a disabled plane pins
//! to the center coordinate — so `HexPlanes::QRS` selects a flat hex disk, `HexPlanes::ALL` a
//! prism, `HexPlanes::YQR` the plane where `s` is pinned, etc. `HexMapIterRadial` adds a cell
//! distance filter (hex distance + `|dy|`: a prism, not a ball) and `HexMapIterCube` walks the
//! odd-r rows covered by two continuous unit-space points. Iteration order is deterministic
//! (`r` fastest, then `q`, then `y`) but is NOT distance-sorted.
//!
//! The wrap-aware variants (`wrap_cell` / `WrappedRegion::wrapped`) generate the shape in
//! unwrapped axial space and then canonicalize each cell through `wrap_hex_coords`, dropping
//! duplicates (a shape wider than the lap can reach the same torus point twice). `y` is never
//! wrapped.
//!
//! Lockstep sibling: `client/Scripts/World/HexRegion.cs` — both copies must change together.
//! The C# side has no test project, so the ported assertions live in this file's tests.

// Phase 2A lands the port only; Phase 2B adoption (player/reducers.rs shape queries and the
// bullet/AoE preview consumers) is what references these items, so silence the dead-code lint
// for this file until then.
#![allow(dead_code)]

use std::collections::HashSet;

use super::wrap::wrap_hex_coords;

/// SQRT(3), floored to `f32` like the source's `Math_SQRT3` (which is `real_t` = float).
const SQRT3: f32 = 1.732_050_807_568_877_3_f32;
/// SQRT(3)/2.
const SQRT3_2: f32 = 0.866_025_403_784_438_6_f32;

/// Axial hex cell id: plane coordinates `q`/`r` plus a `y` level. `s` is derived.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct HexMapCellId {
    pub q: i32,
    pub r: i32,
    pub y: i32,
}

impl HexMapCellId {
    pub const ZERO: HexMapCellId = HexMapCellId { q: 0, r: 0, y: 0 };
    pub const INVALID: HexMapCellId = HexMapCellId { q: i32::MAX, r: i32::MAX, y: i32::MAX };

    pub const fn new(q: i32, r: i32, y: i32) -> Self {
        Self { q, r, y }
    }

    /// The `s` coordinate, for converting from axial to cube coordinates.
    pub const fn s(&self) -> i32 {
        -self.q - self.r
    }

    /// True when `q`/`r`/`y` all fit the source's int16 `Key` packing range.
    pub const fn in_bounds(&self) -> bool {
        self.q >= i16::MIN as i32
            && self.q <= i16::MAX as i32
            && self.r >= i16::MIN as i32
            && self.r <= i16::MAX as i32
            && self.y >= i16::MIN as i32
            && self.y <= i16::MAX as i32
    }

    /// Distance between two cells in cell units. This is the number of **faces** we traverse
    /// to get to the other node: the plane hex distance plus `|dy|`. It is calculated this
    /// way because allowing diagonal traversal up or down would make `neighbors(radius = N)`
    /// a prism instead of a rough sphere.
    pub fn distance(&self, other: HexMapCellId) -> i32 {
        // widened so the sentinel/pathological inputs cannot overflow the intermediate sum
        let dq = (other.q - self.q) as i64;
        let dr = (other.r - self.r) as i64;
        let dy = (other.y - self.y) as i64;
        let hex_distance = (dq.abs() + (dq + dr).abs() + dr.abs()) / 2;
        (hex_distance + dy.abs()) as i32
    }

    /// Axial rounding, based on <https://observablehq.com/@jrus/hexround>. The source uses
    /// Godot's `round()` (half away from zero); `f32::round()` has the same semantics.
    pub fn axial_round(q_in: f32, r_in: f32) -> HexMapCellId {
        let mut q = q_in.round() as i32;
        let mut r = r_in.round() as i32;

        let q_rem = q_in - q as f32;
        let r_rem = r_in - r as f32;

        if q_rem.abs() >= r_rem.abs() {
            q += (0.5 * r_rem + q_rem).round() as i32;
        } else {
            r += (0.5 * q_rem + r_rem).round() as i32;
        }

        HexMapCellId::new(q, r, 0)
    }

    /// Convert an `(x, y, z)` point on a unit hex grid (height = 1, radius = 1) into a cell
    /// id. <https://www.redblobgames.com/grids/hexagons/#pixel-to-hex>
    pub fn from_unit_point(point: (f32, f32, f32)) -> HexMapCellId {
        let q = SQRT3 / 3.0 * point.0 - 1.0 / 3.0 * point.2;
        let r = 2.0 / 3.0 * point.2;
        let cell_id = HexMapCellId::axial_round(q, r);

        HexMapCellId::new(cell_id.q, cell_id.r, point.1.round() as i32)
    }

    /// Pixel center of this cell on a unit hex grid.
    /// <https://www.redblobgames.com/grids/hexagons/#hex-to-pixel>
    pub fn unit_center(&self) -> (f32, f32, f32) {
        (
            SQRT3 * self.q as f32 + SQRT3_2 * self.r as f32,
            self.y as f32,
            1.5 * self.r as f32,
        )
    }

    /// Convert to odd-r coordinates.
    /// <https://www.redblobgames.com/grids/hexagons/#conversions-offset>
    pub fn to_oddr(&self) -> (i32, i32, i32) {
        (self.q + (self.r - (self.r & 1)) / 2, self.y, self.r)
    }

    pub fn from_oddr(odd_r: (i32, i32, i32)) -> HexMapCellId {
        HexMapCellId::new(
            odd_r.0 - (odd_r.2 - (odd_r.2 & 1)) / 2,
            odd_r.2,
            odd_r.1,
        )
    }

    /// Rotate the cell about the y-axis relative to another cell. Only y-axis rotation is
    /// supported: the rotation happens in this cell's y plane, so the center's own `y` is
    /// ignored. `steps` is in 60 degree increments.
    pub fn rotate(&self, steps: i32, center: HexMapCellId) -> HexMapCellId {
        let center = HexMapCellId::new(center.q, center.r, self.y);
        let pos = *self - center;
        let mut steps = steps % 6;
        if steps < 0 {
            steps += 6;
        }
        match steps {
            0 => *self,
            1 => HexMapCellId::new(-pos.s(), -pos.q, 0) + center,
            2 => HexMapCellId::new(pos.r, pos.s(), 0) + center,
            3 => HexMapCellId::new(-pos.q, -pos.r, 0) + center,
            4 => HexMapCellId::new(pos.s(), pos.q, 0) + center,
            _ => HexMapCellId::new(-pos.r, -pos.s(), 0) + center,
        }
    }

    /// All cells within `radius` of this cell, along the provided planes. A cell id is a
    /// hexagonal prism around itself: `radius` expands the planes and the distance filter
    /// includes the vertical axis.
    pub fn neighbors(
        &self,
        radius: i32,
        planes: HexPlanes,
        include_center: bool,
    ) -> HexMapIterRadial {
        HexMapIterRadial::new(*self, radius, !include_center, planes)
    }
}

impl std::ops::Add for HexMapCellId {
    type Output = HexMapCellId;

    fn add(self, other: HexMapCellId) -> HexMapCellId {
        HexMapCellId::new(self.q + other.q, self.r + other.r, self.y + other.y)
    }
}

impl std::ops::Sub for HexMapCellId {
    type Output = HexMapCellId;

    fn sub(self, other: HexMapCellId) -> HexMapCellId {
        HexMapCellId::new(self.q - other.q, self.r - other.r, self.y - other.y)
    }
}

impl std::ops::Mul<i32> for HexMapCellId {
    type Output = HexMapCellId;

    fn mul(self, m: i32) -> HexMapCellId {
        HexMapCellId::new(self.q * m, self.r * m, self.y * m)
    }
}

impl std::ops::Neg for HexMapCellId {
    type Output = HexMapCellId;

    fn neg(self) -> HexMapCellId {
        HexMapCellId::new(-self.q, -self.r, -self.y)
    }
}

/// Which axes a region expands along (mirrors the source's `HexMapPlanes`). An enabled plane
/// expands by `radius` from the center; a disabled plane pins to the center coordinate.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct HexPlanes {
    pub y: bool,
    pub q: bool,
    pub r: bool,
    pub s: bool,
}

impl HexPlanes {
    pub const ALL: HexPlanes = HexPlanes { y: true, q: true, r: true, s: true };
    pub const QRS: HexPlanes = HexPlanes { y: false, q: true, r: true, s: true };
    pub const YRS: HexPlanes = HexPlanes { y: true, q: false, r: true, s: true };
    pub const YQS: HexPlanes = HexPlanes { y: true, q: true, r: false, s: true };
    pub const YQR: HexPlanes = HexPlanes { y: true, q: true, r: true, s: false };
}

/// Plane-bounded axial iterator: the region's bounds are computed up front, then the walk is
/// `r` fastest, then `q`, then `y`, skipping any cell outside the `s` range or the int16
/// `in_bounds` space.
pub struct HexMapIterAxial {
    y_max: i32,
    q_min: i32,
    q_max: i32,
    r_min: i32,
    r_max: i32,
    s_min: i32,
    s_max: i32,
    center: HexMapCellId,
    cell: HexMapCellId,
}

impl HexMapIterAxial {
    pub fn new(center: HexMapCellId, radius: i32, planes: HexPlanes) -> Self {
        // The source takes an unsigned radius; a negative value would only be representable
        // there as a huge number, so clamp it to 0 here.
        let radius = radius.max(0);

        let (y_min, y_max) = if planes.y {
            (center.y - radius, center.y + radius)
        } else {
            (center.y, center.y)
        };

        let (q_min, q_max) = if planes.q {
            (center.q - radius, center.q + radius)
        } else {
            (center.q, center.q)
        };

        let (r_min, r_max) = if planes.r {
            (center.r - radius, center.r + radius)
        } else {
            (center.r, center.r)
        };

        let (s_min, s_max) = if planes.s {
            (center.s() - radius, center.s() + radius)
        } else {
            (center.s(), center.s())
        };

        // the source keeps y_min for its begin(); the yield-then-advance Iterator form only
        // needs the bounds that `advance` reads
        let mut iter = Self {
            y_max,
            q_min,
            q_max,
            r_min,
            r_max,
            s_min,
            s_max,
            center,
            cell: HexMapCellId::new(q_min, r_min, y_min),
        };
        iter.advance_until_valid();
        iter
    }

    pub fn center(&self) -> HexMapCellId {
        self.center
    }

    pub fn cell(&self) -> HexMapCellId {
        self.cell
    }

    fn advance(&mut self) {
        if self.cell.r < self.r_max {
            self.cell.r += 1;
        } else if self.cell.q < self.q_max {
            self.cell = HexMapCellId::new(self.cell.q + 1, self.r_min, self.cell.y);
        } else if self.cell.y < self.y_max {
            self.cell = HexMapCellId::new(self.q_min, self.r_min, self.cell.y + 1);
        } else {
            self.cell = HexMapCellId::INVALID;
        }
    }

    fn advance_until_valid(&mut self) {
        // The source loop computes `s` on the INVALID sentinel and breaks on the next step;
        // checking first is equivalent and keeps `s` off `i32::MAX` (which would overflow).
        while self.cell != HexMapCellId::INVALID
            && (self.cell.s() < self.s_min || self.cell.s() > self.s_max || !self.cell.in_bounds())
        {
            self.advance();
        }
    }
}

impl Iterator for HexMapIterAxial {
    type Item = HexMapCellId;

    // The source's iterator is positioned on the first cell before iteration and its ++
    // advances; yield-then-advance produces the same sequence.
    fn next(&mut self) -> Option<HexMapCellId> {
        if self.cell == HexMapCellId::INVALID {
            return None;
        }
        let current = self.cell;
        self.advance();
        self.advance_until_valid();
        Some(current)
    }
}

/// Axial iterator filtered by cell distance (plane + `|dy|`: a prism, not a ball). Mirrors
/// the source's `HexMapIterRadial`, which owns a `HexMapIterAxial` and prunes its walk.
pub struct HexMapIterRadial {
    axial: HexMapIterAxial,
    radius: i32,
    exclude_center: bool,
}

impl HexMapIterRadial {
    pub fn new(
        center: HexMapCellId,
        radius: i32,
        exclude_center: bool,
        planes: HexPlanes,
    ) -> Self {
        let radius = radius.max(0);
        let mut iter = Self {
            axial: HexMapIterAxial::new(center, radius, planes),
            radius,
            exclude_center,
        };
        iter.advance_until_valid();
        iter
    }

    pub fn center(&self) -> HexMapCellId {
        self.axial.center
    }

    pub fn cell(&self) -> HexMapCellId {
        self.axial.cell
    }

    fn advance_until_valid(&mut self) {
        // The source loop also spins when the cell is INVALID; the explicit check is
        // equivalent and keeps `distance` off the INVALID sentinel.
        while self.axial.cell != HexMapCellId::INVALID
            && ((self.exclude_center && self.axial.cell == self.axial.center)
                || self.axial.center.distance(self.axial.cell) > self.radius)
        {
            self.axial.advance();
            self.axial.advance_until_valid();
        }
    }
}

impl Iterator for HexMapIterRadial {
    type Item = HexMapCellId;

    fn next(&mut self) -> Option<HexMapCellId> {
        if self.axial.cell == HexMapCellId::INVALID {
            return None;
        }
        let current = self.axial.cell;
        self.axial.advance();
        self.axial.advance_until_valid();
        self.advance_until_valid();
        Some(current)
    }
}

/// Cube-space rectangle iterator: projects two continuous unit-space points onto odd-r rows
/// and walks those rows `x` fastest, then odd-r `z`, then `y`. The odd-r corner x-shifts
/// handle the two diagonally adjacent cells a vertical boundary can slice through (the
/// source's table lives in `iter_cube.cpp`); this is the quad/lasso selection path.
pub struct HexMapIterCube {
    min: (i32, i32, i32),
    max: (i32, i32, i32),
    pos: (i32, i32, i32),
    min_x_shift: [i32; 2],
    max_x_shift: [i32; 2],
    max_x: i32,
}

impl HexMapIterCube {
    const INVALID_POS: (i32, i32, i32) = (i32::MAX, i32::MAX, i32::MAX);

    pub fn new(a: (f32, f32, f32), b: (f32, f32, f32)) -> Self {
        // The source sorts the two points lexicographically before doing anything else.
        let (a, b) = if unit_point_less(b, a) { (b, a) } else { (a, b) };

        let min_cell = HexMapCellId::from_unit_point(a);
        let max_cell = HexMapCellId::from_unit_point(b);
        let min_x_center = min_cell.unit_center().0;
        let max_x_center = max_cell.unit_center().0;

        let min = min_cell.to_oddr();
        let max = max_cell.to_oddr();

        // The location of the region corner within its cell matters for hex cells, in x
        // specifically: a vertical line through a hex cell exits through the southwest or
        // southeast cell depending on which side of the cell center it passes. The even/odd
        // row shifts below encode the source's table:
        // | min/max row parity | x vs cell center | even-row mod | odd-row mod |
        // | even               | x <  center      | 0           | -1          |
        // | even               | x >= center      | 0           | 0           |
        // | odd                | x <= center      | 0           | 0           |
        // | odd                | x >  center      | +1          | 0           |
        let mut min_x_shift = [0, 0];
        let mut max_x_shift = [0, 0];

        if (min.2 & 1) == 1 && a.0 >= min_x_center {
            min_x_shift[0] = 1;
        } else if (min.2 & 1) == 0 && a.0 < min_x_center {
            min_x_shift[1] = -1;
        }

        if (max.2 & 1) == 1 && b.0 > max_x_center {
            max_x_shift[0] = 1;
        } else if (max.2 & 1) == 0 && b.0 <= max_x_center {
            max_x_shift[1] = -1;
        }

        let pos = min;
        let max_x = max.0 + max_x_shift[(pos.2 & 1) as usize];

        let mut iter = Self {
            min,
            max,
            pos,
            min_x_shift,
            max_x_shift,
            max_x,
        };
        if !min_cell.in_bounds() {
            iter.advance();
        }
        iter
    }

    pub fn cell(&self) -> HexMapCellId {
        if self.pos == Self::INVALID_POS {
            HexMapCellId::INVALID
        } else {
            HexMapCellId::from_oddr(self.pos)
        }
    }

    fn advance(&mut self) {
        loop {
            if self.pos.0 < self.max_x {
                self.pos.0 += 1;
            } else if self.pos.2 < self.max.2 {
                self.pos.2 += 1;
                self.pos.0 = self.min.0 + self.min_x_shift[(self.pos.2 & 1) as usize];
                self.max_x = self.max.0 + self.max_x_shift[(self.pos.2 & 1) as usize];
            } else if self.pos.1 < self.max.1 {
                self.pos.0 = self.min.0;
                self.pos.2 = self.min.2;
                self.pos.1 += 1;
            } else {
                self.pos = Self::INVALID_POS;
                break;
            }
            if HexMapCellId::from_oddr(self.pos).in_bounds() {
                break;
            }
        }
    }
}

impl Iterator for HexMapIterCube {
    type Item = HexMapCellId;

    fn next(&mut self) -> Option<HexMapCellId> {
        if self.pos == Self::INVALID_POS {
            return None;
        }
        let current = HexMapCellId::from_oddr(self.pos);
        self.advance();
        Some(current)
    }
}

/// Lexicographic `Vector3 <` comparison, matching Godot's operator ordering (x, then y,
/// then z) that the source relies on.
fn unit_point_less(a: (f32, f32, f32), b: (f32, f32, f32)) -> bool {
    if a.0 != b.0 {
        a.0 < b.0
    } else if a.1 != b.1 {
        a.1 < b.1
    } else {
        a.2 < b.2
    }
}

/// Canonicalize a cell's `q`/`r` into the map lap, preserving its `y` level. Built on
/// `wrap_hex_coords`, including its "non-positive grid size is not a torus" pass-through.
pub fn wrap_cell(
    cell: HexMapCellId,
    chunk_hex_radius: i32,
    chunk_cols: i32,
    chunk_rows: i32,
) -> HexMapCellId {
    let (q, r) = wrap_hex_coords(cell.q, cell.r, chunk_hex_radius, chunk_cols, chunk_rows);
    HexMapCellId::new(q, r, cell.y)
}

/// Wrap-aware region iterator: canonicalizes each cell through `wrap_hex_coords` and drops
/// duplicates (a shape wider than the lap can reach the same torus point from two different
/// off-lap neighbors; a shape query must not touch it twice). First-seen order is preserved.
pub struct WrappedHexIter<I> {
    inner: I,
    chunk_hex_radius: i32,
    chunk_cols: i32,
    chunk_rows: i32,
    seen: HashSet<HexMapCellId>,
}

/// Extension for any region iterator: `.wrapped(radius, cols, rows)` canonicalizes and
/// dedupes the yielded cells.
pub trait WrappedRegion: Iterator<Item = HexMapCellId> + Sized {
    fn wrapped(
        self,
        chunk_hex_radius: i32,
        chunk_cols: i32,
        chunk_rows: i32,
    ) -> WrappedHexIter<Self> {
        WrappedHexIter {
            inner: self,
            chunk_hex_radius,
            chunk_cols,
            chunk_rows,
            seen: HashSet::new(),
        }
    }
}

impl<I: Iterator<Item = HexMapCellId>> WrappedRegion for I {}

impl<I: Iterator<Item = HexMapCellId>> Iterator for WrappedHexIter<I> {
    type Item = HexMapCellId;

    fn next(&mut self) -> Option<HexMapCellId> {
        for cell in self.inner.by_ref() {
            let wrapped = wrap_cell(
                cell,
                self.chunk_hex_radius,
                self.chunk_cols,
                self.chunk_rows,
            );
            if self.seen.insert(wrapped) {
                return Some(wrapped);
            }
        }
        None
    }
}

// Tests ported from godot-hex-map's tests/hex_map_cell_id.cpp, hex_map_iter_axial.cpp,
// hex_map_iter_radial.cpp, and hex_map_iter_cube.cpp, plus order/count assertions for the
// deterministic iteration order and the wrap-aware variant. These run standalone (a
// SpacetimeDB module cannot link a test binary); see the Phase 2A task notes for the temp
// crate command.
#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::BTreeSet;

    fn c(q: i32, r: i32, y: i32) -> HexMapCellId {
        HexMapCellId::new(q, r, y)
    }

    fn set(cells: &[HexMapCellId]) -> BTreeSet<HexMapCellId> {
        cells.iter().copied().collect()
    }

    const SHRT_MAX: i32 = i16::MAX as i32;

    // ---- hex_map_cell_id.cpp -----------------------------------------------------------

    #[test]
    fn cell_add_sub_neg() {
        assert_eq!(c(0, 0, 0) + c(1, 2, 3), c(1, 2, 3));
        assert_eq!(c(0, 0, 0) + c(-1, -2, -3), c(-1, -2, -3));
        assert_eq!(c(0, 0, 0) - c(1, 2, 3), c(-1, -2, -3));
        assert_eq!(c(0, 0, 0) - c(-1, -2, -3), c(1, 2, 3));
        assert_eq!(-c(1, 2, 3), c(-1, -2, -3));
        assert_eq!(c(2, -3, 4) * 2, c(4, -6, 8));
    }

    #[test]
    fn distance_matches_source_spec() {
        assert_eq!(c(0, 0, 0).distance(c(0, 0, 0)), 0);

        // movement along an axis
        assert_eq!(c(0, 0, 0).distance(c(10, 0, 0)), 10); // q
        assert_eq!(c(0, 0, 0).distance(c(-10, 0, 0)), 10);
        assert_eq!(c(0, 0, 0).distance(c(0, 10, 0)), 10); // r
        assert_eq!(c(0, 0, 0).distance(c(0, -10, 0)), 10);
        assert_eq!(c(0, 0, 0).distance(c(0, 0, 10)), 10); // y
        assert_eq!(c(0, 0, 0).distance(c(0, 0, -10)), 10);
        assert_eq!(c(0, 0, 0).distance(c(10, -10, 0)), 10); // s
        assert_eq!(c(0, 0, 0).distance(c(-10, 10, 0)), 10);

        assert_eq!(c(0, 0, 0).distance(c(1, 1, 0)), 2);
        assert_eq!(c(0, 0, 0).distance(c(1, 1, 1)), 3);
        assert_eq!(c(0, 0, 0).distance(c(1, 1, 2)), 4);

        // up/down neighbors off axis
        assert_eq!(c(0, 0, 0).distance(c(1, 0, 1)), 2);
        assert_eq!(c(0, 0, 0).distance(c(0, 1, 1)), 2);
        assert_eq!(c(0, 0, 0).distance(c(1, 0, -1)), 2);

        // diagonal along the r axis
        assert_eq!(c(0, 0, 0).distance(c(0, 2, 2)), 4);
        assert_eq!(c(0, 0, 0).distance(c(0, 3, 3)), 6);
        assert_eq!(c(0, 0, 0).distance(c(0, 4, 4)), 8);

        // diagonal not on any particular axis
        assert_eq!(c(0, 0, 0).distance(c(2, -1, 5)), 7);
        assert_eq!(c(0, 0, 0).distance(c(4, -2, 2)), 6);
        assert_eq!(c(0, 0, 0).distance(c(123, -24, 21)), 123 + 21);

        // and the reverse direction agrees
        assert_eq!(c(2, -1, 5).distance(c(0, 0, 0)), 7);
    }

    #[test]
    fn axial_round_is_half_away_from_zero() {
        // exact ties: f32::round is half away from zero, and the q/r tie-break keeps the
        // nearest cube coordinate
        assert_eq!(HexMapCellId::axial_round(0.5, 0.5), c(0, 1, 0));
        assert_eq!(HexMapCellId::axial_round(-0.5, -0.5), c(0, -1, 0));
        assert_eq!(HexMapCellId::axial_round(0.5, 0.0), c(0, 0, 0));
        assert_eq!(HexMapCellId::axial_round(1.5, 0.0), c(1, 0, 0));

        assert_eq!(HexMapCellId::axial_round(0.0, 0.0), c(0, 0, 0));
        assert_eq!(HexMapCellId::axial_round(0.49, -0.49), c(0, 0, 0));
        assert_eq!(HexMapCellId::axial_round(0.6, 0.3), c(1, 0, 0));
        assert_eq!(HexMapCellId::axial_round(0.25, 0.75), c(0, 1, 0));
        assert_eq!(HexMapCellId::axial_round(0.75, 0.25), c(1, 0, 0));
    }

    #[test]
    fn unit_center_roundtrips_through_from_unit_point() {
        for cell in [
            c(0, 0, 0),
            c(1, 0, 0),
            c(-2, 3, 0),
            c(37, 5, 0),
            c(-11, -4, 0),
        ] {
            assert_eq!(
                HexMapCellId::from_unit_point(cell.unit_center()),
                cell,
                "unit center of {cell:?} did not round-trip"
            );
        }

        // the y axis is rounded independently, like the source's from_unit_point
        assert_eq!(HexMapCellId::from_unit_point((0.0, 2.4, 0.0)), c(0, 0, 2));
        assert_eq!(HexMapCellId::from_unit_point((0.0, -2.6, 0.0)), c(0, 0, -3));
    }

    #[test]
    fn odd_r_roundtrip() {
        // The C++ suite round-trips a Vector3i whose components are (q, y, r); Rust has no
        // equivalent variant type, so the portable half is the odd-r bijection.
        for cell in [c(0, 0, 0), c(1, 0, 0), c(-1, 1, 0), c(-2, 3, -4), c(37, 5, 2)] {
            assert_eq!(HexMapCellId::from_oddr(cell.to_oddr()), cell);
        }
        assert_eq!(c(1, 0, 0).to_oddr(), (1, 0, 0));
        assert_eq!(c(0, 1, 0).to_oddr(), (0, 0, 1));
        assert_eq!(c(0, -1, 0).to_oddr(), (-1, 0, -1));
        assert_eq!(c(-1, 1, 0).to_oddr(), (-1, 0, 1));
    }

    #[test]
    fn rotate_steps_about_origin() {
        // east rotates into northeast and around the six directions
        let east = c(1, 0, 0);
        assert_eq!(east.rotate(0, c(0, 0, 0)), c(1, 0, 0));
        assert_eq!(east.rotate(1, c(0, 0, 0)), c(1, -1, 0)); // northeast
        assert_eq!(east.rotate(2, c(0, 0, 0)), c(0, -1, 0)); // northwest
        assert_eq!(east.rotate(3, c(0, 0, 0)), c(-1, 0, 0)); // west
        assert_eq!(east.rotate(4, c(0, 0, 0)), c(-1, 1, 0)); // southwest
        assert_eq!(east.rotate(5, c(0, 0, 0)), c(0, 1, 0)); // southeast
        assert_eq!(east.rotate(6, c(0, 0, 0)), c(1, 0, 0));
        assert_eq!(east.rotate(-1, c(0, 0, 0)), c(0, 1, 0));
        assert_eq!(east.rotate(-6, c(0, 0, 0)), c(1, 0, 0));

        // origin is a fixed point
        assert_eq!(c(0, 0, 0).rotate(3, c(0, 0, 0)), c(0, 0, 0));
    }

    #[test]
    fn rotate_about_center_ignores_center_y() {
        // rotation happens in the cell's own y plane: the center's y is replaced by this
        // cell's, so a same-plane center rotates normally and the y survives
        assert_eq!(c(2, 0, 3).rotate(3, c(1, 0, 0)), c(0, 0, 3));
        assert_eq!(c(2, 0, 3).rotate(1, c(1, 1, -7)), c(1, 0, 3));

        // six one-step rotations return to the start, about a non-origin center
        let center = c(-3, 2, 5);
        let mut cell = c(1, 4, 5);
        let start = cell;
        for _ in 0..6 {
            cell = cell.rotate(1, center);
        }
        assert_eq!(cell, start);
    }

    // ---- hex_map_iter_axial.cpp --------------------------------------------------------

    #[test]
    fn iter_axial_radius_zero() {
        let cells: Vec<_> = HexMapIterAxial::new(c(0, 0, 0), 0, HexPlanes::ALL).collect();
        assert_eq!(cells, vec![c(0, 0, 0)]);

        // a negative radius clamps to zero rather than panicking
        let cells: Vec<_> = HexMapIterAxial::new(c(0, 0, 0), -3, HexPlanes::ALL).collect();
        assert_eq!(cells, vec![c(0, 0, 0)]);
    }

    #[test]
    fn iter_axial_radius_one_all_set_and_order() {
        let expected_set = set(&[
            c(0, 0, -1),
            c(1, 0, -1),
            c(-1, 0, -1),
            c(0, 1, -1),
            c(-1, 1, -1),
            c(0, -1, -1),
            c(1, -1, -1),
            c(0, 0, 0),
            c(1, 0, 0),
            c(-1, 0, 0),
            c(0, 1, 0),
            c(-1, 1, 0),
            c(0, -1, 0),
            c(1, -1, 0),
            c(0, 0, 1),
            c(1, 0, 1),
            c(-1, 0, 1),
            c(0, 1, 1),
            c(-1, 1, 1),
            c(0, -1, 1),
            c(1, -1, 1),
        ]);

        let cells: Vec<_> = HexMapIterAxial::new(c(0, 0, 0), 1, HexPlanes::ALL).collect();
        assert_eq!(cells.len(), expected_set.len());
        assert_eq!(set(&cells), expected_set);

        // the deterministic walk: r fastest, then q, then y, skipping out-of-s band cells
        assert_eq!(
            cells,
            vec![
                c(-1, 0, -1),
                c(-1, 1, -1),
                c(0, -1, -1),
                c(0, 0, -1),
                c(0, 1, -1),
                c(1, -1, -1),
                c(1, 0, -1),
                c(-1, 0, 0),
                c(-1, 1, 0),
                c(0, -1, 0),
                c(0, 0, 0),
                c(0, 1, 0),
                c(1, -1, 0),
                c(1, 0, 0),
                c(-1, 0, 1),
                c(-1, 1, 1),
                c(0, -1, 1),
                c(0, 0, 1),
                c(0, 1, 1),
                c(1, -1, 1),
                c(1, 0, 1),
            ]
        );
    }

    #[test]
    fn iter_axial_plane_semantics() {
        // QRS pins y: a flat hex disk (the source's neighbor disk)
        let qrs: Vec<_> = HexMapIterAxial::new(c(0, 0, 0), 1, HexPlanes::QRS).collect();
        assert_eq!(
            qrs,
            vec![
                c(-1, 0, 0),
                c(-1, 1, 0),
                c(0, -1, 0),
                c(0, 0, 0),
                c(0, 1, 0),
                c(1, -1, 0),
                c(1, 0, 0),
            ]
        );

        // YQR pins s: the diagonal line q = -r, expanded by y
        let yqr: Vec<_> = HexMapIterAxial::new(c(0, 0, 0), 1, HexPlanes::YQR).collect();
        assert_eq!(
            yqr,
            vec![
                c(-1, 1, -1),
                c(0, 0, -1),
                c(1, -1, -1),
                c(-1, 1, 0),
                c(0, 0, 0),
                c(1, -1, 0),
                c(-1, 1, 1),
                c(0, 0, 1),
                c(1, -1, 1),
            ]
        );

        // YRS pins q
        let yrs: Vec<_> = HexMapIterAxial::new(c(0, 0, 0), 1, HexPlanes::YRS).collect();
        assert_eq!(
            yrs,
            vec![
                c(0, -1, -1),
                c(0, 0, -1),
                c(0, 1, -1),
                c(0, -1, 0),
                c(0, 0, 0),
                c(0, 1, 0),
                c(0, -1, 1),
                c(0, 0, 1),
                c(0, 1, 1),
            ]
        );

        // YQS pins r
        let yqs: Vec<_> = HexMapIterAxial::new(c(0, 0, 0), 1, HexPlanes::YQS).collect();
        assert_eq!(
            yqs,
            vec![
                c(-1, 0, -1),
                c(0, 0, -1),
                c(1, 0, -1),
                c(-1, 0, 0),
                c(0, 0, 0),
                c(1, 0, 0),
                c(-1, 0, 1),
                c(0, 0, 1),
                c(1, 0, 1),
            ]
        );

        // pinned planes keep a non-origin center
        let off_center: Vec<_> = HexMapIterAxial::new(c(3, -2, 5), 0, HexPlanes::QRS).collect();
        assert_eq!(off_center, vec![c(3, -2, 5)]);
    }

    #[test]
    fn iter_axial_int16_bounds() {
        // center entirely out of range in q: no cells
        let far: Vec<_> = HexMapIterAxial::new(c(SHRT_MAX + 10, 0, 0), 1, HexPlanes::ALL).collect();
        assert_eq!(far, vec![]);

        // y = SHRT_MAX + 1 clamps to the top valid layer
        let hi_y: Vec<_> =
            HexMapIterAxial::new(c(0, 0, SHRT_MAX + 1), 1, HexPlanes::ALL).collect();
        assert_eq!(
            hi_y,
            vec![
                c(-1, 0, SHRT_MAX),
                c(-1, 1, SHRT_MAX),
                c(0, -1, SHRT_MAX),
                c(0, 0, SHRT_MAX),
                c(0, 1, SHRT_MAX),
                c(1, -1, SHRT_MAX),
                c(1, 0, SHRT_MAX),
            ]
        );

        // q = SHRT_MAX + 1 clamps to the top valid column
        let hi_q: Vec<_> = HexMapIterAxial::new(c(SHRT_MAX + 1, 0, 0), 1, HexPlanes::ALL).collect();
        assert_eq!(
            hi_q,
            vec![
                c(SHRT_MAX, 0, -1),
                c(SHRT_MAX, 1, -1),
                c(SHRT_MAX, 0, 0),
                c(SHRT_MAX, 1, 0),
                c(SHRT_MAX, 0, 1),
                c(SHRT_MAX, 1, 1),
            ]
        );
    }

    // ---- hex_map_iter_radial.cpp -------------------------------------------------------

    #[test]
    fn iter_radial_source_cases() {
        let radius_zero: Vec<_> = HexMapIterRadial::new(c(0, 0, 0), 0, false, HexPlanes::ALL).collect();
        assert_eq!(radius_zero, vec![c(0, 0, 0)]);

        let radius_zero_excluded: Vec<_> =
            HexMapIterRadial::new(c(0, 0, 0), 0, true, HexPlanes::ALL).collect();
        assert_eq!(radius_zero_excluded, vec![]);

        // radius one only reaches the six planar neighbors plus the level above/below
        let radius_one: Vec<_> =
            HexMapIterRadial::new(c(0, 0, 0), 1, false, HexPlanes::ALL).collect();
        assert_eq!(
            radius_one,
            vec![
                c(0, 0, -1),
                c(-1, 0, 0),
                c(-1, 1, 0),
                c(0, -1, 0),
                c(0, 0, 0),
                c(0, 1, 0),
                c(1, -1, 0),
                c(1, 0, 0),
                c(0, 0, 1),
            ]
        );

        let radius_one_excluded: Vec<_> =
            HexMapIterRadial::new(c(0, 0, 0), 1, true, HexPlanes::ALL).collect();
        assert_eq!(radius_one_excluded.len(), 8);
        assert!(!radius_one_excluded.contains(&c(0, 0, 0)));
    }

    #[test]
    fn iter_radial_radius_two_count_and_filter() {
        // 19 hexes at dy = 0, 7 at each dy = ±1, 1 at each dy = ±2
        let cells: Vec<_> = HexMapIterRadial::new(c(0, 0, 0), 2, false, HexPlanes::ALL).collect();
        assert_eq!(cells.len(), 35);
        assert!(cells.iter().all(|cell| c(0, 0, 0).distance(*cell) <= 2));
        assert_eq!(set(&cells).len(), 35);

        // pinned planes make it a flat disk again
        let qrs: Vec<_> = HexMapIterRadial::new(c(0, 0, 0), 2, false, HexPlanes::QRS).collect();
        assert_eq!(qrs.len(), 19);
        assert!(qrs.iter().all(|cell| cell.y == 0));
    }

    #[test]
    fn iter_radial_int16_bounds() {
        // only the level below the out-of-range center is reachable
        let hi_y: Vec<_> =
            HexMapIterRadial::new(c(0, 0, SHRT_MAX + 1), 1, false, HexPlanes::ALL).collect();
        assert_eq!(hi_y, vec![c(0, 0, SHRT_MAX)]);
    }

    #[test]
    fn neighbors_matches_radial_iterator() {
        // include_center = true is the source's `exclude_center = false`
        let via_method: Vec<_> = c(1, -2, 0).neighbors(2, HexPlanes::QRS, true).collect();
        let via_ctor: Vec<_> = HexMapIterRadial::new(c(1, -2, 0), 2, false, HexPlanes::QRS).collect();
        assert_eq!(via_method, via_ctor);

        let with_center: Vec<_> = c(1, -2, 0).neighbors(1, HexPlanes::ALL, true).collect();
        assert_eq!(with_center.len(), 9);
        assert!(with_center.contains(&c(1, -2, 0)));

        let excluded: Vec<_> = c(1, -2, 0).neighbors(1, HexPlanes::ALL, false).collect();
        assert_eq!(excluded.len(), 8);
        assert!(!excluded.contains(&c(1, -2, 0)));
    }

    // ---- hex_map_iter_cube.cpp ---------------------------------------------------------

    #[test]
    fn iter_cube_source_cases() {
        let cases: &[((f32, f32, f32), (f32, f32, f32), &[HexMapCellId])] = &[
            ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0), &[c(0, 0, 0)]),
            ((1.0, 0.0, 0.0), (1.0, 0.0, 0.0), &[c(1, 0, 0)]),
            ((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), &[c(0, 0, 0), c(1, 0, 0)]),
            (
                (-0.1, 0.0, 0.0),
                (0.1, 0.0, 1.5),
                &[c(0, 0, 0), c(-1, 1, 0), c(0, 1, 0)],
            ),
            (
                (-0.1, 0.0, 0.0),
                (-0.05, 0.0, 1.5),
                &[c(0, 0, 0), c(-1, 1, 0)],
            ),
            (
                (0.1, 0.0, 0.0),
                (0.2, 0.0, 1.5),
                &[c(0, 0, 0), c(0, 1, 0)],
            ),
            (
                c(-1, 1, 0).unit_center(),
                c(-1, 2, 0).unit_center(),
                &[c(-1, 1, 0), c(-1, 2, 0)],
            ),
            (
                (0.0, 0.0, 0.0),
                (0.0, 2.0, 0.0),
                &[c(0, 0, 0), c(0, 0, 1), c(0, 0, 2)],
            ),
            (
                (0.0, 0.0, 0.0),
                (0.0, -2.0, 0.0),
                &[c(0, 0, 0), c(0, 0, -1), c(0, 0, -2)],
            ),
        ];

        for (min, max, expected) in cases {
            let cells: Vec<_> = HexMapIterCube::new(*min, *max).collect();
            assert_eq!(
                set(&cells),
                set(expected),
                "iter_cube from {min:?} to {max:?} mismatched"
            );
        }

        // the source sorts the two points, so the reversed arguments give the same set
        let forward: Vec<_> = HexMapIterCube::new((0.0, 0.0, 0.0), (0.0, -2.0, 0.0)).collect();
        let reversed: Vec<_> = HexMapIterCube::new((0.0, -2.0, 0.0), (0.0, 0.0, 0.0)).collect();
        assert_eq!(set(&forward), set(&reversed));
    }

    #[test]
    fn iter_cube_prism_is_a_radius_one_hex_prism() {
        let cells: Vec<_> = HexMapIterCube::new((-1.0, -1.0, -1.0), (1.0, 1.0, 1.0)).collect();
        let expected = set(&[
            c(0, 0, 0),
            c(0, 0, 1),
            c(0, 0, -1),
            c(1, 0, 0),
            c(-1, 0, 0),
            c(0, 1, 0),
            c(-1, 1, 0),
            c(0, -1, 0),
            c(1, -1, 0),
            c(1, 0, 1),
            c(-1, 0, 1),
            c(0, 1, 1),
            c(-1, 1, 1),
            c(0, -1, 1),
            c(1, -1, 1),
            c(1, 0, -1),
            c(-1, 0, -1),
            c(0, 1, -1),
            c(-1, 1, -1),
            c(0, -1, -1),
            c(1, -1, -1),
        ]);
        assert_eq!(cells.len(), expected.len());
        assert_eq!(set(&cells), expected);

        // deterministic odd-r walk order (r = odd-r z fastest, then q, then y)
        assert_eq!(
            cells,
            vec![
                c(0, -1, -1),
                c(1, -1, -1),
                c(-1, 0, -1),
                c(0, 0, -1),
                c(1, 0, -1),
                c(-1, 1, -1),
                c(0, 1, -1),
                c(0, -1, 0),
                c(1, -1, 0),
                c(-1, 0, 0),
                c(0, 0, 0),
                c(1, 0, 0),
                c(-1, 1, 0),
                c(0, 1, 0),
                c(0, -1, 1),
                c(1, -1, 1),
                c(-1, 0, 1),
                c(0, 0, 1),
                c(1, 0, 1),
                c(-1, 1, 1),
                c(0, 1, 1),
            ]
        );
    }

    #[test]
    fn iter_cube_widened_box_selects_corners() {
        let cells: Vec<_> = HexMapIterCube::new(
            (-SQRT3 - 0.1, -1.0, -1.0),
            (SQRT3 + 0.1, 1.0, 1.0),
        )
        .collect();
        let expected = set(&[
            c(0, 0, 0),
            c(0, 0, 1),
            c(0, 0, -1),
            c(1, 0, 0),
            c(-1, 0, 0),
            c(0, 1, 0),
            c(-1, 1, 0),
            c(0, -1, 0),
            c(1, -1, 0),
            c(-1, -1, 0),
            c(1, 1, 0),
            c(-2, 1, 0),
            c(2, -1, 0),
            c(1, 0, 1),
            c(-1, 0, 1),
            c(0, 1, 1),
            c(-1, 1, 1),
            c(0, -1, 1),
            c(1, -1, 1),
            c(-1, -1, 1),
            c(1, 1, 1),
            c(-2, 1, 1),
            c(2, -1, 1),
            c(1, 0, -1),
            c(-1, 0, -1),
            c(0, 1, -1),
            c(-1, 1, -1),
            c(0, -1, -1),
            c(1, -1, -1),
            c(-1, -1, -1),
            c(1, 1, -1),
            c(-2, 1, -1),
            c(2, -1, -1),
        ]);
        assert_eq!(cells.len(), 33);
        assert_eq!(set(&cells), expected);
    }

    #[test]
    fn iter_cube_int16_bounds() {
        let empty: Vec<_> = HexMapIterCube::new(
            c(SHRT_MAX + 1, 0, 0).unit_center(),
            c(SHRT_MAX + 2, 0, 0).unit_center(),
        )
        .collect();
        assert_eq!(empty, vec![]);

        let low: Vec<_> = HexMapIterCube::new(
            c(i16::MIN as i32 - 1, i16::MIN as i32 - 1, i16::MIN as i32).unit_center(),
            c(i16::MIN as i32, i16::MIN as i32, i16::MIN as i32).unit_center(),
        )
        .collect();
        assert_eq!(low, vec![c(i16::MIN as i32, i16::MIN as i32, i16::MIN as i32)]);

        let high: Vec<_> = HexMapIterCube::new(
            c(SHRT_MAX, SHRT_MAX, SHRT_MAX).unit_center(),
            c(SHRT_MAX + 1, SHRT_MAX + 1, SHRT_MAX).unit_center(),
        )
        .collect();
        assert_eq!(high, vec![c(SHRT_MAX, SHRT_MAX, SHRT_MAX)]);
    }

    // ---- wrap-aware variant ------------------------------------------------------------

    #[test]
    fn wrap_cell_matches_wrap_hex_coords_and_preserves_y() {
        // the live world grid: chunk radius 2 (19 hexes/chunk), 6x6 chunks
        const RADIUS: i32 = 2;
        const COLS: i32 = 6;
        const ROWS: i32 = 6;

        for cell in [c(0, 0, 0), c(37, 5, -3), c(-500, 512, 7), c(1234, 5678, 2)] {
            let wrapped = wrap_cell(cell, RADIUS, COLS, ROWS);
            let (q, r) = wrap_hex_coords(cell.q, cell.r, RADIUS, COLS, ROWS);
            assert_eq!(wrapped, HexMapCellId::new(q, r, cell.y));
        }

        // a non-positive grid size is not a torus — pass through unchanged
        assert_eq!(wrap_cell(c(123, -45, 9), RADIUS, 0, ROWS), c(123, -45, 9));
        assert_eq!(wrap_cell(c(123, -45, 9), RADIUS, COLS, -2), c(123, -45, 9));
    }

    #[test]
    fn wrapped_radial_around_seam_hex_is_canonical_and_distinct() {
        const RADIUS: i32 = 2;
        const COLS: i32 = 6;
        const ROWS: i32 = 6;

        // (37, 5) is the +q edge hex of canonical chunk (5, 5): its wrapped radius-one disk
        // straddles the seam, so the canonicalization is what makes the query useful.
        let center = c(37, 5, 0);
        let cells: Vec<_> = HexMapIterRadial::new(center, 1, false, HexPlanes::QRS)
            .wrapped(RADIUS, COLS, ROWS)
            .collect();

        assert_eq!(cells.len(), 7);
        assert_eq!(set(&cells).len(), 7);
        for cell in &cells {
            assert_eq!(
                wrap_cell(*cell, RADIUS, COLS, ROWS),
                *cell,
                "wrapped region yielded a non-canonical cell"
            );
        }

        // the same cells as the unwrapped disk mapped through the wrap
        let unwrapped: Vec<_> = HexMapIterRadial::new(center, 1, false, HexPlanes::QRS).collect();
        let expected: Vec<_> = unwrapped
            .iter()
            .map(|cell| wrap_cell(*cell, RADIUS, COLS, ROWS))
            .collect();
        assert_eq!(set(&cells), set(&expected));
    }

    #[test]
    fn wrapped_iterator_dedupes_self_overlap_and_keeps_first_seen_order() {
        const RADIUS: i32 = 2;

        // a one-chunk torus: a radius-one disk fits inside the lap, but a radius-three disk
        // wraps back onto itself, so duplicates must be dropped
        let cells: Vec<_> = HexMapIterRadial::new(c(0, 0, 0), 1, false, HexPlanes::QRS)
            .wrapped(RADIUS, 1, 1)
            .collect();
        assert_eq!(cells.len(), 7);
        assert_eq!(set(&cells).len(), 7);

        let big: Vec<_> = HexMapIterRadial::new(c(0, 0, 0), 3, false, HexPlanes::QRS)
            .wrapped(RADIUS, 1, 1)
            .collect();
        assert!(big.len() < 37, "self-overlap was not deduped");
        assert_eq!(set(&big).len(), big.len());
        assert!(big.iter().all(|cell| cell.y == 0));

        // first-seen order is the unwrapped walk order with wrapped coordinates
        let unwrapped: Vec<_> = HexMapIterRadial::new(c(0, 0, 0), 3, false, HexPlanes::QRS).collect();
        let mut expected: Vec<HexMapCellId> = Vec::new();
        for cell in unwrapped {
            let wrapped = wrap_cell(cell, RADIUS, 1, 1);
            if !expected.contains(&wrapped) {
                expected.push(wrapped);
            }
        }
        assert_eq!(big, expected);
    }

    #[test]
    fn wrapped_axial_and_cube_iterators_are_wrap_aware() {
        const RADIUS: i32 = 2;
        const COLS: i32 = 6;
        const ROWS: i32 = 6;

        let axial: Vec<_> = HexMapIterAxial::new(c(37, 5, 0), 1, HexPlanes::YQR)
            .wrapped(RADIUS, COLS, ROWS)
            .collect();
        assert!(axial.iter().all(|cell| wrap_cell(*cell, RADIUS, COLS, ROWS) == *cell));

        let cube: Vec<_> = HexMapIterCube::new((-1.0, -1.0, -1.0), (1.0, 1.0, 1.0))
            .wrapped(RADIUS, COLS, ROWS)
            .collect();
        assert_eq!(cube.len(), 21);
        assert!(cube.iter().all(|cell| wrap_cell(*cell, RADIUS, COLS, ROWS) == *cell));
    }
}
