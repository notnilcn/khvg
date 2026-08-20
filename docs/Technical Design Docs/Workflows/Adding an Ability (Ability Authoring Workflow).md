# Adding an Ability (Ability Authoring Workflow)

A step-by-step tutorial for adding an equippable ability to the game — from "I have an
idea" to "it works on every client in-game". It uses the **Slash Orb** (a black slash
rectangle in front of the player that erases enemy bullets) as the running example, since
that's the ability this document was written from.

**Audience:** contributors adding items/abilities. No prior knowledge of the codebase
assumed. Simple abilities are *data-shaped* Rust (a seed entry); new *effect types*
additionally touch one enum, one reducer arm, and two small client methods.

---

## The 30-second mental model

Abilities are **items with an `Ability` behavior**, sitting in ability slots (indices
32–37). The full pipeline for any feature here is five steps:

1. **Table/type** — the data shape exists (`AbilityEffect` variant),
2. **Reducer** — the server validates and applies it (`activate_ability`),
3. **Subscription** — clients receive the result (the `BulletControlEvent` event table is
   already subscribed; you rarely touch this step),
4. **Client calls the reducer** — `TryActivateAbility` from the ability hotkeys
   (`Ability1`–`Ability6`) or clicking the slot,
5. **Client renders** — optimistic local apply for the caster + event echo for everyone else.

**Forgetting step 4 is the recurring mistake.** If the server does everything right but
nobody calls the reducer, nothing happens.

Networking for bullet-control abilities (delete/split/attract/slash) is **cast relay**:
the server tracks no live bullets. It validates the cast (cooldown, charges, range clamp)
and appends one `BulletControlEvent` row. Every client — including the caster, who already
applied the effect optimistically and skips its own echo via `cast_by` — resolves the
proximity query against its own local bullets and draws the visual. Edge-of-radius results
can differ slightly between clients; that's accepted by design.

Two kinds of ability authoring:

- **Reusing an existing effect** (`Heal`, `Buff`, `DeleteBullets`, `SplitBullets`,
  `AttractBullets`, `SlashBullets`): you only write a **seed entry** and a texture entry.
  Skip to Part 2.
- **A new effect type**: the full pipeline — Part 3.

---

## Part 1 — The authoring vocabulary

### The item

Everything lives in `server/spacetimedb/src/item/seeds.rs::seed_world_items`:

```rust
Item::seed(ctx, Item {
    item_id: "Slash Orb".to_string(),        // unique key, also the spawn/give key
    display_name: "Slash Orb".to_string(),
    description: "A void-dark orb. Activate to slash the space in front of you, \
                  erasing any enemy bullets caught in the arc.".to_string(),
    texture_id: "Orb".to_string(),           // must exist as a TextureEntry (see below)
    equip_slot: EquipSlot::Ability,
    slot_cost: 1,                            // contiguous ability cells occupied
    stat_modifiers: vec![],                  // passive stats while equipped (scaled by position)
    behaviors: vec![ItemBehavior::Ability(AbilityBehavior {
        effect: AbilityEffect::SlashBullets(SlashParams { length: 100.0, width: 60.0 }),
        potency: 1.0,                        // heal amount / buff magnitude (unused for bullet control)
        duration: 0.0,                       // buff seconds (0 = instant)
        cooldown_seconds: 1.5,
        max_charges: 0,                      // 0 = unlimited
    })],
    max_enchantments: 0,
    innate_enchantment_ids: vec![],
    stat_requirements: NO_REQUIREMENTS,      // all-zero = no gate
});
```

Rules of the shape:

- **`slot_cost` spans cells.** A cost-3 item's head row holds the item; the next two cells
  are followers marked `occupied_by`. Activating a follower activates its head. Ability
  items stored loose in the backpack sit in one cell — the cost only applies in the
  ability region.
- **Position matters.** Ability heads get their effect scaled by
  `ABILITY_POSITION_MULTIPLIERS` = `[1.5, 1.25, 1.1, 1.0, 0.9, 0.8]` — the first ability
  slot is strongest. The server applies this in `scale_ability_effect`; the client mirrors
  it in `LocalPlayerInventoryComponent.AbilityPositionMultipliers`. If you add a scalable
  parameter to an effect, scale it in *both* places (the client mirror is the same
  multiplication inside `TryActivateAbility`).
- **Items are not consumed on use.** Cooldown and charges live on the slot row
  (`cooldown_until`, `charges`), not the item.
