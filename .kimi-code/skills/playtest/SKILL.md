---
name: playtest
description: Playtest and verify the game end-to-end — drive the running Godot client via open-godot-mcp (click/hover/drag/keys with node-path-resolved coordinates) and use the `spacetime` CLI for server-side setup/inspection (seed, spawn, give, SQL, logs)
type: prompt
whenToUse: When playtesting, verifying, or driving the running game — entering the world, toggling inventory, dragging items, opening chests, spawning enemies, giving items, reading tables, or checking module logs. Covers both the Godot MCP tools and the `spacetime` CLI.
---

# Playtest: Godot MCP + SpacetimeDB CLI

## Which tool for what — read this first

**Anything a real player does in the game goes through the Godot MCP tools** (recipes below): entering the world, moving, clicking UI, dragging items, opening chests. Do **not** join the world or drive gameplay with `spacetime call` — a CLI `join_world` creates a server-side player with no client attached, so nothing renders, input paths go untested, and it doesn't verify what you were asked to verify. If the task is "join the game / play / test this flow", use the Godot recipes.

**The `spacetime` CLI is for server-side setup and inspection around that playtest**: claiming admin, spawning/despawning enemies, giving items, seeding chests, reading tables with SQL, and checking module logs. Use it to arrange the scenario, then verify the result in the running client via the Godot tools (or via SQL when the check is purely server-side state).

Typical combined flow: `spacetime call bullethell claim_admin` → `spawn_enemy`/`give_item` to set up → `godot_game play` (auto-enters the world) → verify via `godot_exec eval` on live nodes and/or `spacetime sql`.

# Part 1 — Godot MCP playtest recipes

Every UI element's position is authored in the `.tscn` files, so **do not take a screenshot to find buttons**. Resolve the authored node path to live coordinates with `godot_exec eval` + `get_global_rect().get_center()` and inject input there. Screenshots are a fallback for when a recipe fails, or for final visual verification — never for navigation.

The full node-path tables are in `${KIMI_SKILL_DIR}/references/ui-map.md` — read it when you need a path not quoted in a recipe below.

## Session start

1. **If `godot_health check` says BRIDGE_NOT_CONNECTED, launch the editor yourself** — the MCP bridge auto-loads with the editor. Run in the background (it never exits):
   ```bash
   "/c/Users/Clinton/g/Godot/Godot-stable_mono_win64_console.exe" -e --path "C:/Users/Clinton/g/main/client"
   ```
   Wait ~20 s, then `godot_instance list` → the instance appears already adopted; `godot_health check` to verify the bridge (port 6970).
2. **Always run from `main_menu.tscn`** — the project's main scene and the only correct entry point. `godot_editor_edit open_scene res://Scenes/main_menu.tscn`, then `godot_game play` **with `scene: "res://Scenes/main_menu.tscn"` passed explicitly** — never run whatever scene happens to be open in the editor, and never start from `game.tscn` directly (that's the gameplay scene).
3. **Entering the world is automatic.** Each instance's launch args (saved in the editor's per-instance debug options) do the whole menu flow: `--2D`/`--3D` picks the visual mode on the main menu and `--charN` joins the Nth character profile (the last profile when fewer than N exist), so a played instance lands directly in the world with no menu clicking. Verify with the **in-world check**: eval `get_node("/root/game/UI/LobbyComponent").visible == false` **and** `get_node("/root/game/LocalPlayer")` exists (pid-stamped per the standing rule). If the lobby stays visible instead, the instance's identity has no profiles — see recipe 1.
4. Logs: `godot_log get` / `godot_log errors`. Eval is the escape hatch for any live node (`find_child`, signal lists, `get(...)` exports).

## Standing rules

