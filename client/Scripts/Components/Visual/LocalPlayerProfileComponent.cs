#nullable enable
using Godot;
using SpacetimeDB.Types;

/// <summary>
/// Applies the local player's active profile: LocalPlayerActiveProfile rows (delivered by
/// the child binder declared in local_player.tscn, signals wired in the editor) set the
/// profile name and swap the Sprite's SpriteFrames. AimSettingsChanged is raised on the
/// LocalPlayer so observers (CombatComponent) stay wired to the entity, not this component.
/// </summary>
public partial class LocalPlayerProfileComponent : Component
{
    public string ProfileName { get; private set; } = "";

    private TableBinderComponent profileBinder = null!;
    private AnimatedSprite2D sprite = null!;

    public override void _Ready()
    {
        base._Ready();
        profileBinder = GetNode<TableBinderComponent>("LocalPlayerActiveProfileBinder");
        sprite = GetNode<AnimatedSprite2D>("../Sprite");
    }

    // --- TableBinderComponent signal handlers (wired in local_player.tscn) ---
    // The binder has ReplayExistingRows on, so a cached profile row comes through the
    // insert path — no separate Iter() replay needed.

    private void OnProfileRowInserted()
    {
        var profile = (PlayerProfile)profileBinder.LastRow!;
        ProfileName = profile.ProfileName;
        var resPath = GameManager.GetResPath(profile.TextureId);
        if (resPath != null)
            sprite.SpriteFrames = GD.Load<SpriteFrames>(resPath);
        (Entity as LocalPlayer)?.RaiseAimSettingsChanged();
    }

    private void OnProfileRowUpdated()
    {
        // Refresh notification only — the old OnActiveProfileUpdate didn't re-apply the
        // texture on update either.
        (Entity as LocalPlayer)?.RaiseAimSettingsChanged();
    }
}
