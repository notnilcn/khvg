An alternative to the torus world is a honeycomb geode
https://blenderartists.org/t/geode-honeycomb/653434
https://en.wikipedia.org/wiki/User:Tomruen/Geodestic_sphere

# Torus world overview
I want to make the map a torus because I don't like maps that don't wrap around. Apparently it's mathematically impossible to discretise a sphere with hexagons so it has to be a torus.
https://mathematica.stackexchange.com/questions/39879/create-a-torus-with-a-hexagonal-mesh-for-3d-printing
The images below show how the tile map wraps around.
![[Pasted image 20260425115208.png]]
![[Pasted image 20260426070151 .png|720]]
Light blue hexagons are the outer ring of the torus (outer diameter is number of light blue hexagons * 1.5).
Dark blue hexagons are the inner ring of the torus (inner diameter is number of dark blue hexagons * 1.5, however, due to the geometry of it all the number of dark blue hexagons must equal the number of light blue hexagons. Use the green hexagons as a better indication of inner diameter).
Green hexagons are the thickness of the torus.
Idk what the red hexagons are. Perhaps they are gay.
==**The number of chunk columns needs to cleanly divide into the number of chunk rows**==. This is because the number of hexagons going up needs to match the number of hexagons going bottom right.

# Torus world logic
Before we can talk about the code, we need to talk about the chunking because it directly effects the world map
## Hex grid chunking and hex grid map
![[chunkradius7.png]]
Here are some amazing resources (you don't have to read them, but if you do then read them in order):
- https://www.redblobgames.com/grids/hexagons/
	- A downloaded copy of this is in [here](<Hexagonal Grids.html>).
- https://observablehq.com/@sanderevers/hexagon-tiling-of-an-hexagonal-grid
	- A downloaded copy of this is in [here](<47e466502e507073@1681.js>).
- https://observablehq.com/@sanderevers/hexmod-representation
	- A downloaded copy of this is in [here](<4ec02aa4a67ac76d@842.js>).

Chunks are going to be hexagons of hexagons. The way that this effects the world map is that the world map is now going to be a rhombus at the chunk-level, but not at the individual-hex-level.
The dimensions of it all are going to be extremely similar to bitcraft.
- The [outer radius](<Hexagonal Grids.html>) of an individual hex is roughly the size of 3 players standing dick to ass.
- The max number of hexagons that are visible in the radius when you fully zoom out is 16.

> [!info] The formula for calculating the number of hexagons that're in a chunk from a hex_radius is:
$3r^2+3r+1$ ^r-to-chunk

After a bunch of discussion, I have settled on making the chunk_hex_radius=5 and the aoi_chunk_radius=3 (the radius doesn't include the centre hex btw, so the chunk_hex_diameter is 11 and the aoi_chunk_diameter is )

Since the max number of hexagons that are visible in the radius when you fully zoom out is 16, the chunk radius should be 6 hexes, and the AoI radius should be 3 chunks. You can get away with making the chunk radius 5 hexes and the AoI radius 3 chunks, but it'll be tight. Just do a chunk radius of 6.

## Hex grid math
## Hex coordinates
Just go read the [web site](<Hexagonal Grids.html>).
You might think hexes can easily be represented as \[q, r\], but you would be wrong. There is in fact an arguably easier way to represent hex coordinates, and it is with 3 coordinates, \[x,y,z\], rather than 2. This is because when you orient a cube so that it's point corner is pointing towards you vertically, it makes a hexagon ::hmm::. Yes, indeed, this is an arguably easier way to represent hex coordinates, and you will agree with my by the end of this section - perhaps not wholeheartedly, but maybe begrudgingly.

The reason why reframing things this way makes things easier is because you can now stack a bunch of cubes together and view them the same way to get a plane in 3d space. This plane in 3d space allows you to use 3d math to calculate distance and shit. Calculating distance the other way is gay, which is kinda hot and makes my dick hard, which is why doing it this way is easier cus it doesn't make my dick hard and the opposite of hard is easy so yeah it's easy.
I told you you should've read the website.
### Converting from floating point to individual hex


### Converting from individual hex to chunk



# ![Torus world lore](<04 Lore.md#Torus world lore>)
If you come up with any lore, write it in the linked md above (click the top .


# Area of Interest notes
In the end game, players are going to be able to move around very quickly. They might be able to outpace chunk loading. If this happens, just make the AoI buffer larger depending on player move speed and teleport cooldown. The AoI buffer should only contain obstacles and terrain like water and lava.

