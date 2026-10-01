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

Typical combined flow: `spacetime --server khvg-proxy call bullethell claim_admin` → `spawn_enemy`/`give_item` to set up → `godot_game play` (auto-enters the world) → verify via `godot_exec eval` on live nodes and/or `spacetime sql`.

# Part 1 — Godot MCP playtest recipes (project-specific)

Generic UI-driving mechanics (CLICK/HOVER/DRAG/PRESS/HOLD/TYPE exact tool calls, coordinate rules, eval pitfalls, stall diagnosis, failure checklist) live in the MCP itself — **follow them, don't re-derive here**:

- `playtest` MCP prompt — the full interactive + deterministic workflow, including exact CLICK/DRAG JSON.
- `Docs/03-Realtime-Testing/Interactive-Playtest.md` in `Open-Godot-MCP/` — the long-form version (primitives, standing rules, 3D attribution patterns).
- Tool descriptions on `godot_input` / `godot_exec` / `godot_game` / `godot_game_time` / `godot_screenshot` / `godot_runtime_state` — short reminders at the call site.

What this file adds on top: **this project's** entry point, launch args, node paths, key bindings, and game-logic effects. `<root>` below is the gameplay scene root — always `game`. Full node-path tables are in `${KIMI_SKILL_DIR}/references/ui-map.md` — read it when you need a path not quoted in a recipe.

## Session start

1. **If `godot_health check` says BRIDGE_NOT_CONNECTED, launch the editor yourself** — the MCP bridge auto-loads with the editor. The Godot binary is on `PATH` as `Godot-stable_mono_win64_console` (resolves to `C:\Users\Clinton\g\Godot\Godot-stable_mono_win64_console.exe`), so call it by name — no absolute path. Run in the background (it never exits):
   ```powershell
   Start-Process -FilePath "Godot-stable_mono_win64_console" -ArgumentList "-e", "--path", "C:\Users\Clinton\g\main\client"
   ```
   `Start-Process` returns immediately while the editor keeps running (the `bash` tool here is Windows PowerShell). To launch it directly instead, run `Godot-stable_mono_win64_console -e --path "C:/Users/Clinton/g/main/client"`.
   Wait ~20 s, then `godot_instance list` → the instance appears already adopted; `godot_health check` to verify the bridge (port 6970).
2. **Always run from `main_menu.tscn`** — the project's main scene and the only correct entry point. `godot_editor_edit open_scene res://Scenes/main_menu.tscn`, then `godot_game play` **with `scene: "res://Scenes/main_menu.tscn"` passed explicitly** — never run whatever scene happens to be open in the editor, and never start from `game.tscn` directly (that's the gameplay scene).
3. **Entering the world is automatic.** Each instance's launch args (saved in the editor's per-instance debug options) do the whole menu flow: `--2D`/`--3D` picks the visual mode on the main menu and `--charN` joins the Nth character profile (the last profile when fewer than N exist), so a played instance lands directly in the world with no menu clicking. Verify with the **in-world check**: eval `get_node("/root/game/UI/LobbyComponent").visible == false` **and** `get_node("/root/game/LocalPlayer")` exists (pid-stamped — see project rules). If the lobby stays visible instead, the instance's identity has no profiles — see recipe 1.
4. Logs: `godot_log get` / `godot_log errors` (game entries are tagged per instance).

## Project standing rules

