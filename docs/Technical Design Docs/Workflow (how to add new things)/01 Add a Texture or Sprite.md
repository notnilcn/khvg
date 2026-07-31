# 01 Add a Texture or Sprite

Every visual in the game resolves through the server's `TextureEntry` table: a `texture_id` string → a `res://` path to a Godot resource. The client caches the whole table at connect (base wave, `CatalogComponent`) and `GD.Load`s the path at point of use. There are **no hardcoded asset paths anywhere in `client/Scripts`** — so adding art never touches client code. Background: [[01 Architecture & Sync Model|01]] (`conn-5`/`conn-7`).

## 1. Create the Godot resource

Textures are `SpriteFrames` `.tres` files under `client/Textures/`, organized by purpose (`Attacks/`, `Enemies/`, `Items/`, `Players/`, `World/`). Raw PNGs live in `client/Assets/`. What the `.tres` must contain depends on the consumer:

- **Entities (players, enemies)** — must have `Idle` and `Walk` animations. `Enemy.cs` and `RemoteVisualComponent` call `Play("Walk")`/`Play("Idle")` based on movement; if those animations don't exist the sprite never plays. Single-frame animations are fine.
- **Projectiles / bullets** — `BulletSpawnerComponent.ApplyBulletTexture` reads `GetFrameTexture("default", 0)` ([[BulletSpawnerComponent.cs##private void ApplyBulletTexture|ApplyBulletTexture]]): it needs a `default` animation and only ever uses its **first frame**.
- **Items / enchantments (UI icons)** — the inventory UI loads the resource as a texture source; a one-frame `default` animation suffices.
- **World (ground/overlay/decor)** — consumed by the terrain MultiMesh batching; follow the existing `Textures/World/...` entries. Note decor art bakes its shadow as a *separate* entry (`Tree` + `TreeShadow`), and decor is never rotated on placement ([[03 World & Hex Grid|03]]).

**Orientation matters for projectiles.** All player bullets get a global `ProjectileTextureAngleOffset = π/4` rotation ([[BulletSpawnerComponent.cs##ProjectileTextureAngleOffset|offset]]) because `Arrow.tres` art faces up-right. If your projectile art is drawn facing **right** (`Vector2.Right`, the engine convention), it will appear 45° off — either rotate the source art to face up-right like the arrow, or accept that this one global offset currently assumes arrow-style art for every projectile.

## 2. Register the `TextureEntry`

Two ways:

**Seed it (ships with the game).** Add a line to `seed_default_textures` in `main/seeds.rs` ([[main/seeds.rs##pub fn seed_default_textures|seed_default_textures]]), following the existing entries:

```rust
TextureEntry::seed(ctx, texture("Fireball", "res://Textures/Attacks/Fireball.tres", TextureKind::Projectile));
```

`TextureKind` (`Player`/`Enemy`/`Projectile`/`Item`/`Environment`, [[world/tables.rs##pub enum TextureKind|TextureKind]]) is informational grouping — pick the matching one. The `texture_id` string is the key everything else references (`Item.texture_id`, `EnemyTemplate.texture_id`, `WeaponBehavior.projectile_texture_id`, step-def `texture_id`, biome texture configs). Keep the id and the file's base name identical — every seeded entry does, and it makes ids guessable.

**Upsert it at runtime (experimenting).**

```bash
spacetime call --server local bullethell upsert_texture_entry '{"texture_id": "Fireball", "res_path": "res://Textures/Attacks/Fireball.tres", "kind": {"projectile": {}}}'
```

## 3. Verify

- `bash build.sh` (from `server/`) if you seeded; nothing to rebuild if you upserted — `TextureEntry` is a `public` table, so the row pushes to connected clients immediately.
- In-game, whatever references the `texture_id` (item icon, enemy sprite, bullet) should now render. If a sprite is invisible but everything else works: the `.tres` is missing the animation names its consumer expects (`Idle`/`Walk` vs `default`) — that's the most common failure.

## Gotchas

- `res_path` must be a loadable `SpriteFrames` resource for entities and bullets. Don't point a bullet `texture_id` at a raw `.png` — `GD.Load<SpriteFrames>` will return null and the bullet renders textureless (silently).
- Republishing with `--delete-data` re-applies seeds, so a seeded entry edited in place (new `res_path` for an existing id) refreshes cleanly. CLI-upserted rows are wiped by the next `build.sh`.
- Enchantment icons follow the same path (`Enchantment.texture_id`), though no seeded enchantment texture entries exist yet — the UI degrades gracefully without one.
