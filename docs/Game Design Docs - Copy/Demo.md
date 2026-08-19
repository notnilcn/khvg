# Demo / Vertical Slice Scope

This file is where I ruthlessly cut the game down to something a single person can actually ship. The whole point of the demo is: **prove the bullet-hell core is fun, steal some RotMG players, worry about everything else later.** Everything in [[01 Executive Summary]] that isn't the RotMG core is shelf-ware until this demo exists and doesn't suck.

If a feature doesn't help someone dodge bullets, shoot bullets, or feel bad about dying, it's not in the demo.

## What the demo IS

- A playable bullet-hell MMO slice with real multiplayer (Godot client + SpacetimeDB server, no mock bullshit).
- One safe hub, a few biomes, a few dungeons, a few bosses.
- The tutorial flashback as the opener (see below).
- Perma-death with item recovery off your body, nexus-style teleport with the 2s delay. See [[02 Gameplay]].
- Implicit classes via gear stats (dex/str/wis), per [[03 Class system, Item system, and Equipment system]] — but only a small slice of it.
- The low-poly-3D-through-pixel-art-shader look. This is non-negotiable because it's half the selling point. If the game looks like ass nobody will care that the netcode works.

## What the demo ISN'T (the cut list)

Explicitly **not** in the demo. Not "later if time permits" — not in it. I know myself, if I leave the door open I'll walk through it.

- **No guilds.** No guild safe hubs, no alliances, no pillar territory stuff.
- **No base building.** No claiming hexes, no structures, no player-made paths between hubs.
- **No territory PvP.** No PvP of any kind, honestly. Direct PvP was already a "probably won't work in a bullet-hell" and the territory stuff is endgame content for a game that doesn't have a midgame yet.
- **No crafting/gathering skills.** No logging, fishing, mining. Artisan temperament can rot on the shelf with the rest of the BitCraft stuff.
- **No Factorio/Clash of Clans resource loop.** Shelved, see [[99 (outdated) Game Design Document]] — that whole doc is basically a museum of scope creep at this point.
- **Minimal story.** One hub, one NPC with like three lines of dialogue, done. The 3-safe-hub love-triangle storyline is post-launch. Lore stays in [[04 Lore]] where it belongs.
- **No changing/rotating world map.** The hex torus world is static for the demo. The "stale areas re-roll biomes" idea is cool but it's a persistence headache I don't need.
- **No cosmetics shop, no monetization.** Except maybe the paid instant-nexus consumable, since the delayed-vs-instant nexus is core to the death tension and I want to feel whether it plays fair before I ever charge anyone real money for it. Even that's a "maybe."
- **No enchantment/toggle meta depth beyond the minimum.** One weapon toggle to prove the system works (see below), not the full Borderlands-inspired modifier soup.
- **Only 3 of the lore safe hubs' worth of content — i.e., one.** You spawn in hub 01's aesthetic and that's the whole world map.

## Core loop in the demo

Spawn in hub → gear up from the tutorial → walk out into a biome → kill shit, level up, get drops → push toward a dungeon → die in the dungeon → run back for your body → repeat until you beat the final boss of the demo.

Concrete numbers, because vague scope is how demos die:

- **1 safe hub.** Small. Nexus portal in the middle, a gear chest/npc, a firing range dummy so people can test shot patterns. That's it.
- **3 biomes.** Something like: grassland (starter), desert (mid), corrupted/patchwork zone (hard, uses the catastrophe theming). Each biome is one zone, not some sprawling procgen continent.
- **4 dungeons.** One per difficulty step, plus one "oh god" dungeon. Portals drop from biome bosses like RotMG.
- **5 bosses.** 3 biome bosses (one each) + 2 dungeon end bosses. The 5th is the demo's final boss and should have at least one phase that forces group coordination, because that's the whole thesis.
- **~20-30 enemy types.** Enough that each biome feels distinct, few enough that I can actually tune the bullet patterns by hand.

## Classes / weapons / abilities that make the cut

Classes are implicit via gear stats anyway ([[03 Class system, Item system, and Equipment system]]), so "cutting classes" really means "cutting gear lines." The demo gets **3 archetypes**, one per stat, chosen to cover the classic trinity:

