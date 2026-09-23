yeah nah i dont really give a shit about any of this shit tbh i'm just a guy.
a lot of this was written by ai but i corrected it because i wanted to game myself because i know i'm the type of person who falls to that one talking point about how if you want to ask a question and have it answered quickly and correctly then what youre supposed to do is you ask the question and then make an alt account and answer it wrongly so that some autistic nerd (me) does the "erm ackshually" thing and corrects the incorrect answer.

# Class system overview
Classes aren't going to be explicitly defined. They are going to be defined by the gear that exists in the game and the stat requirements for that gear. When players figure out the optimal builds and stat allocations, they will naturally sort themselves in the stereotypical MMORPG classes.

**The reason why it is defined this way is because I want to be able to define new classes simply by adding new gear to the game**. As in, if I want to define a new class (e.g. a monk), then I'll simply just define the exact stat distribution I want for that class (e.g. monk = equal parts wisdom + strength + dexterity) and I add new gear that requires that stat distribution.

The reason for this is that I wouldn't have to update the spacetimedb database schema whenever I wanted to add a class, so players wouldn't have to update their clients whenever an update rolls around.

# Stats
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
# Item and equipment system overview
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

Instead: weapons get toggles, tied to enchantments, that flip the shot pattern between low-count/high-damage and high-count/low-damage. Same tactical decision, no inventory shuffling.

RotMG's Staff of Extreme Prejudice is the reference case — it fires 10 bullets in a circle and does the highest DPS in the game, but only if you're standing directly on top of the enemy, so it's only good against staggered targets. I want that shot pattern to exist here as a modifier/enchantment: useless early, game-breaking late. Off-meta builds with a huge late-game payoff is a classic RPG fantasy and I want it in this game.

All the buffs and status conditions in [[02 Gameplay#Buffs, status conditions, and damage calculation nuances]] should be able to show up as enchantments/modifiers. If the enchantments are good enough, a lower-tier weapon should sometimes beat a higher-tier one.

Enchantment slots should be player-modifiable, plus some innate enchantments that can't be changed but also don't eat up a slot. A weapon born with the Extreme-Prejudice-style toggle as an *innate* enchantment should be extremely valuable to minmaxers.

Abilities are tied to equipment (n=6 ability slots, some abilities eating multiple). Some ability items passively modify your kit just by being equipped (paladin buffs), others require you to actively swap to them to use them (wizard spells).
# Classes & Class items
The stat distribution syntax for each of the classes is kinda self explanatory.
Class items are going to be locked behind stat thresholds.

Bullet master has access to all 3 bullet interaction abilities: delete, split, attract. Warrior + tank + paladin have delete, wizard + witch doctor have attract, trickster has split.

Trickster has access to all 3 dodge abilities: telegraphed short range teleport, ground grapple, instant medium range teleport. Trickster also has access to a bunch of other abilities: invisibility, poisons, decoys.
### DPS
#### Bullet Monk (dex > 0) & (dps > 0)
I kinda dont like the class name but it makes the most sense.

Make it so that the bullet master can store and delay their dps to blast counter phases or wait out invulnerability phases. In the long run, they should do less dps than other classes but when you take into account invulnerability and whatnot then these guys should out-damage other classes because of their ability to store and delay the damage they deal.
##### Bullet Monk Weapons
- idk they just use their hands to summon bullets
	- Summon floating orbs that shoot continuous bullets. The actual physical animation for how they shoot in a particular direction with this is that they point with their fingers.
	- Conjure orbs that are periodically thrown at enemies. The actual physical animation for how they shoot in a particular direction with this is that they throw shit.
- Bullet Monk weapons can innately toggle between charge/shotgun/piercing/radial/etc. shot patterns
	- Floating orbs that shoot continuous bullets have an animation that shoot at the ground and split into radial bullets. Other patterns are just different hand signs i guess (open palm, pointed finger, etc).
	- Confjured orbs that are periodically thrown at enemies are simply just thrown differently. Back hand scattering/throw = shotgun. Front hand throwing = charge. Overhand pegging = pierce. Throwing at ground = radial.
##### Bullet Monk Abilities
- Big charge shot: self-slows, disables weapon, fires a single shot that gets stronger the longer it's charged
- Big volley shot: self-slows, disables weapon, fires a large spread that gets stronger the longer it's charged — especially strong against low-defense enemies
- Big radial shot: self-slows, disables weapon, fires a fuck ton of bullets in a radius around the player position that gets stronger the longer it's charged — strong against staggered enemies
- Spawn a dummy on self that allies can shoot; the archer absorbs the damage that the dummy takes and releases it as a big charge/volley shot
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
Higher grade tattoos are made from higher grade inks: monster blood, god ichor, special tree sap, etc.
It'd be cool if you could make the tattoos glow when you activate them.
To make warriors special you could probably make these items a consumable or something. Like, to equip the ability you use a consumable and it overwrites one of your existing tattoos.
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
- a circle that slows enemies
- a circle that makes enemies receive more damage
- a circle that distributes all damage received within it evenly across all players standing in it (group-coordination tech, similar to the mushroom-tome-push-up thing above but for damage instead of healing)
#### Paladin
Similar to the paladin in RotMG.
Paladins will have less defence than tanks but will have a higher health pool. This will make them weak against enemies that fire a large amount of low-damaging bullets, or enemies that spawn in groups or spawn a bunch of mobs.
I don't really want there to be any religions in the game so maybe don't call them paladins.
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
#### Trickster (wis > dex > 0) & (sup > 0)
This class combines the assassin, rogue, and trickster from RotMG into one class.
##### Trickster Weapons
- Guns?
##### Trickster Ability
Maybe make this class's magic be tied to physical items rather than magic scrolls like the wizard.
- Invisibility (cloak)
- Poisons/debuffs
- Teleport
- Decoy — summons a mirror image that takes aggro
- Teleports all party members to a pocket dimension that makes them briefly invisible + immune to all damage

# Accessory items
Accessory items 
