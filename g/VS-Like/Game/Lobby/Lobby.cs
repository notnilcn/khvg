/*
Lobby screen code.
```c# */
using Godot;

public partial class Lobby : Control
{
    private CenterContainer _center;
    private Label _statusLabel;
    private Label _title;
    private VBoxContainer _vbox;
    private LineEdit _ipInput;
    private Button _hostButton;
    private Button _joinButton;
/*
```
*/


/*
# `_Ready()`
`_Ready()` is a special godot function that automatically fires when the node and all of its children are loaded into the scene.
This defines where all the buttons go and how the ui looks.
```c# */
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _center = new CenterContainer();
        _center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_center);

        _vbox = new VBoxContainer();
        _vbox.AddThemeConstantOverride("separation", 14);
        _center.AddChild(_vbox);

        _title = new Label();
        _title.Text = "Bullet Hell MMO";
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.AddThemeFontSizeOverride("font_size", 24);
        _vbox.AddChild(_title);

        _statusLabel = new Label();
        _statusLabel.Text = "Host a game, or enter an IP address and join one.";
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _vbox.AddChild(_statusLabel);

        _ipInput = new LineEdit();
        _ipInput.CustomMinimumSize = new Vector2(240, 0);
        _ipInput.PlaceholderText   = "Server IP  (e.g. 192.168.1.5)";
        _ipInput.Text              = "127.0.0.1";
        _vbox.AddChild(_ipInput);

        _hostButton = new Button();
        _hostButton.Text = "Host Game";
        _hostButton.Pressed += _OnHostPressed;
        _vbox.AddChild(_hostButton);

        _joinButton = new Button();
        _joinButton.Text = "Join Game";
        _joinButton.Pressed += _OnJoinPressed;
        _vbox.AddChild(_joinButton);

        Network.Instance.ConnectedToServer += _OnConnected;
        Network.Instance.ConnectionFailed  += _OnConnectionFailed;
    }
/*
```
*/

/*
# `_OnHostPressed()`
This is kinda obvious
```c# */
    private void _OnHostPressed()
    {
        _SetUiEnabled(false);
        _statusLabel.Text = $"Starting server on port {Network.DefaultPort}...";

        if (!Network.Instance.StartServer())
        {
            _statusLabel.Text = $"Failed to open port {Network.DefaultPort}.  Is something else already hosting?";
            _SetUiEnabled(true);
            return;
        }

        _LoadGame();
    }
/*
```
*/

/*
# `_OnJoinPressed()`
This is kinda obvious
```c# */
    private void _OnJoinPressed()
    {
        var address = _ipInput.Text.StripEdges();
        if (address.Length == 0)
        {
            _statusLabel.Text = "Enter a server IP address first.";
            return;
        }

        _SetUiEnabled(false);
        _statusLabel.Text = $"Connecting to {address}:{Network.DefaultPort} ...";
        Network.Instance.JoinServer(address);
    }
/*
```
*/


/*
# `_OnConnected()`
This is kinda obvious
```c# */
    private void _OnConnected()
    {
        _statusLabel.Text = "Connected!  Loading game...";
        _LoadGame();
    }
/*
```
*/



/*
# `_OnConnectionFailed()`
This is kinda obvious
```c# */
    private void _OnConnectionFailed()
    {
        _statusLabel.Text = "Could not connect.  Check the IP address and try again.";
        _SetUiEnabled(true);
    }
/*
```
*/


/*
# `_LoadGame()`
This is kinda obvious
```c# */
    private void _LoadGame()
    {
        GetTree().ChangeSceneToFile("res://VS-Like/Maps/Woods/woods_map.tscn");
    }
/*
```
*/


/*
# `_SetUiEnabled(enabled)`
This is kinda obvious
```c# */
    private void _SetUiEnabled(bool enabled)
    {
        _hostButton.Disabled = !enabled;
        _joinButton.Disabled  = !enabled;
        _ipInput.Editable     = enabled;
    }
/*
```
*/
}