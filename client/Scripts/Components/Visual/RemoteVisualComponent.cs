#nullable enable
using Godot;

/// <summary>
/// The RemotePlayer's sprite: loads the SpriteFrames for the server-supplied profile
/// texture id and plays Walk/Idle from the sibling InterpolationComponent's movement
/// state (logic moved out of RemotePlayer.cs).
/// </summary>
public partial class RemoteVisualComponent : VisualComponent
{
    private InterpolationComponent? interpolation;

    protected override void OnEntityReady()
    {
        interpolation = GetSibling<InterpolationComponent>();
    }

    /// Resolves the texture id through the GameManager texture catalog and applies the SpriteFrames.
    public void SetTexture(string textureId)
    {
        var resPath = GameManager.GetResPath(textureId);
        if (resPath != null)
            SpriteFrames = GD.Load<SpriteFrames>(resPath);
    }

    public override void _Process(double delta)
    {
        if (SpriteFrames == null) return;
        Play(interpolation?.Moving == true ? "Walk" : "Idle");
    }
}
