- [x] Remove the Godot multiplayer API
- [ ] Movement system
	- The dead reckoning interpolation stuff needs to take into account camera rotation and camera rotation speed.
- [ ] Enemy bullets spawns should be entirely parametric
	- This is specifically for those "homing bullets" that you left room for. Those bullets will still be homing, however, they will also have a "target player" column that they home in on. It will be extremely similar to how enemy pathing is calculated, except there won't be any need for snapshotting since enemy bullets won't change targets after they've been spawned.
	- You could probably optimize this by batching the "target player" thing across multiple bullets. As in, when a boss spawns a bunch of homing bullets, they all home in on the player that the boss is aggroed on.
	- Actually, you could probably optimize bullet spawns in general by making it so that the bulletspawns table accepts a list of coordinates for the coordinates column. Each coordinate spawns a bullet. You'd also have to have the target coordinates accept a list of target coordinates as well. 
- [ ] 