- **The editor runs 2 game instances by default** (Debug → Run Multiple Instances; instance 1 gets `--p1 --3D --char1`, instance 2 gets `--p2 --3D --char1`). Every runtime tool targets **instance 1** unless you pass `"instance": <n>` in `params` — verified end-to-end for `eval`, `input sequence`, and `screenshot`; each instance is driven fully independently (own scene tree, own SpacetimeDB connection). All recipes below work unchanged per instance — just add `instance` when you mean the second one. Both instances run the same window size, so resolved coordinates are usually identical across instances — still resolve per instance, never hardcode.
- **Pid-stamp every verification eval** (`"pid": OS.get_process_id()`, compare against `godot_game instances`). Two identical-looking lobbies can make a silently misrouted call look successful. Don't trust the OS windows either: a minimized instance shows a stale preview, and injected input hits the in-engine viewport regardless of window state.
- **If a pid stamp changes across calls, re-establish every assumed state** (camera pose, pitch, zoom, player position) before interpreting the next result.
- **Known project cause of a stalled frame loop:** the client's "Connection lost" state can freeze the main loop (fix is `godot_game stop` + `play`). See "Connection lost" below. **Liveness-probe caveat:** `Engine.get_process_frames()` was observed pinned at one value while the game was provably alive (`Time.get_ticks_msec()` advanced between evals, `Engine.get_frames_per_second()` read 165) — compare a ticks delta across two evals before declaring a stall; use `get_process_frames()` only as corroboration.
- **After a C# edit**, `dotnet build client/khvg.csproj` compiles fine with the editor/game running, but the live process keeps its already-loaded assembly — `godot_game stop` + `play` to load the fix. The build output is `.godot/mono/temp/bin/Debug/khvg.dll`.

## Key bindings (game effects — mechanics in the MCP prompt)

`Tab` = toggle inventory menu, `T` = cycle weapon toggle, `Shift+E` = chest interact, `R` (hold) = self-slow, WASD = movement, `Ctrl+scroll` = speed scale, `X` = reset camera pitch to default, `Z` = reset camera yaw. Arrow keys are `Up`/`Down`/`Left`/`Right` (arcball pitch/yaw).

## Recipes

CLICK/HOVER/DRAG/PRESS/HOLD/TYPE mechanics: see the MCP `playtest` prompt (exact JSON). Below is only the game logic around them.

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
- Slot-node quirks that bite: `"Weapon - 0"` exists **twice** (Hotbar and Equipment) — always use full container paths. Slot `TextureRect` icons are `mouse_filter = Ignore`, so hover lands on the slot Panel itself.

### 4. Allocate a stat point

With the menu open (Tab), CLICK `/root/<root>/LocalPlayer/InventoryCanvas/InventoryPanel/Menu/StatsSidebar/Panel/<Stat>Plus` where `<Stat>` is `Strength`/`Wisdom`/`Dexterity`/`DamageDealer`/`Supporter`/`Artisan`. All six buttons share one visibility flag — they're only visible while `UnspentPoints > 0`; check `visible` before clicking.

### 5. Open a chest and take an item

1. Move the local player inside the chest's interact radius (48px circle on `/root/<root>/Chest/InteractComponent`): drive WASD via `godot_input key`, or eval-set the player's position for pure UI testing.
2. PRESS `Shift+E` — toggles `/root/<root>/Chest/ChestCanvas/ChestPanel` visible. It closes when the player steps out, so don't move afterwards.
3. DRAG from a chest entry (`Chest/ChestCanvas/ChestPanel/MarginContainer/VBoxContainer/ItemsGrid/ChestSlot`, auto-suffixed `ChestSlot2`, …) onto any player `SlotComponent`. Fires `TakeFromChest`.
- **Re-resolve chest entry paths after every content change** — entries are freed and re-instantiated, so cached paths/indexes go stale. Later chests also get auto-suffixed root names; locate them by iterating `/root/<root>` children, not by exact name.

### 6. Two players in one world (multi-instance)

Play starts both instances and each auto-joins the world on its own (`--p1`/`--p2` give them distinct identities, each with its own `--charN` profile pick) — this is the standard setup for testing multiplayer sync (remote-player rendering, combat between players, shared chests/loot). Verify with the in-world check on **each** instance (pid-stamped, `"instance": 2` in `params` for the second), then confirm each instance sees the other's player as a `NonLocalPlayer` node under `/root/<root>`. Run the two instances **sequentially** — don't fire runtime calls at both in the same batch.

### 7. Drive and verify the 3D camera (pitch/zoom/render attribution)

