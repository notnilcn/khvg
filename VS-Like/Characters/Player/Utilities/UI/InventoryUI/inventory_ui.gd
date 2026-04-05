extends Control
class_name InventoryUI

## inventory_ui.gd
##
## Programmatic overlay panel that shows the player's equipment, inventory,
## consumable slots, and RPG stats.  Press I to toggle.
##
## Instantiated at runtime by player.gd and added to the HUD CanvasLayer so it
## renders above the game world.  All nodes are created in _ready().
##
## Equipment section:
##   Weapon  Ability  Armor  [Accessory slots unlocked by Wisdom]
## Inventory section (default 6 slots, max 12 via backpack):
##   Grid of item icons — click to equip (puts current occupant in inventory).
## Consumable section (default 2 slots, max 8 via backpack):
##   Row of small item slots.
## Stats section:
##   STR  WIS  DEX  DEF  VIT
##
## Clicking outside the panel (on the dark overlay) closes the UI.

# ── palette ───────────────────────────────────────────────────────────────────
const COLOR_PANEL    := Color(0.18, 0.10, 0.44, 0.97)
const COLOR_SLOT     := Color(0.10, 0.06, 0.26, 0.90)
const COLOR_SLOT_HVR := Color(0.28, 0.18, 0.58, 1.00)
const COLOR_BORDER   := Color(0.45, 0.32, 0.80, 0.90)
const COLOR_LOCKED   := Color(0.08, 0.05, 0.18, 0.70)
const SLOT_SIZE      := Vector2(52, 52)

# ── internal state ────────────────────────────────────────────────────────────
var _player: Player

# Slot buttons keyed by logical name
var _equip_slots: Dictionary = {}   # "weapon" | "ability" | "armor" | "acc_0/1/2"
var _inv_slots:   Array[Button] = []
var _con_slots:   Array[Button] = []

# Stat value labels keyed by stat abbreviation
var _stat_labels: Dictionary = {}  # "STR" | "WIS" | "DEX" | "DEF" | "VIT"


# ── lifecycle ─────────────────────────────────────────────────────────────────
func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	process_mode = Node.PROCESS_MODE_WHEN_PAUSED
	visible      = false
	mouse_filter = Control.MOUSE_FILTER_STOP  # block clicks reaching the game
	_build_ui()


# ── build UI ──────────────────────────────────────────────────────────────────
func _build_ui() -> void:
	# ── dark overlay (click anywhere outside the panel to close) ──────────────
	var overlay := ColorRect.new()
	overlay.color = Color(0, 0, 0, 0.55)
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(overlay)
	overlay.gui_input.connect(func(ev: InputEvent) -> void:
		if ev is InputEventMouseButton and ev.pressed:
			close()
	)

	# ── center panel ──────────────────────────────────────────────────────────
	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left",   160)
	margin.add_theme_constant_override("margin_top",     30)
	margin.add_theme_constant_override("margin_right",  160)
	margin.add_theme_constant_override("margin_bottom",  30)
	add_child(margin)

	var panel_style := StyleBoxFlat.new()
	panel_style.bg_color = COLOR_PANEL
	panel_style.set_corner_radius_all(8)
	panel_style.set_border_width_all(2)
	panel_style.border_color = COLOR_BORDER

	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", panel_style)
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	margin.add_child(panel)

	var root_margin := MarginContainer.new()
	root_margin.add_theme_constant_override("margin_left",   16)
	root_margin.add_theme_constant_override("margin_top",    12)
	root_margin.add_theme_constant_override("margin_right",  16)
	root_margin.add_theme_constant_override("margin_bottom", 12)
	panel.add_child(root_margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 12)
	root_margin.add_child(vbox)

	# Title
	var title := Label.new()
	title.text = "INVENTORY     [I] close"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)

	vbox.add_child(HSeparator.new())
	_build_equipment_section(vbox)
	vbox.add_child(HSeparator.new())
	_build_inventory_section(vbox)
	vbox.add_child(HSeparator.new())
	_build_stats_section(vbox)


# ── equipment section ─────────────────────────────────────────────────────────
func _build_equipment_section(parent: VBoxContainer) -> void:
	var lbl := Label.new()
	lbl.text = "EQUIPMENT"
	parent.add_child(lbl)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 8)
	parent.add_child(hbox)

	var slots := [
		{"key": "weapon",  "tip": "WEAPON"},
		{"key": "ability", "tip": "ABILITY"},
		{"key": "armor",   "tip": "ARMOR"},
		{"key": "acc_0",   "tip": "ACCESSORY"},
		{"key": "acc_1",   "tip": "ACCESSORY"},
		{"key": "acc_2",   "tip": "ACCESSORY"},
	]

	for cfg in slots:
		var btn := _make_slot_button(cfg["tip"])
		hbox.add_child(btn)
		_equip_slots[cfg["key"]] = btn
		var key: String = cfg["key"]  # capture for lambda
		btn.pressed.connect(func() -> void: _on_equip_slot_pressed(key))


