There's a bug where the enemies don't chase the closest player when there are 3 people

there's also probably a bug where players cant see each other when they spawn in. It looks like it happens when a player has been in the game for a long time or something. maybe Whehn they see a player join and rejoin multiple times?


The following code in StdbPlayerSync.cs needs to be better. When a player stops moving it slowly lerps to the position where the player stopped moving.
```
public override void _Process(double delta)
{
	long nowMicros = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

	foreach (var (key, node) in _remoteNodes)
	{
		if (!GodotObject.IsInstanceValid(node)) continue;
		if (!_snapshots.TryGetValue(key, out var snap)) continue;

		// Clamp elapsed to 2s so a stale snapshot doesn't extrapolate off to infinity.
		float elapsed = System.Math.Clamp(
			(nowMicros - snap.EventTimeMicros) / 1_000_000f, 0f, 2f);

		var extrapolated = snap.Pos + new Vector2(snap.MoveX, snap.MoveY) * snap.Speed * elapsed;
		node.GlobalPosition = node.GlobalPosition.Lerp(extrapolated, (float)delta);
	}
}
```
