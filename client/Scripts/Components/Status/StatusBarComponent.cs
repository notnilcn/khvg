#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The local player's active status effects (the status/ module's ActiveStatusEffect
/// rows) as a horizontal strip of "Kind N.Ns" labels above the hotbar, declared in
/// local_player.tscn. Fed by the child ActiveStatusEffectBinder with ReplayExistingRows
/// on — a PERSISTENT table, so replay shows still-live statuses on scene entry (the
/// GroundZone rule, not the event-table rule). The raw public table carries every
/// player's rows, so handlers filter to the local profile (the AbilityChargeComponent
/// pattern). One label per StatusEffectKind — the server keeps one row per (profile,
/// kind). Labels count down locally between the server's 1 Hz row updates; the row's
/// own insert/update/delete is the source of truth.
/// </summary>
public partial class StatusBarComponent : ControlComponent
{
    private sealed class Tracked
    {
        public float Remaining;
        public double StampedAt; // seconds (Time.GetTicksMsec) when the row last arrived
        public Label Label = null!;
    }

    private readonly Dictionary<StatusEffectKind, Tracked> effects = new();
    private HBoxContainer row = null!;
    private TableBinderComponent statusBinder = null!;

    public override void _Ready()
    {
        base._Ready();
        row = GetNode<HBoxContainer>("Row");
        statusBinder = GetNode<TableBinderComponent>("ActiveStatusEffectBinder");
    }

    // --- ActiveStatusEffectBinder signal handlers (wired in local_player.tscn) ---

    private void OnStatusRow()
    {
        var status = (ActiveStatusEffect)statusBinder.LastRow!;
        if (status.ProfileId != LocalProfileId) return;
        if (!effects.TryGetValue(status.Kind, out var tracked))
        {
            tracked = new Tracked { Label = MakeLabel() };
            effects[status.Kind] = tracked;
            row.AddChild(tracked.Label);
        }
        tracked.Remaining = status.Remaining;
        tracked.StampedAt = Time.GetTicksMsec() / 1000.0;
    }

    private void OnStatusRowDeleted()
    {
        var status = (ActiveStatusEffect)statusBinder.LastDeletedRow!;
        if (status.ProfileId != LocalProfileId) return;
        Remove(status.Kind);
    }

    public override void _Process(double delta)
    {
        if (effects.Count == 0) return;
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var kind in effects.Keys.ToList())
        {
            var tracked = effects[kind];
            float shown = tracked.Remaining - (float)(now - tracked.StampedAt);
            if (shown <= 0f)
                Remove(kind); // cosmetic only — the server's row delete confirms it
            else
                tracked.Label.Text = $"{ShortName(kind)} {shown:0.0}s";
        }
    }

    private void Remove(StatusEffectKind kind)
    {
        if (!effects.Remove(kind, out var tracked)) return;
        if (GodotObject.IsInstanceValid(tracked.Label))
            tracked.Label.QueueFree();
    }

    private static Label MakeLabel()
    {
        var label = new Label { Uppercase = true };
        label.AddThemeFontSizeOverride("font_size", 11);
        return label;
    }

    private static string ShortName(StatusEffectKind kind) => kind switch
    {
        StatusEffectKind.DamageToBleed => "Bleed",
        StatusEffectKind.StoreDamage => "Store",
        StatusEffectKind.Invulnerable => "Invuln",
        StatusEffectKind.DamageShield => "Shield",
        StatusEffectKind.DamageReductionPct => "Reduce",
        StatusEffectKind.DamageDealtMult => "Empower",
        StatusEffectKind.HpFloor => "Floor",
        StatusEffectKind.DebuffImmune => "Ward",
        StatusEffectKind.RedirectToCaster => "Redirect",
        StatusEffectKind.Regen => "Regen",
        StatusEffectKind.StoreIncomingSmite => "Smite",
        StatusEffectKind.StoreIncomingBash => "Retrib",
        StatusEffectKind.SelfSlow => "Charge",
        _ => kind.ToString(),
    };

    private static ulong LocalProfileId =>
        GameManager.Conn?.Db.LocalPlayer.Iter().FirstOrDefault()?.ProfileId ?? ulong.MaxValue;
}
