#nullable enable
using Godot;
using System;
using System.Collections.Generic;

public class CharacterModel3D : IDisposable
{
    public Node3D Node;
    private bool _disposed;

    // Clips are grafted in from library scenes (the server's AnimationEntry rows) into the
    // model's AnimationPlayer under their animation_id, so any character sharing the rig can
    // play them. Loaded library scenes are shared across all models/swaps — static content.
    private static readonly Dictionary<string, PackedScene?> librarySceneCache = new();
    private AnimationPlayer? animPlayer;
    private string currentAnimationId = "";

    public CharacterModel3D(PackedScene scene, Vector2 pos2D, Node3D parent, float scale = 50f, float groundY = 0f)
    {
        Node = scene.Instantiate<Node3D>();
        Node.Scale = Vector3.One * scale;
        parent.AddChild(Node);
        Node.GlobalPosition = new Vector3(pos2D.X, groundY, pos2D.Y);
        animPlayer = FindDescendant<AnimationPlayer>(Node);
        if (animPlayer == null)
        {
            animPlayer = new AnimationPlayer { Name = "AnimationPlayer" };
            Node.AddChild(animPlayer);
        }
        if (PixelArtPipelineComponent.Instance is { PixelizeCharacters: true } pipeline)
            pipeline.PixelizeObject(Node);
    }

    public void SyncFrom2D(Vector2 pos, float yaw2D, float groundY = 0f)
    {
        if (_disposed) return;
        Node.GlobalPosition = new Vector3(pos.X, groundY, pos.Y);
        // 2D rotation is clockwise from screen-up; 3D Y-rotation is counter-clockwise from +Z.
        Node.Rotation = new Vector3(0f, Mathf.Pi - yaw2D, 0f);
    }

    /// Pulls clipName out of the library scene at resPath and adds it to this model's
    /// AnimationPlayer under animationId (track paths remapped onto this model's skeleton).
    /// No-op when the id is already grafted or the source has no such clip.
    public void AddAnimationClip(string animationId, string resPath, string clipName)
    {
        if (_disposed || animPlayer == null || animPlayer.HasAnimation(animationId)) return;
        if (!librarySceneCache.TryGetValue(resPath, out var srcScene))
        {
            // Exists() first: a missing path (e.g. moved asset packs) is one quiet warning
            // per library instead of a ResourceLoader engine error per clip.
            if (!ResourceLoader.Exists(resPath))
            {
                GD.PushWarning($"CharacterModel3D: animation library not found: {resPath}");
                librarySceneCache[resPath] = null;
                return;
            }
            srcScene = GD.Load<PackedScene>(resPath);
            if (srcScene == null) { GD.PushWarning($"CharacterModel3D: cannot load animation library {resPath}"); return; }
            librarySceneCache[resPath] = srcScene;
        }
        if (srcScene == null) return;
        var srcRoot = srcScene.Instantiate();
        var srcPlayer = FindDescendant<AnimationPlayer>(srcRoot);
        var clip = srcPlayer?.GetAnimation(clipName);
        if (srcPlayer == null || clip == null)
        {
            GD.PushWarning($"CharacterModel3D: clip {clipName} not found in {resPath}");
            srcRoot.Free();
            return;
        }
        var copy = (Animation)clip.Duplicate();
        RemapTracksToOwnSkeleton(copy, srcPlayer, srcRoot);
        // Animation names can't contain '/' — that's the AnimationPlayer's library-namespace
        // separator. "ual1/Walk" therefore plays as clip "Walk" inside library "ual1".
        string libraryName = "", key = animationId;
        int slash = animationId.IndexOf('/');
        if (slash >= 0)
        {
            libraryName = animationId[..slash];
            key = animationId[(slash + 1)..];
        }
        // HasAnimationLibrary guard: GetAnimationLibrary on a missing name logs an
        // engine error per call (×86 clips on every model swap).
        var library = animPlayer.HasAnimationLibrary(libraryName) ? animPlayer.GetAnimationLibrary(libraryName) : null;
        if (library == null)
        {
            library = new AnimationLibrary();
            animPlayer.AddAnimationLibrary(libraryName, library);
        }
        library.AddAnimation(key, copy);
        srcRoot.Free();
    }

    public bool HasAnimation(string animationId) => animPlayer?.HasAnimation(animationId) ?? false;

    /// Plays the clip; no-op while the same clip is still playing (finished one-shots replay,
    /// so holding fire retriggers e.g. Pistol_Shoot after each run-through).
    public void PlayAnimation(string animationId)
    {
        if (_disposed || animPlayer == null || string.IsNullOrEmpty(animationId)) return;
        if (!animPlayer.HasAnimation(animationId)) return;
        if (currentAnimationId == animationId && animPlayer.IsPlaying()) return;
        currentAnimationId = animationId;
        animPlayer.Play(animationId);
    }

    public float GetAnimationLength(string animationId) =>
        animPlayer != null && animPlayer.HasAnimation(animationId) ? (float)animPlayer.GetAnimation(animationId).Length : 0f;

    // Bone tracks in imported glTF/FBX scenes address the skeleton by node path
    // ("Armature/Skeleton3D:pelvis") relative to the AnimationPlayer's RootNode. Library and
    // character share the Universal rig, so usually only the prefix differs (if at all).
    private void RemapTracksToOwnSkeleton(Animation anim, AnimationPlayer srcPlayer, Node srcRoot)
    {
        var srcSkeleton = FindDescendant<Skeleton3D>(srcRoot);
        var dstSkeleton = FindDescendant<Skeleton3D>(Node);
        if (srcSkeleton == null || dstSkeleton == null) return;
        var srcAnchor = srcPlayer.GetNodeOrNull(srcPlayer.RootNode) ?? srcRoot;
        var dstAnchor = animPlayer!.GetNodeOrNull(animPlayer.RootNode) ?? Node;
        var srcPrefix = srcAnchor.GetPathTo(srcSkeleton).ToString();
        var dstPrefix = dstAnchor.GetPathTo(dstSkeleton).ToString();
        if (srcPrefix == dstPrefix) return;
        for (int i = 0; i < anim.GetTrackCount(); i++)
        {
            var path = anim.TrackGetPath(i).ToString();
            int colon = path.IndexOf(':');
            var nodePart = colon >= 0 ? path[..colon] : path;
            if (nodePart == srcPrefix || nodePart.StartsWith(srcPrefix + "/", StringComparison.Ordinal))
                anim.TrackSetPath(i, new NodePath(dstPrefix + path[srcPrefix.Length..]));
        }
    }

    private static T? FindDescendant<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(Node))
            Node.QueueFree();
    }
}