- **The editor runs 2 game instances by default** (Debug → Run Multiple Instances; instance 1 gets `--p1 --3D --char1`, instance 2 gets `--p2 --3D --char1`). Every runtime tool (`godot_exec`/`godot_input`/`godot_runtime_state`/`godot_screenshot`/`godot_game_time`/`godot_profiler`) targets **instance 1** unless you pass `"instance": <n>` in `params` — verified end-to-end for `eval`, `input sequence`, and `screenshot`; each instance is driven fully independently (own scene tree, own SpacetimeDB connection). `godot_game` action `instances` lists them (pid, launch args, ready). All recipes below work unchanged per instance — just add `instance` when you mean the second one. Both instances run the same window size, so resolved coordinates are usually identical across instances — still resolve per instance, never hardcode. `godot_log get` accepts `instance` too (game entries are tagged).
- **Prove which instance answered with a pid stamp** — include `"pid": OS.get_process_id()` in verification evals and compare against `godot_game instances`. Two identical-looking lobbies can make a silently misrouted call look successful; the pid cannot lie. Don't trust the OS windows either: a minimized instance shows a stale preview, and injected input hits the in-engine viewport regardless of window state.
- **Sequence timeouts = check the frame loop, not the plugin.** `input sequence` awaits `process_frame` between steps, so a stalled game loop times out while plain `eval` still answers (the debugger channel is serviced separately). Diagnose: read `Engine.get_process_frames()` twice — no advance = stalled. Known cause: the client's "Connection lost" state can freeze the main loop; the only fix is `godot_game stop` + `play` (fresh instances), then redo the flow. `frame_delay: 0` skips the await but press+release land in the same frame and clicks don't register — it's a probe, not a workaround.

- **No `for`/`while` loops in eval bodies** — they time out. Use `map`/`filter` or single-node access.
- Injected input dispatches on the **next frame** — check hover/drag/visibility state in a follow-up call, never in the same one.
- Always pass `coords: "viewport"` with coordinates from `get_global_rect()` so window-size/stretch mismatches can't move the click.
- Verify hover with `get_tree().root.gui_get_hovered_control()` — **never** `get_viewport().get_mouse_position()`, which reads the real OS cursor.
- Node names contain spaces (e.g. `"Weapon - 0"`) — quote them in `get_node()`.
- `"Weapon - 0"` exists **twice** (Hotbar and Equipment) — always use full container paths, never a bare recursive `find_child`.

## Core primitives

`<path>` below is a runtime node path like `/root/<root>/UI/LobbyComponent/Panel/Create`. `<root>` is the gameplay scene root — always `game`.

**Tool-call shape:** every MCP tool takes an `action` string and a `params` object — e.g. `godot_input` with `action: "sequence"`, `params: {"steps": [...]}`. The recipes below spell out both parts in full; follow them verbatim.

### CLICK `<path>`

Exactly two tool calls. Do not improvise variations — this exact pair is what works:

1. `godot_exec`, `action: "eval"`:
   ```
   params: {"code": "var p = (get_node(\"<path>\") as Control).get_global_rect().get_center(); return {\"x\": int(p.x), \"y\": int(p.y)}"}
   ```
   For non-Control nodes use `(n as Node2D).get_global_position()` instead. **Return a flat `{"x", "y"}` dict, never a raw `Vector2`** — Vector2 does not survive the bridge encoding reliably, which silently corrupts the coordinates. Never guess or hardcode coordinates; always use the eval-resolved center.
2. `godot_input`, `action: "sequence"` — a **single** call that presses and releases (the default `frame_delay: 1` between steps is what makes it land like a real click), with X/Y from step 1:
   ```json
   params: {"steps": [
     {"type": "mouse_button", "params": {"button": "MOUSE_BUTTON_LEFT", "pressed": true,  "position": {"x": X, "y": Y}, "coords": "viewport"}},
     {"type": "mouse_button", "params": {"button": "MOUSE_BUTTON_LEFT", "pressed": false, "position": {"x": X, "y": Y}, "coords": "viewport"}}
   ]}
   ```
   Do **not** split the click into separate press and release `mouse_button` calls — use the one `sequence` call above.

### HOVER `<path>`

`godot_input mouse_motion` to the rect center → next call, eval `get_tree().root.gui_get_hovered_control()` and confirm it's the target (or its child — the slot `TextureRect` icons are `mouse_filter = Ignore`, so hover lands on the slot Panel itself).

### DRAG `<pathA>` → `<pathB>`

