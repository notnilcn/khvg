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
