# 05 Chat

Channels, membership mirror, messages, reports, rate windows, retention sweep.

## 1. Assumed knowledge

Read [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] (the `# Chat` section holds this doc's five table entries, `^table-chat-channel` through `^table-chat-rate`), [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] (conventions, reading order), [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] (reducers-are-transactional, the `BaseTables`/`LobbyTables`/`GameTables` subscription waves, the one-binder-per-table pattern), and [[docs/Technical Design Docs/Description/12 Player Tables.md|12 Player]] (`LoggedInPlayer` as the in-world proof behind every chat reducer's `require_in_world` gate). Maintainer jump-off points, not authority: [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]]. A *reducer* is a server function a client calls by name that runs transactionally and returns no data — clients learn outcomes only through table/subscription updates. A *view* is a named parameterized server query the client subscribes to instead of raw tables (a `.tscn` file is Godot's scene format, the declarative node tree the game actually instantiates).

## 2. The 30-second version

Chat owns five tables in [[server/spacetimedb/src/chat/mod.rs|chat/mod.rs]]: `ChatChannel` (the channel roster with `members`/`banned` id lists), `ChatChannelMember` (a server-only membership mirror the message view joins through), `ChatMessage` (one row per post), `ChatReport` (server-only abuse reports), and `ChatRate` (server-only per-profile post windows). All writes come from nine small reducers in the same file — eight gated on being in the world plus admin-only moderation (which needs only the admin flag, so it works from the lobby too) — with owner-or-admin checks on invite/ban/unban; old messages die two ways (admin delete, 24-hour retention sweep from the agent cleanup pass). Reads are the anomaly of this system: both public views (`all_chat_channels`, `local_chat_messages`) are subscribed in the `GameTables` wave, but no binder consumes them and no UI calls the reducers — the whole system is server-complete and client-unwired, so every ***Client*** subsection below records "no visible end" explicitly.

## 3. Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do I create / join / leave a channel? | ChatChannel | [[#ChatChannel\|ChatChannel]] — Changers (create/join/leave/invite/ban/unban) |
| Why is there a second membership table? | ChatChannelMember | [[#ChatChannelMember\|ChatChannelMember]] — Shape (the no-`IN` workaround) |
| How do I post, and what can reject my post? | ChatMessage | [[#ChatMessage\|ChatMessage]] — Changers (the five gates) |
| How do I report an abusive message? | ChatReport | [[#ChatReport\|ChatReport]] — Changers |
| Why does posting say "chatting too fast"? | ChatRate | [[#ChatRate\|ChatRate]] — Changers (5-posts-per-5-seconds window) |
| Where do old messages go? | ChatMessage | [[#ChatMessage\|ChatMessage]] — Changers (retention sweep) |
| Who can delete a message / ban from a channel? | ChatMessage / ChatChannel | [[#ChatMessage\|ChatMessage]] (admin delete), [[#ChatChannel\|ChatChannel]] (owner-or-admin ban) |
| How do chat rows reach the client (wave, binder, UI)? | ChatChannel / ChatMessage | [[#ChatChannel\|ChatChannel]], [[#ChatMessage\|ChatMessage]] — Readers ***Client*** (subscribed-but-unwired, no visible end) |
| What must be in the world for any chat reducer to run? | `LoggedInPlayer` in ^table-logged-in-player | [[docs/Technical Design Docs/Description/12 Player Tables.md\|12 Player]] (`require_in_world` gate) |
| How do subscription waves and binders work in general? | — | [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md\|03 Connection, Subscriptions & Views]] |
| What runs the retention sweep, and how often? | ChatMessage | [[#ChatMessage\|ChatMessage]] — Changers (`tick_cleanup` in [[server/spacetimedb/src/main/agents.rs#tick_cleanup\|agents]]) |

## 4. Flowcharts

- [[flowcharts/main-chat.canvas]] — the composed chat flow (composes from the `chat` flow in `flowcharts/flows.json`; currently unresolved until the phase-2 recompose runs).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/chat_subfolder/chat_subfolder.canvas]] — server-side deep dive (tables, reducers, views).
- [[flowcharts/Subflowcharts/client_subfolder/sstdbsdk_subfolder/sstdbsdk_subfolder.canvas]] — client-side deep dive (the `GameTables` wave that carries the two chat views).

## 6. Tables

## ChatChannel

```sync
![[00 Table Map#^table-chat-channel{seamless:true,title:false,marker:01.}]]
```

#### Shape

One row per channel in [[server/spacetimedb/src/chat/mod.rs#ChatChannel|definition]]: auto-increment `channel_id` primary key, free-text `name`, `owner_profile_id` (the creator's profile id — the entity key joining every per-character row, see `^table-player-profile`), and two parallel id lists, `members` and `banned`, both `Vec<u64>` of profile ids. The table is `public` so views can expose it, but membership lives denormalized on the row itself because SpacetimeDB's query builder has no `IN`/`contains` over a vec — which is exactly why the mirror table below exists.

#### Changers

Insert happens only in [[server/spacetimedb/src/chat/mod.rs#chat_create|chat_create]], which requires the caller to be in the world ([[server/spacetimedb/src/player/methods.rs#require_in_world|require_in_world]] resolves `ctx.sender()` — the authenticated caller identity, a method call, never a passed-in argument — against `LoggedInPlayer`), trims the name and rejects empty/over-32-char names, then inserts the channel with the creator as owner and sole member and inserts the matching mirror row in the same transaction. Updates rewrite the `members`/`banned` vecs through five reducers, each loading the row by `channel_id` and failing closed on unknown channels: [[server/spacetimedb/src/chat/mod.rs#chat_join|chat_join]] pushes the caller unless banned (idempotent — already-members skip the write) and mirrors the grant; [[server/spacetimedb/src/chat/mod.rs#chat_leave|chat_leave]] retains the caller out and deletes the mirror rows via the `by_member_channel` composite index; [[server/spacetimedb/src/chat/mod.rs#chat_invite|chat_invite]] lets only the owner (or an admin via [[server/spacetimedb/src/player/methods.rs#is_admin|is_admin]], which checks both login tables, so lobby admins qualify) add a third profile, refusing banned ids; [[server/spacetimedb/src/chat/mod.rs#chat_ban|chat_ban]] (owner-or-admin) retains the target out of `members`, pushes onto `banned` if absent, and deletes the mirror rows; [[server/spacetimedb/src/chat/mod.rs#chat_unban|chat_unban]] (owner-or-admin) retains the target out of `banned` and touches nothing else — unbanning never re-grants membership. Delete never happens: no reducer deletes a `ChatChannel` row, so empty and ownerless channels persist ([[#9. Known gaps / stubs|Known gaps]]).

#### Readers

***Client***: via the [[server/spacetimedb/src/chat/mod.rs#all_chat_channels|all_chat_channels]] global view → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave (both names sit in that list in [[client/sstdbsdk/TableSubscriber.cs#TableSubscriber|TableSubscriber]], whose node lives inline in [[client/Scenes/game.tscn]]) → subscribed but no binder consumes it — a grep over `client/Scripts` finds `AllChatChannels` only in the generated `module_bindings/`, and a grep over `client/Scenes` finds no chat binder or channel UI at all — so **no visible end** (no channel picker exists). ***Server***: the membership/ban gates in [[server/spacetimedb/src/chat/mod.rs#chat_post|chat_post]] read the row per post.

## ChatChannelMember

```sync
![[00 Table Map#^table-chat-channel-member{seamless:true,title:false,marker:02.}]]
```

#### Shape

The membership mirror in [[server/spacetimedb/src/chat/mod.rs#ChatChannelMember|definition]]: one row per channel+profile (`channel_id`, `profile_id`), auto-increment `id`, a composite btree index `by_member_channel` over `(channel_id, profile_id)` for the exact-match deletes plus single-column btree indexes on each id. It exists for one reason, stated on the table: the `local_chat_messages` view joins messages to membership because the query builder cannot express `members.contains(profile)`. It is deliberately not `public`, so it never crosses the wire.

#### Changers

Every grant path that pushes onto `ChatChannel.members` inserts here in the same reducer (`chat_create`, `chat_join`, `chat_invite`); every removal path that retains out of `members` deletes here (`chat_leave`, `chat_ban`, both collecting through `by_member_channel` first because deletes go through an index). `chat_unban` never touches it (unban restores no membership), and no path ever updates a row — insert/delete only.

#### Readers

***Client***: server-only (not `public`) — never subscribed, **no visible end**. ***Server***: the [[server/spacetimedb/src/chat/mod.rs#local_chat_messages|local_chat_messages]] view filters the caller's mirror rows and right-semijoins messages through them, which is the only read of this table in the codebase.

## ChatMessage

```sync
![[00 Table Map#^table-chat-message{seamless:true,title:false,marker:03.}]]
```

#### Shape

One row per posted message in [[server/spacetimedb/src/chat/mod.rs#ChatMessage|definition]]: auto-increment `message_id`, btree-indexed `channel_id`, `sender_profile_id`, trimmed `text`, `sent_at` timestamp. `public`, because the per-caller view exposes it.

#### Changers

Insert only in [[server/spacetimedb/src/chat/mod.rs#chat_post|chat_post]], which stacks five gates before writing: in-world caller, channel exists, caller is a member *or* an admin (admins bypass membership but not the next gate), caller is not banned (this check runs after the admin bypass, so a banned admin is still rejected), then [[server/spacetimedb/src/chat/mod.rs#check_text|check_text]] rejects empty text and text over [[server/spacetimedb/src/main/global.rs#CHAT_MAX_MESSAGE_LEN|CHAT_MAX_MESSAGE_LEN]] (280 chars), then [[server/spacetimedb/src/chat/mod.rs#chat_rate_allow|chat_rate_allow]] rejects with "Chatting too fast — slow down." Delete has two paths and no other: admin [[server/spacetimedb/src/chat/mod.rs#moderate_delete_message|moderate_delete_message]] deletes one row by id (gated only on `is_admin`, so it works from the lobby too — moderation reuses the existing admin slot, and mute/ban escalation stays with channel bans per the reducer's doc comment), and the retention sweep [[server/spacetimedb/src/chat/mod.rs#tick_chat_cleanup|tick_chat_cleanup]] full-scans the table and deletes rows older than [[server/spacetimedb/src/main/global.rs#CHAT_RETENTION_HOURS|CHAT_RETENTION_HOURS]] (24 h), called from the agent [[server/spacetimedb/src/main/agents.rs#tick_cleanup|cleanup pass]] every 1 Hz tick while agents are enabled. Never updated — posts are immutable.

#### Readers

***Client***: via the [[server/spacetimedb/src/chat/mod.rs#local_chat_messages|local_chat_messages]] per-caller view (member channels only through the mirror right-semijoin; callers outside the world get a deliberately-unmatchable sentinel query, the standard empty-view idiom) → [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave → subscribed but no binder consumes it (same verified grep as `AllChatChannels`: generated bindings only, no scene wiring) → **no visible end** (no chat log UI exists). ***Server***: [[server/spacetimedb/src/chat/mod.rs#report_chat_message|report_chat_message]] reads the row being reported (message must exist).

## ChatReport

```sync
![[00 Table Map#^table-chat-report{seamless:true,title:false,marker:04.}]]
```

#### Shape

One row per abuse report in [[server/spacetimedb/src/chat/mod.rs#ChatReport|definition]]: auto-increment `report_id`, btree-indexed `message_id`, `reporter_profile_id`, trimmed `reason`, `reported_at`. Server-only by design (never subscribed; admins read via logs/SQL), so it carries no `public` flag.

#### Changers

Insert only in [[server/spacetimedb/src/chat/mod.rs#report_chat_message|report_chat_message]]: in-world caller, the reported message must exist, reason must be 1–200 chars after trimming. Never updated or deleted anywhere in the code — reports accumulate with no in-code consumer (nothing reads `chat_report`, not even the moderation reducer).

#### Readers

***Client***: server-only (not `public`) — never subscribed, **no visible end**. ***Server***: none in code; out-of-band admin inspection only.

## ChatRate

```sync
![[00 Table Map#^table-chat-rate{seamless:true,title:false,marker:05.}]]
```

#### Shape

One row per profile in [[server/spacetimedb/src/chat/mod.rs#ChatRate|definition]]: `profile_id` primary key (not auto-increment — the profile id *is* the key), `window_start` timestamp, `posts` counter. Server-only. This is the chat-local copy of the `PlayerHitRate` 1-second-window pattern, deliberately decoupled from it per the module header.

#### Changers

Insert/update only in the private [[server/spacetimedb/src/chat/mod.rs#chat_rate_allow|chat_rate_allow]], called on every `chat_post` attempt that passes the text check: it loads-or-defaults the row, resets `window_start`/`posts` when [[server/spacetimedb/src/main/time.rs#seconds_since|seconds_since]] reports the window ([[server/spacetimedb/src/main/global.rs#CHAT_RATE_WINDOW_SECS|CHAT_RATE_WINDOW_SECS]] = 5.0 s) has elapsed, allows while `posts` is under [[server/spacetimedb/src/main/global.rs#CHAT_RATE_MAX_POSTS|CHAT_RATE_MAX_POSTS]] (5), increments on allow, and persists the row either way (insert for first-timers, PK update otherwise). Never deleted — windows go stale, never removed (matching 00's `ChatRate` entry: 5-second window).

#### Readers

***Client***: server-only (not `public`) — never subscribed, **no visible end** (posters only feel it as the rejection string). ***Server***: gates [[server/spacetimedb/src/chat/mod.rs#chat_post|chat_post]].

## 7. Files — pointer deep dive

The module is a single code file — all five tables, all nine reducers, both views, and the two private helpers live in [[server/spacetimedb/src/chat/mod.rs|mod.rs]] (verified by listing `server/spacetimedb/src/chat/`, which holds only `mod.rs` plus the generated `pointers.md`/`pointers-symbols.md`, and by grepping `#[table(` there: exactly five matches). Nothing outside the module writes these tables (a repo-wide grep for the chat accessors finds touches only in `mod.rs`, plus the retention caller in `agents.rs`). Per-file order follows [[server/spacetimedb/src/chat/pointers.md|pointers]], whose only external-subscriber entries are the two views' shared subscription site: [[client/sstdbsdk/TableSubscriber.cs#TableSubscriber|TableSubscriber]] (the `GameTables` name list) and [[client/Scenes/game.tscn|game.tscn]] (the scene hosting the inline `TableSubscriber` node — it declares no chat-specific node).

### mod.rs

The whole system in one file, organized top-to-bottom as tables → helpers → reducers → views. The five `#[table]` structs open the file with their indexes (`by_member_channel` composite plus per-column btrees where exact-match reads need them). Two private helpers follow: `chat_rate_allow` (the fixed-window limiter — read caller's row, reset on window expiry, compare-and-increment, persist) and `check_text` (trim, reject empty, reject over-length). Then the nine reducers, all returning `Result<(), String>` and failing closed with strings (reducers must not panic — a panic destroys the WASM instance): the six membership reducers (`chat_create`, `chat_join`, `chat_leave`, `chat_invite`, `chat_ban`, `chat_unban`), each pairing a `ChatChannel` vec edit with the mirror-table insert/delete that keeps the view joinable; `chat_post` (the five-gate pipeline into `ChatMessage`); `report_chat_message` (validated insert into `ChatReport`); `moderate_delete_message` (admin PK delete). `tick_chat_cleanup` is a plain `pub fn`, not a reducer — it cannot be called by clients and has no schedule row of its own; the agent `tick_cleanup` in `agents.rs` calls it on the shared 1 Hz tick, gated by `should_run` (fail-closed when the `AgentConfig` row is missing). The file closes with the two views: `all_chat_channels` (unfiltered global build over `chat_channel`) and `local_chat_messages` (sentinel-empty default, `LoggedInPlayer` profile lookup by `ctx.sender()`, caller-mirror filter, right-semijoin to messages — the OR-chain/`IN` workaround idiom from `server/AGENTS.md` expressed as a join). Cross-module calls direction: chat calls *out* to `player::methods` (`require_in_world`, `is_admin`), `main::global` (the four `CHAT_*` consts), and `main::time` (`seconds_since`); `main::agents` calls *in* to `tick_chat_cleanup`. No other module reads or writes these tables.

## 8. Cross-table flows

Two flows span tables and fit nowhere singly. **Posting**: `chat_post` reads `ChatChannel` (membership/ban) → mutates `ChatRate` (window check-and-increment) → inserts `ChatMessage`; all three legs are one transaction, so a rejected post leaves no rate or message trace beyond the persisted window row. **Leaving history**: messages exit through `moderate_delete_message` (single-row, admin) or `tick_chat_cleanup` (age scan from the cleanup pass), while `ChatReport` rows and `ChatRate` windows survive both — reports are never swept and rate rows are never deleted.

## 9. Known gaps / stubs

All verified against the code (greps noted per item); aspirational design-doc content stays out per [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]].

- **Client-unwired end to end.** Both views are subscribed in the `GameTables` wave, yet no `TableBinderComponent` consumes `AllChatChannels`/`LocalChatMessages` (grep over `client/Scripts` hits only generated `module_bindings/`; grep over `client/Scenes` hits nothing), no chat UI exists in any live scene, and no gameplay code calls any of the nine chat reducers — the generated reducer stubs are the only client-side references. Channels can be listed over the wire but never displayed; messages can be received but never shown or sent. The recurring mistake from the root `AGENTS.md` applies: the client never calls the reducers.
- **Channels are never deleted.** No reducer deletes `ChatChannel` rows (verified: the only deletes in `mod.rs` target `chat_channel_member` and `chat_message`), and leaving as owner transfers nothing — empty and ownerless channels, with their message history, persist until the retention sweep ages the messages out.
- **No chat cleanup on profile teardown.** `teardown_profile` (death / `delete_profile`) touches no chat table (verified by grep — no chat accessor appears outside `chat/mod.rs` and the `agents.rs` cleanup call), so a deleted profile's id lingers in `members`/`banned` vecs, in mirror rows, and in `ChatRate` rows indefinitely.
- **Unban does not rejoin.** `chat_unban` retains the id out of `banned` and stops there (verified in `mod.rs`); the unbanned profile must `chat_join` again.
- **Retention cadence is the 1 Hz tick, not the 60 s const.** `tick_cleanup` runs `tick_chat_cleanup` every tick while agents are enabled (verified in `agents.rs`); `AGENT_CHAT_CLEANUP_TICK_SECONDS` (60.0) appears only in a log line in `update_agent_timers` and schedules nothing.

## 10. Where to go next

Read [[docs/Technical Design Docs/Description/12 Player Tables.md|12 Player]] for the `LoggedInPlayer` rows and `require_in_world` gate every chat reducer stands behind, and [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] for the view → wave → binder pattern a future chat UI would plug into (one binder per view, wired in `game.tscn`, replay on since both views are persistent rather than event tables).