# ── inventory section ─────────────────────────────────────────────────────────
func _build_inventory_section(parent: VBoxContainer) -> void:
	var lbl := Label.new()
	lbl.text = "INVENTORY"
	parent.add_child(lbl)

	var grid := GridContainer.new()
	grid.columns = 6
	grid.add_theme_constant_override("h_separation", 6)
	grid.add_theme_constant_override("v_separation", 6)
	parent.add_child(grid)

	for i in 12:
		var btn := _make_slot_button("")
		grid.add_child(btn)
		_inv_slots.append(btn)
		var idx := i  # capture for lambda
		btn.pressed.connect(func() -> void: _on_inv_slot_pressed(idx))

	var con_lbl := Label.new()
	con_lbl.text = "CONSUMABLES"
	parent.add_child(con_lbl)

	var con_hbox := HBoxContainer.new()
	con_hbox.add_theme_constant_override("separation", 6)
	parent.add_child(con_hbox)

	for i in 8:
		var btn := _make_slot_button("")
		con_hbox.add_child(btn)
		_con_slots.append(btn)


# ── stats section ─────────────────────────────────────────────────────────────
func _build_stats_section(parent: VBoxContainer) -> void:
	var lbl := Label.new()
	lbl.text = "STATS"
	parent.add_child(lbl)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 24)
	parent.add_child(hbox)

	for stat in ["STR", "WIS", "DEX", "DEF", "VIT"]:
		var vb := VBoxContainer.new()
		vb.add_theme_constant_override("separation", 2)
		hbox.add_child(vb)

		var name_lbl := Label.new()
		name_lbl.text = stat
		name_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		vb.add_child(name_lbl)

		var val_lbl := Label.new()
		val_lbl.text = "0"
		val_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		vb.add_child(val_lbl)

		_stat_labels[stat] = val_lbl


# ── slot button factory ───────────────────────────────────────────────────────
func _make_slot_button(placeholder: String) -> Button:
	var btn := Button.new()
	btn.custom_minimum_size = SLOT_SIZE
	btn.clip_contents       = true
	btn.expand_icon         = true
	btn.icon_alignment      = HORIZONTAL_ALIGNMENT_CENTER
	btn.vertical_icon_alignment = VERTICAL_ALIGNMENT_CENTER
	btn.text = placeholder

	var sn := StyleBoxFlat.new()
	sn.bg_color = COLOR_SLOT
	sn.set_border_width_all(2)
	sn.border_color = COLOR_BORDER
	sn.set_corner_radius_all(4)
	btn.add_theme_stylebox_override("normal", sn)

	var sh := StyleBoxFlat.new()
	sh.bg_color = COLOR_SLOT_HVR
	sh.set_border_width_all(2)
	sh.border_color = Color(0.70, 0.55, 1.0, 1.0)
	sh.set_corner_radius_all(4)
	btn.add_theme_stylebox_override("hover", sh)

	var sp := StyleBoxFlat.new()
	sp.bg_color = Color(0.35, 0.22, 0.70, 1.0)
	sp.set_border_width_all(2)
	sp.border_color = Color(0.70, 0.55, 1.0, 1.0)
	sp.set_corner_radius_all(4)
	btn.add_theme_stylebox_override("pressed", sp)

	return btn


# ── public interface ──────────────────────────────────────────────────────────
func open(player: Player) -> void:
	_player = player
	refresh()
	visible = true
	get_tree().paused = true


func close() -> void:
	visible = false
	get_tree().paused = false


## Refresh all slot icons and stat values from the current player state.
## Call this whenever equipment or inventory changes while the panel is open.
func refresh() -> void:
	if _player == null:
		return
	_refresh_equipment()
	_refresh_inventory()
	_refresh_stats()


# ── refresh helpers ───────────────────────────────────────────────────────────
func _refresh_equipment() -> void:
	_set_slot(_equip_slots.get("weapon"),  _weapon_res(_player.equipped_weapon), "WPN")
	_set_slot(_equip_slots.get("ability"), _weapon_res(_player.equipped_ability), "ABL")
	_set_slot(_equip_slots.get("armor"),   null, "ARM")  # armor not yet implemented

	var acc_count := _player.get_accessory_slot_count()
	for i in 3:
		var slot: Button = _equip_slots.get("acc_%d" % i)
		if slot == null:
			continue
		if i < acc_count:
			var res := _weapon_res(_player.equipped_accessories[i]) if i < _player.equipped_accessories.size() else null
			_set_slot(slot, res, "ACC")
			slot.disabled = false
		else:
			# Slot is wisdom-locked — show requirement.
			slot.icon     = null
			slot.text     = "???"
			slot.disabled = true
			slot.tooltip_text = "Requires %d Wisdom" % (500 * (i))