- **Slot and behavior are independent axes.** `equip_slot` only says where an item goes.
  An "ability" is any item with an `Ability` behavior — capability checks are behavior
  lookups (`ability_behavior(&item)`), never `equip_slot` comparisons.

### The texture

Items render through the `TextureEntry` catalog, seeded in
`server/spacetimedb/src/main/seeds.rs::seed_default_textures`:

```rust
TextureEntry::seed(ctx, texture("Orb", "res://Textures/Items/Artifacts/Orb.tres", TextureKind::Item));
```

The `.tres` must exist under `client/Textures/`. Forget this entry and the slot icon is
blank with no error.

### Hotkeys

`Ability1`–`Ability6` (input map, `project.godot`) map to ability cells 32–37 in order —
`Ability2` activates cell 33. `InventoryComponent.AbilityActions` owns the mapping; you
never wire keys per ability.

---

## Part 2 — Reusing an existing effect (seed-only)

1. Pick an effect and numbers: `Heal`, `Buff(ConsumableBuffEffect::Strength(v))`,
   `DeleteBullets(radius)`, `SplitBullets(radius)`, `AttractBullets(radius)`,
   `SlashBullets(SlashParams { length, width })`.
2. Add the `Item::seed` entry in `item/seeds.rs` (shape above).
3. Add the `TextureEntry` in `main/seeds.rs` if the texture id is new.
4. Build and publish:

```bash
cargo check --manifest-path server/spacetimedb/Cargo.toml   # compile gate
server/build.sh                                             # regen bindings + publish (wipes DB, reseeds)
dotnet build client/khvg.csproj                             # client compile gate
```

5. In-game: get the item (admin `give_item`, or pre-equip in
   `player/methods.rs::try_scaffold_profile` via `place_span` like the Skull/Tome
   entries), drag it into an ability slot, press the matching `AbilityN` key.

Done. Cooldown, charges, position scaling, the reducer call, the event relay, and remote
rendering already exist.

---

## Part 3 — Adding a new effect type (the Slash Orb case study)

A new effect type touches **one enum, one reducer, and two client spots**, in addition to
Part 2's seeds. The slash needed a *shape* the existing code didn't have: everything else
was radial (`DeleteBullets(radius)` deletes within a circle); the slash is a **rectangle**
from the player toward the cursor.

### Step 1: Extend `AbilityEffect` — `item/tables.rs`

```rust
#[derive(SpacetimeType, Clone, Copy, PartialEq)]
pub struct SlashParams { pub length: f32, pub width: f32 }

pub enum AbilityEffect {
    // ... existing variants ...
    SlashBullets(SlashParams),
}
```

