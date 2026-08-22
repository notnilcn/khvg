---
name: godot-playtest
description: Screenshot-free playtesting of the Godot client via open-godot-mcp — reusable MCP tool-call recipes (click/hover/drag/keys) that resolve UI node paths to live coordinates instead of reading screenshots
type: prompt
whenToUse: When playtesting, verifying, or driving the running game through the Godot MCP tools — navigating menus, joining the world, toggling the inventory, dragging items, opening chests, clicking buttons — without taking a screenshot first
---

# Godot MCP playtest recipes

Every UI element's position is authored in the `.tscn` files, so **do not take a screenshot to find buttons**. Resolve the authored node path to live coordinates with `godot_exec eval` + `get_global_rect().get_center()` and inject input there. Screenshots are a fallback for when a recipe fails, or for final visual verification — never for navigation.

The full node-path tables are in `${KIMI_SKILL_DIR}/references/ui-map.md` — read it when you need a path not quoted in a recipe below.

## Session start

1. **If `godot_health check` says BRIDGE_NOT_CONNECTED, launch the editor yourself** — the MCP bridge auto-loads with the editor. Run in the background (it never exits):
   ```bash
   "/c/Users/Clinton/g/Godot/Godot-stable_mono_win64_console.exe" -e --path "C:/Users/Clinton/OneDrive/g/main/client"
   ```
   Wait ~20 s, then `godot_instance list` → the instance appears already adopted; `godot_health check` to verify the bridge (port 6970).
2. **Always run from `main_menu.tscn`** — the project's main scene and the only correct entry point. `godot_editor_edit open_scene res://Scenes/main_menu.tscn`, then `godot_game play` **with `scene: "res://Scenes/main_menu.tscn"` passed explicitly** — never run whatever scene happens to be open in the editor, and never start from `game.tscn` directly (that's the gameplay scene; reaching it through the menu's Character Slots button is recipe 1, which also exercises the menu).
3. **Resolve the scene root once** and reuse it: eval `get_tree().root.get_children().map(func(c): return c.name)`. The menu root is `/root/MainMenu`; after clicking Character Slots the gameplay root has been named both `game` and `Characters` at different times — never hardcode it. Below, `<root>` means the resolved gameplay root.
4. Logs: `godot_log get` / `godot_log errors`. Eval is the escape hatch for any live node (`find_child`, signal lists, `get(...)` exports).

## Standing rules

- **No `for`/`while` loops in eval bodies** — they time out. Use `map`/`filter` or single-node access.
- Injected input dispatches on the **next frame** — check hover/drag/visibility state in a follow-up call, never in the same one.
- Always pass `coords: "viewport"` with coordinates from `get_global_rect()` so window-size/stretch mismatches can't move the click.
- Verify hover with `get_tree().root.gui_get_hovered_control()` — **never** `get_viewport().get_mouse_position()`, which reads the real OS cursor.
- Node names contain spaces (e.g. `"Weapon - 0"`) — quote them in `get_node()`.
- `"Weapon - 0"` exists **twice** (Hotbar and Equipment) — always use full container paths, never a bare recursive `find_child`.

## Core primitives

`<path>` below is a runtime node path like `/root/<root>/UI/LobbyComponent/Panel/Create`.

### CLICK `<path>`

1. `godot_exec eval`: `var c = get_node("<path>") as Control; return c.get_global_rect().get_center()` (for non-Control nodes use `(n as Node2D).get_global_position()`).
2. `godot_input mouse_button`: left **press** at those coords with `coords: "viewport"`, then left **release** at the same coords.

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

### 1. Menu → gameplay

CLICK `/root/MainMenu/MainMenu/Panel/CenterContainer/VBoxContainer/CharSlots` (the "Character Slots" button — the only way into gameplay; `ChangeSceneToFile` frees the old tree). Re-resolve the scene root afterwards (step 3 of Session start). The lobby shows as `/root/<root>/UI/LobbyComponent`.

