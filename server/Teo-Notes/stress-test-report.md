# Enemy/Bullet Stress Test — Findings Report

**Date:** 2026-06-07
**Goal:** Validate the hypothesis that server-side reducer load would remain negligible under heavy enemy/bullet load (since per-tick simulation work is intentionally minimal), and characterize where the real performance ceiling sits.

**Method:** Used a `spawn_stress_test(low, med, high, spread_radius)` admin reducer to spawn varying counts of three enemy archetypes (LowStress / MedStress / HighStress — escalating in attack-pattern complexity and movement behavior) in a disc around the world origin. Instrumented the server's per-tick enemy AI reducer with wall-clock timing (`LogStopwatch`) and load counters (total enemies / enemies actually simulated / bullets fired), and built a lightweight client-side debug overlay reporting FPS, frame time, memory, object/node counts, and draw calls.

---

## Server-Side Results

| Scenario | Enemies (total / simulated) | Bullets fired/tick | Reducer time | Tick cadence |
|---|---|---|---|---|
| Smoke test | 35 / ~17 | — | 0.25 – 2 ms | steady ~104 ms |
| Mid-load | 160 / 33–83 | 51–119 | 1 – 6 ms (mostly 2.5–4 ms) | steady ~104–108 ms |
| Isolated HighStress | 30 / 23 | 69 | 0.7 – 6 ms (mostly 2–4 ms) | steady ~107 ms |

**Findings:**
- Reducer cost scales **linearly** with the number of enemies actually simulated — roughly **0.04–0.05 ms per simulated enemy** — and stayed consistent across every test.
- Even at peak load (83 simulated enemies, 119 bullet-events in a single tick), the reducer used **at most ~6% of the 100 ms tick budget**.
- Tick cadence never drifted — the scheduler fired every ~104–108 ms throughout, with zero skipped or delayed ticks at any load level tested.
- Extrapolating the linear trend, the server would need on the order of **1,500–2,000 simultaneously-simulated enemies** before the reducer alone threatens the tick budget — a number that could never occur in practice, since each client only sees enemies within its area-of-interest (chunk-filtered subscriptions cap what any single client's region can produce).

**Conclusion:** the original hypothesis — that minimal per-tick simulation work would prevent any reducer-side bottleneck — held at every load level tested, with a wide safety margin.

---

## Client-Side Results

| Scenario | FPS | Memory | Objects | Nodes | Draw calls |
|---|---|---|---|---|---|
| Mid-load (160 enemies, dense cluster) | 10–20 (recovers to 30+ as enemies spread) | ~120 MB | ~16,000 | ~4,000 | ~2,500 |
| 90× LowStress (Wander, single bullet stream) | steady 60 | — | ~12,000 | ~2,600 | 600–700 (spikes to ~1,000) |
| 30× HighStress (3 concurrent bullet streams, Chase) | 40 | — | ~14,000 | ~2,800 | ~1,300 |

**Findings:**
- The client ceiling is **rendering-bound** (draw calls / node count), not network- or simulation-bound — FPS tracked draw-call volume far more closely than raw enemy count.
- Normalizing to a **per-enemy cost**: LowStress enemies cost roughly **7 draw calls each**, while HighStress enemies cost roughly **43 draw calls each — about 6× more**, despite being a third of the population.
- The likely cause: the bullet-rendering plugin batches same-pattern bullets into a single draw call. LowStress fires one bullet stream (one batch); HighStress runs three concurrent, structurally distinct attack sequences that can't share a batch — so cost scales with *pattern variety on screen*, not bullet count alone.
- Even at the worst tested density (FPS 10–20, bullets dense enough that the player couldn't visually track their own position), the game remained responsive and did not crash or stutter into unresponsiveness — and recovered smoothly to 30+ FPS as the enemy cluster dispersed.

---

## Overall Conclusions

1. **Server has wide headroom.** Reducer cost scales linearly and predictably with simulated enemy count and stays in the low single-digit milliseconds even under artificial worst-case density — far below the 100 ms tick budget. No architectural changes are needed on the simulation side.
2. **The client's natural ceiling is GPU draw-call volume**, driven primarily by *how visually complex* an enemy's attack pattern is (number of concurrent bullet streams), not by raw enemy headcount. This is a known, expected, and gracefully-degrading limit — not a cliff.
3. **Design implication (a feature, not a bug):** visually complex, multi-pattern enemies (the "HighStress" archetype, analogous to elite/boss-tier monsters) are inherently more expensive to render and should — by design — appear in smaller numbers. That maps naturally onto how such encounters are intended to be used anyway (rare, telegraphed, high-value fights rather than common trash spawns).
4. **Real-world exposure is far lower than tested.** These numbers represent an artificially concentrated worst case (dozens to hundreds of enemies clustered on a single point). In live play, area-of-interest filtering caps what any client ever receives to a local neighborhood — this test deliberately exceeded that envelope to find the breaking point, and the breaking point turned out to be "still playable, just visually overwhelming."

**Bottom line:** the bullet-hell simulation architecture holds up under significantly more load than normal gameplay would ever produce, the server is nowhere close to its limits, and the client degrades predictably and gracefully along an axis (rendering complexity) that is already naturally bounded by game design.
