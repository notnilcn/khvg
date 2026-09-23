// Portions derived from hex_map_godot (135dc75) - MIT. See THIRD_PARTY_NOTICES.md.
#nullable enable
using Godot;
using System;

// Pointy-top hex math shared by the terrain renderer and both hex grid overlays
// (mirrors server methods.rs). The outer radius is per-consumer (terrain uses 96,
// the overlays 32 until MapConfig arrives), so it is a parameter everywhere.
public static class HexMath
{
	private const float Sqrt3 = 1.7320508075688772f;

	public static Vector2 HexToWorld(int q, int r, float outerRadius)
	{
		float R = outerRadius;
		return new Vector2(R * (Sqrt3 * q + Sqrt3 * 0.5f * r), R * 1.5f * r);
	}

	// Server (x,y) ↔ Godot (x, 0, z).
	public static Vector3 HexToWorld3D(int q, int r, float outerRadius)
	{
		float R = outerRadius;
		return new Vector3(
			R * (Sqrt3 * q + Sqrt3 * 0.5f * r),
			0f,
			R * 1.5f * r);
	}

	public static Vector2I WorldToHex(float wx, float wy, float outerRadius)
	{
		float R = outerRadius;
		float q = (wx * Sqrt3 / 3f - wy / 3f) / R;
		float r = wy * 2f / 3f / R;
		float s = -q - r;
		float rq = MathF.Round(q);
		float rr = MathF.Round(r);
		float rs = MathF.Round(s);
		if (MathF.Abs(rq - q) > MathF.Abs(rr - r) && MathF.Abs(rq - q) > MathF.Abs(rs - s))
			rq = -rr - rs;
		else if (MathF.Abs(rr - r) > MathF.Abs(rs - s))
			rr = -rq - rs;
		return new Vector2I((int)rq, (int)rr);
	}

	// Port of hexx Hex::to_lower_res. Maps fine hex (q,r) → chunk (cq,cr).
	public static Vector2I ToLowerRes(int q, int r, int radius)
	{
		int s = -q - r;
		float area = 3f * radius * (radius + 1) + 1f;
		int shift = 3 * radius + 2;
		int a = Mathf.FloorToInt((r + shift * q) / area);
		int b = Mathf.FloorToInt((s + shift * r) / area);
		int c = Mathf.FloorToInt((q + shift * s) / area);
		return new Vector2I(
			Mathf.FloorToInt((1f + a - b) / 3f),
			Mathf.FloorToInt((1f + b - c) / 3f));
	}

	// Center hex of a chunk in global axial coords. Lockstep sibling of server
	// world/hex.rs::chunk_center_hex (hexx to_higher_res) and MSTHexMath.chunk_center.
	public static Vector2I ChunkCenterHex(int cq, int cr, int radius)
	{
		int s = -cq - cr;
		return new Vector2I(cq * (radius + 1) - radius * s, cr * (radius + 1) - radius * cq);
	}

	// Local axial offset of a hex within its owning chunk. Port of hexx Hex::to_local
	// (MSTHexMath.hex_to_local).
	public static Vector2I HexToLocal(int q, int r, int radius)
	{
		var chunk = ToLowerRes(q, r, radius);
		var center = ChunkCenterHex(chunk.X, chunk.Y, radius);
		return new Vector2I(q - center.X, r - center.Y);
	}

	// Map an arbitrary fine hex back into the canonical lap: the hex's chunk coords wrap, and
	// its local offset inside the chunk is preserved, so the wrapped hex is the same point on
	// the torus (positive modulo, like the server's rem_euclid). Lockstep siblings: server
	// wrap_hex_coords (server/spacetimedb/src/world/wrap.rs) and MSTHexMath.wrap_hex
	// (client/addons/MarchingSquaresTerrain/algorithm/terrain/mst_hex_math.gd) — all copies
	// must change together.
	public static Vector2I WrapHex(int q, int r, int radius, int cols, int rows)
	{
		if (cols <= 0 || rows <= 0) return new Vector2I(q, r);
		var chunk = ToLowerRes(q, r, radius);
		var wrapped = new Vector2I(
			((chunk.X % cols) + cols) % cols,
			((chunk.Y % rows) + rows) % rows);
		if (chunk == wrapped) return new Vector2I(q, r);
		var center = ChunkCenterHex(chunk.X, chunk.Y, radius);
		var wrappedCenter = ChunkCenterHex(wrapped.X, wrapped.Y, radius);
		return new Vector2I(wrappedCenter.X + (q - center.X), wrappedCenter.Y + (r - center.Y));
	}

	// Bijection Z² → ℕ using hex spiral order. Lockstep sibling of the server's
	// spiral_chunk_index in server/spacetimedb/src/world/hex.rs — analytical arm detection,
	// no ring walk; both copies must change together.
	public static long SpiralIndex(int cq, int cr)
	{
		int x = cq, z = cr, y = -cq - cr;
		int rho = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
		if (rho == 0) return 0;
		long b = 3L * rho * (rho - 1) + 1;
		long arm, step;
		if (z == -rho && x > 0) { arm = 0; step = y; }
		else if (y == rho && x > -rho) { arm = 1; step = -x; }
		else if (x == -rho && z < rho) { arm = 2; step = z; }
		else if (z == rho && x < 0) { arm = 3; step = x + rho; }
		else if (y == -rho && x >= 0 && x < rho) { arm = 4; step = x; }
		else { arm = 5; step = y + rho; }
		return b + arm * rho + step;
	}
}
