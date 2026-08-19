# Biomes overview
The world is a hexagonal grid wrapped into a torus, and ~75% of it got patchwork-merged with other worlds when the wizards fucked up the teleportation gates (see [[04 Lore]]). Each "biome" isn't really a biome in the Terraria sense — it's a literal chunk of another planet that got stapled onto ours. That means biomes don't have to obey any coherent geological logic. A frozen dead ocean can sit directly next to a jungle that glows in the dark because they came from different worlds.
The teleportation gates are still malfunctioning, which means the world is never static. See the reroll mechanic below.

# Biome tiers / danger zones
Danger scales with distance from the safe hubs. Roughly:

- **Tier 0 — Safe hubs.** The surviving patches of the original world. No monsters, no bosses, waypoints everywhere. This is where new players spawn after the tutorial.
- **Tier 1 — Starter biomes.** Ring around the hubs. Original-world patches, mostly intact. Light monsters, low-damage bullets, resources are common-but-boring (wood, stone, iron-tier metals). A fresh character can farm here without instantly dying.
- **Tier 2 — Mid biomes.** Where the foreign patches start showing up. Moderate bullet density, first area bosses, first biome-gated resources. This is where you start needing to actually dodge instead of face-tanking.
- **Tier 3 — Deep biomes.** Mostly foreign. Heavy bullet hell, you need a group or cracked movement. Rare resources.
- **Tier 4 — Nightmare biomes / epicentre zone.** The patches closest to the apocalypse epicentre. The daily pulses (see below) hit hardest here, so these biomes are the most warped and reroll the most often. The resources here gate the highest-tier gear. These are "you will die, plan accordingly" zones.

The tier isn't fixed per-biome-forever though — because of rerolls, a patch that was Tier 1 yesterday can be Tier 3 tomorrow. More on that below. The tier is a property of the current patch's contents, not of the map location. The only thing that's actually stable about the map is proximity to hubs and proximity to the epicentre, which bias *what* a region rerolls into.

# The reroll / mutation mechanic
This is the part that makes the world feel alive and it's also the part that will be the biggest pain in my ass to implement.

## Basic rules
- Every region of the map (some cluster of hexagons, call it a "tile" or "chunk" — implementation detail) tracks the last time any player entered it.
- If a region hasn't been visited for **n days** (probably n=3, tune later), it becomes "stale".
- The next time a player enters a stale region, we lazily evaluate: roll a die, and with some probability the region rerolls into a different biome.
- Lazy evaluation is important. I am NOT running a cron job that ticks every region on the planet every day. That shit doesn't scale and it's pointless — nobody cares about a biome nobody's looking at. Quantum mechanics rules: it doesn't exist until observed.

