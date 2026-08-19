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

	// Bijection Z² → ℕ using hex spiral order. Direct port of the server's
	// spiral_chunk_index in methods.rs — analytical arm detection, no ring walk.
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
