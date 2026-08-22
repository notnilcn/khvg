---
name: spacetimedb-cli
description: Interact with the SpacetimeDB server from the command line — call reducers, read tables with SQL, and check logs for the `bullethell` module (admin/test workflows)
type: prompt
whenToUse: When you need to call a server reducer directly, seed/spawn/give things for testing, claim admin, inspect table contents, or check module logs — anything server-side done through the `spacetime` CLI instead of the game client
---

# SpacetimeDB CLI (`bullethell` module)

All server interaction is CLI-based. Module name: `bullethell`, server: `local` (the CLI default after `spacetime start`).

## Core commands

```bash
spacetime call bullethell <reducer> [args...]   # invoke a reducer
spacetime sql bullethell "SELECT * FROM <table>" # read table contents
spacetime logs bullethell                        # module logs — reducers return no data, so
                                                 # results/errors show here and in the call output
spacetime describe bullethell                    # exact reducer arg types when unsure
```

Argument encoding: positional, JSON literals. Quote strings (`"Knight"`); numbers/bools bare. Structs and enums (sum types) use SATN JSON: `{"field": value, ...}` for structs, `{"VariantName": {}}` for a unit enum variant, `{"VariantName": payload}` for one with data. On Windows Git Bash, wrap JSON args in **single** quotes so the shell doesn't eat the double quotes.

The caller identity is the CLI's own identity, which gets a `LoggedOutPlayer` row on connect like any client — so lobby/in-world guards apply to CLI calls the same way (`join_world` works from the CLI if you pass a profile id you own; read ids with `spacetime sql`).

## Admin gate

Most testing reducers are admin-only. There is **one global admin slot**:

```bash
spacetime call bullethell claim_admin      # fails if anyone already holds it
spacetime call bullethell release_admin    # frees the slot
```

`"Admin only."` in the output = you forgot `claim_admin` (or someone else holds it).

## Testing reducer quick reference

Admin-only:

```bash
spacetime call bullethell spawn_enemy '"TestBoss"' 0 0 false false   # (template_id, x, y, is_elite, immortal)
spacetime call bullethell despawn_enemy 42                            # enemy_id from sql
spacetime call bullethell despawn_all_enemies
spacetime call bullethell toggle_enemy_spawning false                 # pause biome spawns
spacetime call bullethell give_item '"<username>"' '"<profile>"' '"Bow"'
spacetime call bullethell remove_item '"<username>"' '"<profile>"' '"Bow"'
spacetime call bullethell change_stats '"<username>"' '"<profile>"' 10 10 10 10 10 10   # str wis dex dps sup art
spacetime call bullethell add_chest 0 0                               # (x, y)
spacetime call bullethell add_item_to_chest 1 '"every_single_item"' false   # magic id = one of every catalog item
spacetime call bullethell generate_world_proc '"default"' 123         # (world_id, seed)
spacetime call bullethell toggle_debug                                # 1 Hz position debug rows
```

Gameplay (callable as your own player, subject to the usual guards):

```bash
spacetime call bullethell create_profile '"Bob"' '"Knight"'           # (name, texture_id)
spacetime call bullethell join_world 1                                # profile_id from: sql "SELECT * FROM player_profile"
spacetime call bullethell leave_world
spacetime call bullethell report_movement 100 50 0 0                  # (x, y, direction, speed)
spacetime call bullethell swap_slots 7 0                              # (from_index, to_index)
spacetime call bullethell use_item 1                                  # hotbar consumable slot
spacetime call bullethell activate_ability 32 200 200                 # (slot_index, target_x, target_y)
spacetime call bullethell begin_ability_charge 32
spacetime call bullethell release_ability_charge 32 200 200
spacetime call bullethell allocate_stat '{"Strength": {}}' 5          # enum arg as SATN JSON
spacetime call bullethell set_slot_toggle 0 1                         # weapon slot toggle index
spacetime call bullethell apply_enchantment 0 '"splitting"'           # (slot_index, enchantment_id)
spacetime call bullethell remove_enchantment 0 '"splitting"'
spacetime call bullethell drop_item 7                                 # right-click drop equivalent
spacetime call bullethell pickup_drop 12                              # drop_id from sql
spacetime call bullethell take_from_chest 5                           # chest_item_id from sql
```

Full signatures live in `server/spacetimedb/src/*/reducers.rs` (+ `main/admin.rs`, `chest/mod.rs`, `status/reducers.rs`); `server/AGENTS.md` explains what each does. Runtime enemy-authoring upserts (`upsert_item`, `upsert_enemy_template`, `upsert_*_step_def`, …) take whole structs — usually easier to edit the seed and republish instead.

## Useful SQL reads

```sql
SELECT * FROM player_profile          -- profile ids for join_world / give_item
SELECT * FROM logged_in_player
SELECT * FROM player_inventory_slot   -- slot contents (slot_index semantics: 0 weapon, 1-4 hotbar,
                                      -- 5 accessory, 6 armor, 7-30 backpack, 31 bag, 32-37 ability)
SELECT * FROM enemy                   -- live enemy ids/hp
SELECT * FROM chest                   -- then chest_item for contents
SELECT * FROM item                    -- catalog item_ids for give_item
```

## Gotchas

- **Every publish wipes the database** (`server/build.sh` uses `--delete-data`); seeds re-run from `main/seeds.rs`. Don't expect rows to survive a republish — and any connected game client drops with the wipe.
- **A newly added reducer doesn't exist until you republish** — if `spacetime call` reports the reducer is unknown (or it's missing from `spacetime describe bullethell`), run `server/build.sh` first; `cargo check` alone doesn't publish.
- A reducer returning `Ok` proves the call landed, not that the *effect* you wanted happened — verify via `spacetime sql` or the subscribed client.
- Scheduled reducers (`tick_enemy_spawn`, `tick_enemy_behavior`, `tick_status_effects`) are driven by schedule rows, not meant to be called by hand.
- `claim_admin` after `client_connected` — if it errors "Player not found", connect once (any call) so the row exists, then claim.
