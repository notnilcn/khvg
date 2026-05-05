/*
```c# */
#nullable enable
using System;
using Godot;

internal readonly record struct SyncSnapshot(
    Vector2 Pos, float MoveX, float MoveY, float Speed, long EventTimeMicros);

internal static class Simulator
{
    internal static Vector2 Extrapolate(in SyncSnapshot snap, long nowMicros, float maxElapsed = 2f) // ^Extrapolate
    {
        float elapsed = Math.Clamp((nowMicros - snap.EventTimeMicros) / 1_000_000f, 0f, maxElapsed);
        return snap.Pos + new Vector2(snap.MoveX, snap.MoveY) * snap.Speed * elapsed;
    }
}
/*
```
*/