1. `mouse_motion` to A's center.
2. `mouse_button` left **press** at A.
3. `mouse_motion` to B's center **with `button_mask: ["MOUSE_BUTTON_LEFT"]`** — the button mask on the motion is what crosses the engine's drag-start threshold. Verify with eval `get_tree().root.gui_is_dragging()`.
4. `mouse_button` left **release** at B.

### PRESS `<key>`

`godot_input key` press + release. Key names are plain names like `Tab`, `T`, `E` — **not** Godot enum constants (`KEY_TAB` silently no-ops). Relevant keys: `Tab` = toggle inventory menu, `T` = cycle weapon toggle, `Shift+E` = chest interact, `R` (hold) = self-slow, WASD = movement, `Ctrl+scroll` = speed scale.

### TYPE `<path>` `<text>`

Eval `get_node("<path>").grab_focus()` → `godot_input text` with the text. (Or set `.text` directly by eval if injection is flaky — but prefer injection when testing the real input path.)

## Recipes

### 1. Create a profile (only when the lobby is showing)

The lobby stays visible only when the instance's identity has no profiles to auto-join. Create one, then re-play so the launch args join it:

1. CLICK `/root/<root>/UI/LobbyComponent/Panel/Create` ("CREATE", toggles `CreateProfilePanel` visible).
2. TYPE `/root/<root>/UI/LobbyComponent/CreateProfilePanel/Panel/LineEdit` with the profile name.
3. CLICK `/root/<root>/UI/LobbyComponent/CreateProfilePanel/Panel/Create`.
4. Verify: eval the child count of `/root/<root>/UI/LobbyComponent/ScrollContainer/VBoxContainer` increased by one.
5. `godot_game stop` + `play` again — the `--charN` arg now auto-joins. Confirm with the in-world check.

### 2. Toggle the inventory menu

PRESS `Tab`. This flips two containers under `/root/<root>/LocalPlayer/InventoryCanvas/InventoryPanel/`: `Menu` (backpack + sidebars) shows while `Hotbar` hides. **Hotbar slots can't be hovered or dragged while the menu is open.** `Equipment` and `StatusBarComponent` stay visible either way.

### 3. Move an item (drag slot → slot)

DRAG from the source slot to the target slot (paths in `references/ui-map.md`; example target: `/root/<root>/LocalPlayer/InventoryCanvas/InventoryPanel/Menu/Backpack/Row - 1/General - 7`).

- **Empty slots can't start a drag** (`Icon.Texture == null`) — check the icon first if a drag doesn't start.
- Fires the `SwapSlots(from, to)` reducer; verify via the target slot's `Icon.Texture` becoming non-null or via `InventoryChanged`-driven state.
- Right-click (press) a **backpack** slot with an item = `DropItem`. Left-click (press) an ability slot (32–37) = activate ability.

### 4. Allocate a stat point

With the menu open (Tab), CLICK `/root/<root>/LocalPlayer/InventoryCanvas/InventoryPanel/Menu/StatsSidebar/Panel/<Stat>Plus` where `<Stat>` is `Strength`/`Wisdom`/`Dexterity`/`DamageDealer`/`Supporter`/`Artisan`. All six buttons share one visibility flag — they're only visible while `UnspentPoints > 0`; check `visible` before clicking.

### 5. Open a chest and take an item

1. Move the local player inside the chest's interact radius (48px circle on `/root/<root>/Chest/InteractComponent`): drive WASD via `godot_input key`, or eval-set the player's position for pure UI testing.
2. PRESS `Shift+E` — toggles `/root/<root>/Chest/ChestCanvas/ChestPanel` visible. It closes when the player steps out, so don't move afterwards.
3. DRAG from a chest entry (`Chest/ChestCanvas/ChestPanel/MarginContainer/VBoxContainer/ItemsGrid/ChestSlot`, auto-suffixed `ChestSlot2`, …) onto any player `SlotComponent`. Fires `TakeFromChest`.
- **Re-resolve chest entry paths after every content change** — entries are freed and re-instantiated, so cached paths/indexes go stale. Later chests also get auto-suffixed root names; locate them by iterating `/root/<root>` children, not by exact name.

### 6. Two players in one world (multi-instance)

