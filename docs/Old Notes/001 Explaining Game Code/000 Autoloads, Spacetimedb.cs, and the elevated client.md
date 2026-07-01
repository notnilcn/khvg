# **Before you open the game**
[`project.godot`](<project.godot>) has the following scripts in the auto load list:
1. [Global.cs](<Global.cs>)
	- This script is an outdated artifact from back when this game was written in gdscript
2. [Network.cs](<Network.cs>)
	- This script is an outdated artifact from back when this game was written in gdscript
3. ==[BulletManager.cs](<BulletManager.cs>)==
	- ==This is supposed to draw bullets but it currently doesn't work==
4. [SpacetimeDBManager.cs](<SpacetimeDBManager.cs>)
	- This establishes a connection to the Spacetimedb server. 
		- [OnConnected](<SpacetimeDBManager.cs#^OnConnected>)
5. [StdbPlayerSync.cs](<StdbPlayerSync.cs>)
	- Reads the player positions table and draws
6. [StdbEnemySync.cs](<StdbEnemySync.cs>)
	- Reads the enemy positions table and draws
7. [StdbBulletSync.cs](<StdbBulletSync.cs>)
	- Reads the spawned bullets table and draws
8. [ElevatedClient.cs](<ElevatedClient.cs>)
	- Handles when the --elevated-client argument is passed into the launch arguments
---
# Running an Elevated client
1. The elevated client is ran by adding `--elevated-client` to the launch arguments.
	1. [`SpacetimeDBManager._Ready()`](<SpacetimeDBManager.cs#^--elevated-client>) reads `--elevated-client` from the command line args, generates a distinct auth token, and then attempts a connection to the spacetimedb server. The auth token that's generated isn't anything special yet, it's just there to identify that the elevated client that's joining is different from other clients that're on the same machine.
		1. After the auth token is generated, a connection is attempted to be made to the spacetimedb server
		2. The spacetimedb specific OnConnected method is connected to the [`OnConnected`](<SpacetimeDBManager.cs#^OnConnected>) function. Other things are also connected to other things but they aren't relevant to the elevated client.
	2. After the connection is made, the [`OnConnected`](<SpacetimeDBManager.cs#^OnConnected>) function is called via callback.
		1. This function does a bunch of things but the thing that's relevant is that it checks if `ElevatedClient.IsActive` is true and then [claims the elevated client spot](<SpacetimeDBManager.cs#^claim-elevated-client-spot>) by calling the [ClaimElevated](<Lib.cs#^ClaimElevated>) reducer. ==Don't ask me why `ElevatedClient.IsActive` is true because I don't know. It's lower in the autoload too so you'd assume `ElevatedClient.cs` runs afterwards but apparently not==
		2. I think the EmitSignal is just for godot because [StdbPlayerSync.cs](<StdbPlayerSync.cs>), [StdbEnemySync.cs](<StdbEnemySync.cs>), and [StdbBulletSync.cs](<StdbBulletSync.cs>) don't use them.
2. [`ElevatedClient._Ready()`](<ElevatedClient.cs#`_Ready()`>) checks if `--elevated-client` is in the launch arguments and if it is then it changes the scene to [`woods_map.tscn`](<woods_map.tscn>)
	- `spawner.cs` is part of this scene.
	1. [`Spawner.ready()`](<Spawner.cs#`_ready()`>) sets up the chat console and stuff
---
# Tables
These tables were defined when the spacetimedb server was created.
## [`PlayerAccount`](<Lib.cs#^PlayerAccount>)
Player account data.
## [`CharacterData`](<Lib.cs#^CharacterData>)
Character data.
Characters belong to players.
## [`PlayerInventory`](<Lib.cs#^PlayerInventory>)
Inventory data.
Right now the EquippedAccessories, BackpackSlots, and ConsumableSlots are Lists or strings. It'd probably be better to enumerate them and make them lists of integers.
## ==[`PlayerPosition`](<Lib.cs#^PlayerPosition>)
Player position data. Uses [dead-reckoning](<https://docs.backnd.com/en/sdk-docs/backend/match/note/dead-reckoning/>) to sync player positions.
The code for deciding ChunkX and ChunkY will need to be redone later on.==

==I might want to implement input buffering later on, so this table should take in a list of inputs rather than a single set of inputs.==
## [`ElevatedClient`](<Lib.cs#^ElevatedClient>)
Contains the identity of the elevated client. If there is no elevated client, then anyone client can call the reducers that spawn enemies.
## [`BulletPattern`](<Lib.cs#^BulletPattern>)
Bullet spawning data.
## [`EnemyState`](<Lib.cs#^EnemyState>)
Enemy position and pathing data.

# Reducers
These reducers were defined when the spacetimedb server was created.
## **[`ClientConnected and ClientDisconnected`](<Lib.cs#ClientConnected and ClientDisconnected>)**
##### [`ClientConnected`](<Lib.cs#^ClientConnected>)
##### [`ClientDisconnected`](<Lib.cs#^ClientDisconnected>)
## **[`Elevated client`](<Lib.cs#Elevated client>)**
##### [`ClaimElevated`](<Lib.cs#^ClaimElevated>)
## **[`Position and movement sync`](<Lib.cs#Position and movement sync>)**
##### [`ReportMovement`](<Lib.cs#^ReportMovement>)
## **[`Account`](<Lib.cs#Account>)**
##### [`SetUsername`](<Lib.cs#^SetUsername>)
## **[`Character`](<Lib.cs#Character>)**
##### [`SaveCharacterProgress`](<Lib.cs#^SaveCharacterProgress>)
## **[`Inventory`](<Lib.cs#Inventory>)**
##### [`UpdateInventory`](<Lib.cs#^UpdateInventory>)
## **[`Enemies`](<Lib.cs#Enemies>)**
##### [`SpawnEnemy`](<Lib.cs#^SpawnEnemy>)
##### [`UpdateEnemySnapshots`](<Lib.cs#^UpdateEnemySnapshots>)
##### [`UpdateEnemyTarget`](<Lib.cs#^UpdateEnemyTarget>)
##### [`Lib.cs`](<Lib.cs#^>)
##### [`Lib.cs`](<Lib.cs#^>)
## **[`Bullets`](<Lib.cs#Bullets>)**
##### [`FireBullet`](<Lib.cs#^FireBullet>)
##### [`DespawnBullet`](<Lib.cs#^DespawnBullet>)


