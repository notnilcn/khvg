# Core Gameplay Loop
The core gameplay loop is going to be extremely similar to Realm of the Mad God:
Level up, upgrade gear, dodge enemy bullets, land your own bullets, use your abilities, etc.
There will be nuances like knowing what to do during boss phases but we don't need to get into that. Watch the RotMG videos at the end if you want examples.

There is kind of going to be perma-death in this game, so that's also part of the gameplay loop. I say kind of because, when a character dies, they lose all their skills and stats but they're still able to pick up their items if they reach their body in time. It'd be true perma death if they couldn't pick up their items.
If a player dies in a spot that's far away from any waypoints or spawn points, or if a player dies in a dungeon (dungeons will close after a minute or so), then they won't be able to retrieve their items.
The purpose of this mechanic is so that the game isn't as punishing for newer players.

It'd be possible to add the standard MMORPG quests (fetch quests, defeat n monsters quests, etc) but that shit's boring.
# Buffs, status conditions, and damage calculation nuances
## Damage calculation nuances
Damage calculations nuances come from the interaction between defence and health.
I kind of don't want there to be any elemental damage.

Defence reduces bullet damage by a flat amount (minimum damage=1).
Health increases the amount of raw damage you can take.

What that translates to is:
High defence makes it so that a large number of low-damage bullets are less effective.
E.g. Against a target that has 5 defence, 10 bullets that deal 10 raw damage only deals max(10-5,1)\*10=50, whereas 1 bullet that deals 100 raw damage deals max(100-5,1)\*1=95 damage.
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
# End game content
End game content is not just going to be harder bosses and harder dungeons.

End game content is going to essentially just be building a factory for your guild's territory. Pretty much just Factorio: Space Age + Clash of Clans (and any of the standard RTS games), except you play as the builders and the miners and the individual units and what not.

Each biome will have area bosses that can be aggroed and dragged across the world. One tactic for collecting resources from other biomes will be by aggroing and dragging area bosses into your territory.

There will be territory PvP but there won't be direct PvP.
# Beginner content
New players are going to follow a story line so they aren't going to be directionless like in Realm of the Mad God.
# Rotmg videos
## Group videos
[RotMG: RedMagePOW Clip Compilation 2](<https://youtu.be/-sCk434v3zg&t=41>)

[RotMG: RedMagePOW Clip Complilation](<https://www.youtube.com/watch?v=FqACs109eIE>)

[RotMG - omo Full Skip Voids](<https://youtu.be/UuDYmAeUrDw&t=14>)

[RotMG : Full Skip Lost Hall#1](<https://www.youtube.com/watch?v=w0_A4fmIwJ8>)

[RotMG Lost Halls Void Speedrun ft. Marble Seal drop (08:52:01)](<https://www.youtube.com/watch?v=RHZxqIjksfc>)

[(rotmg o3) Fastest Sorc 2 million top damage (5:48)](<https://www.youtube.com/watch?v=885sidnkIps>)

[Completing My First Marble Colossus Ever! - Rotmg Lost Halls Progression!](<https://www.youtube.com/watch?v=audTKsu6qTA>)

[RotMG Lost Halls 3 man full skip run](<https://www.youtube.com/watch?v=ZRKva2qU9kE>)

[Rotmg - WR Void led by BigDort (5:42)](<https://www.youtube.com/watch?v=uvYRJ30DF5c>)

[Rotmg - Bad players get world record Oryx 3 (2:29.39)](<https://www.youtube.com/watch?v=rgSjmhpQGM8>)

[Oryx 3 - Gemsbok mini RotMG](<https://www.youtube.com/watch?v=fNIsiBib2jc>)

[Oryx 3 - Beisa mini RotMG](<https://www.youtube.com/watch?v=eHdOlBJWA8Q>)

[Oryx 3 - Leucoryx mini (with 3rd counter) RotMG](<https://www.youtube.com/watch?v=QiUTa-oDBxo>)

[Oryx 3 - Dammah mini RotMG](<https://www.youtube.com/watch?v=-mdb-6HktkQ>)

## Solo videos
[out](<https://youtu.be/Psh3f2np4Ok?si=zOuCcC2gKdqe38fy&t=660>)

[RotMG - lvl 1 Kitsune Umi gearless consumableless solo](<https://www.youtube.com/watch?v=IGowzviaTUA>)

[RotMG Solo Plagued Nest WR (3:21)](<https://www.youtube.com/watch?v=mau4t3P7Hpw>)

[RotMG - Secluded Thicket no-hit Solo](<https://www.youtube.com/watch?v=9CWy2cQJn34>)

[RotMG - Murcian No Hit](<https://www.youtube.com/watch?v=-eOg3N7OKLY>)

[my best t8 petless solo void run](<https://youtu.be/mJUmuELLeYc&t=485>)