yeah nah i dont really give a shit about any of this shit tbh i'm just a guy.
a lot of this was written by ai but i corrected it because i wanted to game myself because i know i'm the type of person who falls to that one talking point about how if you want to ask a question and have it answered quickly and correctly then what youre supposed to do is you ask the question and then make an alt account and answer it wrongly so that some autistic nerd (me) does the "erm ackshually" thing and corrects the incorrect answer.

# Main selling points
## Skill based bullet hell mmo
- Similar games:
	- [Binding of Isaac](https://www.youtube.com/watch?v=4JyJc09_XBM)
	- [Enter the Gungeon](https://www.youtube.com/watch?v=7u5epAs6q-g)
	- Vampire Survivors
	- [Realm of the Mad God dungeon](https://www.youtube.com/watch?v=ZRKva2qU9kE) [Realm of the Mad God boss](https://youtu.be/QiUTa-oDBxo?si=Lw6ZIqLUAJj7Fc1X&t=187)
	- [Pixel Quest](https://www.youtube.com/watch?v=S8cvW9NytG0)
- Skill based
	- A player who is skilled enough should technically be able to solo kill some end game bosses with only beginner items. It would be impractical though.
	- Challenge runs and speed runs are going to be part of the game.
	- Semi-perma-death. Players lose stat progression, but they drop their items on death. If players have multiple characters, then they will be able to pick up their items with their other character.
		- The intention behind this is that early game players will be able to pick up their items after dying my end game players will lose their items since end game content is far away from spawn.
	- (conflict: 01 Executive Summary calls it perma-death and lists "perma-death" as one of the three main sources of difficulty; 02 Gameplay and this doc clarify it's only *kind of* perma-death — you lose skills/stats but can still pick up items off your body.)
- Unique gameplay:
	- There are very little games out there that are similar to Realm of the Mad God. There are also no MMORPGs out there that take inspiration from Factorio: Space Age and Clash of Clans. There also aren't really that many unforgiving skill-based perma-death MMORPGs out there.
	- The permadeath isn't as brutal as RotMG's — you lose your skills and stats on death, but you can still recover your items if you reach your body before the timer/dungeon closeout, so newer players aren't punished as hard. See [[02 Gameplay]].
	- The thing that sets this game apart from other bullet-hell MMORPGs is the emphasis on camera rotation.
- Group coordination:
	- Beyond the player territories and resource management, the actual bosses and bullet patterns and phases are going to force players to coordinate. The intention is that the game is going to be both easier and harder than Realm of the Mad God.
- Skills:
	- Similar to most MMORPGs, there is going to be skills for logging, fishing, mining, and crafting.
- Coherent story:
	- Realm of the Mad God doesn't really have a coherent story. I also feel like the post-catastrophe survivor-like story is pretty refreshing (see [[#Lore]]).

## Rotating Biomes
- Biome inspirations:
	- [[docs/Game Design Docs/Art/Pasted image 20260530125651.png|Hollow Knight and Hollow Knight: Silksong]] (i like that they are distinct and that there are glowing things and that the biomes feel lived in)
	- Factorio: Space age (i like [gleba](https://www.youtube.com/watch?v=K3QVrbpfgYc))
- Chunks that haven't been explored for a while have a chance to transform into a different biome
	- This could lead to emergent gameplay where players intentionally wall off chunks to see what resources are in each of its biome variants. ^soyjak-1
- Semi-persistent open world. The non-persistent part is the rotating chunks.
	- When a player places a building on a certain biome chunk and they let that chunk rotate into a new biome, then the building they placed doesn't get forgotten or wiped. If they want to recover the building, they just have to wait until the chunk rotates back to the original biome.

## Base building, automation, RTS PvP
- Similar games:
	- Mindustry
	- Clash of Clans
	- Factorio
	- BitCraft Online
- Each biome will have its own resource that players will be able to gather.
	- It'll be similar to how Factorio has different planets for different resources to make different sciences.
	- I don't like the idea of mining patches of world-generated ore. Maybe make it so that the resource is tied to the biome's enemies?
- Emergent gameplay (insert 2 soyjaks pointing at buzzword)
	- Routers, junctions, and bridges.
	- Base biome optimization
		- [[docs/Game Design Docs/One Page Doc#^soyjak-1|Rotating biomes]]
- Base building:
	- Players are going to be able to claim territories and there is going to be RTS-like territory-PvP, similar to any of your classic RTS games (if you've never played an RTS game, then it's similar to Clash of Clans).
- Resource management:
	- Different biomes are going to have different resources, and you're going to need all of them to progress. Similar to Factorio: Space Age. The idea is that player territories are basically going to be Factorio factories.

# Game overview

## High Concept
Wizards tried to implement world-wide long range teleportation gates but they fucked it up and caused 75% of the planet to be patchwork-merged with other worlds which have monsters and stuff. Kinda post apocalyptic.

## Demographic
### Difficulty enjoyers
Perma-death bullet hell rogue like mmo. Main sources of difficulty are:
- Dodging bullets
- Group Coordination
- Perma-death
	- (conflict: calls the death "perma-death" here, but see Semi-perma-death in [[#Main selling points]] — it's only kind of perma-death.)

### Stylized visual enjoyers
Low poly 3d passed through a pixel art shader.

### Realistic story enjoyers
I'm not a huge fan of stories that are coded. I don't like fantasy-coded stories, science-coded stories, human-condition-coded stories, or politically-coded stories.

##### Premise
Humans discovered magic early. No scarcity > no conflict > no bureaucracy. This causes the patchwork-merge catastrophe.

##### Story Inspirations
###### Vibe
- Project Hail Mary (movie)
- The Martian (movie)
###### Plot

## World building vibes
Players spawn into a post-catastrophe world that has recently been patch-work merged with out worlds because a bunch of wizards naively tried to implement world-wide long range teleportation bates and messed it up. The game is similar to post-apocalypse except less depressing/bleak - the survivors aren't being threatened by anything but they still have to figure out how to survive, so at a societal/narrative level it'll have survival game vibes (different colonies have to establish contact, trade, etc) but at an individual level it'll just be a standard mmorpg.

## Look
This game will use realistically-proportional low-poly 3D models that're passed through a pixel art shader (heavily inspired by [[<https://www.youtube.com/watch?v=1FrIBkuq0ZI&t=417s>|t3ssel8r's dev log videos]]), however, there will be support for a 2D version of the game that uses 2D sprites.
- The game will predominately be viewed from a top-down angle, however, players will be able to rotate, pan, and zoom the camera along any angle, similar to other mmorpgs that use 3d models.
- Different graphics: Realm of the Mad God is a 2D game. The intention is for this game to be visually 3D. The underlying logic is still 2D though, and there is going to be support for a 2D version of the game that looks similar to Realm of the Mad God if the community wants one. I feel like the low poly 3D passed through a pixel-art shader is simply just a really cool look.

**Game Inspirations:** Realm of the Mad God (core gameplay), Alabaster Dawn (core gameplay), Runeward Online (core gameplay), Pixel Quest (core gameplay), BitCraft Online (non-core gameplay), Clash of Clans (non-core gameplay), Factorio: Space Age (non-core gameplay), Hollow Knight (art style), Hollow Knight: Silksong (art style), t3ssel8r videos (art style), Borderlands (item system), and Terraria (idk I just think it's cool).
**Platform:** PC

# Gameplay

## Core Gameplay Loop
The core gameplay loop is going to be extremely similar to Realm of the Mad God:
- Level up, upgrade gear, dodge enemy bullets, land your own bullets, use your abilities, etc.
- There will be nuances like knowing what to do during boss phases but we don't need to get into that. Watch the RotMG videos at the end if you want examples.
- The core gameplay is heavily inspired by Realm of the Mad God, however, there will be elements of the game that are inspired by BitCraft Online, Clash of Clans, and Factorio (currently deferred).

There is kind of going to be perma-death in this game, so that's also part of the gameplay loop. I say kind of because, when a character dies, they lose all their skills and stats but they're still able to pick up their items if they reach their body in time. It'd be true perma death if they couldn't pick up their items.
- If a player dies in a spot that's far away from any waypoints or spawn points, or if a player dies in a dungeon (dungeons will close after a minute or so), then they won't be able to retrieve their items.
- The purpose of this mechanic is so that the game isn't as punishing for newer players.

It'd be possible to add the standard MMORPG quests (fetch quests, defeat n monsters quests, etc) but that shit's boring.

## Damage calculation nuances
Damage calculations nuances come from the interaction between defence and health.
- I kind of don't want there to be any elemental damage.
- Defence reduces bullet damage by a flat amount (minimum damage=1).
- Health increases the amount of raw damage you can take.
- What that translates to is: high defence makes it so that a large number of low-damage bullets are less effective.
	- E.g. Against a target that has 5 defence, 10 bullets that deal 10 raw damage only deals max(10-5,1)\*10=50, whereas 1 bullet that deals 100 raw damage deals max(100-5,1)\*1=95 damage.

## Buffs
There are 2 types of buffs: damage buffs and utility buffs.
### Damage buffs
Damage buffs are essentially just different ways of increasing/decreasing your shot count to play around with the damage calculation nuances.
- flat decrease to individual bullet damage in exchange for double bullets (favors weapons that have a low shot count). E.g. (10-5-3)\*(10+1)=22 vs (100-5-3)\*(1+1)=184.
- extra % true damage (favors weapons that have a low shot count). E.g. (10-5 + 10\*0.5)\*10=100 vs (100-5 + 100\*0.5)\*1=145.
- extra flat true damage (favors weapons that have a high shot count). E.g. (10-5+7)\*10=120 vs (100-5+7)\*1=102.
- % increase fire rate. this is kinda boring.
- % increase damage. this is kinda boring.
### Utility buffs
Utility buffs just make it easier to play.
- increased range
- faster bullet speed
- faster move speed

## Status conditions
These are called status conditions because some of them aren't necessarily bad.
- halves individual bullet damage
- halves fire rate
- slower bullet speed
- slower move speed
- cursed: stores damage taken and doubles it if , otherwise sheds it.
- can't use abilities
- can't move
- reduced defence
- defence reduced to 0
- health bleed
- can't heal
- can't instant nexus
- delayed nexus is longer
- can't nexus

## End game content
End game content is not just going to be harder bosses and harder dungeons.
- End game content is going to essentially just be building a factory for your guild's territory. Pretty much just Factorio: Space Age + Clash of Clans (and any of the standard RTS games), except you play as the individual units (builders, miners, inserters, etc). Like Factorio: Space Age, different biomes will have different resources.
- Each biome will have area bosses that can be aggroed and dragged across the world. One tactic for collecting resources from other biomes will be by aggroing and dragging area bosses into your territory.
- There will be territory PvP but there won't be direct PvP.

## Beginner content
New players are going to follow a story line so they aren't going to be directionless like in Realm of the Mad God.

# Classes, items, and equipment

## Class system overview
Classes aren't going to be explicitly defined. They are going to be defined by the gear that exists in the game and the stat requirements for that gear. When players figure out the optimal builds and stat allocations, they will naturally sort themselves in the stereotypical MMORPG classes.

**The reason why it is defined this way is because I want to be able to define new classes simply by adding new gear to the game**. As in, if I want to define a new class (e.g. a monk), then I'll simply just define the exact stat distribution I want for that class (e.g. monk = equal parts wisdom + strength + dexterity) and I add new gear that requires that stat distribution.

The reason for this is that I wouldn't have to update the spacetimedb database schema whenever I wanted to add a class, so players wouldn't have to update their clients whenever an update rolls around.

## Stats
Right now, the stats are going to be:
- Dexterity
- Strength
- Wisdom

I can't think of any other stats to add that would make meaningful sense. I don't like having a luck stat.

## Temperament
There are also going to be other stats called Temperament stats. Temperament stats work exactly like normal stats — the only difference is conceptual, not mechanical. They flavour what a character does with their normal stats:
- Damage dealer
- Supporter
- Artisan

Different stat + temperament distributions produce different classes. E.g. a tank is a strength supporter, a warrior is a strength damage dealer, a paladin is a strength+wisdom supporter, a healer is a wisdom supporter, a wizard is a wisdom damage dealer.

Artisan is useless for combat, but a character fully specced into it should be valuable in the endgame crafting/economy loop. I want leveling an artisan character to be intentionally gruelling — I want other players to go "what the fuck?" when they see someone who actually did it.

## Item and equipment system overview
- Items are designed around RotMG's swap-out meta, but without requiring the player to actually swap out.
	- Weapons can have toggles that change shot pattern.
	- Characters have n=6 ability slots and can equip multiple abilities. Abilities are tied to items, and some ability items take up multiple slots (shields take 6, spells take 2, tomes/scriptures take 3).
		- If order doesn't matter, then the total number of combinations/permutations that fill up n slots can be calculated with the integer partition function.
			```
			def partitions(n, max_size=None):
				"""Yield partitions of n as non-increasing tuples, each part <= max_size."""
				if max_size is None:
					max_size = n
				if n == 0:
					yield ()
					return
				for k in range(min(max_size, n), 0, -1):
					for rest in partitions(n - k, k):
						yield (k,) + rest
			usage: len(list(partitions(6)))=11
			```
		- If order matters, then the total number of combinations/permutations that fill up n slots can be calculated with the integer composition function
			```
			def compositions(n, max_size=None):
			    """Yield ordered slot arrangements of n."""
			    if n == 0:
			        yield ()
			        return
			    for k in range(1, min(max_size or n, n) + 1):
			        for rest in compositions(n - k, max_size):
			            yield (k,) + rest
			usage: len(list(compositions(6)))=32
			```
		- I think I want the order to matter, since the integer composition function grows quickly ($2^{n-1}$). You could make the order matter by making the first ability stronger than the second ability and so on.
- Items have enchantments. Inspired by the Borderlands item system, though this game won't have any item instancing.
- Non-weapon items can have enchantments that modify or add to the player's shot pattern or ability activation. E.g. a ring could double the player's number of bullets, or an armor could make it so that whenever you use an ability it buffs you to increase your fire rate or adds a shot to your shot pattern.

In RotMG, one of the main ways people maximize DPS is swapping gear sets mid-fight — usually because when an enemy is staggered or armor-broken, a set with a higher bullet count suddenly out-DPSes a set with fewer, harder-hitting bullets (higher defense disproportionately punishes low-bullet-count builds). That swap-out meta was clunky, and I don't want it to be viable in my game.
- Instead: weapons get toggles, tied to enchantments, that flip the shot pattern between low-count/high-damage and high-count/low-damage. Same tactical decision, no inventory shuffling.
- RotMG's Staff of Extreme Prejudice is the reference case — it fires 10 bullets in a circle and does the highest DPS in the game, but only if you're standing directly on top of the enemy, so it's only good against staggered targets. I want that shot pattern to exist here as a modifier/enchantment: useless early, game-breaking late. Off-meta builds with a huge late-game payoff is a classic RPG fantasy and I want it in this game.
- All the buffs and status conditions in [[02 Gameplay#Buffs, status conditions, and damage calculation nuances]] should be able to show up as enchantments/modifiers. If the enchantments are good enough, a lower-tier weapon should sometimes beat a higher-tier one.
- Enchantment slots should be player-modifiable, plus some innate enchantments that can't be changed but also don't eat up a slot. A weapon born with the Extreme-Prejudice-style toggle as an *innate* enchantment should be extremely valuable to minmaxers.
- Abilities are tied to equipment (n=6 ability slots, some abilities eating multiple). Some ability items passively modify your kit just by being equipped (paladin buffs), others require you to actively swap to them to use them (wizard spells).

## Classes & Class items
The stat distribution syntax for each of the classes is kinda self explanatory.
Class items are going to be locked behind stat thresholds.

Bullet master has access to all 3 bullet interaction abilities: delete, split, attract. Warrior + tank + paladin have delete, wizard + witch doctor have attract, trickster has split.

Trickster has access to all 3 dodge abilities: telegraphed short range teleport, ground grapple, instant medium range teleport. Trickster also has access to a bunch of other abilities: invisibility, poisons, decoys.

### DPS
#### Bullet Monk / Bullet master / archer / gunslinger (dex > 0) & (dps > 0)
I kinda dont like the class name but it makes the most sense.
(conflict: called "Bullet Monk" here/03, "Bullet master" in the Items catalogue, and "archer/gunslinger/bullet master" in Inspirations — same class, different working names.)

Make it so that the bullet master can store and delay their dps to blast counter phases or wait out invulnerability phases. In the long run, they should do less dps than other classes but when you take into account invulnerability and whatnot then these guys should out-damage other classes because of their ability to store and delay the damage they deal.

##### Bullet Monk Weapons
- idk they just use their hands to summon bullets
	- Summon floating orbs that shoot continuous bullets. The actual physical animation for how they shoot in a particular direction with this is that they point with their fingers.
	- Conjure orbs that are periodically thrown at enemies. The actual physical animation for how they shoot in a particular direction with this is that they throw shit.
- Bullet Monk weapons can innately toggle between charge/shotgun/piercing/radial/etc. shot patterns
	- Floating orbs that shoot continuous bullets have an animation that shoot at the ground and split into radial bullets. Other patterns are just different hand signs i guess (open palm, pointed finger, etc).
	- Conjured orbs that are periodically thrown at enemies are simply just thrown differently. Back hand scattering/throw = shotgun. Front hand throwing = charge. Overhand pegging = pierce. Throwing at ground = radial.
##### Bullet Monk Abilities
- Big charge shot: self-slows, disables weapon, fires a single shot that gets stronger the longer it's charged (hold-to-charge)
- Big volley shot: self-slows, disables weapon, fires a large spread that gets stronger the longer it's charged (hold-to-charge) — especially strong against low-defense enemies
- Big radial shot: self-slows, disables weapon, fires a fuck ton of bullets in a radius around the player position that gets stronger the longer it's charged — strong against staggered enemies
- Spawn a dummy on self that allies can shoot; the archer absorbs the damage that the dummy takes and releases it as a big charge/volley shot (also pitched as a dummy/blackhole thing)
- Mark an enemy to take increased damage
- Quick slash that destroys enemy bullets, if the slash is stronger than the bullet
- Grapple hook toward a point on the ground (still takes damage while grappling)

#### Warrior (str > 0) & (dps > 0)
##### Warrior Weapons:
- Sword
	- Play pixel quest on roblox or look up the sword strike animation for it.
	- You could probably look to terraria for different sword ideas.
		- Swords that send slashes
##### Warrior Abilities
Magic tattoos. Similar to Witch Hat Atelier. In RotMG, the warrior's ability item is just a helmet which sucks.
- Higher grade tattoos are made from higher grade inks: monster blood, god ichor, special tree sap, etc.
- It'd be cool if you could make the tattoos glow when you activate them.
- To make warriors special you could probably make these items a consumable or something. Like, to equip the ability you use a consumable and it overwrites one of your existing tattoos.
- tattoo that converts all incoming damage for the next n seconds into bleed damage
	- you could probably add some conditions to make it so that they can shed the bleed damage.
- makes their sword do more damage for the next n seconds
	- you can be creative with it by making their sword damage burn the enemy or modifies their sword to store all the damage they deal within the next n seconds and doubles it or smth but the general gist is more damage
	- for the "sword stores all the damage they deal within the next n seconds" thing, you could probably add a fail condition that makes it so they deal 0 damage
- enter a windup animation to do a short teleport. Similar to the Kensei's ability from RotMG. Intention is for players to be able to dodge upcoming bullets for certain boss phases. Cracked players should be able to solo any boss with this ability.
- a tattoo that converts all damage dealt *and* received into bleed (as opposed to just incoming damage)
- a tattoo that stores all damage dealt/received over n seconds and negates/multiplies it if some condition is met - same "high risk high reward" shape as the sword-storing-damage idea above
	- you could balance this by making bosses aggro when too much damage is dealt.
		- you could have a boss who's thematically blind and becomes impossible to dodge if you do too much damage.
- self buff life steal in exchange for self bleed. (from Abilities.md)

**Bleed marks (refined in Abilities.md / implemented in Items.md):** mark an enemy and buff self to convert all incoming damage *from that enemy* over the next n seconds into DoT damage. Incoming hits add to a damage pool instead of HP loss; the pool drains as damage ticks over ~5s. Only 70% of the original damage is applied to the pool (bleed conversion is partly mitigated). One ability per purge condition:
- 1. Avoid taking any damage for ~2s. 2. Stay within a radius of the marked enemy for ~5s. 3. Dealing x damage (for now just use 1 damage) to the marked enemy. 4. The marked enemy dies.

**Store-damage marks (refined):** mark an enemy, then store all the damage dealt by self within the next n seconds and double it if a condition is met or negate it to 0 if a different condition is met. One ability per condition:
- 1. No-hit wager (double if the player takes no hits during the window; released at normal if hit 1-2 times; lost entirely if hit 5+ times).
- 2. Stay within a radius of the marked enemy (doubled if staying within radius r1; normal if staying within r2; negated if you leave r2).
- 3. Dealing x damage (doubled if reaching x damage; normal if damage is between y and z; negated if below z).

#### Wizard (wis > 0) & (dps > 0)
##### Wizard Weapons
- staffs/staves (similar to RotMG)
	- S.T.A.F.F. ripoff (don't copy the bullet pattern exactly 1 for 1)
	- [[<https://youtu.be/m0COVnAtCxA&t=107>|S.T.A.F.F. Showcase - This Weapon SHREDS Everything | RotMG]]
	- Tiered staffs (don't copy the bullet pattern exactly 1 for 1)
	- [[<https://youtu.be/MKtTq4U3hMU?si=ReRPrelHkF1btWOA&t=468>|(Rotmg|[Rotmg) Godly Wizard PPE]] Godly Wizard PPE]]
- Wands (similar to RotMG)
	- Lumiaire
	- Tiered wands
##### Wizard Ability
- Spell that spawns a delayed beam of light that deals damage
	- [[<https://www.youtube.com/watch?v=dU0andwLZmk>|Chaotic Scripture only Cult (Rotmg|[Rotmg)]]]]
- Spell that summons bullets that converge on or from or around the player's cursor
	- [[<https://www.youtube.com/watch?v=-aCETL8lteA>|Rotmg Penetrating Blast Spell, shot pattern]]
	- [[<https://www.youtube.com/watch?v=4W4metbXxsc>|WHY TABLET IS BETTER THEN PARA SPELL ( in o3|[ in o3)]]]]
- Spell that forces the player to stop shooting to enter a wind up animation that sends a fireball or some shit
- Spell that summons a spire that damages surrounding enemies (mirror image of the healer's mushroom tome spire idea below, but offensive instead of healing)
- Spell that calls down a meteor that breaks into shrapnel after it lands
- Spell that shoots out a laser

### SUP
#### Witch Doctor (wis > 0) & (sup > 0)
##### Witch Doctor Weapons
- Staves and wands.
##### Witch Doctor Ability
Back when RotMG had more players there used to be some tech around using mushroom tomes for group coordination because healers could direct where the melee players would have to stand, which was useful if they knew all the boss phases and shit. i remember hearing raid leaders saying "guys, push up. stop staying in the back of the group, you're safer if you push up because that's where all the mushroom tomes are. you'll heal more if you push up."
- AoE heal
- Targeted heal (heals player closest to cursor or heals the lowest health player that's within a radius of the cursor)
- Debuff removal
- a circle that players can't die within (soft "can't drop below 1hp" zone)
- a circle that heals allies over time
- a circle that burst heals allies after n seconds
- a circle that makes allies deal more damage
- a circle that damages enemies over time
- a circle that deals a burst of damage to enemies after n seconds
- a circle that slows enemies
- a circle that makes enemies receive more damage
- a circle that distributes all damage received within it evenly across all players standing in it (group-coordination tech, similar to the mushroom-tome-push-up thing above but for damage instead of healing)
- a circle that makes players immune to debuffs
- a circle that reduces all damage that players take

#### Paladin
Similar to the paladin in RotMG.
Paladins will have less defence than tanks but will have a higher health pool. This will make them weak against enemies that fire a large amount of low-damaging bullets, or enemies that spawn in groups or spawn a bunch of mobs.
I don't really want there to be any religions in the game so maybe don't call them paladins.
(conflict: Temperament above describes a paladin as a *strength+wisdom* supporter; the implemented catalogue lists Paladin as WIS 15 / SUP 12 mirroring Witch Doctor.)
##### Paladin Weapons
- Play pixel quest on roblox or look up the sword strike animation for it.
- You could probably look to terraria for different sword ideas.
##### Paladin Abilities
The actual item that RotMG uses are seals but that's kinda boring. You could probably reuse the tattoo idea?
- Temporarily buff nearby allies to have increased health regen
- Buff all nearby allies to deal more damage
- Converts all damage into bleed damage
- Immunity to all damage for the next n seconds
- Immunity to the next single instance of damage received within n seconds (weaker/cheaper version of the full invuln buff above)
- Buff all allies to deal more damage
	- Buff all allies to store all damage dealt/received over n seconds and negate/multiply it if a condition is met - same shape as the warrior tattoo idea
- Buff all allies to store all damage dealt/received and negates it if a condition is met
- Buff all allies to convert damage dealt/received into bleed damage
- Buff all allies to heal over time
- Redirect all damage that party members receive over the next n seconds to self
- Store all pre-mitigated damage received over the next n seconds and release it as a smite
- (AoE versions of the warrior's bleed/store marks: mark an enemy and buff players in a radius around self to convert incoming damage from that enemy into DoT / to store their damage and double-or-negate it per the wagers above; targets are picked by radius around the caster — there is no party system.)

#### Tank (str > 0) & (sup > 0)
Similar to the knight in RotMG.
Knights will have more defence than paladins but will have a lower health pool. The lower health pool effectively shouldn't matter if the healer is good.
There should be some end game bosses where knights will only be useful for their stagger and armour break debuffs.
##### Tank Weapons
- Play pixel quest on roblox or look up the sword strike animation for it.
- You could probably look to terraria for different sword ideas.
##### Tank Ability
- Shield bash = stagger bar (similar to how knights can stun in rotmg but less so)
- Shield bash applies armour break debuff or expose debuff (similar to ogmur/samurai in RotMG. the expose debuff just adds like + 10% damage to all bullets that hit the enemy. I think this +10% damage ignores the enemy's defence, so weapons that shoot a lot of bullets pair well with this debuff. armor break reduces enemy defence to 0)
- Temporarily invulnerable to all damage
	- Self buff
- Immunity to all damage for the next n seconds
- Reduce all incoming damage by a percentage for the next n seconds
- Store pre-mitigated damage received for the next n seconds and release it as a shield bash
- Shield bash that debuffs enemies to receive more damage and contributes to a stagger bar
- Shield wall that absorbs all bullets that hit it
- (stagger refinement: create two ability items — bar + stun, and bar + stun + break. Per-enemy stagger bar; bash adds stagger, full bar stuns for a few seconds then resets. Stun behaviour is dynamic: some enemies pause attacks + movement, some get slowed, others get enraged.)

#### Trickster (wis > dex > 0) & (sup > 0)
This class combines the assassin, rogue, and trickster from RotMG into one class.
##### Trickster Weapons
- Guns?
##### Trickster Ability
Maybe make this class's magic be tied to physical items rather than magic scrolls like the wizard.
- Invisibility (cloak)
- Poisons/debuffs (reuse shared debuffs)
- Teleport
- Decoy — summons a mirror image that takes aggro / that enemies can target
- Teleports all party members to a pocket dimension that makes them briefly invisible + immune to all damage
- buff nearby allies to be immune to all damage for the next n seconds

## Accessory items
Accessory items
- (stub: 03 ends here and the `Items/Accessories/Accessories.md` file is an empty per-class template — no accessory designs written yet. `Items/Weapons/Weapons.md` and `Items/Armors/Armors.md` are likewise empty per-class templates.)

## Implementation decisions (previous session prompts and outputs)
- Phasing: phase by subsystem — the doc lists ~45 abilities across 6 classes, requiring ~5 new subsystems (status effects, enemy debuffs, ground zones, spell damage, targeting changes). Duplicated buffs/debuffs should share the same function in code (e.g. the warrior's self bleed-conversion and the paladin's AoE version).
- 'Convert damage into bleed damage' (warrior self-buff, paladin AoE buff — same shared function): Dot pool, sheddable, reduced (see Bleed marks above).
- Store-damage doubling/negation: mark an enemy first; applies only to the marked enemy; one ability per condition (no-hit / radius / damage wagers).
- AoE ally effects (paladin 'buff nearby allies', paladin 'redirect party damage', trickster 'buff nearby allies') pick targets by radius around the caster (recommended) — there is no party/squad system in the game.
- Input model: hold-to-charge (recommended) for bullet master's charge/volley shots, wizard's windup fireball, and warrior's windup teleport.
- Defer archer's shootable dummy, trickster's decoy, and trickster's invisibility — they require enemies to target non-player entities / stop targeting players.
- Tank shield bash: bar + stun, and bar + stun + break (two ability items).
- Trickster 'poisons' and 'debuffs': reuse shared debuffs (recommended).

## Implemented items catalogue
Catalogue of the implemented ability items (and class weapons), mirroring the seeds in `server/spacetimedb/src/item/seeds.rs`. The ability design pitches these realize live in `Items/Abilities/Abilities.md`.

**How to read the gates:** classes emerge from gear locked behind stat thresholds (above). Base stats are 10 for everyone, so gates are set just above base — primary 15, secondary 12 — as placeholder values pending tuning. Stat shorthand: STR = strength, WIS = wisdom, DEX = dexterity, DPS = damage-dealer, SUP = supporter.

**Shared rules:** abilities are not consumed on use — they go on cooldown (all items below have unlimited charges). The first ability slot is stronger: effect magnitudes scale by slot position (×1.5 / 1.25 / 1.1 / 1.0 / 0.9 / 0.8). Ability targets clamp to 600 range. One active mark (bleed or store) per player — recasting a mark replaces it (bleed pools dump instantly, store pools forfeit). Status effects purge on death, leaving the world, and disconnect. Hold-to-charge items self-slow (50%) and suppress firing while held; max charge 3s, then auto-release; charge scales the effect ×(0.25 + 0.75 × fraction).

### DPS — Bullet master (DEX 15, DPS 12)
Weapons (toggle shot patterns with T):
- Marksman Bow — piercing longshot (45 dmg, pierces) / point-blank spread (3×12) / quick light shot (20 dmg, fast cadence)
- Warbow — heavy single shot (60 dmg) / five-arrow fan (innate `scatter_fan` toggle, can't be removed)

Abilities:
- Charged Shot (12s) — hold-to-charge, release fires a 600×20 piercing shot for up to 70 damage; scales with DEX
- Volley (10s) — 350-long cone (0.6 rad spread): 6 pellets of 10 damage each — flat armour eats every pellet, so it's strong vs low-defence enemies; scales with DEX
- Quick Slash (4s) — erases enemy bullets dealing ≤15 damage in a 140×60 rectangle in front of you
- Grapple Hook (10s) — pull yourself toward the cursor, up to 450 (you still take damage mid-pull)

### DPS — Warrior (STR 15, DPS 12)
Bleed marks — mark an enemy (cursor-pick, 100px pick radius); 70% of each hit *from that enemy* becomes a pool that drains over ~5s, 30% lands instantly. One item per purge condition:
- Sanguine Brand (10s, cd 15s) — sheds by taking no damage for 2s
- Blood Oath (15s, cd 15s) — sheds by staying within 200 of the mark for 5s
- Crimson Tithe (15s, cd 15s) — sheds by damaging the mark
- Deathmark (20s, cd 15s) — sheds when the mark dies
- Bloodbath (20s, cd 20s) — sheds when the mark dies; also converts *your* hits on the mark into a bleed on it (a third of the hit per second for 3s) instead of instant damage

Store-damage marks — your damage to the mark is withheld for 8s, then released at 2×/1×/0× per the wager (marked-enemy death forfeits the pool):
- Gambler's Mark (cd 20s) — 0 hits taken = 2×, 1–4 hits = 1×, 5+ = 0× (witness-corroborated)
- Duelist's Wager (cd 20s) — never stray 150 from the mark = 2×, stay within 300 = 1×, ever leave 300 = 0×
- Blood Price (cd 20s) — store ≥500 = 2×, ≥200 = 1×, below = 0×

Movement:
- Warpath (cd 12s) — hold-to-charge, release blinks toward the cursor, up to 400 at full charge

### DPS — Wizard (WIS 15, DPS 12)
All spells scale with WIS (`damage × (1 + wis × 0.002)`):
- Arc Beam (8s) — 500×40 beam, 30 damage
- Converge (12s) — 40 damage within 120 of the cursor (converging visual)
- Fireball (10s) — hold-to-charge, release hurls a fireball: up to 50 damage within 100 of the cursor
- Arcane Spire (15s) — spire at the cursor for 8s: enemies within 120 burn for 8 damage/s
- Meteor (20s) — 80 damage within 200 of the cursor (shrapnel visual)
- Arc Laser (15s) — 800×15 laser, 45 damage

### SUP — Witch Doctor (WIS 15, SUP 12)
Circles drawn at the cursor (radius 150). Ally effects apply to whoever stands inside and shed ~1.5s after leaving; enemy effects debuff enemies inside; bursts fire once when the circle expires. Circles die with their owner (death, logout, disconnect).
- Warding Circle (10s, cd 20s) — allies inside are immune to enemy debuffs
- Aegis Circle (10s, cd 20s) — allies inside take 30% reduced damage
- Sanctuary Circle (10s, cd 25s) — allies inside cannot drop below 1 HP
- Mending Circle (10s, cd 20s) — allies inside regenerate 3 HP/s
- Surging Circle (3s, cd 15s) — detonates at expiry: heals allies inside for 25 HP
- Empowering Circle (10s, cd 20s) — allies inside deal 20% more damage
- Withering Circle (10s, cd 15s) — enemies inside bleed for 6 damage/s
- Ruin Circle (3s, cd 15s) — detonates at expiry: 40 damage to enemies inside
- Hindering Circle (10s, cd 15s) — enemies inside are slowed 40%
- Exposing Circle (10s, cd 20s) — enemies inside take 50% more damage
- Communion Circle (10s, cd 25s) — hits on anyone inside are split evenly across everyone inside

### SUP — Paladin (WIS 15, SUP 12 — open question: the design pitch doc has no Paladin stat line; currently mirrors Witch Doctor)
The warrior's bleed and store marks as AoE auras (allies within 150, same mechanics):
- Consecrated Brand (10s, cd 20s) — AoE bleed mark; sheds after 2s without taking a hit
- Martyr's Oath (15s, cd 20s) — AoE bleed mark; sheds after staying near the mark 5s
- Shared Tithe (15s, cd 20s) — AoE bleed mark; sheds once you damage the mark
- Last Rites (20s, cd 20s) — AoE bleed mark; sheds when the mark dies
- Martyr's Gamble (8s, cd 25s) — AoE store mark; no-hit wager (2× at 0 hits, 0× at 5+)
- Templar's Wager (8s, cd 25s) — AoE store mark; radius wager (150 / 300)
- Congregation's Price (8s, cd 25s) — AoE store mark; damage wager (500 / 200)

Buffs:
- Aegis of Faith (3s, cd 20s) — self: invulnerable
- Sacred Ward (10s, cd 15s) — self: absorbs your next single hit
- Guardian's Pact (8s, cd 25s) — hits on allies within 150 are redirected to you
- Prayer of Mending (10s, cd 20s) — you and allies within 150 regenerate 2 HP/s
- Smite (8s, cd 25s) — store the damage you take (pre-mitigation), then unleash it within 150 around you; recast to release early at the cursor

### SUP — Tank (STR 15, SUP 12)
- Shield Bash (cd 6s) — enemies within 120 take weapon damage + 34 stagger; a full stagger bar (100) stuns for 2s, then resets. Stun behavior is per-enemy (pause / slowed / enraged). An armed Retribution store rides the hit
- Sundering Bash (cd 10s) — as Shield Bash, and the stun also breaks armour (defence 0)
- Bulwark (2s, cd 25s) — self: invulnerable
- Iron Stance (8s, cd 20s) — self: 50% reduced damage taken
- Retribution (8s, cd 25s) — store the damage you take (pre-mitigation); at expiry the pool freezes and your next shield bash within 30s unleashes it
- Shield Wall (4s, cd 12s) — 200×80 wall in front of you that erases any enemy bullet crossing it

### SUP — Trickster (WIS 15, DEX 12, SUP 12 — the doc's `wis > dex > 0` ordering is approximated as flat minimums)
- Venom Sigil (6s, cd 8s) — curse the enemy near the cursor: 8 damage/s bleed
- Frost Sigil (4s, cd 8s) — curse: slowed 40%
- Frailty Sigil (6s, cd 12s) — curse: takes 50% more damage
- Smoke Veil (2s, cd 30s) — you and allies within 150 are invulnerable
- Blink (cd 8s) — instantly teleport toward the cursor, up to 300

### Deferred / not implemented
- Archer shootable dummy, trickster decoy, trickster invisibility — all need enemy retargeting onto non-player entities (future batch).

# Lore

## Laconic
Players spawn into a post-catastrophe world that has recently been patchwork-merged with other worlds after a bunch of magicians tried to set up long range world wide teleportation.

## Theme/message
The game is going to be an allegory for why governments, bureaucracy, ethics committees, and red tape are good, actually. Not really interested in the political undertones, I just think there's too many stories out there about how the big bad evil corporation is bad that I feel like the opposite is refreshing (Project Hail Mary and The Martian are kind of like this).
- Before the world was destroyed, the existence of magic meant that people didn't need to hoard or organise or fight for resources, so there wasn't really a need for governments, bureaucracy, ethics committees, and red tape since there was no scarcity which mean no poverty which meant no conflict which meant no wars.
- When the society eventually gets around to experimenting with magic, they don't have the maturity to do it properly which is how the catastrophe occurs. A bunch of wizards wanted to set up long range world wide teleportation because it'd improve lives and shit but they didn't do their due diligence and the teleportation gates ended up malfunctioning somehow which caused the world to get patch-work merged with other worlds.
- I want this to be a fantasy story that takes inspiration from The Martian and Project Hail Mary. This story is going to have the same competent characters and stuff, but it's not going to have the same organizational structures.

## Target Audience
The target audience is maturing children, but it should also appeal to adults. The vibe I'm going for is a grim hope dark fairy-tale fantasy. I want the game to be distinctly human, which is why I want to take inspiration from The Martian and Project Hail Mary. I kind of don't want there to be other intelligent life forms, but I'm not entirely against the idea. I do want some world patches/biomes to have remnants of fallen civilisations though. I don't want the story to be science coded.

## Broad Strokes
### The cause and lead up to the merge
**==The lead up to the merge is going to be part of the tutorial for the game.==** Players will spawn into a flashback to just before the world was merged. It is a scripted defeat scene. They will have end-game level gear. The scene after they die and spawn back in is when the real tutorial begins where some dude tells them how to play the game. The exact lore on how the players die is undecided. Some suggestions:
- Obvious one is having it so that the teleportation gates were breaking and monsters were leaking out and the players were being sent to deal with the monsters.
- Another one could be that it was possible to prevent the merge through some mechanism that required fighting a big bad monster or something but they failed.
- Maybe they needed to collect a bunch of materials or something to prevent the merge and didn't do it in time.

### Before the merge
### World vibes before the merge
Before the world was destroyed, it was a just a standard magic fantasy world. It had monsters and whatnot but other than that it was pretty peaceful. No evil society, no oppression/inequality, no big bad evil. The existence of magic meant that people didn't need to hoard or organise or fight for resources, so there wasn't really a need for royalty and governments and whatnot. This is the thing that leads to their downfall. Since there's no scarcity, there's no conflict and no need for governance, which means their governmental/institutional/organisational structures are undeveloped and immature. Once the society started getting into experimentation there was no higher body to appeal or report to whenever something bad was happening. I want the story to hold a mirror the strict modern day rules around science and ethics and whatnot. The world is answering the question "what if a society had access to world devastating technologies like nukes extremely early on in their tech tree, before their research institutions were able to mature?"

### Specific characters before the merge
#### Alice, leader of the people who caused the catastrophe (before the merge)
Since the world's organizational structures are immature, this society is just a group of well-meaning people who want to play around with and share magic. It's not really a society, just a group of enthusiasts who want to learn things. These are the people that set up the long range world wide teleportation gates. Over short distances, the teleportation gates are fine, but over long distances problems start to arise. For the first few years the teleportation gates work fine, but eventually things build up and the catastrophe occurs.

After the catastrophe, these guys are going to be the "bad" guys. They aren't really bad though, they just aren't concerned with facing punishment since it doesn't really make sense when over 75% of the world has been wiped off of the planet. You could have some of them suicide over the grief, have others self isolate, and have the rest be sorrowful but ultimately unrepentant in an "we can't afford to look backwards" sort of way.

#### Bob, leader of the people who don't forgive the people who caused the catastrophe (before the merge)
Bob is one of the people who discovered in advance that the teleportation gates were going to cause a world wide catastrophe. Before the catastrophe, Bob will have a lot of correspondence with Charlie (leader of the people who do forgive the people who caused the catastrophe). They both independently discovered that the catastrophe is imminent, which is how they found each other.
Originally, Charlie and Bob are going to be friendly with each other, but Charlie's forgiveness of Alice is going to drive Bob away.

#### Charlie, leader of the people who do forgive the people who caused the catastrophe (before the merge)
Charlie is one of the people who discovered in advance that the teleportation gates were going to cause a world wide catastrophe. Before the catastrophe, Charlie will have a lot of correspondence with Bob (leader of the people who don't forgive the people who caused the catastrophe). The both independently discovered that the catastrophe is imminent, which is how they found each other.
Originally, Charlie and Bob are going to be friendly with each other, but Charlie's forgiveness of Alice is going to drive Bob away.

### After the merge
I kind of don't want there to be intelligent life on the other biomes/patches since it's supposed to be a distinctly human story. Some biomes/patches can have remnants of a fallen civilisation though.

### Story line
The story of this game is peripheral to the gameplay. I have ideas for a story but that's it.
The story will follow 3 characters who played a large role in the lead up to the catastrophe. Players will be able to pick a side, however, this won't impact the gameplay. There won't be any faction PvP like in World of Warcraft.

~~The story line is going to follow the person who tried to warn everybody, the friend of the person who tried to warn everybody, and the magic organisation who set up all the teleportation gates.~~
- ~~**Safe hub 01 (person who tried to warn everybody):** the first safe hub that players spawn in to when they open the game. Philosophy: "We just want to survive. Not at any cost necessary, but we will overlook some things that we shouldn't and forgive people that we shouldn't just to survive." Biggest safe hub, lots of orphans. Most peaceful; zero beef with any other safe hub (others may have one-sided beef with it).~~
- ~~**Safe hub 02 (friend of the person who tried to warn everybody):** the second safe hub players will be scripted to visit. Philosophy could be "We don't want to just survive, we want to have standards. Just because the world has ended, that doesn't mean we should forgive or overlook people's actions just because we want to survive." Has (possibly one-sided) beef with safe hub 03 because the friend wants to stand up on the outcasted magician dude's behalf even though the outcasted magician dude doesn't want them to. **==You could probably use this faction as a stand-in for the edgy people who care about fairness and justice and whatnot==**. This faction's beef with safe hub 03 could probably be the main driver of the story.~~
- ~~**Safe hub 03 (magic organisation that outcasted the outcasted magician dude):** the third safe hub players will be scripted to visit (the second safe hub requires knowledge of this safe hub for context). Philosophy: "We want to rebuild what we had before. We fucked up in destroying the world but there's no point in crying over spilt milk." Most advanced. Will 100% **not** have beef with safe hub 01; the relationship there is focused around survival.~~
- ~~**End result of this story line gameplay-wise:** all of the player's items will be stored in one of the 3 safe hubs and the connection between the 3 from there on out will be completely severed. The story arc is that they were connected in the beginning but separated at the end. Before the story line ends players will be able to transfer items between these safe hubs, but after the story line ends players will have to walk between these safe hubs if they want to transfer items to them. Players will still be able to transfer wearable gear between safe hubs by teleporting, but if players want to transfer money or consumables or non-gear items or whatever then they'll have to walk. Players will still be able to teleport, though, since I want experienced players to be able to play with new players. **==You could probably even use this mechanic as a reason for experienced players to seek out newer players==**, though, I can't think of a way that wouldn't be hard to balance or cheat.~~
- ~~**Suggestion 1: Love triangle inspiration.** Take a lot of inspiration from love triangle tropes for the story line between the 3 safe hubs. (weak/pathetic man: make the leaders of safe hub 02 and 03 girls so the player base argues over best girl; strong/principled man: make the leader of safe hub 02 a guy and safe hub 03 a girl; weak man: make the 2 dudes gay; strong man: make the 2 dudes just really good friends.)~~
- ~~**Suggestion 2: Story that takes inspiration from other stories or historical events.** Not a huge fan — it doesn't lean into the "science institutions are good, actually" allegory, which I really really like. I don't know any historical events or fictional stories my story could take inspiration from.~~
- ~~**Suggestion 3**~~

I kinda don't like what I had above. I do like how the main conflict of the story is distinctly human though. One person made a mistake, another person wants to forgive and let bygones be bygones, whereas another wants them to acknowledge the mistake and pay for it somehow.

## Guild territories and guild content
Players will eventually be able to build cities and stuff, similar to how in bitcraft you can form empires. The server code for bitcraft is open source so it shouldnt be too hard to implement.

I kinda want guild content to be primarily built around the fact that the world is so large that it's practically untraversable. I think I want it to take inspiration from Factorio. As in, it should be possible to progress if you did things manually, however, the correct way to progress is to automate things.

The difference between bitcraft's empire system and mine:
- Guild territories are defined by anti-teleportation pillars that have to be maintained by dropping resources in them. You could make it so that a guild's pillar can be overridden by another guild's pillar if the other pillar has more resources than the other pillar.
- Guilds should be encouraged to trade with each other by having certain resources/minerals be gated to certain biomes. Similar to how in Factorio Space Age certain planets have different resources and you need to pipe the resources between each planet in order to progress.
	- Each biome could have area bosses that drop a lot of that area's resources when they die. One way of transporting resources from one biome to another could be by aggroing an area boss that can follow a player into a different biome.
- I think I want to have some type of guild territory pvp. Similar to clash of clans or old school RTS games.

## Random lore that could flavour the story
The following bits of lore either come about as a result of the gameplay, or are simply just interesting and I want it in the game.

## Torus world lore
Reason for this is cus my world is going to be a hexagonal grid that wraps around itself into a torus.
- I have no lore for this.
- I feel like it'd be boring if the torus world was linked to the apocalypse event. Maybe the world was always a torus and the inhabitants don't know any better so they just take it as fact.
- Maybe you could have like way points on hexagons in the inner-diameter. The in game lore reason for how the waypoints work is that they're going through the donut hole. Fits in with the current lore about how setting long range world teleportation is what destroyed the world; the hexagons that're on the inner diameter are closer together which means teleportation along the inner diameter won't be catastrophic.

# Biomes
- Biome chunks should reference a static world for each biome. As in, each biome has its own stored biome world and when a certain chunk changes to that biome world it persists.
- Make it so that adjacent chunks aren't able to have more than n points of interest? That way players won't have to roll every chunk's biome to optimize everything.
- See Rotating Biomes above (biome inspirations, chunk rotation, semi-persistence) and End game content above (each biome has its own resource; biome-gated resources drive guild trade).

# Inspirations, research, and new things to add

## New things to add
### Possible abilities
This list overlaps the class pitches in `Items/Abilities/Abilities.md`, which are already integrated under Classes & Class items above. It is organised by class: archer/gunslinger/bullet master, warrior, wizard, healer/witch doctor, paladin, tank, trickster. Distinct wording: the bullet master's shootable dummy is also pitched as a "blackhole thing".

### Near miss detection
(nothing written yet)

## Game Inspirations
Realm of the Mad God (core gameplay), Alabaster Dawn (core gameplay), Runeward Online (core gameplay), Pixel Quest (core gameplay), BitCraft Online (non-core gameplay), Clash of Clans (non-core gameplay), Factorio: Space Age (non-core gameplay), Hollow Knight (art style), Hollow Knight: Silksong (art style), t3ssel8r videos (art style), Borderlands (item system), and Terraria (idk I just think it's cool).

## Example videos
### Non-RotMG videos
[[<https://youtu.be/Psh3f2np4Ok?si=zOuCcC2gKdqe38fy&t=660>|out]]

### RotMG videos
#### Group videos
[[<https://youtu.be/-sCk434v3zg&t=41>|RotMG: RedMagePOW Clip Compilation 2]]

[[<https://www.youtube.com/watch?v=FqACs109eIE>|RotMG: RedMagePOW Clip Complilation]]

[[<https://youtu.be/UuDYmAeUrDw&t=14>|RotMG - omo Full Skip Voids]]

[[https://www.youtube.com/watch?v=w0_A4fmIwJ8|RotMG : Full Skip Lost Hall#1]]

[[<https://www.youtube.com/watch?v=RHZxqIjksfc>|RotMG Lost Halls Void Speedrun ft. Marble Seal drop (08:52:01|[08:52:01)]]]]

[[5:48)](<https://www.youtube.com/watch?v=885sidnkIps>|(rotmg o3|[rotmg o3) Fastest Sorc 2 million top damage (5:48)]] Fastest Sorc 2 million top damage (5:48)]]

[[<https://www.youtube.com/watch?v=audTKsu6qTA>|Completing My First Marble Colossus Ever! - Rotmg Lost Halls Progression!]]

[[<https://www.youtube.com/watch?v=ZRKva2qU9kE>|RotMG Lost Halls 3 man full skip run]]

[[<https://www.youtube.com/watch?v=uvYRJ30DF5c>|Rotmg - WR Void led by BigDort (5:42|[5:42)]]]]

[[<https://www.youtube.com/watch?v=rgSjmhpQGM8>|Rotmg - Bad players get world record Oryx 3 (2:29.39|[2:29.39)]]]]

[[<https://www.youtube.com/watch?v=fNIsiBib2jc>|Oryx 3 - Gemsbok mini RotMG]]

[[<https://www.youtube.com/watch?v=eHdOlBJWA8Q>|Oryx 3 - Beisa mini RotMG]]

[[<https://www.youtube.com/watch?v=QiUTa-oDBxo>|Oryx 3 - Leucoryx mini (with 3rd counter|[with 3rd counter) RotMG]] RotMG]]

[[<https://www.youtube.com/watch?v=-mdb-6HktkQ>|Oryx 3 - Dammah mini RotMG]]

#### Solo videos
[[<https://www.youtube.com/watch?v=IGowzviaTUA>|RotMG - lvl 1 Kitsune Umi gearless consumableless solo]]

[[<https://www.youtube.com/watch?v=mau4t3P7Hpw>|RotMG Solo Plagued Nest WR (3:21|[3:21)]]]]

[[<https://www.youtube.com/watch?v=9CWy2cQJn34>|RotMG - Secluded Thicket no-hit Solo]]

[[<https://www.youtube.com/watch?v=-eOg3N7OKLY>|RotMG - Murcian No Hit]]

[[<https://youtu.be/mJUmuELLeYc&t=485>|my best t8 petless solo void run]]
