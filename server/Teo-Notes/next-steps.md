# Next Steps

## 1. Non-local player textures

`NearbyRemotePlayers` is a view of `PlayerPosition` and carries no `texture_id`. Remote player nodes spawn with no sprite.

**Fix:** New server-side view that joins `PlayerPosition` + `PlayerProfile` and projects position fields + `texture_id` together. Subscribe to it in the game sub. Wire up in `NonLocalPlayer._Ready()` the same way `LocalPlayer` and `Enemy` already do — look up `texture_id`, resolve via `_textureCache`, load `SpriteFrames`.

---

## 2. Add client texture folders

Organize `client/Assets/` into subfolders matching `TextureKind` (Players, Enemies, Bullets, Items, Environment). Keeps asset paths consistent with how the server categorizes textures.

---

## 3. Server file structure — reintroduce `main` module

Consolidate the module entry point. Currently `init`, `client_connected`, `client_disconnected`, and seed calls are split between `admin/admin.rs` and `player/reducers.rs`.

Introduce a `main` module (or `lib` entry file) that owns:
- `init` reducer (schedules, seeds)
- `client_connected` / `client_disconnected`
- All seed function calls

Each domain module (`player`, `enemy`, `world`, `admin`) keeps its own tables, reducers, and methods. `main` just wires the lifecycle.

---

## 4. Research Path2D attack defs

Investigate pros/cons of defining bullet patterns via a parameterized Path2D node vs. the current typed enum (`PatternType` — Straight, Ring, Sine, Burst, Spiral, Homing). Write up findings to share with notnilc for a decision on bullet pattern architecture going forward.

**Questions to answer:**
- Can Path2D express all current pattern types without loss of precision?
- How does the server represent and store a Path2D definition (serialized curve points)?
- Does parameterized Path2D reduce the number of variants needed, or just shift complexity to the curve data?
- What's the authoring workflow difference for designers?

---

## 5. Code cleanup

Client and server tidy pass:
- Remove dead code left over from the refactor
- Review client script responsibilities (GameManager is still fairly large)
- Consolidate small inconsistencies in naming and structure

---

## Backlog (deferred)

- **Bullet collision + player damage** — wire BlastBullets2D collision callback, call `ReportHit` reducer, batch damage reports
- **AoI re-subscription on chunk crossing** — game sub is static at join time
- **Enemy behavior depth** — phase transitions, spawn zones, loot drop content
- **Torus world** — chunk wrapping for position and subscription logic
