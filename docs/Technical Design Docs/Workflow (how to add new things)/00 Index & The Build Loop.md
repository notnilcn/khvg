# 00 Index & The Build Loop

Workflow guides for adding new content and mechanics to the game. Each guide is self-contained and names the exact files, functions, and commands involved. For the *why* behind any mechanism, these guides link into the system descriptions in `Description/` — start with [[01 Architecture & Sync Model]] if nothing here makes sense yet.

## The one decision that picks your guide

Almost everything in this game is **data in a table**, not a code branch. The first question is always: does my new thing fit an *existing* enum variant, or does it need a *new* one?

| I want to add…                                         | Data-only?                         | Guide                                     |
| ------------------------------------------------------ | ---------------------------------- | ----------------------------------------- |
| A texture / sprite                                     | yes                                | [[01 Add a Texture or Sprite\|01]] |
| A weapon using Single/Triple/Cluster                   | yes                                | [[02 Add a Weapon (existing shot pattern)\|02]] |
| A new player shot pattern (`WeaponPattern` variant)    | no — server enum + client switch   | [[03 Add a Player Shot Pattern\|03]] |
| Armor / accessory / artifact / bag                     | yes                                | [[04 Add an Item\|04]] |
| An enchantment                                         | yes                                | [[05 Add an Enchantment\|05]] |
| A consumable (heal / stat buff)                        | yes                                | [[06 Add a Consumable or Ability\|06]] |
| A new consumable *effect* (`ConsumableEffect` variant) | no — server enum + match arms      | [[06 Add a Consumable or Ability\|06]] |
| An enemy (using existing patterns/movement)            | yes                                | [[07 Add an Enemy\|07]] |
| A new enemy bullet pattern (`PatternType` variant)     | no — server enum + client branch   | [[08 Add an Enemy Bullet Pattern\|08]] |
| A new movement behavior (`MovementBehavior` variant)   | no — server enum + match arm       | [[09 Add a Movement Behavior\|09]] |
| A player stat (`StatKind` variant)                     | no — touches ~8 places, both sides | [[10 Add a Player Stat\|10]] |
| A biome / region                                       | yes                                | [[11 Add a Biome or Region\|11]] |

"Data-only" means: add a row (via seed code or an admin upsert) — no changes to enums, matches, or client scripts.

## The build loop

All server-side work ends in the same two commands, wrapped by `server/build.sh` (run from `server/`):

```bash
bash build.sh
```

which does:

```bash
# 1. Regenerate the client's C# bindings from the module schema
spacetime generate --lang csharp --out-dir ../client/Scripts/module_bindings --module-path ./spacetimedb -y
# 2. Publish to the local `bullethell` database, wiping all data
spacetime publish bullethell --delete-data -y
```

Three things to know about this:

1. **`--delete-data` reruns `init`** ([[lifecycle.rs##pub fn init|init]]), which re-executes every `seed_*` function. Seeds are upserts keyed on their string ids (`[[main/seeds.rs##pub trait Seed|Seed]]`), so republishing *refreshes* seeded content — editing a seed and running `build.sh` is the normal iteration loop. It also wipes all player data, so expect to re-create profiles.
2. **Step 1 is only load-bearing when the schema changes** — a new table, field, enum variant, or reducer signature. Pure seed-data edits compile to identical bindings, but running both steps anyway is harmless and keeps one command to remember. Never hand-edit anything under `client/Scripts/module_bindings/`.
3. The local SpacetimeDB server must be running (`spacetime start` in a separate terminal) for step 2.

After `build.sh`, build the client to catch binding mismatches: open the project in Godot 4.6 mono and build, or `dotnet build client/khvg.csproj` from the repo root.

## The admin CLI: testing content without a UI

No admin reducer has a client-side caller ([[09 Admin, Debug & World Lifecycle|09]]) — everything admin goes through the SpacetimeDB CLI against the running server. **Server selection matters:** `build.sh` publishes to `local`, but `spacetime call`/`spacetime sql` default to whatever server is marked default in `spacetime server list` (usually `maincloud`, which runs an *older* module and different data). Pass `--server local` explicitly, or set local as default:

```bash
# 1. Connect a game client first (an identity must exist), then from a terminal:
spacetime call --server local bullethell claim_admin

# 2. Call any admin reducer, e.g. spawn a test boss at world (100, 50):
spacetime call --server local bullethell spawn_enemy TestBoss 100 50 false false

# 3. Inspect tables directly (the only way to read auto-increment ids):
spacetime sql --server local bullethell "SELECT * FROM single_step_def"
spacetime sql --server local bullethell "SELECT enemy_id, template_id, behavior_id FROM enemy"

# 4. Release the admin slot when done (only one identity can hold it):
spacetime call --server local bullethell release_admin
```

(`spacetime sql` supports plain `SELECT ... FROM ... WHERE ...` — no `ORDER BY`/`LIMIT`. A quick way to test admin tooling without launching Godot: any `spacetime call` connects as its own CLI identity, which creates a player row that `claim_admin` can then claim.)

The upsert reducers (`upsert_item`, `upsert_enchantment`, `upsert_enemy_template`, `upsert_movement_def`, `upsert_single_step_def`, `upsert_repeat_step_def`, `upsert_multi_step_def`, `upsert_texture_entry`, `upsert_world_def`, `upsert_biome_def`, `upsert_biome_region_def`) take whole struct arguments as JSON. Conventions, verified against the CLI:

- **Struct fields** are snake_case: `{"def_id": 0, "movement": ...}`.
- **Enum variants are lowercase**, and a variant with fields is an object: `{"chase": {"speed": 60.0, ...}}`.
- **A unit variant (no fields) is `{"variant": {}}`** — *not* a bare string: `"equip_slot": {"weapon": {}}`, `"pattern": {"single": {}}`, `"mode": {"flat": {}}`.
- Enums inside a `Vec` follow the same rule: `"allowed_slots": [{"weapon": {}}, {"artifact": {}}]`.
- If you get the shape wrong, the error message prints the reducer's full signature with exact variant names — read it, it's the fastest way to correct the JSON.

```bash
spacetime call --server local bullethell upsert_movement_def '{"def_id": 0, "movement": {"chase": {"speed": 60.0, "active_duration": 2.0, "pause_duration": 1.0}}}'
```

Because `def_id` is auto-increment, pass `0` to insert a new row, then find the assigned id with `spacetime sql`. Pass a real (existing) id to update that row in place. Changes to `public` tables push to every connected client the instant the reducer commits — no republish needed ([[09 Admin, Debug & World Lifecycle|09]]).

**Seed it or upsert it?** Seeds are for content that ships; they re-apply on every publish and survive `--delete-data`. CLI upserts are for experimenting; they vanish on the next `build.sh`. The usual flow is: prototype via CLI upserts, then bake the result into a seed once you're happy.

## Conventions in these guides

Same as the `Description/` docs ([[00 Roadmap]]): code references use block-link-plus wikilinks (`[[seeds.rs##pub fn seed_world_items|seed_world_items]]`) — if a link stops resolving, the code moved and the guide is stale. Guides describe what the code *actually does today*, including known gaps, which are called out explicitly rather than documented around.