Generic pose/attribution patterns (center-ray as source of truth, A-B toggle at same pose, PIL comparison, live-loop check) are in `Interactive-Playtest.md` §5 — this recipe is the project bindings:

1. Read the pose: eval the Camera3D's `global_transform.origin` plus `project_ray_normal()` at the viewport center (`Vector2(640, 360)` at 1280x720) — pitch = `asin(-dir.y)`. The rig's C# props ARE reachable from GDScript eval via `get()`/`set()` — direct `rig.PitchDegrees` syntax fails, while `rig.get("PitchDegrees")` and `rig.set("PitchDegrees", -55.0)` work (same for `Distance` and `Yaw`). Prefer exact `set()` over HOLDs for camera work: rate-based holds overshoot because MCP round-trip latency (seconds) dominates the hold duration — even back-to-back press/release calls can swing 60–90°. The game clamps pitch to its configured range, so `set()` outside it just clamps.
2. Manual driving still works: HOLD `Up`/`Down` to arcball the pitch (Zoom_In/Out are scroll actions); `X` resets pitch to default, `Z` resets yaw. Zoom wheel steps need explicit `pressed: true` + `pressed: false` pairs — one tick per pair; bare press steps without `pressed` latch and swallow subsequent ticks. Tap-style press+release pairs for pitch are unreliable at high fps (both can land in the same frame → zero movement); prefer HOLD or exact `set()`. Re-read the pose after every drive — closed-loop, never assumed. (`Engine.time_scale` is also settable via eval for slow-motion inspection, but it does not fix hold overshoot — latency, not game speed, is the problem.)
3. To attribute a visual bug, toggle ONE variable between screenshots at the SAME pose: `camera.projection` (ORTHOGONAL↔PERSECTIVE isolates projection/frustum causes) or the render addon's master flag (e.g. the pixel-art pipeline's `enabled` isolates compositor/material causes). Screenshots land on disk — compare with PIL, not eyeballs: pixel-identical on control regions is the bar for "no change"; for small artifacts, `crop((x0,y0,x1,y1)).resize((w*3, h*3), Image.NEAREST)` before reading. TIME-animated water/clouds mean any two genuinely fresh frames always differ somewhere. Uniform (77,77,77) is the main-viewport clear color showing through the transparent 3D SubViewport, i.e. nothing rasterized there, not even sky. Restore every toggled flag afterwards — never leave one behind.

### 8. Attribute a 3D pixel with a physics raycast (what surface is really there)

When a screenshot pixel is ambiguous (water vs sky vs hole in the world), cast the camera's own ray through it and see what it hits (generic pattern in `Interactive-Playtest.md` §5; project camera path below):

```
params: {"code": "var cam = get_node(\"/root/game/3D/SubViewportContainer/SubViewport/World3DComponent/Camera3D\"); var o = cam.project_ray_origin(Vector2(X, Y)); var d = cam.project_ray_normal(Vector2(X, Y)); var hit = cam.get_world_3d().direct_space_state.intersect_ray(PhysicsRayQueryParameters3D.create(o, o + d * 5000.0)); return {\"pid\": OS.get_process_id(), \"oy\": o.y, \"hit\": hit}"}
```

The load-bearing asymmetry: MST terrain chunks carry trimesh `StaticBody3D` collision, but water tiles, the backing ocean, and the sky-cloud sheet are visual-only (no collision) — so a ray that renders water yet hits NOTHING is seeing through to non-collidable geometry (backing/tiles/sky), while a ray that hits a chunk is terminated by real terrain. Strongest form: combine with the recipe-7 visibility toggle — if suspect pixels render pixel-identical with the terrain node hidden, the terrain contributes zero pixels there and the sightline provably misses it.

### 9. Attribute missing 3D terrain (periodic chunk coverage)

The 3D terrain renders the canonical MST lap plus the `ChunkAprons` mirror pool (a limited set of lap-translated copies); a visible region of the world can be empty because its mirror is missing, not because anything is hidden. To pin it down:

