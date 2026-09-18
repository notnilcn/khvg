# 10 Item

## 1. Assumed knowledge

- [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] — the `# Item` section is this doc's spine; every table below sync-embeds its entry.
- [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] — what these docs are and the conventions they use.
- `03 Connection, Subscriptions & Views` (Roadmap row 03) — the shared read path this doc assumes: a **view** is a named server-side query the client subscribes to instead of writing SQL; views arrive in **subscription waves** (`BaseTables`/`LobbyTables`/`GameTables` in `TableSubscriber.cs`); one **binder** child component per consumed table re-exposes row events as editor-wireable signals. A **reducer** is a server function a client calls by name; it runs transactionally and returns no data, so clients only ever learn outcomes through tables.
- `12 Player` (Roadmap row 12) — owns the inventory slots, equipping rules, and ability activation that consume this catalog; read it for `PlayerInventorySlot`, `slot_allowed`, and `recompute_stats`.
- Maintainer refs (jump-off points, not authority — code wins): [[server/AGENTS.md|server AGENTS.md]] (item notes), [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]] (the ability-authoring recipe), [[client/AGENTS.md|client AGENTS.md]] (client file map).

## 2. The 30-second version

The item module owns five tables: the live `Item`/`Enchantment` catalog, the admin-only `StagedItem`/`StagedEnchantment` staging copies, and the `AbilityKnowledgeRequirement` equip gate. Nothing here moves at runtime — rows are written by publish-time seeds and admin reducers, read wholesale through two global views (`all_items`, `all_enchantments`) that [[client/Scripts/Components/Catalog/CatalogComponent.cs#CatalogComponent|CatalogComponent]] caches, and everything downstream (slot icons, the hover sidebar, stat resolution, every trigger pull) reads that cache. The one piece of live logic in the folder is weapon composition: [[server/spacetimedb/src/item/methods.rs#resolve_effective_weapon|resolve_effective_weapon]] folds the weapon item's own behaviors plus enchantment grants into the effective weapon, in a fold order the client's `EffectiveWeaponResolver` mirrors exactly.

## 3. Find it fast

| Question / concept | Table | Where |
|---|---|---|
| Where do items and enchantments come from? | `Item` | [Item](#item) — seeds + admin upserts |
| How do catalog rows reach the client? | `Item`, `Enchantment` | [Item](#item) / [Enchantment](#enchantment) — `all_items`/`all_enchantments` → `CatalogComponent` cache |
| What can this weapon fire right now? | `Item` | [Item](#item) — toggle options + `resolve_effective_weapon` fold order |
| Why does the client show a different shot pattern than I expect? | `Item` | [Item](#item) — `Fingerprint`/`CatalogVersion` re-resolve path |
| What do the REQ_* gates on abilities mean? | `Item` | [Item](#item) — seeds; enforced by `check_stat_requirements` on equip |
| Why can't I use an ability item from the hotbar? | `Item` | [Item](#item) — `use_item` guards (behavior-based, Hotbar role, rejects Ability) |
| How do I add a new ability? | `Item` | [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]] recipe + [Files](#7-files--pointer-deep-dive) (`seeds.rs`); full tutorial in [[docs/Technical Design Docs/Workflows/Adding an Ability (Ability Authoring Workflow).md|Adding an Ability]] |
| How do enchantments attach to gear? | `Enchantment` | [Enchantment](#enchantment) — innate vs socketed, `ItemSidebar` socket buttons |
| What is the staging flow for catalog edits? | `StagedItem`, `StagedEnchantment` | [StagedItem](#stageditem) / [StagedEnchantment](#stagedenchantment) — stage → validate → import → clear |
| What locks an ability behind knowledge? | `AbilityKnowledgeRequirement` | [AbilityKnowledgeRequirement](#abilityknowledgerequirement) — `set_ability_knowledge` + `resolve_ability_slot` gate |
| Where do items physically go when picked up? | — | [Cross-table flows](#8-cross-table-flows) — `transfer_item_to_inventory` + `recompute_stats` (shape owned by `12 Player`) |
| Where is damage actually computed? | — | Combat module (`compute_player_damage` over the `EffectiveWeapon` composed here); see `08 Combat` |
| Can items be generated procedurally? Can a Bag grant capacity? | — | [Known gaps](#9-known-gaps) — neither is built |

## 4. Flowcharts

- [[flowcharts/main-item.canvas]] — the composed item flow (manifest flow `item` in `flowcharts/flows.json`; link stays unresolved until the Obsidian recompose runs — do not hand-create the canvas).
- Deep dives (paths verified present on disk):
  - [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/item_subfolder/item_subfolder.canvas]] — the whole server module.
  - [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Catalog_subfolder/Catalog_subfolder.canvas]] — the catalog cache.
  - [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Inventory_subfolder/Inventory_subfolder.canvas]] — inventory UI + activation.

## 6. Tables

### Item

```sync
![[00 Table Map#^table-item{seamless:true,title:false,marker:01.}]]
```

**Shape.** One row per item, and the row is deliberately two independent axes: `equip_slot` says only *where it goes* (Weapon/Armor/Accessory/Bag/Consumable/Ability — artifacts are 1-cost abilities, there is no Artifact variant), while `stat_modifiers` + `behaviors` say *what it does*. That independence is what makes "an accessory that fires projectiles" expressible (`equip_slot: Accessory, behaviors: [Weapon(..)]`) and is the prerequisite for procedural generation. Consequences: there are **no per-type side tables** (no `Weapon`/`Armor` rows to join, no `ArtifactEffect` — an artifact "effect" is just a flat `StatModifier`), and capability lookups are behavior-based, never `equip_slot`-based. `slot_cost` is the multi-slot span width for ability-region placement (head row holds the item, followers are marked `occupied_by`); seeded catalog uses 1 everywhere except `Tome of Mending` (3) and `Ember Scripture` (2) in [[server/spacetimedb/src/item/seeds.rs#seed_world_items|seed_world_items]]. `innate_enchantment_ids` always apply while equipped, don't eat sockets, can't be removed. `stat_requirements` is flat per-stat minimums, all-zero when ungated.

**Changers.** Upsert-only, never deleted: `Item::seed` per entry in `seed_world_items` at publish (idempotent by primary key) and admin [[server/spacetimedb/src/item/reducers.rs#upsert_item|upsert_item]] / [[server/spacetimedb/src/item/reducers.rs#import_staged_items|import_staged_items]]. The seeds are the design surface — see [Files](#7-files--pointer-deep-dive) (`seeds.rs`) for the showcase entries (`Bow`'s two toggle patterns, `Fan Bow`'s innate `point_blank`, the `REQ_BULLET_MASTER`-gated `Marksman Bow`/`Warbow`, the `Null Sigil`/`Delete Orb` bullet-delete abilities) and the `REQ_*` class gates. New abilities reusing an existing `AbilityEffect` are seed-only; a new effect variant additionally arms `scale_ability_effect`/`apply_ability_effect` and possibly `ability_is_charge_capable` — full recipe in [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]].

**Readers.**

- ***Client.*** Via the [[server/spacetimedb/src/item/views.rs#all_items|all_items]] global view → `BaseTables` wave ([[client/sstdbsdk/TableSubscriber.cs#BaseTables|BaseTables]] in `TableSubscriber.cs`) → `AllItemsBinder` on [[client/Scripts/Components/Catalog/CatalogComponent.cs#CatalogComponent|CatalogComponent]] (`ReplayExistingRows = true`, wired in [[client/Scenes/game.tscn|game.tscn]]) → the `itemCache` behind `GetItem`. Visible ends: slot icons in [[client/Scripts/Components/Inventory/InventoryPanel.cs#InventoryPanel|InventoryPanel]] (`SetSlotTexture` resolves `texture_id` → `GetResPath2D`), the hover composition in [[client/Scripts/Components/Inventory/ItemSidebar.cs#ItemSidebar|ItemSidebar]] (modifiers, red-when-unmet requirements, behavior lines, innate/socketed rows), and the effective weapon behind every trigger pull — [[client/Scripts/Components/Weapon/CombatComponent.cs#CombatComponent|CombatComponent]] re-resolves via `EffectiveWeaponResolver` only when its fingerprint changes, where the fingerprint is `EffectiveWeaponResolver.Fingerprint` (weapon item, `ActiveToggle`, every equipped slot's socketed + innate ids) plus `CatalogVersion`. Because the catalog version is part of the key, an admin catalog edit re-resolves the weapon even though no inventory row changed — catalog edits surface through `CatalogChanged`, never through `InventoryChanged`.
- ***Server.*** Behavior/cost/requirement lookups on every inventory touch: `consumable_behavior`/`ability_behavior` in `use_item` and `resolve_ability_slot`, `slot_cost` in span placement (`place_span`/`find_free_span`), `check_stat_requirements` gating any item entering an equipped role from loose storage, and `weapon_toggle_options` both validating [[server/spacetimedb/src/player/reducers.rs#set_slot_toggle|set_slot_toggle]] and indexing `resolve_effective_weapon`. Combat reads the composed result (`resolve_effective_weapon` in `compute_player_damage`), never the raw row.

**Toggle options and fold order (load-bearing).** An item's shot-pattern toggle options are its own `Weapon` behaviors first, then `WeaponToggle` grants from `equipped_enchantment_behaviors` — which folds Weapon/Accessory/Armor/Ability heads in slot-index ascending order, innate enchantments before socketed per slot. [[server/spacetimedb/src/item/methods.rs#resolve_effective_weapon|resolve_effective_weapon]] picks `options[active_toggle]` (clamped, because removing a toggle-granting enchantment can strand the index), then folds shot count as `(shot_count + AddShots, min 1) × (1 + Σ ShotCountMult)`, rounded, min 1, and sums `FlatBulletDamageMod`/`TrueDamageFlat`/`TrueDamagePercent` straight through. The client's `EffectiveWeaponResolver.Resolve`/`ToggleOptions`/`EquippedEnchantmentBehaviors` mirror this exactly, including the clamp — both sides must change together, since both index the same option list. `set_slot_toggle` is weapon-slot-only, rejects span followers, requires ≥ 2 options, and range-checks the value against that same list.

**`use_item` guards.** [[server/spacetimedb/src/player/reducers.rs#use_item|use_item]] guards on three independent axes: the *slot* must be Hotbar-role (matching the client UI, which only exposes UseItem on hotbar keys 1–4), the *item* must carry a `Consumable` behavior (not `equip_slot == Consumable`), and the item must **not** carry an `Ability` behavior — consuming a span head would orphan its follower cells. The consumed row is cleared and `recompute_stats` runs.

### Enchantment

```sync
![[00 Table Map#^table-enchantment{seamless:true,title:false,marker:02.}]]
```

**Shape.** One row per enchantment: `stat_modifiers` plus `behaviors` (`AddShots`, `ShotCountMult`, `FlatBulletDamageMod`, `TrueDamageFlat/Percent`, `OnAbilityUseBuff`, `WeaponToggle`), gated onto gear by `allowed_slots`. An empty `allowed_slots` means socketable nowhere — that is how the innate-only toggles `point_blank` (10-bullet circle granted by `Fan Bow`) and `scatter_fan` (5-arrow fan granted by `Warbow`) stay valuable: being born with them is the only way to get them. The `OnAbilityUseBuff` arm fires from `spend_ability` on every ability activation, so "armor that buffs you when you use an ability" (`echoing`) needs no per-item code.

**Changers.** Upsert-only, never deleted: `Enchantment::seed` per entry in `seed_world_enchantments` (impl lives in `main/seeds.rs`) at publish and admin [[server/spacetimedb/src/item/reducers.rs#upsert_enchantment|upsert_enchantment]] / `import_staged_enchantments`. Seeds include the stat sticks (`bear`, `swift`, `eagle_eye`, `iron_skin`, `vampiric`), the doc-02 tradeoff `splitting` (double bullets, −3 per bullet — armour shreds the doubled weak pellets), `echoing`, and the two innate-only toggles above.

**Readers.**

- ***Client.*** Via [[server/spacetimedb/src/item/views.rs#all_enchantments|all_enchantments]] → `BaseTables` → `AllEnchantmentsBinder` on `CatalogComponent` (replay on, [[client/Scenes/game.tscn|game.tscn]]) → the enchantment cache. Visible ends: `ItemSidebar` renders innate rows (no button), socketed rows (`N / MaxEnchantments`), and — only for enchantable items in equipment slots (weapon 0, accessory 5, armor 6, abilities 32–37) — applicable enchantments filtered by `AllowedSlots` with Socket/Remove buttons calling the `ApplyEnchantment`/`RemoveEnchantment` reducers; one `enchantment_row.tscn` instance per row. `EnchantmentsChanged` (plus `CatalogChanged`, bumped with `CatalogVersion` on every enchantment row change) refreshes the shown slot. Enchantment behaviors reach firing through `EffectiveWeaponResolver`'s fold, in the same slot order the server uses.
- ***Server.*** Folded into resolved stats and weapons in `recompute_stats` (socketed **and** innate modifiers; enchantment *behaviors* are never ability-position-scaled) and `resolve_effective_weapon` / `spend_ability`.

### StagedItem

```sync
![[00 Table Map#^table-staged-item{seamless:true,title:false,marker:03.}]]
```

**Shape.** Same fields as `Item` plus `staged_by` — a scratch copy so admins can draft catalog revisions without touching live rows.

**Changers.** Admin-only [[server/spacetimedb/src/item/reducers.rs#stage_item|stage_item]] inserts/updates (rejects empty ids and `slot_cost == 0`); [[server/spacetimedb/src/item/reducers.rs#validate_staged_items|validate_staged_items]] reports bad rows (empty id, `slot_cost` 0 or > 6, innate ids resolving to neither live nor staged enchantments) without touching live rows; `import_staged_items` upserts every staged row into the live catalog by primary key; [[server/spacetimedb/src/item/reducers.rs#clear_staged_items|clear_staged_items]] drops all staged rows. Code truth on deletion: **import does not delete staged rows** — `clear_staged_items` is the only delete path, so a re-import re-applies whatever is still staged.

**Readers.** ***Client:*** server-only (not `public`), no visible end. ***Server:*** only the staging reducers above read it.

### AbilityKnowledgeRequirement

```sync
![[00 Table Map#^table-ability-knowledge-requirement{seamless:true,title:false,marker:04.}]]
```

**Shape.** Per-ability knowledge gate as a side table (`item_id` → `required_ids`) so `AbilityBehavior` seeds stay untouched; no row means no gate (backward compatible).

**Changers.** Insert/update in admin [[server/spacetimedb/src/player/reducers.rs#set_ability_knowledge|set_ability_knowledge]]; never deleted.

**Readers.** ***Client:*** server-only (not `public`), no visible end. ***Server:*** [[server/spacetimedb/src/player/reducers.rs#resolve_ability_slot|resolve_ability_slot]] rejects gated abilities the profile hasn't acquired (consulting `PlayerKnowledge.acquired`).

### StagedEnchantment

```sync
![[00 Table Map#^table-staged-enchantment{seamless:true,title:false,marker:05.}]]
```

**Shape.** Same fields as `Enchantment` plus `staged_by` — the enchantment half of the staging pair.

**Changers.** Admin-only `stage_enchantment` (rejects empty ids; an empty slot list is legal — it means innate-only), `validate_staged_enchantments` (non-empty id check), `import_staged_enchantments` (upsert by primary key, no staged-row delete), `clear_staged_enchantments` (the only delete path).

**Readers.** ***Client:*** server-only (not `public`), no visible end. ***Server:*** only the staging reducers; note `validate_staged_items` also reads staged enchantments when checking an item's innate references.

## 7. Files — pointer deep dive

File order follows [[server/spacetimedb/src/item/pointers.md|pointers]] (generated index — regenerate, never hand-edit).

### methods.rs

Pure catalog-side reads, no row writes. Per-item lookups (`consumable_behavior`, `ability_behavior`, `ability_is_charge_capable` — the single place charge-capability is decided; [[client/Scripts/Components/Inventory/LocalPlayerInventoryComponent.cs#IsChargeCapable|IsChargeCapable]] mirrors it) take only the item; composition (`equipped_head_slots`, `equipped_enchantment_behaviors`, `weapon_toggle_options`, `resolve_effective_weapon` → `EffectiveWeapon`) reads equipped slot rows plus the catalog. Called from `player/methods.rs` (ability lookups), `player/reducers.rs` (activation, spend, toggle validation), `status/reducers.rs` (charge-capability check), and `combat/calc.rs` (effective weapon for damage). The doc comment on `equipped_enchantment_behaviors` states the load-bearing contract: slot-index ascending, innate before socketed.

### mod.rs

Module declarations only (`tables`, `methods`, `reducers`, `views`, `seeds`). No logic; nothing outside the folder uses it directly.

### reducers.rs

Admin catalog surface, every arm `is_admin`-gated, `Err`-not-panic: `give_item` (drops the item into a free General slot — no stat check, it's a handout), `remove_item` (clears every matching slot; Ability-role matches clear the whole span via `clear_span`), `upsert_item`/`upsert_enchantment` (insert-or-update by primary key), and the staging state machine `stage → validate → import → clear` for both items and enchantments. Live gameplay reducers never live here — they live in `player/reducers.rs` and read this module through `methods.rs`.

### seeds.rs

The content bulk: `seed_world_items` (generic gear, the toggle weapons, multi-slot tomes/scriptures, bullet-control orbs, and ~40 class-gated abilities via the `ability_item` helper — single-behavior, 1-slot, unlimited-charge) and `seed_world_enchantments`. The `REQ_*` consts at the top are the class gates (primary stat ≥ 15, secondary ≥ 12 — modest above-base thresholds, because `BASE_STAT` 10 makes the design doc's literal "> 0" meaningless; Paladin mirrors witch-doctor as an open question, Trickster's ordering gate is approximated flat). `NO_REQUIREMENTS` marks generic gear. Called from `main/lifecycle.rs` at publish; upserts are idempotent. Missing `TextureEntry` for a new `texture_id` is a blank icon, not an error (client `GetResPath2D` returns null → icon stays empty).

### tables.rs

The unified-catalog shape plus every payload enum: `EquipSlot`, `StatKind`/`StatMode`/`StatModifier`, `StatRequirements`, `WeaponBehavior` (damage is **per trigger pull, divided among the pattern's bullets** — the defence tradeoff), `ConsumableBehavior`, the full `AbilityEffect` variant list (Heal/Buff, bullet-control, marks, shields, curses/bashes, zones, Teleport/Grapple, Spell) with per-variant param structs, and `EnchantmentBehavior`. New effect types add a variant here — unit or single-field-newtype only, or SpacetimeDB rejects the table with a misleading serialize cascade (see [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]]).

### views.rs

Two global (unfiltered, `AnonymousViewContext`) public views: `all_items` and `all_enchantments`. No per-caller filtering exists because the catalog is identical for everyone — which is why both ride the `BaseTables` wave, subscribed before lobby/game waves, and why the client can treat them as static caches keyed by id.

## 8. Cross-table flows

**Acquire path.** `pickup_drop`, `take_from_chest`, and admin `give_item` variants all funnel through [[server/spacetimedb/src/player/methods.rs#transfer_item_to_inventory|transfer_item_to_inventory]] = `store_item_in_inventory` + `recompute_stats`; outside callers must never pair them by hand or stats silently desync from equipment. Placement: ability items auto-equip into the first free span of matching `slot_cost` when stat requirements pass, else fall back to loose backpack storage; other items prefer a matching typed slot (requirements-gated) then General. The drop's carried `cooldown_until`/`charges` travel with the item, so whoever picks up an ability inherits its residual cooldown — and `place_span` only initializes fresh charges when the source carried none. Full slot-shape detail (roles, indices, spans) lives in `12 Player`; this section exists only to name the single entry point.

**Activation path (client calls the reducer).** Hotbar keys call `UseItem`; ability keys route through [[client/Scripts/Components/Inventory/LocalPlayerInventoryComponent.cs#TryActivateAbility|TryActivateAbility]] — cooldown/charge pre-check, `ActivateAbility` reducer with the cursor position, then the optimistic local apply (bullet-control via `BulletControllerComponent`, spell flashes via `SpellVisualComponent`, client-tweened grapple), with a per-head `castPendingUntilMs` guard cleared early by the server echo so one click is one cast. Charge-capable items instead route key-down/up to `TryBeginCharge`/`TryReleaseCharge`. Weapon toggle cycles through `SetSlotToggle` from `CombatComponent.CycleWeaponToggle` (T key). Forgetting the reducer call is the recurring failure mode — the client never mutates stats or damage locally.

## 9. Known gaps

- **Procedural item generator: not built.** The unified shape (`equip_slot` ⊥ behaviors, no side tables) is explicitly its prerequisite, but no generator exists — every row is hand-seeded.
- **`Bag` capacity as a behavior: not built.** The seeded `Bag` row is behavior-less (description text only); equipping it changes nothing about inventory size.
- **Slot left-click activation disabled.** [[client/Scripts/Components/Inventory/SlotComponent.cs#SlotComponent|SlotComponent]]'s left-click-to-activate path is commented out — abilities fire from hotkeys only; clicks only drag/drop, backpack right-click drops, and hover shows the sidebar.
- **Staged rows survive import.** Neither `import_staged_items` nor `import_staged_enchantments` deletes the staged rows it promotes; only `clear_staged_*` removes them. Re-importing re-applies stale drafts.

## 10. Where to go next

Read `12 Player` for the slots that wear this catalog (roles, spans, equipping, activation, `recompute_stats`), then `08 Combat` for what the composed `EffectiveWeapon` does to enemies. To add content, follow [[server/spacetimedb/src/item/AGENTS.md|item AGENTS.md]] and the [[docs/Technical Design Docs/Workflows/Adding an Ability (Ability Authoring Workflow).md|Adding an Ability]] tutorial.

**Phase 2 proposal:** the `item` flow composes to `flowcharts/main-item.canvas`; if the compose leaves a stale `flowcharts/main-items.canvas` behind, delete it by hand (stale mains are never pruned automatically).
