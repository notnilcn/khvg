# Items

Catalogue of the implemented ability items (and class weapons), mirroring the seeds in `server/spacetimedb/src/item/seeds.rs`. The ability design pitches these realize live in `Items/Abilities/Abilities.md`.

**How to read the gates:** classes emerge from gear locked behind stat thresholds (doc 03). Base stats are 10 for everyone, so gates are set just above base — primary 15, secondary 12 — as placeholder values pending tuning. Stat shorthand: STR = strength, WIS = wisdom, DEX = dexterity, DPS = damage-dealer, SUP = supporter.

**Shared rules:** abilities are not consumed on use — they go on cooldown (all items below have unlimited charges). The first ability slot is stronger: effect magnitudes scale by slot position (×1.5 / 1.25 / 1.1 / 1.0 / 0.9 / 0.8). Ability targets clamp to 600 range. One active mark (bleed or store) per player — recasting a mark replaces it (bleed pools dump instantly, store pools forfeit). Status effects purge on death, leaving the world, and disconnect. Hold-to-charge items self-slow (50%) and suppress firing while held; max charge 3s, then auto-release; charge scales the effect ×(0.25 + 0.75 × fraction).

# DPS

## Bullet master (DEX 15, DPS 12)

Weapons (toggle shot patterns with T):

