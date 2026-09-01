#nullable enable
using Godot;

/// <summary>
/// Single home for every client-side mirror of a server constant from
/// server/spacetimedb/src/main/global.rs. The server is authoritative — these copies
/// exist so the client can pre-check input and optimistically render what the echo
/// will do. Each member names its server counterpart; no codegen, this file is the
/// one place to grep — both sides must change together.
/// </summary>
public static class ServerConstants
{
    /// Mirror of ABILITY_POSITION_MULTIPLIERS — per-ability-slot potency scale
    /// ("first ability stronger"), indexed by (head slot − AbilitySlotStart).
    public static readonly float[] AbilityPositionMultipliers = [1.5f, 1.25f, 1.1f, 1.0f, 0.9f, 0.8f];

    /// Mirror of MAX_ABILITY_TARGET_RANGE — the server clamps the ability cursor to
    /// this range around the player's position row; optimistic visuals clamp
    /// identically so they land where the echo will.
    public const float MaxAbilityTargetRange = 600f;

    /// Mirror of CHARGE_SELF_SLOW_FACTOR — cosmetic move-speed multiplier while an
    /// ability charge is held (the server clamps for real via the SelfSlow status).
    public const float ChargeSelfSlowFactor = 0.5f;

    /// Mirror of MAX_CHARGE_SECONDS — how long a charge can be held before the 1 Hz
    /// status tick auto-releases it.
    public const float MaxChargeSeconds = 3.0f;

    /// Mirror of CHARGE_MIN_RANGE_FRACTION — range floor of a charge-scaled release
    /// (a 0-charge release still goes 25% of the range).
    public const float ChargeMinRangeFraction = 0.25f;

    /// Charge-scaled range multiplier: ChargeMinRangeFraction at a 0-charge release up
    /// to 100% at full charge. Mirror of charge_range_multiplier in player/reducers.rs,
    /// built from CHARGE_MIN_RANGE_FRACTION in main/global.rs.
    public static float ChargeRangeMultiplier(float fraction) =>
        ChargeMinRangeFraction + (1f - ChargeMinRangeFraction) * Mathf.Clamp(fraction, 0f, 1f);

    /// Mirror of ELEVATION_LEVELS — the number of discrete per-hex elevation levels
    /// world generation stamps onto BuildingTile rows (terrain tint range).
    public const int ElevationLevels = 8;

    /// Mirror of FALL_SECONDS_PER_LEVEL — input-lockout seconds per level dropped when
    /// walking off a cliff; the server enforces the same lockout via falling_until and
    /// applies FALL_DAMAGE_PER_LEVEL on the landing report (damage stays server-side).
    public const float FallSecondsPerLevel = 0.35f;

    /// Mirror of MAX_WALKABLE_UP_DELTA — a step up of at most this many levels is
    /// walked normally; anything steeper is blocked (client predicts the slide, the
    /// server rejects the position claim).
    public const int MaxWalkableUpDelta = 1;

    /// Mirror of MAX_WALKABLE_DOWN_DELTA — a step down of at most this many levels is
    /// walked normally; anything steeper is a fall.
    public const int MaxWalkableDownDelta = 1;

    /// Mirror of FALL_SAFE_DROP — fall damage applies only when the drop exceeds this
    /// many levels (a drop of exactly this is still a fall: tween + lockout, no damage).
    /// The client never computes damage; mirrored here for completeness of the fall rule.
    public const int FallSafeDrop = 2;

    /// Mirror of BULLET_ELEVATION_CLIMB — a bullet survives entering a hex up to this
    /// far above its origin band and can hit entities there.
    public const int BulletElevationClimb = 1;

    /// Mirror of BULLET_ELEVATION_DROP — a bullet can hit entities this far below its
    /// origin band (flying down over lower ground).
    public const int BulletElevationDrop = 2;
}
