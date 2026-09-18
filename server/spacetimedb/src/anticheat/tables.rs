use spacetimedb::{table, Identity, Timestamp};

/// Anti-cheat witness flag: a client saw another player get hit by a bullet. Carries no
/// bullet identity — just who was hit, who witnessed it, and when — so the server can
/// corroborate the victim's self-reported `report_hit` calls against independent witnesses.
/// Server-only (not `public`): clients never subscribe to flags.
#[table(accessor = player_hit_flag)]
pub struct PlayerHitFlag {
    #[primary_key] #[auto_inc]
    pub flag_id: u64,
    #[index(btree)]
    pub hit_player: Identity,
    pub reported_by: Identity,
    pub reported_at: Timestamp,
}

/// Server-only move-validation strike record (Phase 1 — BitCraft
/// `move_validation_helpers::validation_strike` pattern, translated).
/// One row per profile; failures accumulate as timestamps within STRIKE_WINDOW_SECS.
/// Reports are clamped (not rejected) until strikes exceed STRIKES_BEFORE_REJECT,
/// then rejected + resync scheduled (BitCraft's `reset_mobile_entity_timer` =
/// our `teleport_player` restamp).
#[table(accessor = move_validation_strikes)]
pub struct MoveValidationStrikes {
    #[primary_key]
    pub profile_id: u64,
    pub failure_timestamps: Vec<Timestamp>,
}