### 2. Create a profile

1. CLICK `/root/<root>/UI/LobbyComponent/Panel/Create` ("CREATE", toggles `CreateProfilePanel` visible).
2. TYPE `/root/<root>/UI/LobbyComponent/CreateProfilePanel/Panel/LineEdit` with the profile name.
3. CLICK `/root/<root>/UI/LobbyComponent/CreateProfilePanel/Panel/Create`.
4. Verify: eval the child count of `/root/<root>/UI/LobbyComponent/ScrollContainer/VBoxContainer` increased by one.

### 3. Join the world

CLICK the Join button of profile *i*: eval `get_node("/root/<root>/UI/LobbyComponent/ScrollContainer/VBoxContainer").get_child(i).get_node("Join").get_global_rect().get_center()`, then click there. (All profile panels are named `Profile` with auto-suffixed siblings — index-based access is the reliable route.)

- **Join and Delete no-op while `CreateProfilePanel` is visible** — close it first if a click does nothing.
- Join succeeded when eval shows `get_node("/root/<root>/UI/LobbyComponent").visible == false` **and** `get_node("/root/<root>/LocalPlayer")` exists.

### 4. Toggle the inventory menu

PRESS `Tab`. This flips two containers under `/root/<root>/LocalPlayer/InventoryCanvas/InventoryPanel/`: `Menu` (backpack + sidebars) shows while `Hotbar` hides. **Hotbar slots can't be hovered or dragged while the menu is open.** `Equipment` and `StatusBarComponent` stay visible either way.

### 5. Move an item (drag slot → slot)

DRAG from the source slot to the target slot (paths in `references/ui-map.md`; example target: `/root/<root>/LocalPlayer/InventoryCanvas/InventoryPanel/Menu/Backpack/Row - 1/General - 7`).

- **Empty slots can't start a drag** (`Icon.Texture == null`) — check the icon first if a drag doesn't start.
- Fires the `SwapSlots(from, to)` reducer; verify via the target slot's `Icon.Texture` becoming non-null or via `InventoryChanged`-driven state.
- Right-click (press) a **backpack** slot with an item = `DropItem`. Left-click (press) an ability slot (32–37) = activate ability.

### 6. Allocate a stat point

With the menu open (Tab), CLICK `/root/<root>/LocalPlayer/InventoryCanvas/InventoryPanel/Menu/StatsSidebar/Panel/<Stat>Plus` where `<Stat>` is `Strength`/`Wisdom`/`Dexterity`/`DamageDealer`/`Supporter`/`Artisan`. All six buttons share one visibility flag — they're only visible while `UnspentPoints > 0`; check `visible` before clicking.

### 7. Open a chest and take an item

1. Move the local player inside the chest's interact radius (48px circle on `/root/<root>/Chest/InteractComponent`): drive WASD via `godot_input key`, or eval-set the player's position for pure UI testing.
2. PRESS `Shift+E` — toggles `/root/<root>/Chest/ChestCanvas/ChestPanel` visible. It closes when the player steps out, so don't move afterwards.
3. DRAG from a chest entry (`Chest/ChestCanvas/ChestPanel/MarginContainer/VBoxContainer/ItemsGrid/ChestSlot`, auto-suffixed `ChestSlot2`, …) onto any player `SlotComponent`. Fires `TakeFromChest`.
- **Re-resolve chest entry paths after every content change** — entries are freed and re-instantiated, so cached paths/indexes go stale. Later chests also get auto-suffixed root names; locate them by iterating `/root/<root>` children, not by exact name.

## When a recipe fails

Check in order: (1) is the target `visible` and is the parent chain visible (menu open/closed state); (2) did the click land — `gui_get_hovered_control()` after a motion to the same point; (3) `godot_log errors` for reducer or script errors; (4) only then `godot_screenshot game` and read the PNG to see what's actually on screen.
