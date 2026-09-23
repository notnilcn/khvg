// Portions derived from hex_map_godot (135dc75) - MIT. See THIRD_PARTY_NOTICES.md.
#nullable enable
using Godot;

public static class TorusMath
{
	// Mirror of the server's wrap_world_pos (server/spacetimedb/src/world/wrap.rs): map a
	// continuous-space position back into the canonical lap by shifting it by whole lap vectors.
	// Node positions drift up to a lap, so wrap before any hex/elevation lookup. `lapQ`/`lapR`
	// are the world-space periods from MapConfig (TableSubscriber exposes them); a non-positive
	// grid size is not a torus — the position passes through unchanged.
	public static Vector2 WrapPosition(Vector2 pos, int chunkHexRadius, int chunkCols, int chunkRows, float outerRadius, Vector2 lapQ, Vector2 lapR)
	{
		if (chunkCols <= 0 || chunkRows <= 0) return pos;
		var hex = HexMath.WorldToHex(pos.X, pos.Y, outerRadius);
		var chunk = HexMath.ToLowerRes(hex.X, hex.Y, chunkHexRadius);
		var wrapped = new Vector2I(
			((chunk.X % chunkCols) + chunkCols) % chunkCols,
			((chunk.Y % chunkRows) + chunkRows) % chunkRows);
		if (chunk == wrapped) return pos;
		int lapsQ = (wrapped.X - chunk.X) / chunkCols;
		int lapsR = (wrapped.Y - chunk.Y) / chunkRows;
		return pos + lapsQ * lapQ + lapsR * lapR;
	}

	// A torus has no single "true" position for anything — [0, lap) is just one arbitrary
	// representative of infinitely many equivalent copies (canonical ± any combination of lap
	// vectors). This picks whichever copy of `canonical` is actually closest to `reference`
	// (the camera, or another entity), so rendering near a chunk-grid seam looks continuous
	// without ever touching the canonical/stored position itself.
	public static Vector2 NearestCandidate(Vector2 canonical, Vector2 reference, Vector2 lapQ, Vector2 lapR)
	{
		var best = canonical;
		var bestDist = canonical.DistanceSquaredTo(reference);

		Vector2[] shifts =
	{
			lapQ, -lapQ, lapR, -lapR,
			lapQ + lapR, lapQ - lapR, -lapQ + lapR, -lapQ - lapR,
		};
		foreach (var shift in shifts)
		{
			var candidate = canonical + shift;
			var dist = candidate.DistanceSquaredTo(reference);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = candidate;
			}
		}
		return best;
	}
}