1. Raycast the suspect screen pixel to the ground plane (recipe 8 gives origin `o`/direction `d`; project: `t = -o.y / d.y`, hit = `o + d * t`).
2. Identify the owner chunk in eval — the GDScript global class `MarchingSquaresHexRingsGrid` is usable from eval: `world_to_cell(Vector2(hit.x, hit.z), spacing_for(t.cell_size))` then `to_lower_res(hex.x, hex.y, int(t.chunk_hex_radius))` against the `MarchingSquaresTerrain` node. `t.chunks` is a dictionary keyed by `Vector2i` holding the canonical chunk nodes.
3. List the actual mirrors: children of `/root/game/3D/SubViewportContainer/SubViewport/World3DComponent/TerrainComponent3D/ChunkAprons` with `visible` and `global_position` — check whether the missing chunk's nearest lap image is present.
4. **Prove it by injection:** create a temporary `MeshInstance3D`, set `mesh = t.chunks[Vector2i(cq, cr)].mesh`, `AddChild` it under `TerrainComponent3D`, and place it at the missing image position (`MarchingSquaresHexRingsGrid.chunk_center_world(chunk, radius, cell_size)`, plus lap vectors). If the hole fills, it is a mirror-coverage bug, not culling/material. `queue_free()` the probe afterwards.

## When a recipe fails

Generic checklist (visible chain → hover check → `godot_log errors` → screenshot last) is in the MCP docs — run it first.

**"Connection lost" state:** if the SpacetimeDB connection drops, the game shows `/root/<root>/UI/ConnectionLostComponent` with a `CenterContainer/Panel/VBoxContainer/Reconnect` button and all player nodes despawn — a joined instance can look like it "went back to the menu". Reconnect = CLICK that button; it returns through `main_menu.tscn`, where the launch args carry it straight back into the world — confirm with the in-world check. If the frame loop has also stalled (sequence timeouts), skip the button and just `godot_game stop` + `play`. First confirm the server is actually up (`spacetime server ping https://khvgames.com/server`) or reconnect will fail again.

# Part 2 — SpacetimeDB CLI (`bullethell` module)

Server-side setup and inspection. Module name: `bullethell`. The target server is the `khvg-proxy` nickname (`https://khvgames.com/server`, from `server/spacetime.json`) — pass `--server khvg-proxy` on EVERY command (`spacetime server list` shows nicknames). Without it the CLI defaults to local `127.0.0.1:3000` and fails to connect. Remember the routing rule at the top: **don't drive gameplay from here** — use the Godot recipes for anything a player does.

## Core commands

(All commands below need `--server khvg-proxy` inserted after `spacetime` — omitted for brevity.)

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

- **`spacetime sql` is a small SQL subset**: `SELECT a, b FROM t WHERE x > 0 LIMIT n` works, but `BETWEEN` and `ORDER BY` are rejected (400 Bad Request). Keep reads to plain `SELECT … LIMIT` with simple `WHERE`.
- **The `bash` tool here is Windows PowerShell**: `head`/`tail` don't exist — pipe to `Select-Object -First N` instead, or capture full output and search it.
- **Every publish wipes the database** (`server/build.sh` uses `--delete-data`); seeds re-run from `main/seeds.rs`. Don't expect rows to survive a republish — and any connected game client drops with the wipe.
- **A newly added reducer doesn't exist until you republish** — if `spacetime call` reports the reducer is unknown (or it's missing from `spacetime describe bullethell`), run `server/build.sh` first; `cargo check` alone doesn't publish.
- A reducer returning `Ok` proves the call landed, not that the *effect* you wanted happened — verify via `spacetime sql` or the subscribed client.
- Scheduled reducers (`tick_enemy_spawn`, `tick_enemy_behavior`, `tick_status_effects`) are driven by schedule rows, not meant to be called by hand.
- `claim_admin` after `client_connected` — if it errors "Player not found", connect once (any call) so the row exists, then claim.
