using Godot;
using System;

public partial class LobbyGui : Control
{
	public override void _Ready()
	{
		// --2D/--3D launch args auto-pick the visual mode (see LaunchArgs). Deferred:
		// ChangeSceneToFile removes the current scene, which is illegal while the tree
		// is still busy adding it (i.e. anywhere inside _Ready).
		if (LaunchArgs.ForcedMode == VisualMode.TwoD) CallDeferred(MethodName.TwoDPressed);
		else if (LaunchArgs.ForcedMode == VisualMode.ThreeD) CallDeferred(MethodName.ThreeDPressed);
	}

	public void TwoDPressed()
	{
		GameVisuals.Mode = VisualMode.TwoD;
		GetTree().ChangeSceneToFile("res://Scenes/game.tscn");
	}

	public void ThreeDPressed()
	{
		GameVisuals.Mode = VisualMode.ThreeD;
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
