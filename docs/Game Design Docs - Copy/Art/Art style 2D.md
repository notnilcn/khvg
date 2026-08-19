# Overview
The main look of the game is low-poly 3D passed through a pixel art shader (see [[Art style 3D]]). But the underlying game logic is 2D anyway (top-down bullet hell, RotMG-style), so I'm also going to support a full 2D sprite version. This doc is about what that version looks like.

##### Look:
2D sprites, top-down, similar to Realm of the Mad God but moodier and more colourful. If RotMG's sprites are a saturday morning cartoon, I want mine to be the same cartoon but at night with glowy mushrooms everywhere. Hollow Knight vibes crammed into flat little guys.

##### Why does a 2D version even exist?
A few reasons, roughly in order of how much I actually care about them:
- **RotMG veterans.** The core gameplay is a love letter to RotMG. A huge chunk of the people who'd actually play this game have 5000 hours of dodging white bullets in a 2D flash game (RIP flash, pour one out). If those people try the game and the 3D look feels weird to them, they can flip a toggle and get the version their brain already parses at 60hz. I want them to have zero excuses.
- **Low-end machines.** The 3D version isn't going to be Crysis or anything, but a pixel shader over 3D is still heavier than blitting sprites. Potatoes should be able to play. RotMG ran on flash in a school library computer, and I respect that legacy.
- **Modding / custom content later.** Sprites are infinitely easier for a community to make than 3D models + animations. If the game ever gets modding support, 2D assets are the low-friction entry point. This is speculative, idk if modding will ever actually happen, but it's a nice bonus.
- **Honestly?** Some people just like 2D more. Fair enough.

# THE RULE
**Gameplay logic is identical between the two versions. Only rendering differs.**

This is non-negotiable and it's the reason this whole thing is feasible at all. The game is already 2D under the hood - positions, hitboxes, bullets, collisions, all of it. The 3D version is a 2D simulation wearing a costume. So the 2D version is just... the simulation wearing a cheaper costume.

Consequences of the rule:
- No mechanics that only work in 3D. No "you can only dodge this bullet by rotating the camera" bullshit. Camera rotation in the 3D version has to be purely cosmetic comfort, never information advantage.
- Both versions see the same bullet at the same position at the same time. A 3D player and a 2D player in the same dungeon are playing the same game. Same hitboxes, same telegraphs, same everything.
- If a fight is unreadable in the 2D version, the fight is badly designed, period. The 2D version is the readability floor because it's the harshest test - no fancy depth cues or lighting to save you, just sprites and contrast.

# Sprite style direction
##### Resolution / pixel size
Somewhere in the 16x16 to 32x32 range for characters. RotMG is 8x8 with 16x16 for big stuff and honestly 8x8 is too few pixels for the amount of detail I want (Hollow Knight moodiness needs more than 64 pixels). Leaning toward 24x24 or 32x32 base for humanoids. Bosses obviously bigger. Tiles probably 32x32.

The important thing is the pixel density has to be CONSISTENT. Nothing looks shittier than a pixel art game where different assets are at different scales and everything's been rescaled and the pixels are all uneven. One pixel size across all sprites, no exceptions, no rotating sprites unless I commit to redrawing frames.

Also worth noting: the 2D version should NOT just be the 3D version's pixel-art-shader output screenshotted from directly overhead. That'd look muddy as hell. 2D sprites get their own style pass. See the asset pipeline section.

##### Palette rules
Same palette philosophy as the 3D version: pull from the Hollow Knight areas (see [[Hollow Knight References]]). City of Tears blues, Queen's Garden greens, Fungal Wastes glowy-yellows, Crystal Peak pinks/purples, Fog Canyon teal. Grim-hope dark fairytale - dark backgrounds, saturated glowy accents, NOT grey-brown apocalypse mush.

Rules of thumb:
- Environments sit in the dark/desaturated end of their area's palette. They're the stage, not the actor.
- Characters/enemies get one step more saturation than the ground under them so they pop.
- Bullets get maximum saturation and ideally a glow. See below.
- Each biome should be identifiable from its palette alone, the same way you can identify Queen's Garden from a colour swatch. If two biomes look the same in a 2D screenshot, one of them needs to change.

##### Bullet readability is god
This is a bullet hell. The single most important visual element in the entire game is "the thing that's going to kill you." Everything else - how pretty the trees are, how cool the boss looks - is secondary to being able to parse 80 projectiles on screen in half a second.

Hard rules:
- Every bullet type gets an unambiguous shape AND colour. Never rely on colour alone (colourblind players exist), never rely on shape alone (shapes get lost at small sizes).
- Bullets always render above everything except UI and telegraph overlays. Always. I don't care if a tree is "in front" of it.
- Enemy bullets and player bullets are visually distinct at a glance - different colour families, different shapes. RotMG does this pretty well and I'm stealing it.
- High-damage / armor-piercing / status bullets need their own visual language on top of the base bullet look. The player should know "oh that purple swirl means confused" without having ever been hit by it. (They'll learn by being hit by it, but at least they'll learn.)
- When in doubt, make the bullet bigger and brighter. Ugly-and-readable beats pretty-and-I-died-and-couldn't-see-why. Perma-death plus unfair-feeling deaths equals refund.