func _refresh_inventory() -> void:
	var max_inv := _player.inventory_max
	var max_con := _player.consumables_max

	for i in _inv_slots.size():
		var slot := _inv_slots[i]
		if i >= max_inv:
			# Locked slot (no backpack purchased yet)
			slot.disabled = true
			slot.icon     = null
			slot.text     = ""
			_tint_locked(slot)
			continue
		slot.disabled = false
		_restore_slot_style(slot)
		if i < _player.inventory.size():
			_set_slot(slot, _player.inventory[i], "")
		else:
			_set_slot(slot, null, "")

	for i in _con_slots.size():
		var slot := _con_slots[i]
		if i >= max_con:
			slot.disabled = true
			slot.icon     = null
			slot.text     = ""
			_tint_locked(slot)
			continue
		slot.disabled = false
		_restore_slot_style(slot)
		if i < _player.consumables.size():
			_set_slot(slot, _player.consumables[i], "")
		else:
			_set_slot(slot, null, "")


func _refresh_stats() -> void:
	var values := {
		"STR": _player.strength,
		"WIS": _player.wisdom,
		"DEX": _player.dexterity,
		"DEF": _player.defense,
		"VIT": _player.vitality,
	}
	for key in values:
		if _stat_labels.has(key):
			(_stat_labels[key] as Label).text = str(values[key])


# ── slot visual helpers ───────────────────────────────────────────────────────
func _set_slot(slot: Button, res: ResWeapon, placeholder: String) -> void:
	if slot == null:
		return
	if res != null and res.sprite != null:
		slot.icon         = res.sprite
		slot.text         = ""
		slot.tooltip_text = res.weapon_name + "\n" + res.weapon_description
	else:
		slot.icon         = null
		slot.text         = placeholder
		slot.tooltip_text = ""


func _tint_locked(slot: Button) -> void:
	var s := StyleBoxFlat.new()
	s.bg_color = COLOR_LOCKED
	s.set_border_width_all(2)
	s.border_color = Color(0.2, 0.15, 0.35, 0.5)
	s.set_corner_radius_all(4)
	slot.add_theme_stylebox_override("normal", s)
	slot.add_theme_stylebox_override("hover",  s)


func _restore_slot_style(slot: Button) -> void:
	var sn := StyleBoxFlat.new()
	sn.bg_color = COLOR_SLOT
	sn.set_border_width_all(2)
	sn.border_color = COLOR_BORDER
	sn.set_corner_radius_all(4)
	slot.add_theme_stylebox_override("normal", sn)

	var sh := StyleBoxFlat.new()
	sh.bg_color = COLOR_SLOT_HVR
	sh.set_border_width_all(2)
	sh.border_color = Color(0.70, 0.55, 1.0, 1.0)
	sh.set_corner_radius_all(4)
	slot.add_theme_stylebox_override("hover", sh)


func _weapon_res(weapon: Weapon) -> ResWeapon:
	return weapon.resource if weapon != null else null


# ── slot interaction ──────────────────────────────────────────────────────────
## Click an equipment slot → unequip that item back to inventory (if space).
func _on_equip_slot_pressed(key: String) -> void:
	if _player == null:
		return

	match key:
		"weapon":
			if _player.equipped_weapon:
				_player._unequip_to_inventory(_player.equipped_weapon)
				_player.equipped_weapon = null
				_player.weapons_container_ui.update_equipment(_player)
		"ability":
			if _player.equipped_ability:
				_player._unequip_to_inventory(_player.equipped_ability)
				_player.equipped_ability = null
				_player.weapons_container_ui.update_equipment(_player)
		_ when key.begins_with("acc_"):
			var idx := int(key.substr(4))
			if idx < _player.equipped_accessories.size():
				var acc := _player.equipped_accessories[idx]
				_player._unequip_to_inventory(acc)
				_player.equipped_accessories.remove_at(idx)
				_player.weapons_container_ui.update_equipment(_player)

	refresh()


## Click an inventory slot → equip that item (moves current occupant out if any).
func _on_inv_slot_pressed(idx: int) -> void:
	if _player == null:
		return
	if idx >= _player.inventory.size():
		return

	var res := _player.inventory[idx]
	_player.inventory.remove_at(idx)

	if not _player.equip_item(res):
		# Slot was full and unequip-to-inventory also failed → put it back.
		_player.inventory.insert(idx, res)

	refresh()
