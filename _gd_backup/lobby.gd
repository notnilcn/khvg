extends Control
# Overall Description
"""lobby.gd — The first scene that is loaded into the game when you double click project.godot.
This is where you can choose to either "Host Game" or "Join Game".
Connection screen shown before the game starts. All UI nodes are created in code here so we don't depend on the .tscn file having perfect formatting — a hand-written .tscn is easy to get wrong.
"""

# UI
#### `_ready()`
"""The `_ready()` function is a special godot function that automatically runs when all of the nodes' children are added to the scene. Google it if you're curious.
The point of this function here is to layout the lobby ui. You don't really need to know the godot-specific syntax and functions for creating a ui it's just a bunch of random ass functions.
This doesn't define what the functions actually do, it just lays out the ui and what the ui connects to.
"""
"""
```gdscript """
var center: CenterContainer
var status_label: Label
var title: Label
var vbox: VBoxContainer
var ip_input:     LineEdit
var host_button:  Button
var join_button:  Button


func _ready() -> void:
	# Fill the entire window.
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	# Center everything on screen.
	center = CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	# Vertical stack of widgets.
	vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 14)
	center.add_child(vbox)

	# Title
	title = Label.new()
	title.text = "Bullet Hell MMO"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 24)
	vbox.add_child(title)

	# Status / feedback line
	status_label = Label.new()
	status_label.text = "Host a game, or enter an IP address and join one."
	status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(status_label)

	# IP address input — pre-filled with localhost for local testing
	ip_input = LineEdit.new()
	ip_input.custom_minimum_size = Vector2(240, 0)
	ip_input.placeholder_text    = "Server IP  (e.g. 192.168.1.5)"
	ip_input.text                = "127.0.0.1"
	vbox.add_child(ip_input)

	# Buttons
	host_button = Button.new()
	host_button.text = "Host Game"
	host_button.pressed.connect(_on_host_pressed)
	vbox.add_child(host_button)

	join_button = Button.new()
	join_button.text = "Join Game"
	join_button.pressed.connect(_on_join_pressed)
	vbox.add_child(join_button)

	# Listen for connection results from the Network singleton.
	Network.connected_to_server.connect(_on_connected)
	Network.connection_failed.connect(_on_connection_failed)
"""
```
"""


#### `_on_host_pressed()`
"""Defines what happens the the "Host Game" button is pressed.
"""
"""
```gdscript """
func _on_host_pressed() -> void:
	_set_ui_enabled(false)
	status_label.text = "Starting server on port %d..." % Network.DEFAULT_PORT

	if not Network.start_server():
		status_label.text = "Failed to open port %d.  Is something else already hosting?" \
			% Network.DEFAULT_PORT
		_set_ui_enabled(true)
		return

	# Server loads the game scene immediately.
	# Clients will join after connecting and then load the scene themselves.
	_load_game()
"""
```
"""

#### `_on_join_pressed()`
"""Defines what happens the the "Join Game" button is pressed.
"""
"""
```gdscript """
func _on_join_pressed() -> void:
	var address := ip_input.text.strip_edges()
	if address.is_empty():
		status_label.text = "Enter a server IP address first."
		return

	_set_ui_enabled(false)
	status_label.text = "Connecting to %s:%d ..." % [address, Network.DEFAULT_PORT]
	Network.join_server(address)

	# _on_connected() or _on_connection_failed() fires when the result is known.
"""
```
"""

# network callbacks
#### `_on_connected()`
"""
"""
"""
```gdscript """
func _on_connected() -> void:
	status_label.text = "Connected!  Loading game..."
	_load_game()
"""
```
"""

#### `_on_connection_failed()`
"""
"""
"""
```gdscript """
func _on_connection_failed() -> void:
	status_label.text = "Could not connect.  Check the IP address and try again."
	_set_ui_enabled(true)
"""
```
"""

# helpers
#### `_load_game()`
"""Defines 
"""
"""
```gdscript """
func _load_game() -> void:
	get_tree().change_scene_to_file("res://VS-Like/Maps/Woods/woods_map.tscn")
"""
```
"""

#### `_set_ui_enabled(enabled)`
"""Defines 
"""
"""
```gdscript """
func _set_ui_enabled(enabled: bool) -> void:
	host_button.disabled = not enabled
	join_button.disabled  = not enabled
	ip_input.editable     = enabled
"""
```
"""
