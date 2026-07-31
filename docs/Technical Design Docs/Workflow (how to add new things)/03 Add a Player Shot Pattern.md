# 03 Add a Player Shot Pattern (new `WeaponPattern` variant)

The three existing patterns (`Single`, `Triple`, `Cluster`) cover weapons data-only ([[02 Add a Weapon (existing shot pattern)|02]]). A *fourth* firing behavior — say a spiraling shot, a boomerang, a charged beam — is a code change in exactly two places: the server enum (schema) and one client switch (behavior). The server never simulates player bullets; it only stores the enum as opaque payload on the `Item` row, so the entire behavior lives client-side in `CombatComponent`.

## 1. Add the variant (server, schema only)

In `item/tables.rs` ([[item/tables.rs##pub enum WeaponPattern|WeaponPattern]]):

```rust
pub enum WeaponPattern {
    Single,
    Triple,
    Cluster,
    Spiral,   // new
}
```

If the pattern needs per-weapon parameters beyond what `WeaponBehavior` already carries (`shot_count`, `spread_angle`, `projectile_speed`, `range`), prefer reusing those fields first. Only add a new field to `WeaponBehavior` ([[item/tables.rs##pub struct WeaponBehavior|WeaponBehavior]]) if the pattern genuinely needs a new knob — every added field must then be supplied by **every** seeded weapon and every `upsert_item` CLI call from then on.

## 2. Regenerate bindings and publish

```bash
cd server && bash build.sh
```

This produces `client/Scripts/module_bindings/Types/WeaponPattern.g.cs` with the new variant as a C# tagged union (`WeaponPattern.Spiral`), alongside reseeding the database.

## 3. Implement the firing behavior (client)

In `CombatComponent.cs`, add a case to the dispatch switch ([[CombatComponent.cs##private void Fire|Fire]]):

```csharp
switch (_weapon.Pattern)
{
    case WeaponPattern.Single:   FireSingle(origin, aimDir);   break;
    case WeaponPattern.Triple:   FireTriple(origin, aimDir);   break;
    case WeaponPattern.Cluster:  FireCluster(origin, aimDir);  break;
    case WeaponPattern.Spiral:   FireSpiral(origin, aimDir);   break;
}
```

Then write `FireSpiral` following the existing methods ([[CombatComponent.cs##private void FireSingle|FireSingle]] etc.). The contract every `Fire*` method fulfills is **two things per shot** ([[07 Combat & Damage Math|07]] explains why):

1. **Cosmetic bullets** — `BulletManager.Instance.SpawnPlayerBullet(s)` / `SpawnPlayerBullets(origin, angles, lifetime, speed, textureId)`. These never collide; lifetime is conventionally `range / projectile_speed` so the visual dies at max range.
2. **Hit detection** — `SpawnZonesAlong(origin, angle, range, speed)` spawns the invisible `HitZone` train that actually reports hits. `SpawnBulletWithZones(origin, angle, range, speed)` does both for one projectile (see `FireCluster`).

Skip the zones and your pattern is a light show that deals no damage; skip the bullets and it deals invisible damage.

## 4. Use it on a weapon

Seed or upsert an `Item` with `pattern: WeaponPattern::Spiral` — exactly like [[02 Add a Weapon (existing shot pattern)|02]], step 2. A weapon only *references* a pattern; the same variant can back many weapons with different numbers.

## 5. Verify

- `dotnet build client/khvg.csproj` (or Godot build) — catches any binding mismatch.
- In game: equip a Spiral weapon in slot 0, hold left mouse. Check both that bullets render along the intended trajectories *and* that enemies actually lose HP (the zone half).
- Server-side nothing changes behaviorally: `report_enemy_hit` still resolves damage from the equipped weapon's `damage` field, regardless of pattern.

## Gotchas

- **`_weapon.Pattern` is read once per `Fire()`** — patterns that evolve over time (spirals, charges) need their per-shot state in `CombatComponent` fields, computed inside `Fire` from `_fireTimer`/counters; `WeaponBehavior` itself is static data.
- Don't mutate the generated `module_bindings/Types/WeaponPattern.g.cs`; it's overwritten on every `spacetime generate`.
- Aiming (`_nearestEnemy`, `GetAimDir`) happens before the switch, so your `Fire*` gets the aim-assisted direction for free — you don't need to re-derive it.
