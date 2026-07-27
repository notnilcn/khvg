# Don't send individual player bullets, only send the enemy that the player is targeting
Every time a player toggles auto-fire they send their timestamp modulo (modulus is equal to server tick rate).

When players land hits on an enemy, they only send the enemy id.

