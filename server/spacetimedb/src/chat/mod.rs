//! Chat + moderation (Phase 5 — BitCraft `global_module` channel system pattern,
//! scoped to one module).
//!
//! BitCraft: channels + messages + reports + GM moderation + a cleanup agent.
//! Target: same shape, single module. Rate-limit reuses the `PlayerHitRate` 1 s-window
//! pattern (own window here to avoid coupling); length cap mirrors
//! `user_text_input_helpers`; moderation reuses `is_admin`.

use spacetimedb::{table, Query, ReducerContext, Table, Timestamp, ViewContext};
use crate::main::global::*;
use crate::player::methods::{is_admin, require_in_world};
use crate::player::tables::logged_in_player__view;

#[table(accessor = chat_channel, public)]
pub struct ChatChannel {
    #[primary_key] #[auto_inc]
    pub channel_id: u64,
    pub name: String,
    pub owner_profile_id: u64,
    pub members: Vec<u64>,
    pub banned: Vec<u64>,
}

/// Membership mirror (one row per channel+profile): the `local_chat_messages` view
/// joins messages to this table because the query builder has no `IN` / `contains`
/// over the `members` vec. Maintained by the same reducers that edit `members`.
#[table(accessor = chat_channel_member, index(accessor = by_member_channel, btree(columns = [channel_id, profile_id])))]
pub struct ChatChannelMember {
    #[primary_key] #[auto_inc]
    pub id: u64,
    #[index(btree)]
    pub channel_id: u64,
    #[index(btree)]
    pub profile_id: u64,
}

#[table(accessor = chat_message, public)]
pub struct ChatMessage {
    #[primary_key] #[auto_inc]
    pub message_id: u64,
    #[index(btree)]
    pub channel_id: u64,
    pub sender_profile_id: u64,
    pub text: String,
    pub sent_at: Timestamp,
}

/// Server-only abuse reports (never subscribed by clients; admins read via logs/SQL).
#[table(accessor = chat_report)]
pub struct ChatReport {
    #[primary_key] #[auto_inc]
    pub report_id: u64,
    #[index(btree)]
    pub message_id: u64,
    pub reporter_profile_id: u64,
    pub reason: String,
    pub reported_at: Timestamp,
}

/// Server-only per-profile chat rate window.
#[table(accessor = chat_rate)]
pub struct ChatRate {
    #[primary_key]
    pub profile_id: u64,
    pub window_start: Timestamp,
    pub posts: u32,
}

fn chat_rate_allow(ctx: &ReducerContext, profile_id: u64) -> bool {
    let existing = ctx.db.chat_rate().profile_id().find(&profile_id);
    let exists = existing.is_some();
    let mut rate = existing.unwrap_or(ChatRate { profile_id, window_start: ctx.timestamp, posts: 0 });
    if crate::main::time::seconds_since(ctx, rate.window_start) >= CHAT_RATE_WINDOW_SECS {
        rate.window_start = ctx.timestamp;
        rate.posts = 0;
    }
    let allowed = rate.posts < CHAT_RATE_MAX_POSTS;
    if allowed {
        rate.posts += 1;
    }
    if exists {
        ctx.db.chat_rate().profile_id().update(rate);
    } else {
        ctx.db.chat_rate().insert(rate);
    }
    allowed
}

