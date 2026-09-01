Short answer: no rewrite. A per-hex elevation index is exactly the right scope for what you described, and your architecture actually makes the *data* side trivial. The real cost is elsewhere — I'll break it down.

## What your codebase looks like today (relevant facts)

- Hexes are implicit — there's no `Hex` table. Terrain lives as `TriangleTile` rows (6 wedges per hex) in `server/spacetimedb/src/world/instance_tables.rs:16`.
- Generation already has seeded `value_noise2d` channels in `world/terrain/mod.rs` — an elevation pass slots right in.
- Server doesn't simulate bullets at all. It emits `BulletPatternEvent`s; the client renders them and *detects* hits itself, then self-reports via `report_hit`. The server just sanity-checks range.
- Movement is free 2D with zero terrain validation — server never looks at `TriangleTile` for movement.
- Client terrain is a custom MultiMesh pipeline (not TileMapLayer), and the camera is locked top-down (`DefaultPitch = -90`).

## Recommended shape

**1. Data (trivial).** Add `elevation: u8` to `TriangleTile`, duplicated across the 6 wedges. Don't make a separate per-hex table — that means a new view, binder, and sync path for zero benefit at your scope. Generate it in `world/terrain/mod.rs` as another noise channel, **quantized to 2–4 discrete levels** (this is what gives you plateaus with cliff boundaries, rather than gentle slopes you can't render anyway).

**2. Bullet rule (small, and it fits your trust model perfectly).** Since hits are client-detected and server-adjudicated-by-range-check, cliff blocking can be 100% client-side without opening any new cheat surface beyond what already exists:
- When spawning a bullet from a `BulletPatternEvent`, compute its home elevation from `origin_x/y` → hex → elevation (build a `Dictionary<hex, u8>` from mirrored `TriangleTile` rows).
- Each frame/tick, check the hex under the bullet; if `|hexElev - bulletElev| > 1`, despawn it. A blocked bullet simply never reports a hit — the server stays oblivious and stays correct.

**3. Rendering (this is 80% of the work).** Here's the honest catch: your camera is pure top-down, so \"elevation\" can only be *faked in screen space*. The standard tricks, cheapest first:
- **Shading only** — tint wedges darker/lighter per level and draw a cliff-edge texture along boundaries between different levels. No geometry changes; reads surprisingly well.
- **Y-offset + skirts** — shift each elevated hex's MultiMesh transforms up by `level × N px`, and draw vertical \"cliff wall\" quads on the south-facing boundary edges so you don't see gaps. This is the classic 2.5D look, but it touches `TileComponent`/`TerrainLayerComponent`, decor anchoring, and anything else positioned on terrain (players, drops, chests, bullets all need the same offset on elevated hexes, or they'll look sunken).

Start with shading-only. If it looks too flat later, upgrade to offsets — the data model doesn't change.

## What I'd deliberately NOT do

- **Movement blocking.** You said no gameplay implications — good, keep it that way. Players walking \"up\" cliffs looks mildly odd but nobody cares in practice. If you later want it, note the server has no per-hex index on `TriangleTile` (only a btree on `chunk_index`), so server-authoritative elevation movement rules would need a new index. That's a future problem.
- **The 3D mirror.** You have a parallel `World3DComponent`/`PhantomCamera3D` presenter with bullets hovering at fixed `BulletHeight = 20`. If you actively use that view, it needs the matching height offsets or it'll disagree with the 2D view. If it's experimental, ignore it for now.

So: elevation index per hex, quantized noise, client-side bullet check, shading-first visuals. That's a few days of work, not a rewrite — the only system that genuinely fights you is the custom MultiMesh renderer, and only if you go beyond shading.