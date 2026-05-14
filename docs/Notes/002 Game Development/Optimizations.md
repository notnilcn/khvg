


# Position sync
#### Dead Reckoning
Rather than storing and syncing 

This will still be useful though cus it can be used to optimize the game
Rather than syncing and storing player positions we sync and store directions and we only store when a player direction changes as well as the timestamp for it
That way we can implement rollback netcode and interpolate the player movements between the input direction changes
#### Client-side prediction

#### Bit quantization

#### Interest-managed tick rate


# Enemies
#### Parametrically defined enemy pathing
This will probably a pointless optimisation since players will be constantly moving so the parametric function will be constantly updating.


