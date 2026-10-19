I am going to fork mindustry and work off of that and ditch everything I've done here.






The point of the mmo is to rotate all of the biome chunks so that the world is the original.
Make it so that different region servers (europe vs america) have share the same pool of biome chunks. As in, when america's chunk 0,0 rotates, it rotates from the same pool of chunks as europe's (meaning america and europe can't share the same biome chunks in the same spot).

# Mindustry port

## How bullets handle height

Just use collision layers. There's a maximum of 32 collision layers, but I'm not sure if that matters since you can just make it so that bullets that are within a certain terrain height spawn on the same collision layer.


## Hex grid belts

Issue: You can't place a straight line of belts along the pointy axis without accounting for non-integer distances.

Fix: Just make it a purely visual fix. Make the belts that zig zag along the pointy axis look straight by making them look like the blue line below when there are 3 belts placed in a vertical zig-zag:
![[Attachments/Pasted image 20261003113029.png]]
Use your imagination for how you'd handle all the special cases.


## Waves

I think the way waves are going to work is that there's going to be an item/building that attracts enemies.

In the beginning, this item/building will be used as like a vampire survivors type of thing.

RTS pvp will be in the game as like a game mode sort of? I'm not sure how to make that fit into the game in a way that makes sense game-design-wise.

## RTS PvP

Maybe make this like an emergent gameplay thing?

I like factorio's nuclear power.

## Inspiration

![](https://www.youtube.com/watch?v=EgaEnEu9Jew)

Pokemon unite control scheme for mobile games?
Maybe have like an aiming circle and a camera circle and ability circles.

brotato


## Enemy AI
Have the enemy phases and enemy ai run locally.
The server only syncs the phase changes and the targets.

Structure the content so that the targeting can be preloaded. As in, when an enemy wants to change targets, then it allows for like 1 second for the server to send who to target before calculating who to target locally. As in, there are no enemies that do things like "The enemy dashes to the centre of the room and then ***==immediately==*** calculates who to target based on whoever's nearest/did-most-damage" and instead do things like "The enemy dashes to the centre of the room and then charges for 1 second to wait for the server to send which player to target and then targets that player".

## World editor
Maybe have a dungeon or mountain or raid or something that lets players edit the world since you're already going to be implementing a world editor anyway. Maybe have things that lets players rotate a chunk's biome.

