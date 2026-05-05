# Enemy Bullets system

These will be parametric bullets

# Player bullets system
These won't actually be bullets

# Spacetimedb
## World grid
### Torus grid overview
I want to make the map a torus because I don't like maps that don't wrap around. Apparently it's mathematically impossible to discretise a sphere with hexagons so it has to be a torus. The images below show how the tile map wraps around.
![[Pasted image 20260425115208.png]]
![[Pasted image 20260426070151.png]]
Light blue hexagons are the outer ring of the torus (outer diameter is number of light blue hexagons * 1.5).
Dark blue hexagons are the inner ring of the torus (inner diameter is number of dark blue hexagons * 1.5, however, due to the geometry of it all the number of dark blue hexagons must equal the number of light blue hexagons. Use the green hexagons as a better indication of inner diameter).
Green hexagons are the thickness of the torus.
Idk what the red hexagons are. Perhaps they are gay.
**==The number of light blue hexagons must be greater than or equal to 6 for it to be a somewhat 3d-imaginable torus==**.



#### ![Torus world lore](<Lore.md#Torus world>)
If you come up with any lore, write it in the linked md above (click the top .


## Player position sync

