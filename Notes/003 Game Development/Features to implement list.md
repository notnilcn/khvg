These are ordered in terms of of priority.
# 0. [Foundations](<Foundations.md>)
- [ ] [Spacetimedb](<Foundations.md#Spacetimedb>)
	- [ ] [World Grid](<Foundations.md#World Grid>)
		- The are going to be 2 coordinate systems:
			- [x] Continuous coordinate system 
				- This is for physics.
			- [ ] Hex grid chunks
				- This is just visual. This coordinate system is for player buildings.
		- The map is going to be a giant torus rather than a giant square/sphere because apparently implementing it as a torus is mathematically easier.
	- [ ] Syncing
		- [x] Player position sync
		- [ ] Enemy position sync
		- [ ] Enemy bullet sync
		- [ ] Player bullet sync
- [ ] Enemy Bullets system
	- These will be parametric bullets
- [ ] Player bullets system
	- These won't actually be bullets

# 1. Physics
- [ ] Elevated client that reads spacetimedb, calculates physics, and writes the output to spacetimedb.
	- The game needs an elevated client that calculates physics because position sync uses dead reckoning.


# 1. Controls
- Camera rotation.
	- Default left click right click behaviour is camera rotation
###### \[Shelved\] Other controls
- Forgiving aim assist
	- https://www.youtube.com/watch?v=yGci-Lb87zs
- Customisable controls.
	- shift + left click toggles things
	- shift + right click toggles things
	- shift + middle button toggles things
	- shift + scroll wheel changes character speed
	- alt + scroll wheel changes camera rotation speed
		- this could be useful for leucoryx-style bosses that are easier if you do a lot of camera rotation.
	- capslock snaps the camera towards the mouse so that the mouse is north. Holding down capslock while moving the mouse is an alternative way to rotate the camera.
		- alt + capslock snaps the camera towards the mouse so that the mouse is either east or west, depending on whichever is closest.
		- **==you could probably design some dungeons or bosses to be extremely easy if players learned this control scheme==**
	- shift + space bar does some whacky ability stuff. I'm thinking for the trickster ripoff class it toggles a [ghost mode](<https://youtu.be/To5rOl6pn08?si=HfqyH_uChXK7tFZL&t=102>) thing that you control with your mouse and lets you control where you want to teleport. If they're shooting during this ghost mode then they'll lock onto the closest enemy that they're currently shooting. Maybe for other classes it could be like an mid-to-end game ability unlock thing (e.g. for the wizard ripoff class it'd be specifying different variants of the same spell). But for the trickster ripoff class it should be available immediately.

# 2. Dungeon Bosses
- In the beginning the game is just going to be a bunch of interesting bosses. [Arena of the Mad King](<https://naniloit.itch.io/aotmk>) is an example. Same with [this game](<https://www.youtube.com/watch?v=yGci-Lb87zs>)

# 4. Graphics
I have no idea what I want the art direction to be.
It'll probably have to be 3d since camera rotation is going to be a huge part of the game.
Maybe low-poly 3d pass through a pixel art shader since I'm lazy and don't want to spend a lot of time on making it look good (the pixel art shader will make it look intentional rather than lazy).
https://kaylousberg.itch.io/kaykit-medieval-hexagon
https://kaylousberg.itch.io/
I think I want the game to be purple.

# 5. World
- Probably just make it 3d.
- Apocalypse epicenter (maybe multiple?) where enemies get stronger the closer you get to the center.
	- The apocalypse epicenter sends out pulses every month (or every day) that changes the world's landscape.
###### \[Shelved\] Other stuff to do with the world
- World bosses that roam around the world and change the landscape.
	- Creates valleys or fissures that cuts off certain regions
	- Drops minerals or materials


