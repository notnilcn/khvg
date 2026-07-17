This .md file has all of the overviews for various parts of the game. There may or may not be dedicated .md files for some of the things in this .md file.

# **Overview of game genre**
Game genre is bullet-hell mmo (similar to Realm of the Mad God), with future plans for sandbox building and crafting (similar to BitCraft). The sandbox building and crafting aspect is only there because this game will be using a server backend called spacetimedb which lends itself well to that type of game. The sandbox building and crafting aspect is entirely shelved until the bullet-hell stuff is done because I want to steal the Realm of the Mad God player base as soon as possible (the RotMG player-base is desperate for a better game that's similar).

The game will probably be high fantasy.

# **Overview of [lore](<04 Lore.md>) and [lore-relevant gameplay things](<01 Executive Summary.md>)**
**<u>The lore is entirely shaped by the gameplay</u>**.
#### **Gameplay things that shape the lore**
- Safe hubs (similar to RotMG's nexus) ^safe-hubs
	- Similar to RotMG, players will be able to teleport to the safe hub (i.e. the RotMG term for this is "nexus") whenever they want.
		- By default the teleport will be delayed. As in, they press the teleport button and they will teleport after 2 seconds.
		- There will be a consumable that players can purchase with real-world-money that teleports them instantly.
- Unsafe world ^unsafe-world
	- The majority of the world outside of the safe hubs is unsafe.
- Perma-death ^perma-death
	- Similar to RotMG, however, I'm going to be more lenient with rolling back player deaths since it was genuinely frustrating whenever you'd die a bullshit death in RotMG
		- Should be possible with Spacetimedb's architecture
- Spacetimedb's architecture (scope creep stuff) ^spacetimedb-scope-creep-stuff
	- Spacetimedb is good for persistent worlds since everything is a table.
	- Hexagons that players can build structures on
		- Similar to bitcraft
		- Hexagon will have height
			- Under the hood the game is going to be 2d, but bitcraft's hexagons have height so why not. Also, RotMG is pretty flat which is kinda bland.
	- The unsafe parts of the world/landscape/map changes every day (I'm not sure if this is feasible in spacetimedb)
		- This should be doable via lazy evaluation. i.e. whenever a player enters a stale part of the world/landscape/map, it changes based on whether enough time has passed since the last time it was updated.
#### **Lore and Overview**
I want the lore to have a similar vibe to Hollow Knight, except instead of bugs it's humans and instead of a dark fairytale it's grim hope fantasy.
#### **The world has been destroyed**
Gameplay things that this lore is built around:
- [Safe hubs](<99 (outdated) Game Design Document.md#^safe-hubs>)
- [Unsafe world](<99 (outdated) Game Design Document.md#^unsafe-world>)
The world has already been destroyed. Since the game is gonna be high fantasy, the reason why the world was destroyed is cus the magic society were all dumb cunts or some shit. You could probably have the safe hubs be set up by a outcasted mage dude who foresaw the apocalypse event & tried to warn everybody but was outcasted for it, but that story is kinda boring and overdone to me. I can't think of anything better, so maybe instead of making the magic dude scornful we just make him unreasonably empathetic and understanding and good and whatnot, but we do **<u>NOT</u>** make him some old grandpa cus I'm kinda tired of that trope as well. I like the idea of grim-hope.
#### **Outcasted mage dude**
Make him similar to Ryze from league of legends, but make him less unapproachable/unrelatable/other-worldly.
Guy who tries to prevent the apocalypse because his master took him in and trained him to do that. His personal reasons for trying to prevent the apocalypse shouldn't be overly altruistic. He does it because he has the capability to do it and because that's how he was raised. He could fuck off and leave it to someone else but like why. His response to "Why are you doing this?" is something like "I've thought thinking about quitting and I decided it's not worth the hassle. Like yeah I could call it quits and yeah I don't get any appreciation and yeah no one knows what I'm doing but like I'm going to die anyway, I might as well spend my life doing what I'm capable of. No one else sees what I see. No one else can do what I do. It feels good to do it. No point in thinking about it any further than that."
#### **Players are souls that're injected into artificial magic bodies**
Gameplay things that this lore is built around:
- [Safe hubs](<99 (outdated) Game Design Document.md#^safe-hubs>)
- [Perma-death](<99 (outdated) Game Design Document.md#^perma-death>)
When players die, they can choose to respawn into any of the safe hubs they have visited (items don't transfer between safe hubs, and safe hubs will be extremely far apart). I don't want the lore around this to be an allegory for classism or anything because that's boring and overdone, so the dialogue box when a player dies should be like "do you want to be respawned?" with a yes and no, and if they pick no then it closes the game. I want the feel of the game to be grim-hope, so maybe the reason why the native safe-hub inhabitants are doing this is because the safe hubs need to be maintained and there's simply just not enough people to maintain it anymore. You could probably make this soul-injecting stuff taboo or whatever. Maybe have something about how souls are naturally supposed to go through a cycle, and this soul-injection stuff interrupts that cycle, and the only reason why the native safe-hub inhabitants are doing this is because they're desperate. **==You could probably use this piece of lore to encourage players to take time off of the game after they die to let their souls go through a proper cycle.==** Maybe just make the reward something cosmetic though. Maybe also make it a cosmetic thing to identify players who are sweaty as fuck and no-life the game.
#### **There are 3 different lore-based safe hubs**
Gameplay things that this lore is built around:
- [Safe hubs](<99 (outdated) Game Design Document.md#^safe-hubs>)
- [Spacetimedb's architecture](<99 (outdated) Game Design Document.md#^spacetimedb-scope-creep-stuff>)
	- Spacetimedb's architecture allows these safe hubs to be far apart from each other.
###### **01 Safe hub that belongs to the outcasted magician dude**
- This will be the first safe hub that players spawn in to when they open the game.
	- The philosophy of this safe hub will be "We just want to survive. Not at any cost necessary, but we will overlook some things that we shouldn't and forgive people that we shouldn't just to survive.". This safe hub will be the biggest safe hub and it will have a lot of orphans and whatnot.
- This safe hub will be the most peaceful. This safe hub has zero beef with any of the other safe hubs, though, other safe hubs may have one-sided beef with this safe-hub.
###### **03 Safe hub that belongs to the magic organisation that outcasted the outcasted magician dude**
- This will be the third safe hub that players will be scripted to visit (the second safe hub is below, but it requires knowledge of this safe hub as context).
	- The philosophy of this safe hub will be "We want to rebuild what we had before. We fucked up in destroying the world but there's no point in crying over spilt milk". This safe hub will be the most advanced and the requirements to join it will be high, but there won't be any real benefit for players to join this safe hub.
- This safe hub will 100% **<u>not</u>** have beef with the [safe hub that belongs to the outcasted magician dude](<99 (outdated) Game Design Document.md#**Safe hub that belongs to the outcasted magician dude**>). I want these guys' relationships with each other to be focused around survival.
###### **02 Safe hub that belongs to a friend of the outcasted magician dude**
- This will be the second safe hub that players will be scripted to visit.
	- The philosophy of this faction could be something like "We don't want to just survive, we want to have standards. Just because the world has ended, that doesn't mean we should forgive or overlook people's actions just because we want to survive."
- This safe hub will have (possibly one-sided) beef with the [safe hub that belongs to the magic organisation that outcasted the outcasted magician dude](<99 (outdated) Game Design Document.md#**Safe hub that belongs to the magic organisation that outcasted the outcasted magician dude**>) because the friend wants to stand up on the outcasted magician dude's behalf even though the outcasted magician dude doesn't want them to. **==You could probably use this faction as a stand-in for the edgy people who care about fairness and justice and whatnot==**. This faction's beef with the [safe hub that belongs to the magic organisation that outcasted the outcasted magician dude](<99 (outdated) Game Design Document.md#**Safe hub that belongs to the magic organisation that outcasted the outcasted magician dude**>) could probably be the main driver of the story.
#### **Lore-based safe hub story line**
You could probably take a lot of inspiration from love triangle tropes for the story line between the 3 safe hubs. If you are a weak and pathetic man, then you would make the leaders of safe hub 02 and safe hub 03 girls so that you could have the player base argue over which one is best girl. If you are a principled and strong man, then you'll make the leader of safe hub 02 a guy, and the leader of safe hub 03 a girl. Maybe make the leader of safe hub 03 the daughter of the magic organisation's leader who died. If you are a weak man, you'll make the 2 dudes gay. If you are a strong man, you will make the 2 dudes simply just 2 dudes who are really good friends.

The main result of this story line would be that all of the player's items will be stored in one of the 3 safe hubs and the connection between the 3 from there on out will be completely severed. As in, before this story line players will be able to transfer items between these safe hubs, but after this story line players will have to walk between these safe hubs if they want to transfer items to them. Players will still be able to transfer wearable gear between safe hubs by teleporting, but if players want to transfer money or consumables or non-gear items or whatever then they'll have to walk. Players will still be able to teleport, though, since I want experienced players to be able to play with and new players. **==You could probably even use this mechanic as a reason for experienced players to seek out newer players==**, though, I can't think of a way that wouldn't be hard to balance or cheat.

#### **Player-made guild safe hubs and end game content**
I kind of don't want there to be a big bad evil guy.
Like, there can be huge monsters and stuff but I don't want there to be any uniquely special monster.
I think after the safe hub story line, players will be encouraged to make or join their own safe hub guilds which will be expected to compete for resources. 
I don't think PvP would work in a bullet-hell mmo, so the way that they compete will have to be through pve.
Some ways that different guilds can compete with each other:
- Players will able to drag enemies onto other safe hubs, or maybe even onto other players who're mining for resources or something.
- There will be world bosses that roam around the map, and they have certain phases where if you damage them then they get enraged and become extremely dangerous (similar to O3's counter phase or Dammah's counter phase in RotMG).
	- You could make it so that the world bosses drop a portal to a dimension that has a harder version of that boss, but that version of the boss requires an extremely coordinated group and can be fucked over by one person if they decide to troll the group.
		- You could mitigate this by making it so that players can't enter another guild's safe hub area unless invited, so a strategy for ensuring that players don't get trolled when entering these dungeons would be to lure the world bosses inside a safe hub.
- Guild safe hub territories could be defined by pillars that have to be maintained, and the only way to maintain them is to have someone physically walk to it and drop some resources in it. You could make it so that a guild's pillar can be overridden by another guild's pillar if the other pillar has more resources than the other pillar.
- Different guild safe hubs should be able to form alliances via connections that needs to be maintained. These connections will make it so that items and resources can be moved between the safe hubs.

# Tutorial overview (hook for when players first open the game)

When players first open up the game, they spawn into the world before it was destroyed with end game gear. A scripted death occurs (you could make this a challenge for sweats to last as long as possible) and players respawn into the world after it has been destroyed and then the actual tutorial occurs.
**==Players spawning in with end game gear will be the only hint that players get for how to build their characters==**.



# Look
![Frontier Wars: Online - Steam Next Fest Announcement Trailer #gamedev #mmorpg #gaming](<https://www.youtube.com/watch?v=KIOcdETv3rQ>)
# Scope creep stuff
There is an apocalypse epicentre that sends out magical pulses every day that warp the landscape. There are event bosses that spawn resources and alter the terrain.

Players will eventually be able to create their own safe hubs, however, the maintenance for these safe hubs will require cooperation. Non-player built safe hubs will have a maintenance tax.

The paths between safe-hubs are player-made and player-maintained. Players can die and respawn between safe hubs, but if they want to move gear and items in bulk, then they need to create and maintain paths. There should be incentives for creating and maintaining paths (maybe one safe hub is rich in a resource that another safe hub lacks).

By default, all bullets are able to destroy and pass through player-built structures. The only exception is the structures for the paths between safe-hubs. The paths between safe-hubs require special tiles that provide players with immunity to all damage. These tiles can be used later on for a huge open world boss battles where players can set up a defensive line.

