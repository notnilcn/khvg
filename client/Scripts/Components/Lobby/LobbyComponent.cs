#nullable enable
using Godot;
using PhantomCamera;
using SpacetimeDB.Types;

/// <summary>
/// The lobby / character-select UI: the CharSlots markup lives in lobby_component.tscn
/// (instanced under the UI CanvasLayer in main.tscn) and the profile panels are instanced
/// per LocalPlayerProfiles row (data-driven count, so code instantiation is allowed).
/// Owns the show/hide flow with the main-menu PhantomCamera2D priority and the
/// create/join/delete profile reducer calls (logic moved out of GameManager.cs).
/// </summary>
public partial class LobbyComponent : ControlComponent
{
    [Export] public PackedScene PlayerProfileScene { get; set; } = null!;

    private PhantomCamera2D? mainMenuPCam2D;
    private TableBinderComponent profilesBinder = null!;

    public override void _Ready()
    {
        base._Ready();
        profilesBinder = GetNode<TableBinderComponent>("LocalPlayerProfilesBinder");
    }

    protected override void OnRegistered()
    {
        // The phantom_camera addon node stays a plain child of Main — reached by path.
        mainMenuPCam2D = this.GetAncestor<GameManager>()?
            .GetNodeOrNull<Node2D>("MainMenuPhantomCamera2D")?.AsPhantomCamera2D();

        ShowLobby();
    }

    public void ShowLobby()
    {
        Show();
        if (mainMenuPCam2D == null) return;
        mainMenuPCam2D.Priority = 60;
    }

    private void HideLobby()
    {
        Hide();
        if (mainMenuPCam2D == null) return;
        mainMenuPCam2D.Priority = 0;
    }

    // --- TableBinderComponent signal handlers (wired in lobby_component.tscn) ---

    private void OnProfileRowInserted()
    {
        var profile = (PlayerProfile)profilesBinder.LastRow!;
        Control profilePanel = PlayerProfileScene.Instantiate<Control>();
        profilePanel.GetNode<Label>("Name").Text = profile.ProfileName;
        profilePanel.GetNode<Label>("ID").Text = profile.ProfileId.ToString();
        profilePanel.GetNode<Button>("Join").Pressed += () => OnJoinPressed(profile);
        profilePanel.GetNode<Button>("Delete").Pressed += () => OnDeletePressed(profile);
        GetNode("ScrollContainer/VBoxContainer").AddChild(profilePanel);
        GD.Print($"Successfully synced a profile: {profile.ProfileName}, TextureID: {profile.TextureId}");
    }

    private void OnJoinPressed(PlayerProfile profile)
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn == null || GetNode<Control>("CreateProfilePanel").Visible) return;
        HideLobby();
        conn.Reducers.JoinWorld(profile.ProfileId);
        GetSibling<TableSubscriber>()?.SubscribeGame();
        GetSibling<TableSubscriber>()?.UnsubscribeLobby();
    }

    private void OnDeletePressed(PlayerProfile profile)
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn == null || GetNode<Control>("CreateProfilePanel").Visible) return;
        conn.Reducers.DeleteProfile(profile.ProfileId);
        foreach (Control x in GetNode<Control>("ScrollContainer/VBoxContainer").GetChildren())
        {
            if (x.GetNode<Label>("ID").Text.ToInt() == (int)profile.ProfileId) x.QueueFree();
        }
    }

    private void OnCreatePanelPressed()
    {
        GetNode<Control>("CreateProfilePanel").Visible = !GetNode<Control>("CreateProfilePanel").Visible;
    }

    private void OnCreatePressed()
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn == null || !GetNode<Control>("CreateProfilePanel").Visible) return;
        if (GetNode<LineEdit>("CreateProfilePanel/Panel/LineEdit").Text.Length > 0)
            conn.Reducers.CreateProfile(GetNode<LineEdit>("CreateProfilePanel/Panel/LineEdit").Text, "Knight");
        GetNode<Control>("CreateProfilePanel").Hide();
    }
}