- **Warrior (str, dps)** — sword, close range, one tattoo ability (the damage-converts-to-bleed one, because it's the most interesting and teaches the bleed mechanic). No Kensei teleport-dash; that's a "cracked players solo everything" ability and I don't want anyone soloing the final boss in the demo.
- **Wizard (wis, dps)** — staff, the bread-and-butter RotMG-style straight-line shot pattern, plus one spell: the delayed beam. It's readable, it teaches "watch the telegraph," and it's satisfying as hell when it lands.
- **Witch Doctor (wis, sup)** — wand, weak shots, one ability: the mushroom-tome spire heal. Heals in a radius at a placed location. This is deliberately the group-coordination hook — I want demo players yelling "stand on the mushroom" in voice chat like it's 2012 RotMG raids.

Weapon toggles: **exactly one** — a staff toggle between low-count/high-damage and high-count/low-damage shot patterns. That's the proof-of-concept for the whole "swap-out meta without the inventory shuffling" idea. If players engage with the toggle, the system gets expanded; if they ignore it, I learned something cheap.

That's 3 weapons, 3 abilities, 1 toggle. No tank, no paladin, no trickster, no gunner. I know, the gunner's damage-storage kit is cool. It's also a tuning nightmare for a first contact with real players.

## The tutorial flashback (demo opener)

Straight from [[99 (outdated) Game Design Document]] and it stays: player opens the game, spawns **before** the catastrophe with endgame gear — full stacked stats, a weapon with the fun shot pattern, abilities loaded. They fight a horde during the gate malfunction sequence, do huge damage, feel like a god. Then the scripted defeat happens: the patterns become literally undodgeable, you die no matter what, smash cut to the post-catastrophe hub with nothing.

Two reasons this is in the demo even though it's "extra work":

1. It's the hook. "You start as a god and the game takes it away in five minutes" is the entire pitch in playable form.
2. It's the only build hint players get, per the design — the endgame loadout you spawn with quietly shows what a maxed dex/str/wis build looks like. I need to know if players pick up on that at all.

Sweat bait: timer on screen during the flashback, track longest survival. Costs me one UI element and gives streamers something to do. The record will be cheated within a week and that's fine, it's free marketing.

## Multiplayer scope

- Real SpacetimeDB server from day one. The demo's job is partly to find out whether SpacetimeDB holds up for a bullet-hell's update rate, and I can't learn that with a fake local server.
- **Target: ~30-50 concurrent players on one server/world.** Enough to fill a hub, form ad-hoc dungeon groups, and make the world feel alive. Not trying to stress-test thousands; that's a launch problem.
- Server authoritative for bullets and damage. Client-side prediction only for movement, and only if the rubber-banding makes me want to die. Otherwise keep it simple.
- Perma-death in the demo: full rules as designed — lose stats/skills, recover items from your body before the timer or dungeon closeout. **BUT** I'm keeping a server-side rollback switch for bullshit deaths (lag spikes, my own bugs). In RotMG, dying to a lag spike with no appeal was genuinely the worst feeling, and the demo population will be small enough that I can just review deaths manually like a caveman.
- Death dialog: the "do you want to be respawned? yes/no, no closes the game" bit stays. It's cheap and it's the grim-hope vibe in one dialog box.

## Success criteria

How I decide the demo worked, in rough priority order:

1. **Do strangers re-roll after dying?** If someone loses a character they spent an hour on and immediately makes a new one, the loop works. If they close the game and never come back, the loop is dead no matter how good the netcode is.
2. **Do groups form spontaneously for dungeons?** If the mushroom-spire coordination moment happens even once in a random pug, the group-coordination thesis holds.
3. **Does anyone clip it?** Someone posting a clip of a close dodge or a death is worth more than any survey. RotMG's whole culture is clips of people dying to O3 counters.
4. **Server survives a weekend** with ~50 concurrent without me babysitting it.
5. **RotMG players say "this feels like RotMG but..."** — the "but" tells me what to build next.

Explicit non-goals: player counts, retention metrics, revenue. It's a demo, not a business.

## Milestones (rough order)

1. **Movement + camera.** Top-down, rotatable/pannable/zoomable 3D camera, hex-torus world that doesn't make anyone seasick. The camera rotation is the differentiator so it gets built first, not bolted on.
2. **Client-server bullet sync on SpacetimeDB.** Player shoots, bullet moves, enemy takes damage, two clients see the same thing. The scariest technical unknown, so it goes early.
3. **Bullet patterns + one enemy type per pattern family.** Spread, aimed, ring, spiral. If dodging these isn't fun with placeholder cubes as enemies, no art will save it.
4. **Nexus teleport (2s delay) + safe hub.**
5. **Perma-death + corpse item recovery.** This is where the game becomes *the game* and not a tech demo.
6. **Stats + gear + the 3 archetypes.** Implicit classes via stat thresholds, the one weapon toggle.
7. **Tutorial flashback.** After systems, before content — it needs real gear to show off.
8. **3 biomes, 4 dungeons, 5 bosses.** Content pass, hand-tuned patterns.
9. **The pixel-art shader pass.** Yes, late. Gameplay first, pretty second. It'll look like programmer art for months and that's fine.
10. **Playtest weekend with actual RotMG players.** Recruit from wherever they're complaining about Deca this week. This milestone is the demo. Everything before it is pre-production.

The rule for all of the above: if I'm stuck choosing between "add a thing" and "make a thing feel good," feel good wins every time. RotMG has jank everywhere and people still play it because the core dodge-and-shoot feels right. That's the bar.
