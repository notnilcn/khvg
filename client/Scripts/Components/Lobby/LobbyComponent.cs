#nullable enable
using Godot;
using PhantomCamera;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The lobby / character-select UI: the CharSlots markup lives in lobby_component.tscn
/// (instanced under the UI CanvasLayer in game.tscn) and the profile panels are instanced
/// per LocalPlayerProfiles row (data-driven count, so code instantiation is allowed).
/// Owns the show/hide flow with the main-menu PhantomCamera2D priority and the
/// create/join/delete profile reducer calls (logic moved out of GameManager.cs).
/// </summary>
public partial class LobbyComponent : ControlComponent
{
    [Export] public PackedScene PlayerProfileScene { get; set; } = null!;

    /// Seconds to wait for the LocalPlayer row (or a reducer failure) before a join
    /// attempt is aborted and the lobby re-shown with an error.
    [Export] public double JoinTimeoutSeconds { get; set; } = 5.0;

    private PhantomCamera2D? mainMenuPCam2D;
    private TableBinderComponent profilesBinder = null!;
    private bool joinPending;
    /// One panel per profile row, keyed by ProfileId — insert replays can't duplicate a
    /// panel, and delete goes straight to the panel instead of scanning ID labels.
    private readonly Dictionary<ulong, Control> profilePanels = new();

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
        if (LaunchArgs.CharIndex is int charIndex)
            QueueAutoJoin(charIndex);
    }

    // --charN: join the Nth profile once the full list is known — the lobby wave's
    // applied callback (rows already cached on a re-shown lobby, so join straight away).
    private void QueueAutoJoin(int charIndex)
    {
        var subscriber = GetSibling<TableSubscriber>();
        if (subscriber == null) return;
        if (subscriber.LobbySubActive) AutoJoin(charIndex);
        else
        {
            void Handler() { subscriber.LobbyApplied -= Handler; AutoJoin(charIndex); }
            subscriber.LobbyApplied += Handler;
        }
    }

    private void AutoJoin(int charIndex)
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn == null) return;
        // ProfileId order matches the panel order (cache iteration is primary-key order).
        var profiles = conn.Db.LocalPlayerProfiles.Iter().OrderBy(p => p.ProfileId).ToList();
        if (profiles.Count == 0)
        {
            GD.PrintErr($"[LobbyComponent] --char{charIndex}: no profiles to join.");
            return;
        }
        var profile = profiles[Math.Min(charIndex, profiles.Count) - 1];
        GD.Print($"[LobbyComponent] --char{charIndex}: auto-joining profile {profile.ProfileName}");
        OnJoinPressed(profile);
    }

    public void ShowLobby()
    {
        Show();
        if (mainMenuPCam2D == null) return;
        mainMenuPCam2D.Priority = 60;
    }

    public void HideLobby()
    {
        joinPending = false;
        Hide();
        if (mainMenuPCam2D == null) return;
        mainMenuPCam2D.Priority = 0;
    }

    // --- TableBinderComponent signal handlers (wired in lobby_component.tscn) ---

    private void OnProfileRowInserted()
    {
        var profile = (PlayerProfile)profilesBinder.LastRow!;
        if (profilePanels.ContainsKey(profile.ProfileId)) return;
        Control profilePanel = PlayerProfileScene.Instantiate<Control>();
        profilePanel.GetNode<Label>("Name").Text = profile.ProfileName;
        profilePanel.GetNode<Label>("ID").Text = profile.ProfileId.ToString();
        profilePanel.GetNode<Button>("Join").Pressed += () => OnJoinPressed(profile);
        profilePanel.GetNode<Button>("Delete").Pressed += () => OnDeletePressed(profile);
        GetNode("ScrollContainer/VBoxContainer").AddChild(profilePanel);
        profilePanels[profile.ProfileId] = profilePanel;
        GD.Print($"Successfully synced a profile: {profile.ProfileName}, TextureID: {profile.TextureId}");
    }

    private void OnJoinPressed(PlayerProfile profile)
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn == null || joinPending || GetNode<Control>("CreateProfilePanel").Visible) return;
        joinPending = true;
        GetNode<Label>("Panel/JoinError").Text = "";

        // The lobby stays visible until the LocalPlayer row actually arrives —
        // EntitySpawnerComponent.OnLocalPlayerInsert calls HideLobby() then. The reducer
        // event and the timeout below cover the failure paths.
        conn.Reducers.OnJoinWorld += OnJoinWorldResult;
        conn.Reducers.JoinWorld(profile.ProfileId);
        GetSibling<EntitySpawnerComponent>()?.DespawnAll();
        GetSibling<TableSubscriber>()?.SubscribeGame();
        GetSibling<TableSubscriber>()?.UnsubscribeLobby();
        GetTree().CreateTimer(JoinTimeoutSeconds).Timeout += OnJoinTimeout;
    }

    private void OnJoinWorldResult(ReducerEventContext ctx, ulong profileId)
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn != null)
            conn.Reducers.OnJoinWorld -= OnJoinWorldResult;
        if (!joinPending) return;
        if (ctx.Event.Status is Status.Failed(var reason))
            FailJoin($"Join failed: {reason}");
        else if (ctx.Event.Status is Status.OutOfEnergy)
            FailJoin("Join failed: out of energy");
        // Committed: nothing to do — the join completes when the LocalPlayer row arrives.
    }

    private void OnJoinTimeout()
    {
        if (joinPending)
            FailJoin("Join timed out — no response from the server.");
    }

    private void FailJoin(string message)
    {
        joinPending = false;
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn != null)
            conn.Reducers.OnJoinWorld -= OnJoinWorldResult;
        GetSibling<TableSubscriber>()?.UnsubscribeGame();
        GetSibling<TableSubscriber>()?.SubscribeLobby();
        GetNode<Label>("Panel/JoinError").Text = message;
    }

    public override void _ExitTree()
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn != null)
            conn.Reducers.OnJoinWorld -= OnJoinWorldResult;
        base._ExitTree();
    }

    private void OnDeletePressed(PlayerProfile profile)
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn == null || GetNode<Control>("CreateProfilePanel").Visible) return;
        conn.Reducers.DeleteProfile(profile.ProfileId);
        if (profilePanels.Remove(profile.ProfileId, out Control? panel))
            panel.QueueFree();
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
