/*
# Declaring variables
```c# */
using Godot;

public partial class Lobby : Control
{
	private CenterContainer _center;
	private Label _statusLabel;
	private Label _title;
	private VBoxContainer _vbox;
	private Button _playButton;
/*
```
*/

/*
# `_Ready()`
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
		_statusLabel.Text = "Press Play to enter the game.";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_vbox.AddChild(_statusLabel);

		_playButton = new Button();
		_playButton.Text = "Play";
		_playButton.Pressed += _OnPlayPressed;
		_vbox.AddChild(_playButton);
	}
/*
```
*/

/*
# `_OnPlayPressed()`
```c# */
	private void _OnPlayPressed()
	{
		GetTree().ChangeSceneToFile("res://VS-Like/Maps/Woods/woods_map.tscn");
	}
/*
```
*/
}