**Gotcha that actually happened:** SpacetimeType enum variants must be *unit* or
*single-field* (newtype). `SlashBullets(f32, f32)` fails to compile ("must be a unit
variant or a newtype variant") with a cascade of confusing `Serialize`/`Deserialize`
errors pointing at unrelated lines. Two or more payload values ⇒ wrap them in a struct.

### Step 2: Relay the cast — `BulletControlKind` + `activate_ability`

Bullet-control effects are rebroadcast through `BulletControlEvent`
(`enemy/instance_tables.rs`), an append-only event table with fixed fields:
`kind, x, y, radius, target_x, target_y, cast_by`. Adding `Slash` to `BulletControlKind`
is free; the rectangle rides the **existing fields** rather than new columns:

- `x, y` — cast origin (the server's `PlayerPosition` row, not client-supplied),
- `target_x, target_y` — far end of the slash (`origin + dir * length`),
- `radius` — rectangle width.

Reusing fields beats schema sprawl, but **document the encoding in the table's doc
comment** — the next effect author reads that comment first.

In `player/reducers.rs`:

- `scale_ability_effect` — multiply every scalable dim by the position multiplier
  (`length * m`, `width * m`).
- `activate_ability` — new match arm next to the existing bullet-control arm. It derives
  the direction from the (range-clamped) cursor target and inserts the event. One edge
  case matters: cursor exactly on the player gives a zero-length direction. Pick a fixed
  fallback (`(1, 0)`) and **mirror it on the client**, or the optimistic visual and the
  echo disagree.

### Step 3: Client effect + visual — `BulletControllerComponent.cs`

`client/Scripts/Components/Bullets/BulletControllerComponent.cs` owns the live-bullet
queries (`DeleteNear`/`SplitNear`/`AttractNear`) over
`BulletSpawnerComponent.LiveEnemyBullets`. The plugin can't enumerate bullets — each
instance is queried through untyped `Call`s (`get_amount_bullets`,
`all_bullets_get_transforms`, `is_bullet_status_enabled`) — so new queries copy that
iteration shape exactly:

```csharp
// Point-in-rotated-rect: translate into rect-local space, then
// along-axis in [0, length], cross-axis in [-width/2, width/2].
private IEnumerable<(GodotObject Instance, int Index)> FindBulletsInRect(Vector2 origin, Vector2 end, float width) { ... }

public void Slash(Vector2 origin, Vector2 end, float width)
{
    foreach (var (inst, index) in FindBulletsInRect(origin, end, width))
        inst.Call("disable_bullet", index);
    SpawnSlashVisual(origin, end, width);
}
```

The visual is a code-created `Polygon2D` (data-driven count, like `HitZone`s — no `.tscn`)
added under the parent `BulletManager` (`z_index = 1` keeps it above entities), faded and
freed by a short `Tween`. **Every client runs the same method**, so the visual replicates
with the cast — the remote-client rendering requirement needed no extra networking, just
putting the draw call inside the shared method.

Then handle the echo in `OnBulletControlEventRow` (the binder handler): read the rect back
out of `(X, Y, TargetX, TargetY, Radius)` and call `Slash`. Own echoes stay skipped via
`GameManager.IsLocal(row.CastBy)`.

### Step 4: Client calls it — `TryActivateAbility`

`LocalPlayerInventoryComponent.TryActivateAbility` (the shared static used by the hotkeys
*and* clicking the slot) needs one switch arm for the **optimistic local apply**:

```csharp
case AbilityEffect.SlashBullets(var p):
{
    var origin = player.GlobalPosition;
    var axis = target - origin;
    var dir = axis.LengthSquared() > 0.000001f ? axis.Normalized() : Vector2.Right; // mirror the server fallback
    controller?.Slash(origin, origin + dir * (p.Length * multiplier), p.Width * multiplier);
    break;
}
```

Note the reducer call (`Conn.Reducers.ActivateAbility(...)`) is generic — it was already
there; only the optimistic apply is per-effect. Also add a display line in
`ItemSidebarComponent.FormatAbility` so the hover panel describes the effect.

### Step 5: Ship it

Seeds (Part 2), then:

```bash
cargo check --manifest-path server/spacetimedb/Cargo.toml
server/build.sh                      # regenerates client/Scripts/module_bindings — never hand-edit those
dotnet build client/khvg.csproj
```

Multiplayer test with two clients: caster sees the optimistic cast; the second client sees
the rectangle and the deletions via the event echo.

---

## Pitfalls (each of these has bitten someone)

- **SpacetimeType enums: unit or newtype variants only.** Multi-value payload ⇒ struct.
- **Step 4 amnesia.** Server validates + relays, client subscribed — but nothing calls the
  reducer. Abilities activate from `TryActivateAbility` (hotkeys/slot click); check there
  first when "nothing happens".
- **Optimistic/echo drift.** Whatever the server computes (fallback direction, range
  clamp, position multiplier) must be mirrored in the client's optimistic apply, or the
  caster sees a different effect than everyone else.
- **Position multipliers are a two-sided mirror** — `ABILITY_POSITION_MULTIPLIERS`
  (server `main/global.rs`) and `AbilityPositionMultipliers` (client
  `LocalPlayerInventoryComponent`) must change together.
- **`module_bindings/` is generated.** Regenerate via `server/build.sh`; never hand-edit.
- **Every publish wipes the DB.** Content = seeds. If it's not seeded, it's gone.
- **Untyped plugin calls fail at runtime, not compile time.** Typos in
  `Call("disable_bullet", …)`-style strings surface only when the cast fires — test every
  new query in-game.
- **Texture ids are soft references.** A missing `TextureEntry` means a blank icon, not an
  error.

## New-ability checklist

- [ ] New effect type? `AbilityEffect` variant (struct payload if >1 value) →
      `scale_ability_effect` arm → `activate_ability` arm (+ event encoding documented) →
      client query/visual in `BulletControllerComponent` → echo handler arm → optimistic
      arm in `TryActivateAbility` → `FormatAbility` line
- [ ] `Item::seed` entry (cooldown, charges, slot_cost, position-scaled dims are sane)
- [ ] `TextureEntry` seed for any new texture id
- [ ] `cargo check` → `server/build.sh` → `dotnet build client/khvg.csproj`
- [ ] In-game: hotkey activation from an equipped slot; cooldown/charge behavior correct;
      second client sees the effect and the visual
