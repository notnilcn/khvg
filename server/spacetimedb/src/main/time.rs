//! Shared wall-clock helper: seconds between a stored timestamp and now. Lives here
//! (not in `enemy/methods.rs`, where it used to sit) because callers span player, chest,
//! status, and enemy — a clock is not enemy-domain.

use spacetimedb::{ReducerContext, Timestamp};

pub fn seconds_since(ctx: &ReducerContext, since: Timestamp) -> f32 {
    let now = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    let then = since.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    now.saturating_sub(then) as f32 / 1_000_000.0
}