## Probabilities
Gut-feel numbers, will tune:
- Chance a stale region rerolls at all: **~20%** per staleness check.
- Staleness is re-checked every time the region is entered after the n-day threshold, so if you keep visiting a region every day it effectively never rerolls. Active farming routes are stable. Forgotten corners of the map churn. That's the intended feel.
- If a reroll happens, the new biome is picked from a weighted table biased by:
  - distance from the safe hubs (hubs push toward low tiers),
  - distance from the epicentre (epicentre pushes toward high tiers),
  - the biome it used to be (small self-similarity bias, like 10%, so the world doesn't feel like white noise),
  - what its neighbours currently are (small bias so you get patches/clusters of similar biomes instead of pure confetti — the world should look like a quilt, not a dice roll).

## What happens to player structures when a region rerolls
**Protected by anti-teleportation pillars (my pick).** Guild territories are already defined by anti-teleportation pillars that eat resources to stay up (see [[04 Lore#Guild territories and guild content]]). So: anything inside a maintained pillar's radius is exempt from rerolls. The pillar is literally doing what the wizards' gates failed to do — holding the local patch of world in place. If the pillar runs out of resources, the protection lapses and the region becomes eligible to reroll again, structures and all.
## The epicentre and the daily pulses
There's an apocalypse epicentre — the ground zero of the gate catastrophe — that sends out a magical pulse roughly once per day (from the old GDD, still canon).

- Each pulse **warps the landscape a little** in an expanding ring: visual corruption, monsters get a temporary buff, chest/resource spawns get shuffled, that kind of thing.
- Each pulse also **bumps the reroll probability** of every stale region it passes over for the next day. Near the epicentre the pulses are frequent/strong so the world churns fast; far away the pulse arrives weak and late so the world is stable. This is the mechanical justification for the tier gradient — the epicentre end of the torus is nightmare land not just because the biomes are nastier but because nothing there holds still long enough to be safe.
- Fun emergent thing: players can predict pulse timing and either hide or go farming the post-pulse resource shuffle. Daily login incentive that isn't a chore, hopefully.

# Waypoints
Waypoints sit on hexagons near the inner diameter of the torus. Lore reason: teleporting "through the donut hole" is safe because the inner-diameter hexes are closer together, so short-range teleportation doesn't blow up the world (again). Long-range teleportation across the outer diameter is exactly what killed the planet, so no, you can't teleport from anywhere to anywhere. Walk, or route through the hole.

Gameplay consequence: waypoints are unevenly distributed on purpose. Getting to a deep biome means teleporting to the nearest inner-diameter waypoint and then *walking the rest of the way*, which is where the danger actually lives. Fast travel is a convenience, not a skip button.

# The biomes
Names are all placeholder-ish until I commit. Each biome has: what it looks like, what resources it gates (Factorio: Space Age rules — you need ALL of them eventually, that's what forces trade between guilds), its area boss, and what world the patch came from. No living intelligent aliens — fallen civilisations only, per [[04 Lore]].

## 1. The Verdant Remnant (Tier 0-1)
**Look:** the last big surviving chunk of the original world. Green fields, normal trees, normal-ish sky. Colorful glowing flowers dotted around because even the original world was a magic world. Should feel like the "before" in a dark fairytale.
**Resources:** wood, food crops, basic stone, iron. Nothing rare, but you need a LOT of it for territory upkeep.
**Area boss:** **The Old Shepherd** — a giant weathered stone golem that used to be some farmer's field-warden construct. Fight gimmick: slow rotating bullet rings that speed up every time someone breaks one of its armor plates. Teaches new players the "stagger then burst" meta. Drops a mountain of iron/wood.
**Origin lore:** original world, pre-merge. The golem is still doing its job from before the apocalypse, guarding a field that hasn't been farmed in years. Grim-hope in a nutshell.

## 2. The Hollowing (Tier 1-2)
**Look:** Hollow Knight-ass biome. Dead grey forest, fog, bioluminescent blue fungus, giant pale roots. Everything hums faintly. My personal favorite.
**Resources:** fungal resin (alchemy/crafting base), glow-wood (light sources, lamp fuel), spore extracts for potions.
**Area boss:** **Mother Cap** — a colossal fungal matriarch rooted in the center of the patch. She doesn't move; instead the *arena* attacks you. Bullet gimmick: spore clouds that drift and bloom into bullet flowers when touched, plus walls of fungal threads that restrict the arena over time if you don't burn them down. Group coordination fight: someone has to clear threads while everyone else dodges.
**Origin lore:** a world where a single fungal hivemind consumed everything and then starved. The remnants are its corpse, still fruiting. Fallen civilisation flavour: there are fossilised "worship totems" of whatever lived there before the fungus won.

## 3. The Ashen Reach (Tier 2)
**Look:** volcanic badlands. Black basalt hexes, lava channels, ember particles everywhere, orange glow against dark rock. Very "fire world from every game ever" but through the pixel shader it looks great.
**Resources:** obsidian, sulfur, emberglass (heat-tier crafting material — needed for smelters/refineries).
**Area boss:** **Kilnfather** — a magma tortoise the size of a village. Gimmick: it periodically submerges into lava and re-emerges somewhere else in the arena, flooding its old position with fire geysers. Stationary players die. It also sheds molten "shell ticks" that chase players and explode into bullet bursts — the RotMG "don't stand on the minions" lesson.
**Origin lore:** a world that went through its own apocalypse (runaway volcanic winter) long before ours. The ruins of their heat-temples are half-sunk in the lava.

## 4. The Drowned Vault (Tier 2)
**Look:** a dead ocean floor that got merged *without its water*. Cracked seabed, coral towers, shipwrecks, kelp forests swaying in air like they're still underwater (they move with the magic currents — free excuse for pretty animation). Occasional floating pockets of water you can drown in mid-air, which is funny the first time and infuriating the fourth time.
**Resources:** salt, pearlsteel (lightweight high-tier metal), abyssal coral (used for healing-item crafting).
**Area boss:** **The Tide Regret** — a ghost-whale that swims through the air. Gimmick: it's untargetable while "diving" (phases through the floor, leaving bullet wakes on the surface like ripples), and only becomes vulnerable when it breaches. Classic "wait for the window, then dump everything" fight — gunners with stored-damage kits feast here (see [[03 Class system, Item system, and Equipment system#Gunner (dex) & (dps)]]).
**Origin lore:** a world whose oceans were teleported somewhere else. Whatever civilisation lived on that seafloor suffocated in the open air. Their vault-cities are still down there, sealed.

## 5. The Glass Steppe (Tier 2-3)
**Look:** endless plains of translucent glass grass that chime when walked on. Light refracts everywhere. Beautiful and deeply annoying — enemies are hard to see against the ground, which is intentional and I will not apologise.
**Resources:** sunglass (lenses, scopes, focus items — wizard gear material), silica dust, prismatic shards (enchantment reroll material, high demand).
**Area boss:** **The Prism Herd** — not one boss, a stampede. Seven-ish crystalline elk-things that charge in telegraphed patterns, each one leaving behind a lattice of laser beams that persists until that elk dies. Kill order matters because the beams overlap into death grids. Very "dodge the wall you let them build" energy.
**Origin lore:** a desert world where a civilisation figured out how to grow glass like crops. They glassed their own planet by accident (agriculture win, ecosystem loss). The elk are what's left of their livestock.

## 6. The Gloaming Weald (Tier 3)
**Look:** perpetual twilight jungle. Everything glows — neon vines, violet trees, pollen motes drifting like snow. The "colorful glowing plants" pillar of the art direction lives here at max volume. Hostile as hell despite being pretty, which is the joke.
**Resources:** lumen sap (magic ink base for tattoos — warriors want this, see [[03 Class system, Item system, and Equipment system#Warrior (str) & (dps)]]), glowfruit (consumables), moth-silk (light armor).
**Area boss:** **Queen of the Moth Season** — a giant lunar moth. Gimmick: she sheds luminous scales that fill the arena as slow-drifting bullet fog; her wingbeats periodically gust the entire scale-field in a direction, turning the ambient fog into directed bullet walls. The fight is about reading wind telegraphs. Stand in the wrong place when she flaps and you eat the whole sky.
**Origin lore:** a tidally-locked world's night side. Its civilisation worshipped the moths that carried their dead; the jungle outlasted the worshippers.

## 7. The Clockwork Wound (Tier 3)
**Look:** a ruined machine-civilisation patch. Brass towers, broken gear-trees, oil rivers, ticking everywhere. The only "tech" biome — but it's dead tech, nobody's home, so it doesn't break the no-living-aliens rule. Fits the no-science-coding constraint because it's all *arcane* machinery, gears powered by bound spirits or whatever. It's magic with extra steps.
**Resources:** gears, coil-copper, motive cores (automation components — you NEED these for the Factorio endgame, and only from here).
**Area boss:** **The Last Foreman** — a towering automaton still running its quota. Gimmick: it rebuilds itself from the arena — conveyor belts feed scrap toward it, and if players don't destroy the scrap in transit the boss heals and gains armor plates that add new bullet patterns. DPS check + target-priority check. Raid-leader "STOP HITTING THE BOSS, HIT THE CONVEYORS" energy.
**Origin lore:** a world that automated everything and then had nothing left to do. The machines kept building after the builders died of pure obsolescence. Allegory? idk maybe.

## 8. The Salt Chancel (Tier 3)
**Look:** a blinding white desert of salt flats with ruined cathedral-city bones sticking out. Mirage shimmer, wind chimes made of bone, very "the gods left" energy.
**Resources:** blessed salt (anti-corruption crafting — pillar upkeep uses this!), relic gold, incense minerals.
**Area boss:** **The Choir Absent** — a floating cathedral-organ golem thing. Gimmick: it attacks in rhythm. Bullet patterns are synced to an audible chant, and the "verses" repeat — once you learn the song you can dodge it on beat. Phase changes switch the tempo. Music nerds will be insufferable about this boss and I support them.
**Origin lore:** a theocratic world that prayed its gods into leaving (asked too much, got abandoned). The cathedrals still sing to nobody.

## 9. The Howling Barrens (Tier 3-4)
**Look:** wind-scoured grey wasteland, constant screaming wind, floating rock shards, thin air vibe. Nothing glows here and that's the point — it's the anti-Hollowing.
**Resources:** storm iron (conductive metal for advanced gear), windglass, shriek glands (consumable crafting — sound-based items? idk, sounds cool).
**Area boss:** **The Siren of Nowhere** — a massive blind wyvern that hunts by sound. Gimmick: your *actions* generate noise that it targets. Shooting is loud. Abilities are loud. Standing still and walking is quiet. Whole raid has to play red-light-green-light against a bullet hell boss. High-damage builds trigger its frenzy (thematic callback to the blind boss idea in [[03 Class system, Item system, and Equipment system]]). Tricksters with decoys are MVP here.
**Origin lore:** a world whose atmosphere is being slowly stolen through a gate tear that never closed. Everything there died holding its breath. Cheery!

## 10. The Epicentre Scar (Tier 4)
**Look:** the ground zero of the merge. Geography barely holds together — hexes tilt and drift, gates flicker in and out of existence, raw unshaped magic leaking everywhere like auroras on the ground. Every daily pulse visibly originates here.
**Resources:** unshaped mana (the top-tier universal crafting catalyst — needed for the best gear and the biggest pillar networks), gate shards (waypoint construction!), ichor of the Between (god-tier tattoo ink).
**Area boss:** **The Seamstress** — the thing that got stuck halfway through the gates when they merged the worlds. Not evil, not even really alive in the normal sense — more like a wound in the shape of a spider, endlessly stitching patches of different worlds together. Gimmick: she *rewrites the arena mid-fight* — floor hexes swap to other biomes, importing those biomes' hazards (lava from Ashen Reach, drown-pockets from Drowned Vault, etc). Every pull is different. The "final boss of the overworld" until I come up with something worse.
**Origin lore:** not from any world. From between them. The gates didn't just open doors — something was using the doors already.

## Extra biome ideas (bench)
- **The Orchard of Teeth** — carnivorous fruit forest. Tier 2. Boss: a harvest-queen thing. Maybe too gross, maybe perfect.
- **The Paper Sea** — an ocean world merged as a frozen single frame, waves locked mid-crash like a painting. Mostly vibes, no resource idea yet.
- **The Mimic Fields** — looks exactly like the Verdant Remnant. It is not the Verdant Remnant. Tier 4 jumpscare biome. Reroll-only spawn, never appears fresh. This one's definitely in, it's too funny.

# Area bosses as logistics (boss-dragging)
This is in [[02 Gameplay]] but worth repeating because it's biome-coupled: every area boss can be aggroed and dragged across the world into other biomes, and bosses drop a truckload of their biome's gated resources when killed. So one of the main "resource transport" strategies is literally herding a boss home and killing it in your territory like a walking cargo ship. A cargo ship that is actively trying to murder your guild the entire walk home.

Design notes:
- Dragging a boss out of its home biome should make it *stronger* somehow (enrage timers, it picks up the local biome's hazards) so it's not free. Risk scales with distance dragged.
- Bosses dragged into another guild's territory will obviously be used as siege weapons. That's not an exploit, that's content.
- A boss dragged through the epicentre pulse should do something horrible. idk what yet. Probably mutates.

# How this feeds the guild endgame
The whole biome resource spread is the trade engine:
- No single territory can sit on every biome type, and the reroll mechanic means no territory can *rely* on its local biome staying put. You either maintain trade routes, or you drag bosses home, or you raid.
- Pillar upkeep eats Verdant Remnant bulk resources + Salt Chancel blessed salt, so even Tier 4 guilds are tethered to the starter biomes. The low tiers stay economically relevant forever — new players farm stuff that veterans genuinely need, which is the healthiest low-level economy I can think of.
- Motive cores only come from the Clockwork Wound, gate shards only from the Epicentre Scar, lumen sap only from the Gloaming Weald, etc. Factorio: Space Age rules: every planet/biome has something you can't get anywhere else, and the tech tree touches all of them.
- The reroll mechanic doubles as a map reset: if one guild monopolises a rich region, the world will eventually just... move the biome. You can't hold a resource forever, you can only hold *logistics*. Empires built on routes, not rocks. That's the thesis.

# Open questions
- Exact reroll table weights. Needs simulation, not vibes.
- Should players get a warning that their unprotected region is stale ("the ground feels thin")? Probably yes, some diegetic sign a few days before reroll eligibility. Being eaten by the world should feel like neglect, not a dice roll out of nowhere.
- Whether bosses respawn on a timer or only when the biome rerolls. Leaning timer, with reroll as a bonus spawn.
