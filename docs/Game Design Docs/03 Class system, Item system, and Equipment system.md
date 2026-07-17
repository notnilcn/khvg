# Class system overview
Classes aren't going to be explicitly defined. They are going to be defined by the gear that exists in the game and the stat requirements for that gear. When players figure out the optimal builds and stat allocations, they will naturally sort themselves in the stereotypical MMORPG classes.

**The reason why it is defined this way is because I want to be able to define new classes simply by adding new gear to the game**. As in, if I want to define a new class (e.g. a monk), then I'll simply just define the exact stat distribution I want for that class (e.g. monk = equal parts wisdom + strength + dexterity) and I add new gear that requires that stat distribution.

~~This means that players will be able to re-spec their characters into different classes by redistributing their stats (we can probably make this extremely impractical though, for example we could make it so that all viable gear absolutely requires you to have a certain distribution of stats - as in, their stat distributions need to meet both a relative threshold and an absolute threshold) which may or may not be a good thing.~~

~~This also means that players who allocate their stats inefficiently will be able to play as narratively conflicting classes (e.g. if a player chooses to distribute their stats evenly, then they will be able to use any gear they'd like). I'm not sure if this should be allowed. The way players will be discouraged from doing this will be by making it so that allocating your stats inefficiently makes it so that you fuck up your progression. Or maybe we can make it so that the more a player uses a certain type of gear, the more "locked in" they become with their stat allocation. As in, if a player allocates their stats evenly but they only ever use healer gear, then the more they use their healer gear, the more their wisdom stat locks in. Stats that aren't locked in can be reallocated however the player wants.~~

We can figure out the details later
# Stats
Right now, the stats are going to be:
- Dexterity
- ~~Vitality~~ I think Vitality should be rolled into strength.
- Strength
- Wisdom
I can't think of any other stats to add that would make meaningful sense. I don't like having a luck stat.
# Temperament
There are also going to be other stats called Temperament stats. There really isn't much of a difference between Temperament stats and normal stats. They act the same way as normal stats, so any differences would be entirely conceptual. The conceptual difference is that temperament stats flavour what a character does with their normal stats. The temperament stats are:
- Damage dealer
- Supporter
- Artisan
Different stat distributions make different classes.
E.g. A tank is a strength supporter. A warrior is a strength damage dealer. A paladin is a strength + wisdom supporter. A healer is a wisdom supporter. A wizard is a wisdom damage dealer.

The artisan temperament is completely useless for combat but players who have characters that are fully specced into artisan will be valuable in the end game. I kind of want the process for leveling up an artisan character to be intentionally gruelling - I think it'd be cool for players to think "what the fuck?" when they see someone who has fully specced into the artisan temperament.
# Item and Equipment system overview
- Items are designed around RotMG's swap-out meta.
	- Weapons can have toggles that change shot pattern.
	- Characters have n=6 ability slots and can equip multiple abilities. Abilities are tied to items and some ability items take up multiple ability slots.
- Items will have enchantments and modifiers, similar to the Borderlands item system.
In RotMG, one thing people do to maximize dps is have item swapouts. I want to design my equipment system around this so that they don't have to, since it was pretty clunky.
The main reason why people used swapouts is because of the damage calculation nuances. When enemies are staggered or are armor broken, it is often better to swap out to a set that has a large bullet count because higher defence disproportionately effects builds that have a larger bullet count.

With this in mind, we can simply just allow weapons to have toggles which can change the shot pattern between low-number-of-shots & high raw damage vs large-number-of-shots & low damage. These toggles will be tied to item enchantments.
- There is a staff in RotMG called the staff of extreme prejudice that shoots out 10 bullets in a circular pattern. It does the highest dps in the game but it requires you to literally stand on the enemy to land all 10 shots, so it's only used when the enemy is staggered. This feature will be added into my game via a item modifiers/enchantments. I kind of want this shot pattern to be one of those "useless in the early game, game breaking in the late game" things because the off meta builds that have huge payoffs thing is a pretty classic rpg fantasy.
The reason why I want to address it this way rather than integrating swap-out sets is because I kind of don't want item swapping to be as popular in my game as it is in RotMG.

This item enchantment modifier thing that changes the shot pattern sounds a lot like the Borderlands item system, and that's because my game's item system will take a lot of inspiration from Borderlands' item system.
All of the buffs and status conditions in [[02 Gameplay#Buffs, debuffs, and damage calculation nuances]] are able to show up as enchantments and modifiers in my item system. If the enchantments are good, some lower-tier weapons may be better than higher tier weapons.
- You could probably add enchantment slots that can be modified by the player, as well as innate enchantments that can't be modified by the player but don't take up an enchantment slot. You could probably make weapons that have the staff of extreme prejudice's shot pattern toggle enchantment extremely valuable for minmaxers who want to speed-run things.

Abilities are tied to equipment. Characters have n=6 ability slots, and some abilities take up multiple slots (e.g. shields take up 6 slots, spells take up 2 slots, and tomes/scriptures take up 3 slots)
Some ability items modify your main ability if they're in your equipment slot (e.g. paladin buffs), whereas other ability items require you to swap to it to cast it (e.g. wizard spells).
# Classes
The stat distribution syntax is kinda self explanatory. If a stat isn't mentioned, then it can be anything. If a stat is mentioned, then it needs to be greater than 0. i.e. every time you see a ")" replace it with ">0)". I use "≳" instead of ">" is because ">" can't be used in filenames.
## Class items
Class items are going to be locked behind stat thresholds.
### DPS
#### Archer
Similar to the archer in RotMG
##### Weapons
- Bow
##### Abilities
- idk
#### Warrior (str) & (dps)
##### Weapons: Sword (similar to pixel quest)
- Play pixel quest on roblox or look up the sword strike animation for it.
- You could probably look to terraria for different sword ideas.
##### Ability: Magic tattoos
Similar to Witch Hat Atelier. In RotMG, the warrior's ability item is just a helmet which sucks.
Higher grade tattoos are made from higher grade inks and whatnot. Monster blood, god ichor, special tree sap, etc.
The in-game justification for how warriors can swap between abilities is that they have to do some preparation for the tattoo they want to use.
It'd be cool if you could make the tattoos glow when you activate them.
You should probably make these items like a consumable or something. Like, to equip the ability you use a consumable and it 
- converts all incoming damage for the next n seconds into bleed damage
	- you could probably add some conditions to make it so that they can shed the bleed damage.
- makes their sword do more damage for the next n seconds
	- you can be creative with it by making their sword damage burn the enemy or modifies their sword to store all the damage they deal within the next n seconds and doubles it or smth but the general gist is more damage
	- for the "sword stores all the damage they deal within the next n seconds" thing, you could probably add a fail condition that makes it so they deal 0 damage
- enter a windup animation to do a short teleport. Similar to the Kensei's ability from RotMG. Intention is for players to be able to dodge upcoming bullets for certain boss phases. Cracked players should be able to solo any boss with this ability.
#### Wizard (wis) & (dps)
##### Weapons: Staffs + Wands
- staffs/staves (similar to RotMG)
	- S.T.A.F.F. ripoff (don't copy the bullet pattern exactly 1 for 1)
	- [S.T.A.F.F. Showcase - This Weapon SHREDS Everything | RotMG](<https://youtu.be/m0COVnAtCxA&t=107>)
	- Tiered staffs (don't copy the bullet pattern exactly 1 for 1)
	- [(Rotmg) Godly Wizard PPE](<https://youtu.be/MKtTq4U3hMU?si=ReRPrelHkF1btWOA&t=468>)
- Wands (similar to RotMG)
	- Lumiaire
	- Tiered wands
##### Ability: Spell scrolls
- Spell that spawns a delayed beam of light that deals damage
	- [Chaotic Scripture only Cult (Rotmg)](<https://www.youtube.com/watch?v=dU0andwLZmk>)
- Spell that summons bullets that converge on or from or around the player's cursor
	- [Rotmg Penetrating Blast Spell, shot pattern](<https://www.youtube.com/watch?v=-aCETL8lteA>)
	- [WHY TABLET IS BETTER THEN PARA SPELL ( in o3)](<https://www.youtube.com/watch?v=4W4metbXxsc>)
- Spell that forces the player to stop shooting to enter a wind up animation that sends a fireball or some shit
### SUP
#### Healer (wis) & (sup)
##### Weapons: Wands
Copy RotMG
- Luminaire
- Tiered wands
##### Ability: Tomes/books/scriptures
- Mushroom tome ripoff
	- Spawns a spire at a location and heals everyone in a radius around it.
	- Back when RotMG had more players there used to be some tech around using this for group coordination because healers could direct where the melee players would have to stand, which was useful if they knew all the boss phases and shit. i remember hearing raid leaders saying "guys, push up. stop staying in the back of the group, you're safer if you push up because that's where all the mushroom tomes are. you'll heal more if you push up."
- AoE heal
- Targeted heal (heals player closest to cursor or heals the lowest health player that's within a radius of the cursor)
- Debuff removal
#### Paladin
Similar to the paladin in RotMG.
Paladins will have less defence than tanks but will have a higher health pool. This will make them weak against enemies that fire a large amount of low-damaging bullets, or enemies that spawn in groups or spawn a bunch of mobs.
I don't really want there to be any religions in the game so maybe don't call them paladins.
##### Weapons: Sword (similar to pixel quest)
- Play pixel quest on roblox or look up the sword strike animation for it.
- You could probably look to terraria for different sword ideas.
##### Abilities
The actual item that RotMG uses are seals but that's kinda boring. You could probably reuse the tattoo idea?
- Temporarily invulnerable to all damage
	- Self buff
- Temporarily buff nearby allies to have increased health regen
- Buff all nearby allies to deal more damage
- Converts all damage into bleed damage
#### Tank (str) & (sup)
Similar to the knight in RotMG.
Knights will have more defence than paladins but will have a lower health pool. The lower health pool effectively shouldn't matter if the healer is good.
There should be some end game bosses where knights will only be useful for their stagger and armour break debuffs.
##### Weapons: Sword (similar to pixel quest)
- Play pixel quest on roblox or look up the sword strike animation for it.
- You could probably look to terraria for different sword ideas.
##### Ability: shield
- Shield bash = stagger bar (similar to how knights can stun in rotmg but less so)
- Shield bash applies armour break debuff or expose debuff (similar to ogmur/samurai in RotMG. the expose debuff just adds like + 10% damage to all bullets that hit the enemy. I think this +10% damage ignores the enemy's defence, so weapons that shoot a lot of bullets pair well with this debuff. armor break reduces enemy defence to 0)
- Temporarily invulnerable to all damage
	- Self buff
	- 
#### Trickster (wis ≳ dex) & (sup)
This class combines the assassin, rogue, and trickster from RotMG into one class.
##### Weapons: idk
Crossbow probably? Idk. I don't like how daggers are implemented in RotMG and idk how to implement them in this game. Could probably just give them magic throwing knives but i feel like we can come up with something better.
Terraria has magic throwing knifes but thats kinda weird.
##### Ability: a whole buncha shit (implement all)
Maybe make this class's magic be tied to physical items rather than magic scrolls like the wizard.
- Invisibility (cloak)
- Poisons and debuffs (vials of liquid? i don't like that it comes off as a consumable though)
- Teleport (magic rocks or orbs?)
- Decoy (magic rocks or orbs?)
	- decoys summon a mirror image that can take aggro



# Old notes
Subclasses:
- periodic invulnerability (str + wis spec)
- high defence (str + vit spec)
- high health (vit + str spec)
My content brain is telling me to do this via gear, but my monetisation brain is telling me to do it via subclasses since you're gonna be monetising character slots.

High defence will be good for lots of bullets (reduces bullet damage by a flat amount, lots of bullets = lots of reduction).

High health will be good for a small amount of high-damaging bullets. Also for armor break situations.

Periodic invulnerability will be skill based and will require party coordination if you want to maximize it. The **only** advantage that this should give over the other tech trees is more damage. A party with 1 dps + 1 defence/health tank + 1 support should roughly deal the same amount of damage as a party with 1 support + 2 periodic invulnerability knights, but only if the periodic invulnerability knights have the right gear. In some dungeons, the 1 support + 2 periodic invulnerability tanks setup should be more optimal. **This 1 support + 2 periodic invulnerability tanks set up should require extreme amounts of danger and extreme amounts of coordination**. If tanks don't tank correctly, the enemy will aggro onto the support who should die instantly due to immobility and low defense.

## Old notes v2
Should be the classic tank. Since it has high defense, it can tank a lot of weak, low-damage bullets.

High defense tanks are useful for:
- Boss fights that have a lot of weak, low-damage bullets that target the nearest player.
- Boss fights that have a lot of minions that fire weak, low-damage bullets

The purpose of the tank in these boss fight is the clear space/bullets for the support/dps who should be behind the tank.

You could probably have end-game boss that requires the 2 main types of tanks by having a boss that shoots a low amount of targeted, high damaging, armor piercing shots (high hp tank would be required here) and also spawns a lot of minions that deal lots of instances of low-damage (high defense tank would be required here).

## Random notes
There are going to be 2 main types of tanks, high defence tanks and high hp tanks. 

There should be dungeons that require scouts in order to optimize. Similar to how, in rotmg, the shatters requires you to destroy the statues and how the lost halls requires you to find the boss room or destroy the pots.

Scouts should be able to solo certain dungeons, but it should be more optimal to do the dungeons in groups.

There should be end game builds that all thieves can use to put them on par with a dps in terms of damage under certain circumstances (e.g. if the enemy is properly debuffed).

This class might break the balance of the game.
I like the idea of having a class that can make or break a dungeon run though, similar to how a trickster can fuck up certain boss phases in RotMG. I like how tricksters are required for full-skip void runs in RotMG.