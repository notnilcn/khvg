#nullable enable
using Godot;
using System;

/// Launch-argument overrides for automated runs: --2D/--3D auto-presses the main menu's
/// 2D/3D button (LobbyGui) and --charN auto-presses the Join button of the Nth character
/// profile (LobbyComponent; the last profile is picked when fewer than N exist).
/// Mirrors the DatabaseConnector --pN scan: editor-run args land in GetCmdlineArgs,
/// exported builds pass their own args after "--" in GetCmdlineUserArgs.
public static class LaunchArgs
{
    public static VisualMode? ForcedMode { get; private set; }

    /// 1-based profile index from --charN (e.g. --char1 → 1), null when absent.
    public static int? CharIndex { get; private set; }

    static LaunchArgs()
    {
        foreach (var arg in OS.GetCmdlineArgs()) Parse(arg);
        foreach (var arg in OS.GetCmdlineUserArgs()) Parse(arg);
    }

    private static void Parse(string arg)
    {
        if (arg.Equals("--2D", StringComparison.OrdinalIgnoreCase)) ForcedMode = VisualMode.TwoD;
        else if (arg.Equals("--3D", StringComparison.OrdinalIgnoreCase)) ForcedMode = VisualMode.ThreeD;
        else if (TryParseCharIndex(arg, out int index)) CharIndex = index;
    }

    // Whole-arg match on the digits (the DatabaseConnector --pN guard), so a prefix like
    // "--character" never counts.
    private static bool TryParseCharIndex(string arg, out int index)
    {
        index = 0;
        const string prefix = "--char";
        if (arg.Length <= prefix.Length || !arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        for (int i = prefix.Length; i < arg.Length; i++)
            if (!char.IsDigit(arg[i])) return false;
        index = int.Parse(arg.Substring(prefix.Length));
        return index >= 1;
    }
}