fn check_text(text: &str) -> Result<(), String> {
    let trimmed = text.trim();
    if trimmed.is_empty() {
        return Err("Message cannot be empty.".to_string());
    }
    if trimmed.len() > CHAT_MAX_MESSAGE_LEN {
        return Err(format!("Message too long (max {} chars).", CHAT_MAX_MESSAGE_LEN));
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn chat_create(ctx: &ReducerContext, name: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let name = name.trim().to_string();
    if name.is_empty() || name.len() > 32 {
        return Err("Channel name must be 1-32 characters.".to_string());
    }
    let row = ctx.db.chat_channel().insert(ChatChannel {
        channel_id: 0, name, owner_profile_id: player.profile_id,
        members: vec![player.profile_id], banned: vec![],
    });
    ctx.db.chat_channel_member().insert(ChatChannelMember {
        id: 0, channel_id: row.channel_id, profile_id: player.profile_id,
    });
    log::info!("chat_create: profile {} created channel {}", player.profile_id, row.channel_id);
    Ok(())
}

#[spacetimedb::reducer]
pub fn chat_join(ctx: &ReducerContext, channel_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.chat_channel().channel_id().find(&channel_id).ok_or("Channel not found.")?;
    if row.banned.contains(&player.profile_id) {
        return Err("You are banned from this channel.".to_string());
    }
    if !row.members.contains(&player.profile_id) {
        let mut row = row;
        row.members.push(player.profile_id);
        ctx.db.chat_channel().channel_id().update(row);
        ctx.db.chat_channel_member().insert(ChatChannelMember {
            id: 0, channel_id, profile_id: player.profile_id,
        });
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn chat_leave(ctx: &ReducerContext, channel_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.chat_channel().channel_id().find(&channel_id).ok_or("Channel not found.")?;
    if row.members.contains(&player.profile_id) {
        let mut row = row;
        row.members.retain(|m| *m != player.profile_id);
        ctx.db.chat_channel().channel_id().update(row);
        for m in ctx.db.chat_channel_member().by_member_channel().filter((channel_id, player.profile_id)).collect::<Vec<_>>() {
            ctx.db.chat_channel_member().id().delete(&m.id);
        }
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn chat_invite(ctx: &ReducerContext, channel_id: u64, member_profile_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.chat_channel().channel_id().find(&channel_id).ok_or("Channel not found.")?;
    if row.owner_profile_id != player.profile_id && !is_admin(ctx) {
        return Err("Only the channel owner can invite.".to_string());
    }
    if row.banned.contains(&member_profile_id) {
        return Err("That player is banned from this channel.".to_string());
    }
    if !row.members.contains(&member_profile_id) {
        let mut row = row;
        row.members.push(member_profile_id);
        ctx.db.chat_channel().channel_id().update(row);
        ctx.db.chat_channel_member().insert(ChatChannelMember {
            id: 0, channel_id, profile_id: member_profile_id,
        });
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn chat_ban(ctx: &ReducerContext, channel_id: u64, member_profile_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.chat_channel().channel_id().find(&channel_id).ok_or("Channel not found.")?;
    if row.owner_profile_id != player.profile_id && !is_admin(ctx) {
        return Err("Only the channel owner can ban.".to_string());
    }
    let mut row = row;
    row.members.retain(|m| *m != member_profile_id);
    if !row.banned.contains(&member_profile_id) {
        row.banned.push(member_profile_id);
    }
    ctx.db.chat_channel().channel_id().update(row);
    for m in ctx.db.chat_channel_member().by_member_channel().filter((channel_id, member_profile_id)).collect::<Vec<_>>() {
        ctx.db.chat_channel_member().id().delete(&m.id);
    }
    Ok(())
}

#[spacetimedb::reducer]
pub fn chat_unban(ctx: &ReducerContext, channel_id: u64, member_profile_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.chat_channel().channel_id().find(&channel_id).ok_or("Channel not found.")?;
    if row.owner_profile_id != player.profile_id && !is_admin(ctx) {
        return Err("Only the channel owner can unban.".to_string());
    }
    let mut row = row;
    row.banned.retain(|m| *m != member_profile_id);
    ctx.db.chat_channel().channel_id().update(row);
    Ok(())
}

#[spacetimedb::reducer]
pub fn chat_post(ctx: &ReducerContext, channel_id: u64, text: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let row = ctx.db.chat_channel().channel_id().find(&channel_id).ok_or("Channel not found.")?;
    if !row.members.contains(&player.profile_id) && !is_admin(ctx) {
        return Err("You are not a member of this channel.".to_string());
    }
    if row.banned.contains(&player.profile_id) {
        return Err("You are banned from this channel.".to_string());
    }
    check_text(&text)?;
    if !chat_rate_allow(ctx, player.profile_id) {
        return Err("Chatting too fast — slow down.".to_string());
    }
    ctx.db.chat_message().insert(ChatMessage {
        message_id: 0, channel_id, sender_profile_id: player.profile_id,
        text: text.trim().to_string(), sent_at: ctx.timestamp,
    });
    Ok(())
}

#[spacetimedb::reducer]
pub fn report_chat_message(ctx: &ReducerContext, message_id: u64, reason: String) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    ctx.db.chat_message().message_id().find(&message_id).ok_or("Message not found.")?;
    if reason.trim().is_empty() || reason.len() > 200 {
        return Err("Reason must be 1-200 characters.".to_string());
    }
    ctx.db.chat_report().insert(ChatReport {
        report_id: 0, message_id, reporter_profile_id: player.profile_id,
        reason: reason.trim().to_string(), reported_at: ctx.timestamp,
    });
    Ok(())
}

/// Admin moderation: delete a message (mute/ban escalation stays with the existing
/// admin slot + channel bans; global bans are out of scope for this cut).
#[spacetimedb::reducer]
pub fn moderate_delete_message(ctx: &ReducerContext, message_id: u64) -> Result<(), String> {
    if !is_admin(ctx) {
        return Err("Admin only.".to_string());
    }
    ctx.db.chat_message().message_id().find(&message_id).ok_or("Message not found.")?;
    ctx.db.chat_message().message_id().delete(&message_id);
    Ok(())
}

/// Cleanup agent (BitCraft `chat_cleanup_agent` mirror): delete messages older than
/// CHAT_RETENTION_HOURS. Called from the cleanup pass. Bounded scan.
pub fn tick_chat_cleanup(ctx: &ReducerContext) {
    let now_micros = ctx.timestamp.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
    let retention_micros = (CHAT_RETENTION_HOURS * 3600.0 * 1_000_000.0) as u128;
    for msg in ctx.db.chat_message().iter().collect::<Vec<_>>() {
        let then = msg.sent_at.to_duration_since_unix_epoch().unwrap_or_default().as_micros();
        if now_micros.saturating_sub(then) >= retention_micros {
            ctx.db.chat_message().message_id().delete(&msg.message_id);
        }
    }
}

#[spacetimedb::view(accessor = all_chat_channels, public)]
fn all_chat_channels(_ctx: &ViewContext) -> impl Query<ChatChannel> {
    _ctx.from.chat_channel().build()
}

#[spacetimedb::view(accessor = local_chat_messages, public)]
fn local_chat_messages(ctx: &ViewContext) -> impl Query<ChatMessage> {
    // Only messages in channels the caller belongs to, via the membership mirror
    // table (the builder has no IN / vec-contains, so the join carries membership).
    // Full-table replay is deliberately avoided — clients see member channels only.
    let empty = ctx.from.chat_message().r#where(|m| m.message_id.eq(u64::MAX)).build();
    let profile_id = match ctx.db.logged_in_player().player_id().find(ctx.sender()) {
        Some(p) => p.profile_id,
        None => return empty,
    };
    ctx.from.chat_channel_member()
        .r#where(move |mem| mem.profile_id.eq(profile_id))
        .right_semijoin(ctx.from.chat_message(), |mem, msg| mem.channel_id.eq(msg.channel_id))
        .build()
}
