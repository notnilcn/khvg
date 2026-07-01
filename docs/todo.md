Passing whether an enemy is elite into the [`_spawn_single_enemy(is_elite)`](<spawner.gd#`_spawn_single_enemy(is_elite)`>) function seems wrong. Maybe you could have like a modifier array thing that is passed into a modifier function/class that modifies the enemy. E.g.
- have an elite modifier that adds a thing on top of the sprite and increases their hp and guarantees a drop. 
- have an immortal modifier that makes the enemy immortal.


# Area of Interest