Play starts both instances and each auto-joins the world on its own (`--p1`/`--p2` give them distinct identities, each with its own `--charN` profile pick) — this is the standard setup for testing multiplayer sync (remote-player rendering, combat between players, shared chests/loot). Verify with the in-world check on **each** instance (pid-stamped per the standing rule, `"instance": 2` in `params` for the second), then confirm each instance sees the other's player as a `NonLocalPlayer` node under `/root/<root>`. Run the two instances **sequentially** — don't fire runtime calls at both in the same batch.

## When a recipe fails

Check in order: (1) is the target `visible` and is the parent chain visible (menu open/closed state); (2) did the click land — `gui_get_hovered_control()` after a motion to the same point; (3) `godot_log errors` for reducer or script errors; (4) only then `godot_screenshot game` and read the PNG to see what's actually on screen.

**"Connection lost" state:** if the SpacetimeDB connection drops, the game shows `/root/<root>/UI/ConnectionLostComponent` with a `CenterContainer/Panel/VBoxContainer/Reconnect` button and all player nodes despawn — a joined instance can look like it "went back to the menu". Reconnect = CLICK that button; it returns through `main_menu.tscn`, where the launch args carry it straight back into the world — confirm with the in-world check. If the frame loop has also stalled (sequence timeouts; see the standing rule), skip the button and just `godot_game stop` + `play`. First confirm the server is actually up (`spacetime server ping https://khvgames.com/server`) or reconnect will fail again.

# Part 2 — SpacetimeDB CLI (`bullethell` module)

Server-side setup and inspection. Module name: `bullethell`, server: `https://khvgames.com/server` — the project default from `server/spacetime.json`, so `spacetime` commands run from `server/` hit it automatically (no local server involved). Remember the routing rule at the top: **don't drive gameplay from here** — use the Godot recipes for anything a player does.

## Core commands

```bash
spacetime call bullethell <reducer> [args...]   # invoke a reducer
spacetime sql bullethell "SELECT * FROM <table>" # read table contents
spacetime logs bullethell                        # module logs — reducers return no data, so
                                                 # results/errors show here and in the call output
spacetime describe bullethell                    # exact reducer arg types when unsure
```

Argument encoding: positional, JSON literals. Quote strings (`"Knight"`); numbers/bools bare. Structs and enums (sum types) use SATN JSON: `{"field": value, ...}` for structs, `{"VariantName": {}}` for a unit enum variant, `{"VariantName": payload}` for one with data. On Windows Git Bash, wrap JSON args in **single** quotes so the shell doesn't eat the double quotes.

The caller identity is the CLI's own identity, which gets a `LoggedOutPlayer` row on connect like any client — so lobby/in-world guards apply to CLI calls the same way.

## Admin gate

Most testing reducers are admin-only. There is **one global admin slot**:

```bash
spacetime call bullethell claim_admin      # fails if anyone already holds it
spacetime call bullethell release_admin    # frees the slot
```

`"Admin only."` in the output = you forgot `claim_admin` (or someone else holds it).

## Testing reducer quick reference

Admin-only (setup for a playtest):

```bash
spacetime call bullethell spawn_enemy '"TestBoss"' 0 0 false false   # (template_id, x, y, is_elite, immortal)
spacetime call bullethell despawn_enemy 42                            # enemy_id from sql
spacetime call bullethell despawn_all_enemies
spacetime call bullethell toggle_enemy_spawning false                 # pause biome spawns
spacetime call bullethell give_item '"<username>"' '"<profile>"' '"Bow"'
spacetime call bullethell remove_item '"<username>"' '"<profile>"' '"Bow"'
spacetime call bullethell change_stats '"<username>"' '"<profile>"' 10 10 10 10 10 10   # str wis dex dps sup art
spacetime call bullethell add_chest 0 0                               # (x, y)
spacetime call bullethell add_all_items_to_chest 1 false   # one of every catalog item
spacetime call bullethell generate_world_proc '"default"' 123         # (world_id, seed)
spacetime call bullethell toggle_debug                                # 1 Hz position debug rows
```

Gameplay reducers — listed for reference (signatures), but **call these from the client via the Godot recipes, not from the CLI**, unless you're deliberately testing server-side guard behavior without a client:

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
SELECT * FROM player_profile          -- profile ids for give_item
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
