# 07 Claims

## Assumed knowledge

- [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] — the `^table-territory` entry this doc expands.
- [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] — reading order, conventions, and the `main-claims` flow name.
- [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] — views, subscription waves (`BaseTables`/`LobbyTables`/`GameTables`), and binder components, which this doc assumes without re-teaching.
- Maintainer refs (jump-off points, not authority): [[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]].

## The 30-second version

The claims module owns one table, `Territory`: a guild-territory-lite claim (owner profile, hex-tile footprint as parallel `tiles_q`/`tiles_r` vecs, member list plus per-member permission bits, upkeep treasury). Players create and manage rows through five reducers (`claim_territory`, `territory_add_member`, `territory_remove_member`, `territory_deposit`, `disband_territory`); a periodic upkeep pass (`tick_upkeep`, driven by the 1 Hz status tick's cleanup slot) charges `tiles × upkeep` per interval and dissolves unpaid claims. Reads split two ways: every client subscribes to the global `all_territories` view in the game wave but no binder consumes it (no visible end — hex tint overlays are planned, not built), while the server consults the table on every interaction and build path (chest takes, drop pickups, site placement, demolish, legacy `place_building`) through the `check_interact`/`check_build` permission gates.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do I claim land? | `Territory` | [[#Territory\|Territory]] — `claim_territory` (Changers) |
| How do permissions work (who can use my claim)? | `Territory` | [[#Territory\|Territory]] — Shape (`TerritoryPermission` bits) + `territory_add_member` (Changers) |
| Why was my chest take / drop pickup rejected inside a claim? | `Territory` | [[#Territory\|Territory]] — Readers/Server (`check_interact` via `validate_interact`) |
| Why can't I build inside a claim? | `Territory` | [[#Territory\|Territory]] — Readers/Server (`check_build`, footprint consults) |
| What happens when upkeep goes unpaid? | `Territory` | [[#Territory\|Territory]] — `tick_upkeep` (Changers) |
| Where do I see my territory on screen? | `Territory` | [[#Territory\|Territory]] — Readers/Client (nowhere yet: subscribed, no binder) |
| How do I call these reducers from the client? | — | Nowhere — no UI calls them; see [[#Known gaps / stubs\|Known gaps]] |
| What does the upkeep timer run on? | — | [[#Cross-table flows\|Cross-table flows]] (`tick_status_effects` → `tick_cleanup` → `tick_upkeep`) |
| Which buildings belong to a claim? | — | World doc (`BuildingState`/`ProjectSiteState` `territory_id`); stamping/release in [[#Territory\|Territory]] Readers/Server |

## Flowcharts

- [[flowcharts/main-claims.canvas]] — the composed claims flow (expected to stay unresolved until the Obsidian recompose runs; do not hand-create it).
- `flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/claims_subfolder/claims_subfolder.canvas` — the claims module aggregate (verified present).
- `flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/world_subfolder.canvas` — the world module aggregate, where the build-permission consults land (verified present).

## Territory

```sync
![[00 Table Map#^table-territory{seamless:true,title:false,marker:01.}]]
```

### Shape

One row is one claimed territory: `territory_id` (auto-increment primary key), `owner_profile_id` (the profile that owns it — *not* an Identity; claims are per-character), the footprint as parallel `tiles_q`/`tiles_r` `Vec<i32>` (parallel vecs because SpacetimeDB vecs of tuples are awkward, per the comment on [[server/spacetimedb/src/claims/mod.rs#Territory|Territory]]), the member list as parallel `members: Vec<u64>` (profile ids) plus `member_permissions: Vec<u8>` (one bitmask byte per member, index-aligned), and the upkeep treasury (`treasury: u32` plus `last_upkeep_at` timestamp). Permissions are the [[server/spacetimedb/src/claims/mod.rs#TerritoryPermission|TerritoryPermission]] enum — `Inventory`, `Build`, `Usage` — mapped to bits 0/1/2 of each member's byte by [[server/spacetimedb/src/claims/mod.rs#has_permission|has_permission]]; the owner bypasses the bits entirely (owner always passes). Wilderness is the absence of a row: every consult treats "no territory covers this tile" as allow. The table is `public` with accessor `territory`, so both the raw table and the `all_territories` view are subscribable.

### Changers

- **Insert** — only in [[server/spacetimedb/src/claims/mod.rs#claim_territory|claim_territory]] (a *reducer*: a transactional server function clients invoke by name — it returns only success/error, and callers learn the result through table subscriptions). It requires the caller to be in the world via [[server/spacetimedb/src/player/methods.rs#require_in_world|require_in_world]], rejects empty claims and claims over [[server/spacetimedb/src/main/global.rs#MAX_TERRITORY_TILES|MAX_TERRITORY_TILES]] (64) tiles, then full-scans `territory` and rejects any tile overlapping an existing claim (the BitCraft split-claim reject). The new row starts with empty members/permissions, `treasury: 0`, and `last_upkeep_at` stamped at insert. Note what it does *not* check: tiles are arbitrary `(q, r)` pairs — no adjacency/contiguity check and no existence check against generated hexes (see [[#Known gaps / stubs\|Known gaps]]).
- **Update (members/permissions)** — [[server/spacetimedb/src/claims/mod.rs#territory_add_member|territory_add_member]] upserts one member row: owner-or-admin only (checked against `row.owner_profile_id` plus [[server/spacetimedb/src/player/methods.rs#is_admin|is_admin]]), rewriting the permissions byte when the profile is already listed, otherwise pushing onto both vecs in lockstep. [[server/spacetimedb/src/claims/mod.rs#territory_remove_member|territory_remove_member]] has the same owner-or-admin gate and removes both vec entries at the member's index — so the two vecs stay index-aligned by construction. A member cannot remove themselves (no self-leave path), and removing a non-member silently succeeds.
- **Update (treasury)** — [[server/spacetimedb/src/claims/mod.rs#territory_deposit|territory_deposit]] (owner-or-admin) `saturating_add`s `amount` onto `treasury`. The amount comes from the caller's argument alone — nothing is deducted from any player currency or inventory, so deposits mint upkeep credit from nothing.
- **Update (upkeep charge)** — [[server/spacetimedb/src/claims/mod.rs#tick_upkeep|tick_upkeep]] is *not* a reducer but a plain helper called from the cleanup slot (see [[#Cross-table flows\|Cross-table flows]]). Per row it compares `ctx.timestamp` against `last_upkeep_at` and skips rows younger than [[server/spacetimedb/src/main/global.rs#TERRITORY_UPKEEP_INTERVAL_SECS|TERRITORY_UPKEEP_INTERVAL_SECS]] (300 s); otherwise it charges `tiles × `[[server/spacetimedb/src/main/global.rs#TERRITORY_UPKEEP_PER_TILE|TERRITORY_UPKEEP_PER_TILE]] (`1` per tile) from the treasury and restamps `last_upkeep_at` — or, when the treasury can't cover it, logs and dissolves the territory (delete path below). Because the interval check is per-row wall-clock, a paused agent master-switch just delays charges rather than back-charging.
- **Delete** — in [[server/spacetimedb/src/claims/mod.rs#disband_territory|disband_territory]] (owner-or-admin, via `require_in_world`) and on unpaid upkeep in `tick_upkeep`. Both call [[server/spacetimedb/src/claims/mod.rs#unclaim_buildings_in_territory|unclaim_buildings_in_territory]] first, which releases the claim's `BuildingState` and `ProjectSiteState` rows to wilderness (`territory_id = None`) instead of leaving dangling references. Nothing else deletes: death, logout, and profile deletion leave territory rows (and stale member/owner profile ids) behind — `teardown_profile` never touches this table (see [[#Known gaps / stubs\|Known gaps]]).

### Readers

- ***Client*** — via the [[server/spacetimedb/src/claims/mod.rs#all_territories|all_territories]] global view (unfiltered: `_ctx.from.territory().build()`), which is listed in the [[client/sstdbsdk/TableSubscriber.cs#GameTables|GameTables]] wave of [[client/sstdbsdk/TableSubscriber.cs#TableSubscriber|TableSubscriber]] (comment: "drives hex tint overlays"). The `TableSubscriber` node lives in [[client/Scenes/game.tscn]] so the subscription is active in-game — but **no binder consumes it**: `game.tscn` declares no `AllTerritoriesBinder`, and no hand-written client code outside the generated `module_bindings/` references `Territory` at all. Explicit no-visible-end: no hex tint overlay, no territory panel, and no UI calls any of the five claim reducers (they are reachable today only via manual `spacetime call`). The `module_bindings/` reducer stubs (`ClaimTerritory`, `DisbandTerritory`, …) exist because bindings are generated for every reducer, not because anything calls them.
- ***Server*** — three consult shapes, all full-table scans (`territory_at_tile` iterates every row and zip-matches the footprint vecs — fine at guild-claim scale, worth knowing if claims ever number in the thousands):
  - `Inventory` gate: [[server/spacetimedb/src/claims/mod.rs#check_interact|check_interact]] → [[server/spacetimedb/src/claims/mod.rs#can_interact_pos|can_interact_pos]] with `TerritoryPermission::Inventory`, called from [[server/spacetimedb/src/player/methods.rs#validate_interact|validate_interact]] after its wrap-aware range check — so it gates both [[server/spacetimedb/src/chest/mod.rs#take_from_chest|take_from_chest]] and [[server/spacetimedb/src/player/reducers.rs#pickup_drop|pickup_drop]] (the 00 entry names only the chest path; the drop path shares the same gate).
  - `Build` gate: [[server/spacetimedb/src/claims/mod.rs#check_build|check_build]] → `can_interact_pos` with `TerritoryPermission::Build`, called per footprint cell (hex → world via [[server/spacetimedb/src/world/hex.rs#hex_to_world|hex_to_world]]) from `check_build_perm_for_cells` in `world/building.rs`, which gates [[server/spacetimedb/src/world/building.rs#place_project_site|place_project_site]] (non-admins), the non-owner branch of [[server/spacetimedb/src/world/building.rs#advance_project_site|advance_project_site]], and the non-owner branch of [[server/spacetimedb/src/world/building.rs#demolish_building|demolish_building]]; the legacy admin-only [[server/spacetimedb/src/world/reducers.rs#place_building|place_building]] consults it too, but only on its unreachable-behind-`is_admin` branch (admins bypass either way).
  - Footprint ownership: [[server/spacetimedb/src/claims/mod.rs#territory_id_at_hex|territory_id_at_hex]] resolves the claim under one hex; `territory_under_footprint` in `world/building.rs` folds it over a footprint and stamps `Some(id)` only when *every* cell sits inside the *same* territory, else `None` (wilderness) — that stamp is what `unclaim_buildings_in_territory` later clears on dissolve.
  - `Usage` (bit 2) has no consult: it is defined on the enum and stored in the bitmask, but no code path passes it to `can_interact_pos`.

## Files — pointer deep dive

### mod.rs

The module's only file (per [[server/spacetimedb/src/claims/pointers.md|pointers]]): table definition, permission model, consults, reducers, upkeep, and view, all in one place. Call direction is inward from three modules — `player/methods.rs` (`validate_interact` → `check_interact`), `world/building.rs` (`check_build`, `territory_id_at_hex`), `world/reducers.rs` (`check_build`), `main/agents.rs` (`tick_cleanup` → `tick_upkeep`) — plus the chest and player reducers transitively through `validate_interact`; nothing in `claims/` calls outward except those same helpers' dependencies (`require_in_world`/`is_admin` from `player/methods.rs`, `MapConfig::load` and `world_to_hex` for the position→hex lookup inside `can_interact_pos`, `building_state`/`project_site` accessors for the unclaim sweep). Guards are uniform: every reducer starts with `require_in_world` (so callers need a `LoggedInPlayer` row), and every mutation except claiming requires owner-or-admin. Data passed across the boundary is always world x/y plus a profile id inward, `Result<(), String>` outward — the tile footprint only crosses at claim time as parallel `tiles_q`/`tiles_r` vecs.

## Cross-table flows

Two mechanisms span tables, both told fully under [[#Territory\|Territory]] above and summarized here. **Claim-gated interaction:** `take_from_chest` (chest) and `pickup_drop` (player) both funnel through `validate_interact` (range check, then `check_interact`), so a single Inventory-permission consult guards chest looting and ground pickups identically; on the build side, `check_build` plus the `territory_under_footprint` stamp guard site placement, site work, demolition, and legacy placement while tying `BuildingState`/`ProjectSiteState` rows to their claim until dissolve releases them. **Upkeep scheduling:** there is exactly one 1 Hz schedule (the `ConsumableEffectSchedule` row driving `tick_status_effects` in `player/reducers.rs`), whose tail calls `tick_regen`, `tick_zones`, the status `tick`, then `tick_cleanup` (`main/agents.rs`) — and `tick_cleanup` runs the trade timeout, chat retention, `tick_upkeep`, and anticheat strike sweeps together, so territory upkeep shares a tick with every other slow sweep rather than owning a schedule row.

## Known gaps / stubs

- `TerritoryPermission::Usage` (bit 2) is defined and storable but never consulted — only `Inventory` and `Build` gate anything. A member granted solely `Usage` is indistinguishable from a non-member today.
- `territory_deposit` mints treasury credit from the `amount` argument with no deduction from any player-side balance or inventory — upkeep funding is currently a free counter.
- Members cannot leave a territory themselves: `territory_remove_member` requires owner-or-admin, so a listed member has no self-remove path; removing a non-member is a silent no-op rather than an error.
- `teardown_profile` (death, `leave_world`/disconnect purge, `delete_profile`) never touches `Territory` rows — a dead or deleted profile's territories persist with a dangling `owner_profile_id`, and its id lingers in other claims' `members` vecs. Enemy-sim "ghost" guards (`logged_in_player` checks) don't apply here; the rows simply outlive their profiles.
- No client surface: `all_territories` is subscribed but binder-less (no visible end — the `TableSubscriber` "hex tint overlays" comment describes planned, not built, UI), and no scene or component calls any claim reducer, so claiming, membership, deposit, and disband are server-reachable but UI-unreachable.
- `claim_territory` accepts any `(q, r)` pairs within the 64-tile cap — no adjacency/contiguity requirement and no check that the hexes exist in the generated world — and overlap detection is a full scan of every claim's footprint per call.

## Where to go next

Read the World doc for the building/site rows claims attach to (`BuildingState`/`ProjectSiteState` stamping, footprints, demolition), and [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] if the view→wave→binder vocabulary above felt compressed.