| Item | Patterns |
|---|---|
| Marksman Bow | Piercing longshot (45 dmg, pierces) / point-blank spread (3×12) / quick light shot (20 dmg, fast cadence) |
| Warbow | Heavy single shot (60 dmg) / five-arrow fan (innate `scatter_fan` toggle, can't be removed) |

Abilities:

| Item | Effect | Cooldown |
|---|---|---|
| Charged Shot | Hold-to-charge, release fires a 600×20 piercing shot for up to 70 damage. Scales with DEX | 12s |
| Volley | 350-long cone (0.6 rad spread): 6 pellets of 10 damage each — flat armour eats every pellet, so it's strong vs low-defence enemies. Scales with DEX | 10s |
| Quick Slash | Erases enemy bullets dealing ≤15 damage in a 140×60 rectangle in front of you | 4s |
| Grapple Hook | Pull yourself toward the cursor, up to 450 (you still take damage mid-pull) | 10s |

## Warrior (STR 15, DPS 12)

Bleed marks — mark an enemy (cursor-pick, 100px pick radius); 70% of each hit *from that enemy* becomes a pool that drains over ~5s, 30% lands instantly. One item per purge condition:

| Item | Sheds by | Duration | Cooldown |
|---|---|---|---|
| Sanguine Brand | Taking no damage for 2s | 10s | 15s |
| Blood Oath | Staying within 200 of the mark for 5s | 15s | 15s |
| Crimson Tithe | Damaging the mark | 15s | 15s |
| Deathmark | The mark dying | 20s | 15s |
| Bloodbath | The mark dying — also converts *your* hits on the mark into a bleed on it (a third of the hit per second for 3s) instead of instant damage | 20s | 20s |

Store-damage marks — your damage to the mark is withheld for 8s, then released at 2×/1×/0× per the wager (marked-enemy death forfeits the pool):

| Item | Wager | Cooldown |
|---|---|---|
| Gambler's Mark | 0 hits taken = 2×, 1–4 hits = 1×, 5+ = 0× (witness-corroborated) | 20s |
| Duelist's Wager | Never stray 150 from the mark = 2×, stay within 300 = 1×, ever leave 300 = 0× | 20s |
| Blood Price | Store ≥500 = 2×, ≥200 = 1×, below = 0× | 20s |

Movement:

| Item | Effect | Cooldown |
|---|---|---|
| Warpath | Hold-to-charge, release blinks toward the cursor — up to 400 at full charge | 12s |

## Wizard (WIS 15, DPS 12)

All spells scale with WIS (`damage × (1 + wis × 0.002)`):

| Item | Effect | Cooldown |
|---|---|---|
| Arc Beam | 500×40 beam, 30 damage | 8s |
| Converge | 40 damage within 120 of the cursor (converging visual) | 12s |
| Fireball | Hold-to-charge, release hurls a fireball: up to 50 damage within 100 of the cursor | 10s |
| Arcane Spire | Spire at the cursor for 8s: enemies within 120 burn for 8 damage/s | 15s |
| Meteor | 80 damage within 200 of the cursor (shrapnel visual) | 20s |
| Arc Laser | 800×15 laser, 45 damage | 15s |

# SUP

## Witch Doctor (WIS 15, SUP 12)

Circles drawn at the cursor (radius 150). Ally effects apply to whoever stands inside and shed ~1.5s after leaving; enemy effects debuff enemies inside; bursts fire once when the circle expires. Circles die with their owner (death, logout, disconnect).

| Item | Effect | Duration | Cooldown |
|---|---|---|---|
| Warding Circle | Allies inside are immune to enemy debuffs | 10s | 20s |
| Aegis Circle | Allies inside take 30% reduced damage | 10s | 20s |
| Sanctuary Circle | Allies inside cannot drop below 1 HP | 10s | 25s |
| Mending Circle | Allies inside regenerate 3 HP/s | 10s | 20s |
| Surging Circle | Detonates at expiry: heals allies inside for 25 HP | 3s | 15s |
| Empowering Circle | Allies inside deal 20% more damage | 10s | 20s |
| Withering Circle | Enemies inside bleed for 6 damage/s | 10s | 15s |
| Ruin Circle | Detonates at expiry: 40 damage to enemies inside | 3s | 15s |
| Hindering Circle | Enemies inside are slowed 40% | 10s | 15s |
| Exposing Circle | Enemies inside take 50% more damage | 10s | 20s |
| Communion Circle | Hits on anyone inside are split evenly across everyone inside | 10s | 25s |

## Paladin (WIS 15, SUP 12 — open question: the design doc has no Paladin header; currently mirrors Witch Doctor)

The warrior's bleed and store marks as AoE auras (allies within 150, same mechanics):

| Item | Effect | Duration | Cooldown |
|---|---|---|---|
| Consecrated Brand | AoE bleed mark; sheds after 2s without taking a hit | 10s | 20s |
| Martyr's Oath | AoE bleed mark; sheds after staying near the mark 5s | 15s | 20s |
| Shared Tithe | AoE bleed mark; sheds once you damage the mark | 15s | 20s |
| Last Rites | AoE bleed mark; sheds when the mark dies | 20s | 20s |
| Martyr's Gamble | AoE store mark; no-hit wager (2× at 0 hits, 0× at 5+) | 8s | 25s |
| Templar's Wager | AoE store mark; radius wager (150 / 300) | 8s | 25s |
| Congregation's Price | AoE store mark; damage wager (500 / 200) | 8s | 25s |

Buffs:

| Item | Effect | Duration | Cooldown |
|---|---|---|---|
| Aegis of Faith | Self: invulnerable | 3s | 20s |
| Sacred Ward | Self: absorbs your next single hit | 10s | 15s |
| Guardian's Pact | Hits on allies within 150 are redirected to you | 8s | 25s |
| Prayer of Mending | You and allies within 150 regenerate 2 HP/s | 10s | 20s |
| Smite | Store the damage you take (pre-mitigation), then unleash it within 150 around you — recast to release early at the cursor | 8s | 25s |

## Tank (STR 15, SUP 12)

| Item | Effect | Duration | Cooldown |
|---|---|---|---|
| Shield Bash | Enemies within 120 take weapon damage + 34 stagger; a full stagger bar (100) stuns for 2s, then resets. Stun behavior is per-enemy (pause / slowed / enraged). An armed Retribution store rides the hit | — | 6s |
| Sundering Bash | As Shield Bash, and the stun also breaks armour (defence 0) | — | 10s |
| Bulwark | Self: invulnerable | 2s | 25s |
| Iron Stance | Self: 50% reduced damage taken | 8s | 20s |
| Retribution | Store the damage you take (pre-mitigation); at expiry the pool freezes and your next shield bash within 30s unleashes it | 8s | 25s |
| Shield Wall | 200×80 wall in front of you that erases any enemy bullet crossing it | 4s | 12s |

## Trickster (WIS 15, DEX 12, SUP 12 — the doc's `wis > dex > 0` ordering is approximated as flat minimums)

| Item | Effect | Duration | Cooldown |
|---|---|---|---|
| Venom Sigil | Curse the enemy near the cursor: 8 damage/s bleed | 6s | 8s |
| Frost Sigil | Curse: slowed 40% | 4s | 8s |
| Frailty Sigil | Curse: takes 50% more damage | 6s | 12s |
| Smoke Veil | You and allies within 150 are invulnerable | 2s | 30s |
| Blink | Instantly teleport toward the cursor, up to 300 | — | 8s |

# Deferred / not implemented

- Archer shootable dummy, trickster decoy, trickster invisibility — all need enemy retargeting onto non-player entities (future batch).
