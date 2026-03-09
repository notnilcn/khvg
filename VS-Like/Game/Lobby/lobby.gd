extends Control
## lobby.gd
##
## A minimal connection screen shown before the game starts.
## The player either hosts (starts a WebSocket server on DEFAULT_PORT) or
## joins (connects to an IP address the host shares with them).
##
## After connecting the game immediately loads woods_map.tscn.
## A real lobby would show a player list, a chat box, a "ready" button, etc.
## This is intentionally minimal — just enough to get two players into a game.

@onready var ip_input:      LineEdit = %IPInput
@onready var status_label:  Label    = %StatusLabel
@onready var host_button:   Button   = %HostButton
@onready var join_button:   Button   = %JoinButton


func _ready() -> void:
	# Pre-fill with localhost so testing on one machine is one-click.
	ip_input.text = "127.0.0.1"

	# Listen for outcomes from the Network singleton.
	Network.connected_to_server.connect(_on_connected)
	Network.connection_failed.connect(_on_connection_failed)


## HOST: start a server and immediately load the game scene.
## The server is also a player (listen-server model).
func _on_host_button_pressed() -> void:
	_set_ui_enabled(false)
	status_label.text = "Starting server..."
	Network.start_server()

	# The server loads the game immediately. Clients will load it too once
	# they connect and receive the RPC from spawner.gd.
	_load_game()


## JOIN: connect to the entered IP.  Wait for confirmation before loading.
func _on_join_button_pressed() -> void:
	var address := ip_input.text.strip_edges()
	if address.is_empty():
		status_label.text = "Enter a server IP address."
		return

	_set_ui_enabled(false)
	status_label.text = "Connecting to %s..." % address
	Network.join_server(address)

	# _on_connected() or _on_connection_failed() will fire when the result
	# is known (see _ready() connections above).


func _on_connected() -> void:
	status_label.text = "Connected!  Loading game..."
	_load_game()


func _on_connection_failed() -> void:
	status_label.text = "Could not connect.  Check the IP and try again."
	_set_ui_enabled(true)


func _load_game() -> void:
	get_tree().change_scene_to_file("res://VS-Like/Maps/Woods/woods_map.tscn")


func _set_ui_enabled(enabled: bool) -> void:
	host_button.disabled = not enabled
	join_button.disabled = not enabled
	ip_input.editable    = enabled
