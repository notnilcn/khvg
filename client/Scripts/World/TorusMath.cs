#nullable enable
using Godot;

public static class TorusMath
{
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