##### Keeping the Hollow Knight moodiness in flat sprites
This is the hard part. Hollow Knight gets its mood from painted backgrounds, parallax layers, fog, and lighting - all things a top-down 2D sprite game doesn't get for free. Some thoughts:
- **Vignette + darkness at screen edges.** Cheap, effective. The world beyond your immediate area should feel like it fades into dark.
- **Glowy shit everywhere.** Bioluminescent mushrooms, floating motes, glowing crystals. Eldest Souls' purple vines but MORE. Small glowy dots are basically free in terms of sprite budget and they do 80% of the "dark fairytale" heavy lifting.
- **Parallax-ish layering.** Even top-down games can fake depth - ground layer, ground detail layer, object layer, canopy/overhang layer above the player. Walking under a tree canopy that darkens the screen a bit does wonders.
- **Ambient particles.** Rain in City of Tears areas, spores in Fungal Wastes areas, drifting petals in Queen's Garden areas. Idle animation for the world itself.
- **Colour grading per biome.** A subtle fullscreen tint per biome keeps palettes cohesive without me having to hand-paint every tile to match.

# Translating 3D assets to 2D
The plan, roughly:
- **Characters/enemies:** render the low-poly 3D model from the top-down angle (and the 8-ish rotation frames needed), apply the pixel shader, downscale to sprite size, then hand-touch the result. Raw renders are a starting point, not the final sprite - they always need cleanup, and honestly for hero characters I might just hand-draw from scratch using the 3D model as reference. Idk yet. The render-to-sprite pipeline is the pragmatic choice for the long tail of enemies (there's going to be a LOT of enemies, it's an MMO), hand-drawn is the choice for anything the player stares at constantly.
- **Tiles/environment:** probably hand-drawn or heavily post-processed renders. Tiles need to tile (duh) and renders straight from 3D almost never tile cleanly.
- **The two versions share source models where possible**, so a new enemy means making one 3D model and getting both versions out of it. If the pipeline works, the 2D version costs maybe 20% extra per asset instead of 100%. If it doesn't work, the 2D version quietly becomes "only for the most important content" and I'll deal with that then.

##### Animation
2D sprite animation is expensive as hell per frame, so:
- Keep frame counts low and lean on it as a style - 3-4 frame walk cycles are charming if you commit.
- Procedural/squash-and-stretch tweening on top of sprite frames for juice (the 3D version does procedural animation stuff anyway, some of that math transfers).
- Attack telegraphs and wind-ups get the most frames. Players read intent from animation; that's where the frames matter. Idle loops can be 2 frames and nobody will care.
- RotMG gets away with like 2-frame everything and people love it. The bar is low. I intend to clear it, but it's low.

# UI considerations (shared between both versions)
The UI should be ONE ui, not two. Same layout, same fonts, same icons, same everything - it just sits on top of whichever renderer is active. Two whole UIs to maintain is a great way to make sure one of them is always broken.

- UI is resolution-independent and pixel-perfect at integer scales. Non-integer scaling of pixel art UI makes me want to die.
- Chat, health/mana, minimap, inventory: identical across versions. An inventory icon is a 2D sprite anyway even in the 3D version.
- Damage numbers and floating text need to be readable in both - big, bold, high contrast, get out of the way fast.
- The renderer toggle itself should be a settings option that doesn't require a restart if I can manage it. Hot-swapping renderers live is a fun engineering problem. It might also be a nightmare. Probably a nightmare. Maybe restart-to-apply is fine.

# Open questions / stuff I'm not sure about
- Is the 2D version a first-class citizen at launch or a "community request" feature that lands later? Exec summary says "if the community wants it" - so probably 3D first, 2D once someone asks. But the readability-floor rule means I kind of have to design as if 2D exists from day one, even if the renderer ships later.
- 8-direction sprites or 4? 8 looks better, 4 is half the work. RotMG is 4. Leaning 8 for player characters, 4 for random mob #437.
- Do I support an actual RotMG-style 8x8 "retro" sprite set as a joke/nostalgia option? That'd be funny. It's also a whole extra asset set, so... no. Unless it's really easy. It's not really easy.
- How do big multi-tile bosses work in 2D? One huge sprite, or a segmented thing? Huge sprites get blurry when scaled; segmented is fiddly. Idk.
- The hex grid - do tiles in the 2D version draw hex outlines, or do I hide the grid entirely and let it be implied? RotMG hides its grid (well, it's square, but whatever). Probably hide it, show on hover/ability aim.
- Do I have the skill/time to actually hand-draw good sprites? Unclear! The render pipeline existing as a crutch makes me feel better about this.
