# Create a mood board

All of these tasks will be done in order. The point of this whole thing is to end up with one board (or a small set of boards) that I can point at and say "the game looks like THIS". Not a pinterest graveyard of 500 unsorted screenshots - an actual usable reference with rules attached to it.

Reminder of what we're going for: low poly 3D through a pixel art shader (t3ssel8r look), realistic-ish humanoid proportions (BitCraft-ish), grim-hope dark fairytale, post-catastrophe patchwork world that's colourful and lived-in, NOT uniformly barren and depressing. Hollow Knight is the vibe north star. Also remember there's a planned 2D sprite version that should look RotMG-ish, so 2D references aren't wasted work.

## Task 1: Understand the game
- [ ] Take screenshots and make notes out of the following games. Don't just screenshot pretty stuff - capture the specific thing listed for each game, and write one line about WHY it works.
	- [ ] Realm of the Mad God
		- Bullet patterns and bullet readability. How do they keep 50 players + 200 bullets on screen readable? Note the bullet colours vs the floor colours.
		- Enemy silhouettes - you can tell what an enemy does from its shape alone. Steal that.
		- This is the core gameplay reference, so also grab HUD/UI shots for the eventual 2D version.
	- [ ] Alabaster Dawn
		- Core gameplay reference. Combat feel, enemy telegraphs, how flashy abilities look without destroying readability.
	- [ ] Runeward Online
		- Core gameplay reference. How an MMO frames a top-down-ish camera and keeps player characters distinct in a crowd.
	- [ ] Pixel Quest
		- Core gameplay reference. Pixel art readability at small sprite sizes.
	- [ ] BitCraft Online
		- Proportions. Characters are realistic-ish, not chibi, not exaggerated. Also grab the hex grid shots and the general "calm lived-in world" feel.
	- [ ] Hollow Knight
		- THE vibe reference. Environment mood: dark and desolate but somehow warm and never edgy. City of Tears, Greenpath, Queen's Gardens, Fungal Wastes.
		- Notice how every area has its own tight palette. Screenshot one room per area and pull the palette from it.
	- [ ] Hollow Knight: Silksong
		- Same as above but note how it's MORE colourful than HK while still being moody. That's the direction I want.
	- [ ] Titan Souls
		- Boss silhouettes and boss arenas. Huge readable bosses in minimal environments. My game is definitely going to have bosses like these.
	- [ ] Hyper Light Drifter
		- Colour. Neon-bright palettes over ruined worlds without it feeling like a rave. Post-apocalypse that isn't brown.
	- [ ] Eldest Souls
		- The desolate feel and the purple vine environments. I like it but it's not colourful enough for me - use it as a "push it further" baseline.
	- [ ] DROVA - Forsaken Kin
		- Grim fantasy world that still feels lived-in and grounded. Note the muted-but-not-boring palette.
	- [ ] THERE IS NO LIGHT
		- Post apocalyptic environments. Too gory and dark overall - NOT the target - but some of the environments are exactly the mood. Cherry-pick carefully.
	- [ ] t3ssel8r devlog videos
		- Not a game but the single most important LOOK reference. Pause and screenshot frames constantly. This is literally what the game will look like.
- [ ] Vibe-only references (movies/games for feel, not mechanics - a handful of shots each is fine):
	- [ ] Project Hail Mary + The Martian (movies) - grim-hope. Everything is broken and the characters just... fix it. That energy.
	- [ ] Avatar (movie) - saturated alien biome colour
	- [ ] Spirit (horse movie) - wide painted landscapes
	- [ ] Monster Hunter (movie) - scale of monsters vs people
	- [ ] Clash of the Titans (movie) - mythic fantasy framing
	- [ ] Minecraft - how much mood you can get out of extremely simple shapes + lighting
	- [ ] Rayman Legends - playful colour (a little goes a long way here)
	- [ ] Expedition 33 - painterly dark fairytale
	- [ ] Outer Wilds - hope-core. Lonely but warm.
	- [ ] Splaty - honestly not sure why past me wrote this down, grab a couple shots and figure out later

## Task 2: Collect and organize the references
- [ ] Make a folder per game/source under the mood board references folder. Don't dump everything into one folder - future me will not sort it, ever.
- [ ] File naming: `game - what it shows.png` (e.g. `hollow knight - city of tears palette.png`). One glance should tell me why the shot exists.
- [ ] Every screenshot gets a one-line note somewhere (in the filename or a notes.md per folder). If I can't say why I saved it, delete it.
- [ ] Tag each shot loosely: `palette`, `boss`, `bullets`, `environment`, `character`, `ui`, `vibe`.
- [ ] Cap it at roughly 10-20 shots per game for the main references. Hollow Knight and t3ssel8r can go over. Quality over hoarding.
- [ ] Keep a separate `2d version` folder for anything that only applies to the RotMG-like sprite version so it doesn't pollute the main board.

## Task 3: Assemble the board
- [ ] One main board, grouped by section: Environments / Bosses / Bullets + Combat / Characters / Colour / Vibe.
- [ ] Environments section is organized by biome mood, not by game. The board should read like OUR world, not like a collage of other games.
- [ ] Pull palette swatches from the best environment shots (5-8 colours each) and line them up along the side of the board. Each swatch group gets a label of where it came from.
- [ ] Make a DO column and a DON'T column:
	- DO: colourful, lived-in, readable, dark fairytale, grim-hope
	- DON'T: gory (THERE IS NO LIGHT overall), uniformly barren/grey-brown apocalypse, chibi proportions, unreadable visual noise
	- Put 1-2 example images in the DON'T column too - "we are NOT this" is just as useful as "we are this".
- [ ] The t3ssel8r frames get pride of place at the top - that's the target render look, everything else serves it.
- [ ] Step back and delete anything that doesn't earn its spot. If the board feels cluttered, it fails.

## Task 4: Distill the board into rules
- [ ] Write the actual rules doc from the board. Draft rules to confirm or kill:
	- [ ] Bullet readability: bullets must contrast with every biome floor palette. Bright, saturated, single-colour bullets with clear outlines. No bullet may share a hue family with the floor beneath it.
	- [ ] Enemy bullets vs player bullets get distinct hue families so you never confuse them mid-fight.
	- [ ] Every biome gets its own tight palette (Hollow Knight rule: one glance = one biome). Pick the palette BEFORE building the biome, not after.
	- [ ] Post-apocalypse ≠ barren. Every biome needs signs of life: glowing plants, patchwork seams where worlds merged, reclaimed ruins.
	- [ ] Grim-hope balance: dark environments always get a warm or bright counterpoint somewhere in frame (lantern light, glowing flora, sky break).
	- [ ] Bosses read as silhouettes first. If you can't tell what it is from the shadow, redesign it (Titan Souls rule).
	- [ ] Realistic-ish proportions for characters (BitCraft rule). No chibi, no exaggeration.
- [ ] Note next to each rule which reference image(s) justify it, so future me can't relitigate settled decisions.
- [ ] Anything still undecided (e.g. what the actual catastrophe event looks like visually) gets parked in an "open questions" section instead of being silently ignored.
