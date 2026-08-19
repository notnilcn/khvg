# Classes, Weapons, Abilities — actual item catalogue

Okay so [[03 Class system, Item system, and Equipment system]] covers the *systems* (stats, temperaments, slots, enchantment rules). This file is where I actually name the damn items. There's a bunch of overlap between this file and that one and honestly idk which one should be canonical — probably this one for specific items and 03 for the rules, but if they ever contradict each other, assume whichever one I wrote most recently is the one I meant. Sorry, future me.

Damage numbers below assume the flat-defence math from [[02 Gameplay#Buffs, debuffs, and damage calculation nuances]] — defence subtracts per bullet, min damage 1, so shot count vs shot damage is THE tradeoff everything is built around.

# Tiering scheme

I'm going with 6 tiers because RotMG's 12-ish tiers always felt like padding and Terraria's "every ore is a tier" thing is a crafting nightmare. Tiers are mostly about *where* it drops, not just bigger numbers — higher tier items get more enchantment slots, which is where the real power is.

| Tier | Name | Where it comes from | Enchantment slots |
|------|------|---------------------|-------------------|
| T0 | Patchwork | Starting gear, story quests, tutorial | 0 |
| T1 | Salvaged | Trash mobs, early dungeons | 1 |
| T2 | Reclaimed | Mid dungeons, biome elites | 2 |
| T3 | Resonant | Hard dungeons, biome bosses | 2 |
| T4 | Gateforged | Endgame dungeons, area bosses | 3 |
| T5 | Ichor | Endgame area bosses, world events | 3 + 1 innate |

The naming scheme is teleportation-catastrophe flavored: everything good was either salvaged from the wreckage or forged out of gate-magic residue. "Ichor" tier is the white-bag tier — see the chase items section.

Also: the whole point of the enchantment system (see 03) is that a T2 weapon with a cracked roll should sometimes beat a fresh T4. A T2 with 2 perfect enchantments vs a T4 with 3 garbage ones should be a real decision, not a no-brainer. If that stops being true in playtesting, the tier stat gaps are too big and I'll shrink them.

# Enchantments

Everything from the buffs/status list in 02 can show up as an enchantment. Player-modifiable slots, plus innate ones that can't be changed but don't eat a slot. Quick reference of the enchantment pool:

**Damage enchantments (shot-count manipulation):**
- **Splitshot** — halve per-bullet damage, double bullet count. The "make my gun a shotgun" button.
- **Hollowpoint** — +25% true damage per bullet, ignores defence. Great on low-shot-count weapons.
- **Weight** — +flat true damage per bullet. Great on high-shot-count weapons. Splitshot + Weight on the same weapon is the classic combo.
- **Hair Trigger** — +20% fire rate. Boring but honest.
- **Prejudice** — converts shot pattern to a point-blank radial burst. The Staff of Extreme Prejudice enchantment. Useless at range, deletes staggered bosses. As an *innate* on a high-tier weapon it's a minmaxer's lottery ticket.

**Utility enchantments:**
- **Longhaul** — +30% range
- **Quickburn** — +25% bullet speed
- **Featherfoot** — +10% move speed while equipped
- **Lifedrinker** — heal for 2% of damage dealt (this one might be broken, we'll see)

**Cursed enchantments (strong effect + a status condition from 02):**
- **Desperate** — +40% damage, but you're cursed: damage taken is stored and doubled if you don't shed it.
- **Oathbound** — +50% damage, can't use abilities. For people who hate fun / love numbers.
- **Glassblower's Blessing** — double bullet count, halves your own defence. Named after a guy in the guild system I haven't designed yet.

# Classes

Reminder: classes are implicit — the "class" is just what stat thresholds your gear demands. Everything below lists its requirement.

## Gunner (dex > 0, damage dealer)

The delay-and-dump class. Lower sustained DPS than the others on paper, but they store damage through invulnerability phases and dump it during vulnerability windows, so in real fights they top meters. See 03 for the design intent.

### Gunner weapons

- **Rustwork Carbine** (T0) — 3 shots, 12 dmg each, straight line. The tutorial gun. It's fine.
- **Salvaged Repeater** (T1) — 5 shots, 9 dmg, slight spread. Toggle: spread ↔ tight line.
- **Longstep Rifle** (T2) — 1 shot, 85 dmg, piercing. Toggle: single piercing slug ↔ 4-shot fan at 22 dmg each. This is the weapon that teaches players the defence math the hard way.
- **Gatetaker's Musket** (T3) — 2 shots, 60 dmg. Innate: **Longhaul**. The lore is it's made from a broken teleport gate strut. It's a lie, it's made by a vendor, but it's good marketing.
- **Whisperburst** (T4) — 8 shots, 14 dmg, fires in a spiral pattern because why not. Toggle: spiral ↔ 2 tight bursts of 4. Rolls well with **Weight**.
- **Alice's Apology** (T5, chase) — see chase items.

### Gunner abilities

Gunner abilities are weapon modifications and trick shots. Slot costs in brackets.

- **Overcharge Cylinder** [2] — big charge shot. Self-slow while charging, single shot that scales with charge time. Full charge ≈ 4x base shot damage.
- **Scatter Drum** [2] — big volley shot. Same charge mechanic as Overcharge but releases a huge spread. Shreds low-defence mobs, tickles anything armoured.
- **Echo Target** [1] — spawn a dummy at cursor location that allies can shoot; absorbs all damage dealt to it. Re-activate to consume the stored damage into your next shot. The group-coordination toy.
- **Bullseye Sigil** [1] — mark an enemy: it takes +15% damage from everyone for 8s. Stack this with the tank's armour break and watch health bars evaporate.
- **Counteredge** [1] — quick slash in front of you that destroys enemy bullets weaker than the slash. High-skill bullet-cancelling. Feels amazing when it works.
- **Gatehook** [1] — grapple to a point on the ground. You still take damage mid-grapple because I want it to be a repositioning tool, not a free dodge. Learned that one from RotMG's trickster teleports into walls.

## Warrior (str > 0, damage dealer)

Abilities are magic tattoos, Witch Hat Atelier style, inked in monster blood or god ichor (see 03). Tattoos are consumable — inking a new one overwrites an old one, and the ink tier gates the tattoo tier. They should glow on activation. This is non-negotiable, it's too cool not to do.

### Warrior weapons

Swords. Some of them send slashes because Terraria was right about everything.

- **Boarding Cleaver** (T0) — melee arc, 30 dmg, no projectile. Welcome to being a warrior, get used to hugging things.
- **Salvaged Falchion** (T2... no wait, T1) — melee arc, 45 dmg, sends a weak 10 dmg slash wave. The wave is mostly there to make new warriors feel cool.
- **Riftbrand** (T2) — melee arc 60 dmg + 3 slash projectiles at 15 dmg. The projectiles count as bullets for defence math, which matters.
- **Gatebreaker** (T3) — melee arc, 140 dmg, slow swing. The "defence doesn't scare me" sword. Innate: **Hollowpoint** on the melee hit.
- **Comet Edge** (T4) — melee arc 90 dmg + a single huge slow slash projectile at 120 dmg. Looks like a meteor trail. I want the VFX to sell this one.
- **The Last Argument** (T5, chase) — see chase items.

### Warrior abilities (tattoos)

Tattoos are 1 slot each — warriors get to mix and match the most, which fits the consumable-ink gimmick. You carry inks, you swap tattoos mid-dungeon if you're a tryhard.

- **Second Skin** [1] — for 3s, all incoming damage converts to bleed instead of instant damage. Shed condition: deal damage equal to the bleed pool before it ticks out. Panic button with a homework assignment attached.
- **Boiling Ink** [1] — for 5s your sword hits apply burn. Simple, effective, looks great with the glow.
- **Ledger** [1] — for 4s, store ALL damage you deal, then deal it again as one hit at the end. Fail condition (if you take >25% max HP during the window): deal nothing. High risk, huge reward, will 100% cause forum arguments.
- **Blink Rune** [1] — windup animation, then a short teleport. The Kensei-style dodge. Cracked players solo bosses with this and this alone, and honestly? Let them. That's the fantasy.
- **Sympathetic Wound** [1] — convert all damage dealt AND received into bleed for 5s. Combo with Second Skin and you become a weird damage-washing machine. I don't fully understand the math of this combo yet which means players will break it.
- **Mute Titan** [1] — for 6s, your damage can't aggro bosses past their damage-threshold tantrum phases. Exists specifically for the blind-boss idea in 03 where dealing too much damage makes the fight worse.

## Wizard (wis > 0, damage dealer)

Staves and wands like RotMG, and spell items that eat 2-3 slots because big magic needs big pockets.

### Wizard weapons

- **Charred Wand** (T0) — 1 shot, 25 dmg. It's a stick that's on fire a little.
- **Salvaged Staff** (T1) — 2 shots, 20 dmg, slight homing wobble.
- **Choir Staff** (T2) — 4 shots, 14 dmg, fires in a slow rotating pattern. My "S.T.A.F.F. at home" — same energy as the RotMG staff, not a 1:1 copy of the bullet pattern, lawyers please stop reading here.
- **Wand of the Inner Diameter** (T3) — 1 shot, 110 dmg, piercing, +innate **Longhaul**. Lore: fires through the donut hole of the torus world, which is why it never misses terrain. Flavor justification for infinite pierce, basically.
- **Starfall Scepter** (T4) — 3 shots, 55 dmg, each shot explodes on impact into 3 fragments at 10 dmg. Fragment weapons are defence-math hell and that's the point.
- **The Unsent Warning** (T5, chase) — see chase items.

### Wizard abilities (spells, tomes, scriptures)

Spells eat 2 slots, tomes/scriptures eat 3. You want a scripture, you're giving up half your bar.

- **Skywrit** [2] — delayed beam of light at cursor location, 2s warning glyph, then bonk. The Chaotic Scripture tribute.
- **Convergence** [2] — summons bullets that converge on your cursor from offscreen. Penetrating Blast Spell energy. Great for hitting things behind other things.
- **Pyre Incantation** [2] — stop shooting, long windup, launch a big slow fireball (300 dmg). The "I am a wizard and I have decided" button.
- **Spire Scripture** [3] — summon a spire that zaps nearby enemies for 6s. Offensive mirror of the witch doctor's healing spire.
- **Meteor Concordance** [3] — call down a meteor (200 dmg) that shatters into 8 shrapnel bullets (25 dmg each). Bosses with high defence just eat the meteor; mobs get blended by the shrapnel.
- **Longbeam Thesis** [3] — channeled laser, 100 dmg/tick, drains mana while held, self-slow. The "delete one guy" spell.

## Witch Doctor (wis > 0, supporter)

Same staves/wands as wizard but tuned weaker — their power budget is in the circles. All the circle tomes from 03/Inspirations live here, and yes I am keeping the mushroom tome push-up tech, that story is too good to waste.

### Witch Doctor weapons

- **Knotted Staff** (T0) — 1 shot, 18 dmg.
- **Rattler's Wand** (T2) — 3 shots, 12 dmg. Innate: heals you for 1% of damage dealt. Diet Lifedrinker.
- **Grudgebone Staff** (T4) — 5 shots, 16 dmg, leaves a brief damaging miasma where bullets land.

### Witch Doctor abilities

Mostly circles. Circles are 1-2 slots, the big tomes are 3.

- **Sporespire Tome** [3] — the mushroom tome. Spawn a spire that heals everyone in a radius. Puts the "push up, stand where the tomes are" coordination back in the game.
- **Circle of Second Chances** [3] — zone where allies can't drop below 1 HP. The "oh god oh god" button.
- **Mending Ring** [2] — heal-over-time circle.
- **Festival Ring** [2] — allies in the circle deal +15% damage. Drop it on the boss, yell at everyone to stand in it.
- **Bitterroot Circle** [2] — enemies in the circle take damage over time. Off-brand but fun.
- **Tar Circle** [1] — slows enemies in the zone.
- **Debt Circle** [2] — all damage taken inside the circle is split evenly among everyone standing in it. The group-coordination one. Either saves the run or gets five people killed by one bullet. No in-between.
- **Purge Draught** [1] — targeted debuff cleanse on lowest-HP ally near cursor.
- **Witch's Fare** [1] — burst heal after a 3s delay. Time it or waste it.

## Warden (str > 0 AND wis > 0, supporter)

Renamed from "Paladin" because there are no religions in this world (see [[04 Lore]]). Candidates were Warden, Aegis, Banneret, "the guy with the auras". **Warden** wins: it's a guard duty word, fits a world where the guards failed to guard the world, and it doesn't smell like a church. If I ever change my mind, this paragraph is where I'll find out.

Wardens have less defence than tanks but a bigger health pool — weak to machine-gun enemies, strong against big single hits. Their abilities are passive-ish auras and party buffs; like 03 says, some just work by being equipped.

### Warden weapons

- **Garrison Blade** (T1) — melee arc, 40 dmg.
- **Beacon Brand** (T3) — melee arc, 70 dmg, innately emits a tiny heal aura (2 HP/s, small radius). The weapon itself is a ward.
- **Oathkeeper** (T4) — melee arc, 100 dmg + 2 slash projectiles at 18 dmg.

### Warden abilities

- **Rally Standard** [2] — aura: nearby allies deal +10% damage. Passive while equipped, active pulse for +20% for 4s.
- **Second Wind Sigil** [2] — aura: nearby allies get increased health regen.
- **Martyr's Seal** [2] — redirect all damage party members take to yourself for 4s. Combo with a tank friend or a death wish.
- **Aegis Vow** [2] — immunity to the next single hit within 6s, for the whole party. The cheap version of full invuln.
- **Reckoning** [3] — store all pre-mitigated damage you receive for 5s, release it as a smite (radial burst). Eat a meteor, return a meteor.
- **Bleedwater Benediction** [2] — party-wide: convert damage dealt/received into bleed for 5s. Weird party-wide version of the warrior tattoo tech.
- **Grand Reversal** [3] — party-wide Ledger: store all damage dealt by the party for 4s, then multiply it if the party collectively meets the fail condition threshold. The raid-finisher. Probably needs the boss-aggro counterbalance from 03 or it's free.

## Tank (str > 0, supporter)

The stagger-and-armour-break class. Some endgame bosses should make tanks relevant ONLY for their debuffs — see 03. Shields eat all 6 slots which means a shielded tank is a pure ability bot, and that's fine, that's the fantasy.

### Tank weapons

- **Salvaged Maul** (T1) — melee arc, 50 dmg, slow.
- **Bulwark Blade** (T3) — melee arc, 80 dmg, innate: +10 defence while equipped.
- **Pillarcracker** (T4) — melee arc, 120 dmg, chunks stagger bar on hit.

### Tank abilities

- **Aegis of the Fallen Gate** [6] — the full shield. Shield wall that absorbs all bullets that hit it, +40% damage reduction, can shield bash. Your entire bar is this shield. You are a wall with legs.
- **Stagger Plate** [2] — shield bash that fills the stagger bar. The RotMG knight stun, but a bar instead of a binary stun so bosses can have stagger resistance curves.
- **Exposing Bash** [2] — bash that applies armour break (defence → 0 for 3s) or expose (+10% damage taken, bypasses defence) depending on the enemy's stagger state. The Ogmur tribute. High-shot-count teammates will love you forever.
- **Granite Oath** [1] — -50% incoming damage for 4s.
- **Unmoved** [2] — full damage immunity for 2s. Short, deliberate, expensive.
- **Backswing Battery** [1] — store pre-mitigated damage received for 4s, release as a shield bash. The tank version of Reckoning, smaller numbers, more frequent.

## Trickster (wis ≳ dex, supporter)

Assassin + rogue + trickster from RotMG in a trench coat. Their magic is tied to physical trick items — cloaks, vials, mirrors — not scrolls. Still not sold on daggers (see 03), so they get guns and crossbows. Whatever. It works.

### Trickster weapons

- **Hushpuppy** (T1) — 2 shots, 15 dmg, silent. Yes I named a gun Hushpuppy. No I will not be taking questions.
- **Wisp Crossbow** (T2) — 1 shot, 70 dmg, bolt curves toward the nearest enemy slightly.
- **Misdirection** (T4) — 4 shots, 20 dmg, bullets visually appear to come from a random direction. Pure flavor in PvE, but I want it anyway.
- **The Getaway** (T5, chase) — see chase items.

### Trickster abilities

- **Unkindness Cloak** [2] — invisibility for 5s, drops aggro. Breaks on shooting.
- **Blackglass Vial** [1] — thrown poison: enemies take DoT and receive +10% damage.
- **Misstep Charm** [1] — short teleport. Less windup than the warrior's Blink Rune but shorter range. The trickster dodges by being elsewhere, the warrior dodges by being brave.
- **Mirror of Erised**... no. **Mirror of Regret** [2] — summon a decoy mirror image that takes aggro for 6s. (The first name was a joke, lawyers.)
- **Pocketworld Latchkey** [3] — teleport the whole party into a pocket dimension: invisible + immune for 3s, then everyone pops back out where they were. The "skip the bullet hell phase" button. Chase-tier ability item, drop rate will be sadistic.

# Chase items (the white bags)

These are the items people clip themselves getting. All T5, all drop from endgame area bosses or catastrophe-themed world events, all tied to the teleportation merge lore from [[04 Lore]]. Names should feel like artifacts of the worst day in the world's history, because they are.

- **Alice's Apology** (Gunner gun, T5) — 6 shots, 30 dmg. Innate: **Prejudice** (the point-blank radial toggle). The Extreme Prejudice gun exists and this is it. Innate Prejudice on a T5 body is the exact "born with the toggle" lottery ticket from 03. Lore: Alice's notes say the gates were "99.7% safe". This gun is what the 0.3% felt like, point blank.
- **The Last Argument** (Warrior sword, T5) — melee arc, 180 dmg, no projectiles, swings slow. Innate: killing blows reset your tattoo cooldowns. Lore: the last thing Bob sent Charlie before the merge was a letter that just said "I told you." This sword has the same energy.
- **The Unsent Warning** (Wizard staff, T5) — 2 shots, 90 dmg, piercing. Innate: your spells' windups/warnings are 30% shorter. Lore: the warning about the gates existed. It just never got sent. Faster warnings, ha. I'm very funny.
- **The Getaway** (Trickster crossbow, T5) — 3 shots, 40 dmg. Innate: kills grant 1s of move speed stacking. Lore: what the gate technicians used to leave, presumably.
- **Threshold** (Tank shield... no, Warden blade, T5) — melee arc, 130 dmg. Innate: allies within a small radius get +5 defence. A sword that makes a perimeter. Lore: named for the gate thresholds everyone walked through like idiots.
- **Pocketworld Latchkey** (Trickster ability, T5, listed above) — keeping it in both places because it straddles the line. Deal with it.

Drop rates: low. Borderlands-legendary low, not RotMG-white-bag-from-2012 low. I want people to actually see one in their lifetime. Maybe. I reserve the right to be cruel.

# Example endgame builds (slot order matters)

Reminder from 03: earlier slots are stronger, so ordering is a real decision. All 6 slots, first to last.

**Gunner, boss-melter (the "wait for the stagger" build):**
`[Overcharge Cylinder 2] [Echo Target 1] [Bullseye Sigil 1] [Counteredge 1] [Gatehook 1]`
Overcharge in the strongest slot because the whole build exists to land one charged shot during the tank's armour-break window. Echo Target banks damage through invuln phases. Counteredge last because it's the panic tool, not the plan.

**Warrior, bleed-washing lunatic:**
`[Ledger 1] [Second Skin 1] [Sympathetic Wound 1] [Blink Rune 1] [Boiling Ink 1] [Mute Titan 1]`
Six 1-slot tattoos, the full menu. Ledger first because it's the win condition. This build either tops the damage chart or dies in 20 seconds and there is no middle ground, which is exactly how warriors should feel.

**Wizard, scripture enjoyer:**
`[Meteor Concordance 3] [Skywrit 2] [Convergence... no wait that's 5] [Pyre Incantation 2 — total 7, too many]`

Okay so: `[Meteor Concordance 3] [Skywrit 2] [flex 1]` — the flex slot is a 1-slot wand trinket I haven't designed yet, which is itself a good argument that I need more 1-slot wizard items. Noted.

**Witch Doctor, raid babysitter:**
`[Sporespire Tome 3] [Festival Ring 2] [Purge Draught 1]`
Spire first: healing throughput scales with slot strength, and keeping idiots alive is the job. Festival Ring second because dead DPS do zero damage but buffed DPS end fights faster which is also healing, if you think about it.

**Tank, professional wall:**
`[Aegis of the Fallen Gate 6]`
That's it. That's the build. One item, six slots, infinite aura. The alternate "debuff bot" build is `[Exposing Bash 2] [Stagger Plate 2] [Backswing Battery 1] [Granite Oath 1]` for bosses where you're only there to make the number go down.

**Trickster, get-out-of-jail vendor:**
`[Pocketworld Latchkey 3] [Unkindness Cloak 2] [Mirror of Regret 2 — over budget]`

`[Pocketworld Latchkey 3] [Unkindness Cloak 2] [Blackglass Vial 1]` — the skip-phase button, the escape button, and a damage amp for when neither button is needed. Tricksters don't have a plan, they have options.

# Stuff this file made me realize

- I need more 1-slot ability items for wizard and trickster, or the composition math (32 compositions of 6, see 03) never actually gets explored for those classes.
- The cursed enchantment pool needs playtesting before I add more. Desperate + Ledger on the same warrior is a combo I can already smell.
- Artisan class still has zero items because artisan is combat-useless by design (see 03), but they'll need their own catalogue eventually. Not today.
