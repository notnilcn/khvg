# UI node-path map

Runtime node paths for every interactive UI element, extracted from the `.tscn` sources. `<root>` = the resolved gameplay scene root (`game` or `Characters` — resolve at runtime, never hardcode). All paths verified against the scenes on 2026-08-21; if a path 404s, the scene changed — re-read the `.tscn` and update this file.

## `main_menu.tscn` — runtime root `/root/MainMenu` (script `LobbyGui.cs`)

The scene has **two** nodes named `MainMenu`: the root `Control` and a child `CenterContainer`.

| Path | Control | Effect |
|---|---|---|
| `/root/MainMenu/MainMenu/Panel/CenterContainer/VBoxContainer/CharSlots` | Button "Character Slots" | `ChangeSceneToFile("res://Scenes/game.tscn")` — only way into gameplay; frees this tree |
| `/root/MainMenu/MainMenu/Panel/CenterContainer/VBoxContainer/ServerList` | Button "Server List" | Shows `ServerList` panel, hides `MainMenu` + `Settings` |
| `/root/MainMenu/MainMenu/Panel/CenterContainer/VBoxContainer/Settings` | Button "Settings" | Shows `Settings` panel, hides `MainMenu` + `ServerList` |
| `/root/MainMenu/ServerList/Panel/CenterContainer/Button` | Button "BACK" | Back to main menu |
| `/root/MainMenu/Settings/Panel/CenterContainer/Button` | Button "BACK" | Back to main menu |

Startup: only the `MainMenu` CenterContainer is visible. `ServerList`/`Settings` panels start hidden; their `ScrollContainer/VBoxContainer`s are empty placeholders. No Quit button, no text fields.

## Lobby — `/root/<root>/UI/LobbyComponent` (from `lobby_component.tscn`)

| Path (relative to `LobbyComponent`) | Control | Effect |
|---|---|---|
| `Panel/Create` | Button "CREATE" | Toggles `CreateProfilePanel` visibility |
| `CreateProfilePanel/Panel/LineEdit` | LineEdit (placeholder "Profile Name =^.^=") | Profile name input |
| `CreateProfilePanel/Panel/Create` | Button "CREATE" | `CreateProfile(text, "Knight")` reducer if text non-empty, then hides panel |
| `ScrollContainer/VBoxContainer` | Container | One `profile_panel.tscn` instance per `LocalPlayerProfiles` row (replay on) |
| `Panel/CenterContainer/Button` | Button "BACK" | **No signal connected — does nothing** |

Each profile panel (all named `Profile`, auto-suffixed — access via `VBoxContainer.get_child(i)`):

- `Join` — Button "JOIN": hides the lobby, calls `JoinWorld(profileId)`, switches subscriptions lobby→game. **No-op while `CreateProfilePanel` is visible.**
- `Delete` — Button "DELETE": `DeleteProfile` reducer + frees the panel. Same visibility guard.
- `Name`, `ID` — Labels. `Icon` — Sprite2D.

Join success: `LobbyComponent.visible == false` and `/root/<root>/LocalPlayer` exists. No wired path back to the lobby from gameplay.

## Inventory — base `/root/<root>/LocalPlayer/InventoryCanvas/InventoryPanel/`

The live UI is declared inline in `local_player.tscn` (`Scenes/UI/inventory_panel.tscn` is an unused duplicate). Slot nodes are uniquely named `<Type> - <SlotIndex>`; the `[Export] SlotIndex` matches the name. No groups, no lookup helper.

### Hotbar — `Hotbar/` (hidden while the Tab menu is open)

| Node | SlotIndex |
|---|---|
| `Hotbar/Weapon - 0` | 0 |
| `Hotbar/Consumable - 1` … `Consumable - 4` | 1–4 |

### Equipment — `Equipment/` (always visible)

| Node | SlotIndex |
|---|---|
| `Equipment/Weapon/Weapon - 0` | 0 (mirror of the hotbar slot — same index, two nodes) |
| `Equipment/Accessory/Accessory - 5` | 5 |
| `Equipment/Armor/Armor - 6` | 6 |
| `Equipment/Abilities/Ability - 32` … `Ability - 37` | 32–37 |

### Backpack — `Menu/Backpack/` (menu open only)

| Node | SlotIndex |
|---|---|
| `Row - 1/General - 7` … `General - 10` | 7–10 |
| `Row - 2/General - 11` … `General - 14` | 11–14 |
| `Row - 3/General - 15` … `General - 18` | 15–18 |
| `Row - 4/General - 19` … `General - 22` | 19–22 |
| `Row - 5/General - 23` … `General - 26` | 23–26 |
| `Row - 6/General - 27` … `General - 30` | 27–30 |
| `Row - 7/Bag - 31` | 31 (not in the exported `GeneralSlots` array — its icon is never refreshed by `InventoryPanel`) |

Each slot has a `TextureRect` icon child with `mouse_filter = Ignore` — hover/drag targets the slot Panel itself.

### Tab toggle

`open_inventory` = Tab. Flips `Menu` (full-screen, `mouse_filter = Ignore`, holds Backpack + StatsSidebar + ItemSidebar; hidden at start) and `Hotbar` (visible at start, hidden while menu is open). `Equipment` and `StatusBarComponent` are always visible.

### StatsSidebar — `Menu/StatsSidebar/Panel/`

Buttons `StrengthPlus`, `WisdomPlus`, `DexterityPlus`, `DamageDealerPlus`, `SupporterPlus`, `ArtisanPlus` → `AllocateStat(stat, 1)`. All six share one visibility flag: visible only while `UnspentPoints > 0`. Labels in the same `Panel`: `Level`, `Health`, the six stats, `Defense`, `Unspent`.

### ItemSidebar — `Menu/ItemSidebar`

`unique_name_in_owner = true` (`%ItemSidebar` resolves anywhere in the LocalPlayer scene). Children: `Panel/Name`, `Panel/Description`, `Panel/Texture`, `Panel/DetailsScroll/DetailsList`. Never hides itself; only children flip visibility on hover.

### StatusBarComponent — `StatusBarComponent/`

Always visible. Children: `Row` (HBoxContainer of effect labels), `ActiveStatusEffectBinder`.

## Chest — `/root/<root>/Chest` (first chest; later ones auto-suffix — locate by iterating root children)

| Path | Notes |
|---|---|
| `Chest/InteractComponent` | Area2D, 48px circle; player must be inside for interact |
| `Chest/ChestCanvas/ChestPanel` | PanelContainer, CanvasLayer layer 2, hidden by default; toggled by `interact` (Shift+E), closes when the player steps out |
| `Chest/ChestCanvas/ChestPanel/MarginContainer/VBoxContainer/ItemsGrid` | GridContainer, 6 columns; entries are code-instantiated `ChestSlot`, `ChestSlot2`, … (48×48, child `Icon`) |

Chest entries are drag-source only (drag data `{"chest_item_id": …}` → `TakeFromChest` on drop onto any player slot) and are **rebuilt on every content change** — re-resolve paths after each change.
