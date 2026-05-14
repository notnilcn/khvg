These are ordered in terms of of priority.
# **0. [Foundations](<Foundations.md>)**
- [ ] [Spacetimedb](<Foundations.md#Spacetimedb>)
	- [ ] [World Grid](<Foundations.md#World Grid>)
		- The are going to be 2 coordinate systems:
			- [x] Continuous coordinate system 
				- This is for physics.
			- [ ] Hex grid chunks
				- This is just visual. This coordinate system is for player buildings.
				- https://kaylousberg.itch.io/kaykit-medieval-hexagon
		- The map is going to be a giant torus rather than a giant square/sphere because apparently implementing it as a torus is mathematically easier.
	- [ ] Syncing
		- [x] Player position sync
		- [x] Enemy position sync
		- [ ] Enemy bullet sync
		- [ ] Player bullet sync
- [ ] Translation layer between SpacetimeDB and blastbullets2d
	- [ ] Enemy Bullets system
		- These will be parametric bullets
		- Use the blastbullets2d plugin.
			- Just make the bullets 2d sprites.
	- [ ] Player bullets system
		- These won't actually be bullets.
			- Player clients will mostly have full authority over which enemies that the player is landing a hit on. Yes, I meant what I said. Player clients won't have any authority over the damage dealt, but they will have full authority over which enemies that the player has landed a hit on.
		- You should sync firing angles.
			- This is so that players can see where other players are shooting.
			- This is also so that the elevated client can audit a player's firing angle against the enemies that the player has landed a hit on.
				- Hacked clients will probably be able to reverse engineer the proper angles and send the correct firing angles to get past these audits. If this happens, you might as well disable this audit. It's fine though, since we're already implementing a forgiving aim assist.

# **1. Elevated client, probabilistic anti-cheat, and multiplayer authority**
- [ ] Handles enemy spawning.
- [ ] Simulates enemy pathing and sends snapshots to players clients for them to sync.
	- Enemy pathing is going to be deterministic, so player clients will also simulate enemy pathing locally. Enemies on player clients will snap to the snapshots that the elevated client sends out.
	- Deterministic also means pseudo-random btw. Snapping to the snapshots is only there because different machines calculate the same calculation differently, which results in floating point drift and stuff.
	- You could probably set the snapshot timer to something like one every 0.3 seconds or something.
- [ ] Calculations for enemies getting damaged
	- These will be batched to a larger extent than the calculations for players getting damaged. This is because the server doesn't need to tell the clients every little bit of damage that an enemy takes - if two bullets hit an enemy at the same time (or within like 0.1 seconds of each other) players won't realistically be able to tell if those calculations were separate or batched.
- [ ] Calculations for players getting damaged
	- These will be batched to a lesser extent than the calculations for enemies damaged. Again, the server doesn't need to tell the clients about every little bit of damage that a player takes - if two bullets hit a player at the same time (or within like 0.1 seconds of each other) players won't realistically be able to tell if those calculations were separate or batched.
#### **1.1 (deferred) Probabilistic anti-cheat and multiplayer authority**
There won't be any client-side anti cheat. The way anti-cheating will be handled is by simulating things in the elevated client. Each player will have a cheater probability stat thing that effects their upload tick rate thing (download tick rates stay the same). Players that have a high cheater probability stat thing will have their upload audit rate set higher, whereas players that have a low cheater probability stat thing will have their upload audit rate set lower. These words don't really make sense, but they will make more sense when I elaborate.
- [ ] Each player has a cheater probability stat thing
	- [ ] This cheater probability stat thing determines the player's audit rate.
		- High cheater probability players will have a higher audit rate, whereas low cheater probability players will have a lower audit rate.
- [ ] Player clients tell the server when the player was hit by a bullet.
	- [ ] If a player is being audited, then the elevated client calculates whether the player was hit its nearby bullets. **==If the player isn't being audited, then the server trusts the player client==**.
- [ ] Player clients tell the server which enemies that the player has landed a hit on.
	- [ ] If a player is being audited, then the elevated client calculates whether the player landed hits on those enemies. **==If the player isn't being audited, then the server trusts the player client==**.
	- Hacked clients will probably be able to reverse engineer the proper angles and send the correct firing angles to get past these audits. If this happens, you might as well disable this audit. It's fine though, since we're already implementing a forgiving aim assist.
- [ ] Player clients tell the server where the player moved and stuff.
	- If a player is alone and is in a dungeon or something (somewhere where no-clipping is important) then they will be audited and the elevated-client will calculate physics and stuff. Otherwise, **==the server will trust the player client==** since no clipping is kinda obvious and I expect no clippers to get reported.
#### **1.2 Transaction(deferred) Cheat detectione elevated client should be able to audit a player's transaction logs. Spacetimedb keeps a transacis able to tion log of evey reducer call, so it should be possible to reconstruct and replay all player inputs as well as all bullet spawns to see if a player is cheating.
	- Maybe you could vectorize this and turn it into a bunch of matrix operations and run it on a gpu rather than a cpu. theo it inyou bought  batches every month toyourmake it?economical.
#### **1.3 Things that increase/decreas?
- [ ] **==Rather than having the elevated client do the validations, you could have the player clients do the validation. As in, player clients also send whether they think other players were hit by a bullet.==**
	- This has room for another exploit wherein players falsely report that another player was hit when they actually weren't.
	- It'd be pretty weird for people to make hacked clients for this, though, since you'd need to get a bunch of non-hacking players to download your hacked client as well. Like, you'd need to get like 5 or so hacked clients to follow a player that they want banned and have the hacked clients report that the player they want banned should've been hit when the player actually wasn'te a player's cheater probability stat:**
- Increase:
	 - [ ] A player's total gold value has a spike increase.
	 - [ ] A player has perfect accuracy when landing hits on enemies
		- This is less concerning since you already have forgiving aim assist so maybe only implement this if it becomes a huge problem and people start complaining about auto-aim.
	- [ ] A player never gets hit
		- You could probably design certain bosses so that certain shots are impossible to dodge. If a player dodges these bullets then their cheater probability stat will be 1. Don't immediately ban them though since they'll probably figure it out.
	- [ ] A player has a lot of auto-nexuses
		- Maybe don't ban this since instant-nexusing will be gated behind a paid consumable.
- Decrease:
	- [ ] The player consistently passes their audits
	- [ ] The player has purchased something with real money
	- [ ] Reports
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
	- Items in a player's vault will be available in the [lore-based safe hub](<Overviews.md#**There are 3 different lore-based safe hubs**>) that they chose, or the default [outcasted magician's safe hub](<Overviews.md#**01 Safe hub that belongs to the outcasted magician dude**>) vault if they haven't chosen one yet. Items in a guild vault aren't transferrable between servers.
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

