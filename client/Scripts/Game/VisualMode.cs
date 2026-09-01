/// Which visual layer set the client loads, picked on the main menu (2D/3D buttons in
/// main_menu.tscn) before game.tscn is entered. Static because the choice must survive the
/// ChangeSceneToFile from the menu to the game scene. 2D mode never loads world_3d.tscn or
/// any res_path3d asset; 3D mode loads no 2D world visuals (terrain, entity/drop/chest
/// sprites, bullet textures all gate on Is3D) — only UI icons still use res_path2d.
public enum VisualMode { TwoD, ThreeD }

public static class GameVisuals
{
	public static VisualMode Mode = VisualMode.TwoD;
	public static bool Is3D => Mode == VisualMode.ThreeD;
}
