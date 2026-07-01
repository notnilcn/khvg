These are ordered in terms of of priority.
# **0. [Foundations](<Foundations.md>)**
- [x] [Spacetimedb](<Foundations.md#Spacetimedb>)
	- [x] [World Grid](<Foundations.md#World Grid>)
		- The are going to be 2 coordinate systems:
			- [x] Continuous coordinate system 
				- This is for physics.
			- [x] Hex grid chunks
				- This is just visual. This coordinate system is for player buildings.
				- https://kaylousberg.itch.io/kaykit-medieval-hexagon
		- The map is going to be a giant torus rather than a giant square/sphere because apparently implementing it as a torus is mathematically easier.
	- [ ] Syncing
		- [x] Player position sync
		- [x] Enemy position sync
		- [x] Enemy bullet sync
		- [ ] Player bullet sync
- [x] Translation layer between SpacetimeDB and blastbullets2d
	- [x] Enemy Bullets system
		- These will be parametric bullets
		- Use the blastbullets2d plugin.
			- Just make the bullets 2d sprites.
		- (deferred) Maybe allow player clients to have the player-client-simulated enemies spawn their own local bullets as well. It'll still be server authoritative, you'd just have the client-spawned bullets lerp to the server-spawned bullets if there's a difference. Maybe have like an option in the settings to enable/disable this. You'd have to put the player-client-simulated-bullets on a different collision layer and collision mask though, since incorrect local bullets might incorrectly collide with other players, which will stuff up the probabilistic swarm anti-cheat.
	- [ ] Player bullets system
		- These won't actually be bullets.
			- Player clients will mostly have full authority over which enemies that the player is landing a hit on. Yes, I meant what I said. Player clients won't have any authority over the damage dealt, but they will have full authority over which enemies that the player has landed a hit on.
		- You should sync firing angles.
			- This is so that players can see where other players are shooting.
			- This is also so that the elevated client can audit a player's firing angle against the enemies that the player has landed a hit on.
				- Hacked clients will probably be able to reverse engineer the proper angles and send the correct firing angles to get past these audits. If this happens, you might as well disable this audit. It's fine though, since we're already implementing a forgiving aim assist.

# **1. Elevated client, probabilistic swarm anti-cheat, and multiplayer authority**
- The elevated client is just a headless version of the player clients, and it acts as the source of truth.
- Probabilistic anti-cheat is just an anti-cheat that only checks players that have a higher cheater score.
- Swarm anti-cheat is just an anti-cheat that relies on non-hacking players to check whether the players around them are cheating. E.g. if a hacker is no-clipping through walls, then player clients will be able to detect that and will automatically tell the server that it thinks that hacker is hacking.
	- Any player who's above a certain cheater score gets audited (the server starts logging their actions in the transaction log) and if they're found to be cheating, then they're banned.
	- If they're found to not be cheating, then all of the players who reported that that player was cheating loses some reputation score.
	- **==This model is susceptible to groups of players targeting a single player to get them banned. As in, if a group of players with hacked clients all target a single player and they all say that that player was hit by a bunch of bullets when in actuality that player wasn't, then the server won't be able to deal with that live. This is why players shouldn't be banned immediately==**.
#### Elevated Client
- [ ] Handles enemy spawning.
- [x] Simulates enemy pathing and sends snapshots to players clients for them to sync.
	- Enemy pathing is going to be deterministic, so player clients will also simulate enemy pathing locally. Enemies on player clients will snap to the snapshots that the elevated client sends out.
	- Deterministic also means pseudo-random btw. Snapping to the snapshots is only there because different machines calculate the same calculation differently, which results in floating point drift and stuff.
	- You could probably set the snapshot timer to something like one every 0.3 seconds or something.
- [ ] Calculations for enemies getting damaged
	- These will be batched to a larger extent than the calculations for players getting damaged. This is because the server doesn't need to tell the clients every little bit of damage that an enemy takes.
	- The way that damage is calculated kind of doesn't even allow for non-batched updates. The only thing that the player clients are send to the server is which enemy they're targeting. To calculate the damage, the elevated client (or a scheduled reducer) will have to read all of the players who're targeting an enemy, read the weapon stats and whatnot (you could probably pre-compute this part by having an on_equip reducer that calculates the player's dps appends it to a private table. Similar idea with an on_buff reducer.), and then calculate the damage from there.
		- It might be better to let it be handled by the elevated client because you'll want to make it instant for lower-health enemies. If it's batched for lower-health enemies and players are able to one-shot them, then there'll be a delay between the enemies dying on the player clients and the enemies dying on the server. You could probably get over this by allowing players to simulate enemy deaths locally, but this could result in some issues like enemies popping back into existence after the client simulated that the enemy died.
			- (deferred) You could probably make this a togglable option in the settings.
- [ ] Calculations for players getting damaged
	- These will be batched to a lesser extent than the calculations for enemies damaged. Again, the server doesn't need to tell the clients about every little bit of damage that a player takes - if two bullets hit a player at the same time (or within like 0.1 seconds of each other) players won't realistically be able to tell if those calculations were separate or batched.
- [ ] (deferred) Maybe rewrite the elevated client in Rust later on. But since it'll literally have the same logic as the player clients, there's no point in rewriting it in Rust now since you're already writing the player client in C#.
	- If you're going to rewrite the elevated client in Rust then you might as well rewrite the player client in Rust as well. They're literally going to be the same, the only difference is that the elevated client is going to be headless and it's going to have permission to call admin reducers.
		- Maybe don't rewrite it in Rust.
#### **1.1 Probabilistic swarm anti-cheat and multiplayer authority**
The things that player clients have full authority over are:
- [ ] Player clients have full authority over when the player was hit by a bullet.
	- [ ] Hackers are detected via swarm anti-cheat. Other player clients also simulate those collisions and are able to tell the server whether that player was hit by that bullet. This contributes to a cheater score.
	- Since this game is PvE, cheaters don't need to be banned immediately.
	- The server should be able to perform the same hit validation collisions from the transaction log, however, this should only be done when players appeal a death or appeal a ban.
- [ ] Player clients have full authority over the enemies that they landed a hit on.
	- [ ] They do not have authority on the damage dealt.
	- [ ] (deferred) Since firing angles are already synced, you can implement the swarm anti cheat and have the player clients check each other to see if the players around them are cheating.
	- Hacked clients will probably be able to reverse engineer the proper angles and send the correct firing angles to get past these audits. It's fine though, since we're already implementing a forgiving aim assist.
- [ ] Player clients will have full authority over where the player moved (within reason - as in, no teleporting and their movespeed needs to match their character stats).
	- If a player is alone and is in a dungeon or something (somewhere where no-clipping is important) then they will be audited and the elevated-client will calculate physics and stuff. Otherwise, **==the server will trust the player client==** since no clipping is kinda obvious and I expect no clippers to get reported.
- [ ] (deferred) Each player has a cheater probability stat thing
	- [ ] This cheater probability stat thing determines the player's audit rate.
		- High cheater probability players will have a higher audit rate, whereas low cheater probability players will have a lower audit rate.
#### **1.2 Transaction(deferred) Cheat detectione elevated client should be able to audit a player's transaction logs. Spacetimedb keeps a transacis able to tion log of evey reducer call, so it should be possible to reconstruct and replay all player inputs as well as all bullet spawns to see if a player is cheating.
- Maybe you could vectorize this and turn it into a bunch of matrix operations and run it on a gpu rather than a cpu.
#### **1.3 Things that increase/decrease the cheater score
- Increase:
	 - [ ] A player's total gold value has a spike increase.
	 - [ ] A player never drops a target when reporting that they're landing hits on enemies
		- This is less concerning since you already have forgiving aim assist so maybe only implement this if it becomes a huge problem and people start complaining about auto-aim.
	- [ ] A player never gets hit
		- You could probably design certain bosses so that certain shots are impossible to dodge. If a player dodges these bullets then their cheater probability stat will be 1. Don't immediately ban them though since they'll probably figure it out.
	- [ ] A player has a lot of auto-nexuses
		- Maybe don't ban this since instant-nexusing will be gated behind a paid consumable. Maybe just do some price gouging and make it more expensive for cheaters.
- Decrease:
	- [ ] The player consistently passes their audits
	- [ ] The player has purchased something with real money
# **2. Controls**
- Camera rotation.
	- Default left click right click behaviour is camera rotation
###### \[Shelved\] Other controls
- [ ] Forgiving aim assist
	- https://www.youtube.com/watch?v=yGci-Lb87zs
- [ ] Swap slot configurations
	- Just a more official version of how players in realm of the mad god regularly swap between equipment in the middle of a fight to maximize dps.
	- I'm not sure what the controls for this should be. Maybe something similar to league of legends' ping wheel?
- [ ] More in depth controls.
	- [ ] shift + left click toggles things
	- [ ] shift + right click toggles things
	- [ ] shift + middle button toggles things
	- [ ] shift + scroll wheel changes character speed
	- [ ] alt + scroll wheel changes camera rotation speed
		- this could be useful for leucoryx-style bosses that are easier if you do a lot of camera rotation.
	- [ ] capslock snaps the camera towards the mouse so that the mouse is north. Holding down capslock while moving the mouse is an alternative way to rotate the camera.
		- [ ] alt + capslock snaps the camera towards the mouse so that the mouse is either east or west, depending on whichever is closest.
		- **==you could probably design some dungeons or bosses to be extremely easy if players learned this control scheme==**
			- Maybe a boss that teleports a lot and requires you to change your rotation.
	- [ ] shift + space bar does some whacky ability stuff. I'm thinking for the trickster ripoff class it toggles a [ghost mode](<https://youtu.be/To5rOl6pn08?si=HfqyH_uChXK7tFZL&t=102>) thing that you control with your mouse and lets you control where you want to teleport. If they're shooting during this ghost mode then they'll lock onto the closest enemy that they're currently shooting. Maybe for other classes it could be like an mid-to-end game ability unlock thing (e.g. for the wizard ripoff class it'd be specifying different variants of the same spell). But for the trickster ripoff class it should be available immediately.
		- probably defer this
# **3. Dungeon Bosses**
- In the beginning the game is just going to be a bunch of interesting bosses. [Arena of the Mad King](<https://naniloit.itch.io/aotmk>) is an example. Same with [this game](<https://www.youtube.com/watch?v=yGci-Lb87zs>)
# **4. Graphics**
- [ ] Free 3d assets
	- https://kaylousberg.itch.io/kaykit-medieval-hexagon
	- https://kaylousberg.itch.io/
- [ ] Pixel art shader
	- https://www.youtube.com/watch?v=7wwE5FLZceY
	- https://www.youtube.com/watch?v=dHbqsr-KjOg
	- 
I have no idea what I want the art direction to be.
It'll probably have to be 3d since camera rotation is going to be a huge part of the game.
Maybe low-poly 3d pass through a pixel art shader since I'm lazy and don't want to spend a lot of time on making it look good (the pixel art shader will make it look intentional rather than lazy).
https://kaylousberg.itch.io/kaykit-medieval-hexagon
https://kaylousberg.itch.io/
I think I want the game to be purple.
# **5. Lobby**
- Server list
	- Players will be able to switch to different servers, similar to RotMG.
	- Items in a player's vault will be available in the [lore-based safe hub](<99 (outdated) Game Design Document.md#**There are 3 different lore-based safe hubs**>) that they chose, or the default [outcasted magician's safe hub](<99 (outdated) Game Design Document.md#**01 Safe hub that belongs to the outcasted magician dude**>) vault if they haven't chosen one yet. Items in a guild vault aren't transferrable between servers.
- Character selection
	- Player slots will be purchasable like in RotMG. Players will be able to have 2 by default.
	- There will be a character creator thing (COMPLETELY SHELVED UNTIL WE HAVE A MINIMUM VIABLE PRODUCT).
		- Just be lazy and encode a bunch of different nose/hair/eye/head/eyebrow/mouth/skull shapes continuously and let the player play around with the sliders. Make it too customizable.
			- I feel like there should be a mathematical way to calculate the appropriate texture maps to allow players to colour their characters freely as well.
- Settings
# **97. Spacetimedb stuff (deferred)**
## Sharding
![Rust-powered SpacetimeDB is 1000x Faster? Founder Explains](<https://youtu.be/qfKBv3A0CVs?si=la7f0RChUlYZTRJB&t=2002>)

# **98. World (deferred)**
- Biomes
	- Ocean
	- Desert
	- Apocalypse epicenter (maybe multiple?) where enemies get stronger the closer you get to the center.
	- The apocalypse epicenter sends out pulses every month (or every day) that changes the world's landscape.
###### \[Shelved\] Other stuff to do with the world
- World bosses that roam around the world and change the landscape.
	- Creates valleys or fissures that cuts off certain regions
	- Drops minerals or materials
- Character selection
	- Player slots will be purchasable like in RotMG. Players will be able to have 2 by default.
	- There will be a character creator thing (COMPLETELY SHELVED UNTIL WE HAVE A MINIMUM VIABLE PRODUCT).
		- Just be lazy and encode a bunch of different nose/hair/eye/I don't know what sharding. The github repo for bitcraft is open source though (bitcraftpublic folder) so have a look in that.head/eyebrow/mouth/skull shapes continuously and let the player play around with the sliders. Make it too customizable.
			- I feel like there should be a mathematical way to calculate the appropriate texture maps to allow players to colour their characters freely as well.

# **99. Weapons animation (deferred)**
![Explaining a complex (yet modular) weapon system I made!](<https://www.youtube.com/watch?v=a5GJcgdSEQo>)

The weapons in this game are going to be magical so you probably don't need to implement the above.

