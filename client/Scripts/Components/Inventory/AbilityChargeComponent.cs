#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Linq;

/// <summary>
/// The local player's ability-charge state (P4): mirrors the server-echoed AbilityCharge
/// row delivered by the child AbilityChargeBinder (declared in local_player.tscn, signals
/// wired in the editor; a persistent table, so replay is OK). The echo is the ONLY
/// driver — no optimistic local flag — so timeout auto-releases, rejected begins, and
/// lifecycle purges (death/logout/disconnect) all converge on the right state by
/// themselves.
///
/// Observers: CombatComponent holds fire while Charging (firing is client-authoritative,
/// so the charge fire-suppression lives client-side), and this component applies the
/// charge SelfSlow factor to PositionSyncComponent cosmetically — the server clamps the
/// reported speed for real (report_movement's SelfSlow clamp).
/// </summary>
public partial class AbilityChargeComponent : Component
{
    /// Mirrors of main/global.rs — CHARGE_SELF_SLOW_FACTOR / MAX_CHARGE_SECONDS /
    /// CHARGE_MIN_RANGE_FRACTION. Both sides must change together.
    public const float ChargeSelfSlowFactor = 0.5f;
    public const float MaxChargeSeconds = 3.0f;

    /// Charge-scaled range multiplier: 25% at a 0-charge release up to 100% at full
    /// charge. Mirror of charge_range_multiplier in player/reducers.rs.
    public static float ChargeRangeMultiplier(float fraction) => 0.25f + 0.75f * Mathf.Clamp(fraction, 0f, 1f);

    /// True while the server-echoed charge row exists for the local player.
    public bool Charging { get; private set; }

    /// The charged ability head slot, or -1 while not charging.
    public int ChargedSlot { get; private set; } = -1;

    private TableBinderComponent chargeBinder = null!;

    public override void _Ready()
    {
        base._Ready();
        chargeBinder = GetNode<TableBinderComponent>("AbilityChargeBinder");
    }

    // --- TableBinderComponent signal handlers (wired in local_player.tscn) ---
    // The table is public and shared, so other players' charge rows arrive too —
    // filter to the local profile.

    private void OnChargeRow()
    {
        var row = (AbilityCharge)chargeBinder.LastRow!;
        if (row.ProfileId != LocalProfileId) return;
        Charging = true;
        ChargedSlot = (int)row.SlotIndex;
        GetSibling<PositionSyncComponent>()?.SetExternalSlow(ChargeSelfSlowFactor);
    }

    private void OnChargeRowDeleted()
    {
        var row = (AbilityCharge)chargeBinder.LastDeletedRow!;
        if (row.ProfileId != LocalProfileId) return;
        Charging = false;
        ChargedSlot = -1;
        GetSibling<PositionSyncComponent>()?.SetExternalSlow(null);
    }

    private static ulong LocalProfileId =>
        GameManager.Conn?.Db.LocalPlayer.Iter().FirstOrDefault()?.ProfileId ?? ulong.MaxValue;
}
