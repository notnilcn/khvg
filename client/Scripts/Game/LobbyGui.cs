using Godot;
using System;

public partial class LobbyGui : Control
{
	public override void _Ready()
	{
	}

	public void CharSlotsPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/game.tscn");
	}

	public void ServerListPressed()
	{
		GetNode<CenterContainer>("MainMenu").Hide();
		GetNode<Panel>("Settings").Hide();
		GetNode<Panel>("ServerList").Show();
	}
	public void SettingsPressed()
	{
		GetNode<CenterContainer>("MainMenu").Hide();
		GetNode<Panel>("ServerList").Hide();
		GetNode<Panel>("Settings").Show();
	}

	public void BackPressed()
	{
		GetNode<Panel>("ServerList").Hide();
		GetNode<Panel>("Settings").Hide();
		GetNode<CenterContainer>("MainMenu").Show();
	}
}
